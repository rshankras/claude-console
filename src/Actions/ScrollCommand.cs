namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// Scroll keys (group "Scroll") — read back through the Claude Code conversation from the
    /// keypad. One auto-discovered command, one SDK action per direction via AddParameter.
    ///   Scroll Up   → Page Up   keystroke (key code 116)
    ///   Scroll Down → Page Down keystroke (key code 121)
    ///
    /// These work in BOTH Claude Code rendering modes:
    ///   • Classic (default) — Claude Code leaves the transcript in Terminal.app's scrollback and
    ///     does NOT grab Page Up/Down, so the keys scroll Terminal natively.
    ///   • Fullscreen (/tui fullscreen) — Claude Code intercepts Page Up/Down and scrolls its own
    ///     alternate-screen buffer by half a screen.
    /// Either way one press = one page back/forward. Sent as raw System Events key codes to the
    /// focused terminal (same path as the Esc / arrow / Return keys); needs Accessibility, which the
    /// plugin already requires. Classic mode relies on Terminal's scrollback, so keep a generous
    /// scrollback limit (Terminal ▸ Settings ▸ Profiles ▸ Window ▸ Scrollback).
    /// </summary>
    public class ScrollCommand : PluginDynamicCommand
    {
        private const String Up = "scroll_up";
        private const String Down = "scroll_down";

        public ScrollCommand()
            : base()
        {
            this.AddParameter(Up, "Scroll Up", "Scroll");
            this.AddParameter(Down, "Scroll Down", "Scroll");
        }

        protected override void RunCommand(String actionParameter)
        {
            var bridge = BridgeManager.Instance;
            switch (actionParameter)
            {
                case Up:
                    bridge.InjectKeystroke("key code 116"); // key code 116 = Page Up
                    break;
                case Down:
                    bridge.InjectKeystroke("key code 121"); // key code 121 = Page Down
                    break;
            }

            PluginLog.Info($"ScrollCommand: {actionParameter}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            switch (actionParameter)
            {
                case Up: return "Scroll Up";
                case Down: return "Scroll Down";
                default: return actionParameter;
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            // Icon basename == actionParameter (scroll_up / scroll_down .png in Resources/icons),
            // matching ControlCommand / AnswerCommand. Falls back to the label text if missing.
            return KeyImage.Render(imageSize, this.GetCommandDisplayName(actionParameter, imageSize), KeyImage.Blue, actionParameter);
        }
    }
}
