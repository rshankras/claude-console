namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// Voice for the desktop surface: press to record (Tink, green equalizer), press again to
    /// transcribe — and the text lands in the APP'S COMPOSER and submits, not in a terminal.
    /// Same capture pipeline as every other product (helper + whisper, one shared runtime, one
    /// Microphone grant); only the sink differs, via BridgeManager.StopVoiceCaptureTo.
    ///
    /// The desktop injection law applies to the sink: the transcript reaches the composer of
    /// the conversation on screen, or nowhere — WriteComposer verifies the text landed before
    /// pressing Send, and a failure is logged, never half-typed.
    /// </summary>
    public class DesktopVoiceCommand : PluginDynamicCommand
    {
        private readonly ListeningFace _face;

        public DesktopVoiceCommand()
            : base(displayName: "Voice", description: "Speak a prompt — press to start, press again to send it to the app", groupName: "Agent")
        {
            _face = new ListeningFace(() => this.ActionImageChanged());
        }

        protected override void RunCommand(String actionParameter)
        {
            if (!DesktopServices.Declared)
            {
                PluginLog.Warning("DesktopVoiceCommand: no desktop surface declared");
                return;
            }

            var bridge = BridgeManager.Instance;
            if (!_face.IsActive)
            {
                bridge.StartVoiceCapture();
                _face.Start();
            }
            else
            {
                bridge.StopVoiceCaptureTo(text =>
                {
                    if (!DesktopServices.Automation.WriteComposer(text, send: true, out var error))
                    {
                        PluginLog.Warning($"DesktopVoiceCommand: transcript did not reach the composer — {error}");
                    }
                });
                _face.Stop();
            }

            this.ActionImageChanged();
            PluginLog.Info($"DesktopVoiceCommand: recording={_face.IsActive}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            _face.IsActive ? "Listening" : "Voice";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            _face.IsActive
                ? KeyImage.Render(imageSize, "Listening", KeyImage.Green, _face.Icon)
                : KeyImage.Render(imageSize, "Voice", KeyImage.Purple, "voice");
    }
}
