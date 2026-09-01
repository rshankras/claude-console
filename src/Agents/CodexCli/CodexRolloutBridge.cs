namespace Loupedeck.ClaudeConsolePlugin.Agents
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
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
        // Normal polls stay cheap. A first sighting gets a wider bounded tail because a plugin
        // reload can occur mid-turn after megabytes of tool output have followed task_started.
        private const Int32 MaxCatchUpBytes = 64 * 1024;
        private const Int32 MaxInitialCatchUpBytes = 8 * 1024 * 1024;
        private const Int32 MaxMetadataBytes = 1024 * 1024;
        private const Double MaxStartSkewSeconds = 120.0;

        private readonly String _sessionsRoot;
        private readonly String _ipcSessionsDir;

        /// <summary>How far into each rollout file we have already read.</summary>
        private readonly Dictionary<String, Int64> _offsets = new Dictionary<String, Int64>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rollout file → the session key it was matched to, so a pairing sticks.</summary>
        private readonly Dictionary<String, String> _claims = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rollout file → its last-reported cwd, so every envelope can carry the project.</summary>
        private readonly Dictionary<String, String> _cwds = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rollout file -> last lifecycle edge, so ordinary growth can refresh busy.</summary>
        private readonly Dictionary<String, String> _activities = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rollout metadata is immutable; read its first record at most once per file.</summary>
        private readonly Dictionary<String, (DateTime? StartedUtc, String Cwd)> _metadata =
            new Dictionary<String, (DateTime?, String)>(StringComparer.OrdinalIgnoreCase);

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
            var firstSighting = known < 0;

            // session_meta is the first record and may be far outside the 64 KB catch-up tail in
            // a long session. Read it once so every state envelope has the folder name from the
            // first poll, rather than showing the generic agent fallback until another turn_context.
            var metadata = this.MetadataFor(path);
            if (metadata.Cwd != null)
            {
                this._cwds[path] = metadata.Cwd;
            }

            // First sighting: start from a bounded wide tail, not the beginning. The last edge wins,
            // so this recovers the current turn without replaying a whole day of history.
            if (known < 0)
            {
                known = Math.Max(0, info.Length - MaxInitialCatchUpBytes);
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
                var catchUpLimit = firstSighting ? MaxInitialCatchUpBytes : MaxCatchUpBytes;
                var take = (Int32)Math.Min(catchUpLimit, stream.Length - known);
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
            // history, and a batch containing start-then-complete means the turn is over. The cwd
            // rides along the same pass: turn_context/session_meta records carry it, and without
            // it the envelope's payload is empty and the key can only ever say "Codex" instead of
            // the project's folder name — seen on hardware 2026-08-21.
            String activity = null;
            foreach (var line in complete.Split('\n'))
            {
                var mapped = ActivityFor(line);
                if (mapped != null)
                {
                    activity = mapped;
                }

                var cwd = CwdFrom(line);
                if (cwd != null)
                {
                    this._cwds[path] = cwd;
                }
            }

            if (activity == null)
            {
                // Windows can keep LastWriteTime frozen while Codex appends through an open file
                // handle. Every complete batch is nevertheless direct evidence of transcript
                // activity, so refresh a currently-busy envelope and its observation timestamp.
                // Once growth stops, no heartbeat is written and the normal stall window applies.
                if (this._activities.TryGetValue(path, out var current)
                    && String.Equals(current, CodexStateBridge.BusyEvent, StringComparison.Ordinal))
                {
                    return this.WriteState(path, current, preserveWaiting: true) ? 1 : 0;
                }

                // A long active turn can push task_started outside the catch-up tail. Once the
                // rollout has been safely correlated, publish its immutable metadata immediately
                // so an old/missing state file cannot leave the key labelled only "Codex". Keep
                // this per-session: metadata from one rollout must not replace shared activity.
                return firstSighting && this.WriteState(path, "SessionStart", writeShared: false) ? 1 : 0;
            }

            this._activities[path] = activity;
            return this.WriteState(path, activity) ? 1 : 0;
        }

        /// <summary>
        /// The event → activity mapping, and the whole of this bridge's knowledge of codex's
        /// format. Anything else — including events we have never seen — maps to null, which
        /// means "no update", never a default.
        /// </summary>
        internal static String ActivityFor(String line)
        {
            if (String.IsNullOrWhiteSpace(line)
                || (!line.Contains("task_", StringComparison.Ordinal)
                    && !line.Contains("turn_aborted", StringComparison.Ordinal)))
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

        /// <summary>The three records this bridge knows, mapped to two activity edges.</summary>
        private static String Map(String type) =>
            type switch
            {
                "task_started" => CodexStateBridge.BusyEvent,
                "task_complete" => CodexStateBridge.IdleEvent,
                "turn_aborted" => CodexStateBridge.IdleEvent,
                _ => null,
            };

        /// <summary>
        /// The session's working directory, when this line reports one — turn_context and
        /// session_meta both carry payload.cwd. Same defensive posture as ActivityFor: anything
        /// unreadable is null, never a guess. The cheap Contains guard keeps the JSON parse off
        /// the overwhelmingly common lines that carry no cwd at all.
        /// </summary>
        internal static String CwdFrom(String line)
        {
            if (String.IsNullOrWhiteSpace(line) || !line.Contains("\"cwd\"", StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("payload", out var payload)
                    && payload.ValueKind == JsonValueKind.Object
                    && payload.TryGetProperty("cwd", out var cwd)
                    && cwd.ValueKind == JsonValueKind.String)
                {
                    var value = cwd.GetString();
                    return String.IsNullOrWhiteSpace(value) ? null : value;
                }

                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

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
        private Boolean WriteState(
            String rolloutPath,
            String activityEvent,
            Boolean writeShared = true,
            Boolean preserveWaiting = false)
        {
            // Two payload fields this transport can honestly supply, and the reader wants both:
            // cwd names the key with the project folder instead of a generic label, and
            // transcript_path points the context reader at the file to size the window from —
            // which on this platform IS this rollout, the very file we are tailing. The macOS
            // hook supplies the same two; without transcript_path the context key stays blank on
            // Windows no matter how much the session has used (seen on hardware 2026-08-21).
            var fields = new List<String>
            {
                "\"transcript_path\":\"" + JsonEscape(rolloutPath) + "\"",
                "\"transcript_activity_ts\":" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };
            if (this._cwds.TryGetValue(rolloutPath, out var cwd))
            {
                fields.Add("\"cwd\":\"" + JsonEscape(cwd) + "\"");
            }
            var payload = "{" + String.Join(",", fields) + "}";

            var envelope =
                "{\"schema\":1,\"agent\":\"codex-cli\",\"transport\":\"rollout\",\"event\":\"" + activityEvent + "\",\"ts\":" +
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ",\"payload\":" + payload + "}\n";

            try
            {
                Directory.CreateDirectory(this._ipcSessionsDir);
                var key = this.KeyFor(rolloutPath);
                if (!writeShared && key == null)
                {
                    return false;
                }

                var wrote = false;
                var sharedPath = Path.Combine(this._ipcSessionsDir, "shared.json");
                if (writeShared && (!preserveWaiting || !IsWaitingHookState(sharedPath)))
                {
                    WriteAtomic(sharedPath, envelope);
                    wrote = true;
                }

                var keyedPath = key == null ? null : Path.Combine(this._ipcSessionsDir, key + ".json");
                if (keyedPath != null && (!preserveWaiting || !IsWaitingHookState(keyedPath)))
                {
                    WriteAtomic(keyedPath, envelope);
                    wrote = true;
                }

                return wrote;
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

            var metadata = this.MetadataFor(rolloutPath);
            DateTime started;
            if (metadata.StartedUtc.HasValue)
            {
                started = metadata.StartedUtc.Value;
            }
            else
            {
                try
                {
                    started = new FileInfo(rolloutPath).CreationTimeUtc;
                }
                catch (Exception ex)
                {
                    PluginLog.Verbose(ex, $"CodexRolloutBridge: cannot read creation time of {rolloutPath}");
                    return null;
                }
            }

            var unclaimed = live.Where(s => !this._claims.ContainsValue(s.Key)).ToList();
            if (unclaimed.Count == 0)
            {
                return null;
            }

            var ordered = unclaimed
                .OrderBy(s => Math.Abs((s.Start.ToUniversalTime() - started).TotalSeconds))
                .ToList();

            var best = Math.Abs((ordered[0].Start.ToUniversalTime() - started).TotalSeconds);
            if (best > MaxStartSkewSeconds)
            {
                return null;
            }

            if (ordered.Count > 1)
            {
                var runnerUp = Math.Abs((ordered[1].Start.ToUniversalTime() - started).TotalSeconds);

                // Too close to call: two sessions started within a second of each other. The shared
                // file still carries the state; a coin-flip claim would not.
                if (Math.Abs(best - runnerUp) < 1.0)
                {
                    return null;
                }
            }

            this._claims[rolloutPath] = ordered[0].Key;
            return ordered[0].Key;
        }

        /// <summary>
        /// A rollout heartbeat says only that the turn is moving; it cannot disprove the exact
        /// PermissionRequest hook that says the turn is paused for the user. Preserve that state
        /// until a real lifecycle/terminal edge replaces it.
        /// </summary>
        private static Boolean IsWaitingHookState(String path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                return root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("transport", out var transport)
                    && transport.ValueKind == JsonValueKind.String
                    && String.Equals(transport.GetString(), "hook", StringComparison.Ordinal)
                    && root.TryGetProperty("event", out var evt)
                    && evt.ValueKind == JsonValueKind.String
                    && String.Equals(evt.GetString(), "PermissionRequest", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private (DateTime? StartedUtc, String Cwd) MetadataFor(String path)
        {
            if (this._metadata.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var result = ((DateTime?)null, (String)null);
            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var take = (Int32)Math.Min(MaxMetadataBytes, stream.Length);
                var buffer = new Byte[take];
                var read = stream.Read(buffer, 0, take);
                var text = Encoding.UTF8.GetString(buffer, 0, read);
                var newline = text.IndexOf('\n');
                if (newline >= 0)
                {
                    var firstLine = text.Substring(0, newline).TrimEnd('\r');
                    using var doc = JsonDocument.Parse(firstLine);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object
                        && String.Equals(TypeOf(root), "session_meta", StringComparison.Ordinal)
                        && root.TryGetProperty("payload", out var payload)
                        && payload.ValueKind == JsonValueKind.Object)
                    {
                        var cwd = payload.TryGetProperty("cwd", out var cwdElement)
                            && cwdElement.ValueKind == JsonValueKind.String
                                ? cwdElement.GetString()
                                : null;

                        DateTime? startedUtc = null;
                        if (payload.TryGetProperty("timestamp", out var timestamp)
                            && timestamp.ValueKind == JsonValueKind.String
                            && DateTimeOffset.TryParse(
                                timestamp.GetString(), CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                out var parsed))
                        {
                            startedUtc = parsed.UtcDateTime;
                        }

                        result = (startedUtc, String.IsNullOrWhiteSpace(cwd) ? null : cwd);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                PluginLog.Verbose(ex, $"CodexRolloutBridge: cannot read session metadata from {path}");
            }

            this._metadata[path] = result;
            return result;
        }

        private static void WriteAtomic(String path, String content)
        {
            // Reload briefly overlaps plugin instances inside one service process. A PID-only temp
            // name lets their poll loops collide; a unique sibling preserves atomic replacement.
            // A hook and the rollout fallback can also replace the same destination concurrently.
            // Windows reports that short collision as either IOException or AccessDenied, so retry
            // the move before using an in-place write as the last-resort recovery path. A partial
            // JSON read is harmless (the reader skips one poll); losing cwd forever is not.
            var tmp = path + "." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, content);
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        File.Move(tmp, path, overwrite: true);
                        return;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        if (attempt == 4)
                        {
                            break;
                        }
                        System.Threading.Thread.Sleep(10 << attempt);
                    }
                }

                // Reached only when the fifth atomic replacement failed. This commonly means the
                // destination was created by Codex's sandbox identity with write but not delete
                // rights for the service identity. Overwrite its contents without replacing the
                // directory entry; the outer caller still reports if even this is denied.
                File.WriteAllText(path, content);
            }
            finally
            {
                try { if (File.Exists(tmp)) { File.Delete(tmp); } } catch { /* best effort */ }
            }
        }

        // Minimal JSON string escaping for the one hand-built payload field — same discipline as
        // the hook exe's JsonEscape, and for the same reason: Windows paths are full of
        // backslashes and this string lands in a file the plugin parses as JSON.
        private static String JsonEscape(String s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (ch < 0x20) { sb.Append("\\u").Append(((Int32)ch).ToString("x4")); }
                        else { sb.Append(ch); }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
