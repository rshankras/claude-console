namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// The Windows backend: session discovery (Phase 1), console injection via a short-lived
    /// helper (Phase 2), and terminal control through wt.exe (Phase 3). Voice is not implemented
    /// (Phase 5) — those keys log and no-op rather than pretending.
    ///
    /// NOTHING HERE HAS RUN ON WINDOWS HARDWARE. The decision logic is unit-tested on macOS and
    /// the helpers cross-compile clean, but the Win32 calls themselves are unverified — see the
    /// ordered checklist in docs/windows-port-2.0-plan.md, starting with
    /// `claude-console-inject selftest`.
    ///
    /// Sessions are keyed by the Claude process (PID + start time), never by window title: the
    /// 2026-08-07 spike found every Claude tab reports the identical title "✳ Claude Code", and
    /// that Claude REWRITES the title to a conversation summary once chatting starts. See
    /// docs/windows-port-2.0-plan.md §1.
    /// </summary>
    internal sealed class WindowsPlatformBridge : IPlatformBridge
    {
        public String Name => "Windows";

        /// <summary>
        /// The agent this product drives: what its processes look like, and the CLI the launch
        /// keys run. Declared once, at construction. The matcher defaults to None — matching
        /// NOTHING — for the same reason AgentProcessMatcher.None exists at all: an undeclared
        /// product must never silently adopt another agent's sessions.
        /// </summary>
        internal WindowsPlatformBridge(AgentProcessMatcher matcher = null, String cliCommand = "claude")
        {
            this._matcher = matcher ?? AgentProcessMatcher.None;
            this._cliCommand = String.IsNullOrWhiteSpace(cliCommand) ? "claude" : cliCommand;
            WindowsTerminalCli.AgentCli = this._cliCommand;
        }

        private readonly AgentProcessMatcher _matcher;
        private readonly String _cliCommand;

        // Discovery, injection and terminal control are all implemented (Phases 1-3). Voice is
        // not (Phase 5) — those keys log and no-op. Nothing gates on this today; it is the
        // backend's own statement of whether it has a working implementation, and Windows now does.
        public Boolean IsSupported => OperatingSystem.IsWindows();

        // Measured on Windows, twice, on sessions that were never restarted (#58):
        //   2026-09-10 14:57:44 settings.json wired → 14:58:21 a session started at 14:39 reported
        //     its status line (37 s, no restart between — docs/windows-qa-2.2.1.md);
        //   2026-09-11 12:50:52 rewired after an Off → 12:50:57 "Live status: Enabled" from a
        //     session started the previous evening, and its PermissionRequest hook fired at
        //     12:57:32 (hook-invoked.log) — so the approval path applies live too, not only the
        //     status line. The owner watched the Cost key go from the setup word to a value with
        //     no restart (docs/windows-qa-2.2.2.md, pass 2).
        // QA's 2.2.0 report that nothing came alive until a restart was #74 wearing this face: with
        // the hook writing "permission" no press could ever land, restart or not.
        public Boolean SettingsApplyLive => true;

        /// <summary>
        /// Enumerates the process table. Injectable so the discovery logic is testable on any OS —
        /// the default implementation is the only Windows-only code in this class.
        /// </summary>
        internal Func<IEnumerable<WindowsProcessInfo>> ProcessEnumerator { get; set; }

        /// <summary>
        /// Resolves a PID's full command line, or null if it can't be read. Only consulted for
        /// interpreter processes (an npm/bun install), and cached per PID — the native
        /// `claude.exe` install needs no command line at all, so the common case costs nothing.
        /// </summary>
        internal Func<Int32, String> CommandLineResolver { get; set; }

        /// <summary>
        /// Resolves a PID's parent, or 0 if unknown. Test seam; production batches parents into
        /// the same query as command lines. Parents exist so SessionsFrom's one-key-per-session
        /// rule can fire: codex ALWAYS runs as a TUI process plus a child codex (its app server),
        /// and without parents both got a key — "codex codex" on real hardware, with the phantom
        /// child's key focusing nothing.
        /// </summary>
        internal Func<Int32, Int32> ParentPidResolver { get; set; }

        // Session key → (command line, parent pid). Pruned every scan to the processes still
        // alive, so a long-running plugin that sees thousands of short-lived node processes
        // doesn't grow.
        private readonly Dictionary<String, (String Cmd, Int32 Ppid)> _cmdCache =
            new Dictionary<String, (String, Int32)>(StringComparer.Ordinal);

        /// <summary>Test seam: proves the cache is pruned rather than accumulating.</summary>
        internal Int32 CommandLineCacheCount => _cmdCache.Count;

        internal Func<Int32, DateTime, String> DirectoryResolver { get; set; } = WindowsProcessDirectory.Read;
        private readonly Dictionary<String, String> _sessionDirectories = new(StringComparer.Ordinal);
        public IReadOnlyDictionary<String, String> SessionDirectories => _sessionDirectories;

        public HashSet<String> DiscoverSessions()
        {
            var enumerator = this.ProcessEnumerator ?? this.EnumerateProcesses;

            List<WindowsProcessInfo> rows;
            try
            {
                rows = enumerator()?.ToList();
            }
            catch (Exception ex)
            {
                // "I don't know", NOT "none" — an empty set would tell the registry to reap every
                // live session (see IPlatformBridge.DiscoverSessions).
                PluginLog.Verbose(ex, "WindowsPlatformBridge: process scan failed");
                return null;
            }

            if (rows == null)
            {
                return null;
            }

            this.FillCommandLines(rows);

            // Remember where a live CLI session actually runs from, so the launch keys follow
            // the real install location (any directory, any drive) rather than assuming the
            // native installer's default. IsClaudeSession has already excluded Claude Desktop —
            // its exe is also claude.exe, and capturing IT would launch the desktop app.
            foreach (var row in rows)
            {
                if (WindowsProcessWatcher.IsAgentSession(row, this._matcher))
                {
                    var exe = WindowsProcessWatcher.ExeFromCommandLine(row.CommandLine, this._matcher);
                    if (exe != null)
                    {
                        WindowsTerminalCli.ObservedClaudeExe = exe;
                        break;
                    }
                }
            }

            var sessions = WindowsProcessWatcher.SessionsFrom(rows, this._matcher);

            // Only Codex needs these hints — it can withhold SessionStart until its first prompt,
            // leaving a key with no project name. Claude Code names every session through its own
            // hook, so reading another process's memory for it would be a cost with no reader.
            // The macOS bridge gates the same way; keep the two backends symmetrical.
            if (_cliCommand != "codex")
            {
                _sessionDirectories.Clear();
                return sessions;
            }

            foreach (var stale in _sessionDirectories.Keys.Where(k => !sessions.Contains(k)).ToArray())
                _sessionDirectories.Remove(stale);
            foreach (var row in rows)
            {
                var key = WindowsProcessWatcher.SessionKeyFor(row);
                if (!sessions.Contains(key)) continue;
                // Retry missing data and refresh CWD after /resume; only a few bounded reads
                // per live CLI, with no subprocess, directory crawl or title inference.
                String directory = null;
                try { directory = DirectoryResolver?.Invoke(row.Pid, row.StartTime); }
                catch { /* Access may change while the session is running; metadata is optional. */ }
                if (!String.IsNullOrWhiteSpace(directory)) _sessionDirectories[key] = directory;
            }
            return sessions;
        }

        // Every candidate needs its command line (see NeedsCommandLine), so the cost matters: a
        // machine with Claude Desktop open has a dozen claude.exe processes. Results are cached per
        // session key and only UNSEEN pids are looked up, so a steady state costs nothing; when
        // there are new pids they are fetched in ONE batched query rather than one process each.
        private void FillCommandLines(List<WindowsProcessInfo> rows)
        {
            // Drop cache entries for processes that are gone, so a long-running plugin doesn't grow.
            var liveKeys = new HashSet<String>(rows.Select(WindowsProcessWatcher.SessionKeyFor), StringComparer.Ordinal);
            foreach (var stale in _cmdCache.Keys.Where(k => !liveKeys.Contains(k)).ToList())
            {
                _cmdCache.Remove(stale);
            }

            var pending = rows
                .Where(r => r.CommandLine == null && NeedsCommandLine(r) &&
                            !_cmdCache.ContainsKey(WindowsProcessWatcher.SessionKeyFor(r)))
                .ToList();

            if (pending.Count > 0)
            {
                var perPid = this.CommandLineResolver != null
                    ? pending.ToDictionary(
                        r => r.Pid,
                        r => (Cmd: SafeResolve(this.CommandLineResolver, r.Pid),
                              Ppid: this.ParentPidResolver != null ? SafeResolveParent(this.ParentPidResolver, r.Pid) : 0))
                    : ResolveDetails(pending.Select(r => r.Pid));

                foreach (var row in pending)
                {
                    perPid.TryGetValue(row.Pid, out var details);
                    _cmdCache[WindowsProcessWatcher.SessionKeyFor(row)] = details;
                }
            }

            foreach (var row in rows)
            {
                if (_cmdCache.TryGetValue(WindowsProcessWatcher.SessionKeyFor(row), out var cached))
                {
                    row.CommandLine ??= cached.Cmd;
                    if (row.ParentPid == 0)
                    {
                        row.ParentPid = cached.Ppid;
                    }
                }
            }
        }

        private static Int32 SafeResolveParent(Func<Int32, Int32> resolver, Int32 pid)
        {
            try
            {
                return resolver(pid);
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, $"WindowsPlatformBridge: could not read parent for pid {pid}");
                return 0;
            }
        }

        private static String SafeResolve(Func<Int32, String> resolver, Int32 pid)
        {
            try
            {
                return resolver(pid);
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, $"WindowsPlatformBridge: could not read command line for pid {pid}");
                return null;
            }
        }

        /// <summary>
        /// EVERY candidate needs its command line — including claude.exe.
        ///
        /// This used to skip claude.exe as an "optimisation", on the theory that the native CLI is
        /// identifiable by name alone. It isn't: Claude DESKTOP's executable is also called
        /// claude.exe, and the only thing separating them is the install path in the command line.
        /// Skipping the lookup meant the Desktop markers had nothing to match, so all ten of
        /// Desktop's processes were counted as Claude Code sessions (seen on real hardware
        /// 2026-08-07 — slots pinned to a crashpad handler and a GPU process).
        /// </summary>
        internal static Boolean NeedsCommandLine(WindowsProcessInfo p) => p?.Name != null;

        // ------------------------------------------------------------------------------------------
        // Windows-only plumbing below. Compiles everywhere; only ever CALLED on Windows.
        // ------------------------------------------------------------------------------------------

        // Names worth enumerating at all — everything else in the process table is irrelevant, and
        // touching fewer Process objects keeps the ~2s scan cheap. The AGENT's names come from the
        // matcher; only the interpreter set is static knowledge. This was a hardcoded claude list
        // until 2026-08-20 — the eighth bug of the built-but-never-wired shape, and the deepest:
        // the matcher-driven watcher was correct and never received a codex row to inspect,
        // because the scan never asked the OS for processes named codex. The unit tests inject
        // their process tables, so only hardware could see it.
        internal static IEnumerable<String> NamesWorthEnumerating(AgentProcessMatcher matcher)
        {
            foreach (var name in matcher?.ExeNames ?? Array.Empty<String>())
            {
                yield return name;
            }

            yield return "node";
            yield return "bun";
            yield return "deno";
            yield return "npx";
        }

        private IEnumerable<WindowsProcessInfo> EnumerateProcesses()
        {
            var rows = new List<WindowsProcessInfo>();

            foreach (var name in NamesWorthEnumerating(this._matcher))
            {
                Process[] found;
                try
                {
                    found = Process.GetProcessesByName(name);   // takes the name WITHOUT .exe
                }
                catch (Exception ex)
                {
                    PluginLog.Verbose(ex, $"WindowsPlatformBridge: GetProcessesByName({name}) failed");
                    continue;
                }

                foreach (var proc in found)
                {
                    try
                    {
                        rows.Add(new WindowsProcessInfo
                        {
                            Pid = proc.Id,
                            // Filled by FillCommandLines: the parent rides the same batched CIM
                            // query as the command line. It was a KNOWN GAP ("nesting barely
                            // occurs") until codex — which ALWAYS nests, TUI plus a child codex —
                            // put two keys per session on real hardware (2026-08-20).
                            ParentPid = 0,
                            Name = name + ".exe",
                            StartTime = proc.StartTime,
                            CommandLine = null,          // resolved lazily, only when ambiguous
                        });
                    }
                    catch (Exception ex)
                    {
                        // A process can exit between enumeration and property access; also access
                        // denied for higher-integrity processes. Skip it rather than fail the scan.
                        PluginLog.Verbose(ex, $"WindowsPlatformBridge: skipping pid {SafePid(proc)}");
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }

            return rows;
        }

        private static String SafePid(Process p)
        {
            try { return p.Id.ToString(); } catch { return "?"; }
        }

        /// <summary>
        /// Fetch command lines AND parent pids for many pids in ONE query.
        ///
        /// Batched deliberately: every agent-named process needs its command line to tell the CLI
        /// from a desktop app, and the parent is what lets SessionsFrom keep one key per session —
        /// codex always runs as a TUI process plus a child codex, and without parents both keyed.
        /// One PowerShell spawn per scan-with-new-pids is fine; a dozen would not be.
        /// </summary>
        internal static Dictionary<Int32, (String Cmd, Int32 Ppid)> ResolveDetails(IEnumerable<Int32> pids)
        {
            var result = new Dictionary<Int32, (String, Int32)>();
            var list = pids?.Distinct().ToList() ?? new List<Int32>();
            if (list.Count == 0 || !OperatingSystem.IsWindows())
            {
                return result;
            }

            // Deliberately NOT a System.Management dependency: that would add a NuGet assembly to
            // the shipped package. Shell out under the same hard timeout discipline every backend
            // uses (BoundedProcess). Tab-separated so a command line containing commas is safe.
            //
            // Single braces, and it matters: `ForEach-Object {{ … }}` makes PowerShell emit the
            // inner scriptblock's TEXT once per row instead of evaluating it — no pid, no tab,
            // nothing parses, and every command line silently stays null. On real hardware
            // (2026-08-07) that let all of Claude Desktop's processes through the marker filter
            // and the keypad typed into a windowless GPU process.
            var filter = String.Join(" or ", list.Select(p => $"ProcessId={p}"));
            var output = BoundedProcess.Run(
                "powershell.exe",
                new List<String>
                {
                    "-NoProfile", "-NonInteractive", "-Command",
                    $"Get-CimInstance Win32_Process -Filter \"{filter}\" | " +
                    "ForEach-Object { \"$($_.ProcessId)`t$($_.ParentProcessId)`t$($_.CommandLine)\" }",
                },
                8000,
                wantOutput: true);

            if (String.IsNullOrWhiteSpace(output))
            {
                return result;
            }

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                var tab = trimmed.IndexOf('\t');
                if (tab <= 0)
                {
                    continue;
                }

                var second = trimmed.IndexOf('\t', tab + 1);
                if (second <= tab)
                {
                    continue;
                }

                if (Int32.TryParse(trimmed.Substring(0, tab), out var pid))
                {
                    Int32.TryParse(trimmed.Substring(tab + 1, second - tab - 1), out var ppid);
                    result[pid] = (trimmed.Substring(second + 1), ppid);
                }
            }

            return result;
        }

        // ------------------------------------------------------------------------------------------
        // Injection (Phase 2) — one short-lived helper process per keypress.
        //
        // The guarantee IPlatformBridge demands (focus the target and type, or type nothing) holds
        // differently here than on macOS, and more strongly: WriteConsoleInput addresses a CONSOLE
        // HANDLE, not the foreground window. There is no "focus" step that another app could win a
        // race against — the keystrokes are delivered to the attached console or not at all, which
        // is why the 2026-08-07 spike could type into a hidden tab while Notepad held the
        // foreground and see nothing leak.
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Runs claude-console-inject.exe. Injectable so the argument construction and outcome
        /// mapping are testable without Windows; returns the helper's exit code (null if it could
        /// not be run at all).
        /// </summary>
        internal Func<List<String>, Int32?> InjectRunner { get; set; }

        public InjectionOutcome InjectText(String sessionKey, String text, Boolean pressEnter)
        {
            if (String.IsNullOrEmpty(text))
            {
                return InjectionOutcome.Skipped;
            }

            return this.RunInject(WindowsInjection.TextArgs(sessionKey, text, pressEnter));
        }

        public InjectionOutcome InjectKey(String sessionKey, KeyStroke key) =>
            this.RunInject(WindowsInjection.KeyArgs(sessionKey, key));

        public InjectionOutcome InjectTabThenEnter(String sessionKey) =>
            this.RunInject(WindowsInjection.TabThenEnterArgs(sessionKey));

        private InjectionOutcome RunInject(List<String> args)
        {
            // Null args means the session key didn't name a process we can reach — treat it as a
            // missing session rather than launching the helper with nothing to aim at.
            if (args == null)
            {
                PluginLog.Warning("WindowsPlatformBridge: no usable target session — nothing typed");
                return InjectionOutcome.SessionMissing;
            }

            var runner = this.InjectRunner ?? this.RunHelper;
            var outcome = WindowsInjection.OutcomeFor(runner(args));

            if (outcome != InjectionOutcome.Ok)
            {
                PluginLog.Warning($"WindowsPlatformBridge: injection skipped — {WindowsInjection.Explain(outcome)}");
            }
            return outcome;
        }

        private Int32? RunHelper(List<String> args)
        {
            var exe = this.HelperPath;
            if (exe == null)
            {
                PluginLog.Warning("WindowsPlatformBridge: claude-console-inject.exe not found in the plugin package");
                return null;
            }

            return BoundedProcess.RunForExitCode(exe, WindowsTools.Arguments(exe, "inject", args), 15000);
        }

        // Resolved LAZILY on every use, not cached at construction: the SDK hands us the plugin
        // path in Plugin.AssemblyFilePath AFTER the bridge is created, so anything captured in a
        // field initialiser is null forever. Settable for tests.
        private String _helperPath;

        /// <summary>Where the inject helper lives — beside the plugin DLL. See PluginPaths.</summary>
        internal String HelperPath
        {
            get => _helperPath ?? WindowsTools.PathFor("inject");
            set => _helperPath = value;
        }

        // ------------------------------------------------------------------------------------------
        // Terminal control (Phase 3) — via wt.exe, the only supported way to drive Windows Terminal.
        //
        // TWO HONEST GAPS, both consequences of Windows Terminal having no automation API:
        //
        // 1. QueryFrontmostSession returns null — "I don't know". Nothing supported maps a terminal
        //    TAB to the process running in it, so we cannot say which session the user is looking
        //    at. This is NOT a silent failure: BridgeManager.RoutingTty degrades through its other
        //    rules, so one session still works, and "exactly one session waiting on you" still
        //    works. With several idle sessions the user presses a session key first — which is the
        //    explicit, already-supported way to aim the keypad. Pinning is exact on Windows even
        //    though frontmost-tracking is not.
        //
        // 2. FocusSession — SOLVED, one level below wt: the tab's label IS the session's console
        //    title, readable via AttachConsole, and UI Automation can select the TabItem carrying
        //    it. claude-console-focus.exe does exactly that, driving UI Automation through COM so
        //    it trims like the other helpers (it is a third exe rather than an inject verb because
        //    a UIA walk belongs in a process that exits). When the helper is missing or can't
        //    identify the tab, we degrade to raising the terminal window — the pre-helper behavior.
        // ------------------------------------------------------------------------------------------

        /// <summary>Runs a terminal command. Injectable so navigation is testable without Windows.</summary>
        internal Func<String, List<String>, Boolean> TerminalRunner { get; set; }

        /// <summary>
        /// Does an existing Windows Terminal window exist? Commands addressed to `-w 0` require
        /// one; without this guard wt may quietly do nothing or create an unrelated window (#33).
        /// Injectable because the production probe is necessarily Windows-only.
        /// </summary>
        internal Func<Boolean> TerminalWindowProbe { get; set; }

        /// <summary>
        /// Raised when a terminal-dependent press cannot be delivered. BridgeManager owns how the
        /// user is told; the platform owns detecting the failure.
        /// </summary>
        internal Action<String> TerminalUnavailable { get; set; }

        public String QueryFrontmostSession() => null;   // see gap 1 above

        public void Navigate(TerminalAction action)
        {
            var args = WindowsTerminalCli.ArgsFor(action);
            if (args == null)
            {
                // Cycling windows is an OS gesture wt.exe can't express. Better to say so than to
                // send something that does the wrong thing.
                PluginLog.Info($"WindowsPlatformBridge: {action} is not available on Windows Terminal");
                return;
            }

            this.RunTerminal(args, requiresExistingWindow: action != TerminalAction.NewClaudeWindow);
        }

        private readonly Object _captureLock = new();
        private System.Threading.EventWaitHandle _captureCancel;
        internal Func<String, List<String>, Int32, Int32?> CaptureRunner { get; set; } = BoundedProcess.RunForExitCode;

        public Boolean TryCancelScreenshot()
        {
            lock (_captureLock)
            {
                if (_captureCancel == null) return false;
                _captureCancel.Set();
                return true;
            }
        }

        public Boolean CaptureScreenshotInteractive(String outputPath)
        {
            // Windows' interactive capture (the ms-screenclip: overlay) delivers to the
            // clipboard, not a file, and a two-minute wait on an overlay does not belong in the
            // service process. claude-console-shot.exe owns the whole dance: launch the overlay,
            // wait for the snip, read the clipboard through Win32, save the PNG.
            var helper = this.ShotHelperPath;
            if (helper == null || !File.Exists(helper))
            {
                PluginLog.Warning("WindowsPlatformBridge: claude-console-shot.exe not found in the plugin package");
                return false;
            }

            // The helper polices its own 120s deadline and exits fast on a dismissed overlay;
            // the bound here is the backstop, a little above the helper's own.
            var eventName = @"Local\VizhiCapture-" + Guid.NewGuid().ToString("N");
            using var cancel = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset, eventName);
            lock (_captureLock)
            {
                if (_captureCancel != null) return false; // one outstanding picker per product
                _captureCancel = cancel;
            }
            try
            {
                var exit = CaptureRunner(helper,
                    WindowsTools.Arguments(helper, "shot", new[] { outputPath, "--cancel-event", eventName }), 130000);
                if (cancel.WaitOne(0) || exit != 0 || !File.Exists(outputPath))
                {
                    PluginLog.Info($"WindowsPlatformBridge.CaptureScreenshotInteractive: no capture (exit {exit?.ToString() ?? "null"})");
                    return false;
                }
                return true;
            }
            finally
            {
                lock (_captureLock) _captureCancel = null;
            }
        }

        private String _shotHelperPath;

        /// <summary>Path to claude-console-shot.exe. Injectable for tests.</summary>
        internal String ShotHelperPath
        {
            get => this._shotHelperPath ?? WindowsTools.PathFor("shot");
            set => this._shotHelperPath = value;
        }

        public void LaunchAgentSession(String[] extraArgs)
        {
            var args = WindowsTerminalCli.LaunchAgentArgs(extraArgs);
            if (args == null)
            {
                return;
            }

            this.RunTerminal(args, requiresExistingWindow: true);
        }

        public void LaunchClaudeInProject(String projectDir)
        {
            var args = WindowsTerminalCli.LaunchClaudeArgs(projectDir);
            if (args == null)
            {
                return;
            }

            this.RunTerminal(args, requiresExistingWindow: true);
        }

        /// <summary>Runs claude-console-focus.exe. Injectable for tests; returns its exit code.</summary>
        internal Func<List<String>, Int32?> FocusRunner { get; set; }

        // Lazy for the same reason as HelperPath: the SDK provides the plugin path after
        // construction. Settable for tests.
        private String _focusHelperPath;

        /// <summary>Where the tab-focus helper lives — beside the plugin DLL. See PluginPaths.</summary>
        internal String FocusHelperPath
        {
            get => _focusHelperPath ?? WindowsTools.PathFor("focus");
            set => _focusHelperPath = value;
        }

        // The focus helper's exit contract (see tools/windows/ClaudeConsoleFocus/Program.cs):
        // 0 = tab selected + window raised; 4 = window raised, tab not identified; anything else
        // (2 gone, 5 elevated, null crashed/missing runtime) = nothing happened on screen.
        private const Int32 FocusExitOk = 0;
        private const Int32 FocusExitRaisedOnly = 4;

        public void FocusSession(String sessionKey) => this.TryFocusSession(sessionKey);

        public Boolean TryFocusSession(String sessionKey)
        {
            if (!WindowsInjection.TryParseSessionKey(sessionKey, out _, out _))
            {
                return false;
            }

            var exe = this.FocusHelperPath;
            var runner = this.FocusRunner ?? (exe != null
                ? args => BoundedProcess.RunForExitCode(exe, WindowsTools.Arguments(exe, "focus", args), 10000)
                : (Func<List<String>, Int32?>)null);

            if (runner != null)
            {
                var code = runner(WindowsInjection.FocusArgs(sessionKey));
                if (code == FocusExitOk)
                {
                    return true;
                }
                if (code == FocusExitRaisedOnly)
                {
                    PluginLog.Verbose("WindowsPlatformBridge: focus helper could not verify the selected tab");
                    return false;   // raising a window does not identify the requested tab
                }
                PluginLog.Info($"WindowsPlatformBridge: focus helper exit {(code.HasValue ? code.ToString() : "null")} — raising the terminal window instead");
            }

            // Degraded mode: bring the terminal window forward without selecting the tab — the
            // behavior all of Phase 3 had before the focus helper existed.
            this.RunTerminal(WindowsTerminalCli.ArgsFor(TerminalAction.Activate), requiresExistingWindow: true);
            return false;
        }

        public void Alert()
        {
            var exe = this.HelperPath;
            if (exe != null)
            {
                BoundedProcess.RunForExitCode(exe, WindowsTools.Arguments(exe, "inject", new[] { "beep" }), 5000);
            }
        }

        // A terminal-dependent press that lands nowhere is the #33 bug (retest item 16): Claude Code
        // in a classic console window, or wt.exe absent on stock Windows 10. `wt -w 0` with no
        // Windows Terminal window open either fails or spawns a window the user is not looking at,
        // so the probe comes FIRST — a zero exit from wt.exe is not proof the press did anything.
        private Boolean RunTerminal(List<String> args, Boolean requiresExistingWindow)
        {
            if (requiresExistingWindow && !this.HasTerminalWindow())
            {
                return this.ReportTerminalUnavailable("no Windows Terminal window is running");
            }

            var runner = this.TerminalRunner;
            if (runner != null)
            {
                return runner(WindowsTerminalCli.Exe, args)
                    || this.ReportTerminalUnavailable("wt.exe could not deliver the action");
            }

            // wt.exe is absent on stock Windows 10 (R9). Treat null AND nonzero as failure: both
            // mean the requested action was not delivered, and a silent key is the #33 bug.
            var exit = BoundedProcess.RunForExitCode(WindowsTerminalCli.Exe, args, 10000);
            return exit == 0
                || this.ReportTerminalUnavailable(
                    exit.HasValue ? $"wt.exe exited with status {exit}" : "wt.exe is unavailable");
        }

        private Boolean HasTerminalWindow()
        {
            if (this.TerminalWindowProbe != null)
            {
                try { return this.TerminalWindowProbe(); }
                catch (Exception ex)
                {
                    PluginLog.Verbose(ex, "WindowsPlatformBridge: Windows Terminal probe failed");
                    return false;
                }
            }

            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try { return HasWindowsTerminalWindow(); }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, "WindowsPlatformBridge: could not inspect Windows Terminal windows");
                return false;
            }
        }

        // Process.MainWindowHandle is not evidence here: WindowsTerminal.exe reports zero on
        // current builds even while its HWND is visible. Enumerate real top-level windows and use
        // the same stable class name the hardware-proven focus helper uses (#33).
        private const String TerminalWindowClass = "CASCADIA_HOSTING_WINDOW_CLASS";

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        internal static Boolean HasWindowsTerminalWindow()
        {
            var found = false;
            EnumWindows(
                (hwnd, _) =>
                {
                    var className = new StringBuilder(128);
                    if (IsWindowVisible(hwnd)
                        && GetClassNameW(hwnd, className, className.Capacity) > 0
                        && String.Equals(className.ToString(), TerminalWindowClass, StringComparison.Ordinal))
                    {
                        found = true;
                        return false;
                    }
                    return true;
                },
                IntPtr.Zero);
            return found;
        }

        private delegate Boolean EnumWindowsProc(IntPtr hwnd, IntPtr state);

        [DllImport("user32.dll")]
        private static extern Boolean EnumWindows(EnumWindowsProc callback, IntPtr state);

        [DllImport("user32.dll")]
        private static extern Boolean IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern Int32 GetClassNameW(IntPtr hwnd, StringBuilder className, Int32 maxCount);

        private Boolean ReportTerminalUnavailable(String detail)
        {
            PluginLog.Warning($"WindowsPlatformBridge: {detail} — Windows Terminal is required for this action");
            try { this.TerminalUnavailable?.Invoke(detail); }
            catch (Exception ex) { PluginLog.Warning(ex, "WindowsPlatformBridge: terminal failure notice failed"); }
            return false;
        }
    }
}
