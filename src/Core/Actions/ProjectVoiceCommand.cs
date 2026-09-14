namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// Go to Project (voice) — speak a project name to jump straight into it.
    ///
    /// Press once to start listening (you'll hear a "Tink"), say the project name
    /// (e.g. "indie app autopilot", "headroom", "asc metadata"), press again to stop.
    /// The bundled whisper helper transcribes locally, the plugin fuzzy-matches the phrase
    /// against active projects and configured or automatically discovered folders, then opens
    /// a new Terminal tab, cd's into the match, and launches the selected agent — one gesture, no typing.
    ///
    /// Reuses the same recorder as VoiceCommand; only the stop handler differs
    /// (BridgeManager.StopVoiceCaptureForProject → NavigateToProjectByVoice). It also reuses the
    /// same ListeningFace, so a recording Go to Project key looks exactly like a recording Voice key.
    /// </summary>
    public class ProjectVoiceCommand : PluginDynamicCommand
    {
        private readonly ListeningFace _face;
        private readonly FailureFace _fail;
        private String StartupLabel => BridgeManager.Instance.Voice.StartupLabel(VoiceIntent.Project);

        public ProjectVoiceCommand()
            : base(displayName: "Go to Project", description: $"Speak a project name — opens its folder in {BridgeManager.Instance.Agent.DisplayName}", groupName: "Terminal")
        {
            _face = new ListeningFace(() => this.ActionImageChanged());
            _fail = new FailureFace(() => this.ActionImageChanged(), holdMs: VoiceFailure.HoldMs);

            // A dictation that failed says so on the key that was pressed, for a moment (#18). Only
            // this key's own captures: a failure routed to another key is that key's to show.
            BridgeManager.Instance.OnVoiceFailed += (intent, text) =>
            {
                if (intent == VoiceIntent.Project) { _fail.Show(text); }
            };

            // The engine owns "is the mic running, and for whom" (#28). This key only reflects it,
            // so a capture stopped from ANOTHER voice key clears this face too — three keys used to
            // hold three private flags and could all claim to be recording at once.
            BridgeManager.Instance.Voice.Changed += () =>
            {
                if (BridgeManager.Instance.Voice.IsRecording(VoiceIntent.Project))
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
            _fail.Clear();
            BridgeManager.Instance.ToggleVoice(VoiceIntent.Project);
            PluginLog.Info($"ProjectVoiceCommand: listening={_face.IsActive}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            StartupLabel ?? (_face.IsActive ? "Listening" : (_fail.IsActive ? _fail.Text : "Go to Project"));

        // Listening wins over a stale failure: a new press means a new attempt.
        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            StartupLabel != null
                ? KeyImage.Render(imageSize, StartupLabel, KeyImage.Purple, "project")
                : _face.IsActive
                ? KeyImage.Render(imageSize, "Listening", KeyImage.Green, _face.Icon)
                : _fail.IsActive
                    ? KeyImage.Render(imageSize, _fail.Text, KeyImage.Red, "project")
                    : KeyImage.Render(imageSize, "Project", KeyImage.Blue, "project");
    }
}
