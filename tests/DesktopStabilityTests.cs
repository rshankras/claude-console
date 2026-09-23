namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;

    public class DesktopStabilityTests
    {
        private sealed class Automation : IDesktopAutomation
        {
            internal Func<DesktopSnapshot> Read = () => new() { SurfaceAvailable = true, Mode = "ChatGPT" };
            internal Func<String, Boolean> Write = _ => true;
            internal Func<DesktopSearchSnapshot> ReadSearch = () => DesktopSearchTests.Ready();
            internal Boolean? Front = true;
            internal Int32 Reads;
            public DesktopSnapshot Status() { Interlocked.Increment(ref Reads); return Read(); }
            public Boolean? IsAppFrontmost() => Front;
            public Boolean Press(String[] labels, out String matched) { matched = "New chat"; return Write(""); }
            public Boolean PressGuarded(String[] labels, String card, out String matched, out String error)
            { matched = null; error = null; return true; }
            public Boolean WriteComposer(String text, Boolean send, out String error) { error = null; return Write(text); }
            public Boolean SwitchMode(String mode) => true;
            public Boolean FocusApp() => true;
            public String PrepareDraft(String mode, Boolean empty, out String error) { error = null; Write(""); return "target"; }
            public DesktopSearchSnapshot Search(String action, String target = null, String query = null,
                String value = null, String title = null, String origin = null) => ReadSearch();
        }

        [Fact]
        public void SDK_command_entry_dispatches_without_native_work_or_a_duplicate_backlog()
        {
            var app = new Automation();
            DesktopServices.Declare(new OpenAiDesktopAdapter(), app, new DesktopMonitor(app));
            var queue = new Queue<Action>(); DesktopServices.Actions.Schedule = queue.Enqueue;
            try
            {
                var command = new DesktopControlCommand();
                var run = typeof(DesktopControlCommand).GetMethod("RunCommand",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                for (var i = 0; i < 1000; i++) run.Invoke(command, new Object[] { "new_chat" });
                Assert.Single(queue); Assert.Equal(0, app.Reads);
                DesktopServices.Actions.Stop(); queue.Dequeue()();
                Assert.False(DesktopServices.Actions.IsBusy); Assert.Equal(0, app.Reads);
            }
            finally { DesktopServices.Lifetime.Dispose(); DesktopServices.SearchVoice.Dispose(); }
        }

        [Fact]
        public async Task Listening_animation_never_overlaps_even_when_a_frame_stalls_across_restart()
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0; var active = 0; var peak = 0;
            using var face = new ListeningFace(() =>
            {
                var number = Interlocked.Increment(ref calls);
                peak = Math.Max(peak, Interlocked.Increment(ref active));
                if (number == 1) { entered.Set(); release.Wait(); }
                Interlocked.Decrement(ref active);
                if (number > 1) next.TrySetResult();
            });
            face.Start();
            try
            {
                Assert.True(await Task.Run(() => entered.Wait(3000)));
                face.Stop(); face.Start(); await Task.Delay(400);
                Assert.Equal(1, calls); Assert.Equal(1, peak);
            }
            finally { release.Set(); }
            await next.Task.WaitAsync(TimeSpan.FromSeconds(3)); face.Stop();
            var stopped = calls; await Task.Delay(350);
            Assert.Equal(stopped, calls); Assert.Equal(1, peak); Assert.False(face.IsActive);
            face.SetEnabled(false); face.Start(); Assert.False(face.IsActive);
            face.SetEnabled(true); face.Start(); Assert.True(face.IsActive); face.Stop();
        }

        [Theory]
        [InlineData(false)][InlineData(true)]
        public void Closing_search_can_only_stop_its_capture_once(Boolean starting)
        {
            var state = new VoiceCaptureState(); var now = DateTime.UtcNow;
            Assert.Equal(VoiceAction.Refuse, state.StopIfCapturing(VoiceIntent.DesktopSearch, now));
            state.Press(VoiceIntent.DesktopDraft, now);
            Assert.Equal(VoiceAction.Refuse, state.StopIfCapturing(VoiceIntent.DesktopSearch, now));
            Assert.Equal(VoicePhase.Recording, state.Phase); state.Finish();
            state.Press(VoiceIntent.DesktopSearch, now, starting);
            Assert.Equal(starting ? VoiceAction.Cancel : VoiceAction.Stop,
                state.StopIfCapturing(VoiceIntent.DesktopSearch, now));
            for (var i = 0; i < 1000; i++)
                Assert.Equal(VoiceAction.Refuse, state.StopIfCapturing(VoiceIntent.DesktopSearch, now));
            state.Finish();
            Assert.Equal(VoiceAction.Refuse, state.StopIfCapturing(VoiceIntent.DesktopSearch, now));
            Assert.Equal(VoicePhase.Idle, state.Phase);
        }

        [Fact]
        public async Task Command_returns_before_slow_work_and_thousands_of_extra_taps_cannot_queue()
        {
            var runner = new DesktopActionRunner();
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            Assert.True(runner.TryRun(() => { Interlocked.Increment(ref calls); entered.Set(); release.Wait(); }, () => finished.SetResult()));
            try
            {
                Assert.True(await Task.Run(() => entered.Wait(3000)));
                for (var i = 0; i < 10000; i++) Assert.False(runner.TryRun(() => calls++));
                Assert.True(runner.IsBusy); Assert.Equal(1, calls);
            }
            finally { release.Set(); }
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(runner.IsBusy); Assert.Equal(1, calls);
        }

        [Fact]
        public void Unload_cancels_not_started_work_and_reload_does_not_revive_it()
        {
            var queue = new Queue<Action>(); var runner = new DesktopActionRunner { Schedule = queue.Enqueue };
            var calls = 0;
            Assert.True(runner.TryRun(() => calls++)); runner.Stop(); runner.Start();
            Assert.False(runner.TryRun(() => calls++)); // old invocation still owns the slot
            queue.Dequeue()(); Assert.Equal(0, calls);
            Assert.True(runner.TryRun(() => calls++)); queue.Dequeue()(); Assert.Equal(1, calls);
        }

        private sealed class Publisher
        {
            internal event Action Tick;
            internal Int32 Count => Tick?.GetInvocationList().Length ?? 0;
            internal void Raise() => Tick?.Invoke();
        }
        private sealed class Listener { internal Int32 Count; internal void Tick() => Count++; }

        [Fact]
        public void Five_hundred_reload_cycles_do_not_multiply_subscriptions()
        {
            var publisher = new Publisher(); var listener = new Listener(); using var lifetime = new DesktopLifetime();
            lifetime.Bind(() => publisher.Tick += listener.Tick, () => publisher.Tick -= listener.Tick);
            for (var i = 0; i < 500; i++)
            {
                Assert.Equal(1, publisher.Count); publisher.Raise();
                lifetime.Stop(); lifetime.Stop(); Assert.Equal(0, publisher.Count);
                publisher.Raise(); lifetime.Start(); lifetime.Start();
            }
            Assert.Equal(500, listener.Count);
            lifetime.Dispose(); lifetime.Start(); Assert.Equal(0, publisher.Count);
        }

        [Fact]
        public void Reload_never_revives_a_transcript_from_the_previous_load()
        {
            using var lifetime = new DesktopLifetime(); var oldCapture = lifetime.CaptureGuard();
            Assert.True(oldCapture()); lifetime.Stop(); Assert.False(oldCapture());
            lifetime.Start(); Assert.False(oldCapture()); Assert.True(lifetime.CaptureGuard()());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference ReleasedListener(Publisher publisher)
        {
            var listener = new Listener(); var weak = new WeakReference(listener);
            using var lifetime = new DesktopLifetime();
            lifetime.Bind(() => publisher.Tick += listener.Tick, () => publisher.Tick -= listener.Tick);
            return weak;
        }

        [Fact]
        public void Disposed_scope_releases_objects_even_while_the_publisher_lives()
        {
            var publisher = new Publisher(); var references = new List<WeakReference>();
            for (var i = 0; i < 1000; i++) references.Add(ReleasedListener(publisher));
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.All(references, reference => Assert.False(reference.IsAlive));
            Assert.Equal(0, publisher.Count); GC.KeepAlive(publisher);
        }

        [Fact]
        public void Background_monitor_scans_at_most_every_fifteen_seconds_and_commands_take_priority()
        {
            Int64 now = 100; var busy = false; var app = new Automation { Front = false };
            using var monitor = new DesktopMonitor(app) { Clock = () => now, IsCommandBusy = () => busy };
            monitor.PollOnce(); Assert.Equal(1, app.Reads);
            for (var i = 0; i < 14; i++) { now += 1000; monitor.PollOnce(); }
            Assert.Equal(1, app.Reads);
            now += 1000; monitor.PollOnce(); Assert.Equal(2, app.Reads);
            app.Front = true; monitor.PollOnce(); Assert.Equal(3, app.Reads);
            busy = true; monitor.PollOnce(); Assert.Equal(3, app.Reads);
        }

        [Fact]
        public void Slow_scans_back_off_and_a_fast_scan_restores_normal_updates()
        {
            Int64 now = 100; var slow = true;
            var app = new Automation { Read = () => { now += slow ? 1500 : 10; return new() { SurfaceAvailable = true, StopPresent = true }; } };
            using var monitor = new DesktopMonitor(app) { Clock = () => now };
            monitor.PollOnce(); Assert.Equal(3000, monitor.NextPollDelayMs);
            monitor.PollOnce(); Assert.Equal(6000, monitor.NextPollDelayMs);
            monitor.PollOnce(); Assert.Equal(12000, monitor.NextPollDelayMs);
            slow = false; monitor.PollOnce(); Assert.Equal(1000, monitor.NextPollDelayMs);
        }

        [Fact]
        public async Task Old_monitor_callback_cannot_overlap_or_publish_after_Stop_Start()
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var active = 0; var peak = 0; var calls = 0;
            var app = new Automation { Read = () =>
            {
                var count = Interlocked.Increment(ref calls); var current = Interlocked.Increment(ref active);
                peak = Math.Max(peak, current);
                if (count == 1) { entered.Set(); release.Wait(); }
                Interlocked.Decrement(ref active);
                return new() { SurfaceAvailable = true, Mode = count == 1 ? "Old" : "New" };
            } };
            using var monitor = new DesktopMonitor(app);
            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var modes = new List<String>(); monitor.OnChanged += state => { modes.Add(state.Mode); if (state.Mode == "New") seen.TrySetResult(); };
            monitor.Start();
            try
            {
                Assert.True(await Task.Run(() => entered.Wait(3000)));
                monitor.Stop(); monitor.Start(); await Task.Delay(100); Assert.Equal(1, calls);
            }
            finally { release.Set(); }
            await seen.Task.WaitAsync(TimeSpan.FromSeconds(3)); monitor.Stop();
            Assert.Equal(1, peak); Assert.DoesNotContain("Old", modes);
        }

        [Fact]
        public async Task Draft_retry_keeps_render_reads_fast_and_preserves_a_newer_transcript()
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var app = new Automation { Write = _ => { entered.Set(); release.Wait(); return true; } };
            var recovery = new DesktopDraftRecovery(app); recovery.Retain("First");
            var operation = Task.Run(() => recovery.Insert());
            try
            {
                Assert.True(await Task.Run(() => entered.Wait(3000)));
                await Task.Run(() =>
                {
                    Assert.True(recovery.Pending); Assert.Null(recovery.DiscardableId(VoicePhase.Idle));
                    Assert.Equal("Please Wait", recovery.Insert()); recovery.Retain("Newer");
                }).WaitAsync(TimeSpan.FromSeconds(1));
            }
            finally { release.Set(); }
            Assert.Equal("Draft Ready", await operation); Assert.True(recovery.Pending);
            recovery.Insert(text => { Assert.Equal("Newer", text); return (true, null); });
        }

        [Fact]
        public async Task Search_rendering_and_close_do_not_wait_for_a_native_read()
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var app = new Automation(); var search = new DesktopSearch(app); search.Begin();
            app.ReadSearch = () => { entered.Set(); release.Wait(); return DesktopSearchTests.Ready(); };
            var read = Task.Run(search.Refresh);
            try
            {
                Assert.True(await Task.Run(() => entered.Wait(3000)));
                await Task.Run(() =>
                {
                    search.ResultParameter(new("Result", "1")); search.End(); Assert.False(search.Current.Available);
                }).WaitAsync(TimeSpan.FromSeconds(1));
            }
            finally { release.Set(); }
            await read; Assert.False(search.Current.Available);
        }

        [Fact]
        public void Mac_foreground_probe_does_not_request_a_UI_scan_and_unload_blocks_new_helpers()
        {
            var calls = new List<List<String>>(); var app = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            { Runner = (args, _) => { calls.Add(args); return "{\"ok\":true,\"frontmost\":false}"; } };
            Assert.False(app.IsAppFrontmost()); Assert.Equal("frontmost", Assert.Single(calls)[0]);
            app.IsEnabled = () => false; app.Status(); app.FocusApp(); app.IsAppFrontmost();
            Assert.Single(calls);
        }
    }
}
