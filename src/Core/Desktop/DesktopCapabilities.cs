namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;

    /// <summary>
    /// What a desktop app's UI honestly exposes — the desktop twin of <c>AgentCapabilities</c>,
    /// under the same law: NEVER INVENT A VALUE. A key whose capability is false hides or greys;
    /// it does not render a guess. (This is why there is no Cost and no Context member at all:
    /// no desktop surface has ever exposed either, and adding the field before an app exposes
    /// it would be an invitation to lie.)
    ///
    /// A capability is about what the APP exposes, not what we've implemented. If the app's UI
    /// gains an honest signal tomorrow, exactly one boolean changes.
    /// </summary>
    internal readonly struct DesktopCapabilities
    {
        /// <summary>An approval card with distinct approve/deny controls, readable before pressing.</summary>
        public Boolean ApprovalSignal { get; init; }

        /// <summary>A single UI marker that flips when the app wants the user (the cheap poll).</summary>
        public Boolean Attention { get; init; }

        /// <summary>A stop/interrupt control appears while a task runs — presence doubles as "working".</summary>
        public Boolean Stop { get; init; }

        /// <summary>A mode switcher whose label names the current mode.</summary>
        public Boolean ModeSwitch { get; init; }

        /// <summary>The composer accepts an external write (AX value / selected-text insert).</summary>
        public Boolean ComposerWrite { get; init; }
    }

    /// <summary>
    /// Controls visible in the focused desktop-app window on the latest snapshot. Unlike
    /// <see cref="DesktopCapabilities"/> (what an adapter can ever support), these flags are
    /// live: a Codex task has Changes only after it has produced a diff, for example.
    /// </summary>
    [Flags]
    internal enum DesktopControl
    {
        None = 0,
        Search = 1 << 0,
        Changes = 1 << 1,
        Projects = 1 << 2,
        Plugins = 1 << 3,
        AttachFiles = 1 << 4,
        Permissions = 1 << 5,
        Scheduled = 1 << 6,
        PullRequests = 1 << 7,
        Explore = 1 << 8,
        QuickChat = 1 << 9,
    }
}
