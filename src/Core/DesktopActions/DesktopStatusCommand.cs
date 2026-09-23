namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// The ambient status light — the key you glance at instead of ⌘-Tabbing to check on the
    /// agent. Three honest states from control presence (Stop visible = Working, approval card
    /// = Waiting, neither = Ready) plus the one the terminal products never needed: Hidden,
    /// when the surface itself is unreadable (screen locked, window closed, helper failing).
    /// Hidden is a real state on a GUI surface and must look different from Ready — a grey
    /// "can't see" is honest, a green "Ready" over a locked screen is a lie.
    ///
    /// Display-only. Working uses a static hourglass: the monitor already refreshes on material
    /// state changes, and a display key must not create a permanent LCD redraw loop off-profile.
    /// </summary>
    public class DesktopStatusCommand : DesktopCommandBase
    {
        public DesktopStatusCommand()
            : base(displayName: "Activity", description: "Whether the agent is working, waiting on you, ready — or hidden", groupName: "Agent")
        {
            if (DesktopServices.Declared)
            {
                DesktopServices.OnMonitorChanged(_ => this.Refresh());
            }
        }

        private void Refresh()
        {
            this.ActionImageChanged();
        }

        protected override void RunCommand(String actionParameter) =>
            Execute();

        internal static void Execute() { /* Display-only indicator. */ }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            Face().label;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var (label, icon) = Face();
            return KeyImage.Render(imageSize, label, KeyImage.Blue, icon);
        }

        private static (String label, String icon) Face()
        {
            var state = DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable;
            return FaceFor(state);
        }

        internal static (String label, String icon) FaceFor(DesktopState state)
        {
            return state.Activity switch
            {
                DesktopActivity.Working => ("Working", "busy0"),
                DesktopActivity.WaitingApproval => ("Waiting", "waiting"),
                DesktopActivity.Ready => ("Ready", "done"),
                // Distinct truths, distinct faces (review round: fail closed and say why) —
                // each names the user's own next action: unlock, grant, launch, or wait.
                _ => state.Reason switch
                {
                    "hidden" => ("Hidden", "status"),
                    "no-permission" => ("No Access", "esc"),
                    "not-running" => ("App Off", "terminal"),
                    _ => ("No Signal", "status"),
                },
            };
        }
    }
}
