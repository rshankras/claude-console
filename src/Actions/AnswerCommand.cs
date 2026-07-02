namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// Answer keys (group "Answer") — for responding when Claude Code prompts a question.
    /// One auto-discovered command, one SDK action per response via AddParameter.
    ///   Yes    → types "yes" + Enter   (plain-text questions like "Should I proceed?")
    ///   No     → types "no"  + Enter
    ///   Up     → Up-arrow keystroke    (move the selection up in a menu)
    ///   Down   → Down-arrow keystroke  (move the selection down in a menu)
    ///   Enter  → Return keystroke      (confirm the highlighted menu option / submit)
    ///
    /// Up/Down/Enter drive Claude Code's numbered selection menus (permission prompts,
    /// AskUserQuestion, plan-mode confirmation): arrow to an option, then Enter. Yes/No type a
    /// literal reply for free-text questions — they do NOT pick a numbered menu option (use
    /// Up/Down + Enter for those). All sent as raw System Events input to the focused terminal,
    /// the same path the prompt keys use; needs Accessibility (already required by the plugin).
    /// </summary>
    public class AnswerCommand : PluginDynamicCommand
    {
        private const String Yes = "yes";
        private const String No = "no";
        private const String Up = "up";
        private const String Down = "down";
        private const String Enter = "enter";

        public AnswerCommand()
            : base()
        {
            this.AddParameter(Yes, "Yes", "Answer")
                .SetDescription("Type \"yes\" and press Enter — for plain-text questions (use Up/Down/Return for numbered menus)");
            this.AddParameter(No, "No", "Answer")
                .SetDescription("Type \"no\" and press Enter — for plain-text questions");
            this.AddParameter(Up, "Arrow Up", "Answer")
                .SetDescription("Move the selection up in a Claude Code menu (Up arrow)");
            this.AddParameter(Down, "Arrow Down", "Answer")
                .SetDescription("Move the selection down in a Claude Code menu (Down arrow)");
            this.AddParameter(Enter, "Return", "Answer")
                .SetDescription("Confirm the highlighted menu option / submit (Return)");
        }

        protected override void RunCommand(String actionParameter)
        {
            var bridge = BridgeManager.Instance;
            switch (actionParameter)
            {
                case Yes:
                    bridge.InjectText("yes", pressEnter: true);
                    break;
                case No:
                    bridge.InjectText("no", pressEnter: true);
                    break;
                case Up:
                    bridge.InjectKeystroke("key code 126"); // key code 126 = Up arrow
                    break;
                case Down:
                    bridge.InjectKeystroke("key code 125"); // key code 125 = Down arrow
                    break;
                case Enter:
                    bridge.InjectKeystroke("key code 36"); // key code 36 = Return
                    break;
            }

            PluginLog.Info($"AnswerCommand: {actionParameter}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            switch (actionParameter)
            {
                case Yes: return "Yes";
                case No: return "No";
                case Up: return "Up";
                case Down: return "Down";
                case Enter: return "Enter";
                default: return actionParameter;
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            // Icon basename == actionParameter (yes/no/up/down/enter .png in Resources/icons), matching
            // ControlCommand. Colours are currently unused by KeyImage but follow the palette convention
            // so a future switch to coloured tiles (see KeyImage) renders these sensibly.
            BitmapColor color;
            switch (actionParameter)
            {
                case Yes: color = KeyImage.Green; break;
                case No: color = KeyImage.Red; break;
                case Enter: color = KeyImage.Green; break;
                default: color = KeyImage.Slate; break; // Up, Down
            }
            return KeyImage.Render(imageSize, this.GetCommandDisplayName(actionParameter, imageSize), color, actionParameter);
        }
    }
}
