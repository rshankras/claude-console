namespace Loupedeck.ClaudeConsolePlugin.Agents
{
    using System;
    using System.Text.Json;

    /// <summary>
    /// One Codex session as the keypad needs to see it, parsed from the envelope
    /// <c>scripts/codex-hook.sh</c> writes.
    /// </summary>
    internal sealed class CodexSnapshot
    {
        /// <summary>The lifecycle event this snapshot came from, verbatim.</summary>
        public String Event { get; set; }

        /// <summary>busy | waiting | done | dead — the vocabulary the grid already speaks.</summary>
        public String Activity { get; set; }

        public String SessionId { get; set; }

        public String Model { get; set; }

        public String ProjectDir { get; set; }

        /// <summary>Codex's active approval policy. No Claude Code equivalent.</summary>
        public String PermissionMode { get; set; }

        /// <summary>Set only while an approval is pending.</summary>
        public String PendingTool { get; set; }

        /// <summary>Shell command for Bash, patch body for apply_patch — see <see cref="RiskClassifier"/>.</summary>
        public String PendingCommand { get; set; }

        /// <summary>Present on Stop. Claude Code has no equivalent; Vizhi reconstructed it from the transcript.</summary>
        public String LastAssistantMessage { get; set; }

        /// <summary>Where the rollout JSONL lives, if the best-effort token read wants it.</summary>
        public String TranscriptPath { get; set; }

        public Int64 Ts { get; set; }

        public ApprovalRisk Risk { get; set; } = ApprovalRisk.None;
    }

    /// <summary>
    /// Turns the hook's envelope into a snapshot the grid can render.
    ///
    /// Codex has no statusline, so unlike Claude Code there is no periodic full-state document —
    /// every fact the keys show arrives on a lifecycle event, and this is where an event becomes
    /// state. The activity vocabulary is deliberately the one the grid already speaks, so nothing
    /// downstream learns a second set of words.
    ///
    /// PARSE DEFENSIVELY, ALWAYS. Codex documents neither these payloads nor their stability, and
    /// the events are not uniform — SessionEnd carries no model and no permission_mode. Every field
    /// is therefore optional, and a payload we cannot read at all still yields a snapshot with an
    /// activity, because knowing a session went busy is worth a key even with no detail behind it.
    /// Never throw: this runs on the poll loop, inside an SDK callback.
    /// </summary>
    internal static class CodexStateReader
    {
        /// <summary>Parse one state file's contents. Returns null only when there is nothing usable at all.</summary>
        public static CodexSnapshot Parse(String json)
        {
            if (String.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var evt = Str(root, "event");
                var snap = new CodexSnapshot
                {
                    Event = evt,
                    Activity = ActivityFor(evt),
                    Ts = root.TryGetProperty("ts", out var ts) && ts.TryGetInt64(out var t) ? t : 0,
                };

                if (!root.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object)
                {
                    // A session with no readable detail is still a session. The grid shows the key.
                    return snap;
                }

                snap.SessionId = Str(p, "session_id");
                snap.Model = Str(p, "model");
                snap.ProjectDir = Str(p, "cwd");
                snap.PermissionMode = Str(p, "permission_mode");
                snap.TranscriptPath = Str(p, "transcript_path");
                snap.LastAssistantMessage = Str(p, "last_assistant_message");

                var toolName = Str(p, "tool_name");
                var command = p.TryGetProperty("tool_input", out var ti) && ti.ValueKind == JsonValueKind.Object
                    ? Str(ti, "command")
                    : null;

                // Tool detail is only meaningful while something is actually waiting on the user.
                // Carrying PreToolUse's tool into the idle state would leave a stale amber key.
                if (snap.Activity == "waiting")
                {
                    snap.PendingTool = toolName;
                    snap.PendingCommand = command;
                    snap.Risk = RiskClassifier.Classify(toolName, command, snap.ProjectDir);
                }

                return snap;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Codex's lifecycle mapped onto the grid's four words. Anything unrecognised counts as
        /// busy rather than idle: a new event type almost certainly means the agent is doing
        /// something, and showing "working" for an idle session is a far cheaper mistake than
        /// showing "ready" for one that is mid-turn.
        /// </summary>
        public static String ActivityFor(String hookEvent) =>
            hookEvent switch
            {
                "SessionStart" => "done",
                "Stop" => "done",
                "SessionEnd" => "dead",
                "PermissionRequest" => "waiting",
                "UserPromptSubmit" => "busy",
                "PreToolUse" => "busy",
                "PostToolUse" => "busy",
                null => "done",
                "" => "done",
                _ => "busy",
            };

        private static String Str(JsonElement obj, String name) =>
            obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
    }
}
