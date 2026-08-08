namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// The reinstall registration desync and its self-heal (RegistrationHeal.ShouldHeal).
    ///
    /// Any reinstall drops the application registration from the service's live list while
    /// leaving disk intact; the plugin detects "this load is the install, and the registration
    /// predates the payload" and schedules one service restart. The decision must fire for
    /// exactly that case — a false positive restarts the user's service for nothing, and a
    /// restart loop would be catastrophic, so the cold-start gate is load-bearing.
    /// </summary>
    public class RegistrationHealTests
    {
        private static readonly DateTime Now = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan LongUptime = TimeSpan.FromHours(3);

        [Fact]
        public void Reinstall_over_existing_registration_heals()
        {
            // Payload extracted moments ago, registration from days before: the desync.
            var heal = RegistrationHeal.ShouldHeal(
                serviceUptime: LongUptime,
                payloadWrittenUtc: Now.AddSeconds(-20),
                registrationWrittenUtc: Now.AddDays(-2),
                nowUtc: Now,
                alreadyHealedThisPayload: false);

            Assert.True(heal);
        }

        [Fact]
        public void Clean_install_does_not_heal()
        {
            // A first-ever install writes the registration AFTER extracting the payload, so the
            // registration is the newer file — the install did its job, nothing is desynced.
            var heal = RegistrationHeal.ShouldHeal(
                serviceUptime: LongUptime,
                payloadWrittenUtc: Now.AddSeconds(-20),
                registrationWrittenUtc: Now.AddSeconds(-5),
                nowUtc: Now,
                alreadyHealedThisPayload: false);

            Assert.False(heal);
        }

        [Fact]
        public void Missing_registration_does_not_heal()
        {
            // Mid-clean-install, before the service has written the entry: there is no stale
            // state to re-adopt, and restarting here could interrupt the registration itself.
            var heal = RegistrationHeal.ShouldHeal(
                serviceUptime: LongUptime,
                payloadWrittenUtc: Now.AddSeconds(-20),
                registrationWrittenUtc: null,
                nowUtc: Now,
                alreadyHealedThisPayload: false);

            Assert.False(heal);
        }

        [Fact]
        public void Cold_start_never_heals_which_makes_a_restart_loop_impossible()
        {
            // After our own killall the service comes back within seconds and reloads the plugin
            // with the payload still fresh and the registration still old — the exact inputs that
            // just healed. Only the uptime gate stands between that reload and a forever-loop.
            var heal = RegistrationHeal.ShouldHeal(
                serviceUptime: TimeSpan.FromSeconds(10),
                payloadWrittenUtc: Now.AddSeconds(-40),
                registrationWrittenUtc: Now.AddDays(-2),
                nowUtc: Now,
                alreadyHealedThisPayload: false);

            Assert.False(heal);
        }

        [Fact]
        public void Old_payload_does_not_heal()
        {
            // Plugin re-enabled in Options+ weeks after installing: a plugin load, but not an
            // install event. The live list already has whatever the last service start scanned.
            var heal = RegistrationHeal.ShouldHeal(
                serviceUptime: LongUptime,
                payloadWrittenUtc: Now.AddDays(-14),
                registrationWrittenUtc: Now.AddDays(-30),
                nowUtc: Now,
                alreadyHealedThisPayload: false);

            Assert.False(heal);
        }

        [Fact]
        public void A_payload_heals_at_most_once()
        {
            // Same payload, marker already written — e.g. the service's duplicate "already
            // loaded" scan attempt racing the first load. One restart per install, never two.
            var heal = RegistrationHeal.ShouldHeal(
                serviceUptime: LongUptime,
                payloadWrittenUtc: Now.AddSeconds(-20),
                registrationWrittenUtc: Now.AddDays(-2),
                nowUtc: Now,
                alreadyHealedThisPayload: true);

            Assert.False(heal);
        }
    }
}
