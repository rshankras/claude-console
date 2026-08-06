namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// Voice Draft key — dictation you can CORRECT before it goes. Same capture flow as the Voice
    /// key (press to record, press again to transcribe), but the transcript is only TYPED into
    /// Claude's input box — no Return. Fix whatever whisper misheard, then submit with Return
    /// (keyboard or the keypad's Return key).
    ///
    /// A separate key rather than a mode on the Voice key, on purpose: users asked for both
    /// behaviours at once — direct execute for quick prompts, review for anything long enough to
    /// mis-transcribe — and a toggle would make every press depend on invisible state.
    ///
    /// Same pattern as ProjectVoiceCommand: its own key, its own ListeningFace, the shared
    /// BridgeManager capture plumbing.
    /// </summary>
    public class VoiceDraftCommand : PluginDynamicCommand
    {
        private readonly ListeningFace _face;

        public VoiceDraftCommand()
            : base(displayName: "Voice Draft", description: "Speak a prompt, then fix it before sending — types the transcript without submitting; press Return when it reads right", groupName: "Universal")
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
                bridge.StopVoiceCapture(submit: false);
                _face.Stop();
            }

            this.ActionImageChanged();
            PluginLog.Info($"VoiceDraftCommand: recording={_face.IsActive}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            _face.IsActive ? "Listening" : "Draft";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            _face.IsActive
                ? KeyImage.Render(imageSize, "Listening", KeyImage.Green, _face.Icon)
                : KeyImage.Render(imageSize, "Draft", KeyImage.Purple, "voice_draft");
    }
}
