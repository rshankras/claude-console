namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// The words a live key (Cost / Context / Activity) shows for each live-status state, and the
    /// flashes a press gets while a question is open (#31). One owner, so the three keys can never
    /// drift apart on wording. Short on purpose: a key face holds about thirteen characters before
    /// the renderer shrinks it, and the voice keys' failure faces are held to the same budget.
    /// </summary>
    internal static class LiveStatusFace
    {
        /// <summary>Shown while a first press has armed the key: a second press within the window enables.</summary>
        internal const String PressHint = "Press again";

        /// <summary>Shown while a long press has armed the key: a second long press within the window turns it off.</summary>
        internal const String OffHint = "Turn off?";

        internal static Boolean NeedsSetup(LiveStatusState state) =>
            state is LiveStatusState.NotEnabled or LiveStatusState.Off or LiveStatusState.NeedsRepair;

        /// <summary>The label that replaces the key's live value, or null when the key shows its own.</summary>
        internal static String Label(LiveStatusState state) =>
            state switch
            {
                LiveStatusState.NotEnabled => "Set up",
                LiveStatusState.NeedsRepair => "Set up",
                LiveStatusState.Off => "Off",
                LiveStatusState.JustEnabled => "Restart Claude",
                _ => null,
            };
    }
}
