namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    using Loupedeck.ClaudeConsolePlugin.Actions;

    using Xunit;

    /// <summary>
    /// The Yes/No keys against a permission MENU (#21), and the retest regression on top of it.
    ///
    /// In 2.0.1 both keys typed a literal word and pressed Return. A permission prompt is a
    /// numbered menu, so the word did nothing and the Return confirmed whichever option was
    /// highlighted — option 1, Yes. Pressing No therefore ran the command. Reproduced on hardware
    /// 2026-08-27: a file the session had been asked to delete was deleted by a press of No.
    ///
    /// THE 2.0.1-RETEST REGRESSION: the first fix keyed the decision on State=="waiting", but #51
    /// then made the amber badge require a captured payload — because an idle prompt is "waiting"
    /// too. So a session idling with nothing to approve got Return (Yes) / Escape (No) at it. The
    /// decision now keys on the captured PAYLOAD, exactly like the badge: an approval we can see →
    /// Return/Escape; anything else → do nothing when the transport is expected to report it.
    /// Agents without approval observation are the explicit exception: a manual Yes/No press sends
    /// Return/Escape without ever typing a word. Current Codex hooks report approvals on Windows.
    ///
    /// These tests are about the DECISION, not the AppleScript: which delivery a press should use
    /// given whether the targeted session has a pending approval.
    /// </summary>
    public class AnswerKeyTests
    {
        [Fact]
        public void No_never_types_a_word_at_a_menu___that_is_the_bug()
        {
            // The whole #21 finding in one assertion. With an approval pending, No must Escape
            // (reject) — never type a word, which would let Return confirm option 1 (Yes).
            Assert.Equal(AnswerCommand.AnswerVia.MenuReject, AnswerCommand.Decide(approve: false, hasPendingApproval: true));
        }

        [Fact]
        public void Yes_at_a_menu_confirms_the_highlighted_option()
        {
            // Option 1 is Yes and starts highlighted, so Return is correct AND count-independent —
            // the payload we capture carries tool_name/tool_input only, never the option list.
            Assert.Equal(AnswerCommand.AnswerVia.MenuConfirm, AnswerCommand.Decide(approve: true, hasPendingApproval: true));
        }

        [Fact]
        public void Waiting_with_no_pending_approval_does_nothing___the_retest_regression()
        {
            // The regression the 2.0.1 retest found: a session "waiting" with nothing to approve
            // (an idle prompt, or the Notification hook) must NOT get a bare Return/Escape. No
            // captured payload → NoOp (beep), for BOTH keys.
            Assert.Equal(AnswerCommand.AnswerVia.NoOp, AnswerCommand.Decide(approve: true, hasPendingApproval: false));
            Assert.Equal(AnswerCommand.AnswerVia.NoOp, AnswerCommand.Decide(approve: false, hasPendingApproval: false));
        }

        [Fact]
        public void An_agent_without_approval_observation_can_still_answer_a_visible_prompt()
        {
            Assert.Equal(
                AnswerCommand.AnswerVia.UnobservedConfirm,
                AnswerCommand.Decide(approve: true, hasPendingApproval: false, canObserveApprovals: false));
            Assert.Equal(
                AnswerCommand.AnswerVia.UnobservedReject,
                AnswerCommand.Decide(approve: false, hasPendingApproval: false, canObserveApprovals: false));
        }

        [Fact]
        public void The_unobserved_No_path_is_never_the_confirm_path()
        {
            var yes = AnswerCommand.Decide(approve: true, hasPendingApproval: false, canObserveApprovals: false);
            var no = AnswerCommand.Decide(approve: false, hasPendingApproval: false, canObserveApprovals: false);

            Assert.Equal(AnswerCommand.AnswerVia.UnobservedConfirm, yes);
            Assert.Equal(AnswerCommand.AnswerVia.UnobservedReject, no);
            Assert.NotEqual(yes, no);
        }

        [Fact]
        public void The_two_menu_answers_are_different___a_pass_by_symmetry_would_be_no_fix()
        {
            // Guards the shape of the fix rather than its letter: if Yes and No ever resolve to the
            // same delivery at a menu, one of them is wrong, and 2.0.1's failure was precisely that
            // both ended up meaning Yes.
            var yes = AnswerCommand.Decide(approve: true, hasPendingApproval: true);
            var no = AnswerCommand.Decide(approve: false, hasPendingApproval: true);

            Assert.NotEqual(yes, no);
        }

        [Theory]
        [InlineData(ApprovalRisk.None, ApprovalRisk.None, ApprovalRisk.None)]
        [InlineData(ApprovalRisk.Normal, ApprovalRisk.Normal, ApprovalRisk.Normal)]
        [InlineData(ApprovalRisk.High, ApprovalRisk.High, ApprovalRisk.Normal)]
        public void Both_answer_keys_show_pending_while_only_yes_carries_destructive_risk(
            ApprovalRisk pending,
            ApprovalRisk yes,
            ApprovalRisk no)
        {
            // #60: the dot is an availability cue, so hiding it from No made that key look inert.
            // A dangerous command remains red on Yes; No stays amber because it rejects the action.
            Assert.Equal(yes, AnswerCommand.IndicatorRisk(approve: true, pending));
            Assert.Equal(no, AnswerCommand.IndicatorRisk(approve: false, pending));
        }

        [Fact]
        public void A_press_before_setup_is_answered_before_the_decision_is_even_asked()
        {
            // #58, reproduced 2026-09-02: live status off, a real permission menu on screen, and four
            // Yes presses logged as "no pending approval — ignored" with only a beep to say so. The
            // PermissionRequest hook that would show the plugin the prompt is part of the opt-in
            // wiring, so the press must be explained BEFORE Decide() — which can only ever say NoOp
            // there — and the explanation must reach Options+, not just the log.
            var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Core", "Actions", "AnswerCommand.cs"));
            var body = source.Substring(source.IndexOf("static void AnswerApproval(", StringComparison.Ordinal));

            var setup = body.IndexOf("LiveStatusFace.SetupWord(bridge.LiveStatusApplies, bridge.LiveStatus)", StringComparison.Ordinal);
            var decide = body.IndexOf("Decide(approve, hasPending", StringComparison.Ordinal);
            Assert.True(setup >= 0, "AnswerApproval no longer checks the live-status setup word");
            Assert.True(decide > setup, "the setup check must come before the menu decision");
            Assert.Contains("BridgeNotice.AnswerNeedsSetup()", body.Substring(setup, decide - setup));
        }

        [Fact]
        public void An_answer_that_landed_clears_the_badge_and_one_that_did_not_leaves_it()
        {
            // #60: after a No the menu was gone but the Yes dot and the "Allow?" bar stayed until
            // the session's next prompt, because a rejection fires no hook. The answer key now
            // clears the payload itself — but only on a keystroke the platform reports as landed.
            // A badge left over a failed injection is still true; a badge cleared over one is a lie.
            var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Core", "Actions", "AnswerCommand.cs"));
            var body = source.Substring(source.IndexOf("internal static void Answered(", StringComparison.Ordinal));

            var landed = body.IndexOf("outcome == InjectionOutcome.Ok", StringComparison.Ordinal);
            var clear = body.IndexOf("bridge.Grid.ClearPendingApproval(target)", StringComparison.Ordinal);
            Assert.True(landed >= 0, "Answered() no longer checks whether the keystroke landed");
            Assert.True(clear > landed, "the badge must be cleared only after the keystroke is known to have landed");
            Assert.Contains("the badge stays", body);
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
