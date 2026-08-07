namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// Windows session discovery (Phase 1). The decision layer is pure, so these run on macOS —
    /// which is the point: the parts that can't be exercised without Windows hardware are kept to
    /// one thin enumerator, and everything that decides what counts as a session is covered here.
    ///
    /// Fixtures model the three installs that coexist in the wild: the native installer
    /// (claude.exe), an npm install (node.exe + cli.js), and Claude Desktop's Electron tree, which
    /// must never be mistaken for a session.
    /// </summary>
    public class WindowsDiscoveryTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 7, 9, 0, 0, DateTimeKind.Utc);

        private static WindowsProcessInfo Proc(
            Int32 pid, String name, String cmd = null, Int32 ppid = 0, DateTime? start = null) =>
            new WindowsProcessInfo
            {
                Pid = pid,
                ParentPid = ppid,
                Name = name,
                CommandLine = cmd,
                StartTime = start ?? T0,
            };

        // --- fixtures -------------------------------------------------------------------------

        private static WindowsProcessInfo NativeCli(Int32 pid, DateTime? start = null) =>
            Proc(pid, "claude.exe", @"""C:\Users\me\.local\bin\claude.exe""", start: start);

        private static WindowsProcessInfo NpmCli(Int32 pid, DateTime? start = null) =>
            Proc(pid, "node.exe",
                @"""C:\Program Files\nodejs\node.exe"" ""C:\Users\me\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\cli.js""",
                start: start);

        private static IEnumerable<WindowsProcessInfo> DesktopTree() => new[]
        {
            Proc(900, "claude.exe", @"""C:\Users\me\AppData\Local\AnthropicClaude\app-1.4.0\claude.exe"""),
            Proc(901, "claude.exe", @"""C:\Users\me\AppData\Local\AnthropicClaude\app-1.4.0\claude.exe"" --type=renderer", ppid: 900),
            Proc(902, "claude.exe", @"""C:\Users\me\AppData\Local\AnthropicClaude\app-1.4.0\claude.exe"" --type=gpu-process", ppid: 900),
        };

        // --- what counts as a session -----------------------------------------------------------

        [Fact]
        public void The_native_cli_is_a_session()
        {
            var sessions = WindowsProcessWatcher.SessionsFrom(new[] { NativeCli(1234) });

            Assert.Single(sessions);
        }

        [Fact]
        public void An_npm_install_is_a_session()
        {
            var sessions = WindowsProcessWatcher.SessionsFrom(new[] { NpmCli(2222) });

            Assert.Single(sessions);
        }

        [Fact]
        public void An_unrelated_node_process_is_not_a_session()
        {
            var vite = Proc(3333, "node.exe", @"""C:\Program Files\nodejs\node.exe"" ""C:\dev\app\node_modules\vite\bin\vite.js""");

            Assert.Empty(WindowsProcessWatcher.SessionsFrom(new[] { vite }));
        }

        [Fact]
        public void Claude_desktop_is_never_a_session()
        {
            // The macOS watcher excludes the desktop app via "no controlling terminal"; Windows has
            // no such signal, so this is the explicit replacement for it. A false positive here
            // would put a phantom key on the grid that no keystroke can ever reach.
            Assert.Empty(WindowsProcessWatcher.SessionsFrom(DesktopTree()));
        }

        [Fact]
        public void Desktop_and_cli_can_run_side_by_side()
        {
            var rows = DesktopTree().Concat(new[] { NativeCli(1234) });

            var sessions = WindowsProcessWatcher.SessionsFrom(rows);

            Assert.Single(sessions);
            Assert.Contains("pid-1234-", sessions.Single());
        }

        [Fact]
        public void A_process_with_no_readable_command_line_still_counts_when_named_claude()
        {
            // Access-denied on the command line must not lose a real session.
            var sessions = WindowsProcessWatcher.SessionsFrom(new[] { Proc(1234, "claude.exe", cmd: null) });

            Assert.Single(sessions);
        }

        [Fact]
        public void An_interpreter_with_no_readable_command_line_is_not_assumed_to_be_claude()
        {
            // The opposite bias: a bare node.exe we can't inspect is far more likely to be someone
            // else's dev server than a Claude session. A phantom key is worse than a missing one.
            Assert.Empty(WindowsProcessWatcher.SessionsFrom(new[] { Proc(3333, "node.exe", cmd: null) }));
        }

        // --- one key per session ----------------------------------------------------------------

        [Fact]
        public void A_nested_claude_does_not_take_a_second_key()
        {
            var parent = NativeCli(100);
            var child = Proc(101, "claude.exe", @"""C:\Users\me\.local\bin\claude.exe""", ppid: 100);

            var sessions = WindowsProcessWatcher.SessionsFrom(new[] { parent, child });

            Assert.Single(sessions);
            Assert.Contains("pid-100-", sessions.Single());
        }

        [Fact]
        public void Two_real_sessions_get_two_keys()
        {
            var sessions = WindowsProcessWatcher.SessionsFrom(new[] { NativeCli(100), NpmCli(200) });

            Assert.Equal(2, sessions.Count);
        }

        // --- session keys -----------------------------------------------------------------------

        [Fact]
        public void A_recycled_pid_is_a_different_session()
        {
            // Windows reuses PIDs aggressively. Without the start time in the key, a new session
            // would inherit the dead one's slot AND its pin — so keys would silently target the
            // wrong terminal. This is the whole reason the key is not just the PID.
            var before = NativeCli(1234, start: T0);
            var after = NativeCli(1234, start: T0.AddMinutes(5));

            Assert.NotEqual(
                WindowsProcessWatcher.SessionKeyFor(before),
                WindowsProcessWatcher.SessionKeyFor(after));
        }

        [Fact]
        public void A_session_key_is_stable_across_scans()
        {
            var p = NativeCli(1234);

            Assert.Equal(WindowsProcessWatcher.SessionKeyFor(p), WindowsProcessWatcher.SessionKeyFor(NativeCli(1234)));
        }

        [Theory]
        [InlineData(1234)]
        [InlineData(99999)]
        public void A_session_key_is_filename_safe(Int32 pid)
        {
            // The key becomes "<key>.json" under sessions/ and activity/, so it must survive being
            // a Windows filename — no colons (which a "pid:123:456" form would have had).
            var key = WindowsProcessWatcher.SessionKeyFor(NativeCli(pid));

            Assert.Equal(-1, key.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()));
            Assert.DoesNotContain(":", key);
        }

        // --- the bridge's discovery path --------------------------------------------------------

        [Fact]
        public void Discovery_reports_null_when_the_scan_throws()
        {
            // "I don't know" — not "no sessions", which would reap every live session.
            var bridge = new WindowsPlatformBridge
            {
                ProcessEnumerator = () => throw new InvalidOperationException("scan blew up"),
            };

            Assert.Null(bridge.DiscoverSessions());
        }

        [Fact]
        public void Discovery_resolves_command_lines_only_for_ambiguous_processes()
        {
            // The native install is identifiable by name alone. Resolving a command line costs a
            // subprocess, so the common case must not pay for it on a 2-second timer.
            var asked = new List<Int32>();
            var bridge = new WindowsPlatformBridge
            {
                ProcessEnumerator = () => new[]
                {
                    Proc(1234, "claude.exe"),   // unambiguous
                    Proc(3333, "node.exe"),     // ambiguous — needs the command line
                },
                CommandLineResolver = pid => { asked.Add(pid); return null; },
            };

            bridge.DiscoverSessions();

            Assert.Equal(new[] { 3333 }, asked);
        }

        [Fact]
        public void A_resolved_command_line_is_cached_across_scans()
        {
            var calls = 0;
            var bridge = new WindowsPlatformBridge
            {
                ProcessEnumerator = () => new[] { Proc(3333, "node.exe") },
                CommandLineResolver = pid =>
                {
                    calls++;
                    return @"node.exe ""C:\npm\@anthropic-ai\claude-code\cli.js""";
                },
            };

            var first = bridge.DiscoverSessions();
            var second = bridge.DiscoverSessions();

            Assert.Single(first);
            Assert.Single(second);
            Assert.Equal(1, calls);   // the poll runs every ~2s; this must not spawn a process each time
        }

        [Fact]
        public void The_command_line_cache_does_not_grow_without_bound()
        {
            // A long-running plugin sees many short-lived node processes. Entries for processes
            // that are gone must not accumulate for the life of the session.
            var pid = 4000;
            var bridge = new WindowsPlatformBridge
            {
                ProcessEnumerator = () => new[] { Proc(pid, "node.exe") },
                CommandLineResolver = _ => null,
            };

            for (var i = 0; i < 50; i++)
            {
                bridge.DiscoverSessions();
                pid++;   // every scan sees a brand-new process
            }

            Assert.Equal(1, bridge.CommandLineCacheCount);
        }

        [Fact]
        public void Navigation_is_still_unimplemented_and_says_so()
        {
            // Phase 3. Until then a nav key must be a logged no-op, and the frontmost probe must
            // report "unknown" rather than guessing a session.
            var bridge = new WindowsPlatformBridge();

            Assert.Null(bridge.QueryFrontmostSession());
            bridge.Navigate(TerminalAction.NewTab);       // no-op, must not throw
            bridge.LaunchClaudeInProject(@"C:\dev\proj");
        }
    }
}
