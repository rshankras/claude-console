namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// A dictation that cannot be typed must say so on the key (2.2.1 Windows retest, item 6).
    ///
    /// The transcript path ended in <c>InjectText(text, pressEnter)</c>, which discarded the
    /// platform's outcome: with nothing pinned and no single obvious session — Windows has no
    /// frontmost query — the words were transcribed, logged, and dropped, and the device showed
    /// exactly what it shows for a delivered one. QA counted five dictations after un-pinning: four
    /// vanished, one landed in the wrong key's handler. These pin the two outcomes the key now
    /// reports, through the seam, with the fake platform bridge.
    /// </summary>
    public class VoiceDeliveryTests
    {
        private static (BridgeManager Bridge, PlatformSeamTests.FakePlatformBridge Fake, List<(VoiceIntent, String)> Failures) Rig(String activeTty)
        {
            var fake = new PlatformSeamTests.FakePlatformBridge();
            var bridge = new BridgeManager(fake) { ActiveTty = activeTty };
            var failures = new List<(VoiceIntent, String)>();
            bridge.OnVoiceFailed += (intent, text) => failures.Add((intent, text));
            return (bridge, fake, failures);
        }

        [Fact]
        public void A_transcript_with_a_target_is_typed_and_nothing_is_reported()
        {
            var (bridge, fake, failures) = Rig("ttys007");

            bridge.DeliverDictation("hello there", submit: true);

            var call = Assert.Single(fake.Texts);
            Assert.Equal("ttys007", call.Session);
            Assert.Equal("hello there", call.Text);
            Assert.True(call.Enter);
            Assert.Empty(failures);
            Assert.Equal(0, fake.Alerts);
        }

        [Fact]
        public void No_target_session_says_No_target_on_the_key_that_was_pressed()
        {
            var (bridge, fake, failures) = Rig(activeTty: null);

            bridge.DeliverDictation("hello there", submit: false);

            Assert.Empty(fake.Texts);
            var failure = Assert.Single(failures);
            Assert.Equal(VoiceIntent.Draft, failure.Item1);
            Assert.Equal(VoiceFailure.NoTarget, failure.Item2);
            Assert.Equal(1, fake.Alerts);
        }

        [Fact]
        public void A_refused_injection_says_Not_typed()
        {
            var (bridge, fake, failures) = Rig("ttys007");
            fake.Outcome = InjectionOutcome.SessionMissing;

            bridge.DeliverDictation("hello there", submit: true);

            Assert.Single(fake.Texts);
            var failure = Assert.Single(failures);
            Assert.Equal(VoiceIntent.Send, failure.Item1);
            Assert.Equal(VoiceFailure.NotTyped, failure.Item2);
        }
    }
}
