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
    ///
    /// Tab keys activate Terminal first (so they work from any app) then send Terminal's own
    /// shortcut. Terminal-specific for now; tell me if you switch terminals.
    /// </summary>
    public class NavCommand : PluginDynamicCommand
    {
        private const String Terminal = "terminal";
        private const String NewTab = "new_tab";
        private const String NewClaude = "new_claude";
        private const String NextTab = "next_tab";
        private const String PrevTab = "prev_tab";

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

        public NavCommand()
            : base()
        {
            this.AddParameter(Terminal, "Terminal", "Terminal");
            this.AddParameter(NewTab, "New Tab", "Terminal");
            this.AddParameter(NewClaude, "New Claude", "Terminal");
            this.AddParameter(NextTab, "Next Tab", "Terminal");
            this.AddParameter(PrevTab, "Prev Tab", "Terminal");
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
                default: return actionParameter;
            }
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            return KeyImage.Render(imageSize, this.GetCommandDisplayName(actionParameter, imageSize), KeyImage.Slate, actionParameter);
        }
    }
}
