namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Linq;

    /// <summary>
    /// Direct model-switch keys (group "Core"): Opus, Sonnet, Haiku. Each types "/model &lt;name&gt;"
    /// into Claude Code — one tap straight to a specific model instead of cycling. Model switching
    /// is the user's single most-frequent action, and the common move is Haiku (cheap tasks) ↔
    /// Opus (hard tasks). The Model key (ModelCycleCommand) still shows the current model live and
    /// cycles on press; these are the fast direct alternatives. Rendered in model-purple.
    /// </summary>
    public class ModelSetCommand : PluginDynamicCommand
    {
        private static readonly (String Id, String Name)[] Models =
        {
            ("opus",   "Opus"),
            ("sonnet", "Sonnet"),
            ("haiku",  "Haiku"),
        };

        public ModelSetCommand()
            : base()
        {
            foreach (var m in Models)
            {
                this.AddParameter(m.Id, m.Name, "Core");
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            BridgeManager.Instance.SendPrompt($"/model {actionParameter}");
            PluginLog.Info($"ModelSetCommand: /model {actionParameter}");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            var entry = Models.FirstOrDefault(m => m.Id == actionParameter);
            return entry.Name ?? actionParameter;
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var entry = Models.FirstOrDefault(m => m.Id == actionParameter);
            // Opus/Sonnet/Haiku share one icon, so show the name as text instead (distinguishable).
            return KeyImage.Render(imageSize, entry.Name ?? actionParameter, KeyImage.Purple);
        }
    }
}
