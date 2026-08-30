namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    using Xunit;

    /// <summary>
    /// When a "busy" session has actually stopped working (#30).
    ///
    /// The bug: interrupt a turn with Esc and Claude Code fires NO hook, so the last thing written
    /// is "busy" and the key keeps its hourglass over an idle session. The tempting repair is to
    /// expire busy by age, and the tests below exist mostly to pin why that alone is wrong — a slow
    /// tool call and a dead turn are identical by age, and only the transcript tells them apart.
    /// </summary>
    public class ActivityStallTests
    {
        private const Int64 Now = 1_800_000_000;

        private static Boolean Stalled(String state, Int64 activityAgeS, Int64? transcriptAgeS) =>
            ActivityStall.IsStalledBusy(
                state,
                Now - activityAgeS,
                transcriptAgeS.HasValue ? Now - transcriptAgeS.Value : (Int64?)null,
                Now);

        // -----------------------------------------------------------------------------------
        // Only "busy" can stall
        // -----------------------------------------------------------------------------------

        [Theory]
        [InlineData("waiting")]
        [InlineData("ready")]
        [InlineData("done")]
        [InlineData(null)]
        public void OnlyBusyCanStall(String state)
        {
            // "waiting" in particular must never be expired here: a real approval can sit for hours,
            // and that asymmetry with busy is deliberate (see #51).
            Assert.False(Stalled(state, activityAgeS: 99_999, transcriptAgeS: 99_999));
        }

        // -----------------------------------------------------------------------------------
        // The transcript decides, whenever there is one
        // -----------------------------------------------------------------------------------

        [Fact]
        public void AnInterruptedTurnIsDetected()
        {
            // #30 as reported and measured: the last hook fired 114s ago and the transcript stopped
            // growing 113s ago. Nothing else will ever be written, so the key must let go.
            Assert.True(Stalled("busy", activityAgeS: 114, transcriptAgeS: 113));
        }

        [Fact]
        public void AWorkingSessionKeepsItsHourglass()
        {
            // Measured live while writing this: a healthy session's transcript lagged ~9s behind its
            // state file. A window tight enough to trip on that would flicker the key constantly.
            Assert.False(Stalled("busy", activityAgeS: 2, transcriptAgeS: 9));
        }

        [Fact]
        public void ALongToolCallIsNotAStall()
        {
            // THE important one. The hooks last fired ten minutes ago because PostToolUse hasn't
            // come round again — but the transcript moved a second ago, so the agent is plainly
            // still working. The 45s age check this replaces called this session finished and
            // cleared the key out from under a live turn; that is the regression the transcript
            // rule exists to prevent, and why the issue rejected a plain timeout.
            Assert.False(Stalled("busy", activityAgeS: 600, transcriptAgeS: 1));
        }

        [Fact]
        public void TheQuietWindowIsNinetySeconds()
        {
            Assert.False(Stalled("busy", activityAgeS: 200, transcriptAgeS: 90));
            Assert.True(Stalled("busy", activityAgeS: 200, transcriptAgeS: 91));
        }

        // -----------------------------------------------------------------------------------
        // When the plugin sent the interrupt itself
        // -----------------------------------------------------------------------------------

        private static Boolean StalledAfterEsc(Int64 escAgeS, Int64? transcriptAgeS) =>
            ActivityStall.IsStalledBusy(
                "busy",
                Now - 30,
                transcriptAgeS.HasValue ? Now - transcriptAgeS.Value : (Int64?)null,
                Now,
                Now - escAgeS);

        [Fact]
        public void OurOwnEscapeClearsTheKeyInSecondsNotMinutes()
        {
            // Pressing Esc on the KEYPAD goes through the plugin, so this is not an inference — it
            // is something the plugin did. Making the user watch an hourglass for 90s over a turn
            // the plugin ended itself is the complaint that produced this path.
            Assert.False(StalledAfterEsc(escAgeS: 5, transcriptAgeS: 20));
            Assert.True(StalledAfterEsc(escAgeS: 6, transcriptAgeS: 20));
        }

        [Fact]
        public void AnEscapeThatDidNotStopTheTurnIsIgnored()
        {
            // THE important one. Escape is not exclusively "interrupt": it exits a mode, dismisses a
            // menu, and AnswerCommand sends it to REJECT a tool — after which the agent keeps going.
            // The transcript moved AFTER our Escape, so the turn is alive and the hint must not
            // clear the key. Without this guard, rejecting a tool would report the session idle
            // while it was still working.
            Assert.False(StalledAfterEsc(escAgeS: 60, transcriptAgeS: 2));
        }

        [Fact]
        public void AfterAnIgnoredEscapeTheNormalWindowStillApplies()
        {
            // The hint is discarded, not sticky: the ordinary transcript rule takes over, so a
            // session that later does stall still settles.
            Assert.True(StalledAfterEsc(escAgeS: 300, transcriptAgeS: 120));
        }

        [Fact]
        public void AStaleEscapeFromARecycledTtyIsIgnored()
        {
            // Found in review. macOS reuses tty names as tabs close and open, and the interrupt
            // map is keyed by tty. Without this guard an Escape sent to the PREVIOUS occupant of
            // ttys012 would clear a brand-new session the instant it went busy — and on the
            // no-transcript path it would do so immediately and on every poll.
            var busyAt = Now - 10;      // the new session's UserPromptSubmit
            var oldEsc = Now - 3600;    // an Escape sent an hour ago, to somebody else
            Assert.False(ActivityStall.IsStalledBusy("busy", busyAt, null, Now, oldEsc));
            Assert.False(ActivityStall.IsStalledBusy("busy", busyAt, Now - 8, Now, oldEsc));
        }

        [Fact]
        public void AnEscapeInTheSameSecondAsTheBusyWriteStillCounts()
        {
            // Both stamps are whole seconds. Pressing Esc within the same second as the prompt
            // went busy is unusual but legal, and must not fall on the wrong side of the guard.
            Assert.True(ActivityStall.IsStalledBusy("busy", Now - 10, null, Now, Now - 10));
        }

        [Fact]
        public void AnEscapeSentToAnIdleSessionChangesNothing()
        {
            // Only "busy" can stall, hint or no hint.
            Assert.False(ActivityStall.IsStalledBusy("waiting", Now - 30, Now - 300, Now, Now - 300));
        }

        // -----------------------------------------------------------------------------------
        // With no transcript, fall back to age — but a patient one
        // -----------------------------------------------------------------------------------

        [Fact]
        public void WithNoTranscriptAMissedStopHookStillSettles()
        {
            // The original reason this mechanism existed at all: a session killed with -9 never
            // sends Stop. Without a transcript there is nothing better than age to go on.
            Assert.False(Stalled("busy", activityAgeS: 300, transcriptAgeS: null));
            Assert.True(Stalled("busy", activityAgeS: 301, transcriptAgeS: null));
        }

        [Fact]
        public void TheFallbackIsFarMorePatientThanTheFortyFiveSecondsItReplaces()
        {
            // 45s was short enough to declare a legitimately long tool call finished. With no
            // transcript to check, staying busy too long beats clearing a key that is still working:
            // the key would otherwise report something the agent never said.
            Assert.False(Stalled("busy", activityAgeS: 46, transcriptAgeS: null));
            Assert.False(Stalled("busy", activityAgeS: 120, transcriptAgeS: null));
        }

        // -----------------------------------------------------------------------------------
        // Reading the mtime
        // -----------------------------------------------------------------------------------

        [Fact]
        public void AnAbsentTranscriptReadsAsNoTranscriptNotAsInfinitelyQuiet()
        {
            // The distinction matters: "infinitely quiet" would stall every session whose transcript
            // path is unreadable, clearing keys on sessions that are working perfectly well.
            Assert.Null(ActivityStall.TranscriptMtime(null));
            Assert.Null(ActivityStall.TranscriptMtime("   "));
            Assert.Null(ActivityStall.TranscriptMtime("/no/such/file-" + Guid.NewGuid().ToString("N")));
        }

        [Fact]
        public void ARealFileReportsItsLastWriteTime()
        {
            var path = Path.Combine(Path.GetTempPath(), "stall-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                File.WriteAllText(path, "{}");
                var mtime = ActivityStall.TranscriptMtime(path);

                Assert.NotNull(mtime);
                Assert.InRange(
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds() - mtime.Value, -5, 5);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
