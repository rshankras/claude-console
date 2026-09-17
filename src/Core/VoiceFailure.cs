namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    /// <summary>
    /// What a voice key says when a dictation fails (#18).
    ///
    /// A failed dictation used to look exactly like a successful one: green equaliser, key back to
    /// "Voice", nothing typed, no beep. The only evidence was in the plugin log — and for the
    /// commonest cause, a Microphone permission never granted, not even there: the helper wrote its
    /// reason to stderr, and it is launched detached, so stderr went nowhere. Reported from the
    /// field as "voice functionality is still not working".
    ///
    /// These are the words the key shows for a couple of seconds. They are chosen by what the USER
    /// should do next, because that is what differs between cases — grant a permission, speak up,
    /// wait for a download, reinstall — not by what went wrong internally, which is in the log.
    /// Kept pure so the mapping from the helper's sidecar text is testable.
    /// </summary>
    internal static class VoiceFailure
    {
        /// <summary>
        /// How long a failure word stays on the key. Product-declared, because it is a house style
        /// rather than a fact about the agent: the default is the 2.5 s Claude Console shipped and
        /// QA signed off, and a product that wants longer says so in its plugin constructor. A new
        /// press clears the word on every product, so a longer hold is never a stuck key.
        /// </summary>
        internal static Int32 HoldMs { get; private set; } = 2500;

        /// <summary>Declare this product's failure-word hold. Called from the plugin constructor.</summary>
        internal static void UseHold(Int32 milliseconds) =>
            HoldMs = milliseconds > 0 ? milliseconds : 2500;


        /// <summary>The helper was refused the microphone. Grant it in System Settings.</summary>
        internal const String MicDenied = "Mic denied";

        /// <summary>Recorded fine, heard nothing usable. Speak up, or check the input device.</summary>
        internal const String NoSpeech = "No speech";

        /// <summary>The speech model is still downloading. Try again shortly.</summary>
        internal const String ModelLoading = "Model loading";

        /// <summary>ClaudeVoiceHelper.app is not where the plugin installed it. Reinstall.</summary>
        internal const String NoHelper = "No helper";

        /// <summary>The helper never produced a result within the wait. Check the log.</summary>
        internal const String NoResponse = "No response";

        /// <summary>Everything else — the log has the sidecar text.</summary>
        internal const String Failed = "Voice failed";

        /// <summary>
        /// Transcribed fine, but there was no session to type into: nothing pinned and no single
        /// obvious session. Pin a session slot. Before this, a dropped dictation and a delivered
        /// one looked identical from the device (2.2.1 Windows retest, item 6).
        /// </summary>
        internal const String NoTarget = "No target";

        /// <summary>The session was known but the keystrokes did not land (terminal gone, elevated). The log has the outcome.</summary>
        internal const String NotTyped = "Not typed";

        /// <summary>Insertion was not confirmed; the draft was copied for manual review and paste.</summary>
        internal const String PasteDraft = "Paste Draft";

        /// <summary>whisper-cli is not in the runtime home. Reinstall the package.</summary>
        internal const String NoWhisper = "No whisper";

        /// <summary>"Go to Project" heard a phrase that matched no known project. Check the log for the candidates.</summary>
        internal const String NoMatch = "No match";

        /// <summary>
        /// Map the helper's <c>.error</c> sidecar to the key's words. The sidecar carries a full
        /// sentence for the log; the key gets the two words that tell the user what to do.
        /// </summary>
        internal static String FromSidecar(String sidecar)
        {
            if (String.IsNullOrWhiteSpace(sidecar))
            {
                return Failed;
            }

            if (sidecar.IndexOf("microphone permission", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MicDenied;
            }

            if (sidecar.IndexOf("speech model", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ModelLoading;
            }

            return Failed;
        }
    }
}
