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

        /// <summary>
        /// The word for a state the user has to act on ("Set up" / "Off"), or null once the wiring
        /// is in place. Shared by the live keys, the answer keys and the session faces: with the
        /// hooks absent, Yes / No cannot see a prompt and a session key has no state to show, so
        /// they say this instead of looking ready (#58).
        /// </summary>
        internal static String SetupLabel(LiveStatusState state) =>
            state switch
            {
                LiveStatusState.NotEnabled => "Set up",
                LiveStatusState.NeedsRepair => "Set up",
                LiveStatusState.Off => "Off",
                _ => null,
            };

        /// <summary>
        /// SetupLabel gated on whether the product's agent has a switch at all: Codex keeps its own
        /// hooks file, so its answer keys and session faces never say "Set up" (BridgeManager.LiveStatusApplies).
        /// </summary>
        internal static String SetupWord(Boolean liveStatusApplies, LiveStatusState state) =>
            liveStatusApplies ? SetupLabel(state) : null;

        /// <summary>
        /// The same states, worded for a SESSION key's state bar. The live keys can say "Off"
        /// because they are the status; on a session bar "Off" reads as the session being off
        /// (the owner read it that way on the first hardware pass), so the bar says what is
        /// actually off. "Set up" stays an instruction and needs no rewording (#58).
        /// </summary>
        internal static String SessionBarWord(Boolean liveStatusApplies, LiveStatusState state) =>
            SetupWord(liveStatusApplies, state) switch
            {
                "Off" => "Status off",
                var word => word,
            };

        /// <summary>
        /// The label that replaces the key's live value, or null when the key shows its own.
        /// <paramref name="settingsApplyLive"/> is the platform's word on whether a running session
        /// picks the new wiring up by itself: where it does, the moment after Turn on is "Turned on"
        /// and the live value replaces it on the session's next activity; where it does not, the
        /// honest face is still "Restart Claude" (IPlatformBridge.SettingsApplyLive, #58).
        /// </summary>
        internal static String Label(LiveStatusState state, Boolean settingsApplyLive) =>
            state == LiveStatusState.JustEnabled
                ? (settingsApplyLive ? "Turned on" : "Restart Claude")
                : SetupLabel(state);
    }
}
