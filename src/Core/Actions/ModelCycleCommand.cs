namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Agents;

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
        private Boolean _hasData = true;

        public ModelCycleCommand()
            : base(displayName: "Model", description: "Current model; press to open the /model picker", groupName: "Core")
        {
            _bridge = BridgeManager.Instance;

            _bridge.OnStateChanged += (state) =>
            {
                if (state.Model?.DisplayName != null)
                {
                    // Shorten "Opus 4.8 (1M context)" -> "Opus" so it fits the key.
                    var name = state.Model.DisplayName.Split(' ')[0];

                    // Repaint only when the WORD on the key actually changes. The model changes a
                    // handful of times a day, but this redrew on every state event — 1,642 renders
                    // in 18 minutes, the second largest contributor to the redraw storm (#27).
                    if (name != _displayName || !_hasData)
                    {
                        _displayName = name;
                        _hasData = true;
                        this.ActionImageChanged();
                    }
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

        protected override void RunCommand(String actionParameter)
        {
            // Open the built-in picker rather than guessing the next model — always current, no drift.
            // The agent's own picker, by its own name — both agents call it /model today, but the
            // key must not assume that. Null would mean an agent with no picker at all.
            var command = _bridge.Agent.SlashCommand(AgentVerb.Model);
            if (command != null)
            {
                _bridge.SendPrompt(command);
            }
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
            // live model is known — including when the session on the display keys has reported
            // nothing, since the last writer's model would belong to a different session (#49).
            var key = _hasData ? (_displayName ?? "").ToLowerInvariant() : "";
            var icon = key == "opus" || key == "sonnet" || key == "haiku" ? $"brain_{key}" : "brain";
            return KeyImage.Render(imageSize, "Model", KeyImage.Purple, icon);
        }
    }
}
