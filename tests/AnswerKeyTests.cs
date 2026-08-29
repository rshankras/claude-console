namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

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
    /// Return/Escape; anything else → do nothing.
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
        public void The_two_menu_answers_are_different___a_pass_by_symmetry_would_be_no_fix()
        {
            // Guards the shape of the fix rather than its letter: if Yes and No ever resolve to the
            // same delivery at a menu, one of them is wrong, and 2.0.1's failure was precisely that
            // both ended up meaning Yes.
            var yes = AnswerCommand.Decide(approve: true, hasPendingApproval: true);
            var no = AnswerCommand.Decide(approve: false, hasPendingApproval: true);

            Assert.NotEqual(yes, no);
        }
    }
}
