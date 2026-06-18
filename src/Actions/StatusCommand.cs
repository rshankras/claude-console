namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// LCD Status key (group "Core") — shows whether Claude Code is "Ready" or "Waiting"
    /// (i.e. blocking on a permission decision). Face = status icon; the live word goes in the
    /// LABEL so it isn't drawn twice. Context-window % lives on its own ContextDisplayCommand key.
    /// Display-only — no terminal action on press.
    /// </summary>
    public class StatusCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;
        private String _status = "Ready";

        public StatusCommand()
            : base(displayName: "Status", description: "Shows Claude Code session status", groupName: "Core")
        {
            _bridge = BridgeManager.Instance;

            _bridge.OnStateChanged += (state) =>
            {
                _status = state.Status == "waiting_approval" ? "Waiting" : "Ready";
                this.ActionImageChanged();
            };
        }

        protected override void RunCommand(String actionParameter)
        {
            // Display-only indicator. No terminal action on press.
            PluginLog.Info("StatusCommand: pressed (display-only)");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            return _status;
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            // Face = status icon; live "Ready"/"Waiting" is the LABEL (no double-draw).
            return KeyImage.Render(imageSize, _status, KeyImage.Dark, "status");
        }
    }
}
