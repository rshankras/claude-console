namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;

    public class DesktopPasteIntoChatTests
    {
        private static DesktopCommandRig.Automation App() => new()
        {
            SupportsAppend = true,
            Next = new() { Mode = "ChatGPT", SurfaceAvailable = true },
            CaptureResult = _ => new() { Ok = true, Text = "Customer email — தமிழ் 😀" },
        };

        [Fact]
        public void Clipboard_is_inserted_immediately_without_staging_or_sending()
        {
            var app = App(); var context = new DesktopContextCapture(app);
            Assert.Equal("Pasted", context.Execute("clipboard", new()));
            var append = Assert.Single(app.Calls, c => c.Name == "append");
            Assert.Equal("Customer email — தமிழ் 😀", append.Text);
            Assert.Equal(new[] { "ChatGPT", "fixture-target", "fixture-draft-hash", "False" }, append.Labels);
            Assert.Equal(0, context.Count); Assert.False(context.HasSource);
            Assert.DoesNotContain(app.Calls, c => c.Name is "send" or "write");
        }

        [Theory]
        [InlineData("draft-changed", "Draft Changed")]
        [InlineData("append-unconfirmed", "Check Draft")]
        [InlineData("composer-target-changed", "Chat Changed")]
        public void An_unconfirmed_paste_never_reports_success_or_stages_an_invisible_copy(String error, String expected)
        {
            var app = App(); app.Appender = (_, _, _, _) => (false, error);
            var context = new DesktopContextCapture(app);
            Assert.Equal(expected, context.Execute("clipboard", new()));
            Assert.Equal(0, context.Count);
            Assert.False(context.HasSource);
        }

        [Fact]
        public void Retry_reuses_the_original_baseline_but_a_new_chat_gets_a_new_target()
        {
            var app = App(); var context = new DesktopContextCapture(app);
            app.Appender = (_, _, _, _) => (false, "append-unconfirmed");
            Assert.Equal("Check Draft", context.Execute("clipboard", new()));
            app.AppendDestination = new() { Target = "fixture-target", Fingerprint = "possibly-inserted" };
            app.Appender = (_, _, target, retry) =>
            { Assert.True(retry); Assert.Equal("fixture-draft-hash", target.Fingerprint); return (true, null); };
            Assert.Equal("Pasted", context.Execute("clipboard", new()));
            app.AppendDestination = new() { Target = "new-chat", Fingerprint = "new-draft" };
            app.Appender = (_, _, target, retry) =>
            { Assert.False(retry); Assert.Equal("new-chat", target.Target); Assert.Equal("new-draft", target.Fingerprint); return (true, null); };
            Assert.Equal("Pasted", context.Execute("clipboard", new()));
        }

        [Fact]
        public void Paste_then_dictate_appends_only_the_instruction_and_never_submits()
        {
            var app = App(); var context = new DesktopContextCapture(app);
            Assert.Equal("Pasted", context.Execute("clipboard", new()));
            app.Calls.Clear();
            var workflow = new DesktopWorkflowVoice(app, new(app), context); var voice = new VoiceCaptureState();
            Func<String, String> sink = null;
            var task = new DesktopWorkflowCommand.WorkflowDef { Id = "context_voice", Input = "voice", Prompt = "{brief}" };
            void Toggle(VoiceIntent intent, Func<String, String> next) { voice.Press(intent, DateTime.UtcNow); sink = next; }
            workflow.Press(task, "ChatGPT", voice, new(app), Toggle, allowSend: false, append: true);
            Assert.Null(sink("Reply politely and confirm Friday.")); voice.Finish();
            Assert.Equal("Reply politely and confirm Friday.", Assert.Single(app.Calls, c => c.Name == "append").Text);
            Assert.DoesNotContain(app.Calls, c => c.Name is "write" or "send");
            // A later Dictate press starts another recording instead of sending the draft.
            workflow.Press(task, "ChatGPT", voice, new(app), Toggle, allowSend: false, append: true);
            Assert.True(voice.IsRecording(VoiceIntent.DesktopDraft));
            Assert.Null(sink("Keep it under 100 words.")); voice.Finish();
            Assert.Equal(2, app.Calls.Count(c => c.Name == "append"));
            Assert.DoesNotContain(app.Calls, c => c.Name == "send");
        }

        [Fact]
        public void Spoken_reply_workflow_adds_its_instruction_after_an_immediate_paste()
        {
            var app = App(); var context = new DesktopContextCapture(app);
            Assert.Equal("Pasted", context.Execute("clipboard", new())); app.Calls.Clear();
            var voice = new VoiceCaptureState(); var workflow = new DesktopWorkflowVoice(app, new(app), context);
            Func<String, String> sink = null;
            DesktopWorkflowCommand.Execute("draft_reply", app,
                (_, _) => new() { Id = "draft_reply", Input = "voice", Prompt = "Draft a reply: {brief}" },
                workflow, voice, new(app), (i, s) => { voice.Press(i, DateTime.UtcNow); sink = s; }, context);
            Assert.Null(sink("Confirm Friday")); voice.Finish();
            Assert.Equal("Draft a reply: Confirm Friday", Assert.Single(app.Calls, c => c.Name == "append").Text);
            Assert.DoesNotContain(app.Calls, c => c.Name is "write" or "send");
        }

        [Fact]
        public void Dictation_retry_stays_pinned_to_the_original_chat_and_draft()
        {
            var app = App(); var recovery = new DesktopDraftRecovery(app);
            var workflow = new DesktopWorkflowVoice(app, recovery); var voice = new VoiceCaptureState();
            Func<String, String> sink = null;
            workflow.Press(new() { Id = "context_voice", Input = "voice", Prompt = "{brief}" }, "ChatGPT", voice, new(app),
                (i, s) => { voice.Press(i, DateTime.UtcNow); sink = s; }, allowSend: false, append: true);
            app.Appender = (_, _, _, _) => (false, "append-unconfirmed");
            Assert.Equal("Insert Draft", sink("My instruction")); voice.Finish();
            app.AppendDestination = new() { Target = "different-chat", Fingerprint = "different-draft" };
            app.Appender = (text, mode, target, retry) =>
            {
                Assert.Equal("My instruction", text); Assert.Equal("ChatGPT", mode);
                Assert.Equal("fixture-target", target.Target); Assert.Equal("fixture-draft-hash", target.Fingerprint);
                Assert.True(retry); return (false, "composer-target-changed");
            };
            Assert.Equal("Chat Changed", workflow.RetryFromHome(voice, new(app)));
            Assert.True(recovery.Pending);
        }

        [Fact]
        public void Paste_faces_describe_visible_delivery_and_keep_errors_in_the_footer()
        {
            Assert.Equal(("Paste into Chat", "copy", "CLIPBOARD"), DesktopCaptureCommand.Face("clipboard", null));
            Assert.Equal(("Pasted", "copy", "REVIEW · SEND"), DesktopCaptureCommand.Face("clipboard", null, "Pasted"));
            Assert.Equal(("Paste into Chat", "copy", "Check Draft"), DesktopCaptureCommand.Face("clipboard", null, "Check Draft"));
            Assert.Equal(("Pasting", "copy", "TEXT · WAIT"), DesktopCaptureCommand.Face("clipboard", null, working: true));
        }

        [Theory]
        [InlineData("ChatGPT")]
        [InlineData("Codex")]
        public void Mac_append_uses_a_distinct_guarded_verb_and_never_requests_send(String mode)
        {
            var calls = new List<List<String>>();
            var app = new MacDesktopAutomation(new OpenAiDesktopAdapter()) { Runner = (args, _) =>
            { calls.Add(args); return args[0] == "append-target" ? "{\"ok\":true,\"target\":\"chat\",\"fingerprint\":\"draft\"}" : "{\"ok\":true,\"sent\":false}"; } };
            var target = app.PrepareAppend(mode, out var error);
            Assert.Null(error); Assert.NotNull(target);
            Assert.True(app.AppendPreparedDraft("Copied text", mode, target, true, out error));
            Assert.Equal("append", calls[1][0]); Assert.Contains("--expect-draft", calls[1]);
            Assert.Contains("--expect-target", calls[1]); Assert.Contains("--accept-existing", calls[1]);
            Assert.All(calls, args => Assert.DoesNotContain("--send-label", args));
            Assert.All(calls, args =>
            {
                Assert.Equal(mode, args[args.IndexOf("--expect-mode") + 1]);
                var hints = args.Select((arg, i) => (arg, i)).Where(x => x.arg == "--draft-placeholder")
                    .Select(x => args[x.i + 1]).ToArray();
                Assert.Equal(new OpenAiDesktopAdapter().ComposerPlaceholderLabels, hints);
                Assert.Contains("Do anything", hints);
            });
        }

        [Theory]
        [InlineData("ChatGPT")]
        [InlineData("Codex")]
        public void Stopping_home_dictation_inserts_into_the_starting_mode_without_send(String mode)
        {
            var app = App(); app.Next = new() { Mode = mode, SurfaceAvailable = true };
            var capture = new VoiceCaptureState(); var recovery = new DesktopDraftRecovery(app);
            var workflow = new DesktopWorkflowVoice(app, recovery); Func<String, String> sink = null;
            var task = new DesktopWorkflowCommand.WorkflowDef { Id = "context_voice", Input = "voice", Prompt = "{brief}", Submit = false };
            void Toggle(VoiceIntent intent, Func<String, String> next)
            {
                var result = capture.Press(intent, DateTime.UnixEpoch);
                if (result.Action == VoiceAction.Start) sink = next;
            }
            workflow.Press(task, mode, capture, new(app), Toggle, allowSend: false, append: true);
            Assert.True(capture.IsRecording(VoiceIntent.DesktopDraft));
            workflow.Press(task, mode, capture, new(app), Toggle, allowSend: false, append: true);
            Assert.Equal(VoicePhase.Transcribing, capture.Phase);
            Assert.Null(sink("Explain the current diff. தமிழ் 😀")); capture.Finish();
            var inserted = Assert.Single(app.Calls, c => c.Name == "append");
            Assert.Equal("Explain the current diff. தமிழ் 😀", inserted.Text);
            Assert.Equal(mode, inserted.Labels[0]);
            Assert.False(recovery.Pending);
            Assert.DoesNotContain(app.Calls, c => c.Name is "write" or "send");
            // Dictate stays an input action even after success; another tap starts recording.
            workflow.Press(task, mode, capture, new(app), Toggle, allowSend: false, append: true);
            Assert.True(capture.IsRecording(VoiceIntent.DesktopDraft));
            Assert.DoesNotContain(app.Calls, c => c.Name == "send");
        }
    }
}
