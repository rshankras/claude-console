namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;

    /// <summary>
    /// The OpenAI desktop app — /Applications/ChatGPT.app, bundle id com.openai.codex (yes:
    /// the ChatGPT app's bundle id names Codex), ONE app carrying both surfaces, ChatGPT and
    /// Codex, behind a mode switcher.
    ///
    /// Every string below is UI copy read off the LIVE app during the 2026-08-24 spike sitting
    /// (spikes/desktop-plugin/README.md → "Sitting completed"), not guessed from docs. The card
    /// labels matched Claude Desktop's exactly (Deny / Allow once), which is what makes a shared
    /// engine plausible; the attention marker is the sidebar toggle's relabel, the single
    /// cheapest honest "the app wants you" signal we found.
    ///
    /// "Always allow" exists in the app (behind the Approval options popup) and is DELIBERATELY
    /// absent here: standing permission should never be one elbow away on a hardware key.
    /// </summary>
    internal sealed class OpenAiDesktopAdapter : IDesktopAppAdapter
    {
        public String Id => "openai-desktop";

        public String DisplayName => "ChatGPT (Codex)";

        public String ShortName => "ChatGPT";

        public String BundleId => "com.openai.codex";

        public String MacProcessName => "ChatGPT";

        public String[] ApproveLabels => new[] { "Allow once" };

        public String[] DenyLabels => new[] { "Deny" };

        public String[] StopLabels => new[] { "Stop" };

        public String SendLabel => "Send";

        public String NewChatLabel => "New chat";

        public String AttentionMarker => "needs attention";

        public String ModePrefix => "Switch mode, current mode: ";

        public String ModeSwitcherLabel => "Switch mode";

        public String[] ModeNames => new[] { "ChatGPT", "Codex" };

        // Sidebar rows: every conversation carries a "Pin chat" control; the state texts are the
        // ones observed ON the row during the 2026-08-25 lifecycle recon (task running → card up).
        public String ConversationItemMarker => "Pin chat";

        public String ConversationAwaitingText => "Awaiting approval";

        public String ConversationUnreadText => "Unread";

        // The menu items pair each mode name with its tagline; match on the distinctive full
        // label, not the bare mode word — "Codex" alone would also match the switcher itself.
        public String ModeMenuLabel(String modeName) => modeName switch
        {
            "ChatGPT" => "ChatGPT Create, learn, and explore",
            "Codex" => "Codex Build, debug, and ship",
            _ => null,
        };

        public DesktopCapabilities Capabilities => new DesktopCapabilities
        {
            ApprovalSignal = true,
            Attention = true,
            Stop = true,
            ModeSwitch = true,
            ComposerWrite = true,
        };
    }
}
