namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using WorkflowDef = DesktopWorkflowCommand.WorkflowDef;

    /// <summary>Update only untouched spoken recipes; preserve slot order and custom metadata.</summary>
    internal static class DesktopSpokenWorkflowMigration
    {
        internal static IReadOnlyList<WorkflowDef> Upgrade(String path, IReadOnlyList<WorkflowDef> current, WorkflowDef[] next)
        {
            var replacements = new Dictionary<Int32, String>();
            for (var i = 0; i < current.Count; i++)
            {
                var value = current[i];
                if (value == null || current.Count(w => w?.Id == value.Id) != 1) continue;
                var stock = Previous.FirstOrDefault(w => w.Id == value.Id);
                var updated = next.Concat(DesktopWorkflowCommand.ExtraWorkflows).FirstOrDefault(w => w.Id == value.Id);
                if (stock != null && updated != null && Same(value, stock) && value.Prompt != updated.Prompt)
                    replacements.Add(i, updated.Prompt);
            }
            if (replacements.Count == 0) return current;
            var temp = path + ".spoken-" + Guid.NewGuid().ToString("N");
            try
            {
                var original = File.ReadAllText(path);
                var doc = JsonNode.Parse(original).AsArray();
                if (doc.Count != current.Count) return current;
                foreach (var pair in replacements)
                {
                    var item = doc[pair.Key].AsObject();
                    var fields = item.Select(p => p.Key).Where(k => String.Equals(k, "prompt", StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (fields.Length != 1) return current;
                    var latest = item.Deserialize<WorkflowDef>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (!Same(latest, current[pair.Key])) return current;
                    item[fields[0]] = pair.Value;
                }
                var result = doc.Deserialize<List<WorkflowDef>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (!File.Exists(path + ".before-0.17.9")) File.WriteAllText(path + ".before-0.17.9", original);
                File.WriteAllText(temp, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, path, overwrite: true);
                return result;
            }
            catch { PluginLog.Warning("DesktopSpokenWorkflowMigration: kept existing settings"); return current; }
            finally { try { File.Delete(temp); } catch { } }
        }

        private static Boolean Same(WorkflowDef a, WorkflowDef b) => a != null && b != null
            && a.Id == b.Id && a.Label == b.Label && a.Icon == b.Icon && a.Prompt == b.Prompt
            && a.Input == b.Input && a.Submit == b.Submit && a.SourcePrompt == b.SourcePrompt && a.Scope == b.Scope;

        // Frozen 0.17.8 spoken defaults, independent of future changes to the current recipes.
        internal static readonly WorkflowDef[] Previous =
        {
            new WorkflowDef { Id = "debug", Label = "Debug", Icon = "fix_bug", Submit = false, Input = "voice", Prompt = "Use the text, documents, or screenshots supplied with this message as context. Debug the error described in this brief: {brief}. Reproduce it first, state the root cause in one paragraph, make the smallest fix that addresses the cause, and add a regression test that fails without it.", Scope = "BRIEF" },
            new WorkflowDef { Id = "refactor", Label = "Refactor", Icon = "refactor", Submit = false, Input = "voice", Prompt = "Use the text, documents, or screenshots supplied with this message as context. Refactor the area identified in this brief: {brief}. If the target is unclear, ask before editing. Aim for clarity without changing behavior: clearer names, smaller functions, less nesting, no duplication. Keep the public API stable and run the tests afterward to prove nothing broke.", Scope = "BRIEF" },
            new WorkflowDef { Id = "fix_ci", Label = "Fix CI", Icon = "deploy", Prompt = "Investigate and fix the failing CI run identified in this brief: {brief}. Verify the repository, branch, and run before acting; ask if any is unclear. Use supplied logs or screenshots. Reproduce locally where possible, fix the cause, and report the changes and actual verification.", Scope = "RUN", Input = "voice", Submit = false },
            new WorkflowDef { Id = "rewrite", Label = "Rewrite", Icon = "document", Submit = false, Input = "voice", Prompt = "Use the text, documents, or screenshots supplied with this message as context. Rewrite the material identified in this brief, preserving meaning and following its audience and tone. Ask for missing source material rather than inventing it. Brief: {brief}", Scope = "BRIEF" },
            new WorkflowDef { Id = "draft", Label = "Draft Reply", Icon = "writing", Submit = false, Input = "voice", Prompt = "Use the text, documents, or screenshots supplied with this message as context. Draft a concise reply using this brief, matching the stated recipient, goal and tone. Do not invent commitments or facts. Brief: {brief}", Scope = "BRIEF" },
            new WorkflowDef { Id = "compare", Label = "Compare", Icon = "diff", Submit = false, Input = "voice", Prompt = "Use the text, documents, or screenshots supplied with this message as context. Compare the options identified in this brief. Explain meaningful tradeoffs and finish with a recommendation and assumptions. Ask if the options are unclear. Brief: {brief}", Scope = "BRIEF" },
            new WorkflowDef { Id = "research", Label = "Research", Icon = "review_core", Submit = false, Input = "voice", Prompt = "Research the topic in this brief. Prefer primary and current sources, distinguish facts from inference, and finish with practical conclusions and source links. Brief: {brief}", Scope = "BRIEF" },
            new WorkflowDef { Id = "review_pr", Label = "Review PR", Icon = "review", Submit = false, Input = "voice", Prompt = "Review the pull request identified in this brief. Ask for its identifier if unclear. Brief: {brief}. Check correctness first, then edge cases, error handling, and security; give file:line and a concrete failure scenario per finding, skip style nits, and finish with merge / needs-work and the one change that matters most.", Scope = "BRIEF" },
        };
    }
}
