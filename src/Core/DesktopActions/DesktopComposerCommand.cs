namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>The two final Controls positions: submit a reviewed draft and inspect its output.</summary>
    public class DesktopComposerCommand : DesktopCommandBase
    {
        private readonly FailureFace _feedback;
        private readonly PressHandler _handler = new PressHandler();
        private String _feedbackParameter;
        private String _shownSendStop, _feedbackIntent;

        public DesktopComposerCommand()
        {
            this.SetWidget(true);
            _feedback = new FailureFace(() => this.ActionImageChanged(), holdMs: 1800);
            DesktopServices.Lifetime.OnStop(_feedback.Dispose);
            if (!DesktopServices.Declared) { return; }
            this.AddParameter("send", "Send Draft", "Agent")
                .SetDescription("Send the existing draft in the target window; never replaces composer text");
            this.AddParameter("send_stop", "Send / Stop", "Agent")
                .SetDescription("Send the reviewed draft, or stop the response when the key shows Stop");
            this.AddParameter("output", "Copy Answer / View Changes", "Adaptive")
                .SetDescription("ChatGPT: copy an identifiable completed answer when supported. Codex: open Changes.");
            DesktopServices.OnMonitorChanged(_ => this.ActionImageChanged());
        }

        protected override void RunCommand(String actionParameter)
        {
            var shownIntent = _shownSendStop;
            DesktopServices.Run(() => this.RunDesktopCommand(actionParameter, shownIntent));
        }

        private void RunDesktopCommand(String actionParameter, String shownIntent)
        {
            if (!DesktopServices.Declared) { return; }
            _feedbackParameter = actionParameter;
            _feedback.Clear();
            _feedbackIntent = shownIntent;
            var feedback = _handler.Execute(actionParameter, DesktopServices.App, DesktopServices.Automation, shownIntent);
            if (feedback != null) { _feedback.Show(feedback); }
        }

        internal sealed class PressHandler
        {
            private readonly Object _sendGate = new Object();
            private Int64? _lastSend;
            internal Func<Int64> Clock { get; set; } = () => Environment.TickCount64;

            internal String Execute(String actionParameter, IDesktopAppAdapter app, IDesktopAutomation automation, String shownIntent = null)
            {
                if (actionParameter == "send_stop")
                {
                    // Execute only the verb shown on the key. A response finishing between
                    // glance and press must never turn a Stop request into a submission.
                    if (shownIntent is not ("send" or "stop")) return "Unavailable";
                    if (shownIntent == "stop")
                    {
                        lock (_sendGate)
                        {
                            if (_lastSend.HasValue && Clock() - _lastSend.Value < 1000) return null;
                            if (!automation.PressExact(app.StopLabels)) return "Nothing Running";
                            _lastSend = Clock(); return "Stop Requested";
                        }
                    }
                    actionParameter = "send";
                }
                if (actionParameter == "send")
                {
                    lock (_sendGate)
                    {
                        if (_lastSend.HasValue && Clock() - _lastSend.Value < 1000) { return null; }
                        // Eligibility and submission happen in one pinned-window invocation.
                        if (automation.SendComposer(out var error))
                        {
                            if (DesktopServices.Declared) DesktopServices.WorkflowVoice.Reset();
                            _lastSend = Clock();
                            return "Sent";
                        }
                        PluginLog.Warning($"DesktopComposerCommand(send): {error}");
                        return error == "no-sendable-draft" ? "No Draft" : "Not Sent";
                    }
                }
                if (actionParameter != "output") { return null; }
                var snapshot = automation.Status();
                if (String.Equals(snapshot.Mode, "Codex", StringComparison.OrdinalIgnoreCase))
                {
                    return DesktopNavigateCommand.OpenChanges(automation);
                }
                if (String.Equals(snapshot.Mode, "ChatGPT", StringComparison.OrdinalIgnoreCase))
                {
                    if (!automation.SupportsCopyAnswer) { return "Unsupported"; }
                    return snapshot.SurfaceAvailable && snapshot.CanCopyAnswer && !snapshot.StopPresent
                        && !snapshot.ApprovalPresent && automation.CopyAnswer(out _) ? "Copied" : "No Answer";
                }
                return null;
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "\u200B";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var state = DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable;
            if (actionParameter == "send_stop") _shownSendStop = SendStopIntent(state);
            var face = FaceFor(actionParameter,
                state,
                DesktopServices.Declared && DesktopServices.Automation.SupportsCopyAnswer);
            var status = _feedback.IsActive && _feedbackParameter == actionParameter
                && (actionParameter != "send_stop" || _feedbackIntent == _shownSendStop) ? _feedback.Text : face.Status;
            return KeyImage.RenderControlTile(imageSize, face.Label, face.Icon, face.Enabled, status);
        }

        internal static (String Label, String Icon, String Status, Boolean Enabled) FaceFor(
            String action, DesktopState state, Boolean supportsCopyAnswer = false)
        {
            if (action == "send_stop")
                return SendStopIntent(state) == "stop" ? ("Stop", "stop", "RESPONSE", true)
                    : ("Send", "send", !state.Available ? "Unavailable" : state.Activity == DesktopActivity.WaitingApproval
                        ? "Approval" : state.CanSend ? "REVIEW FIRST" : "No draft", SendStopIntent(state) == "send");
            var unavailable = !state.Available ? "Unavailable" : null;
            var busy = state.Activity == DesktopActivity.Working ? "Busy" :
                state.Activity == DesktopActivity.WaitingApproval ? "Approval" : null;
            if (action == "send")
            {
                var status = unavailable ?? busy ?? (state.CanSend ? null : "No draft");
                return ("Send Draft", "send", status, status == null);
            }
            if (action == "output" && state.Mode == "Codex")
            {
                var status = unavailable ?? (state.AvailableControls.HasFlag(DesktopControl.Changes) ? null : "Not available");
                return ("View Changes", "diff", status, status == null);
            }
            if (action == "output" && state.Mode == "ChatGPT")
            {
                var status = !supportsCopyAnswer ? "Unsupported" : unavailable ?? busy ?? (state.CanCopyAnswer ? null : "No answer");
                return ("Copy Answer", "copy", status, status == null);
            }
            return ("Output", "copy", unavailable ?? "Mode?", false);
        }

        internal static String SendStopIntent(DesktopState state) => !state.Available ? null
            : state.Activity == DesktopActivity.Working ? "stop"
            : state.Activity == DesktopActivity.Ready && state.CanSend ? "send" : null;
    }
}
