namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// LCD Key 5: Live Cost Display — session cost and token count, where the agent reports them.
    ///
    /// Not every agent does. Codex bills a subscription and reports no spend at all, so this key
    /// must not render "$0.00" — a zero is indistinguishable from a genuinely free session, and the
    /// whole value of the hardware is that a glance tells the truth. On such an agent the key shows
    /// a dash and a press types nothing.
    ///
    /// It briefly showed the MODEL instead, which was worse: the profile already has a Model key, so
    /// the keypad carried two keys displaying the same thing and neither said what it was. A key
    /// with nothing to report should look empty, not borrow another key's job.
    /// </summary>
    public class CostDisplayCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;
        private readonly LiveStatusGate _gate;
        private Decimal _cost;
        private Int32 _tokens;
        private String _model;
        private Boolean _hasData = true;

        private Boolean ReportsCost => this._bridge.Agent.Capabilities.Cost;

        // Both reasons for a dash: this agent never reports cost, or this session hasn't reported yet.
        private Boolean HasValue => this.ReportsCost && _hasData;

        public CostDisplayCommand()
            : base(displayName: "Cost", description: "Shows live session cost and token count (press to turn live status on, hold to turn it off)", groupName: "Core")
        {
            _bridge = BridgeManager.Instance;
            // Until live status is set up the key says so instead of a value; the first press arms it and
            // says what a second press will change, the second press enables (#31) — see LiveStatusGate.
            _gate = new LiveStatusGate(_bridge, "Cost", () => this.ActionImageChanged());

            _bridge.OnStateChanged += (state) =>
            {
                var newCost = state.Cost?.TotalCostUsd ?? 0;
                var newTokens = state.ContextWindow?.TotalInputTokens ?? 0;
                var newModel = state.Model?.DisplayName;

                if (newCost != _cost || newTokens != _tokens || newModel != _model || !_hasData)
                {
                    _cost = newCost;
                    _tokens = newTokens;
                    _model = newModel;
                    _hasData = true;
                    this.ActionImageChanged();
                }
            };

            // The session on the display keys has reported nothing — a tab whose Claude has not run
            // a turn yet, or one started before the status-line bridge was wired. Show a dash rather
            // than the last writer's numbers, which would be a different session's (#49).
            _bridge.OnStateUnavailable += () =>
            {
                if (_hasData)
                {
                    _hasData = false;
                    this.ActionImageChanged();
                }
            };
        }

        // The key owns its button events: the service would otherwise run the short action the
        // instant the key goes down, before it can know a long press is coming. Short press = the
        // key's own job (on release); long press = the way off. See LiveStatusGate.
        protected override Boolean ProcessButtonEvent2(String actionParameter, DeviceButtonEvent2 buttonEvent) =>
            _gate.HandleButton(buttonEvent.EventType, () => this.RunCommand(actionParameter));

        protected override void RunCommand(String actionParameter)
        {
            if (!this.ReportsCost)
            {
                return;
            }

            _bridge.SendPrompt("/cost");
        }

        // The live value, or the setup state's words while they apply ("Set up" / "Off" / "Restart Claude").
        private String Label(String newline) =>
            _gate.Label ?? (this.HasValue ? $"${_cost:F2}{newline}{TokenText(_tokens)}" : "—");

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            this.Label(Environment.NewLine);

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            // Face shows the $ icon; the live cost/tokens go in the LABEL (GetCommandDisplayName)
            // so the value isn't drawn twice. Falls back to the value text if the icon is missing.
            return KeyImage.Render(imageSize, this.Label("\n"), KeyImage.Dark, this.ReportsCost ? "cost" : "brain");
        }

        private static String TokenText(Int32 tokens) =>
            tokens >= 1000 ? $"{tokens / 1000}K tok" : $"{tokens} tok";
    }
}
