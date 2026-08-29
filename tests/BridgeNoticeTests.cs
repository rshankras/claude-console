namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;

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
            var text = BridgeNotice.Wired(5);

            Assert.Contains("5 hooks", text);
            Assert.Contains("status line", text);
            Assert.Contains("~/.claude/settings.json", text);
            Assert.Contains("backed up", text);
            // The reassurance that matters most to someone who did not ask for the edit.
            Assert.Contains("Your own entries were kept", text);
        }

        [Fact]
        public void The_notice_is_a_card_not_the_manual()
        {
            // The first version was eight lines: the backup's absolute path (with the user's home
            // directory in it) and the undo command, under a button that already led to both. The
            // mechanics belong behind the button; the card says what happened.
            foreach (var text in new[] { BridgeNotice.Wired(5), BridgeNotice.Unwired() })
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
            Assert.Contains("dashes", text);
            Assert.Contains("backed up", text);
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
        public void The_hook_count_in_the_notice_matches_the_hooks_actually_wired()
        {
            // The notice says "5 hooks". That number must be the number of EnsureHook calls in the
            // wiring routine, or the notice lies the day someone adds a sixth.
            var engine = File.ReadAllText(RepoFile("src", "Core", "BridgeManager.cs"));
            var wiring = engine.Substring(engine.IndexOf("private void EnsureBridgeWired()", StringComparison.Ordinal));
            wiring = wiring.Substring(0, wiring.IndexOf("internal const Int32 WiredHookCount", StringComparison.Ordinal));

            var calls = Regex.Matches(wiring, @"changed \|= EnsureHook\(").Count;
            Assert.Equal(BridgeManager.WiredHookCount, calls);
        }

        [Fact]
        public void The_notice_fires_after_the_write_and_clears_on_a_no_change_load()
        {
            var engine = File.ReadAllText(RepoFile("src", "Core", "BridgeManager.cs"));
            var wiring = engine.Substring(engine.IndexOf("private void EnsureBridgeWired()", StringComparison.Ordinal));

            // The write goes through the one door (RewriteSettings), which also reports whether it
            // wrote at all. Both notices must come after it, and each on its own branch.
            var write = wiring.IndexOf("RewriteSettings(Merge", StringComparison.Ordinal);
            var noChange = wiring.IndexOf("if (!wrote)", StringComparison.Ordinal);
            var warn = wiring.IndexOf("Notify?.Invoke(PluginStatus.Warning", StringComparison.Ordinal);
            var clear = wiring.IndexOf("Notify?.Invoke(PluginStatus.Normal, null", StringComparison.Ordinal);

            Assert.True(write > 0 && noChange > 0 && warn > 0 && clear > 0, "the notice calls are missing from the wiring routine");
            // Announce only what actually happened: the Warning comes AFTER the write succeeded...
            Assert.True(warn > write, "the wired notice is posted before the file is written");
            // ...and the clear lives on the no-change branch, between "nothing was written" and the Warning.
            Assert.True(clear > noChange && clear < warn, "the clear is not on the no-change path");
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
