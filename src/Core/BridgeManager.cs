namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck.ClaudeConsolePlugin.Models;
    using Loupedeck.ClaudeConsolePlugin.Agents;
    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// Bridge Manager — Connects the Logitech Actions SDK plugin to Claude Code
    /// via file-based IPC under a private root in the system temp directory
    /// (owner-only 0700 dirs / 0600 files — see PrivateFiles).
    ///
    /// Reads:  sessions/ + activity/ (statusline + hook data, per Terminal tab)
    /// Types:  keystrokes into the TTY-verified Claude tab in Terminal.app
    /// </summary>
    public class BridgeManager
    {
        // The private IPC layout lives in IpcPaths (shared with SessionRegistry). Local aliases keep
        // the rest of this file readable. They are PROPERTIES, not static readonly fields: the root
        // is per-product and a product declares itself at load, so capturing a path at type-init
        // would freeze the default and quietly point a second console at the first one's tree.
        private static String TempDir => IpcPaths.TempDir;
        private static String SessionsDir => IpcPaths.SessionsDir;
        private static String ActivityDir => IpcPaths.ActivityDir;
        private static String VoiceDir => IpcPaths.VoiceDir;
        private static String StateFile => IpcPaths.SharedStateFile;
        private static String ActivityFile => IpcPaths.SharedActivityFile;

        // Voice capture IPC. Every path is passed to ClaudeVoiceHelper explicitly, so moving them
        // needs no helper rebuild (re-signing the helper would silently reset its Microphone grant).
        private static String VoiceStopFile => IpcPaths.VoiceStopFile;
        private static String VoiceTranscriptFile => IpcPaths.VoiceTranscriptFile;
        private static String VoiceWavFile => IpcPaths.VoiceWavFile;
        // Written by the helper INSTEAD of a transcript when dictation failed outright. Its whole
        // purpose is to make a failure distinguishable from silence — see the poll loop below.
        private static String VoiceErrorFile => VoiceTranscriptFile + ".error";

        /// <summary>
        /// Is the microphone running, and where is the result going? Owned here rather than by the
        /// keys, because three keys drive one microphone and one set of IPC files (#28).
        /// </summary>
        internal readonly VoiceCaptureState Voice = new VoiceCaptureState();

        // Runtime home shared with the voice helper: ~/.claude/claude-console/
        private static readonly String ClaudeConsoleHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "claude-console");
        private static readonly String VoiceHelperApp = Path.Combine(ClaudeConsoleHome, "ClaudeVoiceHelper.app");
        // Self-contained whisper-cli produced by tools/voice/bundle-whisper.sh (no Homebrew needed).
        private static readonly String WhisperBinDir = Path.Combine(ClaudeConsoleHome, "whisper-bin");
        private static readonly String BundledWhisperCli = Path.Combine(WhisperBinDir, "whisper-cli");

        // Live-status bridge — scripts + settings.json the plugin auto-wires (see EnsureBridgeAutoWired).
        private static readonly String ClaudeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        private static readonly String SettingsFile = Path.Combine(ClaudeDir, "settings.json");
        private static readonly String SettingsBackup = Path.Combine(ClaudeDir, "settings.json.claude-console.bak");
        private static readonly String ScriptsDir = Path.Combine(ClaudeConsoleHome, "scripts");
        private static readonly String StatuslineScript = Path.Combine(ScriptsDir, "statusline-handler.sh");
        private static readonly String ActivityScript = Path.Combine(ScriptsDir, "activity-hook.sh");
        // Recovery scripts the user needs precisely when the package is gone (#45). Same directory,
        // same refresh-on-load, but NOT behind the bridge opt-out: declining settings.json wiring
        // must not cost anyone the uninstall remedy.
        private static readonly String[] RecoveryScripts = { "uninstall-registration.sh", "repair-registration.sh", "uninstall.sh" };
        private static readonly String StatuslineChainFile = Path.Combine(ClaudeConsoleHome, "statusline-chain");
        private static readonly String BridgeOptOutFile = Path.Combine(ClaudeConsoleHome, "no-autowire");

        // Speech model — fetched on first use if absent (see EnsureVoiceModel). base.en ≈ 142 MB.
        private static readonly String VoiceModelFile = Path.Combine(ClaudeConsoleHome, "whisper", "ggml-base.en.bin");
        private const String VoiceModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin";
        private const String VoiceModelSha256 = "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002";
        private const Int64 VoiceModelSize = 147964211;

        // On-disk path of the plugin DLL — set by ClaudeConsolePlugin.Load from the SDK's
        // Plugin.AssemblyFilePath. Assembly.Location is EMPTY in the SDK's load context, so this is
        // how EnsureVoiceRuntimeInstalled locates the in-package voice payload (bin/voice/).
        /// <summary>
        /// Mirrored into PluginPaths on assignment so every "where am I installed" lookup — the
        /// voice payload AND the Windows helper executables — resolves from one place. See
        /// PluginPaths for why AppContext.BaseDirectory cannot be used here.
        /// </summary>
        public String PluginAssemblyFilePath
        {
            get => PluginPaths.PluginAssemblyFilePath;
            set => PluginPaths.PluginAssemblyFilePath = value;
        }

        private Timer _pollTimer;
        private ClaudeState _currentState;
        private ActivityState _activity;
        private String _activeTty;   // opaque id of the frontmost session (macOS: "ttys003"); null until known
        private String _pinnedTty;   // session PINNED by a session-key press; outranks _activeTty (see RoutingTty)
        private Int32 _pollTick;     // drives the ~1s cadence of the frontmost-tab check

        // The state file's last-seen raw text. Held so a poll can answer "did anything change?" by
        // comparing bytes instead of raising an event and letting every key repaint to find out (#27).
        private String _lastStateText;

        // Consecutive polls in which nothing changed and no session was live. Drives the cadence.
        private Int32 _quietPolls;

        // False once we have told the keys that the display target reports nothing; set again by the
        // next real state. Held so the announcement fires on the transition, not on every poll (#27).
        private Boolean _displayStateKnown = true;

        // Everything OS-specific lives behind this seam: session discovery, injection, focus, nav.
        // See IPlatformBridge — above it, neither AppleScript nor TTYs nor consoles are visible.
        // Not readonly: declaring the agent rebuilds it, because the product declares itself after
        // this singleton already exists. See the Agent setter.
        private IPlatformBridge _platform;

        public event Action<ClaudeState> OnStateChanged;

        /// <summary>
        /// The session the display keys describe has reported nothing at all — a tab whose Claude
        /// has not yet run a turn, or one started before the status-line bridge was wired. The keys
        /// show a dash: a plausible number belonging to a DIFFERENT session is worse than no number.
        /// </summary>
        public event Action OnStateUnavailable;
        public event Action<ActivityState> OnActivityChanged;

        /// <summary>
        /// A dictation failed: which key's capture it was, and the words that key should show (#18).
        /// Raised from whichever thread learns of the failure — the keys repaint from timer threads
        /// already, so that is safe — and always AFTER the beep, so sound and face agree.
        /// </summary>
        internal event Action<VoiceIntent, String> OnVoiceFailed;

        // The single exit for a dictation that did not produce text: log the detail, beep, and put
        // the user-facing words on the key that was pressed. Before this, three of the four ways a
        // capture could end badly ended in a log line and nothing else — and the fourth (a denied
        // microphone) did not even reach the log (#18).
        private void ReportVoiceFailure(VoiceIntent intent, String keyText, String detail)
        {
            PluginLog.Warning($"BridgeManager: voice {keyText} — {detail}");
            _platform.Alert();
            try { OnVoiceFailed?.Invoke(intent, keyText); }
            catch (Exception ex) { PluginLog.Warning(ex, "BridgeManager: OnVoiceFailed handler failed"); }
        }

        /// <summary>The session grid — one row per live Claude Code session. See SessionRegistry.</summary>
        /// <remarks>Settable internally so tests can inject a registry rooted in a temp directory.</remarks>
        public SessionRegistry Grid { get; internal set; } = new SessionRegistry();

        public ClaudeState CurrentState => _currentState;
        public ActivityState CurrentActivity => _activity;

        // Test seam: lets the unit tests stand in a known target tab instead of shelling out to
        // osascript to discover the frontmost one. Assigning ActiveTty is exactly what the
        // frontmost-tab probe does, so it is also how the tests simulate a poll.
        internal String ActiveTty { get => _activeTty; set => _activeTty = value; }

        /// <summary>The active OS backend. Internal so tests can substitute a fake.</summary>
        internal IPlatformBridge Platform => _platform;

        /// <summary>
        /// Which agent the keys are driving. Actions read this to ask for the agent's own word for
        /// a verb, and to decide whether a key should exist at all — a Cost key on an agent that
        /// reports no cost hides rather than rendering a zero.
        ///
        /// Defaults to NoAgentAdapter, never to a real agent: Core naming one would compile that
        /// adapter into every product, and a product that forgot to declare itself would silently
        /// type another agent's slash commands at whatever was actually running. Each product
        /// assigns its own in its plugin CONSTRUCTOR — the SDK builds actions before Load(), and an
        /// action decides then which keys to add.
        /// </summary>
        internal IAgentAdapter Agent
        {
            get => this._agent;
            set
            {
                this._agent = value ?? new NoAgentAdapter();

                // REBUILD the platform bridge. It is constructed before the product declares its
                // agent — the SDK builds this singleton on first touch — so a bridge made at
                // construction time carries the DEFAULT matcher, and a Codex console would happily
                // discover `claude` processes and show them on its grid. Found on hardware, not in
                // a unit test: every piece was correct in isolation and never wired together.
                if (!this._platformInjected)
                {
                    this._platform = PlatformBridgeFactory.Create(
                        this._agent.ProcessMatcher, this._agent.CliCommand);
                }

                // The grid reads state files through the agent too. Setting one without the other
                // is the same wiring gap in a second place: discovery would find the right tabs and
                // then fail to understand a word they said.
                this.Grid.Agent = this._agent;
            }
        }

        private IAgentAdapter _agent = new NoAgentAdapter();

        // True when a test supplied its own bridge; declaring an agent must not replace it.
        private Boolean _platformInjected;

        // Test seams for the macOS backend, forwarded so the existing mac tests can keep driving
        // the manager directly. No-ops when the backend isn't the mac one.
        internal Func<List<String>, Int32, Boolean, String> OsascriptRunner
        {
            get => (_platform as MacPlatformBridge)?.OsascriptRunner;
            set { if (_platform is MacPlatformBridge m) { m.OsascriptRunner = value; } }
        }

        /// <summary>
        /// Test seam for discovery, per platform: a `ps` table on macOS, a process list on
        /// Windows. Both feed the SAME decision code — which agent's processes count — so a
        /// wiring test can drive whichever bridge the host actually built and assert one answer.
        /// </summary>
        internal Func<IEnumerable<WindowsProcessInfo>> ProcessEnumerator
        {
            get => (_platform as WindowsPlatformBridge)?.ProcessEnumerator;
            set { if (_platform is WindowsPlatformBridge w) { w.ProcessEnumerator = value; } }
        }

        internal Func<String> PsRunner
        {
            get => (_platform as MacPlatformBridge)?.PsRunner;
            set { if (_platform is MacPlatformBridge m) { m.PsRunner = value; } }
        }

        /// <summary>Kept as a forwarder so the TTY-normalization tests keep their entry point.</summary>
        internal static String NormalizeTty(String raw) => MacPlatformBridge.NormalizeTty(raw);

        public BridgeManager()
            : this(PlatformBridgeFactory.Create(), injected: false)
        {
        }

        /// <summary>Test/DI constructor — inject a fake or a specific platform backend.</summary>
        internal BridgeManager(IPlatformBridge platform)
            : this(platform, injected: true)
        {
        }

        // `injected` says whether the CALLER chose this bridge. The public constructor builds a
        // default one before any agent is known, and that must be replaceable — chaining the two
        // constructors without this distinction marked every bridge as injected and silently
        // disabled the rebuild, which is exactly how a Codex keypad kept showing Claude sessions.
        private BridgeManager(IPlatformBridge platform, Boolean injected)
        {
            this._platform = platform ?? new UnsupportedPlatformBridge();
            this._platformInjected = injected;
        }

        // Test seam: the pinned session, so a test can assert the pin was set/released without
        // reaching through RoutingTty's fallbacks.
        internal String PinnedTty => _pinnedTty;

        // ------------------------------------------------------------------------------------------
        // Singleton — the SDK auto-discovers PluginDynamicCommand/Adjustment subclasses and
        // instantiates them with their parameterless constructors, so they cannot receive the
        // bridge by constructor injection. They pull the shared instance from here instead.
        // Lazy + locked so it is safe regardless of whether a command ctor or Plugin.Load()
        // touches it first.
        // ------------------------------------------------------------------------------------------
        private static readonly Object _instanceLock = new Object();
        private static BridgeManager _instance;

        public static BridgeManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        _instance ??= new BridgeManager();
                    }
                }
                return _instance;
            }
        }

        public void StartPolling()
        {
            EnsureIpcRoot();
            CleanupLegacyIpcFiles();
            Grid.LoadPersisted();   // keep slot assignments across a plugin reload
            _pinnedTty = Grid.FocusedSession;   // ...and the session you had selected

            // One-shot timer, re-armed at the END of each PollState (see its finally). This makes
            // polls NON-OVERLAPPING: the next poll can't start until the previous one finishes, so a
            // slow poll (osascript) can never pile callbacks onto the thread pool. An auto-repeating
            // Timer(..., 0, 500) does NOT serialize callbacks, and that pile-up leaked threads until
            // LogiPluginService hit the 4096-thread limit and aborted.
            _pollTimer = new Timer(PollState, null, 0, Timeout.Infinite);
            PluginLog.Info("BridgeManager: Started polling state file (~500ms, non-overlapping)");
        }

        public void StopPolling()
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
            PluginLog.Info("BridgeManager: Stopped polling");
        }

        private void PollState(Object timerState)
        {
            try
            {
                // ~Every 2s, refresh which Terminal tab is frontmost so the live keys follow the
                // session you're actually looking at. Keep the last known tab when Terminal isn't
                // frontmost, so glancing away (e.g. to a browser) doesn't reset the display. The
                // frontmost-tab probe shells out to osascript, so we do it every 4th poll (not every
                // poll) to keep that comparatively expensive call off the hot path.
                if (_pollTick++ % 4 == 0)
                {
                    var tty = _platform.QueryFrontmostSession();
                    if (!String.IsNullOrEmpty(tty))
                    {
                        _activeTty = tty;
                    }
                }

                // Refresh the session grid. The `ps` scan runs on a DIFFERENT tick from the osascript
                // frontmost probe above (2 vs 0) so the two subprocess calls never share a poll —
                // stacking expensive calls on one tick is how the 1.3.1 thread leak began.
                // While backed off (below) the poll itself is rare, so scan every time instead: at a
                // 5 s cadence, every 4th poll would mean 20 s before a new session appeared.
                var liveTtys = _pollTick % 4 == 2 || _quietPolls >= QuietPollsBeforeSlow
                    ? _platform.DiscoverSessions()
                    : null;
                Grid.Refresh(liveTtys);

                // Where the agent cannot push state to us, pull it. Only Windows/Codex sets this
                // (its hook runner spawns nothing there), and the bridge writes the very same IPC
                // files a hook would — so everything below this line is identical either way.
                this.PullState?.Invoke();

                // Compare the file's BYTES before doing anything with them. This event used to fire on
                // every single poll for as long as the state file existed — twice a second, forever,
                // whether or not one character had changed — and each subscriber then repainted its
                // key. That is the redraw storm (#27): ~11 renders a second, each a full render plus
                // an IPC push, continuing when no session was running and the keys were not even on
                // screen. Byte equality is exact here because one writer rewrites the whole file.
                var statePath = this.ActiveStateFile();
                if (statePath == null)
                {
                    // Nothing reported for the session on the display keys. Announce it ONCE, so the
                    // keys can show a dash instead of another session's numbers, and forget the last
                    // text so the next real state always re-fires even if it is byte-identical.
                    if (_displayStateKnown)
                    {
                        _displayStateKnown = false;
                        _lastStateText = null;
                        OnStateUnavailable?.Invoke();
                    }
                }

                var stateText = statePath == null ? null : ReadTextWithRetry(statePath);
                var stateChanged = stateText != null
                    && !String.Equals(stateText, _lastStateText, StringComparison.Ordinal);

                if (stateChanged)
                {
                    var newState = Deserialize<ClaudeState>(stateText);
                    if (newState != null)
                    {
                        _lastStateText = stateText;
                        _currentState = newState;
                        _displayStateKnown = true;
                        OnStateChanged?.Invoke(_currentState);
                    }
                }

                // Activity is pushed by the Claude Code hooks into a separate file; surface changes
                // so the Status key can flip between working / waiting / idle — for the active tab.
                var act = ReadActivity();
                var activityChanged = act?.State != _activity?.State;
                if (activityChanged)
                {
                    _activity = act;
                    OnActivityChanged?.Invoke(_activity);
                }

                // Nothing to watch, and nothing moved: earn a slower cadence. With no live session
                // the only event that can occur is one APPEARING, which the grid scan above still
                // catches every poll. Any change at all, or any session existing, snaps straight back
                // to the fast cadence — so this can never slow down a keypad you are actually using.
                _quietPolls = NextQuietCount(
                    _quietPolls,
                    anythingChanged: stateChanged || activityChanged,
                    anyLiveSession: Grid.LiveSessions().Any());

                // Every 120 polls, prune per-tab files from dead sessions so closed tabs don't
                // accumulate state on disk forever. That was a fixed ~60s when every poll was 500ms;
                // now it stretches with the cadence, which is the right way round — an idle machine
                // has nothing accumulating to prune.
                if (_pollTick % 120 == 1)
                {
                    PruneStaleIpcFiles();
                }
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, "BridgeManager: PollState error");
            }
            finally
            {
                // Re-arm the one-shot: the next poll fires AFTER this one returns, so polls never
                // overlap. Swallow ObjectDisposedException from a concurrent StopPolling.
                try { _pollTimer?.Change(this.NextPollDelayMs(), Timeout.Infinite); }
                catch (ObjectDisposedException) { /* stopped */ }
            }
        }

        // Cadence. Fast whenever anything is happening; an idle machine with no session running has
        // nothing to render and should not cost a laptop 8% of a core indefinitely (#27).
        private const Int32 PollFastMs = 500;
        private const Int32 PollSlowMs = 2000;
        private const Int32 PollIdleMs = 5000;
        private const Int32 QuietPollsBeforeSlow = 20;    // ~10 s of nothing
        private const Int32 QuietPollsBeforeIdle = 60;    // ~2 min of nothing

        private Int32 NextPollDelayMs() => PollDelayForQuietCount(_quietPolls);

        /// <summary>
        /// How long to wait before the next poll, given how many consecutive polls found nothing.
        /// </summary>
        internal static Int32 PollDelayForQuietCount(Int32 quietPolls) =>
            quietPolls >= QuietPollsBeforeIdle ? PollIdleMs
            : quietPolls >= QuietPollsBeforeSlow ? PollSlowMs
            : PollFastMs;

        /// <summary>
        /// The quiet-poll counter. A quiet poll is one where nothing changed AND no session was
        /// live — the only condition under which slowing down is safe, because the sole event that
        /// can still occur is a session appearing, which the grid scan catches on every poll.
        /// Anything else resets to zero, so an active keypad always runs at the fast cadence.
        /// </summary>
        internal static Int32 NextQuietCount(Int32 quietPolls, Boolean anythingChanged, Boolean anyLiveSession)
        {
            if (anythingChanged || anyLiveSession)
            {
                return 0;
            }

            // Saturate rather than overflow: a machine left alone all weekend must not wrap round to
            // a negative count and silently return to polling twice a second.
            return quietPolls >= QuietPollsBeforeIdle ? QuietPollsBeforeIdle : quietPolls + 1;
        }

        // Read the hook-written activity flag (busy/waiting/done). A "busy" whose transcript has
        // gone quiet is treated as done, so an INTERRUPTED turn — which fires no hook at all — can't
        // leave the key stuck on "Working" (#30).
        //
        // This used to expire on a bare 300s literal while SessionRegistry used 45s for the same
        // question, so the Status key and the session-slot keys disagreed about the same session.
        // QA's stuck session sat at 114s: past 45s, nowhere near 300s. Both now ask ActivityStall.
        //
        // The transcript path is taken from the ROUTING session, not from CurrentState: that is the
        // session whose activity file was just read, and with a pin set DisplayTty() is a DIFFERENT
        // session (#25). Pairing one session's activity with another's transcript would decide the
        // hourglass from a tab nobody was asking about.
        private ActivityState ReadActivity()
        {
            var file = ActiveActivityFile();
            if (!File.Exists(file))
            {
                return null;
            }

            var a = ReadJsonWithRetry<ActivityState>(file);
            if (a == null)
            {
                return null;
            }

            var routing = this.RoutingTty();
            var transcript = !String.IsNullOrEmpty(routing)
                && this.Grid.Sessions.TryGetValue(routing, out var routed)
                    ? routed.TranscriptPath
                    : null;

            if (ActivityStall.IsStalledBusy(
                    a.State,
                    a.Ts,
                    ActivityStall.TranscriptMtime(transcript),
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    this.Grid.InterruptedAt(routing)))
            {
                a.State = "done";
            }

            return a;
        }

        // ------------------------------------------------------------------------------------------
        // Per-tab session routing — each Claude Code session (one per Terminal tab) writes a file
        // keyed by its tab's TTY (the bash scripts derive it via `ps -o tty`); the plugin shows
        // whichever tab is frontmost. Falls back to the shared file (last writer) when the active
        // tab has no per-TTY file yet, or when Terminal isn't the frontmost app.
        // ------------------------------------------------------------------------------------------
        // Cost / Model / Context read this one: it follows the tab you are looking at (#25).
        private String ActiveStateFile() => PerTty(SessionsDir, StateFile, this.DisplayTty());

        // Activity follows the ROUTING target: "waiting" here is the approval the Yes key answers,
        // so the Activity face and the key that acts on it must describe the same session.
        private String ActiveActivityFile() => PerTty(ActivityDir, ActivityFile, this.RoutingTty());

        private String PerTty(String dir, String shared, String tty)
        {
            if (!String.IsNullOrEmpty(tty))
            {
                var p = Path.Combine(dir, tty + ".json");

                // Known session, no file: it has reported NOTHING. Falling back to the shared file
                // here would paint the last writer's cost onto a key describing this session —
                // "a key must never show a value the agent did not report" (#49).
                return File.Exists(p) ? p : null;
            }

            // Target unknown (Terminal not frontmost, tty detection failed): the shared file, last
            // writer, is the best guess available and is what single-session users have always seen.
            return shared;
        }

        // Create the private IPC tree (0700). The bash scripts also mkdir it (whichever runs
        // first wins), so both sides must agree on the layout.
        private static void EnsureIpcRoot()
        {
            try
            {
                IpcPaths.EnsureAll();
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "BridgeManager: could not secure the IPC root");
            }
        }

        // Pre-1.4 builds wrote world-readable loose files (/tmp/claude-console-*.json, cmd-queue,
        // voice transcript). Clear them once on load; the glob can't match the new root dir.
        private static void CleanupLegacyIpcFiles()
        {
            try
            {
                foreach (var f in Directory.GetFiles(TempDir, "claude-console-*"))
                {
                    TryDelete(f);
                }
            }
            catch { /* best effort */ }
        }

        // Delete per-tab/voice files not written for 10+ minutes. A live session's statusline
        // refreshes on every assistant message, so anything this old belongs to a dead tab.
        private static readonly TimeSpan StaleIpcAge = TimeSpan.FromMinutes(10);

        // The prune exists to clean up CLOSED tabs. It could not tell a closed tab from a quiet
        // one, so a session you had not typed in for ten minutes had its state file deleted out
        // from under it — and then the display fell back to shared.json (the last writer), putting
        // ANOTHER session's cost on the key, while the grid recreated the session as provisional
        // and its key lost the project name for the agent's name (#49). Live sessions are now
        // exempt, however long they have been idle: the grid drops a session as soon as its process
        // is gone, and only then does its file become prunable.
        private void PruneStaleIpcFiles() =>
            PruneStaleFiles(
                new[] { SessionsDir, ActivityDir, VoiceDir },
                DateTime.UtcNow - StaleIpcAge,
                Grid.Sessions.Keys);

        // Split out so the tests can drive it against a temp root and a controlled cutoff.
        internal static void PruneStaleFiles(IEnumerable<String> dirs, DateTime cutoff, IEnumerable<String> keepKeys = null)
        {
            // Names are "<tty>.json"; a live session's file is never stale, whatever its mtime says.
            var keep = new HashSet<String>(keepKeys ?? Enumerable.Empty<String>(), StringComparer.Ordinal);

            foreach (var dir in dirs)
            {
                try
                {
                    if (!Directory.Exists(dir))
                    {
                        continue;
                    }
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        try
                        {
                            if (keep.Contains(Path.GetFileNameWithoutExtension(f)))
                            {
                                continue;
                            }

                            if (File.GetLastWriteTimeUtc(f) < cutoff)
                            {
                                File.Delete(f);
                            }
                        }
                        catch { /* best effort */ }
                    }
                }
                catch { /* best effort */ }
            }
        }

        // ------------------------------------------------------------------------------------------
        // Targeting. TWO questions, and conflating them is #25.
        //
        //   RoutingTty  — "where does this key ACT?"     Pin first. Injection, the answer keys, and
        //                 every badge that describes an action (amber approval, slot highlight).
        //   DisplayTty  — "what am I LOOKING at?"        The frontmost tab first. Cost, Model,
        //                 Context — read-only keys that should follow your eyes.
        //
        // One resolver used to answer both, so pressing a session key froze the display on the
        // pinned session: with one session frontmost, Cost showed the OTHER session's total and
        // never moved again. The pin is right for routing — acting on session 2 while looking at
        // session 1 is the entire point of the grid — and wrong for a cost readout.
        //
        // The badge rule matters and is not negotiable: a key's badge must describe the session
        // that key will act on. An amber Yes that describes the session you are watching, while
        // Yes answers a different one, is precisely the ambiguity Logitech asked about.
        // ------------------------------------------------------------------------------------------
        internal String RoutingTty()
        {
            var live = Grid.LiveSessions();

            // 0. An explicit session-key press wins over everything below, and keeps winning. This
            //    is the entire point of the grid: act on session 2 while you are looking at session
            //    1, or at a browser. It must NOT be a field the frontmost-tab probe can overwrite —
            //    that made a selection decay within ~2.5s, and let a probe already in flight when
            //    you pressed silently revert the press. Released only by pinning another session,
            //    or automatically when this one exits so the keys are never stranded on a dead tab.
            var pinned = _pinnedTty;
            if (!String.IsNullOrEmpty(pinned))
            {
                if (live.Any(s => s.SessionKey == pinned))
                {
                    return pinned;
                }
                this.ClearPin();
            }

            // 1. The tracked tab, when it is a session we know about.
            var active = _activeTty;
            if (!String.IsNullOrEmpty(active) && live.Any(s => s.SessionKey == active))
            {
                return active;
            }

            // 2. Exactly one session waiting on you — the obvious thing to answer.
            var waiting = live.Where(s => s.State == "waiting").ToList();
            if (waiting.Count == 1)
            {
                return waiting[0].SessionKey;
            }

            // 3. Exactly one session at all.
            if (live.Count == 1)
            {
                return live[0].SessionKey;
            }

            // 4. Ambiguous (or nothing running): fall back to the tracked tab, which may be null —
            //    the injection guard then beeps rather than typing somewhere unintended.
            return active;
        }

        /// <summary>
        /// The session whose numbers should be on the display keys: the tab you are looking at.
        ///
        /// Falls back to <see cref="RoutingTty"/> when the frontmost tab is unknown, and that
        /// fallback is what makes this correct on Windows without a platform branch — Windows
        /// Terminal exposes no way to ask which tab is in front, so `_activeTty` is null there and
        /// the display follows the pin, which is the only targeting Windows has.
        /// </summary>
        internal String DisplayTty()
        {
            var active = _activeTty;
            if (!String.IsNullOrEmpty(active) && Grid.LiveSessions().Any(s => s.SessionKey == active))
            {
                return active;
            }

            return this.RoutingTty();
        }

        /// <summary>
        /// Press a session key: focus that tab and make it the target for every subsequent key,
        /// until you pin a different session, press this one again, or it exits. The pin is what
        /// makes "press slot 2, then Clear" land in slot 2 even minutes later, and even if you have
        /// since switched Terminal back to another tab.
        /// </summary>
        public void SelectSlot(Int32 slot)
        {
            var session = Grid.SlotSession(slot);
            if (session == null)
            {
                return;
            }

            // Pressing the slot that is ALREADY pinned releases it, and the keys go back to
            // following the frontmost tab. Until now a pin could only be MOVED, never dropped —
            // QA's actual complaint in #25 — and the only ways out were pinning a different session
            // or closing the one you had pinned. A second press is what a user tries first, and the
            // tab is focused either way, so the gesture still reads as "take me to this session".
            if (_pinnedTty == session.SessionKey)
            {
                this.ClearPin();
                _activeTty = session.SessionKey;
                _platform.FocusSession(session.SessionKey);
                PluginLog.Info($"BridgeManager: unpinned slot {slot} ({session.Project}) — keys follow the frontmost tab again");
                return;
            }

            _pinnedTty = session.SessionKey;
            Grid.FocusedSession = session.SessionKey;   // survives a plugin reload, like the slot assignments
            _activeTty = session.SessionKey;            // so a later un-pin falls back somewhere sensible
            _platform.FocusSession(session.SessionKey);
            PluginLog.Info($"BridgeManager: pinned slot {slot} -> {session.SessionKey} ({session.Project})");
        }

        // Drop the pin and go back to following the frontmost tab. Called when the pinned session
        // exits, and when we deliberately start a session somewhere else (voice "go to project").
        private void ClearPin()
        {
            if (_pinnedTty == null)
            {
                return;
            }

            PluginLog.Info($"BridgeManager: released the pin on {_pinnedTty} — keys follow the frontmost tab again");
            _pinnedTty = null;
            Grid.FocusedSession = null;
        }

        /// <summary>
        /// Read a JSON file with retry logic to handle race conditions from concurrent writes.
        /// </summary>
        private T ReadJsonWithRetry<T>(String filePath, Int32 maxAttempts = 3, Int32 backoffMs = 10) where T : class
        {
            var json = ReadTextWithRetry(filePath, maxAttempts, backoffMs);
            return json == null ? null : Deserialize<T>(json);
        }

        /// <summary>
        /// The raw text of an IPC file, with the same size cap and retry-on-torn-write behaviour as
        /// <see cref="ReadJsonWithRetry{T}"/>. Split out so a caller can ask "did this file change?"
        /// by comparing bytes, which is both exact and cheaper than deserialising to find out (#27).
        /// </summary>
        private static String ReadTextWithRetry(String filePath, Int32 maxAttempts = 3, Int32 backoffMs = 10)
        {
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    var fi = new FileInfo(filePath);
                    if (!fi.Exists)
                    {
                        return null;
                    }
                    if (fi.Length > MaxIpcFileBytes)
                    {
                        // State/activity files are a few KB; anything huge is corrupt or hostile.
                        PluginLog.Warning($"BridgeManager: {filePath} is {fi.Length} bytes (cap {MaxIpcFileBytes}) — ignoring");
                        return null;
                    }

                    var text = File.ReadAllText(filePath);
                    if (String.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    return text;
                }
                catch
                {
                    if (attempt < maxAttempts - 1)
                    {
                        Thread.Sleep(backoffMs * (attempt + 1));
                    }
                }
            }

            return null;
        }

        private static T Deserialize<T>(String json) where T : class
        {
            try { return JsonSerializer.Deserialize<T>(json); }
            catch { return null; }
        }

        /// <summary>
        /// Type a prompt into the tracked Claude tab and press Return.
        /// </summary>
        public void SendPrompt(String prompt) => InjectText(prompt, pressEnter: true);

        // ------------------------------------------------------------------------------------------
        // Guarded keystroke injection. The guarantee — focus the tracked session and type in ONE
        // indivisible operation, or type nothing at all — is the backend's to keep; see
        // IPlatformBridge. What lives here is only the platform-neutral half: resolve WHICH session
        // the keys act on (RoutingTty), then hand the request across the seam.
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Type text into the tracked Claude session and optionally press Return.
        /// </summary>
        public void InjectText(String text, Boolean pressEnter) =>
            _platform.InjectText(RoutingTty(), text, pressEnter);

        /// <summary>
        /// Send a single key chord to the tracked Claude session, e.g. Shift+Tab to cycle modes.
        /// </summary>
        public void InjectKey(KeyStroke key) => _platform.InjectKey(RoutingTty(), key);

        /// <summary>
        /// Accept the highlighted autocomplete AND submit it in one press.
        /// </summary>
        public void InjectTabThenEnter() => _platform.InjectTabThenEnter(RoutingTty());

        /// <summary>
        /// Optional per-poll pull of agent state, for a product whose agent cannot push it.
        /// Set by the product at load (Windows/Codex → CodexRolloutBridge.Poll); null everywhere
        /// else, where lifecycle hooks push state as it happens. Whatever it writes goes to the
        /// same IPC files, so nothing downstream knows which transport filled them.
        /// </summary>
        public Action PullState { get; set; }

        /// <summary>Drive a terminal navigation gesture (new tab, cycle windows, …).</summary>
        public void Navigate(TerminalAction action) => _platform.Navigate(action);

        /// <summary>
        /// Interactive screenshot into this product's IPC tree. Returns the file's path, or null
        /// when the user cancelled the picker (or the platform can't capture). Timestamped names,
        /// never reused: an earlier shot may still be sitting unsubmitted in a composer, and
        /// overwriting it would silently swap the image that prompt refers to.
        /// </summary>
        public String CaptureScreenshot()
        {
            var dir = IpcPaths.ScreenshotsDir;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"shot-{DateTime.Now:yyyyMMdd-HHmmss}.png");

            return _platform.CaptureScreenshotInteractive(path) ? path : null;
        }

        /// <summary>Open a terminal and start the agent with extra CLI args (e.g. -i shot.png).</summary>
        public void LaunchAgentSession(params String[] extraArgs) => _platform.LaunchAgentSession(extraArgs);

        // State/activity files are a few KB — refuse to slurp anything huge (corrupt or hostile).
        private const Int64 MaxIpcFileBytes = 1 << 20;

        // ------------------------------------------------------------------------------------------
        // Voice capture — offline dictation via the bundled ClaudeVoiceHelper.app (whisper.cpp).
        // The helper is a separate signed app bundle so it can hold its OWN Microphone TCC grant;
        // LogiPluginService (a background daemon) cannot get mic access itself. Flow:
        //   press 1 -> StartVoiceCapture launches the helper (records 16kHz WAV, polls the stop flag)
        //   press 2 -> StopVoiceCapture writes the stop flag; the helper transcribes -> writes the
        //              transcript; a background thread reads it and types it into the focused terminal.
        // ------------------------------------------------------------------------------------------
        /// <summary>
        /// The platforms with a working capture backend: the signed helper app on macOS, the
        /// WinMM helper exe on Windows. Testable for the same reason as AutoWireSupported — a
        /// silent platform gate is how Phase 4 shipped unreachable.
        /// </summary>
        internal static Boolean VoiceSupported =>
            OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

        /// <summary>
        /// Launch the recorder. Returns false when nothing was started, so the caller can clear the
        /// in-flight state rather than leaving the voice keys believing a capture is running (#28).
        /// </summary>
        public Boolean StartVoiceCapture()
        {
            if (OperatingSystem.IsWindows())
            {
                return this.StartVoiceCaptureWindows();
            }

            if (!OperatingSystem.IsMacOS())
            {
                PluginLog.Info("BridgeManager.StartVoiceCapture: unsupported platform");
                return false;
            }

            // Install the helper + whisper from the plugin package if this is a package-only install
            // (no-op for dev builds, where tools/voice/build.sh already placed them in the runtime home).
            EnsureVoiceRuntimeInstalled();

            // Clear any stale transcript/flag so we never type a previous result.
            EnsureIpcRoot();
            TryDelete(VoiceTranscriptFile);
            TryDelete(VoiceErrorFile);
            TryDelete(VoiceStopFile);

            // Voice.Press has already recorded the pressed key's intent by the time we are here, so
            // a failure to START can be shown on the right key too.
            if (!Directory.Exists(VoiceHelperApp))
            {
                this.ReportVoiceFailure(Voice.Intent, VoiceFailure.NoHelper,
                    $"helper missing at {VoiceHelperApp} (run tools/voice/build.sh, or reinstall the plugin)");
                return false;
            }

            // Make sure the speech model is present. If it's still downloading, skip this capture
            // and say so, rather than record audio the helper can't transcribe yet.
            if (!EnsureVoiceModel())
            {
                this.ReportVoiceFailure(Voice.Intent, VoiceFailure.ModelLoading,
                    "speech model not ready (downloading) — try again shortly");
                return false;
            }

            // Launch via LaunchServices (open) so the helper is its own TCC subject. Detached.
            var args = new List<String>
            {
                VoiceHelperApp, "--args",
                "--maxsec", "60",
                "--out", VoiceWavFile,
                "--stopflag", VoiceStopFile,
                "--transcript", VoiceTranscriptFile,
                "--model", VoiceModelFile,
            };
            // Point the helper at the self-contained whisper-cli when we've bundled it.
            if (File.Exists(BundledWhisperCli))
            {
                args.Add("--whisper");
                args.Add(BundledWhisperCli);
            }
            RunDetached("open", args);
            PluginLog.Info("BridgeManager.StartVoiceCapture: helper launched");
            return true;
        }

        // Windows whisper-cli lives in the same runtime-home dir as the macOS bundle, with the
        // platform's suffix. Installed by EnsureVoiceRuntimeInstalled from the package (or by
        // hand from a whisper.cpp release during development).
        private static readonly String WindowsWhisperCli = Path.Combine(WhisperBinDir, "whisper-cli.exe");

        // The same flow as macOS with the platform differences flattened out: the helper is a
        // plain exe launched directly (no LaunchServices, no TCC — Windows mic permission is a
        // Settings toggle the helper documents), and whisper is REQUIRED up front — the Windows
        // helper has no embedded fallback, so recording without it would always type nothing.
        private Boolean StartVoiceCaptureWindows()
        {
            EnsureVoiceRuntimeInstalled();

            EnsureIpcRoot();
            TryDelete(VoiceTranscriptFile);
            TryDelete(VoiceErrorFile);
            TryDelete(VoiceStopFile);

            var helper = PluginPaths.PackagedFile("claude-console-voice.exe");
            if (helper == null)
            {
                PluginLog.Warning("BridgeManager.StartVoiceCapture: claude-console-voice.exe not found in the plugin package");
                return false;
            }

            if (!File.Exists(WindowsWhisperCli))
            {
                PluginLog.Warning($"BridgeManager.StartVoiceCapture: whisper-cli.exe missing at {WhisperBinDir} — voice needs the whisper bundle installed");
                _platform.Alert();
                return false;
            }

            if (!EnsureVoiceModel())
            {
                PluginLog.Info("BridgeManager.StartVoiceCapture: speech model not ready (downloading) — try again shortly");
                _platform.Alert();
                return false;
            }

            RunDetached(helper, new List<String>
            {
                "--maxsec", "60",
                "--out", VoiceWavFile,
                "--stopflag", VoiceStopFile,
                "--transcript", VoiceTranscriptFile,
                "--model", VoiceModelFile,
                "--whisper", WindowsWhisperCli,
            });
            PluginLog.Info("BridgeManager.StartVoiceCapture: helper launched");
            return true;
        }

        // ------------------------------------------------------------------------------------------
        // Package bootstrap — when the helper + whisper ship INSIDE the .lplug4 (release builds), copy
        // them into the runtime home on first use so a package-only install has working voice. Files
        // unpacked from a downloaded .lplug4 carry com.apple.quarantine, so strip it after copying.
        // For dev builds the package has no voice/ payload and the runtime files already exist — no-op.
        // ------------------------------------------------------------------------------------------
        private void EnsureVoiceRuntimeInstalled()
        {
            if (OperatingSystem.IsWindows())
            {
                this.EnsureVoiceRuntimeInstalledWindows();
                return;
            }

            try
            {
                // The SDK loads the plugin in a context where Assembly.Location is empty, so use the
                // path the plugin captured from Plugin.AssemblyFilePath; fall back to Location.
                var asmPath = PluginAssemblyFilePath;
                if (String.IsNullOrEmpty(asmPath))
                {
                    asmPath = typeof(BridgeManager).Assembly.Location;
                }
                var pkgDir = String.IsNullOrEmpty(asmPath) ? null : Path.GetDirectoryName(asmPath);
                if (String.IsNullOrEmpty(pkgDir))
                {
                    PluginLog.Info("BridgeManager.EnsureVoiceRuntimeInstalled: plugin dir unknown — skipping");
                    return;
                }
                var pkgVoice = Path.Combine(pkgDir, "voice");
                PluginLog.Verbose($"BridgeManager.EnsureVoiceRuntimeInstalled: pkgVoice={pkgVoice} exists={Directory.Exists(pkgVoice)}");

                // The guard compares the TREE, not the directory. `Directory.Exists` meant a runtime
                // copy was accepted forever once created: the 2.0.1 whisper bundle shipped without
                // its compute backends, and because ~/.claude/claude-console/ outlives an uninstall,
                // shipping corrected files would have repaired nobody who had ever pressed Voice —
                // their broken copy still "existed" (#24). Comparing every packaged file by size and
                // hash also repairs an install interrupted halfway.
                var pkgHelper = Path.Combine(pkgVoice, "ClaudeVoiceHelper.app");
                if (Directory.Exists(pkgHelper) && !RuntimeTreeMatchesPackage(pkgHelper, VoiceHelperApp))
                {
                    PluginLog.Info($"BridgeManager: installing voice helper from package -> {VoiceHelperApp}");
                    Directory.CreateDirectory(ClaudeConsoleHome);
                    RunSync("/usr/bin/ditto", pkgHelper, VoiceHelperApp);
                    RunSync("/usr/bin/xattr", "-dr", "com.apple.quarantine", VoiceHelperApp);
                }

                var pkgWhisper = Path.Combine(pkgVoice, "whisper-bin");
                if (Directory.Exists(pkgWhisper) && !RuntimeTreeMatchesPackage(pkgWhisper, WhisperBinDir))
                {
                    PluginLog.Info($"BridgeManager: installing whisper bundle from package -> {WhisperBinDir}");
                    RunSync("/usr/bin/ditto", pkgWhisper, WhisperBinDir);
                    RunSync("/usr/bin/xattr", "-dr", "com.apple.quarantine", WhisperBinDir);
                    if (!RuntimeTreeMatchesPackage(pkgWhisper, WhisperBinDir))
                    {
                        PluginLog.Warning($"BridgeManager: whisper bundle at {WhisperBinDir} still differs from the package after install");
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "BridgeManager.EnsureVoiceRuntimeInstalled failed");
            }
        }

        // The Windows analogue of the ditto branch above: a release package carries whisper's
        // Windows build under bin\voice\whisper-bin-win\ (its own directory — voice\whisper-bin
        // holds the macOS dylibs), and it is copied into the runtime home on first use. Plain
        // managed copy: no quarantine to strip, nothing to chmod. A dev machine where whisper-bin
        // was placed by hand is left alone.
        private void EnsureVoiceRuntimeInstalledWindows()
        {
            try
            {
                var pkgDir = PluginPaths.PluginDirectory;
                if (String.IsNullOrEmpty(pkgDir))
                {
                    return;
                }

                var pkgWhisper = Path.Combine(pkgDir, "voice", "whisper-bin-win");
                if (!Directory.Exists(pkgWhisper))
                {
                    return;   // dev build — nothing packaged; a hand-placed bundle is left alone
                }

                // Same rule as macOS: the presence of whisper-cli.exe says nothing about whether the
                // rest of the bundle is the one we shipped (#24).
                if (RuntimeTreeMatchesPackage(pkgWhisper, WhisperBinDir))
                {
                    return;
                }

                PluginLog.Info($"BridgeManager: installing whisper bundle from package -> {WhisperBinDir}");
                CopyTree(pkgWhisper, WhisperBinDir);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "BridgeManager.EnsureVoiceRuntimeInstalledWindows failed");
            }
        }

        private static void CopyTree(String from, String to)
        {
            Directory.CreateDirectory(to);
            foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
            }
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
            }
        }

        /// <summary>
        /// A runtime directory is healthy only when every packaged file is present and identical.
        /// Checking the tree rather than the directory itself is what repairs an existing broken
        /// install: a whisper bundle whose CLI is present but whose compute backends are missing
        /// looks installed to any existence test, and is exactly what shipped in 2.0.1 (#24).
        /// </summary>
        internal static Boolean RuntimeTreeMatchesPackage(String packageRoot, String runtimeRoot)
        {
            if (String.IsNullOrEmpty(packageRoot) || String.IsNullOrEmpty(runtimeRoot)
                || !Directory.Exists(packageRoot) || !Directory.Exists(runtimeRoot))
            {
                return false;
            }

            var packagedFiles = Directory.GetFiles(packageRoot, "*", SearchOption.AllDirectories);
            if (packagedFiles.Length == 0)
            {
                return false;
            }

            foreach (var source in packagedFiles)
            {
                var relative = Path.GetRelativePath(packageRoot, source);
                var installed = Path.Combine(runtimeRoot, relative);
                if (!File.Exists(installed)
                    || new FileInfo(source).Length != new FileInfo(installed).Length)
                {
                    return false;
                }

                // Size alone would pass a same-size corruption, and these are signed Mach-O files
                // where a re-sign changes content without changing length.
                if (!String.Equals(HashFileSha256(source), HashFileSha256(installed), StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        // Run a process and wait for it (ditto/xattr install steps must finish before launching).
        private static void RunSync(String file, params String[] args)
        {
            var psi = new ProcessStartInfo { FileName = file, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }
            using var p = Process.Start(psi);
            p.WaitForExit();
        }

        // ==========================================================================================
        // Live-status bridge — auto-install + auto-wire (zero user action)
        //
        // The live keys (Cost / Context / Model, Activity) read /tmp state files that only get
        // written when Claude Code is wired to push them: a `statusLine` handler feeds Cost/Context/
        // Model and four `hooks` feed Activity, all via ~/.claude/settings.json. A package-only user
        // never does this by hand, so those keys show defaults. To make them "just work", the plugin
        // ships the two scripts embedded in the DLL, writes them to ~/.claude/claude-console/scripts/
        // on first run, and merges the statusLine + hooks into settings.json itself.
        //
        // Safe by design: backs settings.json up once, MERGES rather than clobbers (appends a hook
        // only if absent; CHAINS an existing statusLine instead of replacing it — see the chain block
        // in statusline-handler.sh), writes atomically, and is idempotent. Drop a file at
        // ~/.claude/claude-console/no-autowire to opt out.
        //
        // Effect lands on the user's NEXT Claude Code session — Claude Code reads hooks/statusLine at
        // session start, so a session already running won't pick it up.
        // ==========================================================================================
        public void EnsureBridgeAutoWired()
        {
            if (!AutoWireSupported)
            {
                return;
            }

            // Never block or break plugin load — run on a background thread and swallow failures.
            new Thread(() =>
            {
                try
                {
                    // Before the opt-out, on purpose — see RecoveryScripts.
                    EnsureRecoveryScriptsInstalled();

                    if (File.Exists(BridgeOptOutFile))
                    {
                        PluginLog.Info("Bridge auto-wire: opt-out file present — skipping");
                        return;
                    }
                    EnsureBridgeInstalled();
                    EnsureBridgeWired();
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, "Bridge auto-wire failed");
                }
            })
            { IsBackground = true, Name = "claude-bridge-autowire" }.Start();
        }

        // Write the embedded bridge scripts to ~/.claude/claude-console/scripts/ (refreshed every load
        // so a plugin upgrade updates them) and mark them executable.
        private void EnsureBridgeInstalled()
        {
            if (OperatingSystem.IsWindows())
            {
                // The Windows shim is a compiled exe shipped in the plugin package — there is
                // nothing to extract, and nothing to chmod.
                return;
            }

            Directory.CreateDirectory(ScriptsDir);
            ExtractEmbeddedScript("ClaudeConsole.statusline-handler.sh", StatuslineScript);
            ExtractEmbeddedScript("ClaudeConsole.activity-hook.sh", ActivityScript);
        }

        // Write the recovery scripts to ~/.claude/claude-console/scripts/ (#45). Uninstalling through
        // Options+ deletes the package but leaves the application registration behind; when the
        // uninstalled product was the LAST of ours there is no plugin left to sweep it, and the only
        // remedy was a script in the repo. The runtime home outlives the package, so the remedy lives
        // there, refreshed every load like the bridge scripts.
        private static void EnsureRecoveryScriptsInstalled()
        {
            if (OperatingSystem.IsWindows())
            {
                // bash + `open -a` + tccutil: macOS scripts. Windows uninstall deletes the
                // application data outright (README), so there is no orphan to sweep there.
                return;
            }

            Directory.CreateDirectory(ScriptsDir);
            foreach (var name in RecoveryScripts)
            {
                ExtractEmbeddedScript("ClaudeConsole." + name, Path.Combine(ScriptsDir, name));
            }
        }

        /// <summary>
        /// The handler Claude Code should invoke: the bash script on macOS, the packaged shim on
        /// Windows. Null on Windows when the shim is missing from the package — we then leave
        /// settings.json alone rather than wiring a command that cannot run.
        /// </summary>
        internal String BridgeHandlerPath(String state) =>
            OperatingSystem.IsWindows() ? HookExePath : (state == null ? StatuslineScript : ActivityScript);

        /// <summary>
        /// The platforms auto-wiring knows how to serve: bash scripts on macOS, the compiled shim
        /// on Windows. Testable so "Windows silently skips wiring" can never come back — that
        /// exact early-return shipped in 1.8.x and the live keys showed defaults with nothing in
        /// the log to say why.
        /// </summary>
        internal static Boolean AutoWireSupported =>
            OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

        // Resolved LAZILY, never in a field initialiser: the SDK hands us the plugin path AFTER
        // construction (see PluginPaths), so anything captured at construction is null forever.
        // Same discipline as WindowsPlatformBridge.HelperPath — and the same fix, second time
        // around: this one still used AppContext.BaseDirectory, which points at the SERVICE's
        // directory under the SDK's load context, not the plugin's.
        private String _hookExePath;

        /// <summary>Where the Windows hook shim lives — beside the plugin DLL. Settable for tests.</summary>
        internal String HookExePath
        {
            get => _hookExePath ?? PluginPaths.PackagedFile("claude-console-hook.exe");
            set => _hookExePath = value;
        }

        private static void ExtractEmbeddedScript(String resourceName, String destPath)
        {
            var asm = typeof(BridgeManager).Assembly;
            using var s = asm.GetManifestResourceStream(resourceName);
            if (s == null)
            {
                PluginLog.Warning($"Bridge auto-wire: embedded resource {resourceName} not found");
                return;
            }
            using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                s.CopyTo(fs);
            }
            RunSync("/bin/chmod", "+x", destPath);
        }

        // Merge the statusLine + activity hooks into ~/.claude/settings.json. Idempotent: appends a
        // hook only if ours isn't already there, and chains (never clobbers) an existing statusLine.
        private void EnsureBridgeWired()
        {
            // Refuse a symlinked settings.json — a planted link could redirect our atomic
            // rename-over-write somewhere else entirely. (LinkTarget is null for a missing file.)
            if (new FileInfo(SettingsFile).LinkTarget != null)
            {
                PluginLog.Warning("Bridge auto-wire: settings.json is a symlink — leaving it untouched");
                return;
            }

            JsonObject root;
            if (File.Exists(SettingsFile))
            {
                var text = File.ReadAllText(SettingsFile);
                if (String.IsNullOrWhiteSpace(text))
                {
                    root = new JsonObject();
                }
                else
                {
                    JsonNode parsed;
                    try
                    {
                        parsed = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
                        {
                            CommentHandling = JsonCommentHandling.Skip,
                            AllowTrailingCommas = true,
                        });
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Warning(ex, "Bridge auto-wire: settings.json isn't valid JSON — leaving it untouched");
                        return;
                    }
                    root = parsed as JsonObject;
                    if (root == null)
                    {
                        PluginLog.Warning("Bridge auto-wire: settings.json isn't a JSON object — leaving it untouched");
                        return;
                    }
                }
            }
            else
            {
                root = new JsonObject();
            }

            var isWindows = OperatingSystem.IsWindows();
            var statusHandler = this.BridgeHandlerPath(null);
            var activityHandler = this.BridgeHandlerPath("busy");

            if (isWindows && (statusHandler == null || activityHandler == null))
            {
                PluginLog.Warning("Bridge auto-wire: claude-console-hook.exe is missing from the package — leaving settings.json untouched");
                return;
            }

            var changed = false;

            // --- hooks (additive — append our entry only when it isn't already present) ---
            if (root["hooks"] is not JsonObject hooks)
            {
                hooks = new JsonObject();
                root["hooks"] = hooks;
            }
            changed |= EnsureHook(hooks, "UserPromptSubmit", null, BridgeWiring.ActivityCommand(isWindows, activityHandler, "busy"));
            changed |= EnsureHook(hooks, "PostToolUse", "*", BridgeWiring.ActivityCommand(isWindows, activityHandler, "busy"));
            changed |= EnsureHook(hooks, "Notification", null, BridgeWiring.ActivityCommand(isWindows, activityHandler, "waiting"));
            changed |= EnsureHook(hooks, "Stop", null, BridgeWiring.ActivityCommand(isWindows, activityHandler, "done"));
            // PermissionRequest fires the moment a tool needs approval and carries the tool name and
            // its input, which is what tells a routine approval from `git push --force`. Notification
            // can't: it has no tool name and is delayed ~6s for permission prompts. Unknown events are
            // ignored by older Claude Code builds, so adding this is safe there — the badge simply
            // stays amber instead of going red.
            changed |= EnsureHook(hooks, "PermissionRequest", null, BridgeWiring.ActivityCommand(isWindows, activityHandler, "permission"));

            // --- statusLine (chain an existing one rather than clobbering it) ---
            var ourStatusCmd = BridgeWiring.StatuslineCommand(isWindows, statusHandler);
            var sl = root["statusLine"] as JsonObject;
            var existingCmd = sl?["command"]?.GetValue<String>();
            if (String.IsNullOrWhiteSpace(existingCmd))
            {
                root["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = ourStatusCmd };
                TryDelete(StatuslineChainFile);
                changed = true;
            }
            else if (BridgeWiring.IsOurs(existingCmd))
            {
                // already ours — nothing to do
            }
            else
            {
                // Preserve the user's status bar: record their command so our handler runs it and
                // passes its output through (see the chain block in statusline-handler.sh).
                File.WriteAllText(StatuslineChainFile, existingCmd);
                sl["command"] = ourStatusCmd;
                sl["type"] = "command";
                PluginLog.Info("Bridge auto-wire: chained existing statusLine so it still renders");
                changed = true;
            }

            if (!changed)
            {
                PluginLog.Info("Bridge auto-wire: settings.json already wired — no changes");
                return;
            }

            // Back up once before the first write.
            try
            {
                if (File.Exists(SettingsFile) && !File.Exists(SettingsBackup))
                {
                    File.Copy(SettingsFile, SettingsBackup);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Bridge auto-wire: couldn't back up settings.json (continuing)");
            }

            // Atomic write (temp + rename) so a concurrent reader never sees a half-written file.
            Directory.CreateDirectory(ClaudeDir);
            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var tmp = SettingsFile + ".cc.tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, SettingsFile, overwrite: true);
            PluginLog.Info("Bridge auto-wire: wired live-status bridge into settings.json — start a NEW Claude Code session to activate Cost/Context/Activity");
        }

        // Ensure a hook event's array contains an entry pointing at our activity handler; append if
        // missing. Returns true when it added one. Matches by command substring so a re-run is a
        // no-op — on either platform (see BridgeWiring.IsOurHook). Internal for the idempotence test.
        internal static Boolean EnsureHook(JsonObject hooks, String eventName, String matcher, String command)
        {
            if (hooks[eventName] is not JsonArray arr)
            {
                arr = new JsonArray();
                hooks[eventName] = arr;
            }

            foreach (var entry in arr)
            {
                if (entry?["hooks"] is not JsonArray inner)
                {
                    continue;
                }
                foreach (var h in inner)
                {
                    var c = h?["command"]?.GetValue<String>();
                    if (BridgeWiring.IsOurHook(c))
                    {
                        return false; // ours (or an equivalent) already present
                    }
                }
            }

            var newEntry = new JsonObject();
            if (matcher != null)
            {
                newEntry["matcher"] = matcher;
            }
            newEntry["hooks"] = new JsonArray
            {
                new JsonObject { ["type"] = "command", ["command"] = command },
            };
            arr.Add(newEntry);
            return true;
        }

        // ------------------------------------------------------------------------------------------
        // Speech-model bootstrap — fetch ggml-base.en.bin on first use so the user never has to
        // download it by hand. The download is verified by sha256 before it's promoted into place.
        // ------------------------------------------------------------------------------------------
        private static readonly HttpClient _modelHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        private Int32 _modelDownloading; // 0 = idle, 1 = a background download is in flight

        // True when the model is present and complete. If it is missing, kicks off a one-time
        // background download and returns false so the caller can skip the current capture.
        private Boolean EnsureVoiceModel()
        {
            try
            {
                var fi = new FileInfo(VoiceModelFile);
                if (fi.Exists && fi.Length == VoiceModelSize)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "BridgeManager.EnsureVoiceModel: stat failed");
            }

            if (Interlocked.CompareExchange(ref _modelDownloading, 1, 0) == 0)
            {
                new Thread(DownloadVoiceModel) { IsBackground = true, Name = "claude-voice-model-download" }.Start();
            }
            return false;
        }

        private void DownloadVoiceModel()
        {
            var partFile = VoiceModelFile + ".part";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(VoiceModelFile));
                TryDelete(partFile);
                PluginLog.Info($"BridgeManager: downloading whisper model (~142 MB) from {VoiceModelUrl}");

                using (var resp = _modelHttp.GetAsync(VoiceModelUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
                {
                    resp.EnsureSuccessStatusCode();
                    using (var src = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    using (var dst = new FileStream(partFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        src.CopyTo(dst, 1 << 20);
                    }
                }

                var sha = HashFileSha256(partFile);
                if (!String.Equals(sha, VoiceModelSha256, StringComparison.OrdinalIgnoreCase))
                {
                    PluginLog.Warning($"BridgeManager: model checksum mismatch (got {sha}) — discarding download");
                    TryDelete(partFile);
                    return;
                }

                TryDelete(VoiceModelFile);
                File.Move(partFile, VoiceModelFile);
                PluginLog.Info("BridgeManager: whisper model ready");
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "BridgeManager: whisper model download failed");
                TryDelete(partFile);
            }
            finally
            {
                Interlocked.Exchange(ref _modelDownloading, 0);
            }
        }

        private static String HashFileSha256(String path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        // Stop voice capture and TYPE the transcript into the focused terminal (dictation).
        // submit: true sends it (Return) — the Voice key; false leaves it in the input box for the
        // user to correct before sending — the Voice Draft key. Whisper mishears often enough that
        // "fix it, then press Return yourself" deserves a first-class path.
        public void StopVoiceCapture(Boolean submit = true) =>
            StopVoiceCaptureThen(text => InjectText(text, pressEnter: submit));

        // Stop voice capture and use the transcript to OPEN a project (new tab + cd + claude).
        public void StopVoiceCaptureForProject() => StopVoiceCaptureThen(NavigateToProjectByVoice);

        /// <summary>
        /// The one door every voice key goes through (#28). The key says what it WANTS the transcript
        /// used for; the state machine decides whether this press starts, stops, or is refused —
        /// and, crucially, where a stopped capture's transcript is routed.
        ///
        /// Each key used to hold its own "am I recording?" flag and both start and route on its own,
        /// so a second key pressed mid-recording spawned a second helper against the same files, and
        /// the destination was decided by whichever key you pressed second. Dictating a prompt and
        /// pressing Go to Project to stop it would fuzzy-match your prompt to a project and open it.
        /// </summary>
        internal void ToggleVoice(VoiceIntent intent)
        {
            if (!VoiceSupported)
            {
                PluginLog.Info("BridgeManager.ToggleVoice: voice is not supported on this platform");
                return;
            }

            var (action, routed) = Voice.Press(intent, DateTime.UtcNow);
            switch (action)
            {
                case VoiceAction.Start:
                    PluginLog.Info($"BridgeManager.ToggleVoice: starting capture for {intent}");
                    if (!this.StartVoiceCapture())
                    {
                        // Missing helper, model still downloading, unsupported platform: nothing is
                        // recording, so the state must not say otherwise or the keys lock up.
                        Voice.Finish();
                    }
                    break;

                case VoiceAction.Stop:
                    // The STARTING key's intent, not the one just pressed.
                    if (routed != intent)
                    {
                        PluginLog.Info($"BridgeManager.ToggleVoice: {intent} key stopped a {routed} capture — routing to {routed}");
                    }
                    switch (routed)
                    {
                        case VoiceIntent.Project: this.StopVoiceCaptureForProject(); break;
                        case VoiceIntent.Draft: this.StopVoiceCapture(submit: false); break;
                        default: this.StopVoiceCapture(submit: true); break;
                    }
                    break;

                default:
                    // Transcribing: a result is already in flight and starting again would delete
                    // the file the waiting thread is about to read.
                    PluginLog.Info($"BridgeManager.ToggleVoice: ignoring {intent} press — a {routed} transcript is still in flight");
                    _platform.Alert();
                    break;
            }
        }

        // Shared: signal the helper to stop, then wait for the transcript off the UI thread and run
        // <paramref name="handler"/> with it. Every way the wait can end WITHOUT text — a named
        // failure, a blank transcript, a timeout — is reported to the key (#18); a denied
        // microphone arrives as a named failure now, not as silence.
        private void StopVoiceCaptureThen(Action<String> handler)
        {
            if (!VoiceSupported)
            {
                return;
            }

            try
            {
                File.WriteAllText(VoiceStopFile, "");
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "BridgeManager.StopVoiceCapture: failed to write stop flag");
                Voice.Finish();
                return;
            }

            new Thread(() =>
            {
              // Whatever happens below — transcript, named failure, silence, timeout, an exception —
              // the capture is over when this thread ends. A flag that could survive one crashed
              // helper would leave every voice key dead until the plugin reloaded, which is a worse
              // bug than the one being fixed (#28).
              // Read before anything can Finish() it: the intent of the capture this thread is
              // waiting on, so a failure lands on the key that was pressed.
              var intent = Voice.Intent;
              try
              {
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(150);

                    // A named failure beats a blank transcript. The helper writes this sidecar when
                    // whisper could not run at all — missing backend, missing model, a crash — which
                    // otherwise arrives here as an empty string, indistinguishable from silence, and
                    // is reported to the user as "didn't catch that" for months (#24).
                    if (File.Exists(VoiceErrorFile))
                    {
                        String error;
                        try { error = File.ReadAllText(VoiceErrorFile).Trim(); }
                        catch { continue; }

                        TryDelete(VoiceErrorFile);
                        TryDelete(VoiceTranscriptFile);
                        this.ReportVoiceFailure(intent, VoiceFailure.FromSidecar(error), error);
                        return;
                    }

                    if (!File.Exists(VoiceTranscriptFile))
                    {
                        continue;
                    }

                    String text;
                    try
                    {
                        text = File.ReadAllText(VoiceTranscriptFile).Trim();
                    }
                    catch
                    {
                        continue; // still being written — retry
                    }

                    TryDelete(VoiceTranscriptFile);

                    // Whisper labels sounds it could not read as speech: "(gunshot)", "(static)",
                    // "[BLANK_AUDIO]". They are descriptions of noise, not words anyone said, and
                    // acting on one is acting on a failed dictation. Untreated, "(gunshot)" fuzzy-
                    // matched a project called SafeShot and OPENED it, and "(static)" opened
                    // StatementSense — a wrong project launched from across the room. The same text
                    // would otherwise be typed into a session by the Voice keys.
                    var spoken = CleanTranscript(text);
                    if (!String.IsNullOrWhiteSpace(spoken))
                    {
                        PluginLog.Info($"BridgeManager: transcript ({spoken.Length} chars): {spoken}");
                        try { handler(spoken); }
                        catch (Exception ex) { PluginLog.Warning(ex, "BridgeManager: transcript handler failed"); }
                    }
                    else if (!String.IsNullOrWhiteSpace(text))
                    {
                        // Nothing survived the strip: whisper heard a noise and named it.
                        this.ReportVoiceFailure(intent, VoiceFailure.NoSpeech, $"whisper reported \"{text}\", nothing to act on");
                    }
                    else
                    {
                        // Genuinely empty. This used to be "silence or mic denied" with no way to
                        // tell which; a denial now arrives as a sidecar above, so this IS silence.
                        this.ReportVoiceFailure(intent, VoiceFailure.NoSpeech, "empty transcript (silence)");
                    }
                    return;
                }
                // The helper died without writing anything — the denied-microphone case before the
                // sidecar covered it, or a helper killed mid-run. Nothing will arrive; say so.
                this.ReportVoiceFailure(intent, VoiceFailure.NoResponse, "transcript not produced within 20s");
              }
              finally
              {
                Voice.Finish();
              }
            })
            { IsBackground = true, Name = "claude-voice-transcript" }.Start();
        }

        /// <summary>
        /// Remove whisper's non-speech annotations — anything inside (…) or […] — and collapse the
        /// whitespace left behind. What remains is what the user actually said, which may be nothing.
        ///
        /// This lives in the engine rather than in a helper because BOTH helpers feed it and they
        /// disagreed: the Windows one stripped these, the macOS one matched three exact literals
        /// ("[BLANK_AUDIO]", "(silence)", "[ Silence ]") and let every other annotation through.
        /// One place, one rule, every key that consumes a transcript.
        /// </summary>
        internal static String CleanTranscript(String text)
        {
            if (String.IsNullOrEmpty(text))
            {
                return "";
            }

            var sb = new StringBuilder(text.Length);
            var depth = 0;
            foreach (var ch in text)
            {
                if (ch == '[' || ch == '(')
                {
                    depth++;
                }
                else if ((ch == ']' || ch == ')') && depth > 0)
                {
                    depth--;
                }
                else if (depth == 0)
                {
                    sb.Append(ch);
                }
            }

            return String.Join(" ", sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        // Projects the plugin already knows are real, because a session reported working in one.
        // These count wherever they live, which is the point — they need no root to be under (#26).
        private IReadOnlyList<String> KnownProjectDirs() =>
            Grid.Sessions.Values
                .Select(s => s.ProjectDir)
                .Where(d => !String.IsNullOrEmpty(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        // Match a spoken phrase to a project folder, then open it (new Terminal tab + cd + claude).
        private void NavigateToProjectByVoice(String transcript)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = ProjectDiscovery.Candidates(
                home, ProjectDiscovery.DefaultRootsFile(home), this.KnownProjectDirs());

            var match = MatchProject(transcript, candidates.Paths);
            if (match == null)
            {
                // Say what was searched. The whole of #26 reached us as "it does nothing" — a line
                // naming the candidate count and where they came from would have diagnosed itself.
                PluginLog.Warning(
                    $"NavigateToProjectByVoice: no project matched \"{transcript}\" among "
                    + $"{candidates.Paths.Count} candidate(s) — {candidates.Source}. If your projects "
                    + $"live elsewhere, list their roots in {ProjectDiscovery.DefaultRootsFile(home)}");
                _platform.Alert(); // audible "didn't catch a project" feedback
                return;
            }
            PluginLog.Info($"NavigateToProjectByVoice: \"{transcript}\" -> {match} (of {candidates.Paths.Count} candidates)");
            LaunchClaudeInProject(match);
        }

        internal static String MatchProject(String transcript, IEnumerable<String> candidates)
        {
            var t = NormalizeForMatch(transcript);
            if (t.Length < 2)
            {
                return null;
            }

            String best = null;
            var bestScore = 0;
            foreach (var dir in candidates ?? Enumerable.Empty<String>())
            {
                if (String.IsNullOrEmpty(dir))
                {
                    continue;
                }

                // Match on the folder NAME, so a trailing separator can't reduce it to nothing.
                var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var f = NormalizeForMatch(name);
                if (f.Length == 0)
                {
                    continue;
                }
                var score = MatchScore(t, f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = dir;
                }
            }

            // Require a real match (exact / prefix / substring / strong overlap) to avoid mis-launches.
            return bestScore >= 300 ? best : null;
        }

        // Lowercase, drop common filler/command words ("open the X project"), keep letters+digits only.
        internal static String NormalizeForMatch(String s)
        {
            s = (s ?? "").ToLowerInvariant();
            foreach (var w in new[] { "go to", "switch to", "open", "launch", "the", "project", "folder", "claude" })
            {
                s = s.Replace(w, " ");
            }
            return new String(s.Where(Char.IsLetterOrDigit).ToArray());
        }

        internal static Int32 MatchScore(String t, String f)
        {
            if (t == f) return 1000;
            if (f.StartsWith(t) || t.StartsWith(f)) return 700 + Math.Min(t.Length, f.Length);
            if (f.Contains(t) || t.Contains(f)) return 500 + Math.Min(t.Length, f.Length);

            // Fuzzy fallback: longest contiguous overlap, ≥5 chars and ≥50% of the shorter name.
            //
            // Four was too generous, and it launched the wrong projects: "gunshot" reached SafeShot
            // on "shot", "static" reached StatementSense on "stat". Whisper's noise annotations are
            // now stripped before matching, which is the real fix — this is the second line of
            // defence, since a four-letter overlap is thin evidence that a mishearing meant THIS
            // project. Genuine near-misses keep matching: "tailor"/"sailor" share five.
            var lcs = LongestCommonSubstringLength(t, f);
            var shorter = Math.Min(t.Length, f.Length);
            if (lcs >= 5 && shorter > 0 && lcs * 2 >= shorter)
            {
                return 300 + lcs;
            }
            return 0;
        }

        internal static Int32 LongestCommonSubstringLength(String a, String b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            var prev = new Int32[b.Length + 1];
            var best = 0;
            for (var i = 1; i <= a.Length; i++)
            {
                var cur = new Int32[b.Length + 1];
                for (var j = 1; j <= b.Length; j++)
                {
                    if (a[i - 1] == b[j - 1])
                    {
                        cur[j] = prev[j - 1] + 1;
                        if (cur[j] > best) best = cur[j];
                    }
                }
                prev = cur;
            }
            return best;
        }

        // Open the project in a terminal and start claude there. Where exactly (reuse an idle tab
        // vs open a new one) is the backend's call — it needs terminal-specific knowledge of what
        // "idle" means. What is platform-neutral, and stays here, is the pin bookkeeping.
        internal void LaunchClaudeInProject(String path)
        {
            // Opening a project is an explicit "I'm working here now", and it starts a session in a
            // tab that has no key yet. Holding a pin from before would quietly send Yes / Clear /
            // voice to the OLD session while you type in the new one.
            this.ClearPin();

            _platform.LaunchClaudeInProject(path);
        }

        private static void TryDelete(String path)
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }

        private void RunDetached(String file, List<String> args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                foreach (var a in args)
                {
                    psi.ArgumentList.Add(a);
                }
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"BridgeManager.RunDetached: failed to launch {file}");
            }
        }

    }
}
