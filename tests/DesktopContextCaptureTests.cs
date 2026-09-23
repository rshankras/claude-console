namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;

    public class DesktopContextCaptureTests
    {
        [Fact]
        public void Captured_source_and_brief_form_one_reviewable_draft_without_replacing_source_braces()
        {
            var fake = new DesktopCommandRig.Automation { CaptureResult = _ => new() { Ok = true, Text = "Customer asked: {brief}", Source = "mail", AppName = "Mail" } };
            var context = new DesktopContextCapture(fake); var capture = new VoiceCaptureState();
            Assert.Equal("Text Added", context.Execute("selection", capture));
            Assert.DoesNotContain(fake.Calls, c => c.Name is "send" or "write");
            var workflow = new DesktopWorkflowVoice(fake, new(fake), context);
            Func<String, String> sink = null;
            workflow.Press(new() { Id = "reply", Prompt = "Draft a reply: {brief}", Input = "voice" }, "ChatGPT", capture, new(fake),
                (i,s) => { capture.Press(i, DateTime.UnixEpoch); sink = s; });
            Assert.Null(sink("Friday delivery")); capture.Finish();
            var write = Assert.Single(fake.Calls, c => c.Name == "write");
            Assert.StartsWith("Draft a reply: Friday delivery", write.Text);
            Assert.Contains("Customer asked: {brief}", write.Text);
            Assert.False(write.Send); Assert.Equal(0, context.Count); Assert.True(context.HasSource);
        }

        [Fact]
        public void A_contextual_one_tap_workflow_prepares_for_review_and_does_not_open_the_microphone_or_send()
        {
            var fake = new DesktopCommandRig.Automation(); var context = new DesktopContextCapture(fake);
            context.Execute("selection", new());
            var service = new DesktopWorkflowVoice(fake, new(fake), context); var voice = new VoiceCaptureState();
            var task = new DesktopWorkflowCommand.WorkflowDef { Id = "summary", Prompt = "Summarize this.", Submit = true };
            Assert.Equal("Draft Ready", service.Press(task, "ChatGPT", voice, new(fake), (_,_) => Assert.Fail("Unexpected microphone")));
            Assert.Contains("fixture context", Assert.Single(fake.Calls,c => c.Name == "write").Text);
            Assert.DoesNotContain(fake.Calls,c => c.Name == "send");
            Assert.Equal("Sent", service.Press(task,"ChatGPT",voice,new(fake),(_,_) => Assert.Fail("Unexpected microphone")));
            Assert.Single(fake.Calls,c => c.Name == "send");
        }

        [Fact]
        public void Capture_failure_does_not_focus_or_add_stale_clipboard_text_and_clear_does_not_edit_the_app()
        {
            var fake = new DesktopCommandRig.Automation { Succeeds = false, Error = "no-selection" };
            var context = new DesktopContextCapture(fake); var voice = new VoiceCaptureState();
            Assert.Equal("Select Text", context.Execute("selection", voice));
            Assert.Equal(0, context.Count); Assert.DoesNotContain(fake.Calls, c => c.Name == "focus");
            fake.Succeeds = true; context.Execute("selection", voice); fake.Calls.Clear();
            Assert.Equal("Sources Cleared", context.Execute("clear", voice)); Assert.Empty(fake.Calls);
        }

        [Fact]
        public void Copied_reply_is_retained_independently_of_later_clipboard_reads_and_paste_never_sends()
        {
            var fake = new DesktopCommandRig.Automation(); var context = new DesktopContextCapture(fake); var voice = new VoiceCaptureState();
            context.Execute("selection", voice); context.Consume(1);
            fake.CaptureResult = action => new() { Ok = true, Text = action == "copy" ? "Confirmed reply" : "unrelated clipboard" };
            context.Execute("copy", voice); fake.Calls.Clear();
            Assert.Equal("Reply Inserted", context.Execute("paste", voice));
            var paste = Assert.Single(fake.Calls); Assert.Equal("context:paste", paste.Name);
            Assert.Equal("Confirmed reply", paste.Text); Assert.Equal("source-window", Assert.Single(paste.Labels));
        }

        [Fact]
        public void A_new_clipboard_task_without_an_origin_cannot_reuse_a_previous_email_destination()
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { Mode = "ChatGPT", SurfaceAvailable = true } }; var context = new DesktopContextCapture(fake);
            context.Execute("selection", new()); context.Consume(1);
            fake.CaptureResult = _ => new() { Ok = true, Text = "new source copied elsewhere" };
            context.Execute("clipboard", new());
            Assert.False(context.HasSource);
            Assert.Equal("No Source App", context.Execute("return", new()));
        }

        [Fact]
        public void Unconfirmed_screenshot_is_never_retried_by_dictation_and_existing_sources_survive()
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { Mode = "ChatGPT", SurfaceAvailable = true },
                Attacher = (_,_,_) => (false, "attachment-unconfirmed") };
            var context = new DesktopContextCapture(fake);
            context.Execute("selection", new());
            Assert.Equal("Check Image", context.Execute("screenshot", new()));
            Assert.True(context.Prepare("ChatGPT", "target", out var source, out _, out _));
            Assert.Contains("fixture context", source); Assert.DoesNotContain("image", source);
            Assert.True(context.Prepare("ChatGPT", "target", out _, out _, out _));
            Assert.Single(fake.Calls, c => c.Name == "attach"); Assert.Equal(1, context.Count);
        }

        [Fact]
        public void An_attached_screenshot_is_not_attached_again_when_a_later_dictation_retries()
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { Mode = "ChatGPT", SurfaceAvailable = true } };
            var context = new DesktopContextCapture(fake); var recovery = new DesktopDraftRecovery(fake);
            Assert.Equal("Attached", context.Execute("screenshot", new()));
            Assert.Equal(0, context.Count);
            var service = new DesktopWorkflowVoice(fake, recovery, context); var voice = new VoiceCaptureState();
            Func<String,String> sink = null;
            service.Press(new() { Id = "reply", Prompt = "Reply: {brief}", Input = "voice" }, "ChatGPT", voice, new(fake),
                (i,s) => { voice.Press(i,DateTime.UnixEpoch); sink=s; });
            fake.Succeeds = false; sink("explain this"); voice.Finish();
            Assert.Equal(0, context.Count); Assert.True(recovery.Pending);
            fake.Succeeds = true;
            Assert.Equal("Draft Ready", service.RetryFromHome(voice, new(fake)));
            Assert.Equal(0, context.Count); Assert.Single(fake.Calls, c => c.Name == "attach");
        }

        [Fact]
        public void Screenshot_pins_the_chat_before_the_picker_and_attaches_without_dictation_or_send()
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { Mode = "ChatGPT", SurfaceAvailable = true } };
            var context = new DesktopContextCapture(fake); var voice = new VoiceCaptureState();
            fake.CaptureResult = _ =>
            {
                Assert.Equal(new[] { "status", "prepare-append", "context:screenshot" }, fake.Calls.Select(c => c.Name));
                Assert.False(context.AttachingImage);
                return new() { Ok = true, Image = "/fixture/screenshot.png" };
            };
            fake.Attacher = (path, mode, target) =>
            {
                Assert.Equal("/fixture/screenshot.png", path); Assert.Equal("ChatGPT", mode); Assert.Equal("fixture-target", target);
                Assert.True(context.AttachingImage);
                Assert.Equal("Attaching", DesktopCaptureCommand.Face("screenshot", context, working: true).Label);
                return (true, null);
            };
            Assert.Equal("Attached", context.Execute("screenshot", voice));
            Assert.Equal(VoicePhase.Idle, voice.Phase); Assert.Equal(0, context.Count);
            Assert.False(context.AttachingImage); Assert.False(context.IsBusy);
            Assert.DoesNotContain(fake.Calls, c => c.Name is "write" or "append" or "send");
            Assert.True(context.Prepare("ChatGPT", "fixture-target", out var text, out _, out _));
            Assert.Empty(text); Assert.Single(fake.Calls, c => c.Name == "attach");
            Assert.Equal(("Attached", "screenshot", "REVIEW · SEND"), DesktopCaptureCommand.Face("screenshot", context, "Attached"));
        }

        [Fact]
        public void Cancelling_the_picker_never_attaches_or_focuses_or_stages_anything()
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { Mode = "ChatGPT", SurfaceAvailable = true },
                CaptureResult = _ => new() { Error = "cancelled" } };
            var context = new DesktopContextCapture(fake);
            Assert.Equal("Cancelled", context.Execute("screenshot", new()));
            Assert.Equal(0, context.Count); Assert.False(context.AttachingImage);
            Assert.DoesNotContain(fake.Calls, c => c.Name is "attach" or "focus" or "write" or "send");
        }

        [Theory]
        [InlineData("composer-target-changed", "Chat Changed")]
        [InlineData("mode-changed", "Chat Changed")]
        public void A_chat_change_during_the_picker_cannot_retarget_the_capture(String error, String feedback)
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { Mode = "ChatGPT", SurfaceAvailable = true } };
            fake.CaptureResult = _ =>
            {
                fake.Next = new() { Mode = "Codex", SurfaceAvailable = true };
                fake.AppendDestination = new() { Target = "another-chat", Fingerprint = "new-draft" };
                return new() { Ok = true, Image = "/fixture/image.png" };
            };
            fake.Attacher = (_,mode,target) => { Assert.Equal("ChatGPT", mode); Assert.Equal("fixture-target", target); return (false, error); };
            var context = new DesktopContextCapture(fake);
            Assert.Equal(feedback, context.Execute("screenshot", new()));
            Assert.Single(fake.Calls, c => c.Name == "prepare-append"); Assert.Equal(0, context.Count);
        }

        [Fact]
        public void An_unready_composer_is_refused_before_opening_the_picker()
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { Mode = "ChatGPT", SurfaceAvailable = true },
                Succeeds = false, Error = "composer-unavailable" };
            Assert.Equal("Wait", new DesktopContextCapture(fake).Execute("screenshot", new()));
            Assert.DoesNotContain(fake.Calls, c => c.Name is "context:screenshot" or "attach");
        }

        [Fact]
        public void Capture_and_clear_are_refused_during_voice_or_pending_draft()
        {
            var fake = new DesktopCommandRig.Automation(); var context = new DesktopContextCapture(fake); var voice = new VoiceCaptureState();
            voice.Press(VoiceIntent.DesktopDraft, DateTime.UnixEpoch);
            Assert.Equal("Finish Speaking", context.Execute("selection", voice));
            voice.Finish(); context.DraftPending = () => true;
            Assert.Equal("Insert Draft First", context.Execute("clear", voice)); Assert.Empty(fake.Calls);
        }
    }
}
