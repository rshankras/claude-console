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

        public VoiceCommand()
            : base(displayName: "Voice", description: "Speak a prompt — press to start, press again to transcribe and send", groupName: "Universal")
        {
            _face = new ListeningFace(() => this.ActionImageChanged());
        }

        protected override void RunCommand(String actionParameter)
        {
            var bridge = BridgeManager.Instance;
            if (!_face.IsActive)
            {
                bridge.StartVoiceCapture();
                _face.Start();
            }
            else
            {
                bridge.StopVoiceCapture();
                _face.Stop();
            }

            this.ActionImageChanged();
            PluginLog.Info($"VoiceCommand: recording={_face.IsActive}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            _face.IsActive ? "Listening" : "Voice";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            _face.IsActive
                ? KeyImage.Render(imageSize, "Listening", KeyImage.Green, _face.Icon)
                : KeyImage.Render(imageSize, "Voice", KeyImage.Purple, "voice");
    }
}
