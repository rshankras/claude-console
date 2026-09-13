namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    /// <summary>
    /// What the user is told, in Options+, when the plugin edits their Claude Code settings (#31).
    ///
    /// QA's first ask on #31 was "prompt before modifying user config". A keypad plugin runs inside
    /// a background service and cannot show a dialog — but it can post to Options+'s message centre
    /// through Plugin.OnPluginStatusChanged, with a link, and that is what Spotify and Zoom do to
    /// reach their users. So the edit is announced at the moment it happens, on the thing the user
    /// is looking at, with the undo one click away. Not consent; the nearest the platform offers.
    ///
    /// Pure text so the wording is testable, and so the engine can compose it without knowing
    /// which product it is running in — the product supplies the delivery (BridgeManager.Notify).
    /// </summary>
    internal static class BridgeNotice
    {
        /// <summary>Where "How to undo" points — the README section that explains the edit and the way back.</summary>
        // Options+ exposes this URL to every installed user, so it must not point at the private
        // engineering repository (#68). The card itself contains the immediate instruction; the
        // public product page is the stable home for support and release guidance.
        internal const String SupportUrl = "https://www.rshankar.com/keypad-profiles/#live-status-bridge";

        internal const String SupportTitle = "What was changed, and how to undo it";

        /// <summary>
        /// The message posted on the load that WROTE the wiring. Two sentences on purpose: a
        /// message-centre card is a notice, not the manual. The first version carried the backup's
        /// absolute path and the undo command as well — eight lines with the user's home directory
        /// in it, duplicating the button underneath. Those live in the README section the button
        /// opens (pinned by test), so the card says only what happened and that it is reversible.
        /// </summary>
        internal static String Wired(Int32 hookCount, Boolean settingsApplyLive) =>
            $"Claude Console added its status line and {hookCount} hooks to ~/.claude/settings.json so the " +
            (settingsApplyLive
                ? "live keys work — running sessions pick it up on their next activity. "
                : "live keys work from your next Claude Code session. ") +
            "Your own entries were kept and the previous file was backed up.";

        /// <summary>
        /// Posted once per load when Yes or No is pressed while the live keys are not set up. The
        /// answer keys see a permission prompt only through the PermissionRequest hook, which is
        /// part of the opt-in wiring — so a press then can do nothing, and the card says what would
        /// make it work instead of leaving a beep to explain itself (#58).
        /// </summary>
        internal const String AnswerNeedsSetupTitle = "How to turn the live keys on";

        internal static String AnswerNeedsSetup() =>
            "Yes and No can only see a permission prompt once the live keys are on: press Cost, Context or " +
            "Activity and choose Turn on (or press it twice). That adds 5 hooks and a status line to " +
            "~/.claude/settings.json; nothing changes until you do.";

        /// <summary>The message posted when Disable Live Status (or a marker found at load) removed the wiring.</summary>
        internal static String Unwired() =>
            "Claude Console removed its status line and hooks from ~/.claude/settings.json. Your own " +
            "entries were left alone and the previous file was backed up. The live keys read Off until " +
            "you enable them again.";

        /// <summary>
        /// The message posted by the FIRST press on a live key before live status is enabled: the
        /// prompt, with the change in it, before the change. The press itself changed nothing; the
        /// key only has room to flash "Press again", so this is where the disclosure lives.
        /// </summary>
        internal static String PressAgain(String keyName, Int32 seconds) =>
            $"Press {keyName} again within {seconds} s to add 5 hooks and a status line to ~/.claude/settings.json " +
            "so the live keys work. Your own entries are kept and the file is backed up first. Nothing changes " +
            "until then.";

        /// <summary>
        /// The dialog a first press opens where the product can show one: the same disclosure,
        /// phrased for two buttons ("Not now" / "Turn on") rather than a second press.
        /// </summary>
        internal static String TurnOnDialog(String keyName, Int32 seconds) =>
            "Turn on the live keys? This adds 5 hooks and a status line to ~/.claude/settings.json so " +
            "Cost, Context and Activity show live data. Your own entries are kept and the file is backed " +
            $"up first. (Pressing {keyName} again within {seconds} s also turns it on.)\n\n" +
            // Options+ cannot undo this edit when the plugin is uninstalled (#55): the moment of
            // consent is the one place the user is guaranteed to read how to take it out again.
            "To take it out later, hold a live key and choose Turn off — do that before uninstalling " +
            "the plugin, because uninstalling does not.";

        /// <summary>The dialog a long press opens once live status is on: the mirror question.</summary>
        internal static String TurnOffDialog(String keyName, Int32 seconds) =>
            "Turn off the live keys? This removes the plugin's 5 hooks and status line from ~/.claude/settings.json " +
            "and puts back a status line it had chained. Your own entries are untouched; the file is backed up " +
            $"first. (Holding {keyName} again within {seconds} s also turns it off.)";

        /// <summary>The card posted by a long press before anything is removed.</summary>
        internal static String LongPressAgain(String keyName, Int32 seconds) =>
            $"Hold {keyName} again within {seconds} s to remove the plugin's 5 hooks and status line from " +
            "~/.claude/settings.json. Your own entries are untouched and the file is backed up first. " +
            "Nothing changes until then.";

        /// <summary>The message posted when Enable could not touch settings.json at all.</summary>
        internal static String EnableFailed() =>
            "Claude Console could not edit ~/.claude/settings.json (it is a symlink, not valid JSON, or " +
            "kept changing). Nothing was written. Fix the file, then press the key again.";

        /// <summary>Where the Windows-Terminal notice points — the README's Windows section.</summary>
        internal const String WindowsTerminalUrl = "https://www.rshankar.com/keypad-profiles/#windows-terminal";

        internal const String WindowsTerminalTitle = "How to set Windows Terminal";

        /// <summary>
        /// Posted when a terminal-dependent key finds no Windows Terminal window to drive (#33 /
        /// retest item 16). The nav keys express every window/tab move as a `wt.exe` verb; with
        /// Claude Code in a classic console window and no Windows Terminal open, `wt.exe` either
        /// fails or acts on a window the user is not looking at, so the press was a silent no-op.
        /// The requirement is real (Claude Code itself renders poorly in conhost), but it must be
        /// VISIBLE rather than silent. Typing keys are unaffected — say so, so the user does not
        /// think the whole plugin is dead. <paramref name="detail"/> is the platform's reason.
        /// </summary>
        internal static String WindowsTerminalRequired(String detail) =>
            "The navigation keys need an open Windows Terminal window, and it isn't the current terminal. " +
            "Direct typing keys, Yes/No and voice still work here. Set Settings › System › For developers › " +
            $"Terminal to Windows Terminal and start a new session. ({detail})";
    }
}
