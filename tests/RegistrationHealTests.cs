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
    /// predates the payload" and schedules one service restart. The install-event signal is the
    /// payload being written AFTER the current service started — a real install always happens
    /// inside a running service session, and the reload after our own restart always sees a
    /// payload older than its fresh service start, which is what makes a loop impossible. A
    /// false positive restarts the user's service for nothing, so every gate here is load-bearing.
    /// </summary>
    public class RegistrationHealTests
    {
        private static readonly DateTime ServiceStart = new DateTime(2026, 8, 8, 9, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Reinstall_during_the_service_session_heals()
        {
            // Service up since 09:00, payload extracted at 12:00, registration from days ago.
            var heal = RegistrationHeal.ShouldHeal(
                serviceStartUtc: ServiceStart,
                payloadWrittenUtc: ServiceStart.AddHours(3),
                registrationWrittenUtc: ServiceStart.AddDays(-2),
                alreadyHealedThisPayload: false);

            Assert.True(heal);
        }

        [Fact]
        public void A_second_reinstall_right_after_a_heal_also_heals()
        {
            // The gap the first field test found: the user reinstalled again minutes after the
            // previous heal's restart. The new payload is newer than the restarted service's
            // start time, so it must heal again — an uptime-based gate wrongly suppressed this.
            var heal = RegistrationHeal.ShouldHeal(
                serviceStartUtc: ServiceStart,
                payloadWrittenUtc: ServiceStart.AddSeconds(90),
                registrationWrittenUtc: ServiceStart.AddDays(-2),
                alreadyHealedThisPayload: false);

            Assert.True(heal);
        }

        [Fact]
        public void The_reload_after_our_own_restart_never_heals_which_makes_a_loop_impossible()
        {
            // Our killall restarts the service; the plugin reloads with the payload written
            // BEFORE the fresh service start and the registration still old — the exact inputs
            // that just healed, now inert by construction.
            var heal = RegistrationHeal.ShouldHeal(
                serviceStartUtc: ServiceStart,
                payloadWrittenUtc: ServiceStart.AddMinutes(-1),
                registrationWrittenUtc: ServiceStart.AddDays(-2),
                alreadyHealedThisPayload: false);

            Assert.False(heal);
        }

        [Fact]
        public void Clean_install_does_not_heal()
        {
            // A first-ever install writes the registration AFTER extracting the payload, so the
            // registration is the newer file — the install did its job, nothing is desynced.
            var heal = RegistrationHeal.ShouldHeal(
                serviceStartUtc: ServiceStart,
                payloadWrittenUtc: ServiceStart.AddHours(3),
                registrationWrittenUtc: ServiceStart.AddHours(3).AddSeconds(15),
                alreadyHealedThisPayload: false);

            Assert.False(heal);
        }

        [Fact]
        public void Missing_registration_does_not_heal()
        {
            // Mid-clean-install, before the service has written the entry: there is no stale
            // state to re-adopt, and restarting here could interrupt the registration itself.
            var heal = RegistrationHeal.ShouldHeal(
                serviceStartUtc: ServiceStart,
                payloadWrittenUtc: ServiceStart.AddHours(3),
                registrationWrittenUtc: null,
                alreadyHealedThisPayload: false);

            Assert.False(heal);
        }

        [Fact]
        public void Cold_start_with_an_old_payload_does_not_heal()
        {
            // Ordinary boot: payload predates the service session entirely. Also the dev-build
            // shape — the PostBuild link reload restarts the service right after writing output.
            var heal = RegistrationHeal.ShouldHeal(
                serviceStartUtc: ServiceStart,
                payloadWrittenUtc: ServiceStart.AddDays(-7),
                registrationWrittenUtc: ServiceStart.AddDays(-30),
                alreadyHealedThisPayload: false);

            Assert.False(heal);
        }

        [Fact]
        public void A_payload_heals_at_most_once()
        {
            // Same payload, marker already written — e.g. the service's duplicate "already
            // loaded" scan attempt racing the first load. One restart per install, never two.
            var heal = RegistrationHeal.ShouldHeal(
                serviceStartUtc: ServiceStart,
                payloadWrittenUtc: ServiceStart.AddHours(3),
                registrationWrittenUtc: ServiceStart.AddDays(-2),
                alreadyHealedThisPayload: true);

            Assert.False(heal);
        }
    }
}
