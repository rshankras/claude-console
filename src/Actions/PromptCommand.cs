namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Linq;

    /// <summary>
    /// Quick-prompt keys (group "Prompts"). A single auto-discovered command exposes one SDK
    /// action per entry via AddParameter; pressing a key types that prompt into the terminal.
    /// This is the Core-essentials subset — add rows to expand to the full prompt page.
    /// </summary>
    public class PromptCommand : PluginDynamicCommand
    {
        private static readonly (String Id, String Name, String Prompt)[] Prompts =
        {
            ("fix_bug",     "Fix Bug",     "Fix the bug in the current file"),
            ("write_tests", "Write Tests", "Write tests for the changes you just made"),
            ("explore",     "Explore",     "Explore this codebase and explain its structure, key files, and how it works"),
            ("explain",     "Explain",     "Explain how this code works"),
            ("refactor",    "Refactor",    "Refactor this for clarity"),
            ("review",      "Review",      "Review this code for bugs and issues"),
            ("optimize",    "Optimize",    "Optimize this for performance"),
            ("security",    "Security",    "Check this code for security vulnerabilities"),
            ("document",    "Document",    "Add documentation to this code"),
            ("deploy",      "Deploy",      "Deploy this project"),
        };

        public PromptCommand()
            : base()
        {
            foreach (var p in Prompts)
            {
                this.AddParameter(p.Id, p.Name, "Prompts");
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            var entry = Prompts.FirstOrDefault(p => p.Id == actionParameter);
            if (entry.Prompt == null)
            {
                return;
            }

            BridgeManager.Instance.SendPrompt(entry.Prompt);
            PluginLog.Info($"PromptCommand: Sent prompt '{entry.Id}'");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            var entry = Prompts.FirstOrDefault(p => p.Id == actionParameter);
            return entry.Name ?? actionParameter;
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var entry = Prompts.FirstOrDefault(p => p.Id == actionParameter);
            // Deploy stands out green (ship); the rest are prompt-blue.
            var color = actionParameter == "deploy" ? KeyImage.Green : KeyImage.Blue;
            return KeyImage.Render(imageSize, entry.Name ?? actionParameter, color, actionParameter);
        }
    }
}
