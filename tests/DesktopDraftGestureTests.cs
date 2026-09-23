namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;

    public class DesktopDraftGestureTests
    {
        [Fact]
        public void Tap_retries_once_on_release_not_before_the_hold_is_known()
        {
            var fake = new DesktopCommandRig.Automation { Succeeds = false, Error = "draft-exists" };
            var recovery = new DesktopDraftRecovery(fake);
            recovery.Retain("retained words");
            var buttons = new DesktopVoiceDraftCommand.ButtonHandler();
            var taps = 0;
            void Event(DeviceButtonEventType type) => Assert.True(buttons.Handle(type,
                () => recovery.PendingId, () => { taps++; recovery.Insert(); }, id => recovery.Discard(id)));
            Event(DeviceButtonEventType.Press);
            Event(DeviceButtonEventType.RepeatPress);
            Assert.Empty(fake.Calls);
            Event(DeviceButtonEventType.Release);
            Event(DeviceButtonEventType.Release);
            Assert.Equal(1, taps);
            var call = Assert.Single(fake.Calls);
            Assert.Equal("write", call.Name);
            Assert.Equal("retained words", call.Text);
            Assert.False(call.Send);
            Assert.True(recovery.Pending);
        }

        [Fact]
        public void Hold_discards_only_retained_memory_and_release_cannot_retry_or_record()
        {
            var fake = new DesktopCommandRig.Automation();
            var recovery = new DesktopDraftRecovery(fake);
            recovery.Retain("retained words");
            var buttons = new DesktopVoiceDraftCommand.ButtonHandler();
            var taps = 0; var discarded = 0; var changed = 0;
            recovery.Discarded += () => discarded++;
            recovery.Changed += () => changed++;
            void Event(DeviceButtonEventType type) => buttons.Handle(type, () => recovery.PendingId,
                () => taps++, id => recovery.Discard(id));
            Event(DeviceButtonEventType.Press);
            Assert.True(recovery.Pending);
            Event(DeviceButtonEventType.LongPress);
            Event(DeviceButtonEventType.LongPress);
            Event(DeviceButtonEventType.RepeatPress);
            Event(DeviceButtonEventType.Release);
            Event(DeviceButtonEventType.Release);
            Assert.False(recovery.Pending);
            Assert.Null(recovery.Insert());
            Assert.Equal(1, discarded);
            Assert.Equal(1, changed);
            Assert.Equal(0, taps);
            Assert.Empty(fake.Calls); // no write, clipboard, focus or Send to the app
            Event(DeviceButtonEventType.Press);
            Event(DeviceButtonEventType.Release);
            Assert.Equal(1, taps); // next fresh tap belongs to normal recording again
        }

        [Fact]
        public void A_draft_arriving_or_changing_during_a_hold_is_not_discarded()
        {
            var recovery = new DesktopDraftRecovery(new DesktopCommandRig.Automation());
            var buttons = new DesktopVoiceDraftCommand.ButtonHandler();
            var taps = 0;
            void Event(DeviceButtonEventType type) => buttons.Handle(type, () => recovery.PendingId,
                () => taps++, id => recovery.Discard(id));
            Event(DeviceButtonEventType.Press); // no draft when the hold begins
            recovery.Retain("newly transcribed words");
            Event(DeviceButtonEventType.LongPress);
            Event(DeviceButtonEventType.Release);
            Assert.True(recovery.Pending);
            Event(DeviceButtonEventType.Press);
            var oldId = recovery.PendingId.Value;
            recovery.Retain("newer result");
            Event(DeviceButtonEventType.LongPress);
            Event(DeviceButtonEventType.Release);
            Assert.True(recovery.Pending);
            Assert.False(recovery.Discard(oldId));
            Assert.Equal(0, taps);
        }

        [Fact]
        public void Holding_during_any_active_capture_cannot_discard_a_late_result_or_run_a_tap()
        {
            foreach (var phase in new[] { VoicePhase.Starting, VoicePhase.Recording,
                VoicePhase.Cancelling, VoicePhase.Transcribing })
            {
                var recovery = new DesktopDraftRecovery(new DesktopCommandRig.Automation());
                recovery.Retain("late result");
                var buttons = new DesktopVoiceDraftCommand.ButtonHandler();
                var taps = 0;
                foreach (var type in new[] { DeviceButtonEventType.Press, DeviceButtonEventType.LongPress,
                    DeviceButtonEventType.RepeatPress, DeviceButtonEventType.Release })
                { buttons.Handle(type, () => recovery.DiscardableId(phase), () => taps++, id => recovery.Discard(id, phase)); }
                Assert.True(recovery.Pending);
                Assert.False(recovery.Discard(recovery.PendingId.Value, phase));
                Assert.Equal(0, taps);
            }
        }

        [Fact]
        public void Orphan_events_do_not_discard_and_feedback_returns_to_recording()
        {
            var recovery = new DesktopDraftRecovery(new DesktopCommandRig.Automation());
            recovery.Retain("pending");
            var buttons = new DesktopVoiceDraftCommand.ButtonHandler();
            buttons.Handle(DeviceButtonEventType.LongPress, () => recovery.PendingId,
                () => throw new Exception("must not record"), id => recovery.Discard(id));
            Assert.True(recovery.Pending);
            var capture = new VoiceCaptureState();
            Assert.Equal("HOLD TO DISCARD", DesktopDictationFace.For(VoiceIntent.DesktopDraft,
                capture, "Draft Exists", "voice", pending: true).Footer);
            Assert.True(recovery.Discard(recovery.PendingId.Value));
            var result = DesktopDictationFace.For(VoiceIntent.DesktopDraft, capture, "Discarded", "voice");
            Assert.Equal("Discarded", result.Label);
            Assert.Equal("DICTATE", result.Footer);
            Assert.Equal("Dictate", DesktopDictationFace.For(VoiceIntent.DesktopDraft, capture, null, "voice").Label);
        }
    }
}
