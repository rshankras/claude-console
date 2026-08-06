namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Linq;

    using Xunit;

    /// <summary>
    /// Finding the Terminal tabs that are running Claude Code. This is what tells the grid a session
    /// has ended, so a false positive parks a dead session on a key forever and a false negative
    /// wipes a live one — both are visible on the hardware.
    ///
    /// The fixtures below are REAL `ps -axo pid=,ppid=,tty=,command=` output captured on this Mac
    /// (desktop-app lines abbreviated only in their argument tails), not invented strings.
    /// </summary>
    public class ProcessWatcherTests
    {
        // Real capture: one Claude Code CLI session on ttys000, plus the Claude DESKTOP app, whose
        // Electron processes all run without a controlling terminal.
        private const String RealPsOutput = @"
95147     1 ??       /Applications/Claude.app/Contents/MacOS/Claude
95157     1 ??       /Applications/Claude.app/Contents/Frameworks/Electron Framework.framework/Helpers/chrome_crashpad_handler --no-rate-limit
95159 95147 ??       /Applications/Claude.app/Contents/Frameworks/Claude Helper.app/Contents/MacOS/Claude Helper --type=gpu-process
95177 95147 ??       /Applications/Claude.app/Contents/Frameworks/Claude Helper (Renderer).app/Contents/MacOS/Claude Helper (Renderer) --type=renderer
86714 82013 ttys000  claude
 1234  1200 ttys000  -zsh
";

        [Fact]
        public void Finds_the_cli_session()
        {
            var ttys = ClaudeProcessWatcher.TtysFrom(RealPsOutput);

            Assert.Equal(new[] { "ttys000" }, ttys.OrderBy(t => t).ToArray());
        }

        [Fact]
        public void Ignores_the_claude_desktop_app()
        {
            // The desktop app runs a dozen processes whose paths contain "Claude". They have no
            // controlling terminal, so they can never be a session the keypad types into.
            var ttys = ClaudeProcessWatcher.TtysFrom(RealPsOutput);

            Assert.DoesNotContain("??", ttys);
            Assert.Single(ttys);
        }

        [Fact]
        public void Desktop_binary_is_not_mistaken_for_the_cli_even_on_a_tty()
        {
            // Belt and braces: if the desktop app were ever launched from a terminal it would have a
            // TTY. Its binary is "Claude" (capital C) and the match is case-sensitive.
            var ttys = ClaudeProcessWatcher.TtysFrom(
                "500 1 ttys009  /Applications/Claude.app/Contents/MacOS/Claude\n");

            Assert.Empty(ttys);
        }

        [Theory]
        [InlineData("claude")]
        [InlineData("claude --resume")]
        [InlineData("/usr/local/bin/claude")]
        [InlineData("/Users/x/.local/bin/claude --model opus")]
        [InlineData("node /opt/homebrew/lib/node_modules/@anthropic-ai/claude-code/cli.js")]
        [InlineData("node --enable-source-maps /Users/x/.claude/local/claude")]
        [InlineData("bun /Users/x/.claude/local/claude")]
        public void Recognises_the_ways_claude_code_is_launched(String command)
        {
            Assert.True(ClaudeProcessWatcher.IsClaudeCommand(command), command);
        }

        [Theory]
        [InlineData("-zsh")]
        [InlineData("/bin/bash")]
        [InlineData("vim claude-notes.md")]                       // filename mentions claude
        [InlineData("node /Users/x/projects/server.js")]
        [InlineData("/Applications/Claude.app/Contents/MacOS/Claude")]
        [InlineData("grep claude")]
        public void Ignores_everything_else(String command)
        {
            Assert.False(ClaudeProcessWatcher.IsClaudeCommand(command), command);
        }

        [Fact]
        public void Keeps_one_row_per_tab_when_a_session_spawns_a_nested_claude()
        {
            // Only the top-level session should own the tab's key.
            var ps = "100 1 ttys003  claude\n200 100 ttys003  claude --print\n";

            var rows = ClaudeProcessWatcher.Parse(ps);

            Assert.Single(rows);
            Assert.Equal("100", rows[0].Pid);
        }

        [Fact]
        public void Reports_each_tab_running_a_session()
        {
            var ps = "100 1 ttys000  claude\n300 1 ttys004  claude\n500 1 ttys007  -zsh\n";

            Assert.Equal(new[] { "ttys000", "ttys004" },
                ClaudeProcessWatcher.TtysFrom(ps).OrderBy(t => t).ToArray());
        }

        [Fact]
        public void Normalises_a_full_device_path()
        {
            Assert.Equal(new[] { "ttys002" },
                ClaudeProcessWatcher.TtysFrom("100 1 /dev/ttys002  claude\n").ToArray());
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("garbage that is not ps output\n\n")]
        public void Survives_unusable_input(String ps)
        {
            Assert.Empty(ClaudeProcessWatcher.TtysFrom(ps));
        }
    }
}
