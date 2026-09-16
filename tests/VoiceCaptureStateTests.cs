namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

    using Xunit;

    /// <summary>
    /// One microphone, three keys (#28).
    ///
    /// Voice, Voice Draft and Go to Project each held their own private "am I recording?" flag, and
    /// the engine held none. Pressing a second voice key mid-recording launched a SECOND helper
    /// against the same WAV, stop flag and transcript path — and, because each key both started and
    /// routed, the destination was decided by whichever key you pressed SECOND. Dictate a prompt,
    /// press Go to Project to stop it, and the prompt was fuzzy-matched to a project and opened.
    ///
    /// The clock is passed in, so every transition here is exercised without a microphone.
    /// </summary>
    public class VoiceCaptureStateTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 27, 22, 0, 0, DateTimeKind.Utc);

        [Theory]
        [InlineData(VoiceIntent.Send, VoiceIntent.Draft)]
        [InlineData(VoiceIntent.Send, VoiceIntent.Project)]
        [InlineData(VoiceIntent.Draft, VoiceIntent.Send)]
        [InlineData(VoiceIntent.Draft, VoiceIntent.Project)]
        [InlineData(VoiceIntent.Project, VoiceIntent.Send)]
        [InlineData(VoiceIntent.Project, VoiceIntent.Draft)]
        public void Windows_cross_key_cancel_waits_for_cleanup_then_restarts_with_the_new_intent(
            VoiceIntent initial, VoiceIntent next)
        {
            var state = new VoiceCaptureState();
            Assert.Equal(VoiceAction.Start, state.Press(initial, T0, awaitReadiness: true).Action);
            Assert.Equal("Starting", state.StartupLabel(initial));
            Assert.Null(state.StartupLabel(next));
            var cancelled = state.Press(next, T0.AddSeconds(1), awaitReadiness: true);
            Assert.Equal(VoiceAction.Cancel, cancelled.Action);
            Assert.Equal(initial, cancelled.Intent);
            Assert.False(state.MarkReady(T0.AddSeconds(2)));
            Assert.Equal("Cancelling", state.StartupLabel(initial));
            Assert.Equal(VoiceAction.Refuse, state.Press(next, T0.AddSeconds(3), awaitReadiness: true).Action);
            state.Finish(); // helper exit/cleanup, not merely the second key press
            Assert.Equal(VoiceAction.Start, state.Press(next, T0.AddSeconds(4), awaitReadiness: true).Action);
            Assert.True(state.MarkReady(T0.AddSeconds(5)));
            var stopped = state.Press(initial, T0.AddSeconds(6), awaitReadiness: true);
            Assert.Equal(VoiceAction.Stop, stopped.Action);
            Assert.Equal(next, stopped.Intent);
        }

        [Fact]
        public void A_press_from_idle_starts_recording_with_that_keys_intent()
        {
            var state = new VoiceCaptureState();

            var (action, intent) = state.Press(VoiceIntent.Draft, T0);

            Assert.Equal(VoiceAction.Start, action);
            Assert.Equal(VoiceIntent.Draft, intent);
            Assert.Equal(VoicePhase.Recording, state.Phase);
        }

        [Fact]
        public void The_same_key_pressed_again_stops_rather_than_starting_a_second_capture()
        {
            var state = new VoiceCaptureState();
            state.Press(VoiceIntent.Send, T0);

            var (action, _) = state.Press(VoiceIntent.Send, T0.AddSeconds(3));

            Assert.Equal(VoiceAction.Stop, action);
            Assert.Equal(VoicePhase.Transcribing, state.Phase);
        }

        /// <summary>The duplicate-helper defect: a DIFFERENT key must stop, never start.</summary>
        [Theory]
        [InlineData(VoiceIntent.Send, VoiceIntent.Project)]
        [InlineData(VoiceIntent.Project, VoiceIntent.Send)]
        [InlineData(VoiceIntent.Draft, VoiceIntent.Send)]
        [InlineData(VoiceIntent.Send, VoiceIntent.Draft)]
        public void A_different_voice_key_stops_the_running_capture(VoiceIntent started, VoiceIntent pressed)
        {
            var state = new VoiceCaptureState();
            state.Press(started, T0);

            var (action, _) = state.Press(pressed, T0.AddSeconds(2));

            Assert.Equal(VoiceAction.Stop, action);
        }

        /// <summary>
        /// The dangerous half. Stopping a dictation with the Go to Project key must still SEND the
        /// dictation — not fuzzy-match your sentence against project names and open one.
        /// </summary>
        [Theory]
        [InlineData(VoiceIntent.Send, VoiceIntent.Project)]
        [InlineData(VoiceIntent.Project, VoiceIntent.Send)]
        [InlineData(VoiceIntent.Draft, VoiceIntent.Project)]
        public void The_transcript_is_routed_by_the_starting_key_not_the_stopping_one(
            VoiceIntent started, VoiceIntent stoppedWith)
        {
            var state = new VoiceCaptureState();
            state.Press(started, T0);

            var (_, routed) = state.Press(stoppedWith, T0.AddSeconds(2));

            Assert.Equal(started, routed);
        }

        /// <summary>A press while a result is in flight would delete the file the wait thread reads.</summary>
        [Fact]
        public void A_press_while_transcribing_is_refused()
        {
            var state = new VoiceCaptureState();
            state.Press(VoiceIntent.Send, T0);
            state.Press(VoiceIntent.Send, T0.AddSeconds(2));   // -> Transcribing

            var (action, _) = state.Press(VoiceIntent.Project, T0.AddSeconds(3));

            Assert.Equal(VoiceAction.Refuse, action);
            Assert.Equal(VoicePhase.Transcribing, state.Phase);
        }

        [Fact]
        public void Finishing_returns_to_idle_and_the_next_press_starts_cleanly()
        {
            var state = new VoiceCaptureState();
            state.Press(VoiceIntent.Send, T0);
            state.Press(VoiceIntent.Send, T0.AddSeconds(2));
            state.Finish();

            Assert.Equal(VoicePhase.Idle, state.Phase);

            var (action, intent) = state.Press(VoiceIntent.Project, T0.AddSeconds(5));
            Assert.Equal(VoiceAction.Start, action);
            Assert.Equal(VoiceIntent.Project, intent);
        }

        [Fact]
        public void Finishing_twice_is_harmless()
        {
            var state = new VoiceCaptureState();
            state.Press(VoiceIntent.Send, T0);
            state.Finish();
            state.Finish();

            Assert.Equal(VoicePhase.Idle, state.Phase);
        }

        // -----------------------------------------------------------------------------------------
        // Staleness — the failure mode a plain "hold a flag" fix would introduce.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// If a helper dies without writing anything, the state must expire. A flag that could not
        /// would leave every voice key dead until the plugin reloaded — worse than the original bug.
        /// </summary>
        [Fact]
        public void A_capture_older_than_any_real_capture_is_treated_as_dead()
        {
            var state = new VoiceCaptureState();
            state.Press(VoiceIntent.Send, T0);

            var (action, intent) = state.Press(
                VoiceIntent.Project, T0 + VoiceCaptureState.StaleAfter + TimeSpan.FromSeconds(1));

            Assert.Equal(VoiceAction.Start, action);
            Assert.Equal(VoiceIntent.Project, intent);
        }

        /// <summary>But a capture still within its lifetime must be stopped, not restarted.</summary>
        [Fact]
        public void A_capture_within_its_lifetime_is_still_stopped()
        {
            var state = new VoiceCaptureState();
            state.Press(VoiceIntent.Send, T0);

            var (action, _) = state.Press(
                VoiceIntent.Project, T0 + VoiceCaptureState.StaleAfter - TimeSpan.FromSeconds(1));

            Assert.Equal(VoiceAction.Stop, action);
        }

        [Fact]
        public void A_stuck_transcribe_also_expires()
        {
            var state = new VoiceCaptureState();
            state.Press(VoiceIntent.Send, T0);
            state.Press(VoiceIntent.Send, T0.AddSeconds(1));   // -> Transcribing

            var (action, _) = state.Press(
                VoiceIntent.Send, T0 + VoiceCaptureState.StaleAfter + TimeSpan.FromSeconds(5));

            Assert.Equal(VoiceAction.Start, action);
        }

        // -----------------------------------------------------------------------------------------
        // What the keys read to paint themselves
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void Only_the_key_that_started_shows_as_recording()
        {
            var state = new VoiceCaptureState();
            state.Press(VoiceIntent.Project, T0);

            Assert.True(state.IsRecording(VoiceIntent.Project));
            Assert.False(state.IsRecording(VoiceIntent.Send));
            Assert.False(state.IsRecording(VoiceIntent.Draft));
        }

        [Fact]
        public void No_key_shows_as_recording_once_transcribing()
        {
            var state = new VoiceCaptureState();
            state.Press(VoiceIntent.Project, T0);
            state.Press(VoiceIntent.Project, T0.AddSeconds(2));

            Assert.False(state.IsRecording(VoiceIntent.Project));
        }

        [Fact]
        public void Every_phase_change_notifies_the_keys()
        {
            var state = new VoiceCaptureState();
            var notifications = 0;
            state.Changed += () => notifications++;

            state.Press(VoiceIntent.Send, T0);            // -> Recording
            state.Press(VoiceIntent.Send, T0.AddSeconds(1)); // -> Transcribing
            state.Finish();                                // -> Idle

            Assert.Equal(3, notifications);
        }

        /// <summary>A refused press changes nothing, so it must not repaint anything either.</summary>
        [Fact]
        public void A_refused_press_raises_no_notification()
        {
            var state = new VoiceCaptureState();
            state.Press(VoiceIntent.Send, T0);
            state.Press(VoiceIntent.Send, T0.AddSeconds(1));   // -> Transcribing

            var notifications = 0;
            state.Changed += () => notifications++;
            state.Press(VoiceIntent.Draft, T0.AddSeconds(2));

            Assert.Equal(0, notifications);
        }
    }
}
