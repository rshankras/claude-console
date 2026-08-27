namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Actions;

    using Xunit;

    /// <summary>
    /// The Yes/No keys against a permission MENU (#21).
    ///
    /// In 2.0.1 both keys typed a literal word and pressed Return. A permission prompt is a
    /// numbered menu, so the word did nothing and the Return confirmed whichever option was
    /// highlighted — option 1, Yes. Pressing No therefore ran the command. Reproduced on hardware
    /// 2026-08-27: a file the session had been asked to delete was deleted by a press of No.
    ///
    /// These tests are about the DECISION, not the AppleScript: which delivery a press should use
    /// given what the grid says about the targeted session.
    /// </summary>
    public class AnswerKeyTests
    {
        [Fact]
        public void No_never_types_a_word_at_a_menu___that_is_the_bug()
        {
            // The whole finding in one assertion. If this ever returns TypeWord again, pressing No
            // approves the action, because Return confirms option 1.
            Assert.Equal(AnswerCommand.AnswerVia.MenuReject, AnswerCommand.Decide(approve: false, sessionState: "waiting"));
        }

        [Fact]
        public void Yes_at_a_menu_confirms_the_highlighted_option()
        {
            // Option 1 is Yes and starts highlighted, so Return is correct AND count-independent —
            // the payload we capture carries tool_name/tool_input only, never the option list.
            Assert.Equal(AnswerCommand.AnswerVia.MenuConfirm, AnswerCommand.Decide(approve: true, sessionState: "waiting"));
        }

        [Theory]
        [InlineData("busy")]
        [InlineData("ready")]
        [InlineData(null)]      // no target session at all
        public void With_no_menu_up_the_word_is_still_the_answer(String state)
        {
            // Plain-text questions ("Should I proceed?") are answered by typing, and there is no
            // menu for the word to be swallowed by. Both keys keep that behaviour.
            Assert.Equal(AnswerCommand.AnswerVia.TypeWord, AnswerCommand.Decide(approve: true, sessionState: state));
            Assert.Equal(AnswerCommand.AnswerVia.TypeWord, AnswerCommand.Decide(approve: false, sessionState: state));
        }

        [Fact]
        public void The_two_menu_answers_are_different___a_pass_by_symmetry_would_be_no_fix()
        {
            // Guards the shape of the fix rather than its letter: if Yes and No ever resolve to the
            // same delivery at a menu, one of them is wrong, and 2.0.1's failure was precisely that
            // both ended up meaning Yes.
            var yes = AnswerCommand.Decide(approve: true, sessionState: "waiting");
            var no = AnswerCommand.Decide(approve: false, sessionState: "waiting");

            Assert.NotEqual(yes, no);
        }
    }
}
