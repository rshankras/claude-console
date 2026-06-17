namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// LCD Key 2: Reject — writes "deny" decision to response file.
    /// When Claude Code is waiting for permission (PreToolUse hook), pressing this
    /// key rejects the tool call via file-based IPC.
    /// </summary>
    public class RejectCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;
        private Boolean _isActive;

        public RejectCommand()
            : base(displayName: "Reject", description: "Reject current Claude Code tool call", groupName: "Core")
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
            _bridge.SendDecision("deny", "User rejected from hardware");
            _isActive = false;
            this.ActionImageChanged();
            PluginLog.Info("RejectCommand: Sent 'deny' decision");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            return _isActive
                ? $"\u2717 Reject{Environment.NewLine}(pending)"
                : $"\u2717 Reject";
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            return KeyImage.Render(imageSize, "Reject", KeyImage.Red, "reject");
        }
    }
}
