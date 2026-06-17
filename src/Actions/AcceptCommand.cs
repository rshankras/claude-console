namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// LCD Key 1: Accept — writes "allow" decision to response file.
    /// When Claude Code is waiting for permission (PreToolUse hook), pressing this
    /// key approves the tool call via file-based IPC.
    /// </summary>
    public class AcceptCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;
        private Boolean _isActive;

        public AcceptCommand()
            : base(displayName: "Accept", description: "Accept current Claude Code tool call", groupName: "Core")
        {
            _bridge = BridgeManager.Instance;

            _bridge.OnPermissionNeeded += (_) =>
            {
                _isActive = true;
                this.ActionImageChanged();
            };

            _bridge.OnStateChanged += (state) =>
            {
                var wasActive = _isActive;
                _isActive = state.Status == "waiting_approval";
                if (wasActive != _isActive)
                {
                    this.ActionImageChanged();
                }
            };
        }

        protected override void RunCommand(String actionParameter)
        {
            _bridge.SendDecision("allow");
            _isActive = false;
            this.ActionImageChanged();
            PluginLog.Info("AcceptCommand: Sent 'allow' decision");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            return _isActive
                ? $"\u2713 Accept{Environment.NewLine}(pending)"
                : $"\u2713 Accept";
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            return KeyImage.Render(imageSize, "Accept", KeyImage.Green, "accept");
        }
    }
}
