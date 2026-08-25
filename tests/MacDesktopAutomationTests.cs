namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    using Xunit;

    /// <summary>
    /// The helper-client seam, tested through the settable Runner delegate (the
    /// MacPlatformBridgeTests.OsascriptRunner pattern): no helper binary, no AX, no process —
    /// just "given this verb, exactly these arguments go to the helper, and this reply is
    /// interpreted honestly". The argument names asserted here are the same ones
    /// AxBridgeContractTests pins in the Swift source, closing the loop from C# to helper.
    /// </summary>
    public class MacDesktopAutomationTests
    {
        private static (MacDesktopAutomation auto, List<List<String>> calls) Build(params String[] replies)
        {
            var calls = new List<List<String>>();
            var queue = new Queue<String>(replies);
            var auto = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            {
                Runner = (args, _) =>
                {
                    calls.Add(new List<String>(args));
                    return queue.Count > 0 ? queue.Dequeue() : null;
                },
            };
            return (auto, calls);
        }

        [Fact]
        public void Status_sends_every_adapter_label_and_parses_the_reply()
        {
            var (auto, calls) = Build(
                "{\"ok\":true,\"surface\":true,\"approvalPresent\":true,\"cardText\":\"do it\",\"mode\":\"Codex\"}");

            var snap = auto.Status();

            var args = Assert.Single(calls);
            Assert.Equal("status", args[0]);
            Assert.Contains("--app", args);
            Assert.Contains("com.openai.codex", args);
            Assert.Contains("--approve", args);
            Assert.Contains("Allow once", args);
            Assert.Contains("--deny", args);
            Assert.Contains("--stop", args);
            Assert.Contains("--attention", args);
            Assert.Contains("needs attention", args);
            Assert.Contains("--mode-prefix", args);
            Assert.True(snap.SurfaceAvailable);
            Assert.True(snap.ApprovalPresent);
            Assert.Equal("Codex", snap.Mode);
        }

        [Fact]
        public void A_dead_helper_reads_as_unavailable()
        {
            var (auto, _) = Build();   // runner returns null: timeout / missing binary

            Assert.False(auto.Status().SurfaceAvailable);
        }

        [Fact]
        public void Press_passes_each_label_and_reports_the_match()
        {
            var (auto, calls) = Build("{\"ok\":true,\"matched\":\"Allow once\",\"frontAfter\":\"Safari\"}");

            var ok = auto.Press(new[] { "Allow once", "Approve" }, out var matched);

            Assert.True(ok);
            Assert.Equal("Allow once", matched);
            var args = Assert.Single(calls);
            Assert.Equal("press", args[0]);
            Assert.Equal(2, args.FindAll(a => a == "--label").Count);
        }

        [Fact]
        public void A_failed_press_is_a_false_not_an_exception()
        {
            var (auto, _) = Build("{\"ok\":false,\"error\":\"no-match\"}");

            Assert.False(auto.Press(new[] { "Allow once" }, out var matched));
            Assert.Null(matched);
        }

        [Fact]
        public void Write_with_send_names_the_adapters_send_control()
        {
            var (auto, calls) = Build("{\"ok\":true,\"method\":\"value\",\"sent\":true}");

            var ok = auto.WriteComposer("hello from the keypad", send: true, out _);

            Assert.True(ok);
            var args = Assert.Single(calls);
            Assert.Equal("write", args[0]);
            Assert.Contains("--text", args);
            Assert.Contains("hello from the keypad", args);
            Assert.Contains("--send-label", args);
            Assert.Contains("Send", args);
        }

        [Fact]
        public void Write_without_send_omits_the_send_label_entirely()
        {
            var (auto, calls) = Build("{\"ok\":true,\"method\":\"value\",\"sent\":false}");

            Assert.True(auto.WriteComposer("draft only", send: false, out _));
            Assert.DoesNotContain("--send-label", Assert.Single(calls));
        }

        [Fact]
        public void Empty_text_never_reaches_the_helper()
        {
            var (auto, calls) = Build();

            Assert.False(auto.WriteComposer("  ", send: true, out var error));
            Assert.Empty(calls);
            Assert.NotNull(error);
        }

        [Fact]
        public void Switching_mode_presses_the_switcher_then_the_full_menu_label()
        {
            var (auto, calls) = Build(
                "{\"ok\":true,\"matched\":\"Switch mode, current mode: ChatGPT\"}",
                "{\"ok\":true,\"matched\":\"Codex Build, debug, and ship\"}");

            Assert.True(auto.SwitchMode("Codex"));

            Assert.Equal(2, calls.Count);
            Assert.Contains("Switch mode", calls[0]);
            // The DISTINCTIVE menu label, never the bare mode word — "Codex" alone would also
            // match the switcher itself and re-open the menu instead of picking.
            Assert.Contains("Codex Build, debug, and ship", calls[1]);
        }

        [Fact]
        public void An_unknown_mode_is_refused_before_any_press()
        {
            var (auto, calls) = Build();

            Assert.False(auto.SwitchMode("Claude"));
            Assert.Empty(calls);
        }
    }
}
