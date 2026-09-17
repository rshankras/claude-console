namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// The skip-window that stops the frontmost probe from re-issuing itself against a stalled
    /// machine (#46).
    ///
    /// These pin the policy rather than the plumbing. The measurements that justify the policy are in
    /// spikes/subproc-46: a healthy probe costs ~140ms of its 2000ms budget, ~215ms with every core
    /// saturated, ~600ms with sixteen Apple Events in flight — so an overrun is never "the budget is
    /// tight", and the retry it triggers would block the non-overlapping poll loop for a further
    /// 2000ms while failing again. If a future reader is tempted to widen the budget instead, that is
    /// the evidence to re-read first.
    /// </summary>
    public class ProbeBackoffTests
    {
        [Fact]
        public void FreshBackoffSkipsNothing()
        {
            var b = new ProbeBackoff();

            Assert.False(b.ShouldSkip());
            Assert.False(b.ShouldSkip());
        }

        [Fact]
        public void OneTimeoutSkipsExactlyOneOpportunity()
        {
            var b = new ProbeBackoff();

            b.RecordTimeout();

            Assert.True(b.ShouldSkip());    // the tick right after the overrun
            Assert.False(b.ShouldSkip());   // ...and then we try again
        }

        [Fact]
        public void ConsecutiveTimeoutsDoubleTheWindow()
        {
            var b = new ProbeBackoff();

            b.RecordTimeout();
            Assert.Equal(1, b.SkipsRemaining);

            b.RecordTimeout();
            Assert.Equal(2, b.SkipsRemaining);

            b.RecordTimeout();
            Assert.Equal(4, b.SkipsRemaining);
        }

        [Fact]
        public void TheWindowIsCappedSoTheKeysAreNeverBlindForLong()
        {
            var b = new ProbeBackoff();

            for (var i = 0; i < 20; i++)
            {
                b.RecordTimeout();
            }

            // THE important one. While skipping, the plugin keeps a STALE idea of which tab is
            // frontmost, so the display keys can describe the wrong session — the very harm #46
            // reports. An uncapped exponential would turn a momentary stall into minutes of that.
            // At ~2s per probe opportunity this ceiling is roughly an 8-second blind window.
            Assert.Equal(ProbeBackoff.MaxSkips, b.SkipsRemaining);
        }

        [Fact]
        public void OneGoodAnswerClearsTheWindowCompletely()
        {
            var b = new ProbeBackoff();
            b.RecordTimeout();
            b.RecordTimeout();
            b.RecordTimeout();
            Assert.Equal(4, b.SkipsRemaining);

            b.RecordSuccess();

            // Not decayed — cleared. A probe that answered proves the machine is servicing Apple
            // Events again, and staying blind after that would choose a stale target for no reason.
            Assert.Equal(0, b.SkipsRemaining);
            Assert.False(b.ShouldSkip());
        }

        [Fact]
        public void SuccessAlsoResetsTheDoublingNotJustTheCount()
        {
            var b = new ProbeBackoff();
            b.RecordTimeout();
            b.RecordTimeout();
            b.RecordTimeout();   // level is now 4

            b.RecordSuccess();
            b.RecordTimeout();

            // A later, unrelated stall starts from one skip again. Without resetting the LEVEL (as
            // opposed to only the remaining count) a machine that hiccups once an hour would climb
            // to the ceiling and stay there for the rest of the session.
            Assert.Equal(1, b.SkipsRemaining);
        }
    }
}
