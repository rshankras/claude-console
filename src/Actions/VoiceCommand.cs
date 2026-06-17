namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// Voice key — offline dictation via the bundled ClaudeVoiceHelper.app (whisper.cpp, no cloud).
    ///
    /// Press once to start recording (you'll hear a "Tink"), speak, then press again to stop:
    /// the helper transcribes locally and the plugin types the text into the focused terminal.
    ///
    /// Recording lives in a separate signed app bundle because it holds its OWN Microphone
    /// permission — LogiPluginService (a background daemon) cannot get mic access directly. The
    /// start/stop plumbing is in BridgeManager.StartVoiceCapture / StopVoiceCapture; the bundle
    /// and its build script are in tools/voice/.
    /// </summary>
    public class VoiceCommand : PluginDynamicCommand
    {
        private Boolean _recording;

        public VoiceCommand()
            : base(displayName: "Voice", description: "Speak a prompt — press to start, press again to transcribe and send", groupName: "Universal")
        {
        }

        protected override void RunCommand(String actionParameter)
        {
            var bridge = BridgeManager.Instance;
            if (!_recording)
            {
                bridge.StartVoiceCapture();
            }
            else
            {
                bridge.StopVoiceCapture();
            }

            _recording = !_recording;
            this.ActionImageChanged();
            PluginLog.Info($"VoiceCommand: recording={_recording}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            _recording ? "Listening" : "Voice";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            _recording
                ? KeyImage.Render(imageSize, "Listening", KeyImage.Red, null)   // null icon -> centred text
                : KeyImage.Render(imageSize, "Voice", KeyImage.Purple, "voice");
    }
}
