namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    using Loupedeck.ClaudeConsolePlugin.Models;

    /// <summary>
    /// Persisted slot assignments. Slots are keyed by string ("1".."6") to keep the JSON readable
    /// and to match Vizhi's on-disk shape.
    /// </summary>
    internal class RegistryRecord
    {
        [JsonPropertyName("schema")]
        public Int32 Schema { get; set; } = 1;

        [JsonPropertyName("slots")]
        public Dictionary<String, String> Slots { get; set; } = new Dictionary<String, String>(StringComparer.Ordinal);

        [JsonPropertyName("focused_session")]
        public String FocusedSession { get; set; }
    }

    /// <summary>
    /// The session grid: which Claude Code sessions exist, and which LCD key each one owns.
    ///
    /// Two sources are merged every poll:
    ///   • per-tab statusline + activity files (rich: project, context %, state — but only written
    ///     when Claude actually renders, so a quiet session goes silent for minutes)
    ///   • a `ps` scan (authoritative about liveness, and nothing else)
    /// Neither alone is enough: files alone can't tell a finished session from a quiet one, and `ps`
    /// alone can't name the project.
    ///
    /// Slot assignment is deliberately STABLE — a session keeps the key it was given for as long as
    /// it lives. When session 2 of 3 exits, the other two do not shuffle under your fingers; the
    /// freed key is simply reused by the next session to appear. (Ported from Vizhi's
    /// VizhiActionRouter.NormalizeRegistry.)
    /// </summary>
    public class SessionRegistry
    {
        /// <summary>
        /// Whose state files these are. The registry never parses an agent's format itself; it asks.
        /// Defaults to NoAgentAdapter so an unconfigured registry reads nothing rather than
        /// misreading another agent's documents.
        /// </summary>
        internal Agents.IAgentAdapter Agent { get; set; } = new Agents.NoAgentAdapter();

        public const Int32 SlotCount = 6;

        // When the plugin last sent Escape to a session (#30). Kept here rather than in BridgeManager
        // because BOTH readers of activity need it — the grid for the session keys, the Status key
        // for the hourglass — and they must not disagree about the same session again.
        private readonly Dictionary<String, Int64> _interrupts = new Dictionary<String, Int64>(StringComparer.Ordinal);

        // Directory hints from live process discovery; authoritative hook paths win.
        internal IReadOnlyDictionary<String, String> DiscoveredProjectDirs { get; set; }

        // UTC start of each live session's CLI process, from the same discovery pass; null
        // where the platform does not report it. Only ever a tie-breaker (see Reroute…).
        internal IReadOnlyDictionary<String, DateTime> DiscoveredSessionStarts { get; set; }

        // The last state each live terminal reported for ITSELF — its file's folder matched
        // its process's folder. Shown in its place when its file turns out to hold another
        // session's event (the shared-daemon case below), so the key neither goes blank nor
        // wears the other session's approval.
        private readonly Dictionary<String, GridSession> _ownStates =
            new Dictionary<String, GridSession>(StringComparer.Ordinal);

        // Source files already examined, by their write time: the same misplaced envelope is
        // not re-matched and its target not re-read on every 500 ms poll.
        private readonly Dictionary<String, DateTime> _examinedSources =
            new Dictionary<String, DateTime>(StringComparer.Ordinal);
        private readonly HashSet<String> _routingLogged = new HashSet<String>(StringComparer.Ordinal);

        /// <summary>Test seam: how many distinct routing notices have been logged (one per session and target).</summary>
        internal Int32 RoutingNoticesLogged => _routingLogged.Count;

        // A session starts within moments of its CLI process; anything further apart is a resumed
        // conversation in an older process, which start times cannot place. Same bound as the
        // rollout bridge uses for the same question.
        private const Double MaxStartSkewSeconds = 120.0;

        // Test seam for the five-second post-Escape quiet window. Production always uses wall time.
        internal Func<Int64> NowUnix { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // A Yes/No press may resolve a permission menu without Codex emitting a following hook
        // event. Remember the exact approval-source write we answered so the next 500ms refresh
        // does not resurrect its stale badge. A later file write is a new event and is shown.
        private readonly Dictionary<String, Int64> _acknowledgedApprovals =
            new Dictionary<String, Int64>(StringComparer.Ordinal);

        private readonly Object _lock = new Object();
        private RegistryRecord _registry = new RegistryRecord();
        private Dictionary<String, GridSession> _sessions = new Dictionary<String, GridSession>(StringComparer.Ordinal);
        private String[] _slots = new String[SlotCount];   // index 0 = slot 1; holds TTYs

        // Directories are injected rather than read from IpcPaths statically so the tests can drive a
        // throwaway root. They must NOT default to the live one in a test: this class deletes files
        // it considers dead, and pointed at /tmp/claude-console that would destroy a running
        // session's state.
        private readonly String _sessionsDir;
        private readonly String _activityDir;
        private readonly String _registryFile;

        // Last result of the `ps` scan, reused on the polls that don't run one.
        private IReadOnlyCollection<String> _lastLiveTtys;

        // The last project each live session told us about. A session's state file is its own
        // memory, and it is only rewritten when the session DOES something — so anything that loses
        // that file mid-life (a prune, a cleared /tmp, a half-written read) used to erase the
        // project name, and the key fell back to the agent's name. Showing "Claude Code" where a
        // real folder name had been reads as a different session, which is worse than a stale label.
        //
        // Kept for as long as the TTY is alive and dropped the moment it is reaped, so a REUSED tab
        // can never inherit the previous session's project — the case that makes a remembered name
        // a lie rather than a convenience.
        private readonly Dictionary<String, String> _lastKnownProject =
            new Dictionary<String, String>(StringComparer.Ordinal);

        public SessionRegistry()
            : this(IpcPaths.SessionsDir, IpcPaths.ActivityDir, IpcPaths.RegistryFile)
        {
        }

        internal SessionRegistry(String sessionsDir, String activityDir, String registryFile)
        {
            _sessionsDir = sessionsDir;
            _activityDir = activityDir;
            _registryFile = registryFile;
        }

        /// <summary>
        /// Record that the plugin just sent Escape to <paramref name="tty"/>. The stall rule treats
        /// this as corroboration and stops waiting the full quiet window — see ActivityStall.
        /// </summary>
        internal void NoteInterrupt(String tty)
        {
            if (String.IsNullOrEmpty(tty))
            {
                return;
            }

            lock (_lock)
            {
                _interrupts[tty] = this.NowUnix();
            }
        }

        /// <summary>When the plugin last sent Escape to this session, or null.</summary>
        internal Int64? InterruptedAt(String tty)
        {
            if (String.IsNullOrEmpty(tty))
            {
                return null;
            }

            lock (_lock)
            {
                return _interrupts.TryGetValue(tty, out var at) ? at : (Int64?)null;
            }
        }

        /// <summary>
        /// The keypad answered this session's permission prompt, so the captured payload no longer
        /// describes anything pending. An approval clears itself when the tool runs (PostToolUse
        /// fires within ~170 ms); a rejection fires no hook at all, so the Yes dot and the "Allow?"
        /// bar stayed lit until the session's NEXT prompt (#60). Claude stores the payload in a
        /// separate pending file, which is removed. Codex stores it in the session event itself, so
        /// that exact file version is acknowledged until Codex writes a new event. Returns whether
        /// anything was pending to clear and repaints only when it was.
        /// </summary>
        internal Boolean ClearPendingApproval(String tty)
        {
            if (String.IsNullOrEmpty(tty))
            {
                return false;
            }

            var pendingPath = this.PendingFor(tty);
            var answeredVersion = this.ApprovalSourceVersion(tty);
            TryDelete(pendingPath);

            Boolean cleared;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(tty, out var session) || String.IsNullOrEmpty(session.PendingTool))
                {
                    return false;
                }

                if (!session.ApprovalInSessionState)
                {
                    _acknowledgedApprovals.Remove(tty);
                }
                else
                {
                    _acknowledgedApprovals[tty] = answeredVersion;
                }
                session.PendingTool = null;
                session.PendingCommand = null;
                ClearApprovalMetadata(session);
                session.Risk = ApprovalRisk.None;
                // Claude's separate activity file still owns its state; changing it here would
                // cause a false ready->waiting repaint on the next poll. Codex's approval event is
                // the state source itself, so suppress its stale waiting state with the payload.
                if (session.ApprovalInSessionState && session.State == "waiting")
                {
                    session.State = "ready";
                }
                cleared = true;
            }

            if (cleared)
            {
                OnGridChanged?.Invoke();
            }

            return cleared;
        }

        private String StateFor(String tty) => Path.Combine(_sessionsDir, tty + ".json");
        private String ActivityFor(String tty) => Path.Combine(_activityDir, tty + ".json");
        private String PendingFor(String tty) => Path.Combine(_activityDir, "pending-" + tty + ".json");

        /// <summary>Raised when something that changes a key's APPEARANCE changed.</summary>
        public event Action OnGridChanged;

        /// <summary>Sessions by TTY, as of the last refresh.</summary>
        public IReadOnlyDictionary<String, GridSession> Sessions
        {
            get { lock (_lock) { return new Dictionary<String, GridSession>(_sessions, StringComparer.Ordinal); } }
        }

        /// <summary>
        /// TTY of the session pinned by a session-key press, or null when the keys follow the
        /// frontmost tab. Persisted alongside the slot assignments so a plugin reload — which
        /// happens on every rebuild and every upgrade — doesn't silently drop your selection.
        /// </summary>
        public String FocusedSession
        {
            get { lock (_lock) { return _registry.FocusedSession; } }
            set
            {
                lock (_lock)
                {
                    if (String.Equals(_registry.FocusedSession, value, StringComparison.Ordinal))
                    {
                        return;   // the poll asks constantly; only a real change is worth a write
                    }
                    _registry.FocusedSession = value;
                    this.Persist();
                }
            }
        }

        /// <summary>The session on a 1-based slot, or null when that key is empty.</summary>
        public GridSession SlotSession(Int32 slot)
        {
            lock (_lock)
            {
                if (slot < 1 || slot > SlotCount)
                {
                    return null;
                }
                var tty = _slots[slot - 1];
                return tty != null && _sessions.TryGetValue(tty, out var s) ? s : null;
            }
        }

        /// <summary>All live sessions, in slot order.</summary>
        public IReadOnlyList<GridSession> LiveSessions()
        {
            lock (_lock)
            {
                return _slots.Where(t => t != null && _sessions.ContainsKey(t))
                             .Select(t => _sessions[t])
                             .ToList();
            }
        }

        // ------------------------------------------------------------------------------------------
        // Refresh — called from the bridge poll. `liveTtys` is null when the process scan didn't run
        // on this tick, in which case liveness is left as-is rather than being guessed at.
        // ------------------------------------------------------------------------------------------
        public void Refresh(IReadOnlyCollection<String> liveTtys)
        {
            var changed = false;

            lock (_lock)
            {
                var previousVisual = String.Join(";", _slots.Select(t =>
                    t != null && _sessions.TryGetValue(t, out var s) ? s.VisualKey : ""));

                var next = ReadSessionsFromDisk();

                // The `ps` scan only runs every 4th poll. On the polls in between, reuse the last
                // known set rather than treating "no scan" as "nothing is alive" — otherwise
                // provisional sessions (a live tab with no state file yet) vanish and reappear
                // twice a second, the keys flicker, and a press can land on a momentarily empty slot.
                liveTtys ??= _lastLiveTtys;
                if (liveTtys != null)
                {
                    _lastLiveTtys = liveTtys;
                }

                this.RerouteMisattributedStates(next, liveTtys);

                if (liveTtys != null)
                {
                    // `ps` is the authority on which tabs still exist. Reap the rest — this is what
                    // replaces guessing from file age, and it's why a closed tab clears in ~2s.
                    foreach (var dead in next.Keys.Where(t => !liveTtys.Contains(t)).ToList())
                    {
                        next.Remove(dead);
                        _lastKnownProject.Remove(dead);   // a reused tab must not inherit this name
                        _ownStates.Remove(dead);
                        _examinedSources.Remove(dead);
                        ReapFiles(dead);
                    }

                    // Provisional sessions have no disk file to reap above. Forget their names
                    // too, otherwise a recycled tty inherits the previous process's project.
                    foreach (var dead in _lastKnownProject.Keys.Where(t => !liveTtys.Contains(t)).ToList())
                    {
                        _lastKnownProject.Remove(dead);
                    }

                    // A live tab with no state file yet still deserves a key immediately.
                    foreach (var tty in liveTtys.Where(t => !next.ContainsKey(t)))
                    {
                        next[tty] = new GridSession
                        {
                            SessionKey = tty,
                            // If this session has told us its project before, keep showing it: the
                            // file may be missing, but the fact is not. Only a session that has NEVER
                            // reported gets a null here, and its key falls back to whichever agent is
                            // running (SessionSlotCommand). Naming one here is how a Codex grid ended
                            // up labelled "Claude".
                            Project = _lastKnownProject.TryGetValue(tty, out var remembered) ? remembered : null,
                            State = "ready",
                            IsProvisional = true,
                            UpdatedAt = DateTime.UtcNow,
                        };
                    }
                }

                foreach (var s in next.Values)
                {
                    if (String.IsNullOrWhiteSpace(s.ProjectDir)
                        && DiscoveredProjectDirs != null
                        && DiscoveredProjectDirs.TryGetValue(s.SessionKey, out var directory))
                    {
                        s.ProjectDir = directory;
                        s.Project = ProjectName(directory);
                    }
                    if (!String.IsNullOrWhiteSpace(s.Project))
                    {
                        _lastKnownProject[s.SessionKey] = s.Project;
                    }
                }

                _sessions = next;
                changed |= this.AssignSlots();

                var nowVisual = String.Join(";", _slots.Select(t =>
                    t != null && _sessions.TryGetValue(t, out var s) ? s.VisualKey : ""));
                changed |= nowVisual != previousVisual;
            }

            if (changed)
            {
                OnGridChanged?.Invoke();
            }
        }

        // Stable compaction: keep every session in the slot it already holds, then fill the gaps with
        // newcomers oldest-first, then drop anything past SlotCount. Returns true when the mapping or
        // the persisted registry changed.
        private Boolean AssignSlots()
        {
            var before = (String[])_slots.Clone();

            // 1. Existing holders keep their slot (if still alive).
            var taken = new HashSet<String>(StringComparer.Ordinal);
            for (var i = 0; i < SlotCount; i++)
            {
                var tty = _slots[i];
                if (tty != null && _sessions.ContainsKey(tty) && taken.Add(tty))
                {
                    continue;
                }
                _slots[i] = null;
            }

            // 2. Newcomers fill the lowest free slots, oldest first, so key order reflects the order
            //    sessions were started rather than whichever file happened to be written last.
            var newcomers = _sessions.Values
                .Where(s => !taken.Contains(s.SessionKey))
                .OrderBy(s => s.UpdatedAt)
                .ThenBy(s => s.SessionKey, StringComparer.Ordinal)
                .ToList();

            foreach (var session in newcomers)
            {
                var free = Array.IndexOf(_slots, null);
                if (free < 0)
                {
                    break;   // more than SlotCount sessions — the extras stay unslotted
                }
                _slots[free] = session.SessionKey;
            }

            var slotsChanged = !before.SequenceEqual(_slots, StringComparer.Ordinal);
            if (slotsChanged)
            {
                this.Persist();
            }
            return slotsChanged;
        }

        // ------------------------------------------------------------------------------------------
        // Disk
        // ------------------------------------------------------------------------------------------
        private Dictionary<String, GridSession> ReadSessionsFromDisk()
        {
            var sessions = new Dictionary<String, GridSession>(StringComparer.Ordinal);
            if (!Directory.Exists(_sessionsDir))
            {
                return sessions;
            }

            foreach (var file in SafeFiles(_sessionsDir))
            {
                var tty = Path.GetFileNameWithoutExtension(file);
                if (tty == IpcPaths.SharedName)
                {
                    continue;   // the fallback file is a duplicate of some tab, not a tab of its own
                }

                var session = this.ReadSessionFile(file, tty);
                if (session != null)
                {
                    sessions[tty] = session;
                }
            }

            return sessions;
        }

        /// <summary>One state file, one session; null when it cannot be read or parsed.</summary>
        private GridSession ReadSessionFile(String file, String tty)
        {
            {
                // The AGENT owns its format. Parsing here is what made a Codex hook envelope
                // deserialise as Claude's statusline with every field null: no error, no missing
                // file, just a session stuck on "ready" wearing a project name it never reported.
                String raw;
                DateTime updatedAt;
                try
                {
                    // Capture before reading: a replacement after the read must not give an old
                    // approval the timestamp of newer bytes we have not parsed yet.
                    updatedAt = LastWrite(file);
                    raw = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    return null;
                }

                var state = this.Agent.ParseSessionState(raw);
                if (state == null)
                {
                    return null;
                }

                String activityRaw = null;
                var activityWritten = DateTime.MinValue;
                var activity = state.Activity == null
                    ? ReadActivityState(tty, state.TranscriptPath, out activityRaw, out activityWritten)
                    : NormalizeActivityState(
                        tty,
                        state.Activity,
                        state.ActivityTs ?? new DateTimeOffset(updatedAt.ToUniversalTime()).ToUnixTimeSeconds(),
                        state.TranscriptPath,
                        state.TranscriptWritesOnInterrupt,
                        state.TranscriptActivityTs);

                var session = new GridSession
                {
                    SessionKey = tty,
                    Project = ProjectName(state.ProjectDir),
                    ProjectDir = state.ProjectDir,
                    SessionId = state.SessionId,
                    SessionName = state.SessionName,
                    CtxPercent = state.CtxPercent,
                    TranscriptPath = state.TranscriptPath,
                    State = activity,
                    UpdatedAt = updatedAt,
                    StateSourcePath = state.Activity == null ? this.ActivityFor(tty) : file,
                    StateSourceRaw = state.Activity == null ? activityRaw : raw,
                    StateSourceWrittenAtUtc = state.Activity == null ? activityWritten : updatedAt,
                };
                session.StateObservationStartedAtUtc = ObservationTimes(session.StateSourceRaw, approval: false).Started;

                if (state.ReportsApproval)
                {
                    session.ApprovalInSessionState = true;
                    session.PendingTool = state.PendingTool;
                    session.PendingCommand = state.PendingCommand;
                    session.ApprovalObservedAtUtc = String.IsNullOrEmpty(state.PendingTool) ? null : updatedAt;
                    session.Risk = state.Risk;
                    if (!String.IsNullOrEmpty(state.PendingTool))
                    {
                        var times = ObservationTimes(raw, approval: true);
                        session.ApprovalObservationStartedAtUtc = times.Started;
                        session.ApprovalSourceEventAtUtc = times.Event;
                        session.ApprovalSourcePath = file;
                        session.ApprovalSourceRaw = raw;
                    }
                }
                else
                {
                    this.ApplyPendingApproval(session);
                }

                // A fork/resume can end a conversation while the same CLI process stays alive.
                // Never retain its context or id as if it described the next conversation. The
                // liveness pass below removes exited processes; a live one remains provisional
                // until its next authoritative hook, with its known project label intact.
                if (this.Agent.Id == "codex-cli" && state.Activity == "dead")
                {
                    session.SessionId = null;
                    session.TranscriptPath = null;
                    session.CtxPercent = null;
                    session.IsProvisional = true;
                }

                this.ApplyApprovalAcknowledgement(session);

                return session;
            }
        }

        /// <summary>
        /// Put a hook envelope back on the terminal it belongs to.
        ///
        /// Codex 0.158 runs every session inside one shared app-server daemon, and its hooks run
        /// there. The hook finds "its" terminal by walking the process ancestry, which for a
        /// daemon ends at the terminal that started it — so EVERY session's events land in that
        /// tty's file: its key shows the other session's project and approval, and Yes would type
        /// into the wrong tab. Seen live on 2026-09-28 (AlertWala's approval on the claude-console
        /// key); the same limitation was deferred as "background-server routing" in #103. Once
        /// that terminal is gone the daemon has no terminal in its ancestry and the hook writes
        /// shared.json instead — the same evidence with no key of its own.
        ///
        /// The plugin independently knows each live CLI's real folder and start time from the
        /// process table. A file whose event names a folder its own terminal is not in is re-keyed
        /// to the one live terminal that IS in that folder (start times break a tie between two;
        /// a terminal already known to run a different conversation never qualifies), and the
        /// envelope is written under that key so approval currency, acknowledgement and Yes/No
        /// see exactly what the hook would have written there. With no unique match the event is
        /// held back — the keys never guess — and the file's own terminal keeps its last own
        /// state. When no other terminal is in that folder and nothing says the event is another
        /// conversation, the hook's folder stays authoritative over the process hint, as before:
        /// a resumed session can report a folder its process no longer sits in.
        /// </summary>
        private void RerouteMisattributedStates(Dictionary<String, GridSession> next, IReadOnlyCollection<String> liveTtys)
        {
            var dirs = this.DiscoveredProjectDirs;
            if (!this.Agent.Capabilities.HooksMayReportAnotherTerminal || dirs == null || liveTtys == null)
            {
                return;
            }

            var sources = next.Select(p => (Tty: p.Key, State: p.Value, Shared: false)).ToList();
            var sharedFile = this.StateFor(IpcPaths.SharedName);
            if (File.Exists(sharedFile))
            {
                var shared = this.ReadSessionFile(sharedFile, IpcPaths.SharedName);
                if (shared != null)
                {
                    sources.Add((IpcPaths.SharedName, shared, true));
                }
            }

            foreach (var (tty, state, shared) in sources)
            {
                if (String.IsNullOrWhiteSpace(state.ProjectDir))
                {
                    continue;
                }

                String ownDir = null;
                if (!shared)
                {
                    if (!dirs.TryGetValue(tty, out ownDir) || String.IsNullOrWhiteSpace(ownDir))
                    {
                        continue;   // no independent view of this terminal: nothing to compare with
                    }
                    if (SamePath(ownDir, state.ProjectDir))
                    {
                        _ownStates[tty] = state;   // routed as the hook meant: this terminal's own news
                        continue;
                    }
                }

                var inFolder = 0;
                var candidates = new List<String>();
                foreach (var live in liveTtys)
                {
                    if (live == tty || !dirs.TryGetValue(live, out var dir) || !SamePath(dir, state.ProjectDir))
                    {
                        continue;
                    }
                    inFolder++;
                    // A terminal whose own file names a different conversation is not this one's.
                    var known = next.TryGetValue(live, out var current) && !current.IsProvisional ? current.SessionId : null;
                    if (!String.IsNullOrEmpty(known) && !String.IsNullOrEmpty(state.SessionId) && known != state.SessionId)
                    {
                        continue;
                    }
                    candidates.Add(live);
                }
                if (candidates.Count > 1)
                {
                    candidates = this.ClosestStart(candidates, state.SessionId);
                }

                // No live terminal is in that folder and nothing says this is another conversation:
                // the hook's folder wins over the process hint, as it always has. Once ANY terminal
                // is in that folder the event is somebody else's, matched or not.
                _ownStates.TryGetValue(tty, out var own);
                var anotherConversation = !shared && own?.SessionId != null && state.SessionId != null && own.SessionId != state.SessionId;
                if (inFolder == 0 && !anotherConversation)
                {
                    if (!shared) { _ownStates[tty] = state; }
                    continue;
                }

                // The same envelope, examined last poll: keep its decision, only refresh the substitute.
                var examined = _examinedSources.TryGetValue(tty, out var seenAt) && seenAt == state.UpdatedAt;
                if (!examined)
                {
                    _examinedSources[tty] = state.UpdatedAt;
                    var where = shared ? "shared.json" : tty + (ownDir == null ? String.Empty : $", whose terminal runs in {ownDir}");
                    if (candidates.Count == 1)
                    {
                        var to = candidates[0];
                        if (this.Materialise(state, to))
                        {
                            var reread = this.ReadSessionFile(this.StateFor(to), to);
                            if (reread != null) { next[to] = reread; }
                        }
                        this.LogRoutingOnce($"{state.SessionId}|{tty}>{to}",
                            $"SessionRegistry: an event for {state.ProjectDir} arrived under {where} — {this.Agent.DisplayName}'s hook runs in a shared daemon and reports the terminal that started it; shown on {to}, the one terminal in that folder");
                    }
                    else
                    {
                        this.LogRoutingOnce($"{state.SessionId}|{tty}|held",
                            $"SessionRegistry: an event for {state.ProjectDir} (session {state.SessionId}) arrived under {where}; {inFolder} live terminal(s) run {this.Agent.DisplayName} in that folder and none can be singled out, so the keys will not guess — answer in the terminal");
                    }
                }

                if (!shared)
                {
                    if (own != null)
                    {
                        next[tty] = OwnCopy(own);
                    }
                    else
                    {
                        next.Remove(tty);   // a live tab with no news of its own yet: provisional, named from its process
                    }
                }
            }
        }

        /// <summary>
        /// Write the envelope under the key it belongs to, keeping the hook's write time so every
        /// reader sees the file the hook would have produced. False when the target already holds
        /// this envelope or newer news of its own.
        /// </summary>
        private Boolean Materialise(GridSession state, String to)
        {
            var target = this.StateFor(to);
            try
            {
                if (File.Exists(target))
                {
                    if (LastWrite(target) > state.UpdatedAt) { return false; }
                    if (File.ReadAllText(target) == state.StateSourceRaw) { return false; }
                }
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, state.StateSourceRaw);
                PrivateFiles.EnsurePrivateFile(temporary);   // owner-only, as the hook writes its own
                File.Move(temporary, target, overwrite: true);
                File.SetLastWriteTimeUtc(target, state.UpdatedAt);
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, $"SessionRegistry: could not re-key an event to {to}");
                return false;
            }
        }

        /// <summary>
        /// Among terminals in the event's folder, the one that started just before the session
        /// did — when the agent's session id carries that time and the platform reports process
        /// starts. Otherwise, or when two are too close to call, the whole list comes back.
        /// </summary>
        private List<String> ClosestStart(List<String> candidates, String sessionId)
        {
            var started = this.Agent.SessionStartedAtUtc(sessionId);
            var starts = this.DiscoveredSessionStarts;
            if (started == null || starts == null)
            {
                return candidates;
            }

            var scored = candidates
                .Where(c => starts.ContainsKey(c))
                .Select(c => (Tty: c, Skew: Math.Abs((started.Value - starts[c]).TotalSeconds)))
                .Where(x => x.Skew <= MaxStartSkewSeconds)
                .OrderBy(x => x.Skew)
                .ToList();
            if (scored.Count == 0 || (scored.Count > 1 && scored[1].Skew - scored[0].Skew < 1.0))
            {
                return candidates;
            }
            return new List<String> { scored[0].Tty };
        }

        // The terminal's last own state, minus anything that could be claimed as a live approval:
        // its file no longer holds it, so a press could not be checked against the source.
        private static GridSession OwnCopy(GridSession own) => new GridSession
        {
            SessionKey = own.SessionKey,
            Project = own.Project,
            ProjectDir = own.ProjectDir,
            SessionId = own.SessionId,
            SessionName = own.SessionName,
            CtxPercent = own.CtxPercent,
            TranscriptPath = own.TranscriptPath,
            State = own.State,
            UpdatedAt = own.UpdatedAt,
            IsProvisional = own.IsProvisional,
            StateObservationStartedAtUtc = own.StateObservationStartedAtUtc,
        };

        private static Boolean SamePath(String a, String b)
        {
            if (String.IsNullOrWhiteSpace(a) || String.IsNullOrWhiteSpace(b)) { return false; }
            // Both come from this machine (a process's cwd and the hook's cwd), so only trailing
            // separators and case need levelling: APFS and NTFS are case-insensitive by default.
            static String Trim(String p) => p.Length > 1 ? p.TrimEnd('/', '\\') : p;
            return String.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase);
        }

        private void LogRoutingOnce(String key, String message)
        {
            if (_routingLogged.Add(key))
            {
                PluginLog.Info(message);
            }
        }

        // "ready" unless the hooks say otherwise. A "busy" whose TRANSCRIPT has gone quiet is
        // reported as ready — see ActivityStall, which also explains why age alone was not enough.
        //
        // The transcript path arrives from the caller because the session's state file has already
        // been read and parsed in that loop: consulting it here would mean a second read and parse
        // of the same file for every session on every poll, which is the cost #27 just finished
        // removing.
        private String ReadActivityState(String tty, String transcriptPath, out String raw, out DateTime written)
        {
            var file = this.ActivityFor(tty);
            raw = null;
            written = DateTime.MinValue;
            ActivityState activity = null;
            try
            {
                var info = new FileInfo(file);
                if (info.Exists && info.Length > 0 && info.Length <= MaxFileBytes)
                {
                    written = info.LastWriteTimeUtc;
                    raw = File.ReadAllText(file);
                    activity = JsonSerializer.Deserialize<ActivityState>(raw);
                }
            }
            catch { }
            if (activity?.State == null)
            {
                return "ready";
            }

            return this.NormalizeActivityState(tty, activity.State, activity.Ts, transcriptPath);
        }

        // Activity may live beside the session (Claude Code) or inside it (Codex). Both paths must
        // apply the same interrupted-turn rule; otherwise Codex's non-null Activity bypasses the
        // exact recovery that clears Claude's stale hourglass (#30).
        private String NormalizeActivityState(
            String tty,
            String activity,
            Int64 activityTs,
            String transcriptPath,
            Boolean transcriptWritesOnInterrupt = false,
            Int64? transcriptActivityTs = null)
        {
            if (ActivityStall.IsStalledBusy(
                    activity,
                    activityTs,
                    transcriptActivityTs ?? ActivityStall.TranscriptMtime(transcriptPath),
                    this.NowUnix(),
                    this.InterruptedAt(tty),
                    transcriptWritesOnInterrupt))
            {
                return "ready";
            }

            return NormaliseActivityWord(activity);
        }

        /// <summary>
        /// The state a session is in, from the word a hook wrote. Hooks write busy | waiting | done;
        /// the grid speaks busy | waiting | ready. "permission" is the PermissionRequest hook's argv
        /// verb — the bash hook translates it to "waiting" before writing, and the Windows exe did
        /// not until the 2.2.1 Windows retest (item 2): with the raw word on disk, no session ever
        /// counted as waiting there, so the pending approval was never read and Yes/No answered
        /// nothing. The exe is fixed too; this makes the plugin right whichever hook wrote the file.
        /// </summary>
        internal static String NormaliseActivityWord(String word) =>
            word switch
            {
                "done" => "ready",
                "permission" => "waiting",
                _ => word,
            };

        // Fill in what (if anything) this session is waiting to be approved. Only meaningful while
        // the session is actually waiting: once it moves on, a leftover pending file must not keep a
        // badge lit, and the hook clears it — this is belt and braces for a missed clear.
        private void ApplyPendingApproval(GridSession session)
        {
            if (session.State != "waiting")
            {
                return;
            }

            var pendingPath = this.PendingFor(session.SessionKey);
            var pending = ReadPendingApproval(pendingPath, out var observedAt, out var raw);
            if (pending == null)
            {
                // Waiting, but nothing is pending — so this is Claude asking for input at an idle
                // prompt (the Notification hook), not a tool blocked on approval. It used to take
                // the amber "answer me" badge anyway, so every session left alone drifted into
                // looking like it needed approving: with three sessions open, all three badged, and
                // the badge stopped meaning anything (#51).
                //
                // The two are cleanly distinguishable and always were: `permission` mode writes the
                // payload AND the waiting state, `Notification` writes only the state. The key still
                // shows its waiting face; it just no longer claims an approval is pending.
                return;
            }

            session.PendingTool = pending.Value.Tool;
            session.PendingCommand = pending.Value.Command;
            session.ApprovalObservedAtUtc = String.IsNullOrEmpty(pending.Value.Tool) ? null : observedAt;
            session.Risk = RiskClassifier.Classify(pending.Value.Tool, pending.Value.Command);
            if (!String.IsNullOrEmpty(pending.Value.Tool))
            {
                var times = ObservationTimes(raw, approval: true);
                session.ApprovalObservationStartedAtUtc = times.Started;
                session.ApprovalSourceEventAtUtc = times.Event;
                session.ApprovalSourcePath = pendingPath;
                session.ApprovalSourceRaw = raw;
            }
        }

        private void ApplyApprovalAcknowledgement(GridSession session)
        {
            if (!_acknowledgedApprovals.TryGetValue(session.SessionKey, out var answeredVersion))
            {
                return;
            }

            if (this.ApprovalSourceVersion(session.SessionKey) != answeredVersion)
            {
                // The agent wrote something new. If it is another PermissionRequest, it deserves
                // a fresh badge even when the tool and command text happen to be identical.
                _acknowledgedApprovals.Remove(session.SessionKey);
                return;
            }

            session.PendingTool = null;
            session.PendingCommand = null;
            ClearApprovalMetadata(session);
            session.Risk = ApprovalRisk.None;
            if (session.State == "waiting")
            {
                session.State = "ready";
            }
        }

        private Int64 ApprovalSourceVersion(String tty)
        {
            var pending = this.PendingFor(tty);
            var source = File.Exists(pending) ? pending : this.StateFor(tty);
            return LastWrite(source).Ticks;
        }

        /// <summary>
        /// Pull the tool name and (for Bash) the shell command out of a captured PermissionRequest
        /// payload. Parsed defensively on purpose: these payloads have demonstrably changed shape
        /// between Claude Code versions, and a badge is never worth throwing on.
        /// </summary>
        internal static (String Tool, String Command)? ReadPendingApproval(String path) =>
            ReadPendingApproval(path, out _, out _);

        private static (String Tool, String Command)? ReadPendingApproval(String path, out DateTime observedAt, out String raw)
        {
            observedAt = DateTime.MinValue;
            raw = null;
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length == 0 || fi.Length > MaxFileBytes)
                {
                    return null;
                }

                var writtenBeforeRead = fi.LastWriteTimeUtc;
                raw = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var tool = root.TryGetProperty("tool_name", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString()
                    : null;

                String command = null;
                if (root.TryGetProperty("tool_input", out var input) && input.ValueKind == JsonValueKind.Object)
                {
                    // `command` is guaranteed only for Bash — tool_input's shape is per-tool.
                    if (input.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String)
                    {
                        command = c.GetString();
                    }
                }

                if (tool == null && command == null)
                {
                    return null;
                }
                observedAt = writtenBeforeRead;
                return (tool, command);
            }
            catch
            {
                return null;   // mid-write or an unfamiliar shape
            }
        }

        private static void ClearApprovalMetadata(GridSession session)
        {
            session.ApprovalObservedAtUtc = null;
            session.ApprovalObservationStartedAtUtc = null;
            session.ApprovalSourceEventAtUtc = null;
            session.ApprovalSourcePath = null;
            session.ApprovalSourceRaw = null;
        }

        private static (DateTime? Started, DateTime? Event) ObservationTimes(String raw, Boolean approval)
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) { return (null, null); }
                var transport = root.TryGetProperty("transport", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() : null;
                if (String.IsNullOrEmpty(transport) || transport == "hook")
                {
                    var started = Timestamp(root, "hookStartedUtcTicks");
                    return (started, started);
                }
                if (transport == "rollout-code-mode" || (!approval && transport == "rollout"))
                {
                    return (Timestamp(root, "observationStartedUtcTicks"), Timestamp(root, "rolloutEventUtcTicks"));
                }
            }
            catch { }
            return (null, null);
        }

        private static DateTime? Timestamp(JsonElement root, String name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var ticks) && ticks > 0 && ticks <= DateTime.MaxValue.Ticks
                ? new DateTime(ticks, DateTimeKind.Utc) : null;

        /// <summary>Press-time revalidation of the exact state the user is about to answer.</summary>
        internal Boolean IsApprovalSourceCurrent(String sessionKey)
        {
            GridSession session;
            lock (_lock)
            {
                if (sessionKey == null || !_sessions.TryGetValue(sessionKey, out session) ||
                    String.IsNullOrEmpty(session.PendingTool)) { return false; }
            }
            if (!SourceMatches(session.ApprovalSourcePath, session.ApprovalSourceRaw, session.ApprovalObservedAtUtc) ||
                (session.StateSourcePath != session.ApprovalSourcePath &&
                 !SourceMatches(session.StateSourcePath, session.StateSourceRaw, session.StateSourceWrittenAtUtc)))
            {
                return false;
            }
            lock (_lock)
            {
                return _sessions.TryGetValue(sessionKey, out var current) && ReferenceEquals(current, session) &&
                    !String.IsNullOrEmpty(current.PendingTool);
            }
        }

        private static Boolean SourceMatches(String path, String raw, DateTime? written)
        {
            if (path == null || raw == null || written == null) { return false; }
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxFileBytes || info.LastWriteTimeUtc != written.Value) { return false; }
                return File.ReadAllText(path) == raw && File.GetLastWriteTimeUtc(path) == written.Value;
            }
            catch { return false; }
        }

        internal static Int32? ContextPercent(ClaudeState state)
        {
            var ctx = state?.ContextWindow;
            if (ctx == null)
            {
                return null;
            }
            // Both of these are null on a session that hasn't used any context yet — report "unknown"
            // rather than a misleading 0%.
            if (ctx.UsedPercentage is > 0)
            {
                return (Int32)Math.Round(ctx.UsedPercentage.Value);
            }
            var used = ctx.TotalInputTokens ?? 0;
            var max = ctx.MaxTokens ?? 0;
            return max > 0 && used > 0 ? (Int32)Math.Round(100.0 * used / max) : (Int32?)null;
        }

        /// <summary>Basename of the project dir, or "Claude" when unknown.</summary>
        /// <summary>
        /// The project label: the working directory's own name. Null when there isn't one — the key
        /// then falls back to the running agent's name (SessionSlotCommand). The engine must not
        /// name an agent here; these two fallbacks used to read "Claude", which is how a Codex grid
        /// borrowed Claude's name for any session that had not reported a directory yet.
        /// </summary>
        internal static String ProjectName(String projectDir)
        {
            if (String.IsNullOrWhiteSpace(projectDir))
            {
                return null;
            }

            // Split on BOTH separators rather than deferring to Path.GetFileName, which only knows
            // the separator of the machine it runs on. A Codex rollout carries the cwd written by
            // whichever platform produced it, so a Mac reading a Windows-authored transcript got
            // the whole `C:\demo\repos\presskit` as the session label instead of `presskit`.
            var trimmed = projectDir.TrimEnd('/', '\\');
            var cut = trimmed.LastIndexOfAny(new[] { '/', '\\' });
            var name = cut < 0 ? trimmed : trimmed.Substring(cut + 1);
            return String.IsNullOrEmpty(name) ? null : name;
        }

        private void ReapFiles(String tty)
        {
            TryDelete(this.StateFor(tty));
            TryDelete(this.ActivityFor(tty));
            TryDelete(this.PendingFor(tty));
            // The tty name will be handed to the next tab that opens; an interrupt we recorded
            // against the old occupant must not follow it (#30). ActivityStall also guards this by
            // timestamp — belt and braces, because the failure is a new session reading as idle.
            lock (_lock) { _interrupts.Remove(tty); }
            lock (_lock) { _acknowledgedApprovals.Remove(tty); }
        }

        // Persist slot→tty so assignments survive a plugin reload (a rebuild shouldn't reshuffle your
        // keys). Written only when the mapping actually changed — the poll runs twice a second.
        private void Persist()
        {
            try
            {
                _registry.Schema = 1;
                _registry.Slots = new Dictionary<String, String>(StringComparer.Ordinal);
                for (var i = 0; i < SlotCount; i++)
                {
                    if (_slots[i] != null)
                    {
                        _registry.Slots[(i + 1).ToString()] = _slots[i];
                    }
                }

                PrivateFiles.EnsurePrivateDirectory(Path.GetDirectoryName(_registryFile));
                var json = JsonSerializer.Serialize(_registry, new JsonSerializerOptions { WriteIndented = true });
                var tmp = _registryFile + ".tmp";
                File.WriteAllText(tmp, json);
                PrivateFiles.EnsurePrivateFile(tmp);
                File.Move(tmp, _registryFile, overwrite: true);
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, "SessionRegistry: could not persist the registry");
            }
        }

        /// <summary>Restore slot assignments written by a previous plugin load.</summary>
        public void LoadPersisted()
        {
            var record = ReadJson<RegistryRecord>(_registryFile);
            if (record?.Schema != 1 || record.Slots == null)
            {
                return;
            }

            lock (_lock)
            {
                _registry = record;
                _slots = new String[SlotCount];
                foreach (var pair in record.Slots)
                {
                    if (Int32.TryParse(pair.Key, out var slot) && slot >= 1 && slot <= SlotCount)
                    {
                        _slots[slot - 1] = pair.Value;
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // Small IO helpers — every one of these is best-effort: the bash writers and this reader race
        // by design, and a torn read must never take the plugin down.
        // ------------------------------------------------------------------------------------------
        private const Int64 MaxFileBytes = 1 << 20;

        private static IEnumerable<String> SafeFiles(String dir)
        {
            try { return Directory.GetFiles(dir, "*.json"); }
            catch { return Array.Empty<String>(); }
        }

        private static DateTime LastWrite(String path)
        {
            try { return File.GetLastWriteTimeUtc(path); }
            catch { return DateTime.MinValue; }
        }

        private static T ReadJson<T>(String path) where T : class
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length == 0 || fi.Length > MaxFileBytes)
                {
                    return null;
                }
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            }
            catch
            {
                return null;   // mid-write or malformed; the next poll will pick it up
            }
        }

        private static void TryDelete(String path)
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }
}
