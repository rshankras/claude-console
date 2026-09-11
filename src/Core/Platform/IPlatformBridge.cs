namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Everything the plugin needs from the host operating system, and nothing it doesn't.
    ///
    /// BridgeManager owns the platform-NEUTRAL half — the poll loop, pin-vs-frontmost targeting,
    /// slot assignment, project matching, voice lifecycle, settings auto-wire policy — and calls
    /// through this interface for anything that touches the OS. macOS drives Terminal.app with
    /// AppleScript and keys sessions by TTY; Windows attaches to a console and keys sessions by
    /// the Claude process. Above this line, neither of those facts is visible.
    ///
    /// SESSION KEYS ARE OPAQUE. A session key is a token minted by the platform bridge
    /// (macOS: "ttys003"; Windows: "pid-1234-637..."). Nothing above this interface may parse one.
    /// Two hard requirements on the format, because the id is used as an IPC filename and is
    /// persisted across restarts:
    ///   • filename-safe — it becomes "&lt;id&gt;.json" under sessions/ and activity/
    ///   • stable for the life of the session, and never reused by a later session
    ///
    /// THE INJECTION GUARANTEE. Every Inject* implementation must focus the target session and
    /// type in ONE indivisible operation, and must type NOTHING if the target can't be reached —
    /// returning the reason instead. A keypress may never land in another application. On macOS
    /// that is a single osascript run; on Windows, a console-targeted WriteConsoleInput (which
    /// cannot reach another app by construction). An implementation that focuses, returns, and
    /// then types is a bug however convenient it looks.
    /// </summary>
    internal interface IPlatformBridge
    {
        /// <summary>Short name for logs, e.g. "macOS" / "Windows".</summary>
        String Name { get; }

        /// <summary>
        /// False when running on this OS but without a working backend (wrong OS build, missing
        /// terminal). Callers degrade gracefully rather than throwing.
        /// </summary>
        Boolean IsSupported { get; }

        /// <summary>
        /// Whether a Claude Code session that is ALREADY RUNNING picks up an edit to the user's
        /// settings file — the hooks and status line the live keys depend on — without being
        /// restarted. Decides the wording after Turn on: "Turned on" where the keys come alive on
        /// the session's next activity, "Restart Claude" where they do not (#58).
        ///
        /// Verified true on macOS on 2026-09-02 (Claude Code 2.1.258): a session started with no
        /// hooks wrote its status line one second after Turn on, and its PermissionRequest hook
        /// fired three minutes later, with no restart. Verified true on Windows on 2026-09-10 and
        /// 2026-09-11 (Claude Code 2.1.267/268): sessions started hours earlier reported within
        /// 5–37 s of the write and their PermissionRequest hook fired minutes later, no restart.
        /// Logitech QA's Windows report of "nothing until Claude Code is restarted" (retest item 2)
        /// was #74 — the hook wrote a word the plugin never read — not a settings-reload difference.
        /// Both platforms therefore say "Turned on"; the property stays on the seam because a
        /// future platform (or agent) may well need the restart wording.
        /// </summary>
        Boolean SettingsApplyLive { get; }

        // ------------------------------------------------------------------------------------
        // Discovery
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Session ids of every live Claude session right now, or null if the scan could not run
        /// (a null is "I don't know", NOT "none" — the registry keeps its last known set, so a
        /// transient failure must never reap live sessions).
        /// </summary>
        HashSet<String> DiscoverSessions();

        /// <summary>
        /// The session id of the terminal tab the user is looking at, or null when the terminal
        /// isn't frontmost / isn't running. Called on the poll timer, so it must be cheap and
        /// hard-bounded.
        /// </summary>
        String QueryFrontmostSession();

        // ------------------------------------------------------------------------------------
        // Injection — all session-targeted and guarded (see the guarantee above)
        // ------------------------------------------------------------------------------------

        /// <summary>Type text into the target session, optionally submitting it.</summary>
        /// <param name="sessionKey">Target, or null/empty to use the terminal's front tab.</param>
        InjectionOutcome InjectText(String sessionKey, String text, Boolean pressEnter);

        /// <summary>Send one key chord to the target session.</summary>
        InjectionOutcome InjectKey(String sessionKey, KeyStroke key);

        /// <summary>
        /// Accept the highlighted autocomplete and submit it: Tab, settle, Return. One press,
        /// one guarded operation — deliberately not two InjectKey calls, which would let an app
        /// switch slip between them.
        /// </summary>
        InjectionOutcome InjectTabThenEnter(String sessionKey);

        // ------------------------------------------------------------------------------------
        // Focus and navigation — application-level, not session-targeted
        // ------------------------------------------------------------------------------------

        /// <summary>Bring a specific session's tab to the front.</summary>
        void FocusSession(String sessionKey);

        /// <summary>Drive a terminal navigation gesture (new tab, cycle windows, …).</summary>
        void Navigate(TerminalAction action);

        /// <summary>
        /// Open a terminal at <paramref name="projectDir"/> and start claude there, reusing an
        /// idle tab when there is one and never typing into a busy session.
        /// </summary>
        void LaunchClaudeInProject(String projectDir);

        /// <summary>
        /// Interactive screenshot: the user drags a region (or picks a window) and the capture
        /// lands at <paramref name="outputPath"/>. BLOCKS until they finish or cancel — callers
        /// run it off the action thread. Returns false when cancelled or unsupported; true only
        /// if the file exists. First use may cost a one-time Screen Recording grant for the
        /// plugin service, the same shape as voice's Microphone grant.
        /// </summary>
        Boolean CaptureScreenshotInteractive(String outputPath);

        /// <summary>
        /// Open a terminal and start the agent CLI with <paramref name="extraArgs"/> appended —
        /// same idle-tab reuse as <see cref="LaunchClaudeInProject"/>, but no cd: for launches
        /// whose input is a file (Codex `-i image.png`), not a project. Args are discrete argv
        /// elements so each backend applies its own quoting — a joined string would have to be
        /// re-split, and Windows temp paths contain spaces.
        /// </summary>
        void LaunchAgentSession(String[] extraArgs);

        /// <summary>Audible "that didn't work" — the plugin's only out-of-band signal.</summary>
        void Alert();
    }
}
