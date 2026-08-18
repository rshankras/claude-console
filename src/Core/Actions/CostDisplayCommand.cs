namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// LCD Key 5: Live Cost Display — session cost and token count, where the agent reports them.
    ///
    /// Not every agent does. Codex bills a subscription and reports no spend at all, so this key
    /// must not render "$0.00" — a zero is indistinguishable from a genuinely free session, and the
    /// whole value of the hardware is that a glance tells the truth. On such an agent the key falls
    /// back to what IS known (the model), and pressing it types nothing rather than a command the
    /// agent would reject.
    /// </summary>
    public class CostDisplayCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;
        private Decimal _cost;
        private Int32 _tokens;
        private String _model;

        private Boolean ReportsCost => this._bridge.Agent.Capabilities.Cost;

        public CostDisplayCommand()
            : base(displayName: "Cost", description: "Shows live session cost and token count", groupName: "Core")
        {
            _bridge = BridgeManager.Instance;

            _bridge.OnStateChanged += (state) =>
            {
                var newCost = state.Cost?.TotalCostUsd ?? 0;
                var newTokens = state.ContextWindow?.TotalInputTokens ?? 0;
                var newModel = state.Model?.DisplayName;

                if (newCost != _cost || newTokens != _tokens || newModel != _model)
                {
                    _cost = newCost;
                    _tokens = newTokens;
                    _model = newModel;
                    this.ActionImageChanged();
                }
            };
        }

        protected override void RunCommand(String actionParameter)
        {
            if (!this.ReportsCost)
            {
                PluginLog.Info($"CostDisplayCommand: {_bridge.Agent.DisplayName} reports no cost — nothing to show");
                return;
            }

            _bridge.SendPrompt("/cost");
            PluginLog.Info("CostDisplayCommand: Requested /cost details");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            this.ReportsCost
                ? $"${_cost:F2}{Environment.NewLine}{TokenText(_tokens)}"
                : (String.IsNullOrEmpty(_model) ? "—" : _model);

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            // Face shows the $ icon; the live cost/tokens go in the LABEL (GetCommandDisplayName)
            // so the value isn't drawn twice. Falls back to the value text if the icon is missing.
            var text = this.ReportsCost
                ? $"${_cost:F2}\n{TokenText(_tokens)}"
                : (String.IsNullOrEmpty(_model) ? "—" : _model);

            return KeyImage.Render(imageSize, text, KeyImage.Dark, this.ReportsCost ? "cost" : "brain");
        }

        private static String TokenText(Int32 tokens) =>
            tokens >= 1000 ? $"{tokens / 1000}K tok" : $"{tokens} tok";
    }
}
