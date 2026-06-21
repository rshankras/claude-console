namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    /// <summary>
    /// Quick-prompt keys (group "Prompts"). One SDK action per entry; pressing a key types that
    /// prompt into the terminal. Entries load from ~/.claude/claude-console/prompts.json so users
    /// can bind their own prompts and macros. If that file is missing or invalid, the built-in
    /// defaults are used (and written out once as an editable starter). A key's colour comes from
    /// its icon — an embedded basename like "fix_bug" / "deploy"; an unknown icon falls back to text.
    /// </summary>
    public class PromptCommand : PluginDynamicCommand
    {
        private static readonly String ConfigFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "claude-console", "prompts.json");

        private static readonly PromptDef[] Defaults =
        {
            new PromptDef { Id = "fix_bug",     Label = "Fix Bug",     Icon = "fix_bug",     Prompt = "Fix the bug in the current file" },
            new PromptDef { Id = "write_tests", Label = "Write Tests", Icon = "write_tests", Prompt = "Write tests for the changes you just made" },
            new PromptDef { Id = "explore",     Label = "Explore",     Icon = "explore",     Prompt = "Explore this codebase and explain its structure, key files, and how it works" },
            new PromptDef { Id = "explain",     Label = "Explain",     Icon = "explain",     Prompt = "Explain how this code works" },
            new PromptDef { Id = "refactor",    Label = "Refactor",    Icon = "refactor",    Prompt = "Refactor this for clarity" },
            new PromptDef { Id = "review",      Label = "Review",       Icon = "review",      Prompt = "Review this code for bugs and issues" },
            new PromptDef { Id = "optimize",    Label = "Optimize",    Icon = "optimize",    Prompt = "Optimize this for performance" },
            new PromptDef { Id = "security",    Label = "Security",     Icon = "security",    Prompt = "Check this code for security vulnerabilities" },
            new PromptDef { Id = "document",    Label = "Document",     Icon = "document",    Prompt = "Add documentation to this code" },
            new PromptDef { Id = "deploy",      Label = "Deploy",       Icon = "deploy",      Prompt = "Deploy this project" },
        };

        private readonly Dictionary<String, PromptDef> _prompts = new Dictionary<String, PromptDef>();

        public PromptCommand()
            : base()
        {
            foreach (var p in LoadPrompts())
            {
                if (String.IsNullOrEmpty(p.Id))
                {
                    continue;
                }
                _prompts[p.Id] = p;
                this.AddParameter(p.Id, p.Label ?? p.Id, "Prompts");
            }
        }

        // Load from prompts.json; fall back to (and seed) the built-in defaults.
        private static IEnumerable<PromptDef> LoadPrompts()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    var list = JsonSerializer.Deserialize<List<PromptDef>>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (list != null && list.Count > 0)
                    {
                        return list;
                    }
                }
                else
                {
                    WriteStarter();
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "PromptCommand: prompts.json unreadable — using defaults");
            }

            return Defaults;
        }

        // First run: drop the defaults into the config dir so users have something to edit.
        private static void WriteStarter()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile));
                var json = JsonSerializer.Serialize(Defaults, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFile, json);
                PluginLog.Info($"PromptCommand: wrote starter prompts.json to {ConfigFile}");
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, "PromptCommand: could not write starter prompts.json");
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            if (_prompts.TryGetValue(actionParameter, out var p) && !String.IsNullOrEmpty(p.Prompt))
            {
                BridgeManager.Instance.SendPrompt(p.Prompt);
                PluginLog.Info($"PromptCommand: Sent prompt '{p.Id}'");
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
            => _prompts.TryGetValue(actionParameter, out var p) ? (p.Label ?? actionParameter) : actionParameter;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            _prompts.TryGetValue(actionParameter, out var p);
            var icon = String.IsNullOrEmpty(p?.Icon) ? "explain" : p.Icon;
            // Accent is unused by KeyImage; the icon's baked colour is the key colour.
            return KeyImage.Render(imageSize, p?.Label ?? actionParameter, KeyImage.Blue, icon);
        }
    }
}
