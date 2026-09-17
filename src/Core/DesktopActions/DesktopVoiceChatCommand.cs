namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>Native app-owned spoken conversation. Never records or transcribes locally.</summary>
    public class DesktopVoiceChatCommand : PluginDynamicCommand
    {
        private readonly FailureFace _feedback;

        public DesktopVoiceChatCommand()
            : base(displayName: "Voice Chat", description: "Start a native spoken conversation; press End Voice to finish", groupName: "Agent")
        {
            this.SetWidget(true);
            _feedback = new FailureFace(() => this.ActionImageChanged(), holdMs: 1800);
            if (DesktopServices.Declared)
            {
                DesktopServices.Monitor.OnChanged += _ => { _feedback.Clear(); this.ActionImageChanged(); };
            }
        }

        protected override void RunCommand(String actionParameter)
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
            var active = state.Available && state.VoiceChat == DesktopVoiceState.Active;
            return KeyImage.RenderIntentTile(imageSize,
                _feedback.IsActive ? _feedback.Text : LabelFor(state),
                "voice_chat", active ? "ACTIVE" : state.Available && state.VoiceChat == DesktopVoiceState.Ready
                    ? "TALK" : "CHECK APP");
        }

        internal static String LabelFor(DesktopState state) => !state.Available ? "Unavailable" : state.VoiceChat switch
        {
            DesktopVoiceState.Ready => "Voice Chat",
            DesktopVoiceState.Active => "End Voice",
            _ => "No Voice",
        };
    }
}
