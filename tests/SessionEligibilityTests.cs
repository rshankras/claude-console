namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// A session takes a Session key only if the keys can reach it (#29).
    ///
    /// On QA's machine six cmux sessions filled every slot, so the two real Terminal.app sessions
    /// could never get a key — and pressing a key pinned a TTY the AppleScript could not find,
    /// silently. The discovery now walks each session's parent chain to the terminal application
    /// that owns it and asks the platform whether it can drive that one. The chains below are the
    /// real shapes: `claude → zsh → login → Terminal.app` from this Mac, iTerm2 and VS Code from
    /// their documented process trees, tmux from the fact that its server is reparented to launchd.
    /// </summary>
    public class SessionEligibilityTests
    {
        private const String Listing = @"
    1     0 ??       /sbin/launchd
 2164     1 ??       /System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal
 2269  2164 ttys000  login -pf ravi
 2287  2269 ttys000  -zsh
 3701  2287 ttys000  claude
 4000     1 ??       /Applications/iTerm.app/Contents/MacOS/iTerm2
 4010  4000 ttys004  login -fp ravi
 4020  4010 ttys004  -zsh
 4030  4020 ttys004  claude
 5000     1 ??       /Applications/Visual Studio Code.app/Contents/MacOS/Electron
 5010  5000 ??       /Applications/Visual Studio Code.app/Contents/Frameworks/Code Helper (Plugin).app/Contents/MacOS/Code Helper (Plugin) --type=utility
 5020  5010 ttys007  /bin/zsh -l
 5030  5020 ttys007  claude
 6000     1 ??       tmux
 6010  6000 ttys009  -zsh
 6020  6010 ttys009  claude
";

        private static readonly Func<String, Boolean> TerminalOnly = MacPlatformBridge.IsTerminalApp;

        [Fact]
        public void Only_the_terminal_app_session_gets_a_key()
        {
            var found = AgentProcessWatcher.Discover(Listing, AgentProcessMatcher.ClaudeCode, TerminalOnly);

            Assert.Equal(new[] { "ttys000" }, found.Ttys.OrderBy(t => t).ToArray());
        }

        [Fact]
        public void The_skipped_sessions_are_named_by_the_application_that_owns_them()
        {
            // THE important one: the failure was silent. Each skipped session must say which app
            // has it, so the log line can tell a user why their iTerm2 session has no key.
            var found = AgentProcessWatcher.Discover(Listing, AgentProcessMatcher.ClaudeCode, TerminalOnly);

            var byTty = found.Skipped.ToDictionary(s => s.Tty, s => s.Owner);
            Assert.Equal("iTerm", byTty["ttys004"]);
            Assert.Equal("Visual Studio Code", byTty["ttys007"]);
            // tmux's server is reparented to launchd: no application anywhere in the chain. That is
            // still "cannot reach" — Terminal's tty-of-tab is the OUTER pty, never claude's.
            Assert.True(byTty.ContainsKey("ttys009"));
            Assert.Null(byTty["ttys009"]);
        }

        [Fact]
        public void The_editor_session_is_attributed_to_the_editor_not_its_helper()
        {
            // VS Code's terminal is spawned by a helper bundle nested inside the app bundle. The
            // first .app binary up the chain is that helper, which is still a "Visual Studio Code"
            // bundle path — the name must come from the outermost .app segment the user knows.
            var found = AgentProcessWatcher.Discover(Listing, AgentProcessMatcher.ClaudeCode, TerminalOnly);

            Assert.Equal("Visual Studio Code", found.Skipped.Single(s => s.Tty == "ttys007").Owner);
        }

        [Fact]
        public void No_filter_means_every_session_as_before()
        {
            // Windows keys sessions on console identity and has its own version of this problem
            // (#33). Passing no predicate must keep the pre-#29 behaviour exactly.
            var found = AgentProcessWatcher.Discover(Listing, AgentProcessMatcher.ClaudeCode, drivableOwner: null);

            Assert.Equal(new[] { "ttys000", "ttys004", "ttys007", "ttys009" }, found.Ttys.OrderBy(t => t).ToArray());
            Assert.Empty(found.Skipped);
            Assert.Equal(found.Ttys, AgentProcessWatcher.TtysFrom(Listing, AgentProcessMatcher.ClaudeCode));
        }

        [Fact]
        public void A_nested_claude_still_counts_once_and_inherits_the_tab()
        {
            var listing = Listing + " 3800  3701 ttys000  claude --resume\n";

            var found = AgentProcessWatcher.Discover(listing, AgentProcessMatcher.ClaudeCode, TerminalOnly);

            Assert.Single(found.Ttys);
            Assert.Contains("ttys000", found.Ttys);
        }

        [Fact]
        public void A_truncated_or_cyclic_listing_cannot_spin_the_poll()
        {
            // A parent that is missing from the listing, and a chain that loops: both must end,
            // as "no owner", not as a hang on the poll thread.
            var missingParent = " 7000  6999 ttys011  claude\n";
            var cycle = " 8000  8001 ttys012  claude\n 8001  8000 ttys012  -zsh\n";

            var found = AgentProcessWatcher.Discover(missingParent + cycle, AgentProcessMatcher.ClaudeCode, TerminalOnly);

            Assert.Empty(found.Ttys);
            Assert.All(found.Skipped, s => Assert.Null(s.Owner));
        }

        [Theory]
        [InlineData("/System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal", true)]
        [InlineData("/Applications/iTerm.app/Contents/MacOS/iTerm2", false)]
        [InlineData("/Applications/Ghostty.app/Contents/MacOS/ghostty", false)]
        [InlineData("/Applications/Terminal Helper.app/Contents/MacOS/Terminal Helper", false)]   // not the real thing
        [InlineData(null, false)]
        public void Terminal_app_is_recognised_by_its_bundle_path(String owner, Boolean drivable)
        {
            Assert.Equal(drivable, MacPlatformBridge.IsTerminalApp(owner));
        }

        [Theory]
        [InlineData("/Applications/iTerm.app/Contents/MacOS/iTerm2", "iTerm")]
        [InlineData("/Applications/Visual Studio Code.app/Contents/MacOS/Electron", "Visual Studio Code")]
        [InlineData("/Applications/cmux.app/Contents/MacOS/cmux --flag", "cmux")]
        public void The_owner_name_is_the_bundle_name_a_user_would_recognise(String command, String name)
        {
            Assert.Equal(name, AgentProcessWatcher.AppName(command));
        }
    }
}
