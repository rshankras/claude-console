namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    using Loupedeck.ClaudeConsolePlugin.Actions;

    using Xunit;

    /// <summary>
    /// prompts.json loading, and the 1.7 seed upgrade. The old defaults were one-liners that added
    /// nothing over typing the word yourself; the new ones are real prompts. But the seed file wins
    /// over the built-ins, so every existing install would keep the old prompts forever unless an
    /// UNEDITED seed is upgraded in place. The one unforgivable failure here is overwriting a file
    /// the user customized — any edit at all must block the upgrade.
    /// </summary>
    public class PromptSeedTests : IDisposable
    {
        private readonly String _dir =
            Path.Combine(Path.GetTempPath(), "cc-prompts-" + Guid.NewGuid().ToString("N"));

        private readonly String _file;

        public PromptSeedTests()
        {
            Directory.CreateDirectory(_dir);
            _file = Path.Combine(_dir, "prompts.json");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        // The pre-1.7 seed, as the old WriteStarter wrote it (indented, old prompt texts).
        private static List<PromptDef> LegacySeed() => new List<PromptDef>
        {
            new PromptDef { Id = "fix_bug",     Label = "Fix Bug",     Icon = "fix_bug",     Prompt = "Fix the bug in the current file" },
            new PromptDef { Id = "write_tests", Label = "Write Tests", Icon = "write_tests", Prompt = "Write tests for the changes you just made" },
            new PromptDef { Id = "explore",     Label = "Explore",     Icon = "explore",     Prompt = "Explore this codebase and explain its structure, key files, and how it works" },
            new PromptDef { Id = "explain",     Label = "Explain",     Icon = "explain",     Prompt = "Explain how this code works" },
            new PromptDef { Id = "refactor",    Label = "Refactor",    Icon = "refactor",    Prompt = "Refactor this for clarity" },
            new PromptDef { Id = "review",      Label = "Review",       Icon = "review",      Prompt = "Review this code for bugs and issues" },
            new PromptDef { Id = "optimize",    Label = "Optimize",    Icon = "optimize",    Prompt = "Optimize this for performance" },
            new PromptDef { Id = "security",    Label = "Security",     Icon = "security",    Prompt = "Check this code for security vulnerabilities" },
            new PromptDef { Id = "document",    Label = "Document",     Icon = "document",    Prompt = "Add documentation to this code" },
            new PromptDef { Id = "deploy",      Label = "Deploy",       Icon = "deploy",      Prompt = "Deploy this project" },
        };

        private void Write(List<PromptDef> prompts) =>
            File.WriteAllText(_file, JsonSerializer.Serialize(prompts, new JsonSerializerOptions { WriteIndented = true }));

        [Fact]
        public void Missing_file_is_seeded_with_the_new_defaults()
        {
            var loaded = PromptCommand.LoadPrompts(_file).ToList();

            Assert.True(File.Exists(_file));
            // The new prompts carry method + output expectations; "Explain how this code works"
            // era one-liners are gone.
            Assert.All(loaded, p => Assert.True(p.Prompt.Length > 100,
                $"'{p.Id}' looks like a one-liner again: \"{p.Prompt}\""));
        }

        [Fact]
        public void Unedited_legacy_seed_is_upgraded_in_place()
        {
            this.Write(LegacySeed());

            var loaded = PromptCommand.LoadPrompts(_file).ToList();

            Assert.DoesNotContain(loaded, p => p.Prompt == "Explain how this code works");
            // ...and the FILE was rewritten too, so the next load doesn't downgrade again.
            var onDisk = JsonSerializer.Deserialize<List<PromptDef>>(File.ReadAllText(_file));
            Assert.DoesNotContain(onDisk, p => p.Prompt == "Explain how this code works");
        }

        [Theory]
        [InlineData("prompt")]   // reworded one prompt
        [InlineData("label")]    // relabelled one key
        [InlineData("icon")]     // swapped an icon
        [InlineData("removed")]  // deleted a key
        [InlineData("added")]    // added their own key
        [InlineData("submit")]   // set a submit flag
        public void Any_user_edit_blocks_the_upgrade(String kind)
        {
            var seed = LegacySeed();
            switch (kind)
            {
                case "prompt": seed[3].Prompt = "Explain how this code works, in Tamil"; break;
                case "label": seed[3].Label = "Explica"; break;
                case "icon": seed[3].Icon = "review"; break;
                case "removed": seed.RemoveAt(9); break;
                case "added": seed.Add(new PromptDef { Id = "ship", Label = "Ship", Prompt = "Ship it" }); break;
                case "submit": seed[3].Submit = false; break;
            }
            this.Write(seed);
            var before = File.ReadAllText(_file);

            var loaded = PromptCommand.LoadPrompts(_file).ToList();

            Assert.Equal(before, File.ReadAllText(_file));   // file untouched
            Assert.Equal(seed.Count, loaded.Count);          // and served as-is
        }

        [Fact]
        public void Customized_file_is_served_verbatim()
        {
            var mine = new List<PromptDef>
            {
                new PromptDef { Id = "standup", Label = "Standup", Prompt = "Summarize today's changes as 3 bullets" },
            };
            this.Write(mine);

            var loaded = PromptCommand.LoadPrompts(_file).ToList();

            var p = Assert.Single(loaded);
            Assert.Equal("standup", p.Id);
        }
    }
}
