namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// Session control keys (group "Core"): Esc, Plan, Compact, Context, Clear, Exit. One
    /// auto-discovered command, one SDK action per control via AddParameter.
    ///   Esc     → Escape keystroke — interrupts/stops Claude, exits a mode, dismisses a menu.
    ///             Sent as a real key (code 53), NOT typed text and NOT followed by Enter.
    ///   Plan    → Shift+Tab keystroke (toggles plan mode in the Claude Code TUI; it cycles modes)
    ///   Compact → "/compact" slash command
    ///   Context → "/context" slash command
    ///   Clear   → "/clear" slash command — resets the conversation
    ///   Exit    → "/exit" slash command — quits the Claude Code session
    /// </summary>
    public class ControlCommand : PluginDynamicCommand
    {
        private const String Esc = "esc";
        private const String Plan = "plan";
        private const String Compact = "compact";
        private const String Context = "context";
        private const String Clear = "clear";
        private const String Exit = "exit";

        public ControlCommand()
            : base()
        {
            this.AddParameter(Esc, "Esc", "Core");
            this.AddParameter(Plan, "Plan", "Core");
            this.AddParameter(Compact, "Compact", "Core");
            this.AddParameter(Context, "Context", "Core");
            this.AddParameter(Clear, "Clear", "Core");
            this.AddParameter(Exit, "Exit", "Core");
        }

        protected override void RunCommand(String actionParameter)
        {
            var bridge = BridgeManager.Instance;
            switch (actionParameter)
            {
                case Esc:
                    bridge.InjectKeystroke("key code 53"); // key code 53 = Escape
                    break;
                case Plan:
                    bridge.InjectKeystroke("key code 48 using {shift down}"); // key code 48 = Tab
                    break;
                case Compact:
                    bridge.SendPrompt("/compact");
                    break;
                case Context:
                    bridge.SendPrompt("/context");
                    break;
                case Clear:
                    bridge.SendPrompt("/clear");
                    break;
                case Exit:
                    bridge.SendPrompt("/exit");
                    break;
            }

            PluginLog.Info($"ControlCommand: {actionParameter}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            switch (actionParameter)
            {
                case Esc: return "Esc";
                case Plan: return "Plan";
                case Compact: return "Compact";
                case Context: return "Context";
                case Clear: return "Clear";
                case Exit: return "Exit";
                default: return actionParameter;
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            BitmapColor color;
            switch (actionParameter)
            {
                case Esc: color = KeyImage.Red; break;
                case Exit: color = KeyImage.Red; break;
                case Clear: color = KeyImage.Orange; break;
                case Plan: color = KeyImage.Purple; break;
                default: color = KeyImage.Slate; break; // Compact, Context
            }
            return KeyImage.Render(imageSize, this.GetCommandDisplayName(actionParameter, imageSize), color, actionParameter);
        }
    }
}
