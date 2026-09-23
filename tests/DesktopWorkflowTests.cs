namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.DesktopActions;

    using Xunit;

    /// <summary>
    /// The Workflows page's template mechanism — the prompts.json idea pointed at a composer.
    /// The details that must not drift: user file wins outright, junk degrades to defaults, and
    /// the two templates that name a target the key cannot know (a PR number, an error) are
    /// DRAFTS, because auto-sending "Review pull request #:" would start a task on a blank.
    /// </summary>
    public sealed class DesktopWorkflowTests : IDisposable
    {
        private readonly String _root = Path.Combine(Path.GetTempPath(), $"vd-wf-{Guid.NewGuid():N}");

        public DesktopWorkflowTests() => Directory.CreateDirectory(this._root);

        public void Dispose()
        {
            try { Directory.Delete(this._root, recursive: true); } catch { }
        }

        private String ConfigPath => Path.Combine(this._root, "desktop-workflows.json");
        private String ChatGptConfigPath => Path.Combine(this._root, "desktop-chatgpt-workflows.json");

        [Fact]
        public void A_missing_file_seeds_an_editable_starter_and_returns_defaults()
        {
            var list = DesktopWorkflowCommand.LoadWorkflows(this.ConfigPath).ToList();

            Assert.Equal(9, list.Count);
            Assert.True(File.Exists(this.ConfigPath), "starter file was not written");
        }

        [Fact]
        public void A_user_file_wins_outright()
        {
            File.WriteAllText(this.ConfigPath,
                "[{\"id\":\"mine\",\"label\":\"Mine\",\"icon\":\"review\",\"prompt\":\"do my thing\"}]");

            var list = DesktopWorkflowCommand.LoadWorkflows(this.ConfigPath).ToList();

            var only = Assert.Single(list);
            Assert.Equal("mine", only.Id);
            Assert.True(only.Submits);   // absent submit means send, same as prompts.json
        }

        [Fact]
        public void Garbage_degrades_to_defaults_not_to_an_empty_page()
        {
            File.WriteAllText(this.ConfigPath, "not json{{{");

            Assert.Equal(9, DesktopWorkflowCommand.LoadWorkflows(this.ConfigPath).Count());
        }

        [Fact]
        public void Templates_that_name_an_unknown_target_are_drafts()
        {
            var defaults = DesktopWorkflowCommand.LoadWorkflows(this.ConfigPath).ToList();

            Assert.False(DesktopWorkflowCommand.ExtraWorkflows.Single(w => w.Id == "review_pr").Submits);
            Assert.False(defaults.Single(w => w.Id == "debug").Submits);
            Assert.False(defaults.Single(w => w.Id == "refactor").Submits);
            // The remaining briefs identify their scope.
            Assert.False(defaults.Single(w => w.Id == "fix_ci").Submits);
            Assert.False(defaults.Single(w => w.Id == "update_deps").Submits);
            Assert.All(defaults.Where(w => w.Id != "review_pr" && w.Id != "debug" && w.Id != "refactor" && w.Id != "fix_ci" && w.Id != "update_deps"),
                w => Assert.True(w.Submits));
        }

        [Fact]
        public void Every_default_icon_is_a_real_embedded_resource()
        {
            // A typo'd icon name renders as bare text and looks like a broken key. The desktop
            // product resolves every non-state icon from ITS identity folder (the plugin
            // constructor calls KeyImage.UseIdentityIconFolder("desktop_icons")), so that folder,
            // not the shared one, is what the mapping must be pinned to.
            var iconsDir = RepoDir("src", "Products", "VizhiDesktop", "Resources", "desktop_icons");

            var defaults = DesktopWorkflowCommand.LoadWorkflows(this.ConfigPath)
                .Concat(DesktopWorkflowCommand.LoadChatGptWorkflows(this.ChatGptConfigPath));

            foreach (var w in defaults)
            {
                Assert.True(File.Exists(Path.Combine(iconsDir, w.Icon + ".png")),
                    $"workflow '{w.Id}' names icon '{w.Icon}' which is not an embedded resource");
            }
        }

        [Fact]
        public void Chatgpt_has_nine_separate_editable_workflows()
        {
            var list = DesktopWorkflowCommand.LoadChatGptWorkflows(this.ChatGptConfigPath).ToList();

            Assert.Equal(9, list.Count);
            Assert.True(File.Exists(this.ChatGptConfigPath));
            Assert.Equal("summarize", list[0].Id);
            Assert.Equal("continue", list[8].Id);
        }

        [Fact]
        public void Adaptive_slots_follow_mode_without_changing_position()
        {
            var chat = DesktopWorkflowCommand.LoadChatGptWorkflows(this.ChatGptConfigPath).ToList();
            var codex = DesktopWorkflowCommand.LoadWorkflows(this.ConfigPath).ToList();

            Assert.Equal("summarize", DesktopWorkflowCommand.WorkflowAt("ChatGPT", 1, chat, codex).Id);
            Assert.Equal("review_changes", DesktopWorkflowCommand.WorkflowAt("Codex", 1, chat, codex).Id);
            Assert.Null(DesktopWorkflowCommand.WorkflowAt("", 1, chat, codex));
        }

        [Fact]
        public void Chatgpt_workflows_with_missing_targets_are_drafts()
        {
            var list = DesktopWorkflowCommand.LoadChatGptWorkflows(this.ChatGptConfigPath).ToList();
            var drafts = new[] { "rewrite", "draft", "compare", "research" };

            Assert.All(list.Where(w => drafts.Contains(w.Id)), w => Assert.False(w.Submits));
            Assert.All(list.Where(w => !drafts.Contains(w.Id)), w => Assert.True(w.Submits));
        }

        [Fact]
        public void Writing_draft_uses_a_pen_even_for_an_existing_default_configuration()
        {
            var legacy = new DesktopWorkflowCommand.WorkflowDef { Id = "draft", Icon = "voice_draft" };
            Assert.Equal("writing", DesktopWorkflowCommand.WorkflowIcon(legacy, "ChatGPT"));
            Assert.Equal("voice_draft", legacy.Icon); // do not rewrite user configuration
        }

        [Theory]
        [InlineData("ChatGPT", "rewrite", "document", "rewrite")]
        [InlineData("ChatGPT", "continue", "enter", "continue")]
        [InlineData("Codex", "continue", "enter", "continue")]
        [InlineData("ChatGPT", "rewrite", "brain", "brain")]
        [InlineData("ChatGPT", "summarize", "document", "document")]
        public void Semantic_icon_upgrade_preserves_configuration_and_explicit_alternatives(
            String mode, String id, String oldIcon, String expected)
        {
            var workflow = new DesktopWorkflowCommand.WorkflowDef { Id = id, Icon = oldIcon, Submit = false, Prompt = "Custom text" };
            Assert.Equal(expected, DesktopWorkflowCommand.WorkflowIcon(workflow, mode));
            Assert.Equal(oldIcon, workflow.Icon);
            Assert.False(workflow.Submits);
            Assert.Equal("Custom text", workflow.Prompt);
            Assert.True(File.Exists(Path.Combine(RepoDir("src", "Products", "VizhiDesktop", "Resources", "desktop_icons"), expected + ".png")));
        }

        [Fact]
        public void Optional_review_and_test_execution_do_not_displace_favorites()
        {
            var extras = DesktopWorkflowCommand.CodexDefaults;
            Assert.Contains(extras, w => w.Id == "review_changes" && w.Prompt.Contains("uncommitted"));
            Assert.Contains(extras, w => w.Id == "run_tests" && w.Prompt.Contains("Do not claim tests passed"));
            Assert.Equal(9, DesktopWorkflowCommand.LoadWorkflows(this.ConfigPath).Count());
        }

        [Fact]
        public void Flow_upgrade_preserves_custom_slots_and_metadata_backs_up_once_and_is_idempotent()
        {
            var raw = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(DesktopWorkflowMigration.CodexDefaults)).AsArray();
            raw[2]["Prompt"] = "My custom refactor scope";
            raw[2]["Note"] = "Keep this metadata";
            var original = raw.ToJsonString(); File.WriteAllText(ConfigPath, original);
            var first = DesktopWorkflowCommand.LoadWorkflows(ConfigPath).ToArray();
            Assert.Equal("review_changes", first[0].Id);
            Assert.Equal("run_tests", first[3].Id);
            Assert.Equal("My custom refactor scope", first[2].Prompt);
            Assert.Contains("Keep this metadata", File.ReadAllText(ConfigPath));
            Assert.Equal(original, File.ReadAllText(ConfigPath + ".before-flow"));
            var upgraded = File.ReadAllText(ConfigPath);
            DesktopWorkflowCommand.LoadWorkflows(ConfigPath).ToArray();
            Assert.Equal(upgraded, File.ReadAllText(ConfigPath));
            Assert.Equal(original, File.ReadAllText(ConfigPath + ".before-flow"));
        }

        [Fact]
        public void Voice_recipe_without_brief_placeholder_is_not_registered_as_a_usable_slot()
        {
            Assert.All(DesktopWorkflowCommand.ChatGptDefaults.Concat(DesktopWorkflowCommand.CodexDefaults)
                .Concat(DesktopWorkflowCommand.ExtraWorkflows).Where(w => w.RequiresSpeech),
                w => { Assert.Contains("{brief}", w.Prompt); Assert.False(w.Submits); });
        }

        private static String RepoDir(params String[] parts)
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException(String.Join("/", parts));
        }
    }
}
