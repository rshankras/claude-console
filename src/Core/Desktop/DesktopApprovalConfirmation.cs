namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;

    // The app exposes no durable approval ID. Bind the arm to all observable identity fields
    // and invalidate it whenever the monitor sees that request leave or change.
    internal sealed class DesktopApprovalConfirmation
    {
        private readonly Object _gate = new Object();
        private DesktopState _request;
        private String _action;
        private DateTime _until;

        private Boolean SameRequest(DesktopState state) => _request != null
            && state.Activity == DesktopActivity.WaitingApproval
            && state.Risk == _request.Risk
            && !String.IsNullOrWhiteSpace(state.CardText)
            && state.CardText == _request.CardText
            && state.ActiveTitle == _request.ActiveTitle
            && state.Mode == _request.Mode;

        public void Observe(DesktopState state)
        {
            lock (_gate)
            {
                if (!SameRequest(state)) Reset();
            }
        }

        public Boolean IsArmed(String action, DesktopState state, DateTime now)
        {
            lock (_gate) return SameRequest(state) && action == _action && now < _until;
        }

        public Boolean Confirm(String action, DesktopState state, DateTime now)
        {
            lock (_gate)
            {
                if (IsArmed(action, state, now))
                {
                    Reset();
                    return true;
                }
                Reset();
                if (state.Activity == DesktopActivity.WaitingApproval
                    && !String.IsNullOrWhiteSpace(state.CardText))
                {
                    _request = state;
                    _action = action;
                    _until = now.AddSeconds(3);
                }
                return false;
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _request = null;
                _action = null;
                _until = DateTime.MinValue;
            }
        }
    }
}
