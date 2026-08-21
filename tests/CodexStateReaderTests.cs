namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Agents;

    using Xunit;

    /// <summary>
    /// Turning Codex lifecycle events into grid state.
    ///
    /// The payloads here are REAL — captured from codex-cli 0.145.0 and only shortened, never
    /// reshaped. That matters: Vizhi's Codex adapter guessed its field names and hedged with nested
    /// fallback chains, which is why it silently degraded whenever a guess was wrong. If Codex
    /// changes a field, one of these fixtures should stop matching and a test should fail loudly.
    /// </summary>
    public class CodexStateReaderTests
    {
        // Wraps a payload the way scripts/codex-hook.sh does.
        private static String Envelope(String evt, String payload) =>
            $"{{\"schema\":1,\"agent\":\"codex-cli\",\"event\":\"{evt}\",\"ts\":1786000000,\"payload\":{payload}}}";

        private const String RealPermissionRequest = """
            {"session_id":"01a01324-6044-7bb3-8455-6a9b96c55a3c",
             "turn_id":"01a01324-908b-7a50-943e-674739d4d69d",
             "transcript_path":"/Users/dev/.codex/sessions/2026/08/18/rollout-01a01324.jsonl",
             "cwd":"/Users/dev/project",
             "hook_event_name":"PermissionRequest",
             "model":"gpt-5.6-terra",
             "permission_mode":"default",
             "tool_name":"apply_patch",
             "tool_input":{"command":"*** Begin Patch\n*** Add File: /tmp/codex-probe.txt\n+hello\n*** End Patch"}}
            """;

        private const String RealStop = """
            {"session_id":"01a01317-a463-73c2-9316-a771e607cc15",
             "transcript_path":"/Users/dev/.codex/sessions/2026/08/18/rollout-01a01317.jsonl",
             "cwd":"/Users/dev/project",
             "hook_event_name":"Stop",
             "model":"gpt-5.6-terra",
             "permission_mode":"default",
             "stop_hook_active":false,
             "last_assistant_message":"On `main`, clean working tree."}
            """;

        // SessionEnd is the payload that proves events are NOT uniform: no model, no permission_mode.
        private const String RealSessionEnd = """
            {"session_id":"01a01317-a463-73c2-9316-a771e607cc15",
             "transcript_path":"/Users/dev/.codex/sessions/2026/08/18/rollout-01a01317.jsonl",
             "cwd":"/Users/dev/project",
             "hook_event_name":"SessionEnd",
             "reason":"other"}
            """;

        [Fact]
        public void Reads_every_field_the_keys_display_from_a_real_payload()
        {
            var s = CodexStateReader.Parse(Envelope("PermissionRequest", RealPermissionRequest));

            Assert.NotNull(s);
            Assert.Equal("01a01324-6044-7bb3-8455-6a9b96c55a3c", s.SessionId);
            Assert.Equal("gpt-5.6-terra", s.Model);
            Assert.Equal("/Users/dev/project", s.ProjectDir);
            Assert.Equal("default", s.PermissionMode);
            Assert.EndsWith("rollout-01a01324.jsonl", s.TranscriptPath);
            Assert.Equal(1786000000, s.Ts);
        }

        /// <summary>
        /// The whole reason the approval key exists: a pending patch that escapes the workspace
        /// must arrive already graded High, so the key can go red without anyone re-deriving it.
        /// </summary>
        [Fact]
        public void A_pending_approval_arrives_graded()
        {
            var s = CodexStateReader.Parse(Envelope("PermissionRequest", RealPermissionRequest));

            Assert.Equal("waiting", s.Activity);
            Assert.Equal("apply_patch", s.PendingTool);
            Assert.Contains("/tmp/codex-probe.txt", s.PendingCommand);
            Assert.Equal(ApprovalRisk.High, s.Risk);
        }

        [Fact]
        public void Stop_yields_an_idle_session_and_the_last_message()
        {
            var s = CodexStateReader.Parse(Envelope("Stop", RealStop));

            Assert.Equal("done", s.Activity);
            Assert.Equal("On `main`, clean working tree.", s.LastAssistantMessage);
            Assert.Equal(ApprovalRisk.None, s.Risk);
        }

        /// <summary>
        /// A stale amber key is worse than no key: once the turn moves on, the tool that was
        /// waiting must not still be attached to the session.
        /// </summary>
        [Fact]
        public void Tool_detail_does_not_survive_the_approval_it_belonged_to()
        {
            var stillHasToolFields = Envelope("PostToolUse", RealPermissionRequest);
            var s = CodexStateReader.Parse(stillHasToolFields);

            Assert.Equal("busy", s.Activity);
            Assert.Null(s.PendingTool);
            Assert.Null(s.PendingCommand);
            Assert.Equal(ApprovalRisk.None, s.Risk);
        }

        [Fact]
        public void SessionEnd_is_handled_despite_carrying_no_model_or_permission_mode()
        {
            var s = CodexStateReader.Parse(Envelope("SessionEnd", RealSessionEnd));

            Assert.Equal("dead", s.Activity);
            Assert.Equal("/Users/dev/project", s.ProjectDir);
            Assert.Null(s.Model);
            Assert.Null(s.PermissionMode);
        }

        [Theory]
        [InlineData("SessionStart", "done")]
        [InlineData("UserPromptSubmit", "busy")]
        [InlineData("PreToolUse", "busy")]
        [InlineData("PostToolUse", "busy")]
        [InlineData("PermissionRequest", "waiting")]
        [InlineData("Stop", "done")]
        [InlineData("SessionEnd", "dead")]
        public void The_whole_lifecycle_maps_onto_the_grids_vocabulary(String evt, String expected) =>
            Assert.Equal(expected, CodexStateReader.ActivityFor(evt));

        /// <summary>
        /// Codex may add events. Guessing "busy" for an unknown one shows a working key on an idle
        /// session; guessing "done" shows a ready key on a session mid-turn, which is the mistake
        /// that makes someone type into a busy terminal.
        /// </summary>
        [Fact]
        public void An_unknown_event_is_assumed_busy_not_idle() =>
            Assert.Equal("busy", CodexStateReader.ActivityFor("SubagentStart"));

        /// <summary>
        /// Runs on the poll loop inside an SDK callback, so nothing here may throw — a malformed
        /// file must degrade to null or to a detail-free snapshot, never to an exception.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("not json")]
        [InlineData("[1,2,3]")]
        [InlineData("{\"event\":\"Stop\",\"payload\":\"a string, not an object\"}")]
        [InlineData("{\"event\":\"Stop\",\"payload\":null}")]
        [InlineData("{}")]
        public void Malformed_input_never_throws(String json)
        {
            var ex = Record.Exception(() => CodexStateReader.Parse(json));
            Assert.Null(ex);
        }

        /// <summary>
        /// The hook writes payload:null when Codex hands it nothing readable. Knowing a session
        /// went busy is still worth a key, so that must produce a snapshot rather than nothing.
        /// </summary>
        [Fact]
        public void A_payloadless_event_still_produces_a_session()
        {
            var s = CodexStateReader.Parse("{\"schema\":1,\"event\":\"UserPromptSubmit\",\"ts\":5,\"payload\":null}");

            Assert.NotNull(s);
            Assert.Equal("busy", s.Activity);
            Assert.Equal(5, s.Ts);
            Assert.Null(s.SessionId);
        }

        /// <summary>
        /// The gap that survived every unit test: CodexStateReader was correct and NOTHING CALLED
        /// IT. The grid deserialised every state file as Claude Code's statusline, which a Codex
        /// envelope satisfies with all fields null — so its sessions sat at "ready" forever wearing
        /// a project name they never reported, with no error anywhere. This drives the adapter the
        /// grid actually uses.
        /// </summary>
        [Fact]
        public void The_adapter_the_grid_uses_reads_a_codex_envelope()
        {
            var state = new CodexCliAdapter().ParseSessionState(Envelope("PermissionRequest", RealPermissionRequest));

            Assert.NotNull(state);
            Assert.Equal("/Users/dev/project", state.ProjectDir);
            Assert.Equal("waiting", state.Activity);
            Assert.Equal("apply_patch", state.PendingTool);
            Assert.Equal(ApprovalRisk.High, state.Risk);
            Assert.True(state.ReportsApproval);
        }

        /// <summary>Busy and idle must reach the grid too, not just the approval case.</summary>
        [Theory]
        [InlineData("UserPromptSubmit", "busy")]
        [InlineData("PreToolUse", "busy")]
        [InlineData("Stop", "done")]
        public void Activity_reaches_the_grid_through_the_adapter(String evt, String expected)
        {
            var state = new CodexCliAdapter().ParseSessionState(Envelope(evt, RealStop));

            Assert.Equal(expected, state.Activity);
        }

        /// <summary>
        /// The mirror image: Claude Code's adapter must not be fooled by a Codex envelope, and
        /// vice versa. Both are JSON objects, so a lenient parse "succeeds" on either.
        /// </summary>
        [Fact]
        public void Neither_adapter_silently_accepts_the_others_document()
        {
            var codexDoc = Envelope("Stop", RealStop);
            var fromClaude = new ClaudeCodeAdapter().ParseSessionState(codexDoc);

            // It parses (it is valid JSON) but yields nothing usable — which is exactly why the
            // grid must ask the right adapter rather than assume one format.
            Assert.True(fromClaude == null || fromClaude.ProjectDir == null,
                "Claude's parser must not invent a project from a Codex envelope");
        }

        /// <summary>A Bash approval grades by command, exactly as Claude Code's does.</summary>
        [Fact]
        public void A_bash_approval_still_grades_as_a_command()
        {
            var payload = """
                {"cwd":"/Users/dev/project","model":"gpt-5.6-terra","tool_name":"Bash",
                 "tool_input":{"command":"git push --force origin main"}}
                """;

            var s = CodexStateReader.Parse(Envelope("PermissionRequest", payload));

            Assert.Equal("Bash", s.PendingTool);
            Assert.Equal(ApprovalRisk.High, s.Risk);
        }
    }
}
