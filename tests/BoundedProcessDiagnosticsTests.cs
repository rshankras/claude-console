namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// What a subprocess overrun tells the next person who reads the log (#46).
    ///
    /// The message this replaces was "exceeded 2000ms — killing (hung window server / locked screen /
    /// a11y prompt?)". All three suspects were false when the issue was filed, and the hardware
    /// measurements in spikes/subproc-46 explain why none of them is the usual cause: nothing
    /// reachable by load gets a probe near its budget (~140ms healthy, ~215ms with every core
    /// saturated, ~600ms with sixteen Apple Events in flight). A log line that guesses wrong sends the
    /// next reader after the wrong thing, so the guesses are gone and two facts took their place —
    /// how OFTEN this is happening, and how long the calls that nearly overran actually took.
    /// </summary>
    [Collection("bounded-process-overruns")]
    public class BoundedProcessDiagnosticsTests
    {
        public BoundedProcessDiagnosticsTests() => BoundedProcess.ResetOverrunsForTest();

        // -------------------------------------------------------------------------------------
        // Rate: the question the old message could not answer
        // -------------------------------------------------------------------------------------

        [Fact]
        public void OverrunsAreCountedPerExecutable()
        {
            Assert.Equal("1st overrun since load", BoundedProcess.NoteOverrun("osascript"));
            Assert.Equal("2nd overrun since load", BoundedProcess.NoteOverrun("osascript"));
            Assert.Equal("3rd overrun since load", BoundedProcess.NoteOverrun("osascript"));
            Assert.Equal("4th overrun since load", BoundedProcess.NoteOverrun("osascript"));

            // /bin/ps keeps its own tally. #46's evidence was "ps 7x and osascript 3x" — one shared
            // counter would have hidden exactly that split, and the split is what showed #27's fix
            // had cured the ps half and left the osascript half untouched.
            Assert.Equal("1st overrun since load", BoundedProcess.NoteOverrun("/bin/ps"));
        }

        [Theory]
        [InlineData(11, "11th")]
        [InlineData(12, "12th")]
        [InlineData(13, "13th")]
        [InlineData(21, "21st")]
        [InlineData(22, "22nd")]
        [InlineData(23, "23rd")]
        public void TheTeensAreOrdinalledCorrectly(Int32 n, String expected)
        {
            String last = null;
            for (var i = 0; i < n; i++)
            {
                last = BoundedProcess.NoteOverrun("osascript");
            }

            Assert.Equal($"{expected} overrun since load", last);
        }

        // -------------------------------------------------------------------------------------
        // Creep detection: cliff or creep, answered by durations rather than by guessing
        // -------------------------------------------------------------------------------------

        [Fact]
        public void AComfortableCallSaysNothing()
        {
            // The measured normal for the frontmost probe. Logging this every 2s would be noise.
            Assert.Null(BoundedProcess.SlowNote("osascript", elapsedMs: 140, timeoutMs: 2000));
        }

        [Fact]
        public void ACallThatAteHalfItsBudgetIsRecordedWithItsRealDuration()
        {
            var note = BoundedProcess.SlowNote("osascript", elapsedMs: 1400, timeoutMs: 2000);

            // The duration is the point: a handful of these in the log is the difference between
            // "the budget is creeping up on us" and "the machine stalled once", which is precisely
            // what #46 could not tell from the old message.
            Assert.Equal("osascript took 1400ms of its 2000ms budget", note);
        }

        [Fact]
        public void TheThresholdIsExactlyHalfTheBudget()
        {
            Assert.Null(BoundedProcess.SlowNote("osascript", 999, 2000));
            Assert.NotNull(BoundedProcess.SlowNote("osascript", 1000, 2000));
        }

        [Fact]
        public void LongBudgetsAreExemptBecauseSlowIsTheirNormalState()
        {
            // THE important one, and the reason a plain fraction is not enough on its own.
            // `screencapture -i` (120s) waits for a human to drag a selection and the whisper helper
            // (130s) transcribes audio — both are MEANT to take most of their budget. Warning about
            // them every time would bury the poll-path lines this exists to surface.
            Assert.Null(BoundedProcess.SlowNote("/usr/sbin/screencapture", elapsedMs: 90_000, timeoutMs: 120_000));
            Assert.Null(BoundedProcess.SlowNote("ClaudeVoiceHelper", elapsedMs: 100_000, timeoutMs: 130_000));

            // The 5s `ps` scan and the 2s probe — the repeated, poll-path calls — stay covered.
            Assert.NotNull(BoundedProcess.SlowNote("/bin/ps", elapsedMs: 4_000, timeoutMs: 5_000));
        }
    }
}
