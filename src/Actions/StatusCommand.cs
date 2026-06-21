namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Threading;

    /// <summary>
    /// LCD Activity key (group "Core") — shows at a glance whether Claude Code is Working, Waiting on
    /// you, or Ready/idle. Live state comes from the activity hooks (scripts/activity-hook.sh →
    /// /tmp/claude-console-activity.json). Without the hooks wired it still shows Waiting on a
    /// permission prompt (state.Status) and Ready otherwise. While Working, the hourglass animates
    /// (sand flips) so the key looks alive. Face = status icon; the live word is the LABEL.
    /// Display-only — no terminal action on press.
    /// </summary>
    public class StatusCommand : PluginDynamicCommand
    {
        // Frames cycled while Working — a flipping/draining hourglass.
        private static readonly String[] BusyFrames = { "busy0", "busy1" };

        private readonly BridgeManager _bridge;
        private String _status = "Ready";
        private String _icon = "done";
        private Boolean _busy;
        private Timer _animTimer;
        private Int32 _frame;

        public StatusCommand()
            : base(displayName: "Activity", description: "Shows whether Claude is working, waiting, or ready", groupName: "Core")
        {
            _bridge = BridgeManager.Instance;
            _bridge.OnActivityChanged += (_) => this.Refresh();
            _bridge.OnStateChanged += (_) => this.Refresh();
        }

        // Map the activity flag (plus the waiting_approval fallback) to a face + word.
        private void Refresh()
        {
            var activity = _bridge.CurrentActivity?.State;
            var waitingApproval = _bridge.CurrentState?.Status == "waiting_approval";

            if (waitingApproval || activity == "waiting")
            {
                _status = "Waiting";
                _icon = "waiting";
                this.SetBusy(false);
            }
            else if (activity == "busy")
            {
                _status = "Working";
                this.SetBusy(true);
            }
            else
            {
                _status = "Ready";
                _icon = "done";
                this.SetBusy(false);
            }

            this.ActionImageChanged();
        }

        // Animate the "Working" face (~2.5 fps) only while busy; static otherwise.
        private void SetBusy(Boolean busy)
        {
            if (busy == _busy)
            {
                return;
            }

            _busy = busy;
            if (busy)
            {
                _frame = 0;
                _animTimer = new Timer(_ => { _frame++; this.ActionImageChanged(); }, null, 400, 400);
            }
            else
            {
                _animTimer?.Dispose();
                _animTimer = null;
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            // Display-only indicator. No terminal action on press.
            PluginLog.Info("StatusCommand: pressed (display-only)");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
            => _status;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var icon = _busy ? BusyFrames[_frame % BusyFrames.Length] : _icon;
            return KeyImage.Render(imageSize, _status, KeyImage.Dark, icon);
        }
    }
}
