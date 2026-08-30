namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text.Json.Nodes;

    using Xunit;

    /// <summary>
    /// The approved hardware design is also what a clean install imports. The plugin is universal
    /// (#23) and ships no packaged profile, so the two downloadable fallbacks in profiles/ ARE the
    /// default layout: a developer who rearranges live Options+ keys but leaves these untouched
    /// fixes one keypad and ships the old layout to every new macOS and Windows import.
    /// tools/sync-default-profiles.py rewrites the first page; this guards the result.
    /// </summary>
    public class KeypadLayoutTests
    {
        private static readonly String[] ApprovedFirstPage =
        {
            "SessionSlotCommand___1", "SessionSlotCommand___2", "SessionSlotCommand___3",
            "ControlCommand___clear", "AnswerCommand___no", "AnswerCommand___yes",
            "ControlCommand___esc", "ControlCommand___tab", "VoiceCommand",
        };

        [Theory]
        [InlineData("ClaudeConsole-Keypad.lp5")]
        [InlineData("ClaudeConsole-Windows.lp5")]
        public void Fallback_profiles_use_the_approved_five_page_layout(String file)
        {
            var path = RepoFile("profiles", file);

            var page = PressPage(path, 0);
            Assert.Equal(ApprovedFirstPage, page.Select(ActionName).ToArray());
            Assert.Equal(5, PressPageCount(path));

            // The static preview Options+ shows before import mirrors the live first page.
            using var zip = ZipFile.OpenRead(path);
            var preview = (JsonArray)ReadJsonEntry(zip, "metadata/ProfilePreview.json")["buttonPages"];
            Assert.Equal(9, preview.Count);
            for (var i = 0; i < 9; i++)
            {
                Assert.Equal((Int32)page[i]!["controlId"], (Int32)preview[i]!["controlId"]);
                Assert.Equal((String)page[i]!["pressAction"], (String)preview[i]!["actionName"]);
            }
        }

        private static String ActionName(JsonNode control) =>
            ((String)control["pressAction"] ?? String.Empty).Split("Actions.", 2).Last();

        private static JsonArray PressPages(String path)
        {
            using var zip = ZipFile.OpenRead(path);
            var profile = ReadJsonEntry(zip, "ProfileInfo.json");
            return (JsonArray)profile["layout"]["layoutModes"][0]["workspaces"][0]["pressPages"];
        }

        private static JsonArray PressPage(String path, Int32 index) =>
            (JsonArray)PressPages(path)[index]["controls"];

        private static Int32 PressPageCount(String path) => PressPages(path).Count;

        private static JsonNode ReadJsonEntry(ZipArchive zip, String name)
        {
            using var stream = zip.GetEntry(name).Open();
            using var reader = new StreamReader(stream);
            return JsonNode.Parse(reader.ReadToEnd());
        }

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
