namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Text.Json;

    using Xunit;

    public sealed class WindowsHookHealthTests : IDisposable
    {
        private const String SessionA = "pid-101-638900000000000000";
        private const String SessionB = "pid-202-638900000000000001";
        private readonly String _directory = Path.Combine(Path.GetTempPath(), "cc-hook-health-" + Guid.NewGuid().ToString("N"));
        private DateTime _now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        private readonly String _helper;
        private readonly String _ipc;

        public WindowsHookHealthTests()
        {
            this._helper = Path.Combine(this._directory, "package", "claude-console-hook.exe");
            this._ipc = Path.Combine(this._directory, "claude-console");
            this.RestoreHelper();
        }

        public void Dispose()
        {
            try { Directory.Delete(this._directory, recursive: true); } catch { }
        }

        private WindowsHookHealth Monitor(String helper = null, String ipc = null) =>
            new WindowsHookHealth(helper ?? this._helper, ipc ?? this._ipc, "claude-console", () => this._now);

        private void RestoreHelper()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this._helper));
            File.WriteAllText(this._helper, "test helper");
            File.SetLastWriteTimeUtc(this._helper, this._now.AddDays(-1));
        }

        private static void Success(WindowsHookHealth monitor, String key, DateTime started, DateTime? completed = null)
        {
            Directory.CreateDirectory(monitor.HealthDirectory);
            File.WriteAllText(Path.Combine(monitor.HealthDirectory, "success-" + key + ".json"), JsonSerializer.Serialize(new
            {
                schema = 1,
                sessionKey = key,
                startedUtcTicks = started.Ticks,
                completedUtcTicks = (completed ?? started.AddTicks(1)).Ticks,
                @event = "PermissionRequest",
            }));
        }

        private static void Failure(WindowsHookHealth monitor, DateTime observed, String reason = "launch-failed", String scope = null)
        {
            Directory.CreateDirectory(monitor.HealthDirectory);
            File.WriteAllText(Path.Combine(monitor.HealthDirectory, $"failure-{observed.Ticks}-{Guid.NewGuid():N}.json"),
                scope == null
                    ? JsonSerializer.Serialize(new { schema = 1, observedUtcTicks = observed.Ticks, reason })
                    : JsonSerializer.Serialize(new { schema = 1, observedUtcTicks = observed.Ticks, reason, scope }));
        }

        [Fact]
        public void A_present_helper_without_execution_evidence_is_not_healthy()
        {
            var monitor = this.Monitor();
            Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, monitor.Refresh());
            Assert.False(monitor.IsSessionObservationCurrent(SessionA));
            Assert.Equal(DateTime.MinValue.Ticks, monitor.InvalidatedAtUtc.Ticks);
        }

        [Fact]
        public void Successful_delivery_verifies_only_the_session_that_delivered_it()
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now);

            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
            Assert.True(monitor.IsSessionObservationCurrent(SessionA));
            Assert.False(monitor.IsSessionObservationCurrent(SessionB));
            Assert.False(monitor.IsSessionObservationCurrent("shared"));
            Assert.False(monitor.IsSessionObservationCurrent(null));
        }

        [Fact]
        public void A_quiet_healthy_session_does_not_expire()
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now);
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());

            this._now = this._now.AddDays(30);
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
            Assert.True(monitor.IsSessionObservationCurrent(SessionA));
        }

        [Fact]
        public void Missing_helper_invalidates_active_evidence_and_restoration_needs_a_new_hook()
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now.AddSeconds(-1));
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
            File.Delete(this._helper);

            monitor.Refresh();
            Assert.False(monitor.IsSessionObservationCurrent(SessionA));
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Status);
            Assert.Equal(this._now, monitor.InvalidatedAtUtc);

            // Putting the file back is not evidence that it runs: the newest thing seen is still
            // the failure, so the keys stay Blocked until a hook actually succeeds.
            this.RestoreHelper();
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            Assert.False(monitor.IsSessionObservationCurrent(SessionA));

            Success(monitor, SessionA, this._now.AddSeconds(1));
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
            Assert.True(monitor.IsSessionObservationCurrent(SessionA));
        }

        [Fact]
        public void Missing_failure_is_persisted_across_plugin_restart()
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now.AddSeconds(-1));
            File.Delete(this._helper);
            monitor.Refresh();
            this.RestoreHelper();

            var restarted = this.Monitor();
            Assert.Equal(WindowsHookHealthStatus.Unavailable, restarted.Refresh());
            Assert.Equal(this._now, restarted.InvalidatedAtUtc);
            Assert.False(restarted.IsSessionObservationCurrent(SessionA));
        }

        [Fact]
        public void No_evidence_at_all_is_awaiting_fresh_not_unavailable()
        {
            // A fresh install, a new day, no session open: nothing has failed, so nothing is blocked.
            var monitor = this.Monitor();
            Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, monitor.Refresh());
            Assert.Equal(DateTime.MinValue.Ticks, monitor.InvalidatedAtUtc.Ticks);
            Assert.Empty(Directory.Exists(monitor.HealthDirectory) ? Directory.GetFiles(monitor.HealthDirectory) : Array.Empty<String>());
        }

        [Theory]
        [InlineData("observation-failed")]
        [InlineData("input-timeout")]
        public void A_delivery_failure_withholds_a_receipt_but_never_blocks_the_helper(String reason)
        {
            // The exe ran (or stdin was slow); that is not a helper failure. Other sessions keep
            // working, and with no receipts at all the answer is "nothing yet", not Blocked.
            var monitor = this.Monitor();
            Failure(monitor, this._now, reason, scope: "delivery");
            Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, monitor.Refresh());
            Assert.Equal(DateTime.MinValue.Ticks, monitor.InvalidatedAtUtc.Ticks);
            Assert.Equal((this._now, reason), monitor.LastDeliveryFailure);

            Success(monitor, SessionA, this._now.AddSeconds(-1));
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
            Assert.True(monitor.IsSessionObservationCurrent(SessionA));
        }

        [Fact]
        public void A_launcher_record_without_a_scope_is_a_helper_failure()
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now.AddSeconds(-1));
            Failure(monitor, this._now, "launch-failed");   // pre-scope shape
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
        }

        [Fact]
        public void A_failure_followed_by_a_success_is_forgotten_even_after_that_session_is_pruned()
        {
            // Yesterday: one launch failure, then hours of successful hooks. Today the sessions
            // are dead and their receipts pruned. The failure is history, not the current state.
            var monitor = this.Monitor();
            Failure(monitor, this._now.AddHours(-13));
            Success(monitor, SessionA, this._now.AddHours(-12));
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());

            monitor.PruneDeadSessions(Array.Empty<String>());
            Assert.Empty(Directory.GetFiles(monitor.HealthDirectory, "success-*.json"));
            Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, monitor.Refresh());

            var restarted = this.Monitor();   // the watermark file carries it across a restart
            Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, restarted.Refresh());
        }

        [Fact]
        public void A_failure_after_the_last_success_stays_unavailable_across_pruning_and_restart()
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now.AddHours(-12));
            Failure(monitor, this._now.AddHours(-11));
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());

            monitor.PruneDeadSessions(Array.Empty<String>());
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            Assert.Equal(WindowsHookHealthStatus.Unavailable, this.Monitor().Refresh());
        }

        [Fact]
        public void A_newer_helper_file_than_the_failure_starts_over_as_awaiting_fresh()
        {
            // An upgrade or reinstall replaces the file the failure was recorded against. Start
            // clean and let the first hook decide; a file IT put back keeps its old mtime and
            // stays Unavailable (covered above).
            var monitor = this.Monitor();
            Failure(monitor, this._now);
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            File.SetLastWriteTimeUtc(this._helper, this._now.AddSeconds(1));
            Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, monitor.Refresh());
        }

        [Fact]
        public void Repeated_missing_checks_do_not_create_marker_storms_or_move_the_barrier()
        {
            var monitor = this.Monitor();
            File.Delete(this._helper);
            monitor.Refresh();
            var barrier = monitor.InvalidatedAtUtc;
            var revision = monitor.Revision;

            this._now = this._now.AddHours(1);
            monitor.Refresh();
            monitor.Refresh();
            Assert.Equal(barrier, monitor.InvalidatedAtUtc);
            Assert.Equal(revision, monitor.Revision);
            Assert.Single(Directory.GetFiles(monitor.HealthDirectory, "failure-*.json"));

            this.RestoreHelper();
            monitor.Refresh();
            File.Delete(this._helper);
            monitor.Refresh();
            Assert.Equal(this._now, monitor.InvalidatedAtUtc);
        }

        [Theory]
        [InlineData("launch-failed")]
        [InlineData("nonzero-exit")]
        [InlineData("unavailable")]
        public void A_present_but_failed_helper_is_blocked_until_successful_delivery(String reason)
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now.AddSeconds(-1));
            Failure(monitor, this._now, reason);

            Assert.True(File.Exists(this._helper));
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            Assert.False(monitor.IsSessionObservationCurrent(SessionA));

            Success(monitor, SessionA, this._now.AddSeconds(1));
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
        }

        [Fact]
        public void A_hook_started_before_failure_cannot_recover_by_finishing_after_it()
        {
            var monitor = this.Monitor();
            Failure(monitor, this._now);
            Success(monitor, SessionA, this._now.AddSeconds(-1), this._now.AddSeconds(1));

            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            Assert.False(monitor.IsSessionObservationCurrent(SessionA));
        }

        [Fact]
        public void A_late_older_failure_does_not_erase_a_newer_failure()
        {
            var monitor = this.Monitor();
            Failure(monitor, this._now.AddSeconds(2));
            Failure(monitor, this._now);
            Success(monitor, SessionA, this._now.AddSeconds(1));

            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            Assert.Equal(this._now.AddSeconds(2), monitor.InvalidatedAtUtc);
            Success(monitor, SessionA, this._now.AddSeconds(3));
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
        }

        [Fact]
        public void Another_sessions_recovery_does_not_make_old_approval_information_current()
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now.AddSeconds(-1));
            Failure(monitor, this._now);
            Success(monitor, SessionB, this._now.AddSeconds(1));

            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
            Assert.True(monitor.IsSessionObservationCurrent(SessionB));
            Assert.False(monitor.IsSessionObservationCurrent(SessionA));
            Assert.Equal(this._now, monitor.InvalidatedAtUtc);
        }

        [Fact]
        public void Evidence_from_other_products_and_other_installs_is_not_reused()
        {
            var oldInstall = this.Monitor();
            Success(oldInstall, SessionA, this._now);
            Failure(oldInstall, this._now.AddSeconds(-1));
            var otherProduct = this.Monitor(ipc: Path.Combine(this._directory, "codex-console"));
            Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, otherProduct.Refresh());
            Assert.Equal(DateTime.MinValue.Ticks, otherProduct.InvalidatedAtUtc.Ticks);

            var otherHelper = Path.Combine(this._directory, "other-package", "claude-console-hook.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(otherHelper));
            File.Copy(this._helper, otherHelper);
            var otherInstall = this.Monitor(helper: otherHelper);
            Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, otherInstall.Refresh());
            Assert.Equal(DateTime.MinValue.Ticks, otherInstall.InvalidatedAtUtc.Ticks);
        }

        [Fact]
        public void In_place_binary_replacement_needs_execution_evidence_from_the_new_file()
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now);
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
            File.SetLastWriteTimeUtc(this._helper, this._now.AddSeconds(1));

            Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, monitor.Refresh());
            Assert.False(monitor.IsSessionObservationCurrent(SessionA));
            Success(monitor, SessionA, this._now.AddSeconds(2));
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
        }

        [Fact]
        public void Malformed_failure_evidence_cannot_be_overridden_by_old_success()
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now);
            File.WriteAllText(Path.Combine(monitor.HealthDirectory, "failure-broken.json"), "{");
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            Assert.False(monitor.IsSessionObservationCurrent(SessionA));
        }

        [Fact]
        public void A_shared_or_mismatched_receipt_does_not_verify_a_session()
        {
            var monitor = this.Monitor();
            Success(monitor, "shared", this._now);
            Success(monitor, SessionA, this._now);
            File.Move(Path.Combine(monitor.HealthDirectory, "success-" + SessionA + ".json"),
                Path.Combine(monitor.HealthDirectory, "success-" + SessionB + ".json"));

            Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, monitor.Refresh());
            Assert.False(monitor.IsSessionObservationCurrent(SessionA));
            Assert.False(monitor.IsSessionObservationCurrent(SessionB));
        }

        [Fact]
        public void Receipt_updates_increment_revision_even_when_status_stays_healthy()
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now);
            monitor.Refresh();
            var initial = monitor.Revision;
            monitor.Refresh();
            Assert.Equal(initial, monitor.Revision);

            Success(monitor, SessionA, this._now.AddSeconds(1));
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
            Assert.True(monitor.Revision > initial);
            var recovered = monitor.Revision;
            monitor.Refresh();
            Assert.Equal(recovered, monitor.Revision);
        }

        [Fact]
        public void Binary_replacement_advances_the_pending_state_freshness_floor()
        {
            var monitor = this.Monitor();
            Failure(monitor, this._now);
            File.SetLastWriteTimeUtc(this._helper, this._now.AddSeconds(1));
            monitor.Refresh();
            Assert.Equal(this._now.AddSeconds(1), monitor.FreshAfterUtc);
        }

        [Fact]
        public void Repeated_missing_episodes_keep_a_bounded_durable_failure_history()
        {
            var monitor = this.Monitor();
            for (var index = 0; index < 20; ++index)
            {
                Failure(monitor, this._now.AddSeconds(index - 20));
            }
            File.Delete(this._helper);
            monitor.Refresh();
            Assert.Equal(16, Directory.GetFiles(monitor.HealthDirectory, "failure-*.json").Length);
            Assert.Equal(this._now, monitor.InvalidatedAtUtc);
        }

        [Fact]
        public void Pruning_retains_live_idle_sessions_and_failure_barriers_but_removes_dead_receipts()
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now.AddHours(-12));
            Success(monitor, SessionB, this._now.AddHours(-12));
            Failure(monitor, this._now.AddHours(-13));

            monitor.PruneDeadSessions(new[] { SessionA });

            Assert.True(File.Exists(Path.Combine(monitor.HealthDirectory, "success-" + SessionA + ".json")));
            Assert.False(File.Exists(Path.Combine(monitor.HealthDirectory, "success-" + SessionB + ".json")));
            Assert.Single(Directory.GetFiles(monitor.HealthDirectory, "failure-*.json"));
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
            Assert.True(monitor.IsSessionObservationCurrent(SessionA));
        }

        [Fact]
        public void Failed_discovery_and_just_started_sessions_do_not_lose_receipts()
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now.AddHours(-12));
            Success(monitor, SessionB, this._now);

            monitor.PruneDeadSessions(null);
            Assert.Equal(2, Directory.GetFiles(monitor.HealthDirectory, "success-*.json").Length);

            monitor.PruneDeadSessions(Array.Empty<String>());
            Assert.Single(Directory.GetFiles(monitor.HealthDirectory, "success-*.json"));
            Assert.True(File.Exists(Path.Combine(monitor.HealthDirectory, "success-" + SessionB + ".json")));
        }

        [Fact]
        public void Receipt_pruning_does_not_touch_other_products_or_installs()
        {
            var monitor = this.Monitor();
            var other = this.Monitor(ipc: Path.Combine(this._directory, "codex-console"));
            Success(monitor, SessionA, this._now.AddHours(-12));
            Success(other, SessionA, this._now.AddHours(-12));

            monitor.PruneDeadSessions(Array.Empty<String>());

            Assert.Empty(Directory.GetFiles(monitor.HealthDirectory, "success-*.json"));
            Assert.Single(Directory.GetFiles(other.HealthDirectory, "success-*.json"));
        }

        [Fact]
        public void A_corrupt_future_receipt_cannot_make_old_state_current()
        {
            var monitor = this.Monitor();
            Failure(monitor, this._now);
            Success(monitor, SessionA, this._now.AddYears(1));
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            Assert.False(monitor.IsSessionObservationCurrent(SessionA));
        }

        [Fact]
        public void A_corrupt_future_failure_does_not_move_the_recovery_barrier_into_the_future()
        {
            var monitor = this.Monitor();
            Failure(monitor, this._now.AddYears(1));
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            Assert.Equal(this._now, monitor.InvalidatedAtUtc);
        }

        [Fact]
        public void Repairing_unreadable_health_evidence_still_requires_a_fresh_hook()
        {
            var monitor = this.Monitor();
            Success(monitor, SessionA, this._now.AddSeconds(-1));
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
            var malformed = Path.Combine(monitor.HealthDirectory, "failure-broken.json");
            File.WriteAllText(malformed, "{");
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            var barrier = monitor.InvalidatedAtUtc;
            var records = Directory.GetFiles(monitor.HealthDirectory, "failure-*.json").Length;

            this._now = this._now.AddSeconds(1);
            monitor.Refresh();
            Assert.Equal(barrier, monitor.InvalidatedAtUtc);
            Assert.Equal(records, Directory.GetFiles(monitor.HealthDirectory, "failure-*.json").Length);

            File.Delete(malformed);
            Assert.Equal(WindowsHookHealthStatus.Unavailable, monitor.Refresh());
            Assert.False(monitor.IsSessionObservationCurrent(SessionA));
            var restarted = this.Monitor();
            Assert.Equal(WindowsHookHealthStatus.Unavailable, restarted.Refresh());
            Assert.Equal(barrier, restarted.InvalidatedAtUtc);

            Success(monitor, SessionA, this._now);
            Assert.Equal(WindowsHookHealthStatus.Healthy, monitor.Refresh());
            Assert.True(monitor.IsSessionObservationCurrent(SessionA));
        }
    }
}
