namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
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
        private static readonly String CodexConfigFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "claude-console", "desktop-workflows.json");

        private static readonly String ChatGptConfigFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "claude-console", "desktop-chatgpt-workflows.json");

        // The appendix's nine, written in the house prompt style: scoped to something concrete,
        // method named, output shaped. Entries that need a target the key can't know (a PR
        // number, an error) are DRAFTS — they park in the composer for one edit; the rest send.
        private static readonly WorkflowDef[] CodexDefaults =
        {
            new WorkflowDef { Id = "review_pr",   Label = "Review PR",   Icon = "review",      Submit = false, Prompt = "Review pull request #: correctness first, then edge cases, error handling, and security; give file:line and a concrete failure scenario per finding, skip style nits, and finish with merge / needs-work and the one change that matters most." },
            new WorkflowDef { Id = "debug",       Label = "Debug",       Icon = "fix_bug",     Submit = false, Prompt = "Debug this error: . Reproduce it first, state the root cause in one paragraph, make the smallest fix that addresses the cause, and add a regression test that fails without it." },
            new WorkflowDef { Id = "refactor",    Label = "Refactor",    Icon = "refactor", Submit = false,    Prompt = "Refactor this area: . Aim for clarity without changing behavior: clearer names, smaller functions, less nesting, no duplication. Keep the public API stable and run the tests afterward to prove nothing broke." },
            new WorkflowDef { Id = "write_tests", Label = "Write Tests", Icon = "write_tests",                 Prompt = "Write tests for the most recent changes — the uncommitted diff if there is one, otherwise the last commit. Use the project's test framework and conventions, cover the happy path, edge cases, and failure modes, then run the suite and fix any failures." },
            new WorkflowDef { Id = "explain_diff", Label = "Explain Diff", Icon = "diff",                      Prompt = "Explain the current diff — uncommitted changes if any, otherwise the last commit — change by change: what each does, why it was likely needed, and anything risky or surprising a reviewer should look at twice." },
            new WorkflowDef { Id = "fix_ci",      Label = "Fix CI",      Icon = "deploy",                      Prompt = "Find out why CI is failing: read the latest failing run, reproduce the failure locally if possible, fix the cause rather than the symptom, and state clearly whether the failure was the code or the pipeline." },
            new WorkflowDef { Id = "security",    Label = "Security",    Icon = "security",                    Prompt = "Run a security pass over the recent changes: unvalidated input at trust boundaries, injection, path traversal, secrets in code or logs, unsafe temp files and permissions. Rate each finding by exploitability with the concrete attack; skip purely theoretical ones." },
            new WorkflowDef { Id = "update_deps", Label = "Update Deps", Icon = "push",                        Prompt = "Update this project's dependencies conservatively: patch and minor versions first, read changelogs for anything breaking, update lockfiles, run the full test suite, and summarize what moved and what you deliberately held back." },
            new WorkflowDef { Id = "continue",    Label = "Continue",    Icon = "enter",                       Prompt = "Continue where we left off: restate in two sentences what we were doing and what remains, then proceed with the next step." },
        };

        // General conversation workflows. Anything that needs material the key cannot identify
        // is a draft; it focuses the composer for the user to supply the missing target.
        private static readonly WorkflowDef[] ChatGptDefaults =
        {
            new WorkflowDef { Id = "summarize", Label = "Summarize", Icon = "document", Prompt = "Summarize our conversation so far: decisions, important context, unresolved questions, and the next useful action. Keep it concise and use headings only where they help." },
            new WorkflowDef { Id = "explain", Label = "Explain", Icon = "explain", Prompt = "Explain the topic we were discussing in plain language, including the key idea, why it matters, one concrete example, and the most common misunderstanding." },
            new WorkflowDef { Id = "rewrite", Label = "Rewrite", Icon = "document", Submit = false, Prompt = "Rewrite this material: . Preserve the meaning, improve clarity and flow, and match this audience and tone: ." },
            new WorkflowDef { Id = "draft", Label = "Draft", Icon = "writing", Submit = false, Prompt = "Draft a: . Audience: . Goal: . Tone: . Keep it concise and ready to use." },
            new WorkflowDef { Id = "compare", Label = "Compare", Icon = "diff", Submit = false, Prompt = "Compare:  versus . Use the criteria that matter most for this decision, call out meaningful tradeoffs, and finish with a recommendation and its assumptions." },
            new WorkflowDef { Id = "research", Label = "Research", Icon = "review_core", Submit = false, Prompt = "Research: . Prefer primary and current sources, distinguish verified facts from inference, and finish with the practical conclusions and source links." },
            new WorkflowDef { Id = "brainstorm", Label = "Brainstorm", Icon = "brain", Prompt = "Brainstorm useful approaches to the problem we were discussing. Give a varied shortlist, identify the strongest three, and explain the tradeoff that makes each one distinct." },
            new WorkflowDef { Id = "plan", Label = "Plan", Icon = "plan", Prompt = "Turn what we have discussed into an actionable plan with ordered steps, dependencies, risks, verification points, and a clear definition of done." },
            new WorkflowDef { Id = "continue", Label = "Continue", Icon = "enter", Prompt = "Continue where we left off: briefly restate the current objective and what remains, then take the next useful step." },
        };

        // Optional assignments; these do not displace the user's nine favorites.
        internal static readonly WorkflowDef[] ExtraWorkflows =
        {
            new WorkflowDef { Id = "review_changes", Label = "Review Changes", Icon = "review", Submit = true,
                Prompt = "Review the current workspace's uncommitted changes. If the working tree is clean, report that and stop. Check correctness, edge cases, and security; give file:line and a concrete failure scenario for each finding. Do not edit files." },
            new WorkflowDef { Id = "run_tests", Label = "Run Tests", Icon = "write_tests", Submit = true,
                Prompt = "Run this project's existing relevant test suite using its documented commands. Report the command, exit status, failures, and any tests you could not run. Do not claim tests passed unless you executed them; do not write new tests or change code." },
        };

        private readonly Dictionary<String, WorkflowDef> _codexById = new Dictionary<String, WorkflowDef>();
        private readonly IReadOnlyList<WorkflowDef> _codexWorkflows;
        private readonly IReadOnlyList<WorkflowDef> _chatGptWorkflows;
        private readonly FailureFace _feedback;
        private String _feedbackParameter;

        public DesktopWorkflowCommand()
            : base()
        {
            this.SetWidget(true);
            _feedback = new FailureFace(() => this.ActionImageChanged(), holdMs: 2500);
            if (!DesktopServices.Declared || !DesktopServices.App.Capabilities.ComposerWrite)
            {
                _codexWorkflows = Array.Empty<WorkflowDef>();
                _chatGptWorkflows = Array.Empty<WorkflowDef>();
                return;   // no composer, no workflow keys — never a key that types into nothing
            }

            _codexWorkflows = LoadWorkflows(CodexConfigFile).Where(IsUsable).Take(9).ToArray();
            _chatGptWorkflows = LoadChatGptWorkflows(ChatGptConfigFile).Where(IsUsable).Take(9).ToArray();

            // Legacy named Codex actions stay registered so existing customized profiles keep
            // working. The adaptive default profile binds the stable slot_* parameters below.
            foreach (var w in _codexWorkflows.Concat(ExtraWorkflows).DistinctBy(w => w.Id))
            {
                _codexById[w.Id] = w;
                this.AddParameter(w.Id, w.Label ?? w.Id, "Workflows")
                    .SetDescription(w.Submits
                        ? "Sends this task brief to the app: " + w.Prompt
                        : "Drafts this brief in the composer for you to scope, then send: " + w.Prompt);
            }

            for (var slot = 1; slot <= 9; slot++)
            {
                this.AddParameter($"slot_{slot}", $"Adaptive Workflow {slot}", "Adaptive Workflows")
                    .SetDescription("Uses the workflow at this position for the focused ChatGPT or Codex mode");
            }

            DesktopServices.Monitor.OnChanged += _ => this.ActionImageChanged();
        }

        // Load from desktop-workflows.json; fall back to (and seed) the defaults — the
        // prompts.json mechanism, path-injected for tests the same way.
        internal static IEnumerable<WorkflowDef> LoadWorkflows(String configFile)
            => LoadWorkflows(configFile, CodexDefaults);

        internal static IEnumerable<WorkflowDef> LoadChatGptWorkflows(String configFile)
            => LoadWorkflows(configFile, ChatGptDefaults);

        private static IEnumerable<WorkflowDef> LoadWorkflows(String configFile, WorkflowDef[] defaults)
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
                    WriteStarter(configFile, defaults);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "DesktopWorkflowCommand: desktop-workflows.json unreadable — using defaults");
            }

            return defaults;
        }

        private static void WriteStarter(String configFile, WorkflowDef[] defaults)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configFile));
                File.WriteAllText(configFile,
                    JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true }));
                PluginLog.Info($"DesktopWorkflowCommand: wrote starter desktop-workflows.json to {configFile}");
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, "DesktopWorkflowCommand: could not write starter desktop-workflows.json");
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            if (!DesktopServices.Declared) { return; }
            _feedbackParameter = actionParameter;
            _feedback.Clear();
            var mode = IsSlot(actionParameter) || actionParameter is "review_changes" or "run_tests"
                ? DesktopServices.Automation.Status().Mode
                : "Codex"; // legacy named actions have always been Codex workflows
            if (actionParameter is "review_changes" or "run_tests" && mode != "Codex")
            {
                _feedback.Show("Use Codex");
                return;
            }
            var w = this.Resolve(actionParameter, mode);
            if (w == null || String.IsNullOrEmpty(w.Prompt))
            {
                PluginLog.Info($"DesktopWorkflowCommand({actionParameter}): mode '{mode}' has no workflow — ignored");
                return;
            }

            if (!DesktopServices.Automation.WriteComposer(w.Prompt, send: w.Submits, out var error))
            {
                PluginLog.Warning($"DesktopWorkflowCommand({w.Id}): {error}");
                _feedback.Show(error == "draft-exists" ? "Draft Exists" : w.Submits ? "Not Sent" : "Not Typed");
                return;
            }

            // A drafted brief needs the user AT the composer to scope it — go there.
            if (!w.Submits)
            {
                DesktopServices.Automation.FocusApp();
            }

            PluginLog.Info($"DesktopWorkflowCommand: {(w.Submits ? "sent" : "drafted")} '{w.Id}'");
        }

        // Widget labels and explicit submission state are rendered together, without an SDK label strip.
        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            return "\u200B"; // the full tile owns its label and submission strip
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var mode = DesktopServices.Declared ? DesktopServices.Monitor.Current.Mode : "";
            var workflow = this.Resolve(actionParameter, mode);
            var failed = _feedback.IsActive && _feedbackParameter == actionParameter;
            return KeyImage.RenderIntentTile(imageSize,
                failed ? _feedback.Text : workflow?.Label ?? "Mode?",
                WorkflowIcon(workflow, mode), failed ? "CHECK APP" : workflow == null ? "UNAVAILABLE" :
                workflow.Submits ? "SEND" : "DRAFT");
        }

        internal static String WorkflowIcon(WorkflowDef workflow, String mode) =>
            mode == "ChatGPT" && workflow?.Id == "draft" && workflow.Icon == "voice_draft"
                ? "writing" : workflow?.Icon ?? "status";

        private WorkflowDef Resolve(String actionParameter, String mode)
        {
            if (TryReadSlot(actionParameter, out var slot))
            {
                return WorkflowAt(mode, slot, _chatGptWorkflows, _codexWorkflows);
            }

            return _codexById.TryGetValue(actionParameter ?? "", out var legacy) ? legacy : null;
        }

        internal static WorkflowDef WorkflowAt(
            String mode, Int32 slot,
            IReadOnlyList<WorkflowDef> chatGpt, IReadOnlyList<WorkflowDef> codex)
        {
            var set = String.Equals(mode, "ChatGPT", StringComparison.OrdinalIgnoreCase)
                ? chatGpt
                : String.Equals(mode, "Codex", StringComparison.OrdinalIgnoreCase)
                    ? codex
                    : null;
            return set != null && slot >= 1 && slot <= set.Count ? set[slot - 1] : null;
        }

        private static Boolean IsUsable(WorkflowDef w) =>
            w != null && !String.IsNullOrWhiteSpace(w.Id) && !String.IsNullOrWhiteSpace(w.Prompt);

        private static Boolean IsSlot(String actionParameter) =>
            TryReadSlot(actionParameter, out _);

        private static Boolean TryReadSlot(String actionParameter, out Int32 slot)
        {
            slot = 0;
            return actionParameter?.StartsWith("slot_", StringComparison.Ordinal) == true
                && Int32.TryParse(actionParameter.Substring(5), out slot)
                && slot >= 1 && slot <= 9;
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
