namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using WorkflowDef = DesktopWorkflowCommand.WorkflowDef;

    // Frozen 0.16.3 settings. Only unchanged stock slots acquire the 0.17 workflow semantics.
    internal static class DesktopWorkflowUxMigration
    {
        internal static IReadOnlyList<WorkflowDef> Upgrade(String path, IReadOnlyList<WorkflowDef> current, WorkflowDef[] next)
        {
            var previous = ReferenceEquals(next, DesktopWorkflowCommand.ChatGptDefaults) ? ChatGptDefaults : CodexDefaults;
            if (current.Count != previous.Length || current.Where((w, i) => w?.Id != previous[i].Id).Any()) return current;
            Boolean Same(WorkflowDef a, WorkflowDef b) => a.Id == b.Id && a.Label == b.Label && a.Icon == b.Icon
                && a.Prompt == b.Prompt && a.Input == b.Input && a.Submits == b.Submits
                && a.SourcePrompt == b.SourcePrompt && a.Scope == b.Scope;
            var changed = Enumerable.Range(0, current.Count).Where(i => Same(current[i], previous[i]) && !Same(current[i], next[i])).ToArray();
            if (changed.Length == 0) return current;
            var temp = path + ".ux-" + Guid.NewGuid().ToString("N");
            try
            {
                var doc = JsonNode.Parse(File.ReadAllText(path)).AsArray();
                foreach (var index in changed)
                {
                    var item = doc[index].AsObject();
                    var updated = JsonSerializer.SerializeToNode(next[index], new JsonSerializerOptions {
                        IgnoreReadOnlyProperties = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }).AsObject();
                    foreach (var field in updated)
                    {
                        var key = item.Select(p => p.Key).FirstOrDefault(k => String.Equals(k, field.Key, StringComparison.OrdinalIgnoreCase)) ?? field.Key;
                        item[key] = field.Value?.DeepClone();
                    }
                }
                if (!File.Exists(path + ".before-0.17")) File.Copy(path, path + ".before-0.17");
                File.WriteAllText(temp, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, path, overwrite: true);
                return JsonSerializer.Deserialize<List<WorkflowDef>>(doc.ToJsonString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex) { PluginLog.Warning(ex, "Desktop workflow upgrade kept existing settings"); return current; }
            finally { try { File.Delete(temp); } catch { } }
        }
        internal static readonly WorkflowDef[] CodexDefaults =
        {
            new WorkflowDef { Id = "review_changes", Label = "Review Changes", Icon = "review", Submit = true, Prompt = "Review the current workspace's uncommitted changes. If the working tree is clean, report that and stop. Check correctness, edge cases, and security; give file:line and a concrete failure scenario for each finding. Do not edit files." },
            new WorkflowDef { Id = "debug",       Label = "Debug",       Icon = "fix_bug",     Submit = false, Input = "voice", Prompt = "Debug the error described in this brief: {brief}. Reproduce it first, state the root cause in one paragraph, make the smallest fix that addresses the cause, and add a regression test that fails without it." },
            new WorkflowDef { Id = "refactor",    Label = "Refactor",    Icon = "refactor", Submit = false, Input = "voice", Prompt = "Refactor the area identified in this brief: {brief}. If the target is unclear, ask before editing. Aim for clarity without changing behavior: clearer names, smaller functions, less nesting, no duplication. Keep the public API stable and run the tests afterward to prove nothing broke." },
            new WorkflowDef { Id = "run_tests", Label = "Run Tests", Icon = "write_tests", Submit = true, Prompt = "Run this project's existing relevant test suite using its documented commands. Report the command, exit status, failures, and any tests you could not run. Do not claim tests passed unless you executed them; do not write new tests or change code." },
            new WorkflowDef { Id = "explain_diff", Label = "Explain Diff", Icon = "diff",                      Prompt = "Explain the current diff — uncommitted changes if any, otherwise the last commit — change by change: what each does, why it was likely needed, and anything risky or surprising a reviewer should look at twice." },
            new WorkflowDef { Id = "fix_ci",      Label = "Fix CI",      Icon = "deploy",                      Prompt = "Find out why CI is failing: read the latest failing run, reproduce the failure locally if possible, fix the cause rather than the symptom, and state clearly whether the failure was the code or the pipeline." },
            new WorkflowDef { Id = "security",    Label = "Security",    Icon = "security",                    Prompt = "Run a security pass over the recent changes: unvalidated input at trust boundaries, injection, path traversal, secrets in code or logs, unsafe temp files and permissions. Rate each finding by exploitability with the concrete attack; skip purely theoretical ones." },
            new WorkflowDef { Id = "update_deps", Label = "Update Deps", Icon = "push",                        Prompt = "Update this project's dependencies conservatively: patch and minor versions first, read changelogs for anything breaking, update lockfiles, run the full test suite, and summarize what moved and what you deliberately held back." },
            new WorkflowDef { Id = "continue",    Label = "Continue",    Icon = "enter",                       Prompt = "Continue where we left off: restate in two sentences what we were doing and what remains, then proceed with the next step." },
        };
        internal static readonly WorkflowDef[] ChatGptDefaults =
        {
            new WorkflowDef { Id = "summarize", Label = "Summarize", Icon = "document", Prompt = "Summarize our conversation so far: decisions, important context, unresolved questions, and the next useful action. Keep it concise and use headings only where they help." },
            new WorkflowDef { Id = "explain", Label = "Explain", Icon = "explain", Prompt = "Explain the topic we were discussing in plain language, including the key idea, why it matters, one concrete example, and the most common misunderstanding." },
            new WorkflowDef { Id = "rewrite", Label = "Rewrite", Icon = "document", Submit = false, Input = "voice", Prompt = "Rewrite the material identified in this brief, preserving meaning and following its audience and tone. Ask for missing source material rather than inventing it. Brief: {brief}" },
            new WorkflowDef { Id = "draft", Label = "Draft Reply", Icon = "writing", Submit = false, Input = "voice", Prompt = "Draft a concise reply using this brief, matching the stated recipient, goal and tone. Do not invent commitments or facts. Brief: {brief}" },
            new WorkflowDef { Id = "compare", Label = "Compare", Icon = "diff", Submit = false, Input = "voice", Prompt = "Compare the options identified in this brief. Explain meaningful tradeoffs and finish with a recommendation and assumptions. Ask if the options are unclear. Brief: {brief}" },
            new WorkflowDef { Id = "research", Label = "Research", Icon = "review_core", Submit = false, Input = "voice", Prompt = "Research the topic in this brief. Prefer primary and current sources, distinguish facts from inference, and finish with practical conclusions and source links. Brief: {brief}" },
            new WorkflowDef { Id = "brainstorm", Label = "Brainstorm", Icon = "brain", Prompt = "Brainstorm useful approaches to the problem we were discussing. Give a varied shortlist, identify the strongest three, and explain the tradeoff that makes each one distinct." },
            new WorkflowDef { Id = "plan", Label = "Plan", Icon = "plan", Prompt = "Turn what we have discussed into an actionable plan with ordered steps, dependencies, risks, verification points, and a clear definition of done." },
            new WorkflowDef { Id = "continue", Label = "Continue", Icon = "enter", Prompt = "Continue where we left off: briefly restate the current objective and what remains, then take the next useful step." },
        };
    }
}
