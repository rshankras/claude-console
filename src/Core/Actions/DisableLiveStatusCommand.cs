namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// Disable Live Status (group "Setup &amp; Privacy") — takes exactly the plugin's entries back out
    /// of ~/.claude/settings.json (a chained status line goes back to what it was) and leaves the
    /// Off marker, so the live keys read "Off" and no later load wires anything (#31). This class
    /// can only ever disable: it never calls Enable, and a test pins that.
    /// </summary>
    public class DisableLiveStatusCommand : PluginDynamicCommand
    {
        private const String Disable = "disable";

        public DisableLiveStatusCommand()
            : base()
        {
            var agent = BridgeManager.Instance.Agent;
            if (!agent.Capabilities.SettingsFileWiring)
            {
                return;
            }

            this.AddParameter(Disable, "Disable Live Status", LiveStatusFace.SetupGroup)
                .SetDescription(
                    "Removes the plugin's hooks and status line from ~/.claude/settings.json and puts back a " +
                    "status line it had chained. Your own entries are untouched. The Cost / Context / Activity " +
                    "keys read Off until you enable them again.");
        }

        protected override void RunCommand(String actionParameter)
        {
            PluginLog.Info("DisableLiveStatusCommand: pressed");
            BridgeManager.Instance.DisableLiveStatus();
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            "Disable Live Status";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            KeyImage.Render(imageSize, "Disable Live Status", KeyImage.Slate, "off");
    }
}
