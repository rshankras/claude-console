namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Threading;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// The ambient status light — the key you glance at instead of ⌘-Tabbing to check on the
    /// agent. Three honest states from control presence (Stop visible = Working, approval card
    /// = Waiting, neither = Ready) plus the one the terminal products never needed: Hidden,
    /// when the surface itself is unreadable (screen locked, window closed, helper failing).
    /// Hidden is a real state on a GUI surface and must look different from Ready — a grey
    /// "can't see" is honest, a green "Ready" over a locked screen is a lie.
    ///
    /// Display-only; the hourglass animates while Working so the key reads as alive.
    /// </summary>
    public class DesktopStatusCommand : PluginDynamicCommand
    {
        private static readonly String[] BusyFrames = { "busy0", "busy1" };

        private Timer _animTimer;
        private Int32 _frame;
        private Boolean _busy;

        public DesktopStatusCommand()
            : base(displayName: "Activity", description: "Whether the agent is working, waiting on you, ready — or hidden", groupName: "Agent")
        {
            if (DesktopServices.Declared)
            {
                DesktopServices.Monitor.OnChanged += _ => this.Refresh();
            }
        }

        private void Refresh()
        {
            this.SetBusy(DesktopServices.Monitor.Current.Activity == DesktopActivity.Working);
            this.ActionImageChanged();
        }

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

        protected override void RunCommand(String actionParameter) =>
            PluginLog.Info("DesktopStatusCommand: pressed (display-only)");

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            Face().label;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var (label, icon) = Face();
            if (_busy)
            {
                icon = BusyFrames[_frame % BusyFrames.Length];
            }

            return KeyImage.Render(imageSize, label, KeyImage.Dark, icon);
        }

        private static (String label, String icon) Face()
        {
            var state = DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable;
            return state.Activity switch
            {
                DesktopActivity.Working => ("Working", "busy0"),
                DesktopActivity.WaitingApproval => ("Waiting", "waiting"),
                DesktopActivity.Ready => ("Ready", "done"),
                _ => ("Hidden", "status"),
            };
        }
    }
}
