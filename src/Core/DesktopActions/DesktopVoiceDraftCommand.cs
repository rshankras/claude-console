namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// Voice Draft — the review-round variant of Voice: transcribe into the composer, bring the
    /// app forward, and DO NOT send. The preview-before-commit answer for anything where a
    /// mis-transcription matters: you read what whisper heard, fix a word, press Return
    /// yourself. Same capture pipeline, same shared runtime; only the last step differs.
    /// </summary>
    public class DesktopVoiceDraftCommand : PluginDynamicCommand
    {
        private readonly ListeningFace _face;

        public DesktopVoiceDraftCommand()
            : base(displayName: "Voice Draft", description: "Speak — the transcript waits in the composer for you to review and send", groupName: "Agent")
        {
            _face = new ListeningFace(() => this.ActionImageChanged());
        }

        protected override void RunCommand(String actionParameter)
        {
            if (!DesktopServices.Declared)
            {
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
                    if (DesktopServices.Automation.WriteComposer(text, send: false, out var error))
                    {
                        DesktopServices.Automation.FocusApp();   // reviewing needs eyes on it
                    }
                    else
                    {
                        PluginLog.Warning($"DesktopVoiceDraftCommand: transcript did not reach the composer — {error}");
                    }
                });
                _face.Stop();
            }

            this.ActionImageChanged();
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            _face.IsActive ? "Listening" : "Voice Draft";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            _face.IsActive
                ? KeyImage.Render(imageSize, "Listening", KeyImage.Green, _face.Icon)
                : KeyImage.Render(imageSize, "Voice Draft", KeyImage.Purple, "voice_draft");
    }
}
