namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Agents;

    /// <summary>
    /// Model picker with a fixed brain icon. The historical class name is retained so existing
    /// key bindings keep working. Press to open the agent model picker; navigate with Up/Down/Return.
    /// </summary>
    public class ModelCycleCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;

        public ModelCycleCommand()
            : base(displayName: "Model", description: "Open the /model picker", groupName: "Core")
        {
            _bridge = BridgeManager.Instance;
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
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            "Model";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            KeyImage.Render(imageSize, "Model", KeyImage.Purple, "brain");
    }
}
