namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;

    public class DesktopComposerTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Draft_write_and_retry_pass_observed_placeholder_without_requesting_send(Boolean retry)
        {
            var calls = new List<List<String>>();
            var automation = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            {
                Runner = (args, _) => { calls.Add(args); return "{\"ok\":true,\"method\":\"paste\",\"sent\":false}"; },
            };
            Assert.True(retry ? automation.RecoverDraft("my words", out _)
                : automation.WriteComposer("my words", false, out _));
            var call = Assert.Single(calls);
            Assert.Equal("Work with ChatGPT", call[call.IndexOf("--draft-placeholder") + 1]);
            Assert.Equal("Send", call[call.IndexOf("--composer-send-label") + 1]);
            Assert.DoesNotContain("--send-label", call);
            Assert.Equal(retry, call.Contains("--accept-existing"));
        }

        [Theory]
        [InlineData("write", false, false)]
        [InlineData("write", true, false)]
        [InlineData("recover", false, true)]
        [InlineData("draft", false, false)]
        [InlineData("draft", false, true)]
        [InlineData("prompt", false, false)]
        [InlineData("prompt", true, false)]
        [InlineData("append", false, false)]
        [InlineData("append", false, true)]
        public void Every_text_entry_api_passes_one_complete_long_prompt_with_its_guards(String path, Boolean send, Boolean retry)
        {
            var text = "  " + String.Concat(Enumerable.Repeat("Long instruction: café தமிழ் 😀.\n", 150)) + "  ";
            var calls = new List<List<String>>();
            var automation = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            { Runner = (args, _) => { calls.Add(args); return "{\"ok\":true,\"sent\":false}"; } };
            var ok = path switch
            {
                "write" => automation.WriteComposer(text, send, out _),
                "recover" => automation.RecoverDraft(text, out _),
                "draft" => automation.WritePreparedDraft(text, "Codex", "original-chat", retry, out _),
                "prompt" => automation.WritePreparedPrompt(text, "Codex", "original-chat", send, out _),
                _ => automation.AppendPreparedDraft(text, "Codex", new() { Target = "original-chat", Fingerprint = "original-draft" }, retry, out _),
            };
            Assert.True(ok);
            var call = Assert.Single(calls);
            Assert.Equal(text, call[call.IndexOf("--text") + 1]);
            Assert.Equal(1, call.Count(a => a == "--text"));
            Assert.Equal(send, call.Contains("--send-label"));
            Assert.Equal(retry, call.Contains("--accept-existing"));
            Assert.Equal(new OpenAiDesktopAdapter().ModePrefix, call[call.IndexOf("--mode-prefix") + 1]);
            Assert.Equal("Pin chat", call[call.IndexOf("--conv-marker") + 1]);
            Assert.Contains("--stop", call); Assert.Contains("--approve", call); Assert.Contains("--voice-end", call);
            if (path is "draft" or "prompt" or "append")
            {
                Assert.Equal("Codex", call[call.IndexOf("--expect-mode") + 1]);
                Assert.Equal("original-chat", call[call.IndexOf("--expect-target") + 1]);
            }
        }

        [Theory]
        [InlineData("{\"ok\":true,\"sent\":true}", true, null)]
        [InlineData("{\"ok\":false,\"error\":\"composer-target-changed\"}", false, "composer-target-changed")]
        [InlineData("{\"ok\":false,\"error\":\"no-sendable-draft\"}", false, "no-sendable-draft")]
        public void Sending_is_a_single_guarded_verb_without_replacing_text(String reply, Boolean success, String failure)
        {
            var calls = new List<List<String>>();
            var automation = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            {
                Runner = (args, _) => { calls.Add(args); return reply; },
            };
            Assert.Equal(success, automation.SendComposer(out var error));
            Assert.Equal(failure, error);
            var call = Assert.Single(calls);
            Assert.Equal("send", call[0]);
            Assert.Contains("--send-label", call);
            Assert.Contains("--stop", call);
            Assert.Contains("--approve", call);
            Assert.DoesNotContain("--text", call);
        }

        [Theory]
        [InlineData("screenshot", 120000)]
        [InlineData("paste", 6000)]
        [InlineData("copy", 6000)]
        public void Context_passes_literal_arguments_and_bounded_timeout(String action, Int32 expectedTimeout)
        {
            var automation = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            { Runner = (args, timeout) => {
                Assert.Equal(expectedTimeout, timeout);
                    Assert.Equal(action == "copy" ? "copy-reply" : "context-" + action, args[0]);
                Assert.Equal(action == "copy" ? "com.openai.codex" : "@frontmost", args[args.IndexOf("--app")+1]);
                Assert.Equal("literal `$text`\nreply", args[args.IndexOf("--text")+1]);
                Assert.DoesNotContain("--send-label", args);
                return "{\"ok\":true}";
            } };
            Assert.True(automation.Context(action, "opaque-source", "literal `$text`\nreply").Ok);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("not-json")]
        [InlineData("{}")]
        public void An_inconclusive_image_helper_result_never_allows_blind_repeat(String reply)
        {
            var automation = new MacDesktopAutomation(new OpenAiDesktopAdapter()) { Runner = (_,_) => reply };
            Assert.False(automation.AttachPreparedImage("/fixture/image.png", "ChatGPT", "target", out var error));
            Assert.Equal("attachment-unconfirmed", error);
        }

        [Fact]
        public void Spoken_workflow_pins_mode_and_target_without_sending_until_explicit_submission()
        {
            var calls = new List<List<String>>();
            var automation = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            { Runner = (args, _) => { calls.Add(args); return "{\"ok\":true,\"target\":\"original-editor\",\"sent\":true}"; } };
            Assert.Equal("original-editor", automation.PrepareDraft("Codex", true, out _));
            Assert.DoesNotContain("--allow-existing", calls[0]);
            Assert.True(automation.WritePreparedDraft("complete brief", "Codex", "original-editor", false, out _));
            Assert.DoesNotContain("--send-label", calls[1]);
            Assert.DoesNotContain("--accept-existing", calls[1]);
            Assert.True(automation.SendPreparedDraft("Codex", "original-editor", out _));
            Assert.Equal("send", calls[2][0]);
            foreach (var call in calls)
                Assert.Equal("Codex", call[call.IndexOf("--expect-mode") + 1]);
            foreach (var call in calls.Skip(1))
                Assert.Equal("original-editor", call[call.IndexOf("--expect-target") + 1]);
        }

        [Fact]
        public void Unsupported_copy_never_uses_generic_press()
        {
            IDesktopAutomation automation = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            {
                Runner = (_, _) => throw new InvalidOperationException("Must not guess a copy selector"),
            };
            Assert.False(automation.CopyAnswer(out var error));
            Assert.Equal("unsupported", error);
        }

        [Theory]
        [InlineData(false, false, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        public void Busy_or_approval_state_disables_submission_and_copy(Boolean busy, Boolean approval, Boolean enabled)
        {
            var state = DesktopMonitor.Map(new DesktopSnapshot
            {
                SurfaceAvailable = true, StopPresent = busy, ApprovalPresent = approval,
                CanSend = true, CanCopyAnswer = true, Mode = "ChatGPT",
            });
            Assert.Equal(enabled, state.CanSend);
            Assert.Equal(enabled, state.CanCopyAnswer);
            var send = DesktopComposerCommand.FaceFor("send", state);
            var copy = DesktopComposerCommand.FaceFor("output", state, supportsCopyAnswer: true);
            Assert.Equal("Send Draft", send.Label);
            Assert.Equal("Copy Answer", copy.Label);
            Assert.Equal(enabled, send.Enabled);
            Assert.Equal(enabled, copy.Enabled);
            Assert.Equal(approval ? "Approval" : busy ? "Busy" : null, send.Status);
            Assert.Equal(send.Status, copy.Status);
        }

        [Fact]
        public void Snapshot_absent_flags_fail_closed_and_present_flags_parse()
        {
            var missing = DesktopSnapshot.Parse("{\"ok\":true,\"surface\":true}");
            Assert.False(missing.CanSend);
            Assert.False(missing.CanCopyAnswer);
            var present = DesktopSnapshot.Parse("{\"ok\":true,\"surface\":true,\"canSend\":true,\"canCopyAnswer\":true}");
            Assert.True(present.CanSend);
            Assert.True(present.CanCopyAnswer);
        }

        [Fact]
        public void Output_is_mode_aware_and_unknown_mode_does_not_guess()
        {
            var changes = DesktopComposerCommand.FaceFor("output", DesktopMonitor.Map(new DesktopSnapshot
            { SurfaceAvailable = true, Mode = "Codex", AvailableControls = DesktopControl.Changes }));
            Assert.Equal("View Changes", changes.Label);
            Assert.True(changes.Enabled);
            var empty = DesktopComposerCommand.FaceFor("output", DesktopMonitor.Map(new DesktopSnapshot
            { SurfaceAvailable = true, Mode = "Codex" }));
            Assert.Equal(changes.Label, empty.Label);
            Assert.Equal("Not available", empty.Status);
            Assert.False(empty.Enabled);
            Assert.Equal("Mode?", DesktopComposerCommand.FaceFor("output", DesktopMonitor.Map(new DesktopSnapshot
            { SurfaceAvailable = true })).Status);
            Assert.Equal("Unavailable", DesktopComposerCommand.FaceFor("send", DesktopState.Unavailable).Status);
        }

        [Fact]
        public void Unsupported_copy_is_distinct_from_an_empty_answer()
        {
            var state = DesktopMonitor.Map(new DesktopSnapshot { SurfaceAvailable = true, Mode = "ChatGPT" });
            var unsupported = DesktopComposerCommand.FaceFor("output", state);
            Assert.Equal("Copy Answer", unsupported.Label);
            Assert.Equal("Unsupported", unsupported.Status);
            Assert.False(unsupported.Enabled);
            Assert.Equal("No answer", DesktopComposerCommand.FaceFor("output", state, supportsCopyAnswer: true).Status);
            Assert.Equal("No draft", DesktopComposerCommand.FaceFor("send", state).Status);
        }
    }
}
