namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Linq;

    using Xunit;

    /// <summary>
    /// Discovering sessions for an agent the scanner was never written for.
    ///
    /// Discovery is the one place that genuinely spans both seams — it is "find agent X on OS Y" —
    /// so the risk is that generalising it quietly loosens the Claude Code rules that took real
    /// field bugs to get right. Both directions are asserted here: Codex is found, and nothing that
    /// was previously excluded starts being included.
    /// </summary>
    public class AgentDiscoveryTests
    {
        // Shaped like real `ps -axo pid=,ppid=,tty=,command=` output.
        private const String Ps = @"
  501     1 ??       /Applications/Claude.app/Contents/MacOS/Claude
  610   600 s001     -zsh
  611   610 s001     codex
  700   699 s002     -zsh
  701   700 s002     /Users/dev/.codex/packages/standalone/releases/0.145.0-aarch64-apple-darwin/bin/codex
  800   799 s003     -zsh
  801   800 s003     claude
  900   899 s004     node /usr/local/lib/node_modules/@openai/codex/bin/codex.js
  950   949 ??       codex
";

        [Fact]
        public void Codex_sessions_are_found_by_the_codex_matcher()
        {
            var ttys = AgentProcessWatcher.TtysFrom(Ps, AgentProcessMatcher.CodexCli);

            Assert.Contains("s001", ttys);
            Assert.Contains("s002", ttys);   // the standalone native binary
            Assert.Contains("s004", ttys);   // npm-installed, running under node
        }

        /// <summary>
        /// Two consoles can be installed side by side, and each grid must show only its own agent.
        /// A Claude key appearing for a Codex session would target the wrong terminal.
        /// </summary>
        [Fact]
        public void Each_matcher_sees_only_its_own_agent()
        {
            var codex = AgentProcessWatcher.TtysFrom(Ps, AgentProcessMatcher.CodexCli);
            var claude = AgentProcessWatcher.TtysFrom(Ps, AgentProcessMatcher.ClaudeCode);

            Assert.DoesNotContain("s003", codex);    // claude's tab
            Assert.DoesNotContain("s001", claude);   // codex's tab
            Assert.Contains("s003", claude);
            Assert.Empty(codex.Intersect(claude));
        }

        /// <summary>
        /// The rule that keeps a GUI app off the session grid. It predates the agent seam and must
        /// survive it: a process with no controlling terminal is not a tab anything can type into.
        /// </summary>
        [Fact]
        public void Processes_without_a_terminal_are_still_excluded()
        {
            var codex = AgentProcessWatcher.TtysFrom(Ps, AgentProcessMatcher.CodexCli);
            var claude = AgentProcessWatcher.TtysFrom(Ps, AgentProcessMatcher.ClaudeCode);

            Assert.DoesNotContain("??", codex);
            Assert.DoesNotContain("??", claude);
            Assert.Equal(3, codex.Count);   // s001, s002, s004 — not the ?? row
        }

        /// <summary>Case-sensitivity is what keeps Claude's desktop binary ("Claude") off the grid.</summary>
        [Fact]
        public void Matching_is_case_sensitive()
        {
            Assert.False(AgentProcessWatcher.IsAgentCommand(
                "/Applications/Claude.app/Contents/MacOS/Claude", AgentProcessMatcher.ClaudeCode));
            Assert.False(AgentProcessWatcher.IsAgentCommand("CODEX", AgentProcessMatcher.CodexCli));
            Assert.True(AgentProcessWatcher.IsAgentCommand("codex", AgentProcessMatcher.CodexCli));
        }

        [Theory]
        [InlineData("codex")]
        [InlineData("/opt/homebrew/bin/codex")]
        [InlineData("codex --ask-for-approval untrusted")]
        [InlineData("node /usr/local/lib/node_modules/@openai/codex/bin/codex.js")]
        [InlineData("node --enable-source-maps /usr/local/lib/node_modules/@openai/codex/bin/codex.js")]
        public void Every_shape_codex_ships_in_is_recognised(String command) =>
            Assert.True(AgentProcessWatcher.IsAgentCommand(command, AgentProcessMatcher.CodexCli), command);

        [Theory]
        [InlineData("-zsh")]
        [InlineData("vim codex.md")]                       // a file that merely shares the name
        [InlineData("grep codex notes.txt")]
        [InlineData("node server.js --codex")]             // a flag, not the script
        [InlineData("claude")]                             // the other agent
        public void Nothing_that_merely_mentions_codex_is_a_session(String command) =>
            Assert.False(AgentProcessWatcher.IsAgentCommand(command, AgentProcessMatcher.CodexCli), command);

        /// <summary>
        /// Only the first non-flag argument is the script. Without that stop, `node app.js` followed
        /// by an argument containing "/.codex/" would register a stray process as a live session.
        /// </summary>
        [Fact]
        public void Only_the_script_argument_is_examined_not_every_argument() =>
            Assert.False(AgentProcessWatcher.IsAgentCommand(
                "node /srv/app/server.js --config /Users/dev/.codex/settings.json",
                AgentProcessMatcher.CodexCli));

        /// <summary>A nested agent process must not become a second key for the same tab.</summary>
        [Fact]
        public void A_nested_session_does_not_double_up_a_tab()
        {
            var ps = "  100    99 s005     codex\n  101   100 s005     codex exec inner\n";

            var rows = AgentProcessWatcher.Parse(ps, AgentProcessMatcher.CodexCli);

            Assert.Single(rows);
            Assert.Equal("100", rows[0].Pid);
        }

        /// <summary>
        /// No matcher means NO agent, never a default one. Defaulting to Claude Code is what let an
        /// undeclared product adopt Claude's sessions — the engine must name no agent anywhere.
        /// </summary>
        [Fact]
        public void The_default_matcher_matches_nothing()
        {
            Assert.False(AgentProcessWatcher.IsAgentCommand("claude"));
            Assert.False(AgentProcessWatcher.IsAgentCommand("codex"));
            Assert.Empty(AgentProcessWatcher.TtysFrom(Ps));
        }
    }
}
