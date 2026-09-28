namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;

    using Loupedeck.ClaudeConsolePlugin.Agents;
    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// #112. A session key press ran the Windows focus helper (1.3–1.8 s measured on the owner
    /// laptop, 1.8 s at Logitech) inside the SDK's 1,000 ms action budget, so the service logged
    /// "Action timed out" and "action failed" for a pin that had worked. The press now returns at
    /// once and the selection lands on a background task; everything SelectSlot guaranteed —
    /// routing changes only after focus succeeds, pressing the pinned slot releases it — must
    /// still hold, and a press during a selection must be dropped rather than queued.
    /// </summary>
    public sealed class SlotSelectionAsyncTests : IDisposable
    {
        private readonly String _root = Path.Combine(Path.GetTempPath(), "cc-slot-async-" + Guid.NewGuid().ToString("N"));
        private readonly PlatformSeamTests.FakePlatformBridge _platform = new();
        private readonly BridgeManager _bridge;

        public SlotSelectionAsyncTests()
        {
            var sessions = Path.Combine(this._root, "sessions");
            var activity = Path.Combine(this._root, "activity");
            Directory.CreateDirectory(sessions);
            Directory.CreateDirectory(activity);
            foreach (var (tty, project) in new[] { ("ttys001", "alpha"), ("ttys002", "beta") })
            {
                File.WriteAllText(Path.Combine(sessions, tty + ".json"), JsonSerializer.Serialize(new
                {
                    session_id = "sid-" + tty,
                    workspace = new { project_dir = "/Users/x/" + project },
                    context_window = new { used_percentage = 10 },
                }));
            }
            var grid = new SessionRegistry(sessions, activity, Path.Combine(this._root, "registry.json")) { Agent = new ClaudeCodeAdapter() };
            grid.Refresh(new HashSet<String>(new[] { "ttys001", "ttys002" }, StringComparer.Ordinal));
            this._bridge = new BridgeManager(this._platform) { Agent = new ClaudeCodeAdapter(), Grid = grid };
        }

        public void Dispose()
        {
            try { Directory.Delete(this._root, recursive: true); } catch { }
        }

        private Int32 SlotOf(String tty)
        {
            for (var slot = 1; slot <= SessionRegistry.SlotCount; slot++)
            {
                if (this._bridge.Grid.SlotSession(slot)?.SessionKey == tty) { return slot; }
            }
            throw new InvalidOperationException(tty + " has no slot");
        }

        private static void Await(ManualResetEventSlim done) =>
            Assert.True(done.Wait(10_000), "the selection did not complete");

        [Fact]
        public void A_press_returns_before_focus_completes_and_pins_only_after_it()
        {
            using var hold = new ManualResetEventSlim(false);
            using var done = new ManualResetEventSlim(false);
            this._platform.FocusRelease = hold;
            var slot = this.SlotOf("ttys002");

            Assert.True(this._bridge.BeginSelectSlot(slot, done.Set));

            // The action thread is back already: nothing has changed yet, and the key says so.
            Assert.Equal(slot, this._bridge.SelectingSlot);
            Assert.Null(this._bridge.PinnedTty);
            Assert.False(done.IsSet);

            hold.Set();
            Await(done);
            Assert.Equal("ttys002", this._bridge.PinnedTty);
            Assert.Equal(0, this._bridge.SelectingSlot);
            Assert.Equal(new[] { "ttys002" }, this._platform.Focused);
        }

        [Fact]
        public void A_second_press_during_a_selection_is_dropped_not_queued()
        {
            using var hold = new ManualResetEventSlim(false);
            using var done = new ManualResetEventSlim(false);
            this._platform.FocusRelease = hold;
            var first = this.SlotOf("ttys001");
            var second = this.SlotOf("ttys002");

            Assert.True(this._bridge.BeginSelectSlot(first, done.Set));
            Assert.False(this._bridge.BeginSelectSlot(second, () => throw new InvalidOperationException("a dropped press must not complete")));
            Assert.Equal(first, this._bridge.SelectingSlot);

            hold.Set();
            Await(done);
            Assert.Equal("ttys001", this._bridge.PinnedTty);
            Assert.Equal(new[] { "ttys001" }, this._platform.Focused);

            // Once it has landed, the next press is an ordinary one.
            using var again = new ManualResetEventSlim(false);
            Assert.True(this._bridge.BeginSelectSlot(second, again.Set));
            Await(again);
            Assert.Equal("ttys002", this._bridge.PinnedTty);
        }

        [Fact]
        public void A_failed_focus_leaves_the_selection_unchanged_and_still_completes()
        {
            using var done = new ManualResetEventSlim(false);
            this._platform.FocusSucceeds = false;
            var slot = this.SlotOf("ttys002");

            Assert.True(this._bridge.BeginSelectSlot(slot, done.Set));
            Await(done);
            Assert.Null(this._bridge.PinnedTty);
            Assert.Equal(0, this._bridge.SelectingSlot);
        }

        [Fact]
        public void Pressing_the_pinned_slot_again_releases_it_off_the_action_thread()
        {
            var slot = this.SlotOf("ttys002");
            this._bridge.SelectSlot(slot);
            Assert.Equal("ttys002", this._bridge.PinnedTty);

            using var done = new ManualResetEventSlim(false);
            Assert.True(this._bridge.BeginSelectSlot(slot, done.Set));
            Await(done);
            Assert.Null(this._bridge.PinnedTty);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Routed_input_and_navigation_are_inert_until_focus_finishes(Boolean focusSucceeds)
        {
            this._bridge.SelectSlot(this.SlotOf("ttys001"));
            using var hold = new ManualResetEventSlim(false);
            using var done = new ManualResetEventSlim(false);
            this._platform.FocusRelease = hold;
            this._platform.FocusSucceeds = focusSucceeds;
            Assert.True(this._bridge.BeginSelectSlot(this.SlotOf("ttys002"), done.Set));
            try
            {
                this._bridge.InjectText("draft", false);
                this._bridge.SendPrompt("/clear");
                Assert.Equal(InjectionOutcome.Skipped, this._bridge.InjectKey(KeyStroke.Return));
                Assert.Equal(InjectionOutcome.Skipped, this._bridge.InjectKeyTo("ttys001", KeyStroke.Escape));
                Assert.Equal(InjectionOutcome.Skipped, this._bridge.InjectApprovalTo("ttys001", KeyStroke.Return));
                this._bridge.InjectTabThenEnter();
                this._bridge.DeliverDictation("dictation", false);
                this._bridge.Navigate(TerminalAction.NewClaudeTab);
                this._bridge.LaunchClaudeInProject(this._root);
                this._bridge.LaunchAgentSession("-i", "image.png");
                Assert.Empty(this._platform.Texts);
                Assert.Empty(this._platform.Keys);
                Assert.Empty(this._platform.TabEnters);
                Assert.Empty(this._platform.Navigations);
                Assert.Empty(this._platform.Launches);
                Assert.Empty(this._platform.AgentLaunches);
            }
            finally
            {
                hold.Set();
                Await(done);
            }
            this._bridge.InjectText("after focus", true);
            Assert.Equal((focusSucceeds ? "ttys002" : "ttys001", "after focus", true), Assert.Single(this._platform.Texts));
        }

        [Fact]
        public void Selection_does_not_start_inside_an_admitted_input_action()
        {
            this._platform.DuringTextInjection = () =>
                Assert.False(this._bridge.BeginSelectSlot(this.SlotOf("ttys002"), null));
            this._bridge.InjectText("already sending", true);
            Assert.Single(this._platform.Texts);
            Assert.Empty(this._platform.Focused);
            Assert.Equal(0, this._bridge.SelectingSlot);
        }

        [Fact]
        public void An_empty_slot_starts_nothing()
        {
            Assert.False(this._bridge.BeginSelectSlot(SessionRegistry.SlotCount, () => throw new InvalidOperationException("nothing to complete")));
            Assert.Equal(0, this._bridge.SelectingSlot);
            Assert.Empty(this._platform.Focused);
        }

        [Fact]
        public void A_session_that_exits_during_focus_does_not_replace_the_previous_pin()
        {
            this._bridge.SelectSlot(this.SlotOf("ttys001"));
            using var entered = new ManualResetEventSlim(false);
            using var hold = new ManualResetEventSlim(false);
            using var done = new ManualResetEventSlim(false);
            this._platform.FocusEntered = entered;
            this._platform.FocusRelease = hold;
            Assert.True(this._bridge.BeginSelectSlot(this.SlotOf("ttys002"), done.Set));
            try
            {
                Assert.True(entered.Wait(10000));
                this._bridge.Grid.Refresh(new HashSet<String> { "ttys001" });
            }
            finally
            {
                hold.Set();
                Await(done);
            }
            Assert.Equal("ttys001", this._bridge.PinnedTty);
            Assert.Equal(0, this._bridge.SelectingSlot);
        }

        [Fact]
        public void An_input_exception_does_not_leave_future_selection_blocked()
        {
            this._platform.DuringTextInjection = () => throw new InvalidOperationException("test failure");
            Assert.Throws<InvalidOperationException>(() => this._bridge.InjectText("failed", true));
            using var done = new ManualResetEventSlim(false);
            Assert.True(this._bridge.BeginSelectSlot(this.SlotOf("ttys002"), done.Set));
            Await(done);
            Assert.Equal("ttys002", this._bridge.PinnedTty);
        }
    }
}
