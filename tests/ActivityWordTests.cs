namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    using Xunit;

    /// <summary>
    /// The word a hook writes for a permission prompt must count as "waiting" (2.2.1 Windows
    /// retest, item 2 — every Yes/No press discarded).
    ///
    /// The PermissionRequest hook is invoked with the argv verb "permission". The bash hook
    /// translates it to the state word "waiting" before writing; the Windows exe wrote the verb
    /// itself. The plugin's pending-approval check and its routing fallback both compare against
    /// the literal "waiting", so on Windows no session ever waited, the pending payload beside it
    /// was never read, and Yes/No answered "no pending approval on (no target)" for three
    /// sessions that were discovered, pinned and named. Both sides are fixed: the exe translates
    /// like the bash hook, and the plugin normalises the word so it no longer depends on which
    /// hook wrote the file. Nothing asserted the word before; these do.
    /// </summary>
    public class ActivityWordTests : IDisposable
    {
        private readonly String _root = Path.Combine(Path.GetTempPath(), "cc-word-" + Guid.NewGuid().ToString("N"));
        private readonly String _sessionsDir;
        private readonly String _activityDir;

        public ActivityWordTests()
        {
            _sessionsDir = Path.Combine(_root, "sessions");
            _activityDir = Path.Combine(_root, "activity");
            Directory.CreateDirectory(_sessionsDir);
            Directory.CreateDirectory(_activityDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        [Theory]
        [InlineData("busy", "busy")]
        [InlineData("waiting", "waiting")]
        [InlineData("done", "ready")]
        [InlineData("permission", "waiting")]
        public void The_grid_speaks_busy_waiting_ready_whatever_the_hook_wrote(String word, String state)
        {
            Assert.Equal(state, SessionRegistry.NormaliseActivityWord(word));
        }

        [Fact]
        public void A_session_whose_hook_wrote_permission_is_waiting_with_its_pending_approval_read()
        {
            const String key = "pid-47320-639241143769947818";
            File.WriteAllText(Path.Combine(_sessionsDir, key + ".json"),
                "{\"session_id\":\"sid\",\"workspace\":{\"project_dir\":\"C:\\\\w\\\\proj\"},\"context_window\":{\"used_percentage\":10}}");
            File.WriteAllText(Path.Combine(_activityDir, key + ".json"),
                $"{{\"state\":\"permission\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}");
            File.WriteAllText(Path.Combine(_activityDir, "pending-" + key + ".json"),
                "{\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"git push --force\"}}");

            var grid = new SessionRegistry(_sessionsDir, _activityDir, Path.Combine(_root, "registry.json"))
            {
                Agent = new Agents.ClaudeCodeAdapter(),
            };
            grid.Refresh(new HashSet<String> { key });
            var session = grid.SlotSession(1);

            Assert.NotNull(session);
            Assert.Equal("waiting", session.State);
            Assert.False(String.IsNullOrEmpty(session.PendingTool), "the pending payload beside a waiting session must be read");
        }

        [Fact]
        public void The_windows_hook_translates_the_permission_verb_before_writing()
        {
            var source = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "windows", "ClaudeConsoleHook", "Program.cs"));

            // The exact expression, so a refactor that drops it fails here and not on QA's machine.
            Assert.Contains("var word = state == \"permission\" ? \"waiting\" : state;", source);
            Assert.Contains("JsonEscape(word)", source);
        }

        private static String RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "src", "Products")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }
            throw new InvalidOperationException("repo root not found above " + AppContext.BaseDirectory);
        }
    }
}
