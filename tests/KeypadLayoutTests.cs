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

        private static readonly String[] VizhiFirstPage =
        {
            "SessionSlotCommand___1", "SessionSlotCommand___2", "SessionSlotCommand___3",
            "ControlCommand___esc", "AnswerCommand___no", "AnswerCommand___yes",
            "ScreenshotCommand", "VoiceCommand", "VoiceDraftCommand",
        };

        private static readonly String[] VizhiCodexPage =
        {
            "ModelCycleCommand", "ControlCommand___plan_native", "ControlCommand___skills",
            "ControlCommand___review", "ContextCommand", "ControlCommand___compact",
            "AnswerCommand___up", "AnswerCommand___enter", "AnswerCommand___down",
        };

        private static readonly String[] VizhiPromptPage =
        {
            "PromptCommand___explore", "PromptCommand___explain", "PromptCommand___document",
            "PromptCommand___optimize", "PromptCommand___refactor", "PromptCommand___fix_bug",
            "PromptCommand___review", "PromptCommand___write_tests", "PromptCommand___security",
        };

        private static readonly String[] VizhiTerminalPage =
        {
            "ProjectVoiceCommand", "NavCommand___new_tab", "NavCommand___new_claude",
            "NavCommand___prev_tab", "NavCommand___next_tab", "ControlCommand___exit",
            "ControlCommand___agent", "ControlCommand___fork", "ControlCommand___resume",
        };

        private static readonly String[] VizhiGitPage =
        {
            "GitCommand___status", "GitCommand___diff", "GitCommand___log",
            "GitCommand___commit", "GitCommand___push", "GitCommand___create_pr",
            "", "", "",
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

        [Theory]
        [InlineData("VizhiCodex-Keypad.lp5", "com.apple.terminal", "com.apple.Terminal", "DefaultMac")]
        [InlineData("VizhiCodex-Windows.lp5", "windowsterminal", "WindowsTerminal", "DefaultWin")]
        public void Vizhi_profiles_match_the_host_and_never_bind_unsupported_or_Claude_actions(
            String file, String applicationName, String processName, String defaultPlugin)
        {
            var path = RepoFile("profiles", file);
            var page = PressPage(path, 0);
            Assert.Equal(VizhiFirstPage, page.Select(ActionName).ToArray());
            Assert.Equal(VizhiCodexPage, PressPage(path, 1).Select(ActionName).ToArray());
            Assert.Equal(VizhiPromptPage, PressPage(path, 2).Select(ActionName).ToArray());
            Assert.Equal(VizhiTerminalPage, PressPage(path, 3).Select(ActionName).ToArray());
            Assert.Equal(VizhiGitPage, PressPage(path, 4).Select(ActionName).ToArray());
            Assert.Equal(5, PressPageCount(path));
            Assert.Equal(5, PressPages(path).Select(p => (String)p["name"]).Distinct().Count());

            using var zip = ZipFile.OpenRead(path);
            var profile = ReadJsonEntry(zip, "ProfileInfo.json");
            var application = ReadJsonEntry(zip, "ApplicationInfo.json");
            Assert.Equal(applicationName, (String)profile["applicationName"]);
            Assert.Equal(applicationName, (String)application["name"]);
            Assert.Equal(processName, (String)application["processOrBundleName"]);
            Assert.Equal((String)profile["name"], (String)application["defaultProfileName"]);

            var plugins = (JsonArray)profile["additionalNativePluginNames"];
            Assert.Contains(defaultPlugin, plugins.Select(n => (String)n));
            Assert.Contains("VizhiCodex", plugins.Select(n => (String)n));
            Assert.DoesNotContain("ClaudeConsole", plugins.Select(n => (String)n));

            foreach (var pressPage in (JsonArray)profile["layout"]["layoutModes"][0]["workspaces"][0]["pressPages"])
            {
                foreach (var control in (JsonArray)pressPage["controls"])
                {
                    var action = (String)control["pressAction"];
                    if (String.IsNullOrEmpty(action))
                    {
                        continue;
                    }
                    Assert.StartsWith("$VizhiCodex___", action);
                    Assert.DoesNotContain("CostDisplayCommand", action);
                    Assert.DoesNotContain("ControlCommand___tab", action);
                }
            }

            // Options+ shows this static strip before import. It must advertise the same product
            // and positions as the live first page, not the Claude profile the generator started from.
            var preview = (JsonArray)ReadJsonEntry(zip, "metadata/ProfilePreview.json")["buttonPages"];
            Assert.Equal(9, preview.Count);
            for (var i = 0; i < 9; i++)
            {
                Assert.Equal((Int32)page[i]["controlId"], (Int32)preview[i]["controlId"]);
                Assert.Equal((String)page[i]["pressAction"], (String)preview[i]["actionName"]);
                Assert.StartsWith("$VizhiCodex___", (String)preview[i]["actionName"]);
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
