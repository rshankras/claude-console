namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    using Loupedeck.ClaudeConsolePlugin.Agents;

    using Xunit;

    /// <summary>
    /// #125. The installer restores the package's ZIP timestamps, and ZIP times carry no zone: a
    /// helper packed at 11:34 IST is dated 11:34 LOCAL wherever it lands, hours in the future on a
    /// machine behind IST. The health monitor used that mtime as the freshness floor, so every
    /// receipt was stale until the clock caught up — Yes/No refused quietly, values withheld, and
    /// Vizhi reading Run /hooks. The floor must latch to min(mtime, first observation) per file.
    /// </summary>
    public sealed class HelperVersionLatchTests : IDisposable
    {
        private const String SessionA = "pid-101-638900000000000000";
        private readonly String _directory = Path.Combine(Path.GetTempPath(), "cc-version-latch-" + Guid.NewGuid().ToString("N"));
        // A morning install in CEST from a package packed at 11:34 IST: the file reads ~3 h ahead.
        private DateTime _now = new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);
        private readonly DateTime _installedAt;
        private readonly String _helper;
        private readonly String _ipc;

        public HelperVersionLatchTests()
        {
            this._installedAt = this._now;
            this._helper = Path.Combine(this._directory, "package", "claude-console-hook.exe");
            this._ipc = Path.Combine(this._directory, "claude-console");
            Directory.CreateDirectory(Path.GetDirectoryName(this._helper));
            File.WriteAllText(this._helper, "test helper");
            File.SetLastWriteTimeUtc(this._helper, this._now.AddHours(3));
        }

        public void Dispose()
        {
            try { Directory.Delete(this._directory, recursive: true); } catch { }
        }

        private WindowsHookHealth Monitor(String ipc = null, String product = "claude-console") =>
            new WindowsHookHealth(this._helper, ipc ?? this._ipc, product, () => this._now);

        private static void Success(WindowsHookHealth monitor, String key, DateTime started)
        {
            Directory.CreateDirectory(monitor.HealthDirectory);
            File.WriteAllText(Path.Combine(monitor.HealthDirectory, "success-" + key + ".json"), JsonSerializer.Serialize(new
            {
                schema = 1,
                sessionKey = key,
                startedUtcTicks = started.Ticks,
                completedUtcTicks = started.AddTicks(1).Ticks,
                @event = "PermissionRequest",
            }));
        }

        [Fact]
        public void A_helper_dated_in_the_future_accepts_a_receipt_written_now()
        {
            var monitor = this.Monitor();
            Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, monitor.Refresh());   // plugin load: first sighting
            this._now = this._now.AddMinutes(1);
            Success(monitor, SessionA, this._now);

            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
            Assert.True(monitor.IsSessionObservationCurrent(SessionA));
            Assert.True(monitor.IsObservationStartedCurrent(this._now.Ticks));
            // The floor is where the file was first seen, never the file's own future time.
            Assert.Equal(this._installedAt, monitor.FreshAfterUtc);
        }

        [Fact]
        public void The_latched_floor_survives_a_restart_and_the_clock_passing_the_file_time()
        {
            var first = this.Monitor();
            first.Refresh();                                   // first sighting: latch at 09:00
            this._now = this._now.AddMinutes(1);
            Success(first, SessionA, this._now);
            Assert.Equal(WindowsHookHealthStatus.Healthy, first.Refresh());

            this._now = this._now.AddHours(4);                // 13:01 — the clock has passed 12:00
            var restarted = this.Monitor();
            Assert.Equal(WindowsHookHealthStatus.Healthy, restarted.Refresh());
            Assert.True(restarted.IsSessionObservationCurrent(SessionA));
            Assert.Equal(this._installedAt, restarted.FreshAfterUtc);
        }

        [Fact]
        public void A_real_replacement_after_a_future_dated_file_still_demands_fresh_evidence()
        {
            var monitor = this.Monitor();
            this._now = this._now.AddMinutes(1);
            Success(monitor, SessionA, this._now);
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());

            // An upgrade lands, itself future-dated. The old receipt belongs to the old file.
            this._now = this._now.AddMinutes(10);
            File.WriteAllText(this._helper, "upgraded helper");
            File.SetLastWriteTimeUtc(this._helper, this._now.AddHours(5));
            Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, monitor.Refresh());
            Assert.False(monitor.IsSessionObservationCurrent(SessionA));
            Assert.Equal(this._now, monitor.FreshAfterUtc);

            Success(monitor, SessionA, this._now.AddSeconds(1));
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Restoring_a_future_dated_helper_first_seen_missing_requires_a_successful_hook(Boolean restartBeforeRestore)
        {
            var mtime = File.GetLastWriteTimeUtc(this._helper);
            File.Delete(this._helper);
            var monitor = this.Monitor();
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            var failedAt = monitor.InvalidatedAtUtc;
            this._now = this._now.AddMinutes(1);
            if (restartBeforeRestore) { monitor = this.Monitor(); }
            File.WriteAllText(this._helper, "restored helper");
            File.SetLastWriteTimeUtc(this._helper, mtime);
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            Assert.Equal(failedAt, monitor.InvalidatedAtUtc);

            // The synthetic version floor must not become evidence of an upgrade on restart,
            // even after the original future mtime has become a past time.
            this._now = this._now.AddHours(4);
            monitor = this.Monitor();
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            Assert.False(monitor.IsSessionObservationCurrent(SessionA));
            Success(monitor, SessionA, this._now);
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
            Assert.True(monitor.IsSessionObservationCurrent(SessionA));
        }

        [Fact]
        public void A_future_dated_helper_with_a_launch_failure_before_first_sighting_stays_blocked()
        {
            var monitor = this.Monitor();
            Directory.CreateDirectory(monitor.HealthDirectory);
            File.WriteAllText(Path.Combine(monitor.HealthDirectory, "failure-launch.json"), JsonSerializer.Serialize(new
            {
                schema = 1, observedUtcTicks = this._now.AddSeconds(-1).Ticks,
                reason = "launch-failed", scope = "helper",
            }));
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            this._now = this._now.AddSeconds(1);
            Success(monitor, SessionA, this._now);
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
        }

        [Fact]
        public void Codex_status_is_active_when_the_helper_is_dated_in_the_future_but_hooks_are_firing()
        {
            var root = Path.Combine(this._directory, "codex-root");
            var sessions = Path.Combine(root, "sessions");
            Directory.CreateDirectory(sessions);
            var manager = new BridgeManager(new PlatformSeamTests.FakePlatformBridge())
            {
                Agent = new CodexCliAdapter(),
                HookHealth = this.Monitor(root, "codex-console"),
            };
            this._now = this._now.AddMinutes(1);
            Success(manager.HookHealth, SessionA, this._now);
            manager.RefreshHelperHealth();

            var codex = new CodexStateBridge(Path.Combine(root, "codex-home"), sessions) { HookExe = this._helper };
            codex.EnsureInstalled("#!/bin/sh\n");
            File.SetLastWriteTimeUtc(codex.HooksFile, this._now.AddMinutes(-2));
            var state = Path.Combine(sessions, SessionA + ".json");
            File.WriteAllText(state, "{\"event\":\"Stop\",\"payload\":{}}");
            File.SetLastWriteTimeUtc(state, this._now);

            Assert.Equal(CodexBridgeStatus.Active, codex.StatusFor(windows: true, manager.HookHealth));
        }

        [Fact]
        public void Codex_status_read_at_load_takes_the_latched_floor_before_polling_starts()
        {
            // VizhiCodexPlugin.Load() evaluates the bridge status BEFORE StartPolling(), which was
            // where the monitor was first built. Reviewing PR #127: that first read fell back to the
            // exe's raw future mtime, so every service start inside the window logged Run /hooks and
            // posted the trust card, then flipped to Active on the first poll. Load() now primes the
            // monitor first; this test reads the status exactly as Load() does, before any poll.
            var root = Path.Combine(this._directory, "codex-root");
            var sessions = Path.Combine(root, "sessions");
            Directory.CreateDirectory(sessions);

            // An earlier run saw the future-dated helper and persisted its first sighting.
            var earlier = this.Monitor(root, "codex-console");
            Directory.CreateDirectory(earlier.HealthDirectory);
            earlier.Refresh();
            var codex = new CodexStateBridge(Path.Combine(root, "codex-home"), sessions) { HookExe = this._helper };
            codex.EnsureInstalled("#!/bin/sh\n");
            File.SetLastWriteTimeUtc(codex.HooksFile, this._now);

            // Hooks fired after that sighting, before the service restarts.
            this._now = this._now.AddMinutes(30);
            var state = Path.Combine(sessions, SessionA + ".json");
            File.WriteAllText(state, "{\"event\":\"Stop\",\"payload\":{}}");
            File.SetLastWriteTimeUtc(state, this._now);

            // The restart: a fresh manager whose monitor has not refreshed yet, as at Load().
            this._now = this._now.AddMinutes(1);
            var manager = new BridgeManager(new PlatformSeamTests.FakePlatformBridge())
            {
                Agent = new CodexCliAdapter(),
                HookHealth = this.Monitor(root, "codex-console"),
            };
            // Without the prime the raw mtime (three hours ahead) is the floor and the envelope is
            // "older than the install": the defect the load order fix removes.
            Assert.Equal(CodexBridgeStatus.AwaitingTrust, codex.StatusFor(windows: true, manager.HookHealth));

            manager.PrimeHelperHealth();
            Assert.Equal(CodexBridgeStatus.Active, codex.StatusFor(windows: true, manager.HookHealth));
        }
    }
}
