namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// LCD Key 4: Model Cycle — cycles through opus/sonnet/haiku.
    /// Writes a switch_model command to cmd-queue.jsonl.
    /// LCD display updates to show current model name from statusline.
    /// </summary>
    public class ModelCycleCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;
        private readonly String[] _models = { "opus", "sonnet", "haiku" };
        private Int32 _currentIndex;
        private String _displayName = "Model";

        public ModelCycleCommand()
            : base(displayName: "Model", description: "Cycle through Claude models (Opus / Sonnet / Haiku)", groupName: "Core")
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
            _currentIndex = (_currentIndex + 1) % _models.Length;
            // Types "/model <name>" into the terminal — the built-in Claude Code slash command.
            _bridge.SendPrompt($"/model {_models[_currentIndex]}");
            PluginLog.Info($"ModelCycleCommand: Switching to {_models[_currentIndex]}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            return $"Model{Environment.NewLine}{_displayName}";
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            // Brain tinted to the CURRENT model's colour (matches the Opus/Sonnet/Haiku keys);
            // falls back to the neutral white brain until the live model is known.
            var key = (_displayName ?? "").ToLowerInvariant();
            var icon = key == "opus" || key == "sonnet" || key == "haiku" ? $"brain_{key}" : "brain";
            return KeyImage.Render(imageSize, $"Model\n{_displayName}", KeyImage.Purple, icon);
        }
    }
}
