namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    /// <summary>
    /// Enable Live Status (group "Setup &amp; Privacy") — the one thing that edits the user's Claude Code
    /// settings (#31). Its description says exactly what will change BEFORE the user presses; the
    /// press is the consent; the card afterwards confirms it and links the undo. Separate from
    /// Disable on purpose: a key that could do either would need its face read first.
    ///
    /// Only where the agent reads its hooks from the user's own settings file. Codex installs its
    /// own hooks.json behind its own trust prompt, so there the key would have nothing to do — and
    /// a key that does nothing is the bug (#29).
    /// </summary>
    public class EnableLiveStatusCommand : PluginDynamicCommand
    {
        private const String Enable = "enable";

        public EnableLiveStatusCommand()
            : base()
        {
            var agent = BridgeManager.Instance.Agent;
            if (!agent.Capabilities.SettingsFileWiring)
            {
                return;
            }

            this.AddParameter(Enable, "Enable Live Status", LiveStatusFace.SetupGroup)
                .SetDescription(
                    "Adds 5 hooks and a status line to ~/.claude/settings.json so the Cost / Context / Activity " +
                    "keys show live data. Chains a status line you already have (yours keeps running), backs the " +
                    "file up first, and takes effect on your next Claude Code session.");
        }

        protected override void RunCommand(String actionParameter)
        {
            PluginLog.Info("EnableLiveStatusCommand: pressed");
            BridgeManager.Instance.EnableLiveStatus();
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            "Enable Live Status";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
            KeyImage.Render(imageSize, "Enable Live Status", KeyImage.Green, "setup");
    }
}
