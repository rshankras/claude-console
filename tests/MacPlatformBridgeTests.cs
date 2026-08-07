namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// The macOS backend in isolation — the half of the old BridgeManager that knows about
    /// AppleScript, osascript and TTYs. InjectionGuardTests covers the safety property end to end;
    /// this covers the backend's own translation duties: neutral key → key code, osascript result
    /// → outcome, raw tty → session id.
    /// </summary>
    public class MacPlatformBridgeTests
    {
        private static MacPlatformBridge WithResult(String result, List<List<String>> seen = null) =>
            new MacPlatformBridge
            {
                OsascriptRunner = (args, timeout, wantOutput) =>
                {
                    seen?.Add(args);
                    return result;
                },
            };

        // ---------------------------------------------------------------------------------------
        // Key mapping
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Every_neutral_key_has_a_macOS_mapping()
        {
            // Guards the enum against growing a member with no key code — which would otherwise
            // throw inside an SDK callback on the first press of that key.
            foreach (var key in Enum.GetValues<TerminalKey>())
            {
                var spec = MacPlatformBridge.AppleScriptFor(new KeyStroke(key));

                Assert.StartsWith("key code ", spec);
                Assert.Matches(@"^key code \d+$", spec);
            }
        }

        [Fact]
        public void Distinct_keys_map_to_distinct_key_codes()
        {
            var codes = Enum.GetValues<TerminalKey>()
                .Select(k => MacPlatformBridge.AppleScriptFor(new KeyStroke(k)))
                .ToList();

            Assert.Equal(codes.Count, codes.Distinct().Count());
        }

        [Theory]
        [InlineData(KeyModifiers.Shift, "key code 48 using {shift down}")]
        [InlineData(KeyModifiers.Control, "key code 48 using {control down}")]
        [InlineData(KeyModifiers.Command, "key code 48 using {command down}")]
        [InlineData(KeyModifiers.Alt, "key code 48 using {option down}")]
        [InlineData(KeyModifiers.Control | KeyModifiers.Shift, "key code 48 using {control down, shift down}")]
        public void Modifiers_render_in_a_fixed_order(KeyModifiers mods, String expected) =>
            Assert.Equal(expected, MacPlatformBridge.AppleScriptFor(new KeyStroke(TerminalKey.Tab, mods)));

        [Fact]
        public void An_unmodified_key_carries_no_using_clause() =>
            Assert.DoesNotContain("using", MacPlatformBridge.AppleScriptFor(KeyStroke.Escape));

        // ---------------------------------------------------------------------------------------
        // Outcome mapping — the vocabulary a Windows backend must mirror
        // ---------------------------------------------------------------------------------------

        [Theory]
        [InlineData("ok", InjectionOutcome.Ok)]
        [InlineData("no-terminal", InjectionOutcome.NoTerminal)]   // Terminal.app isn't running
        [InlineData("tab-missing", InjectionOutcome.SessionMissing)] // the tracked tab is gone
        [InlineData(null, InjectionOutcome.Failed)]                // osascript itself failed
        [InlineData("something-unexpected", InjectionOutcome.Failed)]
        public void Guard_results_map_to_outcomes(String scriptResult, InjectionOutcome expected)
        {
            var mac = WithResult(scriptResult);

            Assert.Equal(expected, mac.InjectKey("ttys001", KeyStroke.Return));
            Assert.Equal(expected, mac.InjectText("ttys001", "hi", pressEnter: false));
            Assert.Equal(expected, mac.InjectTabThenEnter("ttys001"));
        }

        [Fact]
        public void Empty_text_is_skipped_without_touching_the_os()
        {
            var calls = new List<List<String>>();
            var mac = WithResult("ok", calls);

            Assert.Equal(InjectionOutcome.Skipped, mac.InjectText("ttys001", "", pressEnter: true));
            Assert.Equal(InjectionOutcome.Skipped, mac.InjectText("ttys001", null, pressEnter: true));
            Assert.Empty(calls);
        }

        // ---------------------------------------------------------------------------------------
        // Session ids
        // ---------------------------------------------------------------------------------------

        [Theory]
        [InlineData("/dev/ttys003", "ttys003")]  // osascript form
        [InlineData("ttys003", "ttys003")]       // ps form
        [InlineData("  /dev/ttys012\n", "ttys012")]
        [InlineData("", null)]
        [InlineData("   ", null)]
        [InlineData(null, null)]
        [InlineData("??", null)]                 // no controlling terminal — not a session
        public void Tty_normalization_yields_the_session_id(String raw, String expected) =>
            Assert.Equal(expected, MacPlatformBridge.NormalizeTty(raw));

        [Fact]
        public void The_frontmost_probe_normalizes_what_osascript_returns()
        {
            var mac = WithResult("/dev/ttys005");

            Assert.Equal("ttys005", mac.QueryFrontmostSession());
        }

        [Fact]
        public void The_frontmost_probe_never_auto_launches_the_terminal()
        {
            // Querying must not be able to LAUNCH Terminal.app — that would pop a window on a poll.
            var calls = new List<List<String>>();
            WithResult("", calls).QueryFrontmostSession();

            Assert.Contains("if application \"Terminal\" is running then", calls.Single()[1]);
        }

        [Fact]
        public void Session_discovery_parses_the_ps_table()
        {
            var mac = new MacPlatformBridge
            {
                PsRunner = () =>
                    "  501   500 ttys003  /opt/homebrew/bin/node /opt/homebrew/bin/claude\n" +
                    "  502   500 ttys004  /opt/homebrew/bin/node /opt/homebrew/bin/claude\n" +
                    "  600   500 ??       /Applications/Claude.app/Contents/MacOS/Claude\n",
            };

            var sessions = mac.DiscoverSessions();

            Assert.Equal(new[] { "ttys003", "ttys004" }, sessions.OrderBy(s => s));
        }

        [Fact]
        public void A_failed_ps_scan_reports_null_not_an_empty_set()
        {
            // "I don't know" — an empty set would tell the registry to reap every live session.
            var mac = new MacPlatformBridge { PsRunner = () => null };

            Assert.Null(mac.DiscoverSessions());
        }

        // ---------------------------------------------------------------------------------------
        // Navigation
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Every_navigation_action_produces_a_script()
        {
            // Same guard as the key mapping: a new TerminalAction with no case would silently
            // do nothing on the hardware.
            foreach (var action in Enum.GetValues<TerminalAction>())
            {
                var calls = new List<List<String>>();
                WithResult("ok", calls).Navigate(action);

                var script = Assert.Single(calls)[1];
                Assert.Contains("Terminal", script);
            }
        }

        [Theory]
        [InlineData(TerminalAction.NewClaudeTab)]
        [InlineData(TerminalAction.NewClaudeWindow)]
        public void Starting_a_session_uses_do_script_rather_than_typed_keystrokes(TerminalAction action)
        {
            // `do script` sends the command atomically; per-character keystrokes race the shell.
            var calls = new List<List<String>>();
            WithResult("ok", calls).Navigate(action);

            Assert.Contains("do script \"claude\"", Assert.Single(calls)[1]);
        }

        [Fact]
        public void Opening_a_project_never_types_into_a_busy_tab()
        {
            // The reuse-an-idle-tab optimisation must be conditioned on `busy`, or a project launch
            // would inject `cd ... && claude` into a live session's prompt.
            var calls = new List<List<String>>();
            WithResult("ok", calls).LaunchClaudeInProject("/Users/me/my project");

            var script = Assert.Single(calls)[1];
            Assert.Contains("busy of selected tab of front window is false", script);
            Assert.Contains("cd '/Users/me/my project' && claude", script);
        }
    }
}
