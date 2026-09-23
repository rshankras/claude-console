namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    // Frozen shipped defaults, used only to recognize unchanged settings during the Flow upgrade.
    internal static class DesktopWorkflowMigration
    {
        internal static IReadOnlyList<DesktopWorkflowCommand.WorkflowDef> Upgrade(String path,
            IReadOnlyList<DesktopWorkflowCommand.WorkflowDef> current, DesktopWorkflowCommand.WorkflowDef[] next)
        {
            var previous = next.FirstOrDefault()?.Id == "summarize" ? ChatGptDefaults : CodexDefaults;
            if (current.Count != previous.Length) return current;
            var updated = current.ToArray();
            for (var i = 0; i < updated.Length; i++)
            {
                var value = current[i]; var stock = previous[i];
                if (value == null || value.Input != null || value.Id != stock.Id || value.Label != stock.Label) continue;
                var iconMatches = value.Icon == stock.Icon || value.Id == "draft" && value.Icon == "voice_draft";
                var promptMatches = value.Prompt == stock.Prompt && value.Submits == stock.Submits;
                if (value.Id == "refactor" && value.Prompt == LEGACY_REFACTOR && value.Submits) promptMatches = true;
                if (iconMatches && promptMatches && JsonSerializer.Serialize(value) != JsonSerializer.Serialize(next[i])) updated[i] = next[i];
            }
            if (current.Select((w,i) => ReferenceEquals(w,updated[i])).All(x => x)) return current;
            // Preserve the original even across interrupted/repeated loads; never replace a backup.
            var backup = path + ".before-flow";
            var temporary = path + ".flow-" + Guid.NewGuid().ToString("N");
            try
            {
                if (!File.Exists(backup)) File.Copy(path, backup);
                var document = JsonNode.Parse(File.ReadAllText(path)).AsArray();
                for (var i = 0; i < updated.Length; i++)
                    if (!ReferenceEquals(current[i], updated[i]))
                    {
                        var item = document[i].AsObject();
                        foreach (var field in JsonSerializer.SerializeToNode(updated[i], new JsonSerializerOptions { IgnoreReadOnlyProperties = true }).AsObject())
                        {
                            var key = item.Select(p => p.Key).FirstOrDefault(k => String.Equals(k, field.Key, StringComparison.OrdinalIgnoreCase)) ?? field.Key;
                            item[key] = field.Value?.DeepClone();
                        }
                    }
                File.WriteAllText(temporary, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, path, overwrite: true);
                PluginLog.Info("DesktopWorkflowMigration: upgraded unchanged stock workflow slots; backup preserved");
                return updated;
            }
            catch (Exception ex) { PluginLog.Warning(ex, "DesktopWorkflowMigration: kept existing workflow settings"); return current; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }
        private const String LEGACY_REFACTOR = "Refactor the area we discussed most recently for clarity without changing behavior: clearer names, smaller functions, less nesting, no duplication. Keep the public API stable and run the tests afterward to prove nothing broke.";
        internal static readonly DesktopWorkflowCommand.WorkflowDef[] CodexDefaults =
        {
            new DesktopWorkflowCommand.WorkflowDef { Id = "review_pr",   Label = "Review PR",   Icon = "review",      Submit = false, Prompt = "Review pull request #: correctness first, then edge cases, error handling, and security; give file:line and a concrete failure scenario per finding, skip style nits, and finish with merge / needs-work and the one change that matters most." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "debug",       Label = "Debug",       Icon = "fix_bug",     Submit = false, Prompt = "Debug this error: . Reproduce it first, state the root cause in one paragraph, make the smallest fix that addresses the cause, and add a regression test that fails without it." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "refactor",    Label = "Refactor",    Icon = "refactor", Submit = false,    Prompt = "Refactor this area: . Aim for clarity without changing behavior: clearer names, smaller functions, less nesting, no duplication. Keep the public API stable and run the tests afterward to prove nothing broke." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "write_tests", Label = "Write Tests", Icon = "write_tests",                 Prompt = "Write tests for the most recent changes — the uncommitted diff if there is one, otherwise the last commit. Use the project's test framework and conventions, cover the happy path, edge cases, and failure modes, then run the suite and fix any failures." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "explain_diff", Label = "Explain Diff", Icon = "diff",                      Prompt = "Explain the current diff — uncommitted changes if any, otherwise the last commit — change by change: what each does, why it was likely needed, and anything risky or surprising a reviewer should look at twice." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "fix_ci",      Label = "Fix CI",      Icon = "deploy",                      Prompt = "Find out why CI is failing: read the latest failing run, reproduce the failure locally if possible, fix the cause rather than the symptom, and state clearly whether the failure was the code or the pipeline." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "security",    Label = "Security",    Icon = "security",                    Prompt = "Run a security pass over the recent changes: unvalidated input at trust boundaries, injection, path traversal, secrets in code or logs, unsafe temp files and permissions. Rate each finding by exploitability with the concrete attack; skip purely theoretical ones." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "update_deps", Label = "Update Deps", Icon = "push",                        Prompt = "Update this project's dependencies conservatively: patch and minor versions first, read changelogs for anything breaking, update lockfiles, run the full test suite, and summarize what moved and what you deliberately held back." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "continue",    Label = "Continue",    Icon = "enter",                       Prompt = "Continue where we left off: restate in two sentences what we were doing and what remains, then proceed with the next step." },
        };
        internal static readonly DesktopWorkflowCommand.WorkflowDef[] ChatGptDefaults =
        {
            new DesktopWorkflowCommand.WorkflowDef { Id = "summarize", Label = "Summarize", Icon = "document", Prompt = "Summarize our conversation so far: decisions, important context, unresolved questions, and the next useful action. Keep it concise and use headings only where they help." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "explain", Label = "Explain", Icon = "explain", Prompt = "Explain the topic we were discussing in plain language, including the key idea, why it matters, one concrete example, and the most common misunderstanding." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "rewrite", Label = "Rewrite", Icon = "document", Submit = false, Prompt = "Rewrite this material: . Preserve the meaning, improve clarity and flow, and match this audience and tone: ." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "draft", Label = "Draft", Icon = "writing", Submit = false, Prompt = "Draft a: . Audience: . Goal: . Tone: . Keep it concise and ready to use." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "compare", Label = "Compare", Icon = "diff", Submit = false, Prompt = "Compare:  versus . Use the criteria that matter most for this decision, call out meaningful tradeoffs, and finish with a recommendation and its assumptions." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "research", Label = "Research", Icon = "review_core", Submit = false, Prompt = "Research: . Prefer primary and current sources, distinguish verified facts from inference, and finish with the practical conclusions and source links." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "brainstorm", Label = "Brainstorm", Icon = "brain", Prompt = "Brainstorm useful approaches to the problem we were discussing. Give a varied shortlist, identify the strongest three, and explain the tradeoff that makes each one distinct." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "plan", Label = "Plan", Icon = "plan", Prompt = "Turn what we have discussed into an actionable plan with ordered steps, dependencies, risks, verification points, and a clear definition of done." },
            new DesktopWorkflowCommand.WorkflowDef { Id = "continue", Label = "Continue", Icon = "enter", Prompt = "Continue where we left off: briefly restate the current objective and what remains, then take the next useful step." },
        };
    }
}
