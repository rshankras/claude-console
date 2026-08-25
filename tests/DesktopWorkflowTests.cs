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

            Assert.False(defaults.Single(w => w.Id == "review_pr").Submits);
            Assert.False(defaults.Single(w => w.Id == "debug").Submits);
            // ...and everything else sends on one press.
            Assert.All(defaults.Where(w => w.Id != "review_pr" && w.Id != "debug"),
                w => Assert.True(w.Submits));
        }

        [Fact]
        public void Every_default_icon_is_a_real_embedded_resource()
        {
            // A typo'd icon name renders as bare text and looks like a broken key; the icons
            // live as PNGs in Core/Resources/icons and this pins the mapping to real files.
            var iconsDir = RepoDir("src", "Core", "Resources", "icons");

            foreach (var w in DesktopWorkflowCommand.LoadWorkflows(this.ConfigPath))
            {
                Assert.True(File.Exists(Path.Combine(iconsDir, w.Icon + ".png")),
                    $"workflow '{w.Id}' names icon '{w.Icon}' which is not an embedded resource");
            }
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
