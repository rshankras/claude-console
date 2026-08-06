namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    using Xunit;

    /// <summary>
    /// Reading the captured PermissionRequest payload, and turning it into a badge.
    ///
    /// The payload shape here matches the real one, verified against the zod schemas inside the
    /// installed Claude Code binary (v2.1.223): `tool_name` plus `tool_input`, with the shell string
    /// at `tool_input.command` for Bash. Parsing is deliberately defensive — these payloads have
    /// changed shape between versions, and a badge is never worth throwing on.
    /// </summary>
    public class PendingApprovalTests : IDisposable
    {
        private readonly String _root =
            Path.Combine(Path.GetTempPath(), "cc-pending-" + Guid.NewGuid().ToString("N"));

        private readonly String _sessionsDir;
        private readonly String _activityDir;

        public PendingApprovalTests()
        {
            _sessionsDir = Path.Combine(_root, "sessions");
            _activityDir = Path.Combine(_root, "activity");
            Directory.CreateDirectory(_sessionsDir);
            Directory.CreateDirectory(_activityDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        private String WritePending(String json)
        {
            var path = Path.Combine(_activityDir, "pending-ttys001.json");
            File.WriteAllText(path, json);
            return path;
        }

        // --- payload parsing ------------------------------------------------------------------

        [Fact]
        public void Reads_the_tool_and_command_from_a_real_payload()
        {
            var path = WritePending(@"{
              ""session_id"": ""abc123"",
              ""transcript_path"": ""/Users/x/.claude/projects/-p/abc123.jsonl"",
              ""cwd"": ""/Users/x/project"",
              ""permission_mode"": ""default"",
              ""hook_event_name"": ""PermissionRequest"",
              ""tool_name"": ""Bash"",
              ""tool_input"": { ""command"": ""git push --force"", ""description"": ""Force push"" },
              ""tool_use_id"": ""toolu_01ABC""
            }");

            var pending = SessionRegistry.ReadPendingApproval(path);

            Assert.NotNull(pending);
            Assert.Equal("Bash", pending.Value.Tool);
            Assert.Equal("git push --force", pending.Value.Command);
        }

        [Fact]
        public void Handles_a_tool_with_no_shell_command()
        {
            // tool_input's shape is per-tool; only Bash is guaranteed to carry `command`.
            var path = WritePending(@"{""tool_name"":""Read"",""tool_input"":{""file_path"":""/tmp/x""}}");

            var pending = SessionRegistry.ReadPendingApproval(path);

            Assert.Equal("Read", pending.Value.Tool);
            Assert.Null(pending.Value.Command);
        }

        [Theory]
        [InlineData("")]                                  // empty file (hook wrote nothing)
        [InlineData("   ")]
        [InlineData("not json at all")]
        [InlineData("[1,2,3]")]                           // JSON, but not an object
        [InlineData("{}")]                                // object with none of our fields
        [InlineData(@"{""tool_input"":""a string""}")]    // tool_input not an object
        [InlineData(@"{""tool_name"":42}")]               // wrong type
        public void Survives_a_payload_it_does_not_understand(String json)
        {
            var path = WritePending(json);

            Assert.Null(SessionRegistry.ReadPendingApproval(path));
        }

        [Fact]
        public void Missing_file_is_not_an_error()
        {
            Assert.Null(SessionRegistry.ReadPendingApproval(Path.Combine(_activityDir, "nope.json")));
        }

        // --- end to end through the grid --------------------------------------------------------

        private void WriteSession(String tty)
        {
            File.WriteAllText(Path.Combine(_sessionsDir, tty + ".json"), JsonSerializer.Serialize(new
            {
                session_id = "sid",
                workspace = new { project_dir = "/Users/x/proj" },
                context_window = new { used_percentage = 10 },
            }));
        }

        private void WriteActivity(String tty, String state) =>
            File.WriteAllText(Path.Combine(_activityDir, tty + ".json"), JsonSerializer.Serialize(new
            {
                state,
                ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            }));

        private Models.GridSession Session(String tty = "ttys001")
        {
            var grid = new SessionRegistry(_sessionsDir, _activityDir, Path.Combine(_root, "registry.json"));
            grid.Refresh(new HashSet<String> { tty });
            return grid.SlotSession(1);
        }

        [Fact]
        public void A_destructive_pending_command_turns_the_session_red()
        {
            WriteSession("ttys001");
            WriteActivity("ttys001", "waiting");
            WritePending(@"{""tool_name"":""Bash"",""tool_input"":{""command"":""git push --force""}}");

            var session = Session();

            Assert.Equal(ApprovalRisk.High, session.Risk);
            Assert.Equal("Bash", session.PendingTool);
            Assert.Equal("git push --force", session.PendingCommand);
        }

        [Fact]
        public void A_routine_pending_command_is_amber()
        {
            WriteSession("ttys001");
            WriteActivity("ttys001", "waiting");
            WritePending(@"{""tool_name"":""Bash"",""tool_input"":{""command"":""npm test""}}");

            Assert.Equal(ApprovalRisk.Normal, Session().Risk);
        }

        [Fact]
        public void Waiting_with_no_payload_still_asks_for_an_answer()
        {
            // Older Claude Code has no PermissionRequest hook, and an idle prompt has no tool. The
            // key should still show that something wants you — just without the risk detail.
            WriteSession("ttys001");
            WriteActivity("ttys001", "waiting");

            Assert.Equal(ApprovalRisk.Normal, Session().Risk);
        }

        [Fact]
        public void A_working_session_shows_no_badge_even_if_a_stale_payload_lingers()
        {
            // The hook clears the pending file when the session moves on; this is the belt-and-braces
            // check that a missed clear can't leave a red badge burning after the command has run.
            WriteSession("ttys001");
            WriteActivity("ttys001", "busy");
            WritePending(@"{""tool_name"":""Bash"",""tool_input"":{""command"":""sudo rm -rf /""}}");

            var session = Session();

            Assert.Equal(ApprovalRisk.None, session.Risk);
            Assert.Null(session.PendingCommand);
        }

        [Fact]
        public void A_ready_session_shows_no_badge()
        {
            WriteSession("ttys001");

            Assert.Equal(ApprovalRisk.None, Session().Risk);
        }

        [Fact]
        public void Risk_change_repaints_the_keys()
        {
            WriteSession("ttys001");
            WriteActivity("ttys001", "waiting");
            WritePending(@"{""tool_name"":""Bash"",""tool_input"":{""command"":""npm test""}}");

            var grid = new SessionRegistry(_sessionsDir, _activityDir, Path.Combine(_root, "registry.json"));
            grid.Refresh(new HashSet<String> { "ttys001" });

            var repaints = 0;
            grid.OnGridChanged += () => repaints++;
            WritePending(@"{""tool_name"":""Bash"",""tool_input"":{""command"":""git push --force""}}");
            grid.Refresh(new HashSet<String> { "ttys001" });

            Assert.Equal(1, repaints);   // amber -> red must reach the LCD
        }

        [Fact]
        public void Reaping_a_dead_session_removes_its_pending_file()
        {
            WriteSession("ttys001");
            WriteActivity("ttys001", "waiting");
            var pending = WritePending(@"{""tool_name"":""Bash"",""tool_input"":{""command"":""npm test""}}");

            var grid = new SessionRegistry(_sessionsDir, _activityDir, Path.Combine(_root, "registry.json"));
            grid.Refresh(new HashSet<String>());   // the tab is gone

            Assert.False(File.Exists(pending));
        }
    }
}
