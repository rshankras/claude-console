namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// "Model" key (group "Core"). Live-displays the CURRENT model as a colour-coded brain (read
    /// from the status line) and, on press, sends "/model" to open Claude Code's built-in model
    /// picker — navigate it with the Answer Up/Down/Return keys. Replaces the old direct
    /// Opus/Sonnet/Haiku keys: the picker is always current (no hardcoded model list) and there's
    /// no cycle-index drift. (Class name is historical — it used to cycle opus/sonnet/haiku; kept
    /// as-is so existing key bindings survive the behaviour change.)
    /// </summary>
    public class ModelCycleCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;
        private String _displayName = "Model";

        public ModelCycleCommand()
            : base(displayName: "Model", description: "Current model; press to open the /model picker", groupName: "Core")
        {
            _bridge = BridgeManager.Instance;

            _bridge.OnStateChanged += (state) =>
            {
                if (state.Model?.DisplayName != null)
                {
                    // Shorten "Opus 4.8 (1M context)" -> "Opus" so it fits the key.
                    _displayName = state.Model.DisplayName.Split(' ')[0];
                    this.ActionImageChanged();
                }
            };
        }

        protected override void RunCommand(String actionParameter)
        {
            // Open the built-in picker rather than guessing the next model — always current, no drift.
            _bridge.SendPrompt("/model");
            PluginLog.Info("ModelCycleCommand: opened /model picker");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            // Static "Model" label — the brain icon's colour (set in GetCommandImage from the live
            // model) is what tells you which model you're on.
            return "Model";
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            // Brain tinted to the CURRENT model's colour; falls back to the neutral brain until the
            // live model is known.
            var key = (_displayName ?? "").ToLowerInvariant();
            var icon = key == "opus" || key == "sonnet" || key == "haiku" ? $"brain_{key}" : "brain";
            return KeyImage.Render(imageSize, "Model", KeyImage.Purple, icon);
        }
    }
}
