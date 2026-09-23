namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Linq;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;

    public sealed class DesktopWorkflowVoiceTests
    {
        private static DesktopWorkflowCommand.WorkflowDef Recipe => new()
        { Id = "reply", Label = "Draft Reply", Input = "voice", Prompt = "Draft a reply. Brief: {brief}", Submit = false };

        [Fact]
        public void Failed_prompt_keeps_its_reason_and_retry_preserves_text_target_and_send_intent()
        {
            var fake = new DesktopCommandRig.Automation(); var recovery = new DesktopDraftRecovery(fake);
            var service = new DesktopWorkflowVoice(fake, recovery); var capture = new VoiceCaptureState();
            var recipe = new DesktopWorkflowCommand.WorkflowDef { Id = "explain", Prompt = "Explain the supplied image.", Submit = false };
            fake.Appender = (_, _, _, _) => (false, "composer-selection-changed");
            Assert.Equal("Insert Draft", service.Press(recipe, "ChatGPT", capture, new(fake), (_, _) => Assert.Fail(), append: true));
            Assert.Equal("CURSOR NOT READY", service.Face("explain", "ChatGPT", capture)?.Footer);
            Assert.Equal("Check Cursor", service.Press(recipe, "ChatGPT", capture, new(fake), (_, _) => Assert.Fail()));
            Assert.True(recovery.Pending);
            fake.Appender = (_, _, _, _) => (true, null);
            Assert.Equal("Draft Ready", service.Press(recipe, "ChatGPT", capture, new(fake), (_, _) => Assert.Fail()));
            Assert.False(recovery.Pending);
            Assert.Equal("Send Draft", service.Face("explain", "ChatGPT", capture)?.Label);
            var appends = fake.Calls.Where(c => c.Name == "append").ToArray();
            Assert.Equal(3, appends.Length);
            Assert.All(appends, c => Assert.Equal(recipe.Prompt, c.Text));
            Assert.All(appends, c => Assert.Equal(appends[0].Labels.Take(3), c.Labels.Take(3)));
            Assert.Equal(new[] { "False", "True", "True" }, appends.Select(c => c.Labels[3]));
            Assert.DoesNotContain(fake.Calls, c => c.Name == "send");
            Assert.Equal("unclassified", DesktopDraftRecovery.SafeError("private text from an unexpected helper response"));
        }

        [Fact]
        public void Three_presses_capture_compose_then_send_only_after_review_and_suppress_duplicate_send()
        {
            var fake = new DesktopCommandRig.Automation();
            var recovery = new DesktopDraftRecovery(fake);
            var service = new DesktopWorkflowVoice(fake, recovery) { Clock = () => 100 };
            var capture = new VoiceCaptureState();
            var voice = new DesktopVoiceActions(fake);
            Func<String, String> sink = null;
            void Toggle(VoiceIntent i, Func<String, String> s)
            { var r = capture.Press(i, DateTime.UnixEpoch); if (r.Action == VoiceAction.Start) sink = s; }
            Assert.Null(service.Press(Recipe, "ChatGPT", capture, voice, Toggle));
            Assert.Equal("Listening", service.Face("reply", "ChatGPT", capture)?.Label);
            Assert.DoesNotContain(fake.Calls, c => c.Name is "write" or "send");
            Assert.Null(service.Press(Recipe, "ChatGPT", capture, voice, Toggle));
            Assert.Equal(VoicePhase.Transcribing, capture.Phase);
            Assert.Null(sink("Tell Jo delivery is Friday.")); capture.Finish();
            Assert.Equal("Draft a reply. Brief: Tell Jo delivery is Friday.", Assert.Single(fake.Calls, c => c.Name == "write").Text);
            Assert.Equal("Send Draft", service.Face("reply", "ChatGPT", capture)?.Label);
            Assert.DoesNotContain(fake.Calls, c => c.Name == "send");
            Assert.Equal("Sent", service.Press(Recipe, "ChatGPT", capture, voice, Toggle));
            Assert.Equal("Sent", service.Press(Recipe, "ChatGPT", capture, voice, Toggle));
            Assert.Single(fake.Calls, c => c.Name == "send");
            Assert.Equal(VoicePhase.Idle, capture.Phase);
        }

        [Fact]
        public void Refused_delivery_retains_complete_brief_for_retry_and_discard_never_touches_app()
        {
            var fake = new DesktopCommandRig.Automation(); var recovery = new DesktopDraftRecovery(fake);
            var service = new DesktopWorkflowVoice(fake, recovery); var capture = new VoiceCaptureState();
            Func<String, String> sink = null;
            service.Press(Recipe, "ChatGPT", capture, new(fake), (i,s) => { capture.Press(i, DateTime.UnixEpoch); sink = s; });
            fake.Succeeds = false;
            Assert.Equal("Insert Draft", sink("my brief")); capture.Finish();
            Assert.True(recovery.Pending);
            Assert.Equal("HOLD TO DISCARD", service.Face("reply", "ChatGPT", capture)?.Footer);
            fake.Succeeds = true; fake.Calls.Clear();
            Assert.Equal("Draft Ready", service.Press(Recipe, "ChatGPT", capture, new(fake), (_,_) => Assert.Fail("retry recorded")));
            Assert.Equal("Draft a reply. Brief: my brief", Assert.Single(fake.Calls, c => c.Name == "write").Text);
            Assert.False(recovery.Pending);
            recovery.Retain("retained"); fake.Calls.Clear();
            Assert.True(recovery.Discard(recovery.PendingId.Value));
            Assert.Empty(fake.Calls); Assert.Null(service.Face("reply", "ChatGPT", capture));
        }

        [Fact]
        public void Recipe_is_pinned_and_reset_cancels_late_transcript()
        {
            var fake = new DesktopCommandRig.Automation(); var service = new DesktopWorkflowVoice(fake, new(fake));
            var capture = new VoiceCaptureState(); var recipe = Recipe;
            Func<String, String> sink = null;
            service.Press(recipe, "ChatGPT", capture, new(fake), (i,s) => { capture.Press(i, DateTime.UnixEpoch); sink = s; });
            recipe.Prompt = "wrong {brief}";
            Assert.Null(sink("original"));
            Assert.Equal("Draft a reply. Brief: original", Assert.Single(fake.Calls, c => c.Name == "write").Text);
            service.Reset(); fake.Calls.Clear();
            Assert.Equal("Cancelled", sink("late")); Assert.Empty(fake.Calls);
        }

        [Fact]
        public void Existing_input_refuses_before_microphone_and_active_other_capture_keeps_its_sink()
        {
            var fake = new DesktopCommandRig.Automation { Succeeds = false, Error = "draft-exists" };
            var service = new DesktopWorkflowVoice(fake, new(fake)); var capture = new VoiceCaptureState();
            Assert.Equal("Draft Exists", service.Press(Recipe, "ChatGPT", capture, new(fake), (_,_) => Assert.Fail("recorded")));
            fake.Succeeds = true; capture.Press(VoiceIntent.DesktopSearch, DateTime.UnixEpoch);
            service.Press(Recipe, "ChatGPT", capture, new(fake), (i,s) =>
            { Assert.Null(s); Assert.Equal(VoiceIntent.DesktopSearch, capture.Press(i, DateTime.UnixEpoch).Intent); });
        }

        [Fact]
        public void Retrying_from_Home_clears_the_workflow_recovery_face()
        {
            var fake = new DesktopCommandRig.Automation(); var recovery = new DesktopDraftRecovery(fake);
            var service = new DesktopWorkflowVoice(fake, recovery); var capture = new VoiceCaptureState();
            Func<String, String> sink = null;
            service.Press(Recipe, "ChatGPT", capture, new(fake), (i,s) => { capture.Press(i, DateTime.UnixEpoch); sink = s; });
            fake.Succeeds = false; sink("brief"); capture.Finish();
            fake.Succeeds = true;
            Assert.Equal("Draft Ready", recovery.Insert());
            Assert.Null(service.Face("reply", "ChatGPT", capture));
        }

        [Fact]
        public void Engine_routes_scoped_transcript_once_without_plain_draft_delivery_or_raw_recovery()
        {
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
            bridge.TranscriptSink = (_, _) => throw new Exception("plain sink used");
            bridge.DraftRecoverySink = _ => throw new Exception("raw recovery used");
            var calls = 0; var failures = 0;
            bridge.OnVoiceFailed += (intent, _) => { Assert.Equal(VoiceIntent.DesktopDraft, intent); failures++; };
            bridge.DeliverScopedDraft("brief", text => { Assert.Equal("brief", text); calls++; return null; });
            Assert.Equal(1, calls); Assert.Equal(0, failures);
            bridge.DeliverScopedDraft("brief", _ => "Insert Draft"); Assert.Equal(1, failures);
            bridge.DeliverScopedDraft("brief", _ => "Cancelled"); Assert.Equal(1, failures);
        }

        [Fact]
        public void More_has_mode_specific_tools_and_no_unsupported_copy_or_duplicate_changes()
        {
            var chat = DesktopMoreDynamicFolder.Actions("VizhiDesktop", "ChatGPT");
            var codex = DesktopMoreDynamicFolder.Actions("VizhiDesktop", "Codex");
            Assert.Equal(6, chat.Length); Assert.Equal(7, codex.Length);
            Assert.DoesNotContain(codex, a => a.EndsWith("___return") || a.EndsWith("___clipboard") || a.EndsWith("___copy"));
            Assert.Contains(codex, a => a.EndsWith("___review_pr"));
            Assert.Contains(codex, a => a.EndsWith("___write_tests"));
            Assert.DoesNotContain(chat.Concat(codex), a => a.Contains("___output") || a.Contains("___show_diff"));
            Assert.Empty(DesktopMoreDynamicFolder.Actions("VizhiDesktop", ""));
            Assert.Equal("TO CODEX", DesktopControlCommand.DestinationFor("ChatGPT"));
            Assert.Equal("TO CHATGPT", DesktopControlCommand.DestinationFor("Codex"));
        }
    }
}
