namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// Windows terminal control (Phase 3). Windows Terminal has no automation API, so `wt.exe` is
    /// the whole toolbox — which makes what the keypad CAN'T do as important to pin as what it can.
    /// These tests cover both: the commands we send, and the two gestures we deliberately refuse
    /// to fake.
    /// </summary>
    public class WindowsTerminalTests
    {
        private static (WindowsPlatformBridge Bridge, List<List<String>> Runs) Rig()
        {
            var runs = new List<List<String>>();
            var bridge = new WindowsPlatformBridge
            {
                TerminalRunner = (exe, args) =>
                {
                    Assert.Equal("wt.exe", exe);
                    runs.Add(args);
                    return true;
                },
            };
            return (bridge, runs);
        }

        // ---------------------------------------------------------------------------------------
        // Navigation
        // ---------------------------------------------------------------------------------------

        [Theory]
        [InlineData(TerminalAction.Activate)]
        [InlineData(TerminalAction.NewTab)]
        [InlineData(TerminalAction.NewClaudeTab)]
        [InlineData(TerminalAction.NextTab)]
        [InlineData(TerminalAction.PreviousTab)]
        [InlineData(TerminalAction.NewClaudeWindow)]
        public void Supported_gestures_produce_a_command(TerminalAction action)
        {
            var (bridge, runs) = Rig();

            bridge.Navigate(action);

            Assert.Single(runs);
        }

        [Theory]
        [InlineData(TerminalAction.NextWindow)]
        [InlineData(TerminalAction.PreviousWindow)]
        public void Window_cycling_is_refused_rather_than_faked(TerminalAction action)
        {
            // Cycling WINDOWS is an OS-level gesture; wt.exe cannot express it. Sending some
            // approximation (a new window, a tab switch) would be worse than doing nothing —
            // the user would learn the key lies. It logs and no-ops instead.
            var (bridge, runs) = Rig();

            bridge.Navigate(action);

            Assert.Empty(runs);
        }

        [Fact]
        public void Tab_gestures_act_on_the_window_the_user_is_using()
        {
            // Without "-w 0" every command spawns a NEW terminal window instead of acting on the
            // current one — the single easiest way to get this wrong.
            foreach (var action in new[] { TerminalAction.NewTab, TerminalAction.NextTab, TerminalAction.PreviousTab })
            {
                var args = WindowsTerminalCli.ArgsFor(action);

                Assert.Equal("-w", args[0]);
                Assert.Equal("0", args[1]);
            }
        }

        [Fact]
        public void A_new_claude_window_asks_for_a_new_window()
        {
            var previous = WindowsTerminalCli.FileExists;
            try
            {
                WindowsTerminalCli.FileExists = _ => false;   // no native install → bare "claude"
                var args = WindowsTerminalCli.ArgsFor(TerminalAction.NewClaudeWindow);

                Assert.Equal(new[] { "-w", "new" }, args.Take(2));
                Assert.Contains("claude", args);
            }
            finally
            {
                WindowsTerminalCli.FileExists = previous;
            }
        }

        [Fact]
        public void Next_and_previous_tab_differ()
        {
            // A copy-paste slip here gives two keys that do the same thing, which reads as a
            // dead key rather than a bug.
            Assert.NotEqual(
                WindowsTerminalCli.ArgsFor(TerminalAction.NextTab),
                WindowsTerminalCli.ArgsFor(TerminalAction.PreviousTab));
        }

        [Fact]
        public void Starting_a_session_asks_for_claude_by_name()
        {
            var previous = WindowsTerminalCli.FileExists;
            try
            {
                WindowsTerminalCli.FileExists = _ => false;   // no native install → bare "claude"
                var args = WindowsTerminalCli.ArgsFor(TerminalAction.NewClaudeTab);

                Assert.Contains("new-tab", args);
                Assert.Contains("claude", args);
            }
            finally
            {
                WindowsTerminalCli.FileExists = previous;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Which "claude" a new tab runs
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void A_new_tab_runs_the_native_exe_by_full_path_when_it_is_installed()
        {
            // THE BUG THAT SHIPPED: the tab command was the bare word "claude", which wt resolves
            // against the PATH it inherited — from LogiPluginService, whose PATH lacks the
            // installer's %USERPROFILE%\.local\bin entry. Every launch key produced
            // `[error 2147942402 (0x80070002) when launching `claude']` in the new tab.
            var native = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "bin", "claude.exe");

            var previous = WindowsTerminalCli.FileExists;
            try
            {
                WindowsTerminalCli.FileExists = path => path == native;

                Assert.Contains(native, WindowsTerminalCli.ArgsFor(TerminalAction.NewClaudeTab));
                Assert.Contains(native, WindowsTerminalCli.ArgsFor(TerminalAction.NewClaudeWindow));
                Assert.Contains(native, WindowsTerminalCli.LaunchClaudeArgs(@"C:\dev\proj"));
            }
            finally
            {
                WindowsTerminalCli.FileExists = previous;
            }
        }

        [Fact]
        public void Without_a_native_install_the_tab_falls_back_to_PATH()
        {
            // An npm-managed setup has no .local\bin\claude.exe; the bare name is the best we
            // can do there, and it is what the pre-fix behavior was for everyone.
            var previous = WindowsTerminalCli.FileExists;
            try
            {
                WindowsTerminalCli.FileExists = _ => false;

                Assert.Contains("claude", WindowsTerminalCli.LaunchClaudeArgs(@"C:\dev\proj"));
            }
            finally
            {
                WindowsTerminalCli.FileExists = previous;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Opening a project
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Opening_a_project_passes_the_path_as_its_own_argument()
        {
            // A path with spaces must not need quoting gymnastics, and must not be able to break
            // out into extra arguments.
            var (bridge, runs) = Rig();

            bridge.LaunchClaudeInProject(@"C:\Users\me\My Projects\thing & co");

            var args = Assert.Single(runs);
            Assert.Contains(@"C:\Users\me\My Projects\thing & co", args);
            Assert.Equal("-d", args[args.IndexOf(@"C:\Users\me\My Projects\thing & co") - 1]);
        }

        [Fact]
        public void Opening_a_project_always_uses_a_new_tab()
        {
            // macOS can ask Terminal whether the front tab is idle and reuse it; Windows Terminal
            // exposes no such signal. Guessing wrong would type `cd … && claude` into a LIVE
            // session's prompt, so we always take a fresh tab. A spare tab is the cheap mistake.
            var args = WindowsTerminalCli.LaunchClaudeArgs(@"C:\dev\proj");

            Assert.Contains("new-tab", args);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Opening_a_project_with_no_path_does_nothing(String path)
        {
            var (bridge, runs) = Rig();

            bridge.LaunchClaudeInProject(path);

            Assert.Empty(runs);
        }

        // ---------------------------------------------------------------------------------------
        // The two honest gaps (R1)
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void The_frontmost_session_is_reported_as_unknown_not_guessed()
        {
            // Nothing supported maps a terminal TAB to the process inside it. Returning a guess
            // would aim the typing keys at the wrong session; null makes BridgeManager fall
            // through to its other targeting rules (single session, single waiting session, or an
            // explicit pin), all of which are correct.
            Assert.Null(new WindowsPlatformBridge().QueryFrontmostSession());
        }

        [Fact]
        public void Targeting_still_works_for_a_single_session_without_a_frontmost_probe()
        {
            // The practical consequence of gap 1: one session needs no pin. This is the common
            // case, and it must not regress just because Windows can't see the foreground tab.
            var fake = new PlatformSeamTests.FakePlatformBridge { FrontmostToReport = null };
            var bridge = new BridgeManager(fake);
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cc-wt-" + Guid.NewGuid().ToString("N"));
            var sessions = System.IO.Path.Combine(root, "sessions");
            var activity = System.IO.Path.Combine(root, "activity");
            System.IO.Directory.CreateDirectory(sessions);
            System.IO.Directory.CreateDirectory(activity);

            try
            {
                bridge.Grid = new SessionRegistry(sessions, activity, System.IO.Path.Combine(root, "registry.json"));
                bridge.Grid.Refresh(new HashSet<String> { "pid-1234-638900000000000000" });

                bridge.SendPrompt("hello");

                Assert.Equal("pid-1234-638900000000000000", fake.Texts.Single().Session);
            }
            finally
            {
                try { System.IO.Directory.Delete(root, recursive: true); } catch { /* best effort */ }
            }
        }

        // ---------------------------------------------------------------------------------------
        // Focusing a session's tab — the focus helper, and the wt window-raise it degrades to
        // ---------------------------------------------------------------------------------------

        private const String FocusKey = "pid-1234-638900000000000000";

        [Fact]
        public void Focus_args_carry_the_pid_and_start_ticks_guard()
        {
            // Same contract as injection: a recycled PID must never yank an unrelated window to
            // the foreground, so the helper re-verifies the pair before touching anything.
            Assert.Equal(
                new[] { "tab", "--pid", "1234", "--start-ticks", "638900000000000000" },
                WindowsInjection.FocusArgs(FocusKey));

            Assert.Null(WindowsInjection.FocusArgs("not-a-session-key"));
        }

        [Fact]
        public void A_selected_tab_needs_no_wt_fallback()
        {
            var (bridge, runs) = Rig();
            List<String> focusArgs = null;
            bridge.FocusRunner = args => { focusArgs = args; return 0; };

            bridge.FocusSession(FocusKey);

            Assert.NotNull(focusArgs);
            Assert.Empty(runs);   // the helper did the whole job
        }

        [Fact]
        public void A_raised_but_unidentified_tab_needs_no_wt_fallback_either()
        {
            // Exit 4 = the helper already raised the terminal window; running wt on top would
            // add nothing and can flick focus twice.
            var (bridge, runs) = Rig();
            bridge.FocusRunner = _ => 4;

            bridge.FocusSession(FocusKey);

            Assert.Empty(runs);
        }

        [Theory]
        [InlineData(2)]      // session gone
        [InlineData(5)]      // elevated
        [InlineData(null)]   // helper crashed, or its Desktop runtime is missing
        public void A_failed_focus_helper_falls_back_to_raising_the_window(Object exitCode)
        {
            var (bridge, runs) = Rig();
            bridge.FocusRunner = _ => (Int32?)exitCode;

            bridge.FocusSession(FocusKey);

            var args = Assert.Single(runs);
            Assert.Contains("focus-tab", args);
        }

        [Fact]
        public void A_missing_focus_helper_degrades_to_the_pre_helper_behavior()
        {
            // No helper in the package and no injected runner: raise the window via wt, exactly
            // what every 1.8.x before the helper did.
            var (bridge, runs) = Rig();
            bridge.FocusHelperPath = null;

            bridge.FocusSession(FocusKey);

            var args = Assert.Single(runs);
            Assert.Contains("focus-tab", args);
        }

        [Fact]
        public void Focusing_an_unparseable_session_does_nothing()
        {
            var (bridge, runs) = Rig();
            var helperRan = false;
            bridge.FocusRunner = _ => { helperRan = true; return 0; };

            bridge.FocusSession("ttys003");   // a macOS key

            Assert.False(helperRan);
            Assert.Empty(runs);
        }

        // ---------------------------------------------------------------------------------------
        // Contract with the focus helper (a separate executable; the compiler can't check this)
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void The_focus_helper_verifies_the_session_before_touching_any_window()
        {
            Assert.Contains("VerifyStartTime", ReadFocusSource());
        }

        [Fact]
        public void The_focus_helper_matches_tabs_by_the_sessions_console_title()
        {
            // The whole mechanism: the tab's label IS the console title. If either half drifts
            // (title read, UIA tab match), focus silently degrades to window-raising.
            var source = ReadFocusSource();

            Assert.Contains("GetConsoleTitleW", source);
            Assert.Contains("CASCADIA_HOSTING_WINDOW_CLASS", source);
            Assert.Contains("SelectionItemPattern", source);
        }

        [Fact]
        public void The_focus_helper_survives_claudes_animated_title()
        {
            // A busy Claude repaints the title's leading glyph (✳ · ✢ …), so the console read and
            // the UIA walk can straddle a repaint. Seen on hardware 2026-08-07: the idle session's
            // tab matched, the busy one's didn't. The helper must retry with a FRESH read and
            // compare glyph-stripped cores, or focus works only for idle sessions.
            var source = ReadFocusSource();

            Assert.Contains("TitleCore", source);
            Assert.Contains("ConsoleTitleOf(pid).title", source);   // re-read inside the retry loop
        }

        [Fact]
        public void The_focus_helpers_exit_codes_match_what_the_bridge_expects()
        {
            // 0 = tab selected, 4 = raised only — the two codes FocusSession treats as "done".
            var source = ReadFocusSource();

            Assert.Contains("ExitOk = 0", source);
            Assert.Contains("ExitRaisedOnly = 4", source);
            Assert.Contains("ExitSessionMissing = 2", source);
            Assert.Contains("ExitSessionElevated = 5", source);
        }

        private static String ReadFocusSource()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = System.IO.Path.Combine(dir, "tools", "windows", "ClaudeConsoleFocus", "Program.cs");
                if (System.IO.File.Exists(candidate))
                {
                    return System.IO.File.ReadAllText(candidate);
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate the focus helper source");
        }
    }
}
