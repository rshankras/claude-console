namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// Voice key — offline dictation via the bundled ClaudeVoiceHelper.app (whisper.cpp, no cloud).
    ///
    /// Press once to start recording (you'll hear a "Tink"), speak, then press again to stop:
    /// the helper transcribes locally and the plugin types the text into the focused terminal.
    /// While recording, the face animates a green equalizer and the title switches to "Listening"
    /// (see ListeningFace, shared with Go to Project).
    ///
    /// Recording lives in a separate signed app bundle because it holds its OWN Microphone
    /// permission — LogiPluginService (a background daemon) cannot get mic access directly. The
    /// start/stop plumbing is in BridgeManager.StartVoiceCapture / StopVoiceCapture; the bundle
    /// and its build script are in tools/voice/.
    /// </summary>
    public class VoiceCommand : PluginDynamicCommand
    {
        private readonly ListeningFace _face;
        private readonly FailureFace _fail;
        private Boolean _settingUp;

        public VoiceCommand()
            : base(displayName: "Dictate", description: "Speak a prompt — press to start, press again to transcribe and send", groupName: "Universal")
        {
            _face = new ListeningFace(() => this.ActionImageChanged());
            _fail = new FailureFace(() => this.ActionImageChanged());

            // A dictation that failed says so on the key that was pressed, for a moment (#18). Only
            // this key's own captures: a failure routed to another key is that key's to show.
            BridgeManager.Instance.OnVoiceFailed += (intent, text) =>
            {
                if (intent == VoiceIntent.Send) { _fail.Show(text); }
            };

            BridgeManager.Instance.OnVoiceSetupChanged += (intent, active) =>
            {
                if (intent != VoiceIntent.Send) { return; }
                _settingUp = active;
                this.ActionImageChanged();
            };

            // The engine owns "is the mic running, and for whom" (#28). This key only reflects it,
            // so a capture stopped from ANOTHER voice key clears this face too — three keys used to
            // hold three private flags and could all claim to be recording at once.
            BridgeManager.Instance.Voice.Changed += () =>
            {
                if (BridgeManager.Instance.Voice.IsRecording(VoiceIntent.Send))
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
            // One door for every voice key. Whether this press starts, stops, or is refused — and
            // where a stopped capture's transcript is routed — is the engine's call, not this key's.
            BridgeManager.Instance.ToggleVoice(VoiceIntent.Send);
            PluginLog.Info($"VoiceCommand: recording={_face.IsActive}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            _settingUp ? "Setting up" : (_face.IsActive ? "Listening" : (_fail.IsActive ? _fail.Text : "Dictate"));

        // Listening wins over a stale failure: a new press means a new attempt.
        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            _settingUp
                ? KeyImage.Render(imageSize, "Setting up", KeyImage.Amber)
                : _face.IsActive
                ? KeyImage.Render(imageSize, "Listening", KeyImage.Green, _face.Icon)
                : _fail.IsActive
                    ? KeyImage.Render(imageSize, _fail.Text, KeyImage.Red, "voice")
                    : KeyImage.Render(imageSize, "Dictate", KeyImage.Purple, "voice");
    }
}
