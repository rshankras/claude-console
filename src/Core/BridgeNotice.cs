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
        internal const String SupportUrl = "https://github.com/rshankras/claude-console#the-live-status-bridge";

        internal const String SupportTitle = "What was changed, and how to undo it";

        /// <summary>
        /// The message posted on the load that WROTE the wiring. Two sentences on purpose: a
        /// message-centre card is a notice, not the manual. The first version carried the backup's
        /// absolute path and the undo command as well — eight lines with the user's home directory
        /// in it, duplicating the button underneath. Those live in the README section the button
        /// opens (pinned by test), so the card says only what happened and that it is reversible.
        /// </summary>
        internal static String Wired(Int32 hookCount) =>
            $"Claude Console added its status line and {hookCount} hooks to ~/.claude/settings.json so the " +
            "live keys work from your next Claude Code session. Your own entries were kept and the previous " +
            "file was backed up.";

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
            $"up first. (Pressing {keyName} again within {seconds} s also turns it on.)";

        /// <summary>The message posted when Enable could not touch settings.json at all.</summary>
        internal static String EnableFailed() =>
            "Claude Console could not edit ~/.claude/settings.json (it is a symlink, not valid JSON, or " +
            "kept changing). Nothing was written. Fix the file, then press Enable Live Status again.";
    }
}
