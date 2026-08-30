namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;

    /// <summary>
    /// After a frontmost probe overruns its budget, skip a few of its next opportunities instead of
    /// re-issuing it on the very next tick (#46).
    ///
    /// Why backing off is the right response and a bigger budget is not: measured on hardware
    /// (spikes/subproc-46) a healthy probe costs ~140ms of its 2000ms budget, ~215ms with every core
    /// saturated, and ~600ms with sixteen Apple Events in flight. Nothing reachable by load gets near
    /// the budget, so an overrun means the machine is stalled — and the next probe, two seconds later,
    /// is being asked the same question of the same stalled machine. Each attempt costs a full 2000ms
    /// wait, and polls are deliberately NON-OVERLAPPING, so every retry freezes the whole poll loop
    /// for two seconds while it fails.
    ///
    /// The cost of backing off, stated plainly: while skipping, the plugin keeps a stale idea of which
    /// tab is frontmost, so the display keys can describe the wrong session. That is the same harm the
    /// overrun itself causes — the probe had already failed to update it — so the backoff extends an
    /// existing gap rather than opening a new one. It is bounded for exactly that reason.
    ///
    /// Ceiling: the probe runs every 4th poll, ~2s apart when busy, so <see cref="MaxSkips"/> = 4 caps
    /// the blind window at roughly 8 seconds. The observed clusters (two and three overruns within a
    /// few seconds of each other) are the shape this is sized for.
    /// </summary>
    internal sealed class ProbeBackoff
    {
        /// <summary>Most opportunities ever skipped in a row — see the ceiling note above.</summary>
        internal const Int32 MaxSkips = 4;

        private Int32 _skipsRemaining;
        private Int32 _level;

        /// <summary>Opportunities still to be skipped. Diagnostics and tests.</summary>
        internal Int32 SkipsRemaining => this._skipsRemaining;

        /// <summary>True when this opportunity should be skipped, consuming one skip.</summary>
        internal Boolean ShouldSkip()
        {
            if (this._skipsRemaining <= 0)
            {
                return false;
            }

            this._skipsRemaining--;
            return true;
        }

        /// <summary>The probe overran. Double the skip window, up to the ceiling.</summary>
        internal void RecordTimeout()
        {
            this._level = this._level == 0 ? 1 : Math.Min(this._level * 2, MaxSkips);
            this._skipsRemaining = this._level;
        }

        /// <summary>
        /// The probe answered. Clear the window immediately rather than decaying it: one good answer
        /// proves the machine is servicing Apple Events again, and staying blind after that would be
        /// choosing a stale target for no reason.
        /// </summary>
        internal void RecordSuccess()
        {
            this._level = 0;
            this._skipsRemaining = 0;
        }
    }
}
