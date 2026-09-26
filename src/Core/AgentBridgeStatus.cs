namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    /// <summary>
    /// Whether an agent-owned lifecycle bridge can currently feed the live keys. This is separate
    /// from Claude Code's opt-in settings-file wiring: Codex owns its hooks file and requires the
    /// user to trust it, so it needs guidance without acquiring Claude's Enable/Disable controls.
    /// </summary>
    internal enum AgentBridgeStatus
    {
        Ready = 0,
        AwaitingTrust = 1,
        InstallFailed = 2,
        ForeignConfiguration = 3,
        HelperUnavailable = 4,
    }

    /// <summary>User-facing words for an agent bridge that needs attention (#69).</summary>
    internal static class AgentBridgeNotice
    {
        // Every user-facing link lives on vizhi.dev: the source repository is closed, and this card
        // appears exactly when someone is stuck on hook trust — the worst moment to send them
        // somewhere that is not the product's own documentation.
        internal const String PublicHelpUrl = "https://vizhi.dev/vizhi-codex/#hooks";

        internal static String FaceLabel(AgentBridgeStatus status) =>
            status switch
            {
                AgentBridgeStatus.AwaitingTrust => "Run /hooks",
                AgentBridgeStatus.InstallFailed => "Setup failed",
                AgentBridgeStatus.ForeignConfiguration => "Merge hooks",
                AgentBridgeStatus.HelperUnavailable => "Blocked",
                _ => null,
            };

        internal static String Title(AgentBridgeStatus status) =>
            status switch
            {
                AgentBridgeStatus.AwaitingTrust => "Trust the Vizhi hooks in Codex",
                AgentBridgeStatus.InstallFailed => "Vizhi could not install its Codex hooks",
                AgentBridgeStatus.ForeignConfiguration => "Vizhi left your Codex hooks unchanged",
                AgentBridgeStatus.HelperUnavailable => "Windows hook helper unavailable",
                _ => null,
            };

        internal static String Message(AgentBridgeStatus status) =>
            status switch
            {
                AgentBridgeStatus.AwaitingTrust =>
                    "Live session state and approval indicators need one Codex step: run /hooks, review the Vizhi entries, and trust them. Never use --dangerously-bypass-hook-trust.",
                AgentBridgeStatus.InstallFailed =>
                    "Vizhi could not install its Codex lifecycle hooks. Prompt, navigation and voice actions still work, but live session state and approval indicators are unavailable. Reinstall the plugin, then run /hooks in Codex.",
                AgentBridgeStatus.ForeignConfiguration =>
                    "Vizhi found a hooks.json file it does not own and left it unchanged. Prompt, navigation and voice actions still work. Merge the Vizhi hook entries manually to enable live session state and approval indicators.",
                AgentBridgeStatus.HelperUnavailable =>
                    "The Windows hook helper is missing or fresh hook delivery cannot be verified. Live values and approval actions are blocked. Check security software or contact IT, then trigger a fresh hook event after recovery.",
                _ => null,
            };
    }
}
