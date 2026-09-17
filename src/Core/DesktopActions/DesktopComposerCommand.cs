namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>The two final Controls positions: submit a reviewed draft and inspect its output.</summary>
    public class DesktopComposerCommand : PluginDynamicCommand
    {
        private readonly FailureFace _feedback;
        private readonly Object _sendGate = new Object();
        private Int64 _lastSend;
        private String _feedbackParameter;

        public DesktopComposerCommand()
        {
            _feedback = new FailureFace(() => this.ActionImageChanged(), holdMs: 1800);
            if (!DesktopServices.Declared) { return; }
            this.AddParameter("send", "Send Draft", "Agent")
                .SetDescription("Send the existing draft in the target window; never replaces composer text");
            this.AddParameter("output", "Copy Answer / Changes", "Adaptive")
                .SetDescription("ChatGPT: copy an identifiable completed answer when supported. Codex: open Changes.");
            DesktopServices.Monitor.OnChanged += _ => this.ActionImageChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            if (!DesktopServices.Declared) { return; }
            var automation = DesktopServices.Automation;
            _feedbackParameter = actionParameter;
            _feedback.Clear();
            if (actionParameter == "send")
            {
                lock (_sendGate)
                {
                    var now = Environment.TickCount64;
                    if (_lastSend != 0 && now - _lastSend < 1000) { return; }
                    // The helper makes the eligibility decision and submits in one pinned-window
                    // invocation. A separate Status -> generic Press would introduce retargeting.
                    if (automation.SendComposer(out var error))
                    {
                        _lastSend = Environment.TickCount64;
                        _feedback.Show("Sent");
                    }
                    else
                    {
                        _feedback.Show(error == "no-sendable-draft" ? "No Draft" : "Not Sent");
                        PluginLog.Warning($"DesktopComposerCommand(send): {error}");
                    }
                }
                return;
            }
            if (actionParameter != "output") { return; }
            var snapshot = automation.Status();
            if (String.Equals(snapshot.Mode, "Codex", StringComparison.OrdinalIgnoreCase))
            {
                if (!snapshot.SurfaceAvailable || !snapshot.AvailableControls.HasFlag(DesktopControl.Changes)
                    || !automation.PressInMode(DesktopServices.App.ControlLabels(DesktopControl.Changes), snapshot.Mode, out _))
                {
                    _feedback.Show("No Changes");
                }
            }
            else if (String.Equals(snapshot.Mode, "ChatGPT", StringComparison.OrdinalIgnoreCase))
            {
                // Capability remains false until assistant ownership is verified in the live app.
                _feedback.Show(snapshot.SurfaceAvailable && snapshot.CanCopyAnswer && !snapshot.StopPresent
                    && !snapshot.ApprovalPresent && automation.CopyAnswer(out _) ? "Copied" : "No Answer");
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            _feedback.IsActive && _feedbackParameter == actionParameter ? _feedback.Text : LabelFor(actionParameter,
                DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable);

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            KeyImage.Render(imageSize, this.GetCommandDisplayName(actionParameter, imageSize), KeyImage.Blue,
                actionParameter == "send" ? "enter" :
                DesktopServices.Declared && DesktopServices.Monitor.Current.Mode == "Codex" ? "diff" : "copy");

        internal static String LabelFor(String action, DesktopState state)
        {
            if (!state.Available) { return "Unavailable"; }
            if (action == "send") { return state.CanSend ? "Send" : "No Draft"; }
            if (action != "output") { return "Unavailable"; }
            return state.Mode switch
            {
                "ChatGPT" => state.CanCopyAnswer ? "Copy Answer" : "No Answer",
                "Codex" => state.AvailableControls.HasFlag(DesktopControl.Changes) ? "Changes" : "No Changes",
                _ => "Mode?",
            };
        }
    }
}
