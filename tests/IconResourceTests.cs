namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;

    using Xunit;

    /// <summary>Guards the single designer icon language used by both product packages.</summary>
    public class IconResourceTests
    {
        [Fact]
        public void Obsolete_icon_resources_stay_removed()
        {
            var directory = IconDirectory();
            foreach (var name in new[] { "busy", "clear_core", "context", "model", "yes", "no" })
            {
                Assert.False(File.Exists(Path.Combine(directory, name + ".png")),
                    $"obsolete icon returned: {name}.png");
            }
        }

        [Fact]
        public void Optional_actions_have_designer_pipeline_icons()
        {
            var directory = IconDirectory();
            var optional = new[]
            {
                "gauge", "gauge_warn", "gauge_crit",
                "done", "waiting", "busy0", "busy1",
                "plan", "review_core", "screenshot", "voice_draft", "deploy", "terminal",
                "new_claude_window", "next_window", "prev_window",
            };

            foreach (var name in optional)
            {
                Assert.True(File.Exists(Path.Combine(directory, name + ".png")),
                    $"optional action has no icon: {name}.png");
            }

            // Static SF Symbols were the visual mismatch this cleanup removes. The supplemental
            // generator is intentionally limited to the live listening animation now.
            var generator = File.ReadAllText(RepoFile("tools", "generate-icons.swift"));
            Assert.DoesNotContain("systemSymbolName", generator);
        }

        private static String IconDirectory() => Path.GetDirectoryName(
            RepoFile("src", "Core", "Resources", "icons", "brain.png"));

        private static String RepoFile(params String[] parts)
        {
            var directory = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && directory != null; i++)
            {
                var candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                directory = Path.GetDirectoryName(directory);
            }
            throw new FileNotFoundException(String.Join("/", parts));
        }
    }
}
