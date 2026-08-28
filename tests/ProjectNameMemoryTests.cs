namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    using Xunit;

    /// <summary>
    /// A session that has told us its project keeps its name (#49, second half).
    ///
    /// A session's state file IS its memory, and it is only rewritten when the session does
    /// something. Anything that loses that file mid-life erased the project name, and the key fell
    /// back to the agent's — so a key that had read "claude-console" started reading "Claude Code",
    /// which looks like a different session rather than a missing file. Reported from the device.
    ///
    /// The memory is deliberately dropped when a tab is reaped, so a REUSED tab cannot inherit the
    /// previous session's project — a remembered name must never become a lie.
    /// </summary>
    public class ProjectNameMemoryTests : IDisposable
    {
        private readonly String _root =
            Path.Combine(Path.GetTempPath(), "cc-projname-" + Guid.NewGuid().ToString("N"));

        private readonly String _sessionsDir;
        private readonly String _activityDir;

        public ProjectNameMemoryTests()
        {
            this._sessionsDir = Path.Combine(this._root, "sessions");
            this._activityDir = Path.Combine(this._root, "activity");
            Directory.CreateDirectory(this._sessionsDir);
            Directory.CreateDirectory(this._activityDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(this._root, recursive: true); } catch { /* best effort */ }
        }

        private String StateFile(String tty) => Path.Combine(this._sessionsDir, tty + ".json");

        private void WriteSession(String tty, String project) =>
            File.WriteAllText(this.StateFile(tty), JsonSerializer.Serialize(new
            {
                session_id = "sid-" + tty,
                workspace = new { project_dir = "/Users/x/" + project },
                context_window = new { used_percentage = 10 },
            }));

        private SessionRegistry NewRegistry() =>
            new SessionRegistry(this._sessionsDir, this._activityDir, Path.Combine(this._root, "registry.json"))
            {
                Agent = new Agents.ClaudeCodeAdapter(),
            };

        private static HashSet<String> Live(params String[] ttys) =>
            new HashSet<String>(ttys, StringComparer.Ordinal);

        /// <summary>The reported symptom: the file goes, the session lives, the name must stay.</summary>
        [Fact]
        public void A_reported_project_survives_losing_its_state_file()
        {
            this.WriteSession("ttys001", "claude-console");
            var registry = this.NewRegistry();
            registry.Refresh(Live("ttys001"));
            Assert.Equal("claude-console", registry.SlotSession(1).Project);

            File.Delete(this.StateFile("ttys001"));   // however it went — prune, cleared /tmp, a bad read
            registry.Refresh(Live("ttys001"));        // the session is still very much alive

            Assert.Equal("claude-console", registry.SlotSession(1).Project);
        }

        /// <summary>A session that never reported still has no name to show — that part is honest.</summary>
        [Fact]
        public void A_session_that_never_reported_has_no_project()
        {
            var registry = this.NewRegistry();
            registry.Refresh(Live("ttys009"));

            Assert.Null(registry.SlotSession(1).Project);
        }

        /// <summary>
        /// The case that makes a remembered name dangerous: close the tab, open a new session in the
        /// same one. It must NOT inherit the previous project.
        /// </summary>
        [Fact]
        public void A_reused_tab_does_not_inherit_the_previous_project()
        {
            this.WriteSession("ttys001", "alpha");
            var registry = this.NewRegistry();
            registry.Refresh(Live("ttys001"));
            Assert.Equal("alpha", registry.SlotSession(1).Project);

            registry.Refresh(Live());                 // the tab closes; the session is reaped
            registry.Refresh(Live("ttys001"));        // a new session starts in the same tty

            Assert.Null(registry.SlotSession(1)?.Project);
        }

        [Fact]
        public void A_later_report_replaces_the_remembered_name()
        {
            this.WriteSession("ttys001", "alpha");
            var registry = this.NewRegistry();
            registry.Refresh(Live("ttys001"));

            this.WriteSession("ttys001", "beta");     // same session, moved project
            registry.Refresh(Live("ttys001"));
            Assert.Equal("beta", registry.SlotSession(1).Project);

            File.Delete(this.StateFile("ttys001"));
            registry.Refresh(Live("ttys001"));

            Assert.Equal("beta", registry.SlotSession(1).Project);   // the LATEST name, not the first
        }

        /// <summary>Each session remembers its own; they must not blur into one another.</summary>
        [Fact]
        public void Sessions_remember_their_own_projects_independently()
        {
            this.WriteSession("ttys001", "alpha");
            this.WriteSession("ttys002", "beta");
            var registry = this.NewRegistry();
            registry.Refresh(Live("ttys001", "ttys002"));

            File.Delete(this.StateFile("ttys001"));
            File.Delete(this.StateFile("ttys002"));
            registry.Refresh(Live("ttys001", "ttys002"));

            Assert.Equal("alpha", registry.SlotSession(1).Project);
            Assert.Equal("beta", registry.SlotSession(2).Project);
        }
    }
}
