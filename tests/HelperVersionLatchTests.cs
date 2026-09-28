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
    }
}
