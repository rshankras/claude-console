namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System.IO.Compression;
    using System.Text.Json.Nodes;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration;
    using Xunit;

    public class DesktopViewChangesTests
    {
        [Theory]
        [InlineData(true, "Opened")]
        [InlineData(false, "Couldn't open")]
        public void Codex_stays_on_the_current_keypad_page_and_reports_the_confirmed_result(Boolean succeeds, String expected)
        {
            var fake = new DesktopCommandRig.Automation { Succeeds = succeeds, Next = new() { Mode = "Codex", SurfaceAvailable = true, AvailableControls = DesktopControl.Changes } };
            var searches = 0;
            Assert.Equal(expected, DesktopNavigateCommand.Execute("Codex", fake, () => searches++));
            Assert.Equal(0, searches);
            Assert.Equal(new[] { "status", "open-changes" }, fake.Calls.Select(c => c.Name));
        }

        [Theory]
        [InlineData("Codex", "ChatGPT", true, "Mode Changed")]
        [InlineData("ChatGPT", "Codex", true, "Mode Changed")]
        [InlineData("Codex", "Codex", false, "Open App")]
        [InlineData(null, "Codex", true, "Unavailable")]
        public void Stale_or_missing_mode_does_not_open_any_destination(String shown, String actual, Boolean available, String expected)
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { Mode = actual, SurfaceAvailable = available } };
            Assert.Equal(expected, DesktopNavigateCommand.Execute(shown, fake, () => Assert.Fail("No folder should open")));
            Assert.DoesNotContain(fake.Calls, c => c.Name != "status");
        }

        [Fact]
        public void Chatgpt_opens_the_existing_search_folder_only_once()
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { Mode = "ChatGPT", SurfaceAvailable = true } };
            var searches = 0;
            Assert.Null(DesktopNavigateCommand.Execute("ChatGPT", fake, () => searches++));
            Assert.Equal(1, searches); Assert.Equal("status", Assert.Single(fake.Calls).Name);
            Assert.Equal("DynamicFolder#" + typeof(FindChatDynamicFolder).FullName, DesktopNavigateCommand.SearchFolderParameter);
        }

        [Fact]
        public void A_site_building_chat_without_review_controls_does_not_dispatch_any_open_action()
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { Mode = "Codex", SurfaceAvailable = true } };
            Assert.Equal("Not available", DesktopNavigateCommand.Execute("Codex", fake, () => Assert.Fail("Not a search request")));
            Assert.Equal("status", Assert.Single(fake.Calls).Name);
            var face = DesktopNavigateCommand.FaceFor(DesktopMonitor.Map(fake.Next));
            Assert.Equal("View Changes", face.Label); Assert.False(face.Enabled); Assert.Equal("Not available", face.Status);
        }

        [Theory]
        [InlineData("panel-not-available", "Not available")]
        [InlineData("panel-unconfirmed", "Couldn't open")]
        [InlineData("panel-target-changed", "Couldn't open")]
        public void Missing_capability_and_a_failed_open_have_distinct_feedback(String error, String expected)
        {
            var fake = new DesktopCommandRig.Automation { Succeeds = false, Error = error };
            Assert.Equal(expected, DesktopNavigateCommand.OpenChanges(fake));
        }

        [Fact]
        public void Status_requests_explicit_panel_capability_without_a_keyboard_fallback()
        {
            List<String> call = null;
            var auto = new MacDesktopAutomation(new OpenAiDesktopAdapter()) { Runner = (args, _) => { call = args; return "{\"ok\":true,\"surface\":true,\"mode\":\"Codex\",\"changesPresent\":false}"; } };
            Assert.False(auto.Status().AvailableControls.HasFlag(DesktopControl.Changes));
            Assert.Contains("--panel-visible", call); Assert.Equal("Codex", call[call.IndexOf("--panel-mode") + 1]);
        }

        [Theory]
        [InlineData("{\"ok\":true,\"opened\":true}", true)]
        [InlineData("{\"ok\":true}", false)]
        [InlineData("{\"ok\":true,\"opened\":false}", false)]
        [InlineData("{\"ok\":false,\"error\":\"panel-unconfirmed\"}", false)]
        [InlineData("not json", false)]
        public void Mac_requires_positive_confirmation_and_carries_panel_and_mode_guards(String reply, Boolean success)
        {
            var calls = new List<List<String>>();
            var auto = new MacDesktopAutomation(new OpenAiDesktopAdapter()) { Runner = (args, _) => { calls.Add(args); return reply; } };
            Assert.Equal(success, auto.OpenChanges(out var error));
            Assert.Equal(success ? null : reply == "not json" ? "unexpected-reply" : "panel-unconfirmed", error);
            var call = Assert.Single(calls);
            Assert.Equal("open-panel", call[0]); Assert.Contains("--expect-mode", call); Assert.Contains("Codex", call);
            Assert.Contains("--conv-marker", call); Assert.Contains("--panel-visible", call);
            Assert.DoesNotContain("--panel-key-code", call);
            Assert.DoesNotContain("--panel-modifiers", call);
            Assert.DoesNotContain("Toggle file diff", call);
            Assert.Equal(new[] { "Changes", "This branch" }, new OpenAiDesktopAdapter().ShowDiffLabels);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Migration_only_changes_the_stock_home_key_and_keeps_an_original_backup(Boolean customized)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vizhi-home-" + Guid.NewGuid());
            Directory.CreateDirectory(temp);
            try
            {
                // Test the actual packaged schema, including all unrelated keys and metadata.
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                while (!File.Exists(Path.Combine(root.FullName, "tools/make-desktop-profile.py"))) root = root.Parent;
                using var zip = ZipFile.OpenRead(Path.Combine(root.FullName, "src/Products/VizhiDesktop/package/profiles/DefaultProfile70.lp5"));
                using var reader = new StreamReader(zip.GetEntry("ProfileInfo.json").Open());
                var doc = JsonNode.Parse(reader.ReadToEnd());
                var controls = doc["layout"]["layoutModes"][0]["workspaces"][0]["pressPages"][0]["controls"].AsArray();
                var key = controls.Single(c => (Int32)c["controlId"] == 5);
                key["pressAction"] = customized ? "my-custom-binding" : DesktopHomeNavigationMigration.OldBinding;
                doc["custom-user-setting"] = "preserved";
                var original = doc.ToJsonString();
                var path = Path.Combine(temp, "Profiles", DesktopHomeNavigationMigration.Profiles[0], "ProfileInfo.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, original);
                Assert.Equal(!customized, DesktopHomeNavigationMigration.Upgrade(temp));
                if (!customized)
                {
                    key["pressAction"] = DesktopHomeNavigationMigration.NewBinding;
                    Assert.Equal(original, File.ReadAllText(path + ".before-0.17.10"));
                }
                else Assert.False(File.Exists(path + ".before-0.17.10"));
                Assert.True(JsonNode.DeepEquals(doc, JsonNode.Parse(File.ReadAllText(path))));
                var first = File.ReadAllText(path);
                Assert.False(DesktopHomeNavigationMigration.Upgrade(temp));
                Assert.Equal(first, File.ReadAllText(path));
            }
            finally { Directory.Delete(temp, true); }
        }
    }
}
