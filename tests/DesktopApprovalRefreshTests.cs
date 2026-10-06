namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;

    public class DesktopApprovalRefreshTests
    {
        private static readonly OpenAiDesktopAdapter App = new();
        private static DesktopSnapshot Pending(Boolean high = false) => new()
        {
            SurfaceAvailable = true, ApprovalPresent = true, DenyPresent = true, Mode = "Codex",
            CardText = high ? "rm -rf build" : "Run the unit tests",
            Conversations = new[] { new DesktopConversation { Title = "Task", Selected = true } },
        };
        private static DesktopSnapshot Ready() => new() { SurfaceAvailable = true, Mode = "Codex" };

        private sealed class Automation : IDesktopAutomation
        {
            internal Func<DesktopSnapshot> Read = Ready;
            internal Action OnPress;
            internal Boolean Front = true, Succeeds = true;
            internal Int32 Reads;
            internal readonly List<(String[] Labels, String Card)> Presses = new();
            public DesktopSnapshot Status() { Interlocked.Increment(ref Reads); return Read(); }
            public Boolean? IsAppFrontmost() => Front;
            public Boolean PressGuarded(String[] labels, String card, out String matched, out String error)
            {
                Presses.Add((labels, card)); OnPress?.Invoke();
                matched = Succeeds ? labels[0] : null; error = Succeeds ? null : "card-changed";
                return Succeeds;
            }
            public Boolean Press(String[] labels, out String matched) => throw new InvalidOperationException("unguarded press");
            public Boolean WriteComposer(String text, Boolean send, out String error) => throw new InvalidOperationException();
            public Boolean SwitchMode(String mode) => throw new InvalidOperationException();
            public Boolean FocusApp() => throw new InvalidOperationException("must not focus the app");
        }

        [Theory]
        [InlineData("approve", false)][InlineData("deny", false)]
        [InlineData("approve", true)][InlineData("deny", true)]
        public void Stale_idle_key_checks_once_and_requires_a_fresh_tap_even_for_routine_requests(String action, Boolean high)
        {
            var auto = new Automation { Read = () => Pending(high) };
            using var monitor = new DesktopMonitor(auto);
            var confirmation = new DesktopApprovalConfirmation();
            monitor.OnChanged += confirmation.Observe;
            var now = DateTime.UtcNow;
            var checking = false;
            auto.Read = () => { Assert.True(checking); return Pending(high); };
            var result = DesktopApprovalCommand.Execute(action, DesktopMonitor.Map(Ready()), App, auto,
                confirmation, now, () => { }, monitor, () => checking = true);
            Assert.Equal("Press again", result);
            Assert.Equal(1, auto.Reads); Assert.Empty(auto.Presses);
            Assert.Equal(DesktopActivity.WaitingApproval, monitor.Current.Activity);
            Assert.Equal("Task", monitor.Current.ActiveTitle);
            Assert.Equal("Press again", DesktopApprovalCommand.LabelFor(action, monitor.Current, confirmation, now.AddSeconds(1)));

            result = DesktopApprovalCommand.Execute(action, monitor.Current, App, auto,
                confirmation, now.AddSeconds(1), () => { }, monitor);
            Assert.Equal(action == "approve" ? "Approved" : "Denied", result);
            Assert.Equal(1, auto.Reads); // The guarded press itself revalidates; no duplicate preflight.
            var press = Assert.Single(auto.Presses);
            Assert.Equal(Pending(high).CardText, press.Card);
            Assert.Equal(action == "approve" ? App.ApproveLabels : App.DenyLabels, press.Labels);
            Assert.False(confirmation.IsArmed(action, monitor.Current, now.AddSeconds(1)));
        }

        [Theory]
        [InlineData("approve", false)][InlineData("deny", false)]
        [InlineData("approve", true)][InlineData("deny", true)]
        public void A_fresh_empty_or_unavailable_read_reports_a_reason_without_pressing(String action, Boolean unavailable)
        {
            var auto = new Automation { Read = () => unavailable ? DesktopSnapshot.Unavailable : Ready() };
            using var monitor = new DesktopMonitor(auto);
            var result = DesktopApprovalCommand.Execute(action, null, App, auto,
                new DesktopApprovalConfirmation(), DateTime.UtcNow, () => { }, monitor);
            Assert.Equal(unavailable ? "Check app" : "No request", result);
            Assert.Equal(1, auto.Reads); Assert.Empty(auto.Presses);
        }

        [Fact]
        public void Unreadable_card_and_failed_refresh_cannot_become_an_approval()
        {
            var auto = new Automation { Read = () => new() { SurfaceAvailable = true, ApprovalPresent = true } };
            using var monitor = new DesktopMonitor(auto);
            var confirmation = new DesktopApprovalConfirmation();
            String Check() => DesktopApprovalCommand.Execute("approve", null, App, auto,
                confirmation, DateTime.UtcNow, () => { }, monitor);
            Assert.Equal("Check app", Check());
            auto.Read = () => throw new InvalidOperationException("status failed");
            Assert.Equal("Check app", Check());
            Assert.Equal(2, auto.Reads); Assert.Empty(auto.Presses);
            Assert.Equal(DesktopActivity.Unavailable, monitor.Current.Activity);
        }

        [Theory]
        [InlineData("approve")][InlineData("deny")]
        public void A_changed_card_is_guarded_by_the_shown_text_and_never_retried(String action)
        {
            var auto = new Automation { Succeeds = false };
            using var monitor = new DesktopMonitor(auto);
            var shown = DesktopMonitor.Map(Pending());
            var result = DesktopApprovalCommand.Execute(action, shown, App, auto,
                new DesktopApprovalConfirmation(), DateTime.UtcNow, () => { }, monitor);
            Assert.Equal("Check app", result);
            Assert.Equal(shown.CardText, Assert.Single(auto.Presses).Card);
            Assert.Equal(0, auto.Reads);
            monitor.PollOnce();
            Assert.Equal(1, auto.Reads); Assert.Single(auto.Presses);
        }

        [Theory]
        [InlineData(false)][InlineData(true)]
        public async Task An_old_poll_cannot_replace_the_fresh_request_or_cancel_its_confirmation(Boolean throws)
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var count = 0;
            var auto = new Automation { Read = () =>
            {
                if (Interlocked.Increment(ref count) != 1) return Pending(true);
                entered.Set(); release.Wait();
                if (throws) throw new InvalidOperationException("old failed read");
                return Ready();
            } };
            using var monitor = new DesktopMonitor(auto);
            var confirmation = new DesktopApprovalConfirmation();
            monitor.OnChanged += confirmation.Observe;
            var oldPoll = Task.Run(monitor.PollOnce);
            var now = DateTime.UtcNow;
            try
            {
                Assert.True(await Task.Run(() => entered.Wait(3000)));
                Assert.Equal("Press again", DesktopApprovalCommand.Execute("approve", null, App, auto,
                    confirmation, now, () => { }, monitor));
            }
            finally { release.Set(); await oldPoll.WaitAsync(TimeSpan.FromSeconds(3)); }
            Assert.Equal(DesktopActivity.WaitingApproval, monitor.Current.Activity);
            Assert.True(confirmation.IsArmed("approve", monitor.Current, now.AddSeconds(1)));
            Assert.Empty(auto.Presses);
        }

        [Fact]
        public async Task A_command_read_finishing_after_stop_is_not_published()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var auto = new Automation { Read = () => { entered.Set(); release.Wait(); return Pending(); } };
            using var monitor = new DesktopMonitor(auto);
            var notifications = 0; monitor.OnChanged += _ => notifications++;
            var read = Task.Run(monitor.RefreshForCommand);
            try
            {
                Assert.True(await Task.Run(() => entered.Wait(3000)));
                monitor.Stop();
            }
            finally { release.Set(); }
            Assert.Equal(DesktopActivity.Unavailable, (await read.WaitAsync(TimeSpan.FromSeconds(3))).Activity);
            Assert.Equal(0, notifications); Assert.Empty(auto.Presses);
        }

        [Fact]
        public void Successful_press_requests_a_coalesced_refresh_even_in_background_and_still_yields_to_actions()
        {
            var auto = new Automation { Front = false, Read = () => Pending() };
            var busy = false;
            using var monitor = new DesktopMonitor(auto) { Clock = () => 100, IsCommandBusy = () => busy };
            monitor.PollOnce();
            Assert.Equal(1, auto.Reads);
            auto.OnPress = () => { auto.Read = Ready; busy = true; };
            Assert.Equal("Approved", DesktopApprovalCommand.Execute("approve", monitor.Current, App, auto,
                new DesktopApprovalConfirmation(), DateTime.UtcNow, () => { }, monitor));
            for (var i = 0; i < 10; i++) monitor.RequestRefresh();
            monitor.PollOnce(); Assert.Equal(1, auto.Reads); // Even an explicit refresh cannot scan over an action.
            busy = false;
            monitor.PollOnce(); Assert.Equal(2, auto.Reads);
            Assert.Equal(DesktopActivity.Ready, monitor.Current.Activity);
            monitor.PollOnce(); Assert.Equal(2, auto.Reads); // Passive 15s throttle resumes.
        }

        [Fact]
        public void Slow_active_polling_is_capped_while_idle_backoff_is_preserved()
        {
            for (var slow = 1; slow <= 5; slow++)
            {
                Assert.Equal(2000, DesktopMonitor.PollDelay(DesktopActivity.Working, false, slow));
                Assert.Equal(2000, DesktopMonitor.PollDelay(DesktopActivity.WaitingApproval, false, slow));
            }
            Assert.Equal(1000, DesktopMonitor.PollDelay(DesktopActivity.Working, false, 0));
            Assert.Equal(24000, DesktopMonitor.PollDelay(DesktopActivity.Ready, false, 4));
        }

        [Fact]
        public async Task Refresh_during_a_timer_read_runs_after_it_without_overlapping_or_publishing_the_old_result()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0; var active = 0; var peak = 0;
            var auto = new Automation { Read = () =>
            {
                var number = Interlocked.Increment(ref reads);
                peak = Math.Max(peak, Interlocked.Increment(ref active));
                try
                {
                    if (number == 1) { entered.Set(); release.Wait(); return Ready(); }
                    return Pending();
                }
                finally { Interlocked.Decrement(ref active); }
            } };
            using var monitor = new DesktopMonitor(auto);
            var states = new List<DesktopActivity>();
            monitor.OnChanged += state => { states.Add(state.Activity); seen.TrySetResult(); };
            monitor.Start();
            try
            {
                Assert.True(await Task.Run(() => entered.Wait(3000)));
                for (var i = 0; i < 10; i++) { monitor.RequestRefresh(); monitor.PollOnce(); }
                Assert.Equal(1, reads);
                release.Set();
                await seen.Task.WaitAsync(TimeSpan.FromSeconds(3));
                monitor.Stop();
                Assert.Equal(2, reads); Assert.Equal(1, peak);
                Assert.Equal(DesktopActivity.WaitingApproval, Assert.Single(states));
            }
            finally { release.Set(); monitor.Stop(); }
        }

        [Fact]
        public void SDK_stale_key_refresh_uses_the_action_lane_and_rapid_presses_never_queue()
        {
            using var home = new TempHome();
            var auto = new Automation();
            var monitor = new DesktopMonitor(auto);
            DesktopServices.Declare(App, auto, monitor);
            auto.Read = () => Pending(); monitor.PollOnce(); // Published, but this key has not painted it.
            var queued = new Queue<Action>(); DesktopServices.Actions.Schedule = queued.Enqueue;
            var command = new DesktopApprovalCommand();
            var press = typeof(DesktopApprovalCommand).GetMethod("RunCommand", BindingFlags.NonPublic | BindingFlags.Instance);
            void Tap() => press.Invoke(command, new Object[] { "approve" });
            auto.Read = () =>
            {
                Assert.True(DesktopActionRunner.IsExecuting);
                for (var i = 0; i < 5; i++) Tap();
                Assert.True(command.ShowsBusy("approve"));
                return Pending();
            };
            try
            {
                Tap(); Assert.Equal(1, auto.Reads); Assert.Single(queued);
                queued.Dequeue()();
                Assert.Equal(2, auto.Reads); Assert.Empty(auto.Presses); Assert.Empty(queued);
                Assert.True(command.ShowsBusy("approve"));
            }
            finally { DesktopServices.Actions.Stop(); DesktopServices.Lifetime.Dispose(); monitor.Dispose(); }
        }
    }
}
