namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// Session control keys (group "Core"): Esc, Mode, Tab, Compact, Clear, Exit. One auto-discovered
    /// command, one SDK action per control via AddParameter.
    ///   Esc     → Escape keystroke — interrupts/stops Claude, exits a mode, dismisses a menu.
    ///             Sent as a real key (code 53), NOT typed text and NOT followed by Enter.
    ///   Mode    → Shift+Tab keystroke — cycles Claude Code's input modes
    ///             (normal → auto-accept edits → plan). Action id stays "plan".
    ///   Tab     → Tab then Return in one press — accepts the highlighted autocomplete AND submits
    ///             (e.g. complete a slash command and run it). Distinct from Mode's Shift+Tab.
    ///   Compact → "/compact" slash command
    ///   Clear   → "/clear" slash command — resets the conversation
    ///   Exit    → "/exit" slash command — quits the Claude Code session
    /// (Context lives on its own gauge key — see ContextCommand.)
    /// </summary>
    public class ControlCommand : PluginDynamicCommand
    {
        private const String Esc = "esc";
        private const String Mode = "plan"; // action id kept as "plan" so existing key bindings survive the relabel
        private const String Tab = "tab";
        private const String Compact = "compact";
        private const String Clear = "clear";
        private const String Exit = "exit";

        public ControlCommand()
            : base()
        {
            this.AddParameter(Esc, "Esc", "Core");
            this.AddParameter(Mode, "Mode", "Core");
            this.AddParameter(Tab, "Tab", "Core");
            this.AddParameter(Compact, "Compact", "Core");
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
                case Mode:
                    bridge.InjectKeystroke("key code 48 using {shift down}"); // Shift+Tab — cycle input modes
                    break;
                case Tab:
                    bridge.InjectTabThenEnter(); // Tab (accept autocomplete) + Return (submit), one press
                    break;
                case Compact:
                    bridge.SendPrompt("/compact");
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
                case Mode: return "Mode";
                case Tab: return "Tab";
                case Compact: return "Compact";
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
                case Mode: color = KeyImage.Purple; break;
                default: color = KeyImage.Slate; break; // Compact, Tab
            }
            return KeyImage.Render(imageSize, this.GetCommandDisplayName(actionParameter, imageSize), color, actionParameter);
        }
    }
}
