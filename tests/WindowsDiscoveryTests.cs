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

        // ----------------------------------------------------------------------------------------
        // The other agent. Discovery on Windows carried hardcoded claude names long after macOS
        // went matcher-driven, so the Codex product's Windows build scanned for claude processes
        // and found nothing — profile visible, every key refusing to type (found on hardware,
        // 2026-08-20). These pin the seam: what a matcher names is what discovery finds.
        // ----------------------------------------------------------------------------------------

        [Fact]
        public void A_native_codex_exe_is_a_session_for_the_codex_matcher_only()
        {
            var codex = Proc(4100, "codex.exe",
                @"""C:\Users\sahan\.codex\packages\standalone\releases\0.148.0-x86_64-pc-windows-msvc\bin\codex.exe""");

            Assert.True(WindowsProcessWatcher.IsAgentSession(codex, AgentProcessMatcher.CodexCli));
            Assert.False(WindowsProcessWatcher.IsAgentSession(codex, AgentProcessMatcher.ClaudeCode));
        }

        [Fact]
        public void An_npm_installed_codex_under_node_is_recognised_by_its_script_path()
        {
            // The matcher's hints are written mac-style; the Windows path arrives backslashed.
            var npm = Proc(4200, "node.exe",
                @"""C:\Program Files\nodejs\node.exe"" ""C:\Users\sahan\AppData\Roaming\npm\node_modules\@openai\codex\bin\codex.js""");

            Assert.True(WindowsProcessWatcher.IsAgentSession(npm, AgentProcessMatcher.CodexCli));
            Assert.False(WindowsProcessWatcher.IsAgentSession(npm, AgentProcessMatcher.ClaudeCode));
        }

        [Fact]
        public void A_claude_session_is_invisible_to_the_codex_matcher()
        {
            Assert.False(WindowsProcessWatcher.IsAgentSession(NativeCli(1234), AgentProcessMatcher.CodexCli));
            Assert.False(WindowsProcessWatcher.IsAgentSession(NpmCli(2222), AgentProcessMatcher.CodexCli));
        }

        [Fact]
        public void The_none_matcher_matches_nothing_ever()
        {
            // The engine default. A product that never declared its agent must not adopt sessions.
            Assert.Empty(WindowsProcessWatcher.SessionsFrom(
                new[] { NativeCli(1), NpmCli(2), Proc(3, "codex.exe", @"""C:\x\codex.exe""") },
                AgentProcessMatcher.None));
        }

        [Fact]
        public void Codex_exe_location_is_learned_from_a_live_session()
        {
            var exe = WindowsProcessWatcher.ExeFromCommandLine(
                @"""C:\Users\sahan\.codex\packages\standalone\releases\0.148.0-x86_64-pc-windows-msvc\bin\codex.exe"" --model gpt-5",
                AgentProcessMatcher.CodexCli);

            Assert.Equal(@"C:\Users\sahan\.codex\packages\standalone\releases\0.148.0-x86_64-pc-windows-msvc\bin\codex.exe", exe);
        }

        /// <summary>
        /// The enumerator's name pre-filter must come from the matcher too. It was a hardcoded
        /// claude list until 2026-08-20 — the matcher-driven watcher was correct and never
        /// received a codex row, because the OS was never asked for processes named codex. The
        /// unit tests inject their own process tables, which is exactly why only this derivation
        /// can be pinned here; the lesson is that a seam is only as honest as its narrowest layer.
        /// </summary>
        [Fact]
        public void The_process_scan_asks_the_os_for_the_matchers_names()
        {
            Assert.Contains("codex", WindowsPlatformBridge.NamesWorthEnumerating(AgentProcessMatcher.CodexCli));
            Assert.DoesNotContain("claude", WindowsPlatformBridge.NamesWorthEnumerating(AgentProcessMatcher.CodexCli));

            Assert.Contains("claude", WindowsPlatformBridge.NamesWorthEnumerating(AgentProcessMatcher.ClaudeCode));
            Assert.DoesNotContain("codex", WindowsPlatformBridge.NamesWorthEnumerating(AgentProcessMatcher.ClaudeCode));

            // Interpreters are platform knowledge, present for every agent — an npm install of
            // either CLI runs under node.
            Assert.Contains("node", WindowsPlatformBridge.NamesWorthEnumerating(AgentProcessMatcher.None));
        }

        /// <summary>
        /// Codex ALWAYS runs nested — a TUI process plus a child codex (its app server) — and on
        /// hardware both took a key: "codex codex", with the phantom child's key focusing nothing.
        /// Parents now ride the same batched query as command lines, so the one-key-per-session
        /// rule fires; the child collapses into its parent.
        /// </summary>
        [Fact]
        public void A_codex_tui_and_its_app_server_child_are_one_session()
        {
            var parents = new Dictionary<Int32, Int32> { [14484] = 800, [24628] = 14484 };
            var bridge = new WindowsPlatformBridge(AgentProcessMatcher.CodexCli)
            {
                ProcessEnumerator = () => new[]
                {
                    Proc(14484, "codex.exe"),
                    Proc(24628, "codex.exe"),
                },
                CommandLineResolver = _ => @"C:\Users\sahan\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe",
                ParentPidResolver = pid => parents.TryGetValue(pid, out var p) ? p : 0,
            };

            var sessions = bridge.DiscoverSessions();

            Assert.Single(sessions);
            Assert.Contains(sessions, k => k.StartsWith("pid-14484-", StringComparison.Ordinal));
        }

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
            var sessions = WindowsProcessWatcher.SessionsFrom(new[] { NativeCli(1234) }, AgentProcessMatcher.ClaudeCode);

            Assert.Single(sessions);
        }

        [Fact]
        public void An_npm_install_is_a_session()
        {
            var sessions = WindowsProcessWatcher.SessionsFrom(new[] { NpmCli(2222) }, AgentProcessMatcher.ClaudeCode);

            Assert.Single(sessions);
        }

        [Fact]
        public void An_unrelated_node_process_is_not_a_session()
        {
            var vite = Proc(3333, "node.exe", @"""C:\Program Files\nodejs\node.exe"" ""C:\dev\app\node_modules\vite\bin\vite.js""");

            Assert.Empty(WindowsProcessWatcher.SessionsFrom(new[] { vite }, AgentProcessMatcher.ClaudeCode));
        }

        [Fact]
        public void Claude_desktop_is_never_a_session()
        {
            // The macOS watcher excludes the desktop app via "no controlling terminal"; Windows has
            // no such signal, so this is the explicit replacement for it. A false positive here
            // would put a phantom key on the grid that no keystroke can ever reach.
            Assert.Empty(WindowsProcessWatcher.SessionsFrom(DesktopTree(), AgentProcessMatcher.ClaudeCode));
        }

        [Fact]
        public void Desktop_and_cli_can_run_side_by_side()
        {
            var rows = DesktopTree().Concat(new[] { NativeCli(1234) });

            var sessions = WindowsProcessWatcher.SessionsFrom(rows, AgentProcessMatcher.ClaudeCode);

            Assert.Single(sessions);
            Assert.Contains("pid-1234-", sessions.Single());
        }

        [Fact]
        public void A_process_with_no_readable_command_line_still_counts_when_named_claude()
        {
            // Access-denied on the command line must not lose a real session.
            var sessions = WindowsProcessWatcher.SessionsFrom(new[] { Proc(1234, "claude.exe", cmd: null) }, AgentProcessMatcher.ClaudeCode);

            Assert.Single(sessions);
        }

        [Fact]
        public void An_interpreter_with_no_readable_command_line_is_not_assumed_to_be_claude()
        {
            // The opposite bias: a bare node.exe we can't inspect is far more likely to be someone
            // else's dev server than a Claude session. A phantom key is worse than a missing one.
            Assert.Empty(WindowsProcessWatcher.SessionsFrom(new[] { Proc(3333, "node.exe", cmd: null) }, AgentProcessMatcher.ClaudeCode));
        }

        // --- real hardware capture, 2026-08-07 --------------------------------------------------

        /// <summary>
        /// The actual process table from a Windows 11 machine running 4 Claude Code sessions
        /// alongside Claude Desktop. Captured by `claude-console-inject selftest`.
        ///
        /// This is here because the invented fixtures above MISSED a real bug: Claude Desktop was
        /// installed from the Microsoft Store, so it lives under Program Files\WindowsApps — not
        /// the %LOCALAPPDATA%\AnthropicClaude path the exclusion list assumed. Its Electron
        /// children were excluded (they carry --type=) but the MAIN process carries no switch at
        /// all, so it was being counted as a Claude Code session.
        /// </summary>
        private static IEnumerable<WindowsProcessInfo> RealCapture()
        {
            const String store = @"""C:\Program Files\WindowsApps\Claude_1.26832.0.0_x64__pzs8sxrjxfjjc\app\Claude.exe""";
            return new[]
            {
                // Claude Desktop — the MAIN process. No --type= switch: only the path betrays it.
                Proc(35208, "claude.exe", store),
                // …and its Electron children, which do carry --type=.
                Proc(31564, "claude.exe", store + " --type=crashpad-handler --user-data-"),
                Proc(5308,  "claude.exe", store + " --type=gpu-process --user-data-dir="),
                Proc(35096, "claude.exe", store + " --type=utility --utility-sub-type=ne"),
                Proc(35000, "claude.exe", store + " --type=renderer --user-data-dir="),
                Proc(18964, "claude.exe", store + " --type=renderer --user-data-dir="),
                Proc(21020, "claude.exe", store + " --type=utility --utility-sub-type=vi"),
                Proc(15028, "claude.exe", store + " --type=utility --utility-sub-type=au"),
                Proc(29724, "claude.exe", store + " --type=utility --utility-sub-type=no"),
                Proc(38496, "claude.exe", store + " --type=renderer --user-data-dir="),

                // The four REAL Claude Code CLI sessions.
                Proc(27904, "claude.exe", @"""C:\Users\sahan\.local\bin\claude.exe"""),
                Proc(20748, "claude.exe", @"""C:\Users\sahan\.local\bin\claude.exe"""),
                Proc(36636, "claude.exe", @"""C:\Users\sahan\.local\bin\claude.exe"""),
                Proc(15988, "claude.exe", "claude  --resume 4160c9c8-1896-430d-a56c-b313e3a56e33"),
            };
        }

        [Fact]
        public void The_real_capture_yields_exactly_the_four_cli_sessions()
        {
            var sessions = WindowsProcessWatcher.SessionsFrom(RealCapture(), AgentProcessMatcher.ClaudeCode);

            Assert.Equal(4, sessions.Count);
            foreach (var pid in new[] { 27904, 20748, 36636, 15988 })
            {
                Assert.Contains(sessions, s => s.StartsWith($"pid-{pid}-", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void A_store_installed_claude_desktop_is_not_a_session()
        {
            // The regression. Its executable is ALSO named claude.exe and the main process carries
            // no --type= switch, so only the WindowsApps path distinguishes it. Getting this wrong
            // puts a key on the grid that no keystroke can ever reach.
            var desktopMain = Proc(35208,
                "claude.exe",
                @"""C:\Program Files\WindowsApps\Claude_1.26832.0.0_x64__pzs8sxrjxfjjc\app\Claude.exe""");

            Assert.False(WindowsProcessWatcher.IsAgentSession(desktopMain, AgentProcessMatcher.ClaudeCode));
        }

        [Fact]
        public void A_bare_claude_invocation_is_still_a_session()
        {
            // `claude --resume <id>` has no path at all — it must not be excluded by accident.
            Assert.True(WindowsProcessWatcher.IsAgentSession(
                Proc(15988, "claude.exe", "claude  --resume 4160c9c8-1896-430d-a56c-b313e3a56e33"), AgentProcessMatcher.ClaudeCode));
        }

        [Fact]
        public void Desktop_is_excluded_when_command_lines_arrive_the_way_the_real_scan_supplies_them()
        {
            // THE REGRESSION THAT SHIPPED. The fixtures above hand SessionsFrom a fully-populated
            // command line, but the real enumerator returns rows with CommandLine = null and
            // resolves them separately. An "optimisation" skipped that lookup for claude.exe on the
            // theory it was identifiable by name — so the Desktop markers had nothing to match and
            // all ten Desktop processes became sessions. The unit tests passed the whole time
            // because they never went through the resolve step.
            //
            // So: drive the BRIDGE, with null command lines, exactly as EnumerateProcesses does.
            var real = RealCapture().ToList();
            var cmdlines = real.ToDictionary(r => r.Pid, r => r.CommandLine);

            var bridge = new WindowsPlatformBridge(AgentProcessMatcher.ClaudeCode)
            {
                ProcessEnumerator = () => real.Select(r => Proc(r.Pid, r.Name, cmd: null, start: r.StartTime)),
                CommandLineResolver = pid => cmdlines.TryGetValue(pid, out var c) ? c : null,
            };

            var sessions = bridge.DiscoverSessions();

            Assert.Equal(4, sessions.Count);
            foreach (var pid in new[] { 27904, 20748, 36636, 15988 })
            {
                Assert.Contains(sessions, s => s.StartsWith($"pid-{pid}-", StringComparison.Ordinal));
            }
            // The specific processes that were pinned to slots on real hardware.
            foreach (var desktopPid in new[] { 35208, 31564, 5308 })
            {
                Assert.DoesNotContain(sessions, s => s.StartsWith($"pid-{desktopPid}-", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void Every_candidate_gets_its_command_line_resolved()
        {
            // claude.exe must NOT be skipped: its command line is the only thing separating the
            // CLI from Claude Desktop.
            var asked = new List<Int32>();
            var bridge = new WindowsPlatformBridge(AgentProcessMatcher.ClaudeCode)
            {
                ProcessEnumerator = () => new[] { Proc(1234, "claude.exe"), Proc(3333, "node.exe") },
                CommandLineResolver = pid => { asked.Add(pid); return null; },
            };

            bridge.DiscoverSessions();

            Assert.Contains(1234, asked);
            Assert.Contains(3333, asked);
        }

        // --- one key per session ----------------------------------------------------------------

        [Fact]
        public void A_nested_claude_does_not_take_a_second_key()
        {
            var parent = NativeCli(100);
            var child = Proc(101, "claude.exe", @"""C:\Users\me\.local\bin\claude.exe""", ppid: 100);

            var sessions = WindowsProcessWatcher.SessionsFrom(new[] { parent, child }, AgentProcessMatcher.ClaudeCode);

            Assert.Single(sessions);
            Assert.Contains("pid-100-", sessions.Single());
        }

        [Fact]
        public void Two_real_sessions_get_two_keys()
        {
            var sessions = WindowsProcessWatcher.SessionsFrom(new[] { NativeCli(100), NpmCli(200) }, AgentProcessMatcher.ClaudeCode);

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
            var bridge = new WindowsPlatformBridge(AgentProcessMatcher.ClaudeCode)
            {
                ProcessEnumerator = () => throw new InvalidOperationException("scan blew up"),
            };

            Assert.Null(bridge.DiscoverSessions());
        }

        [Fact]
        public void A_resolved_command_line_is_cached_across_scans()
        {
            var calls = 0;
            var bridge = new WindowsPlatformBridge(AgentProcessMatcher.ClaudeCode)
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
            var bridge = new WindowsPlatformBridge(AgentProcessMatcher.ClaudeCode)
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
        public void The_frontmost_probe_reports_unknown_and_gestures_route_through_the_runner()
        {
            // The frontmost probe must say "I don't know" rather than guess a session; the
            // gestures must go through the injected runner. The runner is NOT optional here:
            // without it this test drove the REAL wt.exe on Windows — every `dotnet test` run
            // opened a stray terminal tab plus a "Could not access starting directory
            // C:\dev\proj" error tab on the developer's screen (seen on hardware 2026-08-07).
            // Tests must never reach the live terminal.
            var runs = new List<List<String>>();
            var bridge = new WindowsPlatformBridge(AgentProcessMatcher.ClaudeCode)
            {
                TerminalRunner = (exe, args) => { runs.Add(args); return true; },
            };

            Assert.Null(bridge.QueryFrontmostSession());
            bridge.Navigate(TerminalAction.NewTab);
            bridge.LaunchClaudeInProject(@"C:\dev\proj");

            Assert.Equal(2, runs.Count);
        }

        // --- learning the install location from a live session ------------------------------------

        [Theory]
        [InlineData("\"D:\\tools\\claude\\claude.exe\" --resume abc", "D:\\tools\\claude\\claude.exe")]
        [InlineData("D:\\tools\\claude\\claude.exe --resume abc", "D:\\tools\\claude\\claude.exe")]
        [InlineData("\"C:\\Users\\me\\.local\\bin\\claude.exe\"", "C:\\Users\\me\\.local\\bin\\claude.exe")]
        [InlineData("claude --resume abc", null)]                    // no directory to learn
        [InlineData("\"C:\\Program Files\\nodejs\\node.exe\" \"C:\\npm\\@anthropic-ai\\claude-code\\cli.js\"", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void The_exe_is_learned_only_from_a_rooted_claude_command_line(String cmd, String expected) =>
            Assert.Equal(expected, WindowsProcessWatcher.ExeFromCommandLine(cmd, AgentProcessMatcher.ClaudeCode));

        [Fact]
        public void Discovery_learns_the_install_location_from_a_live_session_on_any_drive()
        {
            // Nothing may assume Claude lives on C: — the launch keys use whatever exe a REAL
            // session is running, wherever it is installed.
            var previous = WindowsTerminalCli.ObservedClaudeExe;
            try
            {
                WindowsTerminalCli.ObservedClaudeExe = null;
                var bridge = new WindowsPlatformBridge(AgentProcessMatcher.ClaudeCode)
                {
                    ProcessEnumerator = () => new[]
                    {
                        Proc(1234, "claude.exe", @"""D:\tools\claude\claude.exe"" --resume abc"),
                    },
                    CommandLineResolver = _ => null,
                };

                bridge.DiscoverSessions();

                Assert.Equal(@"D:\tools\claude\claude.exe", WindowsTerminalCli.ObservedClaudeExe);
            }
            finally
            {
                WindowsTerminalCli.ObservedClaudeExe = previous;
            }
        }

        [Fact]
        public void Claude_desktops_exe_is_never_learned_as_the_install_location()
        {
            // Desktop's binary is ALSO claude.exe. Learning it would make the launch keys open
            // the desktop app instead of a terminal session.
            var previous = WindowsTerminalCli.ObservedClaudeExe;
            try
            {
                WindowsTerminalCli.ObservedClaudeExe = null;
                var bridge = new WindowsPlatformBridge(AgentProcessMatcher.ClaudeCode)
                {
                    ProcessEnumerator = () => new[]
                    {
                        Proc(900, "claude.exe",
                            @"""C:\Program Files\WindowsApps\Claude_1.26832.0.0_x64__pzs8sxrjxfjjc\app\Claude.exe"""),
                    },
                    CommandLineResolver = _ => null,
                };

                bridge.DiscoverSessions();

                Assert.Null(WindowsTerminalCli.ObservedClaudeExe);
            }
            finally
            {
                WindowsTerminalCli.ObservedClaudeExe = previous;
            }
        }

        // --- the REAL command-line resolver (Windows only — no-ops elsewhere) ---------------------
        //
        // The second regression in this area shipped one layer below the last one: the test above
        // injects CommandLineResolver, so the real batched PowerShell query was never exercised.
        // Its -Command string said `ForEach-Object {{ … }}` — doubled braces, written as if the
        // string were interpolated when that segment is a plain literal — which makes PowerShell
        // emit the scriptblock's TEXT instead of evaluating it. Nothing parsed, every command line
        // stayed null, and the Desktop filter was blind on real hardware while 338 tests passed.
        // These run the actual powershell.exe round trip.

        [Fact]
        public void The_real_batched_resolver_reads_an_actual_command_line()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var resolved = WindowsPlatformBridge.ResolveDetails(new[] { Environment.ProcessId });

            Assert.True(resolved.TryGetValue(Environment.ProcessId, out var details),
                "the batched PowerShell query returned nothing parseable for the current process");
            Assert.False(String.IsNullOrWhiteSpace(details.Cmd));
            Assert.True(details.Ppid > 0, "the batched query must carry the parent pid too");
        }

        [Fact]
        public void A_desktop_marked_process_is_excluded_through_the_real_resolver()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            // A live decoy whose command line carries an Electron --type= switch, discovered the
            // way the real scan discovers everything: name only, command line resolved by the
            // REAL batched query. If that query breaks again, the decoy sails through the Desktop
            // filter and this fails.
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("# --type=renderer decoy for A_desktop_marked_process_is_excluded_through_the_real_resolver\nStart-Sleep -Seconds 60");

            using var decoy = System.Diagnostics.Process.Start(psi);
            try
            {
                var bridge = new WindowsPlatformBridge(AgentProcessMatcher.ClaudeCode)
                {
                    ProcessEnumerator = () => new[]
                    {
                        Proc(decoy.Id, "claude.exe", cmd: null, start: decoy.StartTime),
                    },
                };

                Assert.Empty(bridge.DiscoverSessions());
            }
            finally
            {
                try { decoy.Kill(); } catch { }
            }
        }
    }
}
