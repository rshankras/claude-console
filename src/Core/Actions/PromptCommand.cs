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
    ///
    /// An entry with "submit": false is a DRAFT key: it types its prompt without pressing Return,
    /// so the user can edit or extend it before sending. Absent means true — existing files keep
    /// today's type-and-send behaviour.
    /// </summary>
    public class PromptCommand : PluginDynamicCommand
    {
        private static readonly String ConfigFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "claude-console", "prompts.json");

        // Written to be worth a dedicated key: each prompt scopes itself to something concrete
        // (the uncommitted diff, the code under discussion — or it asks), names a method, and says
        // what the output should look like. The originals ("Explain how this code works") added
        // nothing over typing the one word yourself, which users noticed. Single-line on purpose:
        // InjectText flattens newlines.
        private static readonly PromptDef[] Defaults =
        {
            new PromptDef { Id = "fix_bug",     Label = "Fix Bug",     Icon = "fix_bug",     Prompt = "Fix the bug we've been discussing — or if none is in context, ask me for the symptom first. Reproduce it, explain the root cause in one short paragraph, make the smallest fix that addresses the cause rather than the symptom, and add a regression test that fails without the fix." },
            new PromptDef { Id = "write_tests", Label = "Write Tests", Icon = "write_tests", Prompt = "Write tests for the most recent changes — the uncommitted diff if there is one, otherwise the last commit. Use the project's existing test framework and conventions, cover the happy path, edge cases, and failure modes, then run the suite and fix any failures." },
            new PromptDef { Id = "explore",     Label = "Explore",     Icon = "explore",     Prompt = "Give me a guided tour of this codebase: what it does, the architecture and key modules with file paths, how data flows through one typical operation, and anything that would surprise a new contributor. Finish with the five files most worth reading first, and why." },
            new PromptDef { Id = "explain",     Label = "Explain",     Icon = "explain",     Prompt = "Explain how the code we're looking at works — or ask me which file or function, if nothing is in context. Start with a one-paragraph summary, then walk the flow step by step, calling out non-obvious decisions, invariants, and gotchas a reader would miss." },
            new PromptDef { Id = "refactor",    Label = "Refactor",    Icon = "refactor",    Prompt = "Refactor the code under discussion for clarity without changing behavior: clearer names, smaller functions, less nesting, no duplication. Keep the public API stable, keep comments that explain why, and run the tests afterward to prove nothing broke." },
            new PromptDef { Id = "review",      Label = "Review",       Icon = "review",      Prompt = "Review the current changes — the uncommitted diff if there is one, otherwise the last commit — like a careful senior engineer: correctness, edge cases, error handling, concurrency, security. Give file:line, severity, and a concrete failure scenario for each finding; skip style nits. If it's clean, say so." },
            new PromptDef { Id = "optimize",    Label = "Optimize",    Icon = "optimize",    Prompt = "Find what is actually slow before optimizing: measure or trace the hot path in the code under discussion and state your evidence. Then optimize only the top bottleneck, keep behavior identical, and say what improvement you expect and how to verify it." },
            new PromptDef { Id = "security",    Label = "Security",     Icon = "security",    Prompt = "Audit the current changes — or the module in context — for security issues: unvalidated input at trust boundaries, injection, path traversal, secrets in code or logs, unsafe temp files and permissions. Rate each finding by exploitability with the concrete attack; skip purely theoretical ones." },
            new PromptDef { Id = "document",    Label = "Document",     Icon = "document",    Prompt = "Document the code under discussion: doc comments on public APIs that explain purpose, constraints, and the why — not restating signatures — plus a usage example where one helps. Match the project's existing documentation style, and update the README if user-facing behavior changed." },
            new PromptDef { Id = "deploy",      Label = "Deploy",       Icon = "deploy",      Prompt = "Get this project ready to ship: run the full test suite, check for uncommitted or unpushed work, bump the version and changelog per the project's convention, and prepare the build or package. Stop and show me a summary for approval before anything goes public — no push, publish, or upload without my OK." },
        };

        // The pre-1.7 seeds, verbatim — used ONLY to recognize a prompts.json the user never
        // edited, so it can be upgraded to the defaults above. Any difference at all (one changed
        // character, a reorder, an added key) means the file is the user's and is left alone.
        private static readonly PromptDef[] LegacyDefaults =
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
            foreach (var p in LoadPrompts(ConfigFile))
            {
                if (String.IsNullOrEmpty(p.Id))
                {
                    continue;
                }
                _prompts[p.Id] = p;
                var param = this.AddParameter(p.Id, p.Label ?? p.Id, "Prompts");
                if (!String.IsNullOrWhiteSpace(p.Prompt))
                {
                    param.SetDescription(p.Submits
                        ? "Types this prompt into Claude Code: " + p.Prompt
                        : "Types this prompt for you to edit before sending (press Return to send): " + p.Prompt);
                }
            }
        }

        // Load from prompts.json; fall back to (and seed) the built-in defaults. A file that is
        // byte-for-byte semantically the pre-1.7 seed — i.e. the user never touched it — is
        // upgraded to the current defaults, because a seeded file otherwise pins every existing
        // install to whatever the defaults said on the day it was first run.
        // Internal + path-injected so the tests can drive it against a temp file.
        internal static IEnumerable<PromptDef> LoadPrompts(String configFile)
        {
            try
            {
                if (File.Exists(configFile))
                {
                    var json = File.ReadAllText(configFile);
                    var list = JsonSerializer.Deserialize<List<PromptDef>>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (list != null && list.Count > 0)
                    {
                        if (IsUneditedLegacySeed(list))
                        {
                            PluginLog.Info("PromptCommand: prompts.json is the unedited pre-1.7 seed — upgrading it to the current defaults");
                            WriteStarter(configFile);
                            return Defaults;
                        }
                        return list;
                    }
                }
                else
                {
                    WriteStarter(configFile);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "PromptCommand: prompts.json unreadable — using defaults");
            }

            return Defaults;
        }

        // True only when the file matches the old seed exactly — same count, same order, same id /
        // label / icon / prompt, and no submit flag anywhere. ANY user edit fails the match.
        internal static Boolean IsUneditedLegacySeed(List<PromptDef> list)
        {
            if (list.Count != LegacyDefaults.Length)
            {
                return false;
            }
            for (var i = 0; i < list.Count; i++)
            {
                var a = list[i];
                var b = LegacyDefaults[i];
                if (!String.Equals(a.Id, b.Id, StringComparison.Ordinal) ||
                    !String.Equals(a.Label, b.Label, StringComparison.Ordinal) ||
                    !String.Equals(a.Icon, b.Icon, StringComparison.Ordinal) ||
                    !String.Equals(a.Prompt, b.Prompt, StringComparison.Ordinal) ||
                    a.Submit != null)
                {
                    return false;
                }
            }
            return true;
        }

        // First run (or legacy upgrade): drop the defaults into the config dir as an editable file.
        private static void WriteStarter(String configFile)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configFile));
                var json = JsonSerializer.Serialize(Defaults, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configFile, json);
                PluginLog.Info($"PromptCommand: wrote starter prompts.json to {configFile}");
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
                // "submit": false in prompts.json turns a key into a DRAFT: the prompt lands in the
                // input box to be edited or extended, and the user sends it with Return.
                BridgeManager.Instance.InjectText(p.Prompt, pressEnter: p.Submits);
                PluginLog.Info($"PromptCommand: {(p.Submits ? "Sent" : "Drafted")} prompt '{p.Id}'");
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
