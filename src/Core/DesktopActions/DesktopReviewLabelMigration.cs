namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using WorkflowDef = DesktopWorkflowCommand.WorkflowDef;

    // Rename only the untouched 0.17 review recipe, wherever the user placed it.
    internal static class DesktopReviewLabelMigration
    {
        internal static IReadOnlyList<WorkflowDef> Upgrade(String path, IReadOnlyList<WorkflowDef> current)
        {
            const String prompt = "Review the current workspace's uncommitted changes. If the working tree is clean, report that and stop. Check correctness, edge cases, and security; give file:line and a concrete failure scenario for each finding. Do not edit files.";
            Boolean Stock(WorkflowDef w) => w?.Id == "review_changes" && w.Label == "Review Changes"
                && w.Icon == "review" && w.Prompt == prompt && w.Submit == true
                && w.Scope == "CHANGES" && w.Input == null && w.SourcePrompt == null;
            var index = current.ToList().FindIndex(Stock);
            if (index < 0 || current.Count(w => w?.Id == "review_changes") != 1) return current;
            var temp = path + ".review-" + Guid.NewGuid().ToString("N");
            try
            {
                var original = File.ReadAllText(path);
                var doc = JsonNode.Parse(original).AsArray();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                if (doc.Count != current.Count || !Stock(doc[index].Deserialize<WorkflowDef>(options))) return current;
                var item = doc[index].AsObject();
                var fields = item.Select(p => p.Key).Where(k => String.Equals(k, "label", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (fields.Length != 1) return current;
                item[fields[0]] = "Review Code";
                var result = doc.Deserialize<List<WorkflowDef>>(options);
                if (!File.Exists(path + ".before-0.17.16")) File.WriteAllText(path + ".before-0.17.16", original);
                File.WriteAllText(temp, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                if (File.ReadAllText(path) != original) return current;
                File.Move(temp, path, true);
                return result;
            }
            catch { PluginLog.Warning("Desktop review label upgrade kept existing settings"); return current; }
            finally { try { File.Delete(temp); } catch { } }
        }
    }
}
