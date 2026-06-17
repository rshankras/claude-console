namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Linq;

    /// <summary>
    /// Roller: Switch between Claude Code sessions.
    /// Reads the session registry and rotates the active session.
    /// </summary>
    public class SessionSwitchAdjustment : PluginDynamicAdjustment
    {
        private readonly BridgeManager _bridge;

        public SessionSwitchAdjustment()
            : base(displayName: "Switch Session", description: "Roll to switch between Claude Code sessions", groupName: "Adjustments", hasReset: false)
        {
            _bridge = BridgeManager.Instance;
        }

        protected override void ApplyAdjustment(String actionParameter, Int32 diff)
        {
            var sessions = _bridge.GetSessions();
            if (sessions.Count <= 1)
            {
                return;
            }

            var currentIdx = sessions.FindIndex(s => s.Id == _bridge.ActiveSessionId);
            if (currentIdx < 0)
            {
                currentIdx = 0;
            }

            var nextIdx = ((currentIdx + diff) % sessions.Count + sessions.Count) % sessions.Count;
            _bridge.SetActiveSession(sessions[nextIdx].Id);
            this.AdjustmentValueChanged();
            PluginLog.Info($"SessionSwitchAdjustment: Switched to session {sessions[nextIdx].Id}");
        }

        protected override String GetAdjustmentValue(String actionParameter)
        {
            var sessions = _bridge.GetSessions();
            var current = sessions.Find(s => s.Id == _bridge.ActiveSessionId);
            if (current != null && !String.IsNullOrEmpty(current.Project))
            {
                return current.Project;
            }
            return _bridge.ActiveSessionId ?? "default";
        }
    }
}
