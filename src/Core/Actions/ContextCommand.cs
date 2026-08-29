namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// Context key (group "Core") — a single gauge icon with live context-window usage as the
    /// LABEL (e.g. "33%" + used/total tokens), read from the status line. Press → "/context" for
    /// the full breakdown. One icon on the face, the live value as the label (no double-draw).
    /// </summary>
    public class ContextCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;
        private readonly LiveStatusGate _gate;
        private Int32 _percent;
        private Int32 _usedTokens;
        private Int32 _maxTokens;

        // "33%" — plus "325k/1M" once the window size is known.
        private Boolean _hasData = true;

        public ContextCommand()
            : base(displayName: "Context", description: "Live context-window usage, press for /context (press to turn live status on, hold to turn it off)", groupName: "Core")
        {
            _bridge = BridgeManager.Instance;
            // Until live status is set up the key says so instead of a value; the first press arms it and
            // says what a second press will change, the second press enables (#31) — see LiveStatusGate.
            _gate = new LiveStatusGate(_bridge, "Context", () => this.ActionImageChanged());

            _bridge.OnStateChanged += (state) =>
            {
                var ctx = state.ContextWindow;
                var used = ctx?.TotalInputTokens ?? 0;
                var max = ctx?.MaxTokens ?? 0;
                var pctD = ctx?.UsedPercentage ?? 0;
                // Status line provides used_percentage directly; fall back to tokens/size if absent.
                var pct = pctD > 0 ? (Int32)Math.Round(pctD)
                        : max > 0 ? (Int32)Math.Round(100.0 * used / max)
                        : 0;

                if (pct != _percent || used != _usedTokens || max != _maxTokens || !_hasData)
                {
                    _percent = pct;
                    _usedTokens = used;
                    _maxTokens = max;
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
            _bridge.SendPrompt("/context");
            PluginLog.Info("ContextCommand: /context");
        }

        private String Label()
        {
            var setup = _gate.Label;
            if (setup != null)
            {
                return setup;
            }

            if (!_hasData)
            {
                return "—";
            }

            if (_maxTokens <= 0)
            {
                return $"{_percent}%";
            }

            var used = _usedTokens >= 1000 ? $"{_usedTokens / 1000}k" : _usedTokens.ToString();
            var max = _maxTokens >= 1_000_000 ? $"{_maxTokens / 1_000_000}M"
                    : _maxTokens >= 1000 ? $"{_maxTokens / 1000}k"
                    : _maxTokens.ToString();
            return $"{_percent}%\n{used}/{max}";
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
            => this.Label().Replace("\n", Environment.NewLine);

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            // Proactive warning: the gauge turns amber as the window fills, red when nearly full.
            var icon = _percent >= 90 ? "gauge_crit" : _percent >= 75 ? "gauge_warn" : "gauge";
            return KeyImage.Render(imageSize, this.Label(), KeyImage.Slate, icon);
        }
    }
}
