namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// Go to Project (voice) — speak a project name to jump straight into it.
    ///
    /// Press once to start listening (you'll hear a "Tink"), say the project name
    /// (e.g. "indie app autopilot", "headroom", "asc metadata"), press again to stop.
    /// The bundled whisper helper transcribes locally, the plugin fuzzy-matches the phrase
    /// against your project folders (scanned live under ~/Work/MyApps and ~/Work), then opens
    /// a new Terminal tab, cd's into the match, and launches claude — one gesture, no typing.
    ///
    /// Reuses the same recorder as VoiceCommand; only the stop handler differs
    /// (BridgeManager.StopVoiceCaptureForProject → NavigateToProjectByVoice). It also reuses the
    /// same ListeningFace, so a recording Go to Project key looks exactly like a recording Voice key.
    /// </summary>
    public class ProjectVoiceCommand : PluginDynamicCommand
    {
        private readonly ListeningFace _face;

        public ProjectVoiceCommand()
            : base(displayName: "Go to Project", description: "Speak a project name — opens a new tab, cd's there, and launches claude", groupName: "Terminal")
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
                bridge.StopVoiceCaptureForProject();
                _face.Stop();
            }

            this.ActionImageChanged();
            PluginLog.Info($"ProjectVoiceCommand: listening={_face.IsActive}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            _face.IsActive ? "Listening" : "Go to Project";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            _face.IsActive
                ? KeyImage.Render(imageSize, "Listening", KeyImage.Green, _face.Icon)
                : KeyImage.Render(imageSize, "Project", KeyImage.Blue, "project");
    }
}
