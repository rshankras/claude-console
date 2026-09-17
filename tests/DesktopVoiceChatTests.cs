namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;

    public class DesktopVoiceChatTests
    {
        [Theory]
        [InlineData("ready", "Voice Chat")]
        [InlineData("active", "End Voice")]
        [InlineData("", "No Voice")]
        [InlineData("listening", "No Voice")]
        public void Only_observed_native_controls_drive_the_face(String wireState, String label)
        {
            var snap = DesktopSnapshot.Parse($"{{\"ok\":true,\"surface\":true,\"voiceChat\":\"{wireState}\"}}");
            var state = DesktopMonitor.Map(snap);
            Assert.Equal(label, DesktopVoiceChatCommand.LabelFor(state));
            Assert.Equal(DesktopActivity.Ready, state.Activity); // Voice is not task generation.
            Assert.Equal("Unavailable", DesktopVoiceChatCommand.LabelFor(DesktopMonitor.Map(
                DesktopSnapshot.Parse($"{{\"ok\":true,\"surface\":false,\"voiceChat\":\"{wireState}\"}}"))));
        }

        [Fact]
        public void App_side_voice_changes_refresh_the_key_without_other_activity_changes()
        {
            var fake = new Fake();
            using var monitor = new DesktopMonitor(fake);
            var labels = new List<String>();
            monitor.OnChanged += state => labels.Add(DesktopVoiceChatCommand.LabelFor(state));
            foreach (var voice in new[] { DesktopVoiceState.Ready, DesktopVoiceState.Active, DesktopVoiceState.Ready })
            {
                fake.Next = new DesktopSnapshot { SurfaceAvailable = true, VoiceChat = voice };
                monitor.PollOnce();
            }
            fake.Next = DesktopSnapshot.Unavailable;
            monitor.PollOnce();
            Assert.Equal(new[] { "Voice Chat", "End Voice", "Voice Chat", "Unavailable" }, labels);
        }

        [Theory]
        [InlineData(true, "start")]
        [InlineData(false, "end")]
        public void Native_transition_is_one_explicit_helper_operation(Boolean active, String action)
        {
            var calls = new List<List<String>>();
            var auto = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            { Runner = (args, _) => { calls.Add(args); return "{\"ok\":true}"; } };
            Assert.True(auto.SetVoiceChat(active, out var error));
            Assert.Null(error);
            var call = Assert.Single(calls);
            Assert.Equal("voice", call[0]);
            Assert.Equal(action, call[call.IndexOf("--action") + 1]);
            Assert.Contains("Start voice chat", call);
            Assert.Contains("Start new voice chat", call);
            Assert.Contains("Stop voice chat", call);
            Assert.DoesNotContain("--text", call);
        }

        [Fact]
        public void Voice_state_guard_failure_is_propagated_without_fallback()
        {
            var calls = 0;
            var auto = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            { Runner = (_, _) => { calls++; return "{\"ok\":false,\"error\":\"voice-state-changed\"}"; } };
            Assert.False(auto.SetVoiceChat(false, out var error));
            Assert.Equal("voice-state-changed", error);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void Stop_uses_an_exact_verb_that_older_helpers_cannot_mistake_for_voice()
        {
            List<String> args = null;
            var auto = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            { Runner = (call, _) => { args = call; return "{\"ok\":true}"; } };
            Assert.True(auto.PressExact(new[] { "Stop" }));
            Assert.Equal("press-exact", args[0]);
            Assert.Equal("Stop", args[args.IndexOf("--label") + 1]);
        }

        [Fact]
        public void Rapid_second_press_does_not_reverse_a_new_session()
        {
            var fake = new Fake();
            var now = 100L;
            var actions = new DesktopVoiceActions(fake) { Clock = () => now };
            var capture = new VoiceCaptureState();
            Assert.True(actions.RequestVoice(DesktopVoiceState.Ready, capture, out var feedback));
            Assert.Equal("Check App", feedback); // not "Listening" based only on a successful press
            now += 100;
            Assert.False(actions.RequestVoice(DesktopVoiceState.Active, capture, out _));
            Assert.Equal(new[] { true }, fake.Requests);
            now += 1200;
            Assert.True(actions.RequestVoice(DesktopVoiceState.Active, capture, out _));
            Assert.Equal(new[] { true, false }, fake.Requests);
        }

        [Fact]
        public void Local_recording_and_transcription_block_native_start_but_allow_native_end()
        {
            var fake = new Fake();
            var actions = new DesktopVoiceActions(fake);
            var capture = new VoiceCaptureState();
            capture.Press(VoiceIntent.DesktopDraft, DateTime.UtcNow);
            Assert.False(actions.RequestVoice(DesktopVoiceState.Ready, capture, out var feedback));
            Assert.Equal("Dictating", feedback);
            capture.Press(VoiceIntent.DesktopDraft, DateTime.UtcNow);
            Assert.False(actions.RequestVoice(DesktopVoiceState.Ready, capture, out _));
            Assert.Empty(fake.Requests);
            Assert.True(actions.RequestVoice(DesktopVoiceState.Active, capture, out _));
            Assert.Equal(new[] { false }, fake.Requests);
        }

        [Fact]
        public void Active_native_session_blocks_local_start_but_never_strands_an_existing_capture()
        {
            var fake = new Fake { Next = new DesktopSnapshot { SurfaceAvailable = true, VoiceChat = DesktopVoiceState.Active } };
            var actions = new DesktopVoiceActions(fake);
            var capture = new VoiceCaptureState();
            var toggles = 0;
            void Toggle(VoiceIntent _) => toggles++;
            Assert.False(actions.RequestDictation(VoiceIntent.DesktopDraft, capture, Toggle, out var feedback));
            Assert.Equal("End Voice", feedback);
            Assert.Equal(0, toggles);
            capture.Press(VoiceIntent.Desktop, DateTime.UtcNow);
            Assert.True(actions.RequestDictation(VoiceIntent.DesktopDraft, capture, Toggle, out _));
            Assert.Equal(1, toggles);
            Assert.Equal(VoiceIntent.Desktop, capture.Intent);
        }

        [Fact]
        public void Unavailable_voice_keeps_dictation_usable_without_attempting_a_native_transition()
        {
            var fake = new Fake();
            var actions = new DesktopVoiceActions(fake);
            var capture = new VoiceCaptureState();
            Assert.False(actions.RequestVoice(DesktopVoiceState.Unavailable, capture, out _));
            Assert.Empty(fake.Requests);
            var toggled = false;
            Assert.True(actions.RequestDictation(VoiceIntent.DesktopDraft, capture, _ => toggled = true, out _));
            Assert.True(toggled);
        }

        private sealed class Fake : IDesktopAutomation
        {
            public DesktopSnapshot Next = DesktopSnapshot.Unavailable;
            public readonly List<Boolean> Requests = new List<Boolean>();
            public DesktopSnapshot Status() => Next;
            public Boolean SetVoiceChat(Boolean active, out String error) { Requests.Add(active); error = null; return true; }
            public Boolean Press(String[] labels, out String matched) => throw new InvalidOperationException();
            public Boolean PressGuarded(String[] labels, String expectCard, out String matched, out String error) => throw new InvalidOperationException();
            public Boolean WriteComposer(String text, Boolean send, out String error) => throw new InvalidOperationException();
            public Boolean SwitchMode(String modeName) => throw new InvalidOperationException();
            public Boolean FocusApp() => throw new InvalidOperationException();
        }
    }
}
