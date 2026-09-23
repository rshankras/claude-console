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

        /// <summary>The app's own current-conversation marker. The macOS helper reads
        /// AXARIACurrent (aria-current="page") before the legacy AXSelected fallback.</summary>
        public Boolean Selected { get; init; }
    }

    /// <summary>
    /// Sidebar status, preferring exact adapter labels (Awaiting approval, Unread/Complete,
    /// Thinking). Older app builds expose running only through an unnamed activity image.
    /// Unread is also the transport state for an explicit Complete badge on a read conversation.
    /// </summary>
    internal enum ConversationState
    {
        Idle = 0,
        Running,
        Unread,
        Awaiting,
    }
}
