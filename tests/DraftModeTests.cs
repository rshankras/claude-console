namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    using Xunit;

    /// <summary>
    /// Draft mode — typing WITHOUT submitting, so the user can correct a voice transcript or extend
    /// a canned prompt before it goes. Two entry points share the mechanism: the Voice Draft key
    /// (StopVoiceCapture(submit: false)) and a prompts.json entry with "submit": false. The property
    /// under test: draft paths must NOT press Return (key code 36), submit paths must.
    /// </summary>
    public class DraftModeTests
    {
        private sealed class Capture
        {
            public List<String> Args { get; private set; }

            public String Script => this.Args != null && this.Args.Count > 1 ? this.Args[1] : null;

            public BridgeManager Bridge(String activeTty = "ttys001")
            {
                var bridge = new BridgeManager { ActiveTty = activeTty };
                bridge.OsascriptRunner = (args, timeout, wantOutput) =>
                {
                    this.Args = args;
                    return "ok";
                };
                return bridge;
            }
        }

        [Fact]
        public void Draft_typing_never_presses_return()
        {
            var capture = new Capture();

            capture.Bridge().InjectText("Explain how this code works", pressEnter: false);

            Assert.Contains("keystroke (item 2 of argv)", capture.Script);
            Assert.DoesNotContain("key code 36", capture.Script);
        }

        [Fact]
        public void Submit_typing_presses_return_after_the_text()
        {
            var capture = new Capture();

            capture.Bridge().InjectText("Explain how this code works", pressEnter: true);

            var script = capture.Script;
            Assert.InRange(
                script.IndexOf("keystroke (item 2 of argv)", StringComparison.Ordinal),
                0,
                script.IndexOf("key code 36", StringComparison.Ordinal) - 1);
        }

        // --- prompts.json "submit" flag ---------------------------------------------------------

        [Fact]
        public void Prompt_without_submit_field_still_submits()
        {
            // Every prompts.json in the wild predates the flag — absent MUST mean today's
            // behaviour, or the update silently turns users' prompt keys into draft keys.
            var p = JsonSerializer.Deserialize<PromptDef>(
                """{"id":"review","label":"Review","prompt":"Review this code"}""");

            Assert.Null(p.Submit);
            Assert.True(p.Submits);
        }

        [Fact]
        public void Prompt_with_submit_false_drafts()
        {
            var p = JsonSerializer.Deserialize<PromptDef>(
                """{"id":"review","label":"Review","prompt":"Review this code","submit":false}""");

            Assert.False(p.Submits);
        }

        [Fact]
        public void Prompt_with_submit_true_submits()
        {
            var p = JsonSerializer.Deserialize<PromptDef>(
                """{"id":"review","label":"Review","prompt":"Review this code","submit":true}""");

            Assert.True(p.Submits);
        }
    }
}
