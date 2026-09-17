namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;

    /// <summary>
    /// The OpenAI desktop app — /Applications/ChatGPT.app, bundle id com.openai.codex (yes:
    /// the ChatGPT app's bundle id names Codex), ONE app carrying both surfaces, ChatGPT and
    /// Codex, behind a mode switcher.
    ///
    /// Original labels were read off the LIVE app during the 2026-08-24 spike sitting
    /// (spikes/desktop-plugin/README.md → "Sitting completed"). Native Voice labels are documented
    /// candidates (see below); availability requires observing the actual exact buttons. The card
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

        // Window-title discovery is deliberately weaker than an Options+ application binding,
        // but safe for reconnaissance and dev testing: the helper refuses ambiguous matches.
        // Replace/augment with the confirmed executable identity after the Windows capture.
        public String[] WindowsWindowTitles => new[] { "ChatGPT", "Codex" };

        public String[] WindowsProcessNames => Array.Empty<String>(); // filled from W0 inspect output

        public String[] ApproveLabels => new[] { "Allow once" };

        public String[] DenyLabels => new[] { "Deny" };

        public String[] StopLabels => new[] { "Stop" };

        // Official UI labels, 2026-09-17: https://learn.chatgpt.com/docs/features/voice
        // User supplied the visible tooltip "Start Voice Chat" on 2026-09-17. AX matching
        // ignores case/whitespace and checks all semantic button labels; the tooltip does not
        // establish which AX attribute carries it. End labels still require live validation.
        // No substring or hotkey fallback. These apply to ChatGPT AND Codex.
        public String[] StartVoiceLabels => new[] { "Start voice chat", "Start new voice chat" };
        public String[] EndVoiceLabels => new[] { "Stop voice chat" };

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

        // Verified AX row controls: ChatGPT has pin; Codex has pin and archive.
        public Int32? ConversationIdleImages(String mode) => mode switch
        {
            "ChatGPT" => 1,
            "Codex" => 2,
            _ => null,
        };

        // The Review surface, from the live button inventory; first match wins. Verify on
        // hardware which of these opens vs toggles — fails safe (logged no-match) either way.
        public String[] ShowDiffLabels => this.ControlLabels(DesktopControl.Changes);

        public String[] ControlLabels(DesktopControl control) => control switch
        {
            DesktopControl.Search => new[] { "Search" },
            // Current Codex builds name this button "Changes +7 -2". Keep the earlier labels as
            // fallbacks so an app rollback does not strand the key.
            DesktopControl.Changes => new[] { "Changes +", "Changes -", "Toggle file diff", "Show files" },
            DesktopControl.Projects => new[] { "Projects" },
            DesktopControl.Plugins => new[] { "Plugins" },
            DesktopControl.AttachFiles => new[] { "Add files and more" },
            DesktopControl.Permissions => new[] { "Change permissions" },
            DesktopControl.Scheduled => new[] { "Scheduled" },
            DesktopControl.PullRequests => new[] { "Pull requests" },
            DesktopControl.Explore => new[] { "Explore" },
            DesktopControl.QuickChat => new[] { "Quick chat" },
            _ => Array.Empty<String>(),
        };

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
