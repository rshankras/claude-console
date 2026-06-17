namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Dial: Navigate through user prompt history (from cmd-queue.jsonl).
    /// Clockwise = newer entries, Counter-clockwise = older entries.
    /// Press dial (reset) = resend the currently selected prompt.
    /// LCD shows the current prompt text and position.
    /// </summary>
    public class HistoryScrollAdjustment : PluginDynamicAdjustment
    {
        private readonly BridgeManager _bridge;
        private Int32 _position;
        private Int32 _total;
        private String _currentDisplay = "Prompts";

        public HistoryScrollAdjustment()
            : base(displayName: "Prompt History", description: "Rotate dial to browse prompt history, press to resend", groupName: "Adjustments", hasReset: true)
        {
            _bridge = BridgeManager.Instance;
        }

        protected override void ApplyAdjustment(String actionParameter, Int32 diff)
        {
            var prompts = _bridge.ReadPromptHistory();
            _total = prompts.Count;

            if (_total == 0)
            {
                _currentDisplay = "No prompts";
                this.AdjustmentValueChanged();
                return;
            }

            // Apply rotation: positive diff = forward (newer), negative = backward (older)
            _position = _bridge.SetDialPosition(_position + diff, _total, "prompts");

            // Build display string for LCD
            if (_position >= 0 && _position < prompts.Count)
            {
                var entry = prompts[_position];
                var value = entry.ContainsKey("value") ? entry["value"].ToString() : "?";

                // Truncate for LCD display (small screen)
                if (value.Length > 20)
                {
                    value = value.Substring(0, 17) + "...";
                }

                // Show icon: / for slash commands, > for regular prompts
                var icon = value.StartsWith("/") ? "/" : ">";
                _currentDisplay = $"{icon} {value}\n{_position + 1}/{_total}";
            }
            else
            {
                _currentDisplay = $"{_position + 1}/{_total}";
            }

            this.AdjustmentValueChanged();
            PluginLog.Verbose($"HistoryScrollAdjustment: pos={_position}/{_total}");
        }

        /// <summary>
        /// Dial press (reset) = resend the currently selected prompt to Claude Code.
        /// </summary>
        protected override void RunCommand(String actionParameter)
        {
            if (_bridge.ResendDialPrompt())
            {
                _currentDisplay = "Sent!";
                this.AdjustmentValueChanged();
                PluginLog.Info("HistoryScrollAdjustment: Resent prompt via dial press");
            }
            else
            {
                _currentDisplay = "No prompt";
                this.AdjustmentValueChanged();
            }
        }

        protected override String GetAdjustmentValue(String actionParameter) => _currentDisplay;
    }
}
