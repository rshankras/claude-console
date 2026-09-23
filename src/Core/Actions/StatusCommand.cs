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
        private readonly LiveStatusGate _gate;
        private String _status = "Ready";
        private String _icon = "done";
        private Boolean _busy;
        private Timer _animTimer;
        private Int32 _frame;

        public StatusCommand()
            : base(
                displayName: "Activity",
                description: BridgeManager.Instance.Agent.Capabilities.SettingsFileWiring
                    ? $"Shows whether {BridgeManager.Instance.Agent.DisplayName} is working, waiting, or ready (press to turn live status on, hold to turn it off)"
                    : $"Shows whether {BridgeManager.Instance.Agent.DisplayName} is working, waiting, or ready",
                groupName: "Core")
        {
            _bridge = BridgeManager.Instance;
            // Without the hooks this key used to fall through to "Ready" forever — a value the agent
            // never reported. Until live status is set up it says so instead; the first press arms it and
            // says what a second press will change, the second press enables (#31) — see LiveStatusGate.
            _gate = new LiveStatusGate(_bridge, "Activity", () => this.ActionImageChanged());
            _bridge.OnActivityChanged += (_) => this.Refresh();
            _bridge.OnStateChanged += (_) => this.Refresh();
        }

        // Map the activity flag to a face + word.
        //
        // This key is subscribed to BOTH the state and activity streams, and it used to repaint at
        // the end of every Refresh whether or not the word or the icon had moved — 2,658 renders in
        // 18 minutes, the largest single contributor to the redraw storm (#27). It is also why the
        // same action appeared twice in the same millisecond: two streams, one unconditional
        // repaint each. Comparing before painting collapses both, with no change in what is shown.
        private void Refresh()
        {
            var previousStatus = _status;
            var previousIcon = _icon;
            var activity = _bridge.CurrentActivity?.State;

            // Until 1.5.0 this also tested CurrentState.Status == "waiting_approval" as a
            // "works without hooks" fallback. Claude Code's status line never sends a `status`
            // field, so that branch was dead — the key silently read Ready forever. The real signal
            // is the activity hooks, and now also the grid, which knows WHICH session is waiting.
            var target = _bridge.RoutingTty();
            var waitingApproval = !String.IsNullOrEmpty(target)
                && _bridge.Grid.Sessions.TryGetValue(target, out var session)
                && session.Risk != ApprovalRisk.None;

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

            // The busy animation drives its own repaints through _animTimer; it does not need one
            // here, and a face that has not changed does not need one at all.
            if (_status != previousStatus || _icon != previousIcon)
            {
                this.ActionImageChanged();
            }
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

        // The key owns its button events: the service would otherwise run the short action the
        // instant the key goes down, before it can know a long press is coming. Short press = the
        // key's own job (on release); long press = the way off. See LiveStatusGate.
        protected override Boolean ProcessButtonEvent2(String actionParameter, DeviceButtonEvent2 buttonEvent) =>
            _gate.HandleButton(buttonEvent.EventType, () => this.RunCommand(actionParameter));

        protected override void RunCommand(String actionParameter)
        {
            // Display-only indicator. No terminal action on press.
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
            => _gate.Label ?? _status;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var icon = _busy ? BusyFrames[_frame % BusyFrames.Length] : _icon;
            return KeyImage.Render(imageSize, _gate.Label ?? _status, KeyImage.Dark, icon);
        }
    }
}
