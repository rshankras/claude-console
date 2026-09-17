namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// Voice Draft — the review-round variant of Voice: transcribe into the composer, bring the
    /// app forward, and DO NOT send. The preview-before-commit answer for anything where a
    /// mis-transcription matters: you read what whisper heard, fix a word, press Return
    /// yourself. Same capture pipeline, same sink; only the submit flag differs.
    /// </summary>
    public class DesktopVoiceDraftCommand : PluginDynamicCommand
    {
        private readonly ListeningFace _face;
        private readonly FailureFace _fail;
        private String StartupLabel => BridgeManager.Instance.Voice.StartupLabel(VoiceIntent.DesktopDraft);

        public DesktopVoiceDraftCommand()
            : base(displayName: "Voice Draft", description: "Speak — the transcript waits in the composer for you to review and send", groupName: "Agent")
        {
            this.SetWidget(true);
            _face = new ListeningFace(() => this.ActionImageChanged());
            _fail = new FailureFace(() => this.ActionImageChanged(), holdMs: VoiceFailure.HoldMs);

            // A dictation that failed says so on the key that was pressed, for a moment (#18). Only
            // this key's own captures: a failure routed to another key is that key's to show.
            BridgeManager.Instance.OnVoiceFailed += (intent, text) =>
            {
                if (intent == VoiceIntent.DesktopDraft) { _fail.Show(text); }
            };

            // The engine owns "is the mic running, and for whom" (#28). This key only reflects it,
            // so a capture stopped from ANOTHER voice key clears this face too.
            BridgeManager.Instance.Voice.Changed += () =>
            {
                if (BridgeManager.Instance.Voice.IsRecording(VoiceIntent.DesktopDraft))
                {
                    if (!_face.IsActive) { _face.Start(); }
                }
                else if (_face.IsActive)
                {
                    _face.Stop();
                }
                this.ActionImageChanged();
            };
        }

        protected override void RunCommand(String actionParameter)
        {
            if (!DesktopServices.Declared)
            {
                PluginLog.Warning("DesktopVoiceDraftCommand: no desktop surface declared");
                return;
            }

            // One door for every voice key. Whether this press starts, stops, or is refused — and
            // where a stopped capture's transcript is routed — is the engine's call, not this key's.
            _fail.Clear();
            DesktopServices.VoiceActions.RequestDictation(VoiceIntent.DesktopDraft, BridgeManager.Instance.Voice,
                intent => BridgeManager.Instance.ToggleVoice(intent), out var feedback);
            if (feedback != null) { _fail.Show(feedback); }
            PluginLog.Info($"DesktopVoiceDraftCommand: recording={_face.IsActive}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "\u200B";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            KeyImage.RenderIntentTile(imageSize,
                StartupLabel ?? (_face.IsActive ? "Listening" : _fail.IsActive ? _fail.Text : "Voice Draft"),
                _face.IsActive ? _face.Icon : "voice_draft",
                _fail.IsActive && !_face.IsActive ? "CHECK APP" : "DRAFT");
    }
}
