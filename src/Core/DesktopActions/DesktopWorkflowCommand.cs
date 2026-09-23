namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>Editable task briefs: SEND uses the current context; SPEAK records the missing
    /// scope, inserts the complete brief, and waits for an explicit Send Draft press.</summary>
    public class DesktopWorkflowCommand : DesktopCommandBase
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
        internal static readonly WorkflowDef[] CodexDefaults =
        {
            new WorkflowDef { Id = "review_changes", Label = "Review Code", Icon = "review", Submit = true, Prompt = "Review the current workspace's uncommitted changes. If the working tree is clean, report that and stop. Check correctness, edge cases, and security; give file:line and a concrete failure scenario for each finding. Do not edit files.", Scope = "CHANGES" },
            new WorkflowDef { Id = "debug", Label = "Debug", Icon = "fix_bug", Submit = false, Input = "voice", Prompt = "Your request:\n{brief}\n\nDebug task:\nInvestigate using the current workspace and supplied context. If you find a bug, reproduce it, explain the cause, make the smallest fix, and verify it with an appropriate test. If no bug is found, say so. Ask if the target is unclear.", Scope = "BRIEF" },
            new WorkflowDef { Id = "refactor", Label = "Refactor", Icon = "refactor", Submit = false, Input = "voice", Prompt = "Your request:\n{brief}\n\nRefactor task:\nRefactor the requested area for clarity without changing behavior or public APIs. Use the workspace and supplied context, ask if the target is unclear, and run relevant tests.", Scope = "BRIEF" },
            new WorkflowDef { Id = "run_tests", Label = "Run Tests", Icon = "write_tests", Submit = true, Prompt = "Run this project's existing relevant test suite using its documented commands. Report the command, exit status, failures, and any tests you could not run. Do not claim tests passed unless you executed them; do not write new tests or change code.", Scope = "TESTS" },
            new WorkflowDef { Id = "explain_diff", Label = "Explain Diff", Icon = "diff", Prompt = "Explain the current diff — uncommitted changes if any, otherwise the last commit; state which scope you used — change by change: what each does, why it was likely needed, and anything risky or surprising a reviewer should look at twice.", Scope = "DIFF" },
            new WorkflowDef { Id = "fix_ci", Label = "Fix CI", Icon = "deploy", Prompt = "Your request:\n{brief}\n\nFix CI task:\nUse the supplied context to identify the repository, branch, and failing run; ask if unclear. Investigate, reproduce locally where possible, fix the cause, and report what you verified.", Scope = "RUN", Input = "voice", Submit = false },
            new WorkflowDef { Id = "security", Label = "Security", Icon = "security", Prompt = "Run a security pass over the recent changes: unvalidated input at trust boundaries, injection, path traversal, secrets in code or logs, unsafe temp files and permissions. Rate each finding by exploitability with the concrete attack; skip purely theoretical ones.", Scope = "CHANGES" },
            new WorkflowDef { Id = "update_deps", Label = "Update Deps", Icon = "push", Prompt = "Update this project's dependencies conservatively: patch and minor versions first, read changelogs for anything breaking, update lockfiles, run the full test suite, and summarize what moved and what you deliberately held back.", Scope = "DEPS", Submit = false },
            new WorkflowDef { Id = "continue", Label = "Continue", Icon = "enter", Prompt = "Continue where we left off: restate in two sentences what we were doing and what remains, then proceed with the next step. If the agreed next step is unclear, ask. Do not treat this as approval of a pending permission request.", Scope = "TASK" },
        };

        // General conversation workflows. Anything that needs material the key cannot identify
        // is a draft; it focuses the composer for the user to supply the missing target.
        internal static readonly WorkflowDef[] ChatGptDefaults =
        {
            new WorkflowDef { Id = "summarize", Label = "Summarize", Icon = "document", Prompt = "Summarize our conversation so far: decisions, important context, unresolved questions, and the next useful action. Keep it concise and use headings only where they help.", Scope = "CHAT", SourcePrompt = "Summarize the text, documents, or images supplied with this message. Preserve the key facts, decisions, and action items. If material is missing or unreadable, say so. Do not substitute a summary of earlier conversation." },
            new WorkflowDef { Id = "explain", Label = "Explain", Icon = "explain", Prompt = "Explain the topic we were discussing in plain language, including the key idea, why it matters, one concrete example, and the most common misunderstanding.", Scope = "CHAT", SourcePrompt = "Explain the text, documents, or images supplied with this message in plain language. Identify the key idea, give a concrete example, and explain any visible error or confusing part. Say when material is missing or unreadable." },
            new WorkflowDef { Id = "rewrite", Label = "Rewrite", Icon = "document", Submit = false, Input = "voice", Prompt = "Your request:\n{brief}\n\nRewrite task:\nRewrite the supplied material while preserving its meaning and following the requested audience and tone. Ask for missing material.", Scope = "BRIEF" },
            new WorkflowDef { Id = "draft", Label = "Draft Reply", Icon = "writing", Submit = false, Input = "voice", Prompt = "Your request:\n{brief}\n\nDraft Reply task:\nDraft a concise reply using the supplied context and requested recipient, goal, and tone. Do not invent facts or commitments. Ask for essential missing details.", Scope = "BRIEF" },
            new WorkflowDef { Id = "compare", Label = "Compare", Icon = "diff", Submit = false, Input = "voice", Prompt = "Your request:\n{brief}\n\nCompare task:\nCompare the requested options using the supplied context. Explain the main tradeoffs and recommend an option with clear assumptions. Ask if the options are unclear.", Scope = "BRIEF" },
            new WorkflowDef { Id = "research", Label = "Research", Icon = "review_core", Submit = false, Input = "voice", Prompt = "Your request:\n{brief}\n\nResearch task:\nResearch the requested topic using relevant supplied context. Prefer current primary sources, separate facts from inference, and provide practical conclusions with source links.", Scope = "BRIEF" },
            new WorkflowDef { Id = "brainstorm", Label = "Brainstorm", Icon = "brain", Prompt = "Brainstorm useful approaches to the problem we were discussing. Give a varied shortlist, identify the strongest three, and explain the tradeoff that makes each one distinct.", Scope = "CHAT", SourcePrompt = "Use the problem and context supplied with this message to brainstorm useful approaches. Give a varied shortlist, identify the strongest three, and explain their tradeoffs. Ask if the intended problem is unclear." },
            new WorkflowDef { Id = "plan", Label = "Plan", Icon = "plan", Prompt = "Turn what we have discussed into an actionable plan with ordered steps, dependencies, risks, verification points, and a clear definition of done.", Scope = "CHAT", SourcePrompt = "Turn the material and goal supplied with this message into an actionable plan with ordered steps, dependencies, risks, verification points, and a definition of done. Ask if the goal is unclear." },
            new WorkflowDef { Id = "continue", Label = "Continue", Icon = "enter", Prompt = "Continue where we left off: briefly restate the current objective and what remains, then take the next useful step. If the agreed next step is unclear, ask. Do not treat this as approval of a pending permission request.", Scope = "CHAT", SourcePrompt = "Use the material supplied with this message to continue the agreed task. Briefly state the next step; ask if the task or next step is unclear. Do not treat this request as permission for an unrelated action." },
        };

        // Optional assignments; these do not displace the user's nine favorites.
        internal static readonly WorkflowDef[] ExtraWorkflows =
        {
            new WorkflowDef { Id = "review_pr", Label = "Review PR", Icon = "review", Submit = false, Input = "voice", Prompt = "Your request:\n{brief}\n\nReview PR task:\nReview the identified pull request; ask if its identifier is unclear. Check correctness, edge cases, and security. Give file:line and a concrete failure scenario for each finding, then recommend merge or changes. Do not edit files.", Scope = "BRIEF" },
            new WorkflowDef { Id = "write_tests", Label = "Write Tests", Icon = "write_tests", Prompt = "Write tests for the most recent changes — the uncommitted diff if there is one, otherwise the last commit. Use the project's test framework and conventions, cover the happy path, edge cases, and failure modes, then run the suite and fix any failures.", Scope = "DIFF", Submit = false },
        };

        private readonly Dictionary<String, WorkflowDef> _codexById = new Dictionary<String, WorkflowDef>();
        private readonly IReadOnlyList<WorkflowDef> _codexWorkflows;
        private readonly IReadOnlyList<WorkflowDef> _chatGptWorkflows;
        private readonly FailureFace _feedback;
        private String _feedbackParameter;
        private readonly Dictionary<String, DesktopVoiceDraftCommand.ButtonHandler> _buttons = new();

        public DesktopWorkflowCommand()
            : base()
        {
            this.SetWidget(true);
            _feedback = new FailureFace(() => this.ActionImageChanged(), holdMs: 2500);
            DesktopServices.Lifetime.OnStop(_feedback.Dispose);
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
                    .SetDescription(w.RequiresSpeech ? "Tap to speak the brief, tap to finish, review, then tap to send." : w.Submits
                        ? "Sends this task brief to the app: " + w.Prompt
                        : "Drafts this brief in the composer for you to scope, then send: " + w.Prompt);
            }

            this.AddParameter("draft_reply", "Draft Reply", "Context").SetDescription("Use captured source material and a spoken instruction to draft a reply in ChatGPT mode.");

            for (var slot = 1; slot <= 9; slot++)
            {
                this.AddParameter($"slot_{slot}", $"Adaptive Workflow {slot}", "Adaptive Workflows")
                    .SetDescription("Uses the workflow at this position for the focused ChatGPT or Codex mode");
                this.AddParameter($"task_{slot}", $"Codex Task {slot}", "Tasks")
                    .SetDescription("Uses this configured Codex task; refuses if the app switched to ChatGPT");
            }

            DesktopServices.OnMonitorChanged(_ => this.ActionImageChanged());
            DesktopServices.OnWorkflowChanged(() => this.ActionImageChanged());
            DesktopServices.OnVoiceChanged(() => this.ActionImageChanged());
            DesktopServices.OnDraftDiscarded(() => { _feedback.Clear(); this.ActionImageChanged(); });
        }

        // Load from desktop-workflows.json; fall back to (and seed) the defaults — the
        // prompts.json mechanism, path-injected for tests the same way.
        internal static IEnumerable<WorkflowDef> LoadWorkflows(String configFile)
            => LoadWorkflows(configFile, CodexDefaults);

        internal static IReadOnlyList<WorkflowDef> LoadCodexFavorites() =>
            LoadWorkflows(CodexConfigFile).Where(IsUsable).Take(9).ToArray();

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
                        // Capture the original stock voice settings before older upgrades
                        // normalize the file; the spoken-format backup must preserve that input.
                        var labeled = ReferenceEquals(defaults, CodexDefaults)
                            ? DesktopReviewLabelMigration.Upgrade(configFile, list) : list;
                        var spoken = DesktopSpokenWorkflowMigration.Upgrade(configFile, labeled, defaults);
                        var flow = DesktopWorkflowMigration.Upgrade(configFile, spoken,
                            ReferenceEquals(defaults, ChatGptDefaults) ? DesktopWorkflowUxMigration.ChatGptDefaults : DesktopWorkflowUxMigration.CodexDefaults);
                        var upgraded = DesktopWorkflowUxMigration.Upgrade(configFile, flow, defaults);
                        return ReferenceEquals(defaults, CodexDefaults)
                            ? DesktopReviewLabelMigration.Upgrade(configFile, upgraded) : upgraded;
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

        protected override Boolean ProcessButtonEvent2(String parameter, DeviceButtonEvent2 buttonEvent)
        {
            if (!_buttons.TryGetValue(parameter, out var handler)) _buttons[parameter] = handler = new();
            return handler.Handle(buttonEvent.EventType, () =>
            {
                if (!DesktopServices.Declared) return null;
                var mode = DesktopServices.Monitor.Current.Mode;
                var face = DesktopServices.WorkflowVoice.Face(this.Resolve(parameter, mode)?.Id, mode, BridgeManager.Instance.Voice);
                return face?.Label == "Insert Draft" ? DesktopServices.DraftRecovery.DiscardableId(BridgeManager.Instance.Voice.Phase) : null;
            }, () => this.RunCommand(parameter), id => DesktopServices.Run(() => DesktopServices.DraftRecovery.Discard(id, BridgeManager.Instance.Voice.Phase)));
        }

        protected override void RunCommand(String actionParameter)
        {
            DesktopServices.Run(() => this.RunDesktopCommand(actionParameter));
        }

        private void RunDesktopCommand(String actionParameter)
        {
            if (!DesktopServices.Declared) { return; }
            _feedbackParameter = actionParameter;
            _feedback.Clear();
            var feedback = Execute(actionParameter, DesktopServices.Automation, this.Resolve,
                DesktopServices.WorkflowVoice, BridgeManager.Instance.Voice, DesktopServices.VoiceActions,
                (intent, sink) =>
                {
                    BridgeManager.Instance.DraftTranscriptSink = sink;
                    try { BridgeManager.Instance.ToggleVoice(intent); }
                    finally { BridgeManager.Instance.DraftTranscriptSink = null; }
                }, DesktopServices.Context);
            if (feedback != null) { _feedback.Show(feedback); }
        }

        internal static String Execute(String actionParameter, IDesktopAutomation automation,
            Func<String, String, WorkflowDef> resolve, DesktopWorkflowVoice workflowVoice = null,
            VoiceCaptureState capture = null, DesktopVoiceActions voice = null,
            Action<VoiceIntent, Func<String, String>> toggle = null, DesktopContextCapture context = null)
        {
            var mode = automation.Status().Mode;
            if (actionParameter == "draft_reply" && mode != "ChatGPT") return "Use ChatGPT";
            if (actionParameter != "draft_reply" && !IsSlot(actionParameter) && mode != "Codex")
            {
                return "Use Codex";
            }
            var w = resolve(actionParameter, mode);
            if (w == null || String.IsNullOrEmpty(w.Prompt))
            {
                return null;
            }

            if (w.RequiresSpeech || (capture != null && workflowVoice?.Face(w.Id, mode, capture) != null))
                return workflowVoice == null || capture == null || voice == null || toggle == null ? "Use Voice"
                    : workflowVoice.Press(w, mode, capture, voice, toggle, append: automation.SupportsAppend && w.RequiresSpeech);
            if (capture != null && capture.Phase != VoicePhase.Idle) return "Finish Speaking";

            if (automation.SupportsAppend && workflowVoice != null && capture != null && voice != null && toggle != null)
            {
                var target = automation.PrepareAppend(mode, out var preparationError);
                if (target == null) return DesktopContextCapture.Problem(preparationError);
                var material = target.HasContent || context?.Count > 0;
                if (material || !w.Submits)
                    return workflowVoice.Press(material && !String.IsNullOrWhiteSpace(w.SourcePrompt) ? w.WithPrompt(w.SourcePrompt) : w,
                        mode, capture, voice, toggle, append: true, preparedAppend: target) ?? "Draft Ready";
                return workflowVoice.Press(w, mode, capture, voice, toggle,
                    append: true, preparedAppend: target, submitImmediately: true);
            }
            if (context?.Count > 0)
                return workflowVoice == null || capture == null || voice == null || toggle == null ? "Use Voice"
                    : workflowVoice.Press(!String.IsNullOrWhiteSpace(w.SourcePrompt) ? w.WithPrompt(w.SourcePrompt) : w, mode, capture, voice, toggle);

            if (!automation.WriteComposer(w.Prompt, send: w.Submits, out var error))
            {
                PluginLog.Warning($"DesktopWorkflowCommand({w.Id}): {error}");
                return error == "draft-exists" ? "Draft Exists" : w.Submits ? "Not Sent" : "Not Typed";
            }

            // A drafted brief needs the user AT the composer to scope it — go there.
            if (!w.Submits)
            {
                automation.FocusApp();
            }

            return w.Submits ? "Sent" : "Draft Ready";
        }

        // Widget labels and explicit submission state are rendered together, without an SDK label strip.
        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            return "\u200B"; // the full tile owns its label and submission strip
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var state = DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable;
            var mode = state.Mode;
            var workflow = this.Resolve(actionParameter, mode);
            var failed = _feedback.IsActive && _feedbackParameter == actionParameter;
            var face = DesktopServices.Declared ? DesktopServices.WorkflowVoice.Face(workflow?.Id, mode, BridgeManager.Instance.Voice) : null;
            var shown = failed && _feedback.Text != "Draft Ready" && _feedback.Text != "Insert Draft"
                ? FaceFor(workflow, mode, _feedback.Text) : face ?? FaceFor(workflow, mode, failed ? _feedback.Text : null);
            if (face == null && !failed && workflow != null && !workflow.RequiresSpeech && DesktopServices.Declared
                && (state.CanSend || DesktopServices.Context.Count > 0)) shown.Footer = "INPUT · DRAFT";
            return KeyImage.RenderIntentTile(imageSize, shown.Label, shown.Icon, shown.Footer);
        }

        internal static (String Label, String Icon, String Footer) FaceFor(WorkflowDef workflow, String mode, String failure = null) =>
            (failure ?? workflow?.Label ?? "Mode?", WorkflowIcon(workflow, mode), failure == "Sent" ? "REQUEST SENT" :
                failure == "Draft Ready" ? "SEND DRAFT" : failure != null ? "CHECK APP" :
                workflow == null ? "UNAVAILABLE" : (String.IsNullOrWhiteSpace(workflow.Scope) ? "" : workflow.Scope + " · ")
                    + (workflow.RequiresSpeech ? "SPEAK" : workflow.Submits ? "SEND" : "DRAFT"));

        internal static String WorkflowIcon(WorkflowDef workflow, String mode) =>
            (mode, workflow?.Id, workflow?.Icon) switch
            {
                ("ChatGPT", "draft", "voice_draft") => "writing",
                ("ChatGPT", "rewrite", "document") => "rewrite",
                (_, "continue", "enter") => "continue",
                _ => workflow?.Icon ?? "status",
            };

        private WorkflowDef Resolve(String actionParameter, String mode)
        {
            return Resolve(actionParameter, mode, _chatGptWorkflows, _codexWorkflows, _codexById);
        }

        internal static WorkflowDef Resolve(String actionParameter, String mode,
            IReadOnlyList<WorkflowDef> chatGpt, IReadOnlyList<WorkflowDef> codex,
            IReadOnlyDictionary<String, WorkflowDef> named)
        {
            if (actionParameter == "draft_reply") return chatGpt.FirstOrDefault(w => w.Id == "draft");
            if (TryReadTaskSlot(actionParameter, out var task)) return mode == "Codex" ? WorkflowAt(mode, task, chatGpt, codex) : null;
            if (TryReadSlot(actionParameter, out var slot))
            {
                return WorkflowAt(mode, slot, chatGpt, codex);
            }

            return named.TryGetValue(actionParameter ?? "", out var legacy) ? legacy : null;
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

        internal static Boolean IsUsable(WorkflowDef w) =>
            w != null && !String.IsNullOrWhiteSpace(w.Id) && !String.IsNullOrWhiteSpace(w.Prompt)
                && (!w.RequiresSpeech || w.Prompt.Contains("{brief}", StringComparison.Ordinal));

        private static Boolean IsSlot(String actionParameter) =>
            TryReadSlot(actionParameter, out _);

        private static Boolean TryReadTaskSlot(String parameter, out Int32 slot)
        {
            slot = 0;
            return parameter?.StartsWith("task_", StringComparison.Ordinal) == true
                && Int32.TryParse(parameter.Substring(5), out slot) && slot >= 1 && slot <= 9;
        }

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
            public String SourcePrompt { get; set; }
            public String Scope { get; set; }
            internal WorkflowDef WithPrompt(String prompt) => new() { Id = Id, Label = Label, Icon = Icon, Prompt = prompt,
                SourcePrompt = SourcePrompt, Scope = Scope, Submit = Submit, Input = Input };

            /// <summary>Null/absent means send; false means draft-and-focus.</summary>
            public Boolean? Submit { get; set; }

            public String Input { get; set; }

            public Boolean RequiresSpeech => String.Equals(Input, "voice", StringComparison.OrdinalIgnoreCase);
            public Boolean Submits => !RequiresSpeech && (this.Submit ?? true);
        }
    }
}
