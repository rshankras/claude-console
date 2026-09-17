namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// Voice for the desktop surface: press to record (Tink, green equalizer), press again to
    /// transcribe — and the text lands in the APP'S COMPOSER and submits, not in a terminal.
    /// Same capture pipeline as every other product (helper + whisper, one shared runtime, one
    /// Microphone grant, the engine's one door in ToggleVoice); only the destination differs, and
    /// the product names it once, in BridgeManager.TranscriptSink.
    ///
    /// The desktop injection law applies to the sink: the transcript reaches the composer of the
    /// conversation on screen, or nowhere — WriteComposer verifies the text landed before pressing
    /// Send, and a failure is named on this key (Not typed / No target), never half-typed and
    /// never only logged.
    /// </summary>
    public class DesktopVoiceCommand : PluginDynamicCommand
    {
        private readonly ListeningFace _face;
        private readonly FailureFace _fail;
        private String StartupLabel => BridgeManager.Instance.Voice.StartupLabel(VoiceIntent.Desktop);

        public DesktopVoiceCommand()
            : base(displayName: "Voice", description: "Speak a prompt — press to start, press again to send it to the app", groupName: "Agent")
        {
            _face = new ListeningFace(() => this.ActionImageChanged());
            _fail = new FailureFace(() => this.ActionImageChanged(), holdMs: VoiceFailure.HoldMs);

            // A dictation that failed says so on the key that was pressed, for a moment (#18). Only
            // this key's own captures: a failure routed to another key is that key's to show.
            BridgeManager.Instance.OnVoiceFailed += (intent, text) =>
            {
                if (intent == VoiceIntent.Desktop) { _fail.Show(text); }
            };

            // The engine owns "is the mic running, and for whom" (#28). This key only reflects it,
            // so a capture stopped from ANOTHER voice key clears this face too.
            BridgeManager.Instance.Voice.Changed += () =>
            {
                if (BridgeManager.Instance.Voice.IsRecording(VoiceIntent.Desktop))
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
                PluginLog.Warning("DesktopVoiceCommand: no desktop surface declared");
                return;
            }

            // One door for every voice key. Whether this press starts, stops, or is refused — and
            // where a stopped capture's transcript is routed — is the engine's call, not this key's.
            _fail.Clear();
            BridgeManager.Instance.ToggleVoice(VoiceIntent.Desktop);
            PluginLog.Info($"DesktopVoiceCommand: recording={_face.IsActive}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            StartupLabel ?? (_face.IsActive ? "Listening" : (_fail.IsActive ? _fail.Text : "Voice"));

        // Listening wins over a stale failure: a new press means a new attempt.
        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            StartupLabel != null
                ? KeyImage.Render(imageSize, StartupLabel, KeyImage.Blue, "voice")
                : _face.IsActive
                ? KeyImage.Render(imageSize, "Listening", KeyImage.Green, _face.Icon)
                : _fail.IsActive
                    ? KeyImage.Render(imageSize, _fail.Text, KeyImage.Red, "voice")
                    : KeyImage.Render(imageSize, "Voice", KeyImage.Blue, "voice");
    }
}
