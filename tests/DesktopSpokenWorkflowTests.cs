namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;
    using WorkflowDef = Loupedeck.ClaudeConsolePlugin.DesktopActions.DesktopWorkflowCommand.WorkflowDef;

    public sealed class DesktopSpokenWorkflowTests : IDisposable
    {
        private readonly String _directory = Path.Combine(Path.GetTempPath(), "vizhi-spoken-" + Guid.NewGuid());
        public DesktopSpokenWorkflowTests() => Directory.CreateDirectory(_directory);
        public void Dispose() => Directory.Delete(_directory, true);
        private String Config => Path.Combine(_directory, "workflows.json");
        private static WorkflowDef[] Defaults(String mode) => mode == "Codex"
            ? DesktopWorkflowCommand.CodexDefaults : DesktopWorkflowCommand.ChatGptDefaults;
        private WorkflowDef[] Load(String mode) => (mode == "Codex"
            ? DesktopWorkflowCommand.LoadWorkflows(Config) : DesktopWorkflowCommand.LoadChatGptWorkflows(Config)).ToArray();

        public static IEnumerable<Object[]> SpokenRecipes() => new[] { "ChatGPT", "Codex" }.SelectMany(mode =>
            Defaults(mode).Concat(mode == "Codex" ? DesktopWorkflowCommand.ExtraWorkflows : Array.Empty<WorkflowDef>())
                .Where(w => w.RequiresSpeech).Select(w => new Object[] { mode, w.Id }));

        [Theory]
        [MemberData(nameof(SpokenRecipes))]
        public void Spoken_request_is_visible_first_and_preserved_until_an_explicit_send(String mode, String id)
        {
            var recipe = Defaults(mode).Concat(DesktopWorkflowCommand.ExtraWorkflows).Single(w => w.Id == id);
            const String brief = "I just would like to check whether is there any bug in this.\nKeep ‘quoted words’, தமிழ் 😀, and literal {brief}.";
            var fake = new DesktopCommandRig.Automation { Next = new() { SurfaceAvailable = true, Mode = mode }, SupportsAppend = true };
            var capture = new VoiceCaptureState(); var flow = new DesktopWorkflowVoice(fake, new(fake));
            Func<String, String> sink = null;
            void Toggle(VoiceIntent intent, Func<String, String> callback)
            { if (capture.Press(intent, DateTime.UnixEpoch).Action == VoiceAction.Start) sink = callback; }
            String Tap() => DesktopWorkflowCommand.Execute("slot_1", fake, (_, _) => recipe, flow, capture, new(fake), Toggle);
            Assert.Null(Tap()); Assert.NotNull(sink);
            Assert.Equal("Listening", flow.Face(id, mode, capture)?.Label);
            Assert.Null(Tap()); Assert.Equal(VoicePhase.Transcribing, capture.Phase);
            Assert.Null(sink(brief)); capture.Finish();
            var insertion = Assert.Single(fake.Calls, c => c.Name == "append");
            var prefix = "Your request:\n" + brief + "\n\n" + recipe.Label + " task:\n";
            Assert.StartsWith(prefix, insertion.Text);
            Assert.DoesNotContain(brief, insertion.Text[prefix.Length..]);
            Assert.False(recipe.Submits); Assert.DoesNotContain(fake.Calls, c => c.Send || c.Name is "send" or "send-prompt");
            Assert.Equal("Send Draft", flow.Face(id, mode, capture)?.Label);
            Assert.Equal("Sent", Tap()); Assert.Single(fake.Calls, c => c.Name == "send");
            Assert.Single(fake.Calls, c => c.Name == "append");
        }

        [Fact]
        public void Debug_investigates_before_fixing_and_allows_no_bug_found()
        {
            var prompt = DesktopWorkflowCommand.CodexDefaults.Single(w => w.Id == "debug").Prompt;
            Assert.Contains("If you find a bug", prompt);
            Assert.Contains("If no bug is found, say so.", prompt);
            Assert.True(prompt.IndexOf("Your request:", StringComparison.Ordinal) < prompt.IndexOf("Debug task:", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("Codex", false)]
        [InlineData("Codex", true)]
        [InlineData("ChatGPT", false)]
        [InlineData("ChatGPT", true)]
        public void Stock_upgrade_changes_only_prompt_text_preserves_metadata_and_backs_up_once(String mode, Boolean camelCase)
        {
            var previous = Defaults(mode).Select(w => DesktopSpokenWorkflowMigration.Previous.FirstOrDefault(p => p.Id == w.Id) ?? w).ToArray();
            var options = new JsonSerializerOptions { IgnoreReadOnlyProperties = true,
                PropertyNamingPolicy = camelCase ? JsonNamingPolicy.CamelCase : null };
            var raw = JsonSerializer.SerializeToNode(previous, options).AsArray();
            foreach (var row in raw) row["OwnerNote"] = "Keep this metadata";
            var original = raw.ToJsonString(); File.WriteAllText(Config, original);
            var updated = Load(mode);
            Assert.Equal(Defaults(mode).Select(w => w.Prompt), updated.Select(w => w.Prompt));
            var after = JsonNode.Parse(File.ReadAllText(Config)).AsArray();
            for (var i = 0; i < raw.Count; i++)
            {
                var key = camelCase ? "prompt" : "Prompt";
                raw[i][key] = after[i][key]?.DeepClone();
                Assert.True(JsonNode.DeepEquals(raw[i], after[i]));
            }
            Assert.Equal(original, File.ReadAllText(Config + ".before-0.17.9"));
            var first = File.ReadAllText(Config); Load(mode);
            Assert.Equal(first, File.ReadAllText(Config));
            Assert.Equal(original, File.ReadAllText(Config + ".before-0.17.9"));
        }

        [Theory]
        [InlineData("Prompt", "My exact custom wording: {brief}")]
        [InlineData("Label", "Custom Debug")]
        [InlineData("Icon", "custom_icon")]
        [InlineData("Scope", "CUSTOM")]
        [InlineData("SourcePrompt", "My source instruction")]
        [InlineData("Input", "custom")]
        [InlineData("Submit", "true")]
        public void A_customized_recipe_is_preserved_while_an_unchanged_neighbor_upgrades(String field, String value)
        {
            var raw = JsonSerializer.SerializeToNode(DesktopSpokenWorkflowMigration.Previous.Take(2)).AsArray();
            raw[0][field] = field == "Submit" ? JsonValue.Create(true) : JsonValue.Create(value);
            var customized = raw[0].DeepClone();
            File.WriteAllText(Config, raw.ToJsonString()); Load("Codex");
            var after = JsonNode.Parse(File.ReadAllText(Config)).AsArray();
            Assert.True(JsonNode.DeepEquals(customized, after[0]));
            Assert.StartsWith("Your request:\n", after[1]["Prompt"].GetValue<String>());
        }

        [Fact]
        public void Reordered_partial_and_optional_recipes_keep_their_order_and_upgrade_by_id()
        {
            var previous = DesktopSpokenWorkflowMigration.Previous.Where(w => w.Id is "review_pr" or "debug").Reverse().ToArray();
            File.WriteAllText(Config, JsonSerializer.Serialize(previous));
            var updated = Load("Codex");
            Assert.Equal(previous.Select(w => w.Id), updated.Select(w => w.Id));
            Assert.All(updated, w => Assert.StartsWith("Your request:\n", w.Prompt));
        }

        [Fact]
        public void Ambiguous_ids_and_settings_changed_since_read_are_not_overwritten()
        {
            var stock = DesktopSpokenWorkflowMigration.Previous.First();
            var duplicate = new[] { stock, stock };
            var original = JsonSerializer.Serialize(duplicate); File.WriteAllText(Config, original);
            Load("Codex"); Assert.Equal(original, File.ReadAllText(Config));
            Assert.False(File.Exists(Config + ".before-0.17.9"));
            var changed = stock.WithPrompt("Changed after loading: {brief}");
            var latest = JsonSerializer.Serialize(new[] { changed }); File.WriteAllText(Config, latest);
            DesktopSpokenWorkflowMigration.Upgrade(Config, new[] { stock }, Defaults("Codex"));
            Assert.Equal(latest, File.ReadAllText(Config)); Assert.False(File.Exists(Config + ".before-0.17.9"));
        }
    }
}
