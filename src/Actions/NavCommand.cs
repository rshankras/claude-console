namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// App / session navigation keys (group "Terminal"), all targeting Terminal.app via AppleScript:
    ///   Terminal   → bring Terminal to the front (replaces Cmd+Tab → Terminal)
    ///   New Tab    → open a new Terminal tab — a fresh shell (Cmd+T)
    ///   New Claude → open a new Terminal tab AND run `claude` in it (one press = new session)
    ///   Next Tab   → switch to the next Terminal tab / session (Ctrl+Tab)
    ///   Prev Tab   → switch to the previous Terminal tab / session (Ctrl+Shift+Tab)
    ///   New Claude (Window) → open a new Terminal WINDOW running `claude` (do script, no target)
    ///   Next Window → cycle to the next Terminal window (Cmd+`)
    ///   Prev Window → cycle to the previous Terminal window (Cmd+Shift+`)
    ///
    /// Tab/window keys activate Terminal first (so they work from any app) then send Terminal's own
    /// shortcut. Windows are for people who prefer separate windows over tabs. Terminal-specific for
    /// now; tell me if you switch terminals.
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

        // Bring Terminal to the front, then send a key chord to it.
        private const String ActivateThen =
            "tell application \"Terminal\" to activate\n" +
            "delay 0.05\n" +
            "tell application \"System Events\" to ";

        // Open a new tab, then run `claude` in it via `do script` (reliable command send — no
        // per-character keystroke timing). delay lets the new tab become front first.
        private const String NewClaudeScript =
            "tell application \"Terminal\"\n" +
            "  activate\n" +
            "  tell application \"System Events\" to keystroke \"t\" using command down\n" +
            "  delay 0.5\n" +
            "  do script \"claude\" in front window\n" +
            "end tell";

        // New WINDOW running `claude`: `do script` with no "in" target opens a fresh window.
        private const String NewClaudeWindowScript =
            "tell application \"Terminal\"\n" +
            "  activate\n" +
            "  do script \"claude\"\n" + // no target → new window
            "end tell";

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
                    bridge.RunAppleScript("tell application \"Terminal\" to activate");
                    break;
                case NewTab:
                    bridge.RunAppleScript(ActivateThen + "keystroke \"t\" using {command down}"); // Cmd+T
                    break;
                case NewClaude:
                    bridge.RunAppleScript(NewClaudeScript);
                    break;
                case NextTab:
                    bridge.RunAppleScript(ActivateThen + "key code 48 using {control down}"); // Ctrl+Tab
                    break;
                case PrevTab:
                    bridge.RunAppleScript(ActivateThen + "key code 48 using {control down, shift down}"); // Ctrl+Shift+Tab
                    break;
                case NewClaudeWindow:
                    bridge.RunAppleScript(NewClaudeWindowScript);
                    break;
                case NextWindow:
                    bridge.RunAppleScript(ActivateThen + "key code 50 using {command down}"); // Cmd+` — cycle windows
                    break;
                case PrevWindow:
                    bridge.RunAppleScript(ActivateThen + "key code 50 using {command down, shift down}"); // Cmd+Shift+`
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
