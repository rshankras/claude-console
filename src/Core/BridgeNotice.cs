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
        /// <summary>
        /// Where "How to undo" points — the product page's section that explains the edit and the
        /// way back. On vizhi.dev, not the GitHub README: the repository went private on 2026-09-02
        /// and every card button returned a 404 until it was reopened (#68, #71), and it will go
        /// private again. These anchors are baked into shipped packages — treat them as frozen.
        /// </summary>
        internal const String SupportUrl = "https://vizhi.dev/claude-console/#live-status-bridge";

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
            "To take it out later, hold a live key and choose Turn off. Windows uninstall removes the " +
            "plugin's entries; on macOS the hooks remove them after the plugin has been missing for " +
            "over a minute. Reinstalling on Windows restores your previous live-status setup; turn " +
            "it off first if you want it to stay off.";

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

        /// <summary>Where the Windows-Terminal notice points — the FAQ's Windows section.</summary>
        internal const String WindowsTerminalUrl = "https://vizhi.dev/faq/#windows";

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
            // "Yes/No and voice still work here" was the old wording, and QA caught it mid-way through
            // a run in which neither did (2.2.1 Windows retest, item 16): they do not need Windows
            // Terminal, but they do need a target session, and a refused press says so on the key.
            "The navigation keys need an open Windows Terminal window, and it isn't the current terminal. " +
            "Direct typing keys, Yes/No and voice do not need it; if one is refused, pin a session slot. " +
            $"Set Settings › System › For developers › Terminal to Windows Terminal and start a new session. ({detail})";

        /// <summary>Where the voice notices point — the FAQ's voice section.</summary>
        internal const String VoiceUrl = "https://vizhi.dev/faq/#voice";

        internal const String VoiceTitle = "How voice works";

        /// <summary>
        /// Posted once when the speech model download starts. A first voice press used to say
        /// "Model loading" for two seconds and nothing else, while 142 MB fetched in the background —
        /// a silent multi-minute cliff on a slow connection (2.2.1 Windows retest, item 6).
        /// </summary>
        internal static String VoiceModelDownloading() =>
            "Voice is downloading its speech model (142 MB, one time). Until it finishes, a voice key says " +
            "Model loading — press it again afterwards. On a slow connection this can take several minutes.";

        internal static String VoiceModelReady() =>
            "The speech model is downloaded. Voice, Voice Draft and Go to Project are ready.";

        internal static String VoiceModelDownloadFailed(String reason) =>
            $"Voice could not download its speech model ({reason}). Check the connection, then press a voice " +
            "key again to retry.";

        /// <summary>
        /// Posted once per load when Go to Project matches nothing. The only mention of the roots
        /// file used to be a WARN line in the log (2.2.1 Windows retest, item 8).
        /// </summary>
        internal static String ProjectNoMatch(String heard, Int32 candidates, String source, String rootsFile) =>
            $"Go to Project heard \"{heard}\" but found no project like it among {candidates} candidate(s) ({source}). " +
            $"List project parent folders one per line in {rootsFile}. A nonempty list replaces automatic discovery; " +
            "clear the list to search automatically, or add the missing parent folder and try again.";
    }
}
