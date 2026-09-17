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

        /// <summary>
        /// Rollout file -> code-mode exec call currently waiting for CLI approval. Codex 0.152
        /// writes the outer custom tool call before showing its menu and the matching output after
        /// the user answers, but does not run PermissionRequest for that nested exec_command.
        /// </summary>
        private readonly Dictionary<String, String> _pendingApprovalCalls =
            new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

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
        /// Direct process CWD observations, independent of the state files this bridge writes.
        /// Close launch times alone cannot distinguish several projects starting together.
        /// </summary>
        internal IReadOnlyDictionary<String, String> LiveSessionDirectories { get; set; }

        /// <summary>Production waits for the first process scan before claiming any transcript.</summary>
        internal Boolean RequireSessionDirectories { get; init; }

        /// <summary>
        /// Read whatever has been appended since the last call and write the resulting state.
        /// Returns how many envelopes were written — for tests and logging, not for control flow.
        /// Never throws: a transport that breaks the plugin is worse than one that reports nothing.
        /// </summary>
        public Int32 Poll()
        {
            if (this.RequireSessionDirectories && this.LiveSessionDirectories == null) return 0;

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

            // Do not consume a finished transcript before its process identity is available.
            // A later directory scan must be able to replay it even if the file never grows again.
            if (this.RequireSessionDirectories && this.KeyFor(path) == null) return 0;

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
            String pendingCommand = null;
            String transport = null;
            // A Busy edge means two different things and only this flag tells them apart: a bare
            // task_started says nothing about approvals, while the code-mode output below is the
            // resolution of an approval we ourselves published. Both land on BusyEvent, so the
            // activity value alone cannot decide whether a waiting envelope may be overwritten.
            var resolvesApproval = false;
            foreach (var line in complete.Split('\n'))
            {
                var mapped = ActivityFor(line);
                if (mapped != null)
                {
                    activity = mapped;
                    pendingCommand = null;
                    transport = null;
                    resolvesApproval = false;

                    if (String.Equals(mapped, CodexStateBridge.IdleEvent, StringComparison.Ordinal))
                    {
                        this._pendingApprovalCalls.Remove(path);
                    }
                }

                // Code mode wraps exec_command in a custom `exec` tool. In Codex CLI 0.152 the
                // nested command's approval menu is real, but PermissionRequest is not dispatched
                // for it. The rollout still gives us exact, ordered edges: the call is written
                // before the menu and its matching output only after Yes/No/Escape resolves it.
                if (TryCodeModeApproval(line, out var approvalCallId, out var command))
                {
                    this._pendingApprovalCalls[path] = approvalCallId;
                    activity = "PermissionRequest";
                    pendingCommand = command;
                    transport = "rollout-code-mode";
                    resolvesApproval = false;
                }
                else if (TryCodeModeOutput(line, out var completedCallId)
                    && this._pendingApprovalCalls.TryGetValue(path, out var pendingCallId)
                    && String.Equals(completedCallId, pendingCallId, StringComparison.Ordinal))
                {
                    this._pendingApprovalCalls.Remove(path);
                    activity = CodexStateBridge.BusyEvent;
                    pendingCommand = null;
                    transport = null;
                    resolvesApproval = true;
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
                // Naming a session is never grounds for discarding a live approval on its key.
                return firstSighting
                    && this.WriteState(path, "SessionStart", writeShared: false, preserveWaiting: true)
                        ? 1 : 0;
            }

            this._activities[path] = activity;

            // A lifecycle edge is not evidence about an approval. The hook owns PermissionRequest
            // and can write it between two of our polls, so a task_started read afterwards must
            // not bury it — that turned the amber key grey with nothing left to re-emit it. Only
            // a terminal edge (task_complete / turn_aborted), a fresh approval, or the code-mode
            // output that resolves our own approval may clear a waiting envelope.
            var preserveWaiting =
                String.Equals(activity, CodexStateBridge.BusyEvent, StringComparison.Ordinal)
                && !resolvesApproval;

            return this.WriteState(
                path, activity, preserveWaiting: preserveWaiting,
                pendingCommand: pendingCommand, transport: transport) ? 1 : 0;
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
        /// Recognise only the OUTER exec_command argument object's explicit escalation request.
        /// A plain substring search is unsafe: the shell command itself may be source code or a
        /// diagnostic search containing the words sandbox_permissions and require_escalated.
        /// </summary>
        internal static Boolean TryCodeModeApproval(String line, out String callId, out String command)
        {
            callId = null;
            command = null;

            if (String.IsNullOrWhiteSpace(line)
                || !line.Contains("custom_tool_call", StringComparison.Ordinal)
                || !line.Contains("exec_command", StringComparison.Ordinal)
                || !line.Contains("require_escalated", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!String.Equals(TypeOf(root), "response_item", StringComparison.Ordinal)
                    || !root.TryGetProperty("payload", out var payload)
                    || payload.ValueKind != JsonValueKind.Object
                    || !String.Equals(TypeOf(payload), "custom_tool_call", StringComparison.Ordinal)
                    || !payload.TryGetProperty("name", out var name)
                    || name.ValueKind != JsonValueKind.String
                    || !String.Equals(name.GetString(), "exec", StringComparison.Ordinal)
                    || !payload.TryGetProperty("call_id", out var id)
                    || id.ValueKind != JsonValueKind.String
                    || String.IsNullOrWhiteSpace(id.GetString())
                    || !payload.TryGetProperty("input", out var inputElement)
                    || inputElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var input = inputElement.GetString();
                var marker = input.IndexOf("tools.exec_command", StringComparison.Ordinal);
                if (marker < 0)
                {
                    return false;
                }

                var openParen = input.IndexOf('(', marker + "tools.exec_command".Length);
                var objectStart = openParen < 0 ? -1 : NextNonWhitespace(input, openParen + 1);
                if (objectStart < 0 || input[objectStart] != '{'
                    || !TryTopLevelStringProperty(input, objectStart, "sandbox_permissions", out var permission)
                    || !String.Equals(permission, "require_escalated", StringComparison.Ordinal))
                {
                    return false;
                }

                // A command that changed to a non-string shape is still an exact approval edge;
                // omit its detail rather than inventing one. The key can still safely say Allow?.
                TryTopLevelStringProperty(input, objectStart, "cmd", out command);
                callId = id.GetString();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>The matching custom tool output is the exact end of the CLI approval menu.</summary>
        internal static Boolean TryCodeModeOutput(String line, out String callId)
        {
            callId = null;
            if (String.IsNullOrWhiteSpace(line)
                || !line.Contains("custom_tool_call_output", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!String.Equals(TypeOf(root), "response_item", StringComparison.Ordinal)
                    || !root.TryGetProperty("payload", out var payload)
                    || payload.ValueKind != JsonValueKind.Object
                    || !String.Equals(TypeOf(payload), "custom_tool_call_output", StringComparison.Ordinal)
                    || !payload.TryGetProperty("call_id", out var id)
                    || id.ValueKind != JsonValueKind.String
                    || String.IsNullOrWhiteSpace(id.GetString()))
                {
                    return false;
                }

                callId = id.GetString();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Read one string property from the first level of a JavaScript object literal. This is a
        /// deliberately small parser for Codex's generated call shape, not a general JS evaluator.
        /// It respects strings and nested values so text inside cmd can never masquerade as an
        /// approval property.
        /// </summary>
        private static Boolean TryTopLevelStringProperty(
            String source,
            Int32 objectStart,
            String wanted,
            out String value)
        {
            value = null;
            var i = objectStart + 1;

            while (i < source.Length)
            {
                i = NextNonWhitespace(source, i);
                while (i >= 0 && i < source.Length && source[i] == ',')
                {
                    i = NextNonWhitespace(source, i + 1);
                }

                if (i < 0 || i >= source.Length || source[i] == '}')
                {
                    return false;
                }

                String property;
                if (source[i] == '"')
                {
                    if (!TryReadJsonString(source, ref i, out property))
                    {
                        return false;
                    }
                }
                else
                {
                    var start = i;
                    while (i < source.Length
                        && (Char.IsLetterOrDigit(source[i]) || source[i] == '_' || source[i] == '$'))
                    {
                        i++;
                    }

                    if (i == start)
                    {
                        return false;
                    }
                    property = source.Substring(start, i - start);
                }

                i = NextNonWhitespace(source, i);
                if (i < 0 || i >= source.Length || source[i] != ':')
                {
                    return false;
                }

                i = NextNonWhitespace(source, i + 1);
                if (i < 0 || i >= source.Length)
                {
                    return false;
                }

                if (String.Equals(property, wanted, StringComparison.Ordinal))
                {
                    return source[i] == '"' && TryReadJsonString(source, ref i, out value);
                }

                if (!SkipJavaScriptValue(source, ref i))
                {
                    return false;
                }
            }

            return false;
        }

        private static Int32 NextNonWhitespace(String source, Int32 start)
        {
            var i = start;
            while (i < source.Length && Char.IsWhiteSpace(source[i]))
            {
                i++;
            }
            return i < source.Length ? i : -1;
        }

        private static Boolean TryReadJsonString(String source, ref Int32 i, out String value)
        {
            value = null;
            if (i < 0 || i >= source.Length || source[i] != '"')
            {
                return false;
            }

            var start = i++;
            var escaped = false;
            while (i < source.Length)
            {
                var ch = source[i++];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (ch == '"')
                {
                    try
                    {
                        value = JsonSerializer.Deserialize<String>(source.Substring(start, i - start));
                        return true;
                    }
                    catch (JsonException)
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        private static Boolean SkipJavaScriptValue(String source, ref Int32 i)
        {
            var depth = 0;
            var quote = '\0';
            var escaped = false;

            while (i < source.Length)
            {
                var ch = source[i];
                if (quote != '\0')
                {
                    i++;
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == quote)
                    {
                        quote = '\0';
                    }
                    continue;
                }

                if (ch == '"' || ch == '\'' || ch == '`')
                {
                    quote = ch;
                    i++;
                    continue;
                }
                if (ch == '{' || ch == '[' || ch == '(')
                {
                    depth++;
                    i++;
                    continue;
                }
                if (ch == '}' || ch == ']' || ch == ')')
                {
                    if (depth == 0)
                    {
                        return ch == '}';
                    }
                    depth--;
                    i++;
                    continue;
                }
                if (ch == ',' && depth == 0)
                {
                    i++;
                    return true;
                }

                i++;
            }

            return false;
        }

        /// <summary>
        /// Write the envelope the hook would have written, so every reader above stays unchanged.
        /// The shared file is always written (the grid's fallback when it has no key match yet);
        /// the per-session file only when this rollout could be attached to a live process.
        /// </summary>
        private Boolean WriteState(
            String rolloutPath,
            String activityEvent,
            Boolean writeShared = true,
            Boolean preserveWaiting = false,
            String pendingCommand = null,
            String transport = null)
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
            if (String.Equals(activityEvent, "PermissionRequest", StringComparison.Ordinal))
            {
                fields.Add("\"tool_name\":\"Bash\"");
                if (pendingCommand != null)
                {
                    fields.Add("\"tool_input\":{\"command\":\"" + JsonEscape(pendingCommand) + "\"}");
                }
            }
            var payload = "{" + String.Join(",", fields) + "}";

            var envelope =
                "{\"schema\":1,\"agent\":\"codex-cli\",\"transport\":\"" +
                JsonEscape(transport ?? "rollout") + "\",\"event\":\"" + activityEvent + "\",\"ts\":" +
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
                if (writeShared && (!preserveWaiting || !IsWaitingState(sharedPath)))
                {
                    WriteAtomic(sharedPath, envelope);
                    wrote = true;
                }

                var keyedPath = key == null ? null : Path.Combine(this._ipcSessionsDir, key + ".json");
                if (keyedPath != null && (!preserveWaiting || !IsWaitingState(keyedPath)))
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
        /// Match the rollout's project against independently observed process directories before
        /// comparing start times. CLI startup can take longer than the interval between launches:
        /// matching only timestamps swapped Presskit and Stage when three projects opened together.
        /// Unknown directories retain the bounded time fallback; known mismatches never qualify.
        /// </summary>
        internal String KeyFor(String rolloutPath)
        {
            var metadata = this.MetadataFor(rolloutPath);
            if (this.RequireSessionDirectories) return this.VerifiedKeyFor(rolloutPath, metadata);
            if (this._claims.TryGetValue(rolloutPath, out var claimed))
            {
                if (!this.LiveSessions.Any(s => s.Key == claimed)) return null;
                if (this.DirectoryMatch(claimed, metadata.Cwd) != false) return claimed;
                // A directory may become readable after an initial time-only claim. Do not
                // keep writing another project's state after direct evidence disproves it.
                this._claims.Remove(rolloutPath);
            }

            var live = this.LiveSessions;
            if (live.Count == 0)
            {
                return null;
            }

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

            var unclaimed = live.Where(s => !this._claims.ContainsValue(s.Key)
                && this.DirectoryMatch(s.Key, metadata.Cwd) != false).ToList();

            // Prefer positive directory evidence over a closer process whose CWD is unavailable.
            var matching = unclaimed.Where(s => this.DirectoryMatch(s.Key, metadata.Cwd) == true).ToList();
            if (matching.Count > 0) unclaimed = matching;
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

        // Production never guesses when either side of the identity is unreadable. A recently
        // closed session can be only seconds older than its replacement, so absolute time skew
        // alone is insufficient: the rollout must also begin after the process starts.
        private String VerifiedKeyFor(String path, (DateTime? StartedUtc, String Cwd) metadata)
        {
            if (!metadata.StartedUtc.HasValue || String.IsNullOrWhiteSpace(metadata.Cwd)) return null;
            var started = metadata.StartedUtc.Value;
            Boolean Matches((String Key, DateTime Start) session) =>
                this.DirectoryMatch(session.Key, metadata.Cwd) == true
                && started >= session.Start.ToUniversalTime()
                && (started - session.Start.ToUniversalTime()).TotalSeconds <= MaxStartSkewSeconds;

            if (this._claims.TryGetValue(path, out var claimed))
            {
                if (this.LiveSessions.Any(s => s.Key == claimed && Matches(s))) return claimed;
                this._claims.Remove(path);
            }
            var matches = this.LiveSessions.Where(s => Matches(s) && !this._claims.ContainsValue(s.Key)).ToList();
            if (matches.Count != 1) return null;
            this._claims[path] = matches[0].Key;
            return matches[0].Key;
        }

        private Boolean? DirectoryMatch(String key, String rolloutDirectory)
        {
            if (String.IsNullOrWhiteSpace(rolloutDirectory)
                || this.LiveSessionDirectories == null
                || !this.LiveSessionDirectories.TryGetValue(key, out var processDirectory)
                || String.IsNullOrWhiteSpace(processDirectory)) return null;

            // This transport runs on Windows. Compare separator/case variants without touching
            // the filesystem or using transcript-derived grid labels as evidence of ownership.
            return String.Equals(
                rolloutDirectory.Replace('/', '\\').TrimEnd('\\'),
                processDirectory.Replace('/', '\\').TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A rollout heartbeat says only that the turn is moving; it cannot disprove the exact
        /// PermissionRequest hook that says the turn is paused for the user. Preserve that state
        /// until a real lifecycle/terminal edge replaces it.
        /// </summary>
        private static Boolean IsWaitingState(String path)
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

            // Files can first appear empty or mid-record. Cache only a complete identity.
            if (result.Item1.HasValue && !String.IsNullOrWhiteSpace(result.Item2)) this._metadata[path] = result;
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
