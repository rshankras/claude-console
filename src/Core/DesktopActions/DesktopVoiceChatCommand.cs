namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>Native app-owned spoken conversation. Never records or transcribes locally.</summary>
    public class DesktopVoiceChatCommand : DesktopCommandBase
    {
        private readonly FailureFace _feedback;

        public DesktopVoiceChatCommand()
            : base(displayName: "Voice Chat", description: "Start or stop native Voice Chat. Uses your configured app shortcut when available.", groupName: "Agent")
        {
            this.SetWidget(true);
            _feedback = new FailureFace(() => this.ActionImageChanged(), holdMs: 1800);
            DesktopServices.Lifetime.OnStop(_feedback.Dispose);
            if (DesktopServices.Declared)
            {
                DesktopServices.OnMonitorChanged(_ => this.ActionImageChanged());
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            DesktopServices.Run(() => this.RunDesktopCommand(actionParameter));
        }

        private void RunDesktopCommand(String actionParameter)
        {
            if (!DesktopServices.Declared) { return; }
            DesktopServices.VoiceActions.RequestVoice(DesktopServices.Monitor.Current.VoiceChat,
                BridgeManager.Instance.Voice, out var feedback);
            if (feedback != null) { _feedback.Show(feedback); }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "\u200B";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var state = DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable;
            var shortcut = DesktopServices.Declared && DesktopServices.Automation.HasVoiceShortcut;
            var active = state.Available && state.VoiceChat == DesktopVoiceState.Active;
            return KeyImage.RenderControlTile(imageSize,
                _feedback.IsActive ? _feedback.Text : LabelFor(state, shortcut),
                "voice_chat", status: active ? "ACTIVE" : shortcut ? "TOGGLE" : state.Available && state.VoiceChat == DesktopVoiceState.Ready
                    ? "TALK" : "CHECK APP");
        }

        internal static String LabelFor(DesktopState state, Boolean shortcut = false) => state.Available && state.VoiceChat == DesktopVoiceState.Active ? "End Voice" : shortcut ? "Voice Chat"
            : !state.Available ? "Unavailable" : state.VoiceChat switch
        {
            DesktopVoiceState.Ready => "Voice Chat",
            DesktopVoiceState.Active => "End Voice",
            _ => "No Voice",
        };
    }
}
