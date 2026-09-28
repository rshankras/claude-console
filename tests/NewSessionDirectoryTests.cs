namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    using Loupedeck.ClaudeConsolePlugin.Agents;
    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// #113 (QA 1.6.1 item 4). New Tab, New &lt;agent&gt; and New &lt;agent&gt; (Window) opened in
    /// the home folder — better than the plugin service's folder (#85), but not where projects
    /// live, and Codex trusts whatever folder it starts in: QA's config.toml gained the whole
    /// profile as a trusted project the second they pressed the key. The bridge now chooses: the
    /// routed session's project folder, else the first usable configured root, else home.
    /// </summary>
    public sealed class NewSessionDirectoryTests : IDisposable
    {
        private readonly String _root = Path.Combine(Path.GetTempPath(), "cc-new-session-" + Guid.NewGuid().ToString("N"));
        private readonly String _sessions;
        private readonly String _activity;
        private readonly String _alpha;
        private readonly String _beta;
        private readonly String _rootsFile;
        private readonly PlatformSeamTests.FakePlatformBridge _platform = new();

        public NewSessionDirectoryTests()
        {
            this._sessions = Path.Combine(this._root, "sessions");
            this._activity = Path.Combine(this._root, "activity");
            this._alpha = Path.Combine(this._root, "work", "alpha");
            this._beta = Path.Combine(this._root, "work", "beta");
            this._rootsFile = Path.Combine(this._root, "project-roots");
            foreach (var dir in new[] { this._sessions, this._activity, this._alpha, this._beta })
            {
                Directory.CreateDirectory(dir);
            }
        }

        public void Dispose()
        {
            try { Directory.Delete(this._root, recursive: true); } catch { }
        }

        private void Session(String tty, String projectDir) =>
            File.WriteAllText(Path.Combine(this._sessions, tty + ".json"), JsonSerializer.Serialize(new
            {
                session_id = "sid-" + tty,
                workspace = new { project_dir = projectDir },
                context_window = new { used_percentage = 10 },
            }));

        private BridgeManager Bridge(params String[] live)
        {
            var grid = new SessionRegistry(this._sessions, this._activity, Path.Combine(this._root, "registry.json")) { Agent = new ClaudeCodeAdapter() };
            grid.Refresh(new HashSet<String>(live, StringComparer.Ordinal));
            // A roots file that does not exist unless a test writes one, so the machine's real
            // ~/.claude/claude-console/project-roots can never leak into these results.
            return new BridgeManager(this._platform) { Agent = new ClaudeCodeAdapter(), Grid = grid, ProjectRootsFile = this._rootsFile };
        }

        private Int32 SlotOf(BridgeManager bridge, String tty) =>
            Enumerable.Range(1, SessionRegistry.SlotCount).Single(slot => bridge.Grid.SlotSession(slot)?.SessionKey == tty);

        [Fact]
        public void The_pinned_sessions_project_folder_wins()
        {
            this.Session("ttys001", this._alpha);
            this.Session("ttys002", this._beta);
            var bridge = this.Bridge("ttys001", "ttys002");
            bridge.SelectSlot(this.SlotOf(bridge, "ttys002"));

            Assert.Equal(this._beta, bridge.NewSessionDirectory());
        }

        [Fact]
        public void The_single_obvious_session_counts_as_routed()
        {
            this.Session("ttys001", this._alpha);
            var bridge = this.Bridge("ttys001");

            Assert.Equal(this._alpha, bridge.NewSessionDirectory());
        }

        [Fact]
        public void A_session_sitting_in_the_home_folder_does_not_qualify()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            this.Session("ttys001", home);
            File.WriteAllLines(this._rootsFile, new[] { "# my layout", this._alpha });
            var bridge = this.Bridge("ttys001");

            Assert.Equal(this._alpha, bridge.NewSessionDirectory());
        }

        [Fact]
        public void The_plugin_services_working_directory_never_qualifies()
        {
            // QA's 1.5.3 fossil: config.toml still lists the LPS install folder as a trusted project.
            this.Session("ttys001", Environment.CurrentDirectory);
            var bridge = this.Bridge("ttys001");

            Assert.Null(bridge.NewSessionDirectory());
        }

        [Fact]
        public void Without_a_routed_session_the_first_usable_configured_root_is_used()
        {
            File.WriteAllLines(this._rootsFile, new[] { Path.Combine(this._root, "gone"), "~", this._beta, this._alpha });
            var bridge = this.Bridge();

            // A missing root and the home folder are skipped; order is otherwise the file's.
            Assert.Equal(this._beta, bridge.NewSessionDirectory());
        }

        [Fact]
        public void With_nothing_to_go_on_the_platform_default_applies()
        {
            var bridge = this.Bridge();
            Assert.Null(bridge.NewSessionDirectory());
        }

        [Theory]
        [InlineData(TerminalAction.NewTab)]
        [InlineData(TerminalAction.NewClaudeTab)]
        [InlineData(TerminalAction.NewClaudeWindow)]
        public void Session_opening_gestures_carry_the_directory_to_the_platform(TerminalAction action)
        {
            this.Session("ttys001", this._alpha);
            var bridge = this.Bridge("ttys001");

            bridge.Navigate(action);

            Assert.Equal((action, this._alpha), Assert.Single(this._platform.NavigationTargets));
        }

        [Theory]
        [InlineData(TerminalAction.Activate)]
        [InlineData(TerminalAction.NextTab)]
        [InlineData(TerminalAction.PreviousWindow)]
        public void Other_gestures_carry_no_directory(TerminalAction action)
        {
            this.Session("ttys001", this._alpha);
            var bridge = this.Bridge("ttys001");

            bridge.Navigate(action);

            Assert.Equal((action, (String)null), Assert.Single(this._platform.NavigationTargets));
        }

        [Theory]
        [InlineData(TerminalAction.NewTab)]
        [InlineData(TerminalAction.NewClaudeTab)]
        [InlineData(TerminalAction.NewClaudeWindow)]
        public void Windows_terminal_starts_the_tab_in_the_chosen_directory(TerminalAction action)
        {
            var args = WindowsTerminalCli.ArgsFor(action, @"C:\Users\me\Work\alpha");
            var at = args.IndexOf("-d");
            Assert.Equal(@"C:\Users\me\Work\alpha", args[at + 1]);

            // No choice: the #85 default, never the service's folder.
            var fallback = WindowsTerminalCli.ArgsFor(action, null);
            Assert.Equal(WindowsTerminalCli.StartingDirectory, fallback[fallback.IndexOf("-d") + 1]);
        }
    }
}
