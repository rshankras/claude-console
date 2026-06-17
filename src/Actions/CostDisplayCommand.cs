namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// LCD Key 5: Live Cost Display — shows session cost and token count.
    /// Updates from statusline data. Press to show /cost details in terminal.
    /// </summary>
    public class CostDisplayCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;
        private Decimal _cost;
        private Int32 _tokens;

        public CostDisplayCommand()
            : base(displayName: "Cost", description: "Shows live session cost and token count", groupName: "Core")
        {
            _bridge = BridgeManager.Instance;

            _bridge.OnStateChanged += (state) =>
            {
                var newCost = state.Cost?.TotalCostUsd ?? 0;
                var newTokens = state.ContextWindow?.TotalInputTokens ?? 0;

                if (newCost != _cost || newTokens != _tokens)
                {
                    _cost = newCost;
                    _tokens = newTokens;
                    this.ActionImageChanged();
                }
            };
        }

        protected override void RunCommand(String actionParameter)
        {
            _bridge.SendPrompt("/cost");
            PluginLog.Info("CostDisplayCommand: Requested /cost details");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            var tokenDisplay = _tokens >= 1000 ? $"{_tokens / 1000}K tok" : $"{_tokens} tok";
            return $"${_cost:F2}{Environment.NewLine}{tokenDisplay}";
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var tokenDisplay = _tokens >= 1000 ? $"{_tokens / 1000}K tok" : $"{_tokens} tok";
            return KeyImage.Render(imageSize, $"${_cost:F2}\n{tokenDisplay}", KeyImage.Dark);
        }
    }
}
