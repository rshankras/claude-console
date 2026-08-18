namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Linq;

    /// <summary>
    /// Git keys (group "Git"). A single auto-discovered command exposes one SDK action per entry
    /// via AddParameter; pressing a key sends a natural-language git instruction to Claude Code
    /// (rather than a slash command, so it works without any custom commands installed).
    /// Core-essentials subset — add rows to expand to the full git page.
    /// </summary>
    public class GitCommand : PluginDynamicCommand
    {
        private static readonly (String Id, String Name, String Prompt)[] Commands =
        {
            ("commit",    "Commit",    "Commit my changes with a clear, conventional commit message"),
            ("diff",      "Diff",      "Show me the current git diff"),
            ("push",      "Push",      "Push my commits to the remote"),
            ("create_pr", "Create PR", "Create a pull request for the current branch"),
            ("status",    "Status",    "Show me the git status"),
            ("log",       "Log",       "Show me the recent git commits"),
        };

        public GitCommand()
            : base()
        {
            foreach (var c in Commands)
            {
                this.AddParameter(c.Id, c.Name, "Git")
                    .SetDescription("Sends Claude: " + c.Prompt);
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            var entry = Commands.FirstOrDefault(c => c.Id == actionParameter);
            if (entry.Prompt == null)
            {
                return;
            }

            BridgeManager.Instance.SendPrompt(entry.Prompt);
            PluginLog.Info($"GitCommand: Sent '{entry.Id}'");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            var entry = Commands.FirstOrDefault(c => c.Id == actionParameter);
            return entry.Name ?? actionParameter;
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var entry = Commands.FirstOrDefault(c => c.Id == actionParameter);
            return KeyImage.Render(imageSize, entry.Name ?? actionParameter, KeyImage.Orange, actionParameter);
        }
    }
}
