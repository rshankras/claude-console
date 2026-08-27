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

            // The engine owns "is the mic running, and for whom" (#28). This key only reflects it,
            // so a capture stopped from ANOTHER voice key clears this face too — three keys used to
            // hold three private flags and could all claim to be recording at once.
            BridgeManager.Instance.Voice.Changed += () =>
            {
                if (BridgeManager.Instance.Voice.IsRecording(VoiceIntent.Draft))
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
            BridgeManager.Instance.ToggleVoice(VoiceIntent.Draft);
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
