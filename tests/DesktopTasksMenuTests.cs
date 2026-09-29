namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System.IO.Compression;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration;
    using Xunit;
    using Workflow = Loupedeck.ClaudeConsolePlugin.DesktopActions.DesktopWorkflowCommand;

    public class DesktopTasksMenuTests
    {
        [Fact]
        public void Default_tasks_fit_one_page_and_every_favorite_remains_reachable()
        {
            var tasks = DesktopSavedPromptsDynamicFolder.Actions("VizhiDesktop", "Codex");
            var more = DesktopMoreDynamicFolder.Actions("VizhiDesktop", "Codex");
            Assert.Equal(new[] { "show_diff", "task_1", "task_4", "task_2", "task_3", "task_5", "task_6", "task_7" },
                tasks.Select(a => ActionString.FromString(a).ActionParameter));
            Assert.Contains(more, a => a.EndsWith("___task_8"));
            Assert.Contains(more, a => a.EndsWith("___task_9"));
            Assert.DoesNotContain(more, a => a.EndsWith("___show_diff"));
            for (var slot = 1; slot <= 9; slot++)
                Assert.Single(tasks.Concat(more), a => a.EndsWith("___task_" + slot));
            // ChatGPT Prompts fits one page too (eight plus Back); Continue, the ninth, is in More.
            var prompts = DesktopSavedPromptsDynamicFolder.Actions("VizhiDesktop", "ChatGPT");
            var chatMore = DesktopMoreDynamicFolder.Actions("VizhiDesktop", "ChatGPT");
            Assert.Equal(Enumerable.Range(1, 8).Select(i => "slot_" + i), prompts.Select(a => ActionString.FromString(a).ActionParameter));
            Assert.Contains(chatMore, a => a.EndsWith("DesktopWorkflowCommand___slot_9"));
            for (var slot = 1; slot <= 9; slot++)
                Assert.Single(prompts.Concat(chatMore), a => a.EndsWith("___slot_" + slot));
            Assert.DoesNotContain(chatMore, a => a.Contains("___task_"));
            Assert.Empty(DesktopSavedPromptsDynamicFolder.Actions("VizhiDesktop", null));
        }

        [Fact]
        public void A_reordered_chatgpt_continue_moves_to_more_from_its_own_slot()
        {
            var configured = Workflow.ChatGptDefaults.ToArray();
            (configured[1], configured[8]) = (configured[8], configured[1]);   // Continue now in slot 2
            var prompts = DesktopSavedPromptsDynamicFolder.Actions("VizhiDesktop", "ChatGPT", prompts: configured);
            var more = DesktopMoreDynamicFolder.Actions("VizhiDesktop", "ChatGPT", prompts: configured);
            Assert.Equal(8, prompts.Length);
            Assert.DoesNotContain(prompts, a => a.EndsWith("___slot_2"));
            Assert.Contains(prompts, a => a.EndsWith("___slot_9"));
            Assert.Single(more, a => a.EndsWith("DesktopWorkflowCommand___slot_2"));
        }

        [Fact]
        public void Reordered_custom_and_duplicate_id_tasks_keep_their_exact_slot_and_content()
        {
            var configured = Workflow.CodexDefaults.Reverse().Select(w => w.WithPrompt("custom " + w.Id)).ToArray();
            configured[1].Id = "mine"; configured[2].Id = "mine";
            var menu = DesktopSavedPromptsDynamicFolder.Actions("VizhiDesktop", "Codex", configured);
            Assert.Equal(9, menu.Length); // Customized Continue moves to More using its original slot.
            var more = DesktopMoreDynamicFolder.Actions("VizhiDesktop", "Codex", tasks: configured);
            Assert.DoesNotContain(menu, a => a.EndsWith("___task_1"));
            Assert.Contains(more, a => a.EndsWith("___task_1"));
            Assert.Equal(Enumerable.Range(2, 8).Select(i => "task_" + i),
                menu.Skip(1).Select(a => ActionString.FromString(a).ActionParameter));
            var named = new Dictionary<String, Workflow.WorkflowDef>();
            for (var i = 0; i < configured.Length; i++)
            {
                var action = Assert.Single(menu.Concat(more), a => a.EndsWith("___task_" + (i + 1)));
                var parameter = ActionString.FromString(action).ActionParameter;
                Assert.Equal("task_" + (i + 1), parameter);
                Assert.Same(configured[i], Workflow.Resolve(parameter, "Codex", Workflow.ChatGptDefaults, configured, named));
                Assert.Null(Workflow.Resolve(parameter, "ChatGPT", Workflow.ChatGptDefaults, configured, named));
            }
        }

        [Theory]
        [InlineData("ChatGPT", (Int32)DesktopControl.Changes, "Mode Changed")]
        [InlineData("Codex", 0, "Not available")]
        public void Tasks_review_refuses_wrong_mode_or_missing_capability_without_opening(String mode, Int32 controls, String expected)
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { Mode = mode, SurfaceAvailable = true, AvailableControls = (DesktopControl)controls } };
            Assert.Equal(expected, DesktopControlCommand.Execute("show_diff", new OpenAiDesktopAdapter(), fake));
            Assert.Equal("status", Assert.Single(fake.Calls).Name);
        }

        [Fact]
        public void Tasks_review_opens_once_and_never_sends_a_review_prompt()
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { Mode = "Codex", SurfaceAvailable = true, AvailableControls = DesktopControl.Changes } };
            Assert.Equal("Opened", DesktopControlCommand.Execute("show_diff", new OpenAiDesktopAdapter(), fake));
            Assert.Equal(new[] { "status", "open-changes" }, fake.Calls.Select(c => c.Name));
            Assert.True(DesktopNavigateCommand.IsHidden("search", "Codex"));
            Assert.False(DesktopNavigateCommand.IsHidden("search", "ChatGPT"));
            Assert.False(DesktopNavigateCommand.IsHidden("find", "Codex")); // Legacy assignments retain their direct route.
        }

        [Theory]
        [InlineData("stock", true)]
        [InlineData("reordered", true)]
        [InlineData("custom-label", false)]
        [InlineData("custom-prompt", false)]
        [InlineData("duplicate", false)]
        public void Rename_upgrades_only_untouched_review_and_preserves_json_and_backup(String scenario, Boolean changed)
        {
            var directory = Path.Combine(Path.GetTempPath(), "vizhi-task-label-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
            try
            {
                var doc = JsonSerializer.SerializeToNode(Workflow.CodexDefaults,
                    new JsonSerializerOptions { IgnoreReadOnlyProperties = true }).AsArray();
                doc[0]["Label"] = "Review Changes";
                doc[0]["custom-metadata"] = "preserved";
                if (scenario == "custom-label") doc[0]["Label"] = "My Review";
                if (scenario == "custom-prompt") doc[0]["Prompt"] = "My prompt";
                if (scenario == "reordered") { var first = doc[0]; doc.RemoveAt(0); doc.Add(first); }
                if (scenario == "duplicate") doc.Add(doc[0].DeepClone());
                var path = Path.Combine(directory, "desktop-workflows.json");
                var original = doc.ToJsonString(); File.WriteAllText(path, original);
                var result = Workflow.LoadWorkflows(path).ToArray();
                if (changed)
                {
                    doc.Single(w => (String)w["Id"] == "review_changes")["Label"] = "Review Code";
                    Assert.Equal("Review Code", result.Single(w => w.Id == "review_changes").Label);
                    Assert.Equal(original, File.ReadAllText(path + ".before-0.17.16"));
                }
                else Assert.False(File.Exists(path + ".before-0.17.16"));
                Assert.True(JsonNode.DeepEquals(doc, JsonNode.Parse(File.ReadAllText(path))));
                var once = File.ReadAllText(path); Workflow.LoadWorkflows(path).ToArray();
                Assert.Equal(once, File.ReadAllText(path));
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
