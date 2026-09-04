namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// The user is told, in Options+, when the plugin edits their Claude Code settings (#31).
    ///
    /// The notice is the platform's answer to "prompt before modifying user config": a keypad plugin
    /// cannot show a dialog, but Plugin.OnPluginStatusChanged reaches the Options+ message centre with
    /// a link. These pin that the words say what was done and that it is reversible, that the card
    /// stays a card (the backup path and the undo command live behind its button, in the README),
    /// and that the engine fires the notice at the right moments and only through the delegate a
    /// product installs, so the engine never has to know which product it is.
    /// </summary>
    public class BridgeNoticeTests
    {
        [Fact]
        public void The_wired_notice_says_what_was_added_and_that_it_is_reversible()
        {
            foreach (var text in new[] { BridgeNotice.Wired(5, settingsApplyLive: true), BridgeNotice.Wired(5, settingsApplyLive: false) })
            {
                Assert.Contains("5 hooks", text);
                Assert.Contains("status line", text);
                Assert.Contains("~/.claude/settings.json", text);
                Assert.Contains("backed up", text);
                // The reassurance that matters most to someone who did not ask for the edit.
                Assert.Contains("Your own entries were kept", text);
            }
        }

        [Fact]
        public void The_wired_notice_promises_a_restart_only_where_the_platform_needs_one()
        {
            // macOS was measured on 2026-09-02: a running session picked the hooks up by itself, so
            // "your next Claude Code session" there sends the user to restart for nothing (#58).
            // Windows still has QA's word that a restart was needed, so the sentence stays there.
            Assert.Contains("next activity", BridgeNotice.Wired(5, settingsApplyLive: true));
            Assert.DoesNotContain("next Claude Code session", BridgeNotice.Wired(5, settingsApplyLive: true));
            Assert.Contains("next Claude Code session", BridgeNotice.Wired(5, settingsApplyLive: false));
        }

        [Fact]
        public void The_answer_keys_notice_names_the_switch_and_changes_nothing_by_itself()
        {
            // Posted when Yes/No is pressed before setup (#58): it must say which keys are the
            // switch, and that reading the card has not edited anything.
            var text = BridgeNotice.AnswerNeedsSetup();

            Assert.Contains("Yes and No", text);
            Assert.Contains("Turn on", text);
            Assert.Contains("~/.claude/settings.json", text);
            Assert.Contains("nothing changes until you do", text);
            // Seen on the hardware pass: the generic button read "What was changed, and how to undo
            // it" under a card that had changed nothing. This card has its own button.
            Assert.NotEqual(BridgeNotice.SupportTitle, BridgeNotice.AnswerNeedsSetupTitle);
            Assert.DoesNotContain("changed", BridgeNotice.AnswerNeedsSetupTitle);
        }

        [Fact]
        public void The_notice_is_a_card_not_the_manual()
        {
            // The first version was eight lines: the backup's absolute path (with the user's home
            // directory in it) and the undo command, under a button that already led to both. The
            // mechanics belong behind the button; the card says what happened.
            foreach (var text in new[] { BridgeNotice.Wired(5, true), BridgeNotice.Wired(5, false), BridgeNotice.Unwired(), BridgeNotice.PressAgain("Activity", 10), BridgeNotice.AnswerNeedsSetup() })
            {
                Assert.DoesNotContain("/Users/", text);
                Assert.DoesNotContain("uninstall.sh", text);
                Assert.DoesNotContain(".bak", text);
                Assert.True(text.Length <= 260, $"{text.Length} chars — that is a paragraph, not a notice");
            }
        }

        [Fact]
        public void The_unwired_notice_says_the_keys_will_go_dark()
        {
            var text = BridgeNotice.Unwired();

            Assert.Contains("removed its status line and hooks", text);
            Assert.Contains("read Off", text);
            Assert.Contains("backed up", text);
            // A user pressed Disable, or created the marker by hand — either way it is not "an opt-out file".
            Assert.DoesNotContain("opt-out file", text);
        }

        [Fact]
        public void The_press_again_notice_is_the_prompt_with_the_change_in_it()
        {
            var text = BridgeNotice.PressAgain("Cost", 10);

            Assert.StartsWith("Press Cost again within 10 s", text);
            Assert.Contains("5 hooks and a status line", text);
            Assert.Contains("~/.claude/settings.json", text);
            Assert.Contains("backed up first", text);
            Assert.Contains("Nothing changes until then", text);
            Assert.True(text.Length <= 260, $"{text.Length} chars — that is a paragraph, not a notice");
        }

        [Fact]
        public void The_support_link_points_at_the_bridge_section_of_the_readme()
        {
            Assert.StartsWith("https://github.com/rshankras/claude-console#", BridgeNotice.SupportUrl);
            Assert.EndsWith("the-live-status-bridge", BridgeNotice.SupportUrl);

            // ...and that anchor exists. A dead "how to undo" link is worse than none.
            var readme = File.ReadAllText(RepoFile("README.md"));
            var at = readme.IndexOf("## The live status bridge", StringComparison.Ordinal);
            Assert.True(at >= 0, "the README section the button opens is missing");

            // The card no longer carries the backup's name or the undo command, so the section it
            // opens must — or the button leads somewhere that does not answer its own title.
            var section = readme.Substring(at);
            var next = section.IndexOf("\n## ", 1, StringComparison.Ordinal);
            section = next > 0 ? section.Substring(0, next) : section;
            Assert.Contains("settings.json.claude-console.bak", section);
            Assert.Contains("uninstall.sh --unwire", section);
            Assert.Contains("no-autowire", section);
        }

        [Fact]
        public void The_windows_terminal_notice_names_the_requirement_and_links_to_existing_guidance()
        {
            var text = BridgeNotice.WindowsTerminalRequired("no Windows Terminal window is running");

            Assert.Contains("open Windows Terminal window", text);
            Assert.Contains("Direct typing keys", text);
            Assert.Contains("no Windows Terminal window is running", text);
            Assert.EndsWith("#windows-notes", BridgeNotice.WindowsTerminalUrl);

            var readme = File.ReadAllText(RepoFile("README.md"));
            Assert.Contains("## Windows notes", readme);
            Assert.Contains("Windows: session/navigation keys beep", readme);
            Assert.Contains("Elevated sessions cannot be controlled", readme);
        }

        [Theory]
        [InlineData("ClaudeConsole")]
        [InlineData("VizhiCodex")]
        public void Every_windows_product_description_names_windows_terminal(String product)
        {
            var metadata = File.ReadAllText(RepoFile(
                "src", "Products", product, "package", "metadata", "LoupedeckPackage.yaml"));

            Assert.Contains("description:", metadata);
            Assert.Contains("Windows Terminal", metadata);
        }

        [Fact]
        public void The_hook_count_in_the_notice_matches_the_hooks_actually_wired()
        {
            // The notice says "5 hooks". That number is the length of the one table both the wirer
            // and the detector read — and the wirer must ITERATE that table rather than list hooks
            // inline, or the notice (and the detector) lie the day someone adds a sixth by hand.
            Assert.Equal(BridgeManager.WiredHookCount, BridgeWiring.HookSpecs.Length);
            Assert.Equal(
                new[] { "UserPromptSubmit", "PostToolUse", "Notification", "Stop", "PermissionRequest" },
                BridgeWiring.HookSpecs.Select(s => s.Event).ToArray());

            var engine = File.ReadAllText(RepoFile("src", "Core", "BridgeManager.cs"));
            var wiring = engine.Substring(engine.IndexOf("private WiringOutcome EnsureBridgeWired()", StringComparison.Ordinal));
            wiring = wiring.Substring(0, wiring.IndexOf("private Boolean UnwireIfWired()", StringComparison.Ordinal));

            Assert.Contains("foreach (var spec in BridgeWiring.HookSpecs)", wiring);
            Assert.Single(Regex.Matches(wiring, @"changed \|= EnsureHook\("));
        }

        [Fact]
        public void The_wired_notice_is_posted_only_by_enable_and_only_after_the_write()
        {
            var engine = File.ReadAllText(RepoFile("src", "Core", "BridgeManager.cs"));

            // Since 2.2.0 nothing on the load path posts the "we changed your file" card, because the
            // load path no longer changes the file. Enable does — and says so after, not before.
            var enable = engine.Substring(engine.IndexOf("internal Boolean EnableLiveStatus()", StringComparison.Ordinal));
            enable = enable.Substring(0, enable.IndexOf("internal Boolean DisableLiveStatus()", StringComparison.Ordinal));

            var write = enable.IndexOf("this.EnsureBridgeWired()", StringComparison.Ordinal);
            var card = enable.IndexOf("Notify?.Invoke(PluginStatus.Warning, BridgeNotice.Wired(", StringComparison.Ordinal);
            Assert.True(write > 0 && card > 0, "Enable no longer wires, or no longer says so");
            Assert.True(card > write, "the wired notice is posted before the file is written");

            // Warning is the only level Options+ renders a card at (a Normal + message post shows
            // nothing — device, 2026-08-29). It badges the All Actions tile, so every load clears it:
            // the badge means "since the last change", not "forever".
            var load = engine.Substring(engine.IndexOf("private void LoadWiring()", StringComparison.Ordinal));
            load = load.Substring(0, load.IndexOf("internal void RunLoadWiringForTests()", StringComparison.Ordinal));
            Assert.Contains("Notify?.Invoke(PluginStatus.Normal, null", load);
            Assert.DoesNotContain("Notify?.Invoke(PluginStatus.Normal, BridgeNotice.", engine);
        }

        [Fact]
        public void Only_the_product_delivers_it_the_engine_never_calls_the_sdk_directly()
        {
            // Products own Plugin.OnPluginStatusChanged; the engine must go through Notify so it
            // stays neutral to which product is running (CLAUDE.md's two seams).
            var engine = File.ReadAllText(RepoFile("src", "Core", "BridgeManager.cs"));
            Assert.DoesNotContain("OnPluginStatusChanged(", engine);

            var product = File.ReadAllText(RepoFile("src", "Products", "ClaudeConsole", "ClaudeConsolePlugin.cs"));
            Assert.Contains("BridgeManager.Instance.Notify =", product);
            Assert.Contains("this.OnPluginStatusChanged(status, message, url, title)", product);
        }

        [Fact]
        public void The_windows_terminal_notice_names_the_fix_and_says_typing_still_works()
        {
            var text = BridgeNotice.WindowsTerminalRequired("wt.exe is unavailable");

            // What the user must DO, and the reassurance that the plugin is not dead — the retest's
            // "documented but still silent at runtime" (#33) is answered by a card that says both.
            Assert.Contains("navigation keys", text);
            Assert.Contains("Windows Terminal", text);
            Assert.Contains("Settings", text);
            Assert.Contains("typing", text);
            Assert.True(text.Length <= 360, $"{text.Length} chars — that is a paragraph, not a notice");
        }

        [Fact]
        public void The_windows_notice_link_points_at_a_readme_section_that_exists()
        {
            Assert.StartsWith("https://github.com/rshankras/claude-console#", BridgeNotice.WindowsTerminalUrl);
            Assert.EndsWith("windows-notes", BridgeNotice.WindowsTerminalUrl);

            var readme = File.ReadAllText(RepoFile("README.md"));
            Assert.Contains("## Windows notes", readme);
        }

        [Fact]
        public void The_windows_terminal_warning_is_surfaced_not_only_logged()
        {
            // The whole point of #33's fix: the platform detects a press that cannot land (no
            // Windows Terminal window, or wt.exe failing) and says why; the engine beeps every time
            // and posts the message-centre card once, rather than only writing a log line.
            var engine = File.ReadAllText(RepoFile("src", "Core", "BridgeManager.cs"));
            Assert.Contains("windows.TerminalUnavailable = detail =>", engine);
            Assert.Contains("BridgeNotice.WindowsTerminalRequired(detail)", engine);
            Assert.Contains("this._platform.Alert();", engine);
            // Posted once, not on every nav press.
            Assert.Contains("_warnedNoTerminal", engine);

            var win = File.ReadAllText(RepoFile("src", "Core", "Platform", "WindowsPlatformBridge.cs"));
            // The probe runs BEFORE wt.exe: a zero exit is not proof the press landed anywhere.
            Assert.Contains("if (requiresExistingWindow && !this.HasTerminalWindow())", win);
            Assert.Contains("this.TerminalUnavailable?.Invoke(detail)", win);
        }

        private static String RepoFile(params String[] relative)
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir, Path.Combine(relative));
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate " + Path.Combine(relative));
        }
    }
}
