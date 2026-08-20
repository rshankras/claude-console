namespace Loupedeck.ClaudeConsolePlugin.Agents
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// The Windows state transport: codex's own rollout transcript, read directly.
    ///
    /// WHY THIS EXISTS. Codex's hook runner creates no process on Windows — proven on hardware
    /// with a probe executable that logs every invocation and cannot exit nonzero, never invoked
    /// while codex reported "hook exited with code 1" (docs/spike-windows-codex-hooks.md). So on
    /// that platform there is no hook to carry state, and the keypad would show sessions that
    /// never change. The rollout JSONL codex writes for every session carries the same edges:
    /// task_started when a turn begins, task_complete when it ends.
    ///
    /// WHAT IT IS NOT. It is not a second state format. Each event is translated into the SAME
    /// envelope the hook writes, so CodexStateReader, the session grid and every key are
    /// untouched — the transport changes, the contract does not.
    ///
    /// THE CONTRACT WITH AN UNSTABLE FORMAT. Codex documents rollout JSONL as unstable, which is
    /// why <see cref="AgentCapabilities.BestEffortContext"/> exists. This reader inherits that
    /// posture exactly: a line that does not parse, a shape that changed, a file that vanished
    /// mid-read — all mean NO UPDATE, never a guessed one. A keypad that stops updating is a
    /// disappointment; a keypad that lies is a bug.
    /// </summary>
    internal sealed class CodexRolloutBridge
    {
        // A turn's events are small and frequent; we only ever want what arrived since the last
        // poll. The cap bounds a first sighting (or a file that grew while we weren't looking).
        private const Int32 MaxCatchUpBytes = 64 * 1024;

        private readonly String _sessionsRoot;
        private readonly String _ipcSessionsDir;

        /// <summary>How far into each rollout file we have already read.</summary>
        private readonly Dictionary<String, Int64> _offsets = new Dictionary<String, Int64>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rollout file → the session key it was matched to, so a pairing sticks.</summary>
        private readonly Dictionary<String, String> _claims = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

        public CodexRolloutBridge(String sessionsRoot = null, String ipcSessionsDir = null)
        {
            this._sessionsRoot = sessionsRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
            this._ipcSessionsDir = ipcSessionsDir ?? IpcPaths.SessionsDir;
        }

        /// <summary>
        /// Today's date, injectable because the bridge only looks at today's and yesterday's
        /// rollout directories — a session that spans midnight still has a live file under
        /// yesterday, and everything older is dead weight on every poll.
        /// </summary>
        internal Func<DateTime> Now { get; set; } = () => DateTime.Now;

        /// <summary>
        /// The live sessions discovery found, as (key, startTime). Set by the caller each poll;
        /// used to attach a rollout file to the process it belongs to (see <see cref="KeyFor"/>).
        /// </summary>
        internal IReadOnlyList<(String Key, DateTime Start)> LiveSessions { get; set; } =
            Array.Empty<(String, DateTime)>();

        /// <summary>
        /// Read whatever has been appended since the last call and write the resulting state.
        /// Returns how many envelopes were written — for tests and logging, not for control flow.
        /// Never throws: a transport that breaks the plugin is worse than one that reports nothing.
        /// </summary>
        public Int32 Poll()
        {
            var written = 0;

            try
            {
                foreach (var file in this.CurrentRolloutFiles())
                {
                    written += this.PollOne(file);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, "CodexRolloutBridge: poll failed");
            }

            return written;
        }

        /// <summary>
        /// Rollout files worth reading: today's and yesterday's directories only. Codex lays them
        /// out as sessions/&lt;yyyy&gt;/&lt;MM&gt;/&lt;dd&gt;/rollout-*.jsonl.
        /// </summary>
        internal IEnumerable<String> CurrentRolloutFiles()
        {
            var today = this.Now().Date;

            foreach (var day in new[] { today, today.AddDays(-1) })
            {
                var dir = Path.Combine(
                    this._sessionsRoot,
                    day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));

                if (!Directory.Exists(dir))
                {
                    continue;
                }

                String[] files;
                try
                {
                    files = Directory.GetFiles(dir, "rollout-*.jsonl");
                }
                catch (Exception ex)
                {
                    PluginLog.Verbose(ex, $"CodexRolloutBridge: cannot list {dir}");
                    continue;
                }

                foreach (var f in files)
                {
                    yield return f;
                }
            }
        }

        private Int32 PollOne(String path)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists)
                {
                    return 0;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, $"CodexRolloutBridge: cannot stat {path}");
                return 0;
            }

            var known = this._offsets.TryGetValue(path, out var offset) ? offset : -1;

            // First sighting: start from the tail, not the beginning. Replaying a whole day of a
            // long session would walk the keypad through hours of stale busy/idle transitions.
            if (known < 0)
            {
                known = Math.Max(0, info.Length - MaxCatchUpBytes);
            }

            // Truncated or rotated underneath us — start over from where it now ends.
            if (info.Length < known)
            {
                known = 0;
            }

            if (info.Length == known)
            {
                return 0;
            }

            String text;
            Int64 newOffset;
            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                stream.Seek(known, SeekOrigin.Begin);
                var take = (Int32)Math.Min(MaxCatchUpBytes, stream.Length - known);
                var buffer = new Byte[take];
                var read = stream.Read(buffer, 0, take);
                text = Encoding.UTF8.GetString(buffer, 0, read);
                newOffset = known + read;
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, $"CodexRolloutBridge: cannot read {path}");
                return 0;
            }

            // A trailing fragment means the writer is mid-line: rewind the offset to the last
            // complete newline so the fragment is re-read whole next poll, never parsed in half.
            var lastNewline = text.LastIndexOf('\n');
            if (lastNewline < 0)
            {
                return 0;   // nothing complete yet; leave the offset where it was
            }

            var complete = text.Substring(0, lastNewline);
            this._offsets[path] = known + Encoding.UTF8.GetByteCount(complete) + 1;

            // Only the LAST recognised event of this batch matters: the keys show a state, not a
            // history, and a batch containing start-then-complete means the turn is over.
            String activity = null;
            foreach (var line in complete.Split('\n'))
            {
                var mapped = ActivityFor(line);
                if (mapped != null)
                {
                    activity = mapped;
                }
            }

            if (activity == null)
            {
                return 0;
            }

            return this.WriteState(path, activity) ? 1 : 0;
        }

        /// <summary>
        /// The event → activity mapping, and the whole of this bridge's knowledge of codex's
        /// format. Anything else — including events we have never seen — maps to null, which
        /// means "no update", never a default.
        /// </summary>
        internal static String ActivityFor(String line)
        {
            if (String.IsNullOrWhiteSpace(line) || !line.Contains("task_", StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                // The interesting type lives under payload on an event_msg record, while the root
                // carries the record kind ("event_msg"). Consider BOTH rather than taking the
                // first non-null — the root's type is always present, so a ?? chain would never
                // reach the payload and nothing would ever map.
                var mapped = Map(TypeOf(root));
                if (mapped != null)
                {
                    return mapped;
                }

                return root.TryGetProperty("payload", out var payload) ? Map(TypeOf(payload)) : null;
            }
            catch (JsonException)
            {
                // A half-written or reshaped line is not an error — it is simply not information.
                return null;
            }
        }

        /// <summary>The only two event names this bridge knows. Everything else: no update.</summary>
        private static String Map(String type) =>
            type switch
            {
                "task_started" => CodexStateBridge.BusyEvent,
                "task_complete" => CodexStateBridge.IdleEvent,
                _ => null,
            };

        private static String TypeOf(JsonElement element) =>
            element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("type", out var t)
            && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;

        /// <summary>
        /// Write the envelope the hook would have written, so every reader above stays unchanged.
        /// The shared file is always written (the grid's fallback when it has no key match yet);
        /// the per-session file only when this rollout could be attached to a live process.
        /// </summary>
        private Boolean WriteState(String rolloutPath, String activityEvent)
        {
            var envelope =
                "{\"schema\":1,\"agent\":\"codex-cli\",\"event\":\"" + activityEvent + "\",\"ts\":" +
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ",\"payload\":null}\n";

            try
            {
                Directory.CreateDirectory(this._ipcSessionsDir);
                WriteAtomic(Path.Combine(this._ipcSessionsDir, "shared.json"), envelope);

                var key = this.KeyFor(rolloutPath);
                if (key != null)
                {
                    WriteAtomic(Path.Combine(this._ipcSessionsDir, key + ".json"), envelope);
                }

                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, "CodexRolloutBridge: cannot write state");
                return false;
            }
        }

        /// <summary>
        /// Attach a rollout file to the session key of the process that owns it.
        ///
        /// Neither side knows the other: the rollout carries a thread id and cwd, the grid keys on
        /// pid + start time. What they share is WHEN they began — a rollout file is created as its
        /// codex process starts. So: the live session whose start time is nearest the file's
        /// creation, claims stick once made, and an ambiguous match claims NOTHING and leaves the
        /// shared file to do its job. A wrong per-session claim would put another session's state
        /// on a key, which is worse than a key that waits.
        /// </summary>
        internal String KeyFor(String rolloutPath)
        {
            if (this._claims.TryGetValue(rolloutPath, out var claimed))
            {
                return this.LiveSessions.Any(s => s.Key == claimed) ? claimed : null;
            }

            var live = this.LiveSessions;
            if (live.Count == 0)
            {
                return null;
            }

            DateTime created;
            try
            {
                created = new FileInfo(rolloutPath).CreationTimeUtc;
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, $"CodexRolloutBridge: cannot read creation time of {rolloutPath}");
                return null;
            }

            var unclaimed = live.Where(s => !this._claims.ContainsValue(s.Key)).ToList();
            if (unclaimed.Count == 0)
            {
                return null;
            }

            // One unclaimed session is the common case and needs no arithmetic at all.
            if (unclaimed.Count == 1)
            {
                this._claims[rolloutPath] = unclaimed[0].Key;
                return unclaimed[0].Key;
            }

            var ordered = unclaimed
                .OrderBy(s => Math.Abs((s.Start.ToUniversalTime() - created).TotalSeconds))
                .ToList();

            var best = Math.Abs((ordered[0].Start.ToUniversalTime() - created).TotalSeconds);
            var runnerUp = Math.Abs((ordered[1].Start.ToUniversalTime() - created).TotalSeconds);

            // Too close to call: two sessions started within a second of each other. The shared
            // file still carries the state; a coin-flip claim would not.
            if (Math.Abs(best - runnerUp) < 1.0)
            {
                return null;
            }

            this._claims[rolloutPath] = ordered[0].Key;
            return ordered[0].Key;
        }

        private static void WriteAtomic(String path, String content)
        {
            var tmp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
    }
}
