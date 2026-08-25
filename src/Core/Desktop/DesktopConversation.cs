namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;

    /// <summary>
    /// One sidebar conversation as the app shows it: its title and what it wants from you.
    /// This is the unit the appendix's home page is made of — "Q3 report · needs input", by
    /// name, at a glance — and the whole argument against a keypad that can only glow.
    /// </summary>
    internal sealed class DesktopConversation
    {
        public String Title { get; init; } = "";
        public ConversationState State { get; init; }

        /// <summary>The app's own selection marker, when it exposes one. The current OpenAI app
        /// build reports none (checked live: button and two ancestors all false) — kept because
        /// it costs nothing and an app update could start answering.</summary>
        public Boolean Selected { get; init; }
    }

    /// <summary>
    /// Verified against the live app (2026-08-25): Awaiting and Unread are literal texts on the
    /// sidebar row; Running is inferred from the row's activity spinner (an image with no text —
    /// the one state the app does not name, which is why it ranks below the text states).
    /// </summary>
    internal enum ConversationState
    {
        Idle = 0,
        Running,
        Unread,
        Awaiting,
    }
}
