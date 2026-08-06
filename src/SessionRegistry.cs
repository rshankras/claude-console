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
        public const Int32 SlotCount = 6;

        // A busy session that hasn't been heard from in this long is treated as finished: the Stop
        // hook can be missed (crash, kill -9), and a key stuck on "Working" forever is worse than
        // one that settles to Ready a little early.
        private static readonly TimeSpan StalledBusyAfter = TimeSpan.FromSeconds(45);

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

                if (liveTtys != null)
                {
                    // `ps` is the authority on which tabs still exist. Reap the rest — this is what
                    // replaces guessing from file age, and it's why a closed tab clears in ~2s.
                    foreach (var dead in next.Keys.Where(t => !liveTtys.Contains(t)).ToList())
                    {
                        next.Remove(dead);
                        ReapFiles(dead);
                    }

                    // A live tab with no state file yet still deserves a key immediately.
                    foreach (var tty in liveTtys.Where(t => !next.ContainsKey(t)))
                    {
                        next[tty] = new GridSession
                        {
                            Tty = tty,
                            Project = "Claude",
                            State = "ready",
                            IsProvisional = true,
                            UpdatedAt = DateTime.UtcNow,
                        };
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
                .Where(s => !taken.Contains(s.Tty))
                .OrderBy(s => s.UpdatedAt)
                .ThenBy(s => s.Tty, StringComparer.Ordinal)
                .ToList();

            foreach (var session in newcomers)
            {
                var free = Array.IndexOf(_slots, null);
                if (free < 0)
                {
                    break;   // more than SlotCount sessions — the extras stay unslotted
                }
                _slots[free] = session.Tty;
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

                var state = ReadJson<ClaudeState>(file);
                if (state == null)
                {
                    continue;
                }

                var projectDir = state.Workspace?.ProjectDir ?? state.Workspace?.CurrentDir;
                var session = new GridSession
                {
                    Tty = tty,
                    Project = ProjectName(projectDir),
                    ProjectDir = projectDir,
                    SessionId = state.SessionId,
                    SessionName = state.SessionName,
                    CtxPercent = ContextPercent(state),
                    State = ReadActivityState(tty),
                    UpdatedAt = LastWrite(file),
                };
                this.ApplyPendingApproval(session);
                sessions[tty] = session;
            }

            return sessions;
        }

        // "ready" unless the hooks say otherwise. A "busy" that has gone quiet for too long is
        // reported as ready — see StalledBusyAfter.
        private String ReadActivityState(String tty)
        {
            var file = this.ActivityFor(tty);
            var activity = ReadJson<ActivityState>(file);
            if (activity?.State == null)
            {
                return "ready";
            }

            if (activity.State == "busy")
            {
                var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - activity.Ts;
                if (age > StalledBusyAfter.TotalSeconds)
                {
                    return "ready";
                }
            }

            return activity.State == "done" ? "ready" : activity.State;
        }

        // Fill in what (if anything) this session is waiting to be approved. Only meaningful while
        // the session is actually waiting: once it moves on, a leftover pending file must not keep a
        // badge lit, and the hook clears it — this is belt and braces for a missed clear.
        private void ApplyPendingApproval(GridSession session)
        {
            if (session.State != "waiting")
            {
                return;
            }

            var pending = ReadPendingApproval(this.PendingFor(session.Tty));
            if (pending == null)
            {
                // Waiting, but we don't know what for — an older Claude Code with no
                // PermissionRequest hook, or an idle prompt. Still worth an "answer me" badge.
                session.Risk = ApprovalRisk.Normal;
                return;
            }

            session.PendingTool = pending.Value.Tool;
            session.PendingCommand = pending.Value.Command;
            session.Risk = RiskClassifier.Classify(pending.Value.Tool, pending.Value.Command);
        }

        /// <summary>
        /// Pull the tool name and (for Bash) the shell command out of a captured PermissionRequest
        /// payload. Parsed defensively on purpose: these payloads have demonstrably changed shape
        /// between Claude Code versions, and a badge is never worth throwing on.
        /// </summary>
        internal static (String Tool, String Command)? ReadPendingApproval(String path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length == 0 || fi.Length > MaxFileBytes)
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
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

                return tool == null && command == null ? null : (tool, command);
            }
            catch
            {
                return null;   // mid-write or an unfamiliar shape
            }
        }

        internal static Int32? ContextPercent(ClaudeState state)
        {
            var ctx = state?.ContextWindow;
            if (ctx == null)
            {
                return null;
            }
            if (ctx.UsedPercentage > 0)
            {
                return (Int32)Math.Round(ctx.UsedPercentage);
            }
            return ctx.MaxTokens > 0 ? (Int32)Math.Round(100.0 * ctx.TotalInputTokens / ctx.MaxTokens) : (Int32?)null;
        }

        /// <summary>Basename of the project dir, or "Claude" when unknown.</summary>
        internal static String ProjectName(String projectDir)
        {
            if (String.IsNullOrWhiteSpace(projectDir))
            {
                return "Claude";
            }
            var name = Path.GetFileName(projectDir.TrimEnd('/'));
            return String.IsNullOrEmpty(name) ? "Claude" : name;
        }

        private void ReapFiles(String tty)
        {
            TryDelete(this.StateFor(tty));
            TryDelete(this.ActivityFor(tty));
            TryDelete(this.PendingFor(tty));
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
