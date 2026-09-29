namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Agents;

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

        /// <summary>
        /// "Status" needs the "Git" qualifier only on an agent that ALSO has a session-status key,
        /// where two keys would otherwise read the same. Asked as a capability, not by agent name:
        /// the engine does not know whose keypad this is, only which verbs the agent answers to.
        /// </summary>
        internal static String LabelFor(String id, String name, IAgentAdapter agent) =>
            id == "status" && agent?.SlashCommand(AgentVerb.SessionStatus) != null
                ? "Git Status"
                : name;

        private static String LabelFor((String Id, String Name, String Prompt) entry) =>
            LabelFor(entry.Id, entry.Name, BridgeManager.Instance.Agent);

        public GitCommand()
            : base()
        {
            var agent = BridgeManager.Instance.Agent;
            foreach (var c in Commands)
            {
                this.AddParameter(c.Id, LabelFor(c.Id, c.Name, agent), "Git")
                    .SetDescription($"Sends {agent.DisplayName}: {c.Prompt}");
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
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            var entry = Commands.FirstOrDefault(c => c.Id == actionParameter);
            return entry.Name == null ? actionParameter : LabelFor(entry);
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var entry = Commands.FirstOrDefault(c => c.Id == actionParameter);
            var label = entry.Name == null ? actionParameter : LabelFor(entry);
            return KeyImage.Render(imageSize, label, KeyImage.Coral, actionParameter);
        }
    }
}
