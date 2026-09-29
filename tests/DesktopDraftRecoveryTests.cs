namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Xunit;

    public class DesktopDraftRecoveryTests
    {
        [Theory]
        [InlineData(false, true, 1)]
        [InlineData(false, false, 0)]
        [InlineData(true, true, 0)]
        public void Automatic_insertion_reports_ready_only_for_a_confirmed_unsent_draft(Boolean send, Boolean succeeds, Int32 expected)
        {
            var fake = new DesktopCommandRig.Automation { Succeeds = succeeds, Error = "draft-exists" };
            var recovery = new DesktopDraftRecovery(fake);
            var ready = 0;
            recovery.Ready += () => ready++;
            var error = DesktopTranscriptDelivery.Write(fake, "Reply Friday.", send, recovery.NotifyReady);
            Assert.Equal(expected, ready);
            Assert.Equal(succeeds, error == null);
            Assert.False(recovery.Pending);
            Assert.Equal("write", fake.Calls[0].Name);
            Assert.Equal(send, fake.Calls[0].Send);
        }

        [Theory]
        [InlineData("draft-exists")]
        [InlineData("write-not-applied")]
        [InlineData("no-unique-composer")]
        public void Refused_draft_retains_the_original_words_and_offers_keypad_retry(String error)
        {
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
            var failures = new List<(VoiceIntent, String)>();
            var retained = new List<String>();
            bridge.OnVoiceFailed += (intent, text) => failures.Add((intent, text));
            bridge.TranscriptSink = (_, submit) => { Assert.False(submit); return error; };
            bridge.DraftRecoverySink = text => { retained.Add(text); return true; };

            bridge.DeliverToSink("Keep this draft", submit: false);

            Assert.Equal("Keep this draft", Assert.Single(retained));
            Assert.Equal((VoiceIntent.DesktopDraft, VoiceFailure.InsertDraft), Assert.Single(failures));
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, true)]
        [InlineData(true, false)]
        public void Successful_delivery_and_auto_send_do_not_create_a_pending_draft(Boolean submit, Boolean success)
        {
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
            var retentions = 0;
            bridge.TranscriptSink = (_, _) => success ? null : "send-not-found";
            bridge.DraftRecoverySink = _ => { retentions++; return true; };

            bridge.DeliverToSink("hello", submit);

            Assert.Equal(0, retentions);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Recovery_failure_cannot_claim_the_draft_was_retained(Boolean throws)
        {
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
            var failures = new List<String>();
            bridge.OnVoiceFailed += (_, text) => failures.Add(text);
            bridge.TranscriptSink = (_, _) => "write-not-applied";
            bridge.DraftRecoverySink = _ => throws ? throw new IOException("unavailable") : false;

            bridge.DeliverToSink("hello", submit: false);

            Assert.Equal(VoiceFailure.NotTyped, Assert.Single(failures));
        }

        [Fact]
        public void A_throwing_sink_still_allows_draft_recovery()
        {
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
            var failures = new List<String>();
            bridge.OnVoiceFailed += (_, text) => failures.Add(text);
            bridge.TranscriptSink = (_, _) => throw new IOException("unavailable");
            bridge.DraftRecoverySink = _ => true;
            bridge.DeliverToSink("hello", submit: false);
            Assert.Equal(VoiceFailure.InsertDraft, Assert.Single(failures));
        }

        [Fact]
        public void Retry_keeps_the_original_transcript_until_insertion_is_confirmed_and_never_sends()
        {
            var fake = new DesktopCommandRig.Automation { Succeeds = false, Error = "draft-exists" };
            var recovery = new DesktopDraftRecovery(fake);
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge())
            {
                TranscriptSink = (text, send) => DesktopTranscriptDelivery.Write(fake, text, send),
                DraftRecoverySink = recovery.Retain,
            };
            bridge.DeliverToSink("Reply Friday — வணக்கம் 😀", false);
            Assert.True(recovery.Pending);
            Assert.Equal("Draft Exists", recovery.Insert());
            Assert.True(recovery.Pending);
            fake.Error = "composer-target-changed";
            Assert.Equal("Chat Changed", recovery.Insert());
            Assert.True(recovery.Pending);
            fake.Succeeds = true;
            Assert.Equal("Draft Ready", recovery.Insert());
            Assert.False(recovery.Pending);
            Assert.Null(recovery.Insert()); // a duplicate press cannot reinsert or send
            Assert.Equal(5, fake.Calls.Count);
            Assert.Equal("focus", fake.Calls[^1].Name);
            Assert.All(fake.Calls.GetRange(0, 4), call => {
                Assert.Equal("write", call.Name);
                Assert.Equal("Reply Friday — வணக்கம் 😀", call.Text);
                Assert.False(call.Send);
            });
            // Submission is a separate, explicit press of the already-shipped keypad action.
            var send = new DesktopActions.DesktopComposerCommand.PressHandler();
            Assert.Equal("Sent", send.Execute("send", new OpenAiDesktopAdapter(), fake));
            Assert.Equal("send", fake.Calls[^1].Name);
            Assert.Null(send.Execute("send", new OpenAiDesktopAdapter(), fake));
            Assert.Equal(6, fake.Calls.Count);
        }

        [Fact]
        public void Recovery_survives_a_helper_exception_and_never_requests_submission()
        {
            var calls = new List<List<String>>();
            var fail = true;
            var auto = new MacDesktopAutomation(new OpenAiDesktopAdapter()) {
                Runner = (args, _) => {
                    if (args[0] == "focus") return "{\"ok\":true}";
                    calls.Add(args);
                    if (fail) throw new IOException("fixture timeout");
                    return "{\"ok\":true,\"method\":\"existing\",\"sent\":false}";
                }
            };
            var recovery = new DesktopDraftRecovery(auto);
            Assert.False(recovery.Retain(" "));
            Assert.True(recovery.Retain("my spoken draft"));
            Assert.Equal("Insert Failed", recovery.Insert());
            Assert.True(recovery.Pending);
            fail = false;
            Assert.Equal("Draft Ready", recovery.Insert());
            Assert.False(recovery.Pending);
            Assert.All(calls, args => {
                Assert.Equal("write", args[0]);
                Assert.Contains("--accept-existing", args);
                Assert.DoesNotContain("--send-label", args);
                Assert.Equal("my spoken draft", args[args.IndexOf("--text") + 1]);
            });
        }

        [Fact]
        public void Pending_draft_face_offers_retry_and_explains_an_occupied_composer()
        {
            var capture = new VoiceCaptureState();
            var retry = DesktopDictationFace.For(VoiceIntent.DesktopDraft, capture, null, "voice", true);
            Assert.Equal("Insert Draft", retry.Label);
            Assert.Equal("HOLD TO DISCARD", retry.Footer);
            var occupied = DesktopDictationFace.For(VoiceIntent.DesktopDraft, capture, "Draft Exists", "voice", true);
            Assert.Equal("Insert Draft", occupied.Label);
            Assert.Equal("HOLD TO DISCARD", occupied.Footer);
            var ready = DesktopDictationFace.For(VoiceIntent.DesktopDraft, capture, "Draft Ready", "voice");
            Assert.Equal("SEND DRAFT", ready.Footer);
        }

        [Fact]
        public void Processing_remains_visible_for_the_starting_intent_until_delivery_finishes()
        {
            var capture = new VoiceCaptureState();
            capture.Press(VoiceIntent.DesktopDraft, DateTime.UtcNow);
            Assert.False(capture.IsTranscribing(VoiceIntent.DesktopDraft));
            capture.Press(VoiceIntent.Desktop, DateTime.UtcNow); // another key can finish recording
            Assert.True(capture.IsTranscribing(VoiceIntent.DesktopDraft));
            Assert.False(capture.IsTranscribing(VoiceIntent.Desktop));
            Assert.Equal(VoiceAction.Refuse, capture.Press(VoiceIntent.DesktopDraft, DateTime.UtcNow).Action);
            Assert.True(capture.IsTranscribing(VoiceIntent.DesktopDraft));
            capture.Finish();
            Assert.False(capture.IsTranscribing(VoiceIntent.DesktopDraft));
        }
    }
}
