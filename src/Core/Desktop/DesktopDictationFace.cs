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
            if (draft && pending && !listening && progress == null)
            {
                return (VoiceFailure.InsertDraft, "voice_draft", retryHint);
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
