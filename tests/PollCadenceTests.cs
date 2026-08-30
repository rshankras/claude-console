namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;

    using Xunit;

    /// <summary>
    /// The idle poll cadence (#27).
    ///
    /// The plugin polled every 500 ms forever and raised its change events unconditionally, so the
    /// keys re-rendered ~11 times a second on a machine with no session running and the keypad
    /// showing another application's profile entirely — around 8% of a CPU core, indefinitely, for
    /// pixels nobody was looking at.
    ///
    /// Backing off is only safe while there is genuinely nothing to watch. These tests pin that
    /// condition, because the failure mode of getting it wrong is not a crash — it is a keypad that
    /// feels sluggish, which no other test would notice.
    /// </summary>
    public class PollCadenceTests
    {
        [Fact]
        public void A_fresh_counter_polls_at_the_fast_cadence()
        {
            Assert.Equal(500, BridgeManager.PollDelayForQuietCount(0));
        }

        [Fact]
        public void Ten_seconds_of_nothing_earns_the_slow_cadence()
        {
            Assert.Equal(500, BridgeManager.PollDelayForQuietCount(19));
            Assert.Equal(2000, BridgeManager.PollDelayForQuietCount(20));
        }

        [Fact]
        public void A_couple_of_minutes_of_nothing_earns_the_idle_cadence()
        {
            Assert.Equal(2000, BridgeManager.PollDelayForQuietCount(59));
            Assert.Equal(5000, BridgeManager.PollDelayForQuietCount(60));
            Assert.Equal(5000, BridgeManager.PollDelayForQuietCount(100_000));
        }

        /// <summary>The cadence must stay inside its stated bounds for ANY counter value.</summary>
        [Theory]
        [InlineData(-5)]
        [InlineData(0)]
        [InlineData(21)]
        [InlineData(1000)]
        [InlineData(Int32.MaxValue)]
        public void The_delay_is_always_between_the_fast_and_idle_bounds(Int32 quiet)
        {
            var delay = BridgeManager.PollDelayForQuietCount(quiet);
            Assert.InRange(delay, 500, 5000);
        }

        // -----------------------------------------------------------------------------------------
        // What counts as a quiet poll
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void A_change_snaps_back_to_the_fast_cadence_immediately()
        {
            var quiet = BridgeManager.NextQuietCount(500, anythingChanged: true, anyLiveSession: false);

            Assert.Equal(0, quiet);
            Assert.Equal(500, BridgeManager.PollDelayForQuietCount(quiet));
        }

        /// <summary>
        /// A live session means something can change at any moment — an approval card, a cost tick.
        /// The keypad must never be slow while a session exists, however long it has been quiet.
        /// </summary>
        [Fact]
        public void A_live_session_keeps_the_fast_cadence_even_when_nothing_changes()
        {
            var quiet = BridgeManager.NextQuietCount(500, anythingChanged: false, anyLiveSession: true);

            Assert.Equal(0, quiet);
            Assert.Equal(500, BridgeManager.PollDelayForQuietCount(quiet));
        }

        [Fact]
        public void Only_a_poll_with_nothing_to_watch_increments_the_counter()
        {
            Assert.Equal(4, BridgeManager.NextQuietCount(3, anythingChanged: false, anyLiveSession: false));
        }

        /// <summary>
        /// Left alone for a weekend, the counter must not overflow into a negative number and quietly
        /// restore the 500 ms cadence — the exact bug this change exists to remove.
        /// </summary>
        [Fact]
        public void The_counter_saturates_rather_than_overflowing()
        {
            var quiet = BridgeManager.NextQuietCount(Int32.MaxValue, anythingChanged: false, anyLiveSession: false);

            Assert.True(quiet >= 0, "the quiet counter went negative");
            Assert.Equal(5000, BridgeManager.PollDelayForQuietCount(quiet));
        }

        [Fact]
        public void An_idle_machine_reaches_the_idle_cadence_and_stays_there()
        {
            var quiet = 0;
            for (var poll = 0; poll < 500; poll++)
            {
                quiet = BridgeManager.NextQuietCount(quiet, anythingChanged: false, anyLiveSession: false);
            }

            Assert.Equal(5000, BridgeManager.PollDelayForQuietCount(quiet));

            // ...and one session appearing is enough to bring it straight back.
            quiet = BridgeManager.NextQuietCount(quiet, anythingChanged: false, anyLiveSession: true);
            Assert.Equal(500, BridgeManager.PollDelayForQuietCount(quiet));
        }

        // -----------------------------------------------------------------------------------------
        // Change-driven repaints.
        //
        // These are SOURCE checks, and deliberately so. A key action cannot be constructed without
        // the Loupedeck plugin host, so no test in this suite has ever executed one — which is
        // exactly how a key that repainted itself twice a second for eighteen months went unnoticed
        // through a green suite. Asserting on the source is weak, but it is not nothing: it fails
        // loudly if the guard is deleted, and that is the regression worth catching.
        // -----------------------------------------------------------------------------------------

        private static String Source(params String[] relative)
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(new[] { dir }.Concat(relative).ToArray());
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate " + Path.Combine(relative));
        }

        [Fact]
        public void The_poll_raises_the_state_event_only_when_the_state_file_changed()
        {
            var bridge = Source("src", "Core", "BridgeManager.cs");

            Assert.Contains("_lastStateText", bridge);
            Assert.Contains("stateChanged", bridge);

            // The old shape: read, then raise on every poll for as long as the file existed.
            Assert.DoesNotContain("ReadJsonWithRetry<ClaudeState>(ActiveStateFile())", bridge);
        }

        [Fact]
        public void The_activity_key_repaints_only_when_its_face_changed()
        {
            var status = Source("src", "Core", "Actions", "StatusCommand.cs");

            Assert.Contains("previousStatus", status);
            Assert.Contains("_status != previousStatus", status);
        }

        [Fact]
        public void The_model_key_repaints_only_when_the_model_name_changed()
        {
            var model = Source("src", "Core", "Actions", "ModelCycleCommand.cs");

            // The guard, not its exact wording: the key must compare before repainting. (It also
            // repaints when coming back from "no data for this session" — #49.)
            Assert.Contains("name != _displayName", model);
        }

        /// <summary>
        /// Cost and Context were already change-guarded, which is why QA counted 60 and 34 renders
        /// against Status's 2,658 and Model's 1,642. Keep them that way.
        /// </summary>
        [Fact]
        public void The_keys_that_were_already_guarded_stay_guarded()
        {
            Assert.Contains("newCost != _cost", Source("src", "Core", "Actions", "CostDisplayCommand.cs"));
            Assert.Contains("pct != _percent", Source("src", "Core", "Actions", "ContextCommand.cs"));
        }
    }
}
