namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// LCD Key 9: Status — shows current Claude Code status.
    /// Displays "Ready", "Waiting", or context usage percentage.
    /// </summary>
    public class StatusCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;
        private String _status = "Ready";
        private Double _contextPct;

        public StatusCommand()
            : base(displayName: "Status", description: "Shows Claude Code session status", groupName: "Core")
        {
            _bridge = BridgeManager.Instance;

            _bridge.OnStateChanged += (state) =>
            {
                if (state.Status == "waiting_approval")
                {
                    _status = "Waiting";
                }
                else
                {
                    _status = "Ready";
                }

                _contextPct = state.ContextWindow?.UsedPercentage ?? 0;
                this.ActionImageChanged();
            };
        }

        protected override void RunCommand(String actionParameter)
        {
            // Display-only indicator (live status + context %). No terminal action on press.
            PluginLog.Info("StatusCommand: pressed (display-only)");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            return $"{_status}{Environment.NewLine}{_contextPct:F0}% ctx";
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            return KeyImage.Render(imageSize, $"{_status}\n{_contextPct:F0}% ctx", KeyImage.Dark);
        }
    }
}
