namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// The Workflows page (Appendix D · Codex Desktop · Page 2): one press launches a Codex task
    /// from a template — where the Codex Micro ships four blind joystick presets, every preset
    /// here has a face. Same user-editable mechanism as the terminal products' prompts.json,
    /// different file (desktop-workflows.json) because these are TASK BRIEFS for an agent with a
    /// workspace, not remarks typed at a TTY.
    ///
    /// Press → the template lands in the app's composer and sends (the proven write path — no
    /// keystrokes, no focus). An entry with "submit": false drafts instead: the text waits in
    /// the composer for you to scope it before sending, which is the right default for briefs
    /// that name a target ("review PR #…") the template cannot know.
    /// </summary>
    public class DesktopWorkflowCommand : PluginDynamicCommand
    {
        private static readonly String ConfigFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "claude-console", "desktop-workflows.json");

        // The appendix's nine, written in the house prompt style: scoped to something concrete,
        // method named, output shaped. Entries that need a target the key can't know (a PR
        // number, an error) are DRAFTS — they park in the composer for one edit; the rest send.
        private static readonly WorkflowDef[] Defaults =
        {
            new WorkflowDef { Id = "review_pr",   Label = "Review PR",   Icon = "review",      Submit = false, Prompt = "Review pull request #: correctness first, then edge cases, error handling, and security; give file:line and a concrete failure scenario per finding, skip style nits, and finish with merge / needs-work and the one change that matters most." },
            new WorkflowDef { Id = "debug",       Label = "Debug",       Icon = "fix_bug",     Submit = false, Prompt = "Debug this error: . Reproduce it first, state the root cause in one paragraph, make the smallest fix that addresses the cause, and add a regression test that fails without it." },
            new WorkflowDef { Id = "refactor",    Label = "Refactor",    Icon = "refactor",                    Prompt = "Refactor the area we discussed most recently for clarity without changing behavior: clearer names, smaller functions, less nesting, no duplication. Keep the public API stable and run the tests afterward to prove nothing broke." },
            new WorkflowDef { Id = "write_tests", Label = "Write Tests", Icon = "write_tests",                 Prompt = "Write tests for the most recent changes — the uncommitted diff if there is one, otherwise the last commit. Use the project's test framework and conventions, cover the happy path, edge cases, and failure modes, then run the suite and fix any failures." },
            new WorkflowDef { Id = "explain_diff", Label = "Explain Diff", Icon = "diff",                      Prompt = "Explain the current diff — uncommitted changes if any, otherwise the last commit — change by change: what each does, why it was likely needed, and anything risky or surprising a reviewer should look at twice." },
            new WorkflowDef { Id = "fix_ci",      Label = "Fix CI",      Icon = "deploy",                      Prompt = "Find out why CI is failing: read the latest failing run, reproduce the failure locally if possible, fix the cause rather than the symptom, and state clearly whether the failure was the code or the pipeline." },
            new WorkflowDef { Id = "security",    Label = "Security",    Icon = "security",                    Prompt = "Run a security pass over the recent changes: unvalidated input at trust boundaries, injection, path traversal, secrets in code or logs, unsafe temp files and permissions. Rate each finding by exploitability with the concrete attack; skip purely theoretical ones." },
            new WorkflowDef { Id = "update_deps", Label = "Update Deps", Icon = "push",                        Prompt = "Update this project's dependencies conservatively: patch and minor versions first, read changelogs for anything breaking, update lockfiles, run the full test suite, and summarize what moved and what you deliberately held back." },
            new WorkflowDef { Id = "continue",    Label = "Continue",    Icon = "enter",                       Prompt = "Continue where we left off: restate in two sentences what we were doing and what remains, then proceed with the next step." },
        };

        private readonly Dictionary<String, WorkflowDef> _workflows = new Dictionary<String, WorkflowDef>();

        public DesktopWorkflowCommand()
            : base()
        {
            if (!DesktopServices.Declared || !DesktopServices.App.Capabilities.ComposerWrite)
            {
                return;   // no composer, no workflow keys — never a key that types into nothing
            }

            foreach (var w in LoadWorkflows(ConfigFile))
            {
                if (String.IsNullOrEmpty(w.Id))
                {
                    continue;
                }

                _workflows[w.Id] = w;
                this.AddParameter(w.Id, w.Label ?? w.Id, "Workflows")
                    .SetDescription(w.Submits
                        ? "Sends this task brief to the app: " + w.Prompt
                        : "Drafts this brief in the composer for you to scope, then send: " + w.Prompt);
            }
        }

        // Load from desktop-workflows.json; fall back to (and seed) the defaults — the
        // prompts.json mechanism, path-injected for tests the same way.
        internal static IEnumerable<WorkflowDef> LoadWorkflows(String configFile)
        {
            try
            {
                if (File.Exists(configFile))
                {
                    var list = JsonSerializer.Deserialize<List<WorkflowDef>>(
                        File.ReadAllText(configFile),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (list != null && list.Count > 0)
                    {
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
                PluginLog.Warning(ex, "DesktopWorkflowCommand: desktop-workflows.json unreadable — using defaults");
            }

            return Defaults;
        }

        private static void WriteStarter(String configFile)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configFile));
                File.WriteAllText(configFile,
                    JsonSerializer.Serialize(Defaults, new JsonSerializerOptions { WriteIndented = true }));
                PluginLog.Info($"DesktopWorkflowCommand: wrote starter desktop-workflows.json to {configFile}");
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, "DesktopWorkflowCommand: could not write starter desktop-workflows.json");
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            if (!_workflows.TryGetValue(actionParameter, out var w) || String.IsNullOrEmpty(w.Prompt))
            {
                return;
            }

            if (!DesktopServices.Automation.WriteComposer(w.Prompt, send: w.Submits, out var error))
            {
                PluginLog.Warning($"DesktopWorkflowCommand({w.Id}): {error}");
                return;
            }

            // A drafted brief needs the user AT the composer to scope it — go there.
            if (!w.Submits)
            {
                DesktopServices.Automation.FocusApp();
            }

            PluginLog.Info($"DesktopWorkflowCommand: {(w.Submits ? "sent" : "drafted")} '{w.Id}'");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
            => _workflows.TryGetValue(actionParameter, out var w) ? (w.Label ?? actionParameter) : actionParameter;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var icon = _workflows.TryGetValue(actionParameter, out var w) ? w.Icon : null;
            return KeyImage.Render(imageSize, this.GetCommandDisplayName(actionParameter, imageSize), KeyImage.Slate, icon);
        }

        /// <summary>One workflow template. JSON-compatible with the terminal PromptDef shape.</summary>
        internal sealed class WorkflowDef
        {
            public String Id { get; set; }
            public String Label { get; set; }
            public String Icon { get; set; }
            public String Prompt { get; set; }

            /// <summary>Null/absent means send; false means draft-and-focus.</summary>
            public Boolean? Submit { get; set; }

            public Boolean Submits => this.Submit ?? true;
        }
    }
}
