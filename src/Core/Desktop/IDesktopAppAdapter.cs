namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;

    /// <summary>
    /// Everything the plugin needs to know about the DESKTOP APP it is driving, and nothing it
    /// doesn't. The third seam: <c>IPlatformBridge</c> hides the OS, <c>IAgentAdapter</c> hides
    /// the terminal agent, this hides *which GUI app* — the AX helper itself is app-agnostic and
    /// receives every one of these strings as arguments. A second desktop app (Claude Desktop is
    /// the expected one) must cost an adapter, never a helper fork.
    ///
    /// The values here are UI copy read off a living app, not API contracts. They WILL drift
    /// with app updates and they differ per localization. The rules for implementers:
    /// label arrays list candidates (match is contains, case-insensitive, first wins), and a
    /// label that stops matching must degrade to a hidden/grey key — never to pressing whatever
    /// matched something else.
    /// </summary>
    internal interface IDesktopAppAdapter
    {
        /// <summary>Stable id for logs, e.g. "openai-desktop".</summary>
        String Id { get; }

        /// <summary>Human name for logs and listings.</summary>
        String DisplayName { get; }

        /// <summary>
        /// The app's name as it should appear ON A KEY — short enough to read at key size, and
        /// the name the user calls it in their Dock, not our product name. "Show ChatGPT" tells
        /// you where the key takes you; "Show App" makes you guess.
        /// </summary>
        String ShortName { get; }

        /// <summary>macOS bundle id the helper attaches to, e.g. "com.openai.codex".</summary>
        String BundleId { get; }

        /// <summary>macOS process name, for the Options+ application binding.</summary>
        String MacProcessName { get; }

        /// <summary>
        /// Visible top-level window titles used to discover the app on Windows without guessing
        /// an executable identity. The UIA reconnaissance verb reports the real process name;
        /// packaging remains Windows-disabled until that identity is confirmed on hardware.
        /// </summary>
        String[] WindowsWindowTitles { get; }

        /// <summary>
        /// Confirmed Windows executable names without .exe. Empty until reconnaissance proves
        /// them; the helper then uses visible titles and refuses ambiguity instead of guessing.
        /// </summary>
        String[] WindowsProcessNames { get; }

        /// <summary>Approval card: labels that mean "approve this once".</summary>
        String[] ApproveLabels { get; }

        /// <summary>Approval card: labels that mean "deny".</summary>
        String[] DenyLabels { get; }

        /// <summary>Labels that stop/interrupt the running task.</summary>
        String[] StopLabels { get; }

        /// <summary>The composer's submit control.</summary>
        String SendLabel { get; }

        /// <summary>Starts a fresh conversation.</summary>
        String NewChatLabel { get; }

        /// <summary>
        /// Substring that appears somewhere in the UI exactly when the app wants the user —
        /// the cheap single-label poll the whole approval light rides on.
        /// </summary>
        String AttentionMarker { get; }

        /// <summary>
        /// Prefix of the mode switcher's label; what follows it is the current mode name.
        /// Empty when the app has no mode concept.
        /// </summary>
        String ModePrefix { get; }

        /// <summary>The mode switcher control itself.</summary>
        String ModeSwitcherLabel { get; }

        /// <summary>
        /// Menu item labels per mode, keyed by the mode name the switcher reports. Pressing the
        /// switcher opens a menu; pressing one of these selects that mode.
        /// </summary>
        String ModeMenuLabel(String modeName);

        /// <summary>The mode names this app can switch between, in toggle order.</summary>
        String[] ModeNames { get; }

        /// <summary>
        /// The per-row control whose presence identifies a sidebar CONVERSATION (e.g. "Pin chat")
        /// — how the helper tells conversation rows from every other button. Empty disables
        /// conversation reading entirely.
        /// </summary>
        String ConversationItemMarker { get; }

        /// <summary>The literal row text meaning "this conversation awaits your approval".</summary>
        String ConversationAwaitingText { get; }

        /// <summary>The literal row text meaning "finished, result unseen".</summary>
        String ConversationUnreadText { get; }

        /// <summary>Verified macOS idle row image count per mode; null disables spinner inference.</summary>
        Int32? ConversationIdleImages(String mode) => null;

        /// <summary>Controls that open the current task's diff/review surface. Empty hides the key.</summary>
        String[] ShowDiffLabels { get; }

        /// <summary>
        /// Accessibility labels for a contextual control. Empty means this app cannot expose
        /// that control. Labels remain app knowledge; the platform helpers stay app-agnostic.
        /// </summary>
        String[] ControlLabels(DesktopControl control);

        /// <summary>What this app's UI honestly exposes. Keys hide where a capability is false.</summary>
        DesktopCapabilities Capabilities { get; }
    }
}
