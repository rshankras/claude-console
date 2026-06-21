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
        private Int32 _percent;
        private Int32 _usedTokens;
        private Int32 _maxTokens;

        public ContextCommand()
            : base(displayName: "Context", description: "Live context-window usage (press for /context)", groupName: "Core")
        {
            _bridge = BridgeManager.Instance;

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

                if (pct != _percent || used != _usedTokens || max != _maxTokens)
                {
                    _percent = pct;
                    _usedTokens = used;
                    _maxTokens = max;
                    this.ActionImageChanged();
                }
            };
        }

        protected override void RunCommand(String actionParameter)
        {
            _bridge.SendPrompt("/context");
            PluginLog.Info("ContextCommand: /context");
        }

        // "33%" — plus "325k/1M" once the window size is known.
        private String Label()
        {
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
