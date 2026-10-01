namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;

    /// <summary>Shared face inputs for the real voice keys and the hardware-free command rig.</summary>
    internal static class DesktopDictationFace
    {
        internal static (String Label, String Icon, String Footer) For(
            VoiceIntent intent, VoiceCaptureState capture, String failure, String listeningIcon, Boolean pending = false, String retryHint = "TAP TO RETRY")
        {
            var draft = intent == VoiceIntent.DesktopDraft;
            var listening = capture.IsRecording(intent);
            var progress = capture.StartupLabel(intent) ?? (capture.IsTranscribing(intent) ? "Transcribing" : null);
            // A retained draft is shown on EVERY voice key that would act on it: a tap on either key
            // inserts it (never sends), a hold discards it. A key that silently did something other
            // than its face promised is how a stale brief ends up in the wrong chat.
            if (pending && !listening && progress == null)
            {
                return (VoiceFailure.InsertDraft, draft ? "voice_draft" : "voice", retryHint);
            }
            if (draft && failure == "Draft Ready") { return ("Draft Ready", "voice_draft", "SEND DRAFT"); }
            if (draft && failure == "Discarded") { return ("Discarded", "voice_draft", "DICTATE"); }
            return (
                failure ?? progress ?? (listening ? (capture.Capped ? "Recording ended" : "Listening") : draft ? "Dictate" : "Dictate & Send"),
                listening ? listeningIcon : draft ? "voice_draft" : "voice",
                failure != null ? (draft && failure == VoiceFailure.InsertDraft ? retryHint : "CHECK APP")
                    : listening ? "PRESS TO STOP" : progress != null ? "WAIT" : draft ? "DRAFT" : "SEND");
        }
    }
}
