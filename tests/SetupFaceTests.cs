namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    using Loupedeck.ClaudeConsolePlugin.Actions;
    using Loupedeck.ClaudeConsolePlugin.Models;
    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// What the answer keys and the session faces say while the live-status wiring is absent (#58).
    ///
    /// Logitech QA's retest of 2.2.0 (item 2) and the owner's own repro on 2026-09-02: live status
    /// off, a real permission menu on screen, and Yes / No looked ready, beeped, and did nothing —
    /// while the session's key read "Complete". The PermissionRequest hook the keys see prompts
    /// through is part of the opt-in wiring, so without it the honest face is the live keys' own
    /// word, "Set up" / "Off", not a green tile.
    ///
    /// The second half of that finding — "until Claude Code is restarted" — turned out to be wrong
    /// on macOS, where a running session picks the wiring up by itself. That fact now lives on the
    /// platform seam (IPlatformBridge.SettingsApplyLive) and the wording follows it.
    /// </summary>
    public class SetupFaceTests
    {
        [Theory]
        [InlineData(LiveStatusState.NotEnabled, "Set up")]
        [InlineData(LiveStatusState.NeedsRepair, "Set up")]
        [InlineData(LiveStatusState.Off, "Off")]
        [InlineData(LiveStatusState.JustEnabled, null)]
        [InlineData(LiveStatusState.Enabled, null)]
        public void The_setup_word_is_the_live_keys_own_word_so_the_user_learns_one_vocabulary(LiveStatusState state, String expected)
        {
            Assert.Equal(expected, LiveStatusFace.SetupWord(liveStatusApplies: true, state));
            Assert.Equal(expected, LiveStatusFace.SetupLabel(state));
        }

        [Fact]
        public void An_agent_without_a_settings_switch_never_says_set_up()
        {
            // Codex keeps its own hooks file behind its trust prompt; its keys have no switch to
            // point at, so "Set up" on them would be an instruction with nothing to do.
            foreach (var state in Enum.GetValues<LiveStatusState>())
            {
                Assert.Null(LiveStatusFace.SetupWord(liveStatusApplies: false, state));
                Assert.Null(LiveStatusFace.SessionBarWord(liveStatusApplies: false, state));
            }
        }

        [Theory]
        [InlineData(LiveStatusState.NotEnabled, "Set up")]
        [InlineData(LiveStatusState.NeedsRepair, "Set up")]
        [InlineData(LiveStatusState.Off, "Status off")]
        [InlineData(LiveStatusState.JustEnabled, null)]
        [InlineData(LiveStatusState.Enabled, null)]
        public void A_session_bar_says_what_is_off_not_that_the_session_is(LiveStatusState state, String expected)
        {
            // First hardware pass, 2026-09-02: every bar read "Off" and the owner asked why all the
            // sessions were off. The live keys ARE the status, so "Off" is right on them; the bar
            // names the thing that is off. The word still fits the bar (≤ 12 characters).
            var word = LiveStatusFace.SessionBarWord(liveStatusApplies: true, state);

            Assert.Equal(expected, word);
            Assert.True(word == null || word.Length <= 12, $"'{word}' is too long for a state bar");
        }

        [Fact]
        public void A_session_the_plugin_knows_nothing_about_says_set_up_not_complete()
        {
            // The owner read "Complete" under a live permission prompt: with the wiring off the
            // registry only knows the session exists (IsProvisional), and "Complete" was the
            // default word for "no state". The setup word is what would change that.
            var unreported = new GridSession { SessionKey = "ttys004", IsProvisional = true, State = "ready" };

            Assert.Equal("Status off", SessionSlotCommand.StateWord(unreported, "Status off"));
            Assert.Equal("Set up", SessionSlotCommand.StateWord(unreported, "Set up"));
        }

        [Fact]
        public void A_wired_session_that_has_not_reported_yet_keeps_the_old_word()
        {
            // Turn on happened; the session simply has not written a state file yet. Nothing is
            // owed from the user, so the face must not ask for anything.
            var unreported = new GridSession { SessionKey = "ttys004", IsProvisional = true, State = "ready" };

            Assert.Equal("Complete", SessionSlotCommand.StateWord(unreported, null));
        }

        [Fact]
        public void With_the_wiring_off_frozen_state_and_a_stale_approval_lose_to_the_setup_word()
        {
            // Found on the hardware pass: `claude` relaunched in the same tab inherited the previous
            // session's state file, so with live status off the new session's bar read "Waiting" —
            // a fact about a process that no longer existed. Nothing can refresh a state file or
            // clear a pending payload while the hooks are absent, so every word the registry holds
            // is at best frozen; the bar says "Off" / "Set up" for every session until the wiring
            // is back, and "Allow?" is no exception (a pending file cannot be cleared either).
            var frozen = new GridSession { SessionKey = "ttys004", State = "waiting" };
            var stalePending = new GridSession { SessionKey = "ttys001", State = "waiting", Risk = ApprovalRisk.Normal };

            Assert.Equal("Status off", SessionSlotCommand.StateWord(frozen, "Status off"));
            Assert.Equal("Status off", SessionSlotCommand.StateWord(stalePending, "Status off"));
        }

        [Fact]
        public void With_the_wiring_on_real_state_and_a_pending_approval_show_as_before()
        {
            var reported = new GridSession { SessionKey = "ttys001", State = "busy" };
            var pending = new GridSession { SessionKey = "ttys001", State = "waiting", Risk = ApprovalRisk.Normal };

            Assert.Equal("Thinking", SessionSlotCommand.StateWord(reported, null));
            Assert.Equal("Allow?", SessionSlotCommand.StateWord(pending, null));
        }

        [Fact]
        public void Each_platform_states_settings_apply_live_from_a_measurement_not_an_assumption()
        {
            // macOS: measured 2026-09-02 (Claude Code 2.1.258). Windows: measured 2026-09-10 and
            // 2026-09-11 on sessions that were never restarted; until then it said false on the
            // strength of QA's retest item 2, which turned out to be #74 wearing this face. Both
            // now say true, and each must still cite its evidence beside the value: the property
            // exists so a platform that genuinely needs a restart can say so, and "true" typed in
            // without a date is exactly the tidy-up this test exists to refuse.
            var mac = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Core", "Platform", "MacPlatformBridge.cs"));
            var windows = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Core", "Platform", "WindowsPlatformBridge.cs"));

            Assert.Contains("public Boolean SettingsApplyLive => true;", mac);
            Assert.Contains("2026-09-02", mac.Substring(0, mac.IndexOf("SettingsApplyLive => true", StringComparison.Ordinal)));
            Assert.Contains("public Boolean SettingsApplyLive => true;", windows);
            var before = windows.Substring(0, windows.IndexOf("SettingsApplyLive => true", StringComparison.Ordinal));
            Assert.Contains("2026-09-10", before);
            Assert.Contains("2026-09-11", before);
            Assert.Contains("#58", before);
        }

        [Fact]
        public void The_answer_and_session_faces_repaint_when_live_status_flips()
        {
            // The setup word comes and goes with the wiring; a face that only repainted on grid
            // changes would keep saying "Off" after Turn on until some session happened to change.
            var answer = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Core", "Actions", "AnswerCommand.cs"));
            var slot = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Core", "Actions", "SessionSlotCommand.cs"));

            Assert.Contains("OnLiveStatusChanged +=", answer);
            Assert.Contains("OnLiveStatusChanged +=", slot);
        }

        private static String RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "src", "Core")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate the repo root");
        }
    }
}
