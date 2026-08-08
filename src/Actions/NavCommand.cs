namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// App / session navigation keys (group "Terminal"). The gestures are named here; how each
    /// one is performed is the platform backend's business (see IPlatformBridge.Navigate):
    ///   Terminal   → bring Terminal to the front (replaces Cmd+Tab → Terminal)
    ///   New Tab    → open a new Terminal tab — a fresh shell (Cmd+T)
    ///   New Claude → open a new Terminal tab AND run `claude` in it (one press = new session)
    ///   Next Tab   → switch to the next Terminal tab / session (Ctrl+Tab)
    ///   Prev Tab   → switch to the previous Terminal tab / session (Ctrl+Shift+Tab)
    ///   New Claude (Window) → open a new Terminal WINDOW running `claude` (do script, no target)
    ///   Next Window → cycle to the next Terminal window (Cmd+`)
    ///   Prev Window → cycle to the previous Terminal window (Cmd+Shift+`)
    ///
    /// Tab/window keys activate the terminal first (so they work from any app) then send its own
    /// shortcut. Windows are for people who prefer separate windows over tabs.
    /// </summary>
    public class NavCommand : PluginDynamicCommand
    {
        private const String Terminal = "terminal";
        private const String NewTab = "new_tab";
        private const String NewClaude = "new_claude";
        private const String NextTab = "next_tab";
        private const String PrevTab = "prev_tab";
        private const String NewClaudeWindow = "new_claude_window";
        private const String NextWindow = "next_window";
        private const String PrevWindow = "prev_window";

        public NavCommand()
            : base()
        {
            this.AddParameter(Terminal, "Terminal", "Terminal")
                .SetDescription("Bring Terminal.app to the front");
            this.AddParameter(NewTab, "New Tab", "Terminal")
                .SetDescription("Open a new Terminal tab (a fresh shell)");
            this.AddParameter(NewClaude, "New Claude", "Terminal")
                .SetDescription("Open a new Terminal tab and start a claude session");
            this.AddParameter(NextTab, "Next Tab", "Terminal")
                .SetDescription("Switch to the next Terminal tab");
            this.AddParameter(PrevTab, "Prev Tab", "Terminal")
                .SetDescription("Switch to the previous Terminal tab");
            this.AddParameter(NewClaudeWindow, "New Claude (Window)", "Terminal")
                .SetDescription("Open a new Terminal window and start a claude session");
            this.AddParameter(NextWindow, "Next Window", "Terminal")
                .SetDescription("Switch to the next Terminal window (Cmd+`)");
            this.AddParameter(PrevWindow, "Prev Window", "Terminal")
                .SetDescription("Switch to the previous Terminal window (Cmd+Shift+`)");
        }

        protected override void RunCommand(String actionParameter)
        {
            var bridge = BridgeManager.Instance;
            switch (actionParameter)
            {
                case Terminal:
                    bridge.Navigate(TerminalAction.Activate);
                    break;
                case NewTab:
                    bridge.Navigate(TerminalAction.NewTab);
                    break;
                case NewClaude:
                    bridge.Navigate(TerminalAction.NewClaudeTab);
                    break;
                case NextTab:
                    bridge.Navigate(TerminalAction.NextTab);
                    break;
                case PrevTab:
                    bridge.Navigate(TerminalAction.PreviousTab);
                    break;
                case NewClaudeWindow:
                    bridge.Navigate(TerminalAction.NewClaudeWindow);
                    break;
                case NextWindow:
                    bridge.Navigate(TerminalAction.NextWindow);
                    break;
                case PrevWindow:
                    bridge.Navigate(TerminalAction.PreviousWindow);
                    break;
            }

            PluginLog.Info($"NavCommand: {actionParameter}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            switch (actionParameter)
            {
                case Terminal: return "Terminal";
                case NewTab: return "New Tab";
                case NewClaude: return "New Claude";
                case NextTab: return "Next Tab";
                case PrevTab: return "Prev Tab";
                case NewClaudeWindow: return "New Claude (Window)";
                case NextWindow: return "Next Window";
                case PrevWindow: return "Prev Window";
                default: return actionParameter;
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            return KeyImage.Render(imageSize, this.GetCommandDisplayName(actionParameter, imageSize), KeyImage.Slate, actionParameter);
        }
    }
}
