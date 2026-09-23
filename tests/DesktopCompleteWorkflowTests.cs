namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;

    public sealed class DesktopCompleteWorkflowTests : IDisposable
    {
        private readonly String _directory = Path.Combine(Path.GetTempPath(), "vizhi-workflow-" + Guid.NewGuid());
        public DesktopCompleteWorkflowTests() { Directory.CreateDirectory(_directory); }
        public void Dispose() { Directory.Delete(_directory, true); }
        private DesktopFile File(String name)
        { var path = Path.Combine(_directory, name); System.IO.File.WriteAllText(path, "Fixture " + name); return DesktopFile.Read(path); }
        private static DesktopCommandRig.Automation App(Boolean content = false) => new()
        {
            Next = new() { SurfaceAvailable = true, Mode = "ChatGPT" }, SupportsAppend = true,
            AppendDestination = new() { Target = "original-chat", Fingerprint = "original-draft", HasContent = content }
        };

        [Theory]
        [InlineData("summarize")]
        [InlineData("explain")]
        [InlineData("brainstorm")]
        [InlineData("plan")]
        [InlineData("continue")]
        public void Existing_material_gets_a_source_instruction_and_waits_for_explicit_send(String id)
        {
            var app = App(true); var context = new DesktopContextCapture(app); var voice = new VoiceCaptureState();
            var flow = new DesktopWorkflowVoice(app, new(app), context);
            var recipe = DesktopWorkflowCommand.ChatGptDefaults.Single(w => w.Id == id);
            String Tap() => DesktopWorkflowCommand.Execute("slot_1", app, (_, _) => recipe, flow, voice, new(app), (_, _) => Assert.Fail("No recording needed"), context);
            Assert.Equal("Draft Ready", Tap());
            var append = Assert.Single(app.Calls, c => c.Name == "append"); Assert.Equal(recipe.SourcePrompt, append.Text);
            Assert.Equal("original-chat", append.Labels[1]); Assert.Equal("original-draft", append.Labels[2]);
            Assert.DoesNotContain(app.Calls, c => c.Name is "write" or "send" or "prompt");
            Assert.Equal("Sent", Tap()); Assert.Single(app.Calls, c => c.Name == "send");
            Assert.Single(app.Calls, c => c.Name == "append");
        }

        [Theory]
        [InlineData("ChatGPT", "summarize")]
        [InlineData("ChatGPT", "explain")]
        [InlineData("ChatGPT", "brainstorm")]
        [InlineData("ChatGPT", "plan")]
        [InlineData("ChatGPT", "continue")]
        [InlineData("Codex", "review_changes")]
        [InlineData("Codex", "run_tests")]
        [InlineData("Codex", "explain_diff")]
        [InlineData("Codex", "security")]
        [InlineData("Codex", "continue")]
        public void An_empty_composer_inserts_the_complete_preset_then_sends_once(String mode, String id)
        {
            var app = App(); app.Next = new() { SurfaceAvailable = true, Mode = mode };
            var voice = new VoiceCaptureState(); var flow = new DesktopWorkflowVoice(app, new(app)) { Clock = () => 1000 };
            var recipe = (mode == "Codex" ? DesktopWorkflowCommand.CodexDefaults : DesktopWorkflowCommand.ChatGptDefaults).Single(w => w.Id == id);
            String Tap() => DesktopWorkflowCommand.Execute("slot_1", app, (_, _) => recipe, flow, voice, new(app), (_, _) => Assert.Fail());
            Assert.Equal("Sent", Tap());
            Assert.Equal("Sent", Tap()); // a rapid second tap must not type or submit again
            var append = Assert.Single(app.Calls, c => c.Name == "append");
            var prompt = Assert.Single(app.Calls, c => c.Name == "send-prompt");
            Assert.Equal(recipe.Prompt, append.Text);
            Assert.Equal(recipe.Prompt, prompt.Text); Assert.True(prompt.Send);
            Assert.Equal(new[] { mode, "original-chat" }, prompt.Labels);
            Assert.Equal(new[] { mode, "original-chat", "original-draft", "False" }, append.Labels);
            Assert.True(app.Calls.IndexOf(append) < app.Calls.IndexOf(prompt));
            Assert.DoesNotContain(app.Calls, c => c.Name is "write" or "prompt" or "send");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_failed_preset_retry_keeps_its_original_baseline_and_never_duplicates_or_auto_sends(Boolean completedLate)
        {
            var app = App(); app.Next = new() { SurfaceAvailable = true, Mode = "Codex" };
            var recipe = DesktopWorkflowCommand.CodexDefaults.Single(w => w.Id == "review_changes");
            var recovery = new DesktopDraftRecovery(app); var flow = new DesktopWorkflowVoice(app, recovery); var voice = new VoiceCaptureState();
            var composer = ""; var mutations = 0;
            app.Appender = (text, mode, target, retry) =>
            {
                Assert.Equal(recipe.Prompt, text); Assert.Equal("original-draft", target.Fingerprint);
                if (retry) return (composer == text, composer == text ? null : "draft-changed");
                composer = completedLate ? text : text[..120]; mutations++;
                app.AppendDestination = new() { Target = target.Target, Fingerprint = "partial-or-complete", HasContent = true };
                return (false, "append-unconfirmed");
            };
            String Tap() => DesktopWorkflowCommand.Execute("slot_1", app, (_, _) => recipe, flow, voice, new(app), (_, _) => Assert.Fail());
            Assert.Equal("Insert Draft", Tap()); Assert.True(recovery.Pending);
            Assert.Equal(completedLate ? "Draft Ready" : "Draft Changed", Tap());
            Assert.Equal(completedLate ? recipe.Prompt : recipe.Prompt[..120], composer);
            Assert.Equal(1, mutations); Assert.Single(app.Calls, c => c.Name == "prepare-append");
            Assert.Equal(!completedLate, recovery.Pending);
            Assert.DoesNotContain(app.Calls, c => c.Send || c.Name is "send" or "write" or "prompt");
            if (completedLate) { Assert.Equal("Sent", Tap()); Assert.Single(app.Calls, c => c.Name == "send"); }
            else { Assert.Equal("Draft Changed", Tap()); Assert.Equal(1, mutations); }
        }

        [Theory]
        [InlineData("draft-changed", "Draft Changed")]
        [InlineData("composer-target-changed", "Chat Changed")]
        [InlineData("send-press-failed", "Check App")]
        public void Failed_immediate_send_preserves_the_draft_and_does_not_reinsert(String failure, String feedback)
        {
            var app = App(); var voice = new VoiceCaptureState(); var recovery = new DesktopDraftRecovery(app);
            var flow = new DesktopWorkflowVoice(app, recovery); var recipe = DesktopWorkflowCommand.ChatGptDefaults[0];
            app.PromptSender = (_, _, _) => (false, failure);
            String Tap() => DesktopWorkflowCommand.Execute("slot_1", app, (_, _) => recipe, flow, voice, new(app), (_, _) => Assert.Fail());
            Assert.Equal(feedback, Tap()); Assert.False(recovery.Pending);
            Assert.Equal("Send Draft", flow.Face(recipe.Id, "ChatGPT", voice)?.Label);
            Assert.Equal("Sent", Tap());
            Assert.Single(app.Calls, c => c.Name == "append");
            Assert.Single(app.Calls, c => c.Name == "send-prompt");
            Assert.Single(app.Calls, c => c.Name == "send");
        }

        [Fact]
        public void Custom_prompt_text_is_preserved_when_appended_to_existing_material()
        {
            var app = App(true); var recipe = new DesktopWorkflowCommand.WorkflowDef { Id = "custom", Prompt = "My exact custom instruction." };
            Assert.Equal("Draft Ready", DesktopWorkflowCommand.Execute("slot_1", app, (_, _) => recipe,
                new(app, new(app)), new(), new(app), (_, _) => Assert.Fail()));
            Assert.Equal(recipe.Prompt, Assert.Single(app.Calls, c => c.Name == "append").Text);
            Assert.DoesNotContain(app.Calls, c => c.Send);
        }

        [Fact]
        public void Refused_material_insertion_retains_the_instruction_and_never_sends()
        {
            var app = App(true); app.Appender = (_, _, _, _) => (false, "composer-target-changed");
            var recovery = new DesktopDraftRecovery(app); var flow = new DesktopWorkflowVoice(app, recovery);
            Assert.Equal("Insert Draft", DesktopWorkflowCommand.Execute("slot_1", app, (_, _) => DesktopWorkflowCommand.ChatGptDefaults[0],
                flow, new(), new(app), (_, _) => Assert.Fail()));
            Assert.True(recovery.Pending); Assert.DoesNotContain(app.Calls, c => c.Send || c.Name == "send");
        }

        [Fact]
        public void Picker_selects_files_on_keypad_and_attaches_to_original_target_only()
        {
            var first = File("one.pdf"); var second = File("two.txt"); var app = App(true);
            var picker = new DesktopFilePicker(app, new(app), _directory); picker.Begin();
            Assert.Equal(2, picker.Current.Files.Length);
            picker.Execute(picker.Parameter(first), new OpenAiDesktopAdapter(), new());
            picker.Execute(picker.Parameter(second), new OpenAiDesktopAdapter(), new());
            Assert.Equal(2, picker.Current.Selected.Length);
            Assert.DoesNotContain(app.Calls, c => c.Name == "files");
            app.FilesAttacher = (files, mode, target) => { Assert.Equal(2, files.Length); Assert.Equal("original-chat", target); return (true, null); };
            Assert.True(picker.Execute("attach", new OpenAiDesktopAdapter(), new()));
            Assert.Equal("Attached", picker.Current.Feedback); Assert.Empty(picker.Current.Selected);
            Assert.DoesNotContain(app.Calls, c => c.Name is "write" or "append" or "send");
        }

        [Fact]
        public void Picker_refuses_changed_files_and_stale_keys_without_retargeting()
        {
            var file = File("one.txt"); var app = App(); var picker = new DesktopFilePicker(app, new(app), _directory);
            picker.Begin(); var old = picker.Parameter(file); picker.Execute(old, new OpenAiDesktopAdapter(), new());
            System.IO.File.AppendAllText(file.Path, "changed");
            Assert.False(picker.Execute("attach", new OpenAiDesktopAdapter(), new())); Assert.Equal("Files Changed", picker.Current.Feedback);
            Assert.DoesNotContain(app.Calls, c => c.Name == "files");
            picker.End(); picker.Begin(); picker.Execute(old, new OpenAiDesktopAdapter(), new()); Assert.Empty(picker.Current.Selected);
        }

        [Fact]
        public void Picker_uncertain_result_cannot_be_blindly_retried()
        {
            var file = File("one.txt"); var app = App(); app.FilesAttacher = (_, _, _) => (false, "attachment-unconfirmed");
            var picker = new DesktopFilePicker(app, new(app), _directory); picker.Begin();
            picker.Execute(picker.Parameter(file), new OpenAiDesktopAdapter(), new());
            Assert.False(picker.Execute("attach", new OpenAiDesktopAdapter(), new())); Assert.True(picker.Current.Uncertain);
            Assert.False(picker.Execute("attach", new OpenAiDesktopAdapter(), new())); Assert.Single(app.Calls, c => c.Name == "files");
        }

        [Fact]
        public void Downloads_are_bounded_and_symlinks_are_not_presented_as_files()
        {
            for (var i = 0; i < 25; i++) File(i + ".txt");
            System.IO.File.CreateSymbolicLink(Path.Combine(_directory, "link.txt"), Path.Combine(_directory, "0.txt"));
            File(".hidden.txt"); var app = App(); var picker = new DesktopFilePicker(app, new(app), _directory); picker.Begin();
            Assert.Equal(18, picker.Current.Files.Length);
            Assert.DoesNotContain(picker.Current.Files, f => f.Name is "link.txt" or ".hidden.txt");
            foreach (var file in picker.Current.Files.Take(9)) picker.Execute(picker.Parameter(file), new OpenAiDesktopAdapter(), new());
            Assert.Equal(8, picker.Current.Selected.Length); Assert.Equal("8 Files Max", picker.Current.Feedback);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Clipboard_files_or_image_attach_without_pasting_filename_text_or_staging(Boolean image)
        {
            var file = File(image ? "clipboard.png" : "report.pdf"); var app = App(true);
            app.CaptureResult = _ => new() { Ok = true, Text = "filename fallback", Image = image ? file.Path : null, Files = image ? null : new[] { file.Path } };
            var context = new DesktopContextCapture(app);
            Assert.Equal("Attached", context.Execute("clipboard", new()));
            Assert.Single(app.Calls, c => c.Name == "files"); Assert.Equal(0, context.Count);
            Assert.DoesNotContain(app.Calls, c => c.Name is "append" or "write" or "send");
            Assert.Equal(image ? "Image" : "Files", context.ClipboardKind);
        }

        [Fact]
        public void File_picker_can_retry_an_initially_unready_chat_without_sending_or_reusing_a_stale_target()
        {
            File("report.txt"); var app = App(); app.Succeeds = false; app.Error = "composer-unavailable";
            var picker = new DesktopFilePicker(app, new(app), _directory); picker.Begin();
            Assert.Equal("Wait", picker.Current.Feedback); Assert.Null(picker.Current.Target);
            app.Succeeds = true; picker.Execute("refresh", new OpenAiDesktopAdapter(), new());
            Assert.Equal("original-chat", picker.Current.Target); Assert.Single(picker.Current.Files);
            Assert.DoesNotContain(app.Calls, c => c.Name is "files" or "append" or "write" or "send");
        }

        [Fact]
        public void Stock_upgrade_preserves_unknown_metadata_custom_prompts_and_backup()
        {
            var path = Path.Combine(_directory, "settings.json");
            var data = JsonSerializer.SerializeToNode(DesktopWorkflowUxMigration.ChatGptDefaults).AsArray();
            data[0]["OwnerNote"] = "keep me"; data[1]["Prompt"] = "Custom explain instruction";
            System.IO.File.WriteAllText(path, data.ToJsonString());
            var upgraded = DesktopWorkflowCommand.LoadChatGptWorkflows(path).ToArray();
            Assert.NotNull(upgraded[0].SourcePrompt); Assert.Equal("Custom explain instruction", upgraded[1].Prompt);
            var after = JsonNode.Parse(System.IO.File.ReadAllText(path)); Assert.Equal("keep me", after[0]["OwnerNote"].GetValue<String>());
            Assert.True(System.IO.File.Exists(path + ".before-0.17"));
            var first = System.IO.File.ReadAllText(path); DesktopWorkflowCommand.LoadChatGptWorkflows(path).ToArray();
            Assert.Equal(first, System.IO.File.ReadAllText(path));
        }
    }
}
