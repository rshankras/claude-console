namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;

    public class DesktopCopyReplyTests
    {
        [Fact]
        public void Copy_retains_the_verified_reply_after_capturing_source_context()
        {
            var fake = new DesktopCommandRig.Automation { CaptureResult = _ => new() { Ok = true,
                Text = "Private customer response", Source = "private-window", AppName = "Private app" } };
            var context = new DesktopContextCapture(fake);
            context.Execute("clipboard", new());
            Assert.Equal("Copied", context.Execute("copy", new()));
            Assert.True(context.HasReply);
        }

        [Theory]
        [InlineData("no-answer", "No Answer")]
        [InlineData("reply-unrecognized", "Check Chat")]
        [InlineData("copy-control-unavailable", "Use App")]
        [InlineData("reply-web-area-missing", "Check Chat")]
        [InlineData("reply-web-area-multiple", "Check Chat")]
        [InlineData("reply-composer-missing", "Check Chat")]
        [InlineData("reply-composer-multiple", "Check Chat")]
        [InlineData("reply-dialog-open", "Check Chat")]
        [InlineData("reply-selection-multiple", "Check Chat")]
        [InlineData("reply-copy-multiple", "Check Chat")]
        [InlineData("reply-copy-outside-latest", "Use App")]
        [InlineData("reply-copy-not-found", "Use App")]
        [InlineData("reply-copy-wrong-role", "Use App")]
        [InlineData("reply-copy-nested-control", "Use App")]
        [InlineData("reply-action-not-found", "Use App")]
        [InlineData("reply-action-row-unrecognized", "Use App")]
        [InlineData("Private reply text\nforged success", "Try Again")]
        [InlineData("copied", "Try Again")]
        public void Helper_failure_returns_actionable_feedback_without_retaining_its_payload(String error, String expected)
        {
            var fake = new DesktopCommandRig.Automation { CaptureResult = _ => new() { Error = error, Text = "Private response" } };
            var context = new DesktopContextCapture(fake);
            Assert.Equal(expected, context.Execute("copy", new()));
            Assert.False(context.HasReply);
        }

        [Fact]
        public void Empty_success_payload_and_thrown_helper_are_not_presented_as_copied()
        {
            var fake = new DesktopCommandRig.Automation { CaptureResult = _ => new() { Ok = true, Text = " " } };
            var context = new DesktopContextCapture(fake);
            Assert.NotEqual("Copied", context.Execute("copy", new()));
            fake.CaptureResult = _ => throw new Exception("Private exception payload");
            Assert.NotEqual("Copied", context.Execute("copy", new()));
            Assert.False(context.HasReply);
        }

        [Fact]
        public void Voice_and_mode_guards_prevent_running_the_helper()
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { SurfaceAvailable = true, Mode = "Codex" } };
            var context = new DesktopContextCapture(fake);
            var voice = new VoiceCaptureState(); voice.Press(VoiceIntent.DesktopDraft, DateTime.UtcNow);
            Assert.Equal("Finish Speaking", context.Execute("copy", voice));
            Assert.Empty(fake.Calls);
            Assert.Equal("Mode Changed", DesktopToolsCommand.Execute("copy_approve", new() { Mode = "ChatGPT" },
                fake, new OpenAiDesktopAdapter(), context, new(), new(), () => {}, _ => "unused"));
            Assert.DoesNotContain(fake.Calls, c => c.Name == "context:copy");
        }

        [Fact]
        public void Helper_exception_releases_the_busy_guard_so_copy_can_be_retried()
        {
            var fake = new DesktopCommandRig.Automation { CaptureResult = _ => throw new IOException("Helper unavailable") };
            var context = new DesktopContextCapture(fake);
            Assert.Equal("Try Again", context.Execute("copy", new()));
            Assert.False(context.IsBusy);
            fake.CaptureResult = _ => new() { Ok = true, Text = "Verified reply" };
            Assert.Equal("Copied", context.Execute("copy", new()));
            Assert.True(context.HasReply);
        }

        [Fact]
        public void Busy_copy_does_not_call_the_helper_twice()
        {
            var fake = new DesktopCommandRig.Automation();
            var context = new DesktopContextCapture(fake);
            fake.CaptureResult = _ =>
            {
                Assert.Equal("Please Wait", context.Execute("copy", new()));
                return new() { Ok = true, Text = "Confirmed reply" };
            };
            Assert.Equal("Copied", context.Execute("copy", new()));
            Assert.Single(fake.Calls, c => c.Name == "context:copy");
        }

        [Fact]
        public void Copy_uses_a_new_guarded_verb_and_verified_app_semantics_without_selection_or_typing()
        {
            List<String> call = null;
            var automation = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            { Runner = (args, _) => { call = args; return "{\"ok\":true,\"text\":\"Latest answer\"}"; } };
            var result = automation.Context("copy");
            Assert.True(result.Ok); Assert.Equal("Latest answer", result.Text);
            Assert.Equal("copy-reply", call[0]); // older helpers cannot silently use selection copy
            Assert.Equal("com.openai.codex", call[call.IndexOf("--app") + 1]);
            foreach (var label in new[] { "Copy response", "Copy", "Copied", "Fork chat from here", "Continue in new chat", "More actions", "ChatGPT said:", "You said:", "Stop", "Working", "Allow once", "Rate response", "Remove good response feedback", "Remove bad response feedback" })
                Assert.Contains(label, call);
            Assert.DoesNotContain("--text", call); Assert.DoesNotContain("--key-code", call);
            automation.Status();
            Assert.Contains("--assistant-heading", call); Assert.Contains("--response-action", call);
        }

        [Fact]
        public void One_tap_retains_the_fresh_reply_for_paste_and_a_failed_new_copy_cannot_reuse_it()
        {
            var fake = new DesktopCommandRig.Automation();
            var context = new DesktopContextCapture(fake); var voice = new VoiceCaptureState();
            context.Execute("selection", voice); context.Consume(1);
            fake.CaptureResult = _ => new() { Ok = true, Text = "Latest complete answer" };
            fake.Calls.Clear();
            Assert.Equal("Copied", context.Execute("copy", voice));
            Assert.Equal("context:copy", Assert.Single(fake.Calls).Name);
            Assert.True(context.HasReply);
            fake.CaptureResult = _ => new() { Error = "answer-not-ready" };
            Assert.Equal("Wait", context.Execute("copy", voice));
            Assert.False(context.HasReply); fake.Calls.Clear();
            Assert.Equal("Copy Reply First", context.Execute("paste", voice));
            Assert.Empty(fake.Calls);
        }

        [Theory]
        [InlineData("no-answer", "No Answer")]
        [InlineData("answer-not-ready", "Wait")]
        [InlineData("answer-changed", "Chat Changed")]
        [InlineData("ambiguous-answer", "Check Chat")]
        [InlineData("copy-unconfirmed", "Try Copy Again")]
        [InlineData("copy-control-unavailable", "Use App")]
        [InlineData("reply-unrecognized", "Check Chat")]
        public void Failed_copy_is_never_presented_as_success(String error, String expected)
        {
            var fake = new DesktopCommandRig.Automation { CaptureResult = _ => new() { Error = error } };
            var context = new DesktopContextCapture(fake);
            Assert.Equal(expected, context.Execute("copy", new()));
            Assert.False(context.HasReply);
        }

        [Theory]
        [InlineData((Int32)DesktopActivity.Ready, true, true, "LATEST ANSWER")]
        [InlineData((Int32)DesktopActivity.Ready, false, false, "No answer")]
        [InlineData((Int32)DesktopActivity.Working, true, false, "Wait")]
        [InlineData((Int32)DesktopActivity.WaitingApproval, true, false, "Wait")]
        [InlineData((Int32)DesktopActivity.Unavailable, true, false, "Open Chat")]
        public void Copy_key_explains_its_availability(Int32 activity, Boolean answer, Boolean enabled, String footer)
        {
            var face = DesktopCaptureCommand.CopyFace(new() { Activity = (DesktopActivity)activity, CanCopyAnswer = answer });
            Assert.Equal("Copy Reply", face.Label); Assert.Equal(enabled, face.Enabled); Assert.Equal(footer, face.Footer);
        }

        [Fact]
        public void Sidebar_activity_and_native_voice_block_copy_without_a_stop_button()
        {
            var active = new DesktopState { Activity = DesktopActivity.Ready, CanCopyAnswer = true,
                Conversations = new[] { new DesktopConversation { Selected = true, State = ConversationState.Running } } };
            Assert.Equal("Wait", DesktopCaptureCommand.CopyFace(active).Footer);
            Assert.False(DesktopCaptureCommand.CopyFace(active).Enabled);
            var voice = new DesktopState { Activity = DesktopActivity.Ready, CanCopyAnswer = true, VoiceChat = DesktopVoiceState.Active };
            Assert.False(DesktopCaptureCommand.CopyFace(voice).Enabled);
        }

        [Theory]
        [InlineData("no-answer", "No Answer")]
        [InlineData("reply-unrecognized", "Check Chat")]
        [InlineData("copy-control-unavailable", "Use App")]
        [InlineData("reply-web-area-missing", "Check Chat")]
        [InlineData("reply-web-area-multiple", "Check Chat")]
        [InlineData("reply-composer-missing", "Check Chat")]
        [InlineData("reply-composer-multiple", "Check Chat")]
        [InlineData("reply-dialog-open", "Check Chat")]
        [InlineData("reply-selection-multiple", "Check Chat")]
        [InlineData("reply-copy-multiple", "Check Chat")]
        [InlineData("reply-copy-outside-latest", "Use App")]
        [InlineData("reply-copy-not-found", "Use App")]
        [InlineData("reply-copy-wrong-role", "Use App")]
        [InlineData("reply-copy-nested-control", "Use App")]
        [InlineData("reply-action-not-found", "Use App")]
        [InlineData("reply-action-row-unrecognized", "Use App")]
        public void Missing_controls_are_distinguished_from_a_missing_reply_without_repeating_the_error(String error, String expected)
        {
            var snapshot = DesktopSnapshot.Parse("{\"ok\":true,\"surface\":true,\"copyAnswerError\":\"" + error + "\"}");
            var state = DesktopMonitor.Map(snapshot);
            Assert.Equal(error, state.CopyAnswerError);
            var idle = DesktopCaptureCommand.CopyFace(state);
            Assert.Equal(expected, idle.Footer);
            var tapped = DesktopCaptureCommand.CopyFace(state, expected);
            Assert.Equal("Copy Reply", tapped.Label);
            Assert.Equal(expected, tapped.Footer);
            Assert.False(tapped.Enabled);
        }
    }
}
