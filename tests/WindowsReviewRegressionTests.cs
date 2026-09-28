namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Text.Json;
    using Xunit;

    /// <summary>
    /// Regressions found by the Windows laptop review of fix/windows-helper-health @ 3de7b20
    /// (#120). Self-contained: a dummy helper file, never executed.
    /// </summary>
    public sealed class WindowsReviewRegressionTests : IDisposable
    {
        private const String Session = "pid-101-638900000000000000";
        private readonly String _root = Path.Combine(Path.GetTempPath(), "review-retention-" + Guid.NewGuid().ToString("N"));
        private readonly String _helper;
        private readonly String _ipc;
        private readonly DateTime _now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

        public WindowsReviewRegressionTests()
        {
            Directory.CreateDirectory(this._root);
            this._helper = Path.Combine(this._root, "hook.exe");
            this._ipc = Path.Combine(this._root, "ipc");
            File.WriteAllText(this._helper, "fixture, never executed");
            File.SetLastWriteTimeUtc(this._helper, this._now.AddDays(-1));
        }

        public void Dispose()
        {
            try { Directory.Delete(this._root, recursive: true); } catch { }
        }

        private WindowsHookHealth Monitor() => new WindowsHookHealth(this._helper, this._ipc, "claude-console", () => this._now);

        private void Receipt(WindowsHookHealth monitor, Int64 started)
        {
            Directory.CreateDirectory(monitor.HealthDirectory);
            File.WriteAllText(Path.Combine(monitor.HealthDirectory, "success-" + Session + ".json"),
                JsonSerializer.Serialize(new { schema = 1, sessionKey = Session, startedUtcTicks = started, completedUtcTicks = started + 1, @event = "PermissionRequest" }));
        }

        // The shape the review's reproduction used: scope only inside the JSON (a pre-3de7b20
        // launcher) — and the shape the launcher writes now, with the scope in the name.
        private static void Failure(WindowsHookHealth monitor, DateTime time, String scope, String reason, Boolean scopeInName)
        {
            Directory.CreateDirectory(monitor.HealthDirectory);
            var name = scopeInName
                ? $"failure-{time.Ticks}-{scope}-{Guid.NewGuid():N}.json"
                : $"failure-{time.Ticks}-{Guid.NewGuid():N}.json";
            File.WriteAllText(Path.Combine(monitor.HealthDirectory, name),
                JsonSerializer.Serialize(new { schema = 1, observedUtcTicks = time.Ticks, scope, reason }));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Delivery_failure_pruning_must_preserve_helper_barrier_across_restart(Boolean scopeInName)
        {
            // Review P1: an old receipt and an approval observed before the helper failed; then a
            // helper failure; then sixteen delivery failures and no success. A restart must still
            // know about the helper failure, or the old approval passes the freshness check.
            var monitor = this.Monitor();
            var oldStart = this._now.AddMinutes(-2).Ticks;
            Receipt(monitor, oldStart);
            Failure(monitor, this._now.AddMinutes(-1), "helper", "launch-failed", scopeInName);
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            for (var i = 0; i < 16; i++)
            {
                Failure(monitor, this._now.AddSeconds(-30 + i), "delivery", "observation-failed", scopeInName);
            }
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());

            var restarted = this.Monitor();
            var restartedStatus = restarted.Refresh();
            Assert.False(restarted.IsSessionObservationCurrent(Session),
                $"Old session became current after restart: status={restartedStatus}, " +
                $"barrier={restarted.InvalidatedAtUtc:O}, old approval timestamp accepted=" +
                restarted.IsObservationStartedCurrent(oldStart));
            Assert.Equal(WindowsHookHealthStatus.Unavailable, restartedStatus);
            Assert.False(restarted.IsObservationStartedCurrent(oldStart));
            Assert.Equal(this._now.AddMinutes(-1), restarted.InvalidatedAtUtc);
        }

        [Fact]
        public void The_helper_failure_record_survives_a_flood_of_delivery_records()
        {
            // Trimming is per scope: sixteen delivery records must not evict the helper record.
            var monitor = this.Monitor();
            Failure(monitor, this._now.AddMinutes(-1), "helper", "launch-failed", scopeInName: true);
            for (var i = 0; i < 40; i++)
            {
                Failure(monitor, this._now.AddSeconds(-50 + i), "delivery", "observation-failed", scopeInName: true);
            }
            monitor.Refresh();
            Assert.Single(Directory.GetFiles(monitor.HealthDirectory, "failure-*-helper-*.json"));
            Assert.Equal(16, Directory.GetFiles(monitor.HealthDirectory, "failure-*-delivery-*.json").Length);
        }

        [Fact]
        public void The_barrier_outlives_every_failure_record()
        {
            // Even with all records gone (a sweep, a hand clean-up, a cap), last-failure.json holds.
            var monitor = this.Monitor();
            Receipt(monitor, this._now.AddMinutes(-2).Ticks);
            Failure(monitor, this._now.AddMinutes(-1), "helper", "launch-failed", scopeInName: true);
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            foreach (var record in Directory.GetFiles(monitor.HealthDirectory, "failure-*.json")) { File.Delete(record); }
            Assert.True(File.Exists(Path.Combine(monitor.HealthDirectory, "last-failure.json")));

            var restarted = this.Monitor();
            Assert.Equal(WindowsHookHealthStatus.Unavailable, restarted.Refresh());
            Assert.Equal(this._now.AddMinutes(-1), restarted.InvalidatedAtUtc);
            Assert.False(restarted.IsSessionObservationCurrent(Session));
        }

        [Fact]
        public void Sessions_keep_independent_approval_requirements_after_restart()
        {
            // B delivers after the failure; A does not. A restart must keep that distinction.
            const String other = "pid-202-638900000000000001";
            var monitor = this.Monitor();
            Receipt(monitor, this._now.AddMinutes(-2).Ticks);
            Failure(monitor, this._now.AddMinutes(-1), "helper", "launch-failed", scopeInName: true);
            monitor.Refresh();
            File.WriteAllText(Path.Combine(monitor.HealthDirectory, "success-" + other + ".json"),
                JsonSerializer.Serialize(new { schema = 1, sessionKey = other, startedUtcTicks = this._now.Ticks, completedUtcTicks = this._now.Ticks + 1, @event = "Stop" }));
            for (var i = 0; i < 16; i++)
            {
                Failure(monitor, this._now.AddSeconds(-30 + i), "delivery", "observation-failed", scopeInName: true);
            }

            var restarted = this.Monitor();
            Assert.Equal(WindowsHookHealthStatus.Healthy, restarted.Refresh());
            Assert.True(restarted.IsSessionObservationCurrent(other));
            Assert.False(restarted.IsSessionObservationCurrent(Session));
        }
    }
}
