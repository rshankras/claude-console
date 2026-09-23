namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System.IO.Compression;
    using System.Text.Json.Nodes;
    using Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration;
    using Xunit;

    public class DesktopHomeScreenshotTests
    {
        private static String Package()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "tools/make-desktop-profile.py"))) root = root.Parent;
            return Path.Combine(root.FullName, "src/Products/VizhiDesktop/package/profiles/DefaultProfile70.lp5");
        }

        private static JsonNode Key(JsonNode doc, String pageName) => doc["layout"]["layoutModes"].AsArray()
            .Single(m => (String)m["modeName"] == "main")["workspaces"][0]["pressPages"].AsArray()
            .Single(p => (String)p["displayName"] == pageName)["controls"].AsArray().Single(c => (Int32)c["controlId"] == 5);

        [Theory]
        [InlineData("stock", true)]
        [InlineData("legacy", true)]
        [InlineData("custom-home", false)]
        [InlineData("custom-tools", false)]
        [InlineData("different-owner", false)]
        [InlineData("reordered-pages", true)]
        public void Migration_swaps_the_stock_pair_atomically_preserving_customizations_and_backup(String scenario, Boolean expected)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vizhi-screenshot-" + Guid.NewGuid());
            try
            {
                using var zip = ZipFile.OpenRead(Package());
                using var reader = new StreamReader(zip.GetEntry("ProfileInfo.json").Open());
                var doc = JsonNode.Parse(reader.ReadToEnd());
                var home = Key(doc, "Home"); var tools = Key(doc, "Tools");
                home["pressAction"] = scenario == "legacy" ? DesktopHomeNavigationMigration.OldBinding : DesktopHomeScreenshotMigration.Navigation;
                tools["pressAction"] = DesktopHomeScreenshotMigration.Screenshot;
                if (scenario == "custom-home") home["pressAction"] = "custom-home-action";
                if (scenario == "custom-tools") tools["pressAction"] = "custom-tools-action";
                if (scenario == "different-owner") doc["nativePluginName"] = "OtherPlugin";
                if (scenario == "reordered-pages")
                {
                    var pages = doc["layout"]["layoutModes"][0]["workspaces"][0]["pressPages"].AsArray();
                    var first = pages[0]; pages.RemoveAt(0); pages.Add(first);
                }
                doc["custom-user-setting"] = "preserve";
                home["custom-icon"] = "preserve-home-icon";
                tools["custom-icon"] = "preserve-tools-icon";
                var original = doc.ToJsonString();
                var path = Path.Combine(temp, "Profiles", DesktopHomeScreenshotMigration.Profile, "ProfileInfo.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, original);
                Assert.Equal(expected, DesktopHomeScreenshotMigration.Upgrade(temp));
                if (expected)
                {
                    Assert.Equal(original, File.ReadAllText(path + ".before-0.17.14"));
                    home["pressAction"] = DesktopHomeScreenshotMigration.Screenshot;
                    tools["pressAction"] = DesktopHomeScreenshotMigration.Navigation;
                }
                else Assert.False(File.Exists(path + ".before-0.17.14"));
                Assert.True(JsonNode.DeepEquals(doc, JsonNode.Parse(File.ReadAllText(path))));
                var firstResult = File.ReadAllText(path);
                Assert.False(DesktopHomeScreenshotMigration.Upgrade(temp));
                Assert.Equal(firstResult, File.ReadAllText(path));
                if (expected) Assert.Equal(original, File.ReadAllText(path + ".before-0.17.14"));
            }
            finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        }

        [Fact]
        public void Normal_update_migrates_an_existing_revision_without_changing_profile_selection()
        {
            var temp = Path.Combine(Path.GetTempPath(), "vizhi-screenshot-update-" + Guid.NewGuid());
            try
            {
                SelfRegistration.CreateRegistration(Package(), null, temp, windows: false);
                var app = Path.Combine(temp, "Loupedeck70", "@_vizhidesktop");
                var file = Path.Combine(app, "Profiles", DesktopHomeScreenshotMigration.Profile, "ProfileInfo.json");
                var doc = JsonNode.Parse(File.ReadAllText(file));
                Key(doc, "Home")["pressAction"] = DesktopHomeScreenshotMigration.Navigation;
                Key(doc, "Tools")["pressAction"] = DesktopHomeScreenshotMigration.Screenshot;
                File.WriteAllText(file, doc.ToJsonString());
                File.WriteAllText(Path.Combine(app, ".vizhi-packaged-profile"), DesktopHomeScreenshotMigration.Profile);
                var appFile = Path.Combine(app, "ApplicationInfo.json");
                var info = JsonNode.Parse(File.ReadAllText(appFile));
                info["defaultProfileName"] = "custom-selected-profile";
                File.WriteAllText(appFile, info.ToJsonString());
                var original = File.ReadAllText(appFile);
                Assert.True(SelfRegistration.UpdateOwnedDefaultProfileIfNeeded(Package(), null, temp, windows: false));
                doc = JsonNode.Parse(File.ReadAllText(file));
                Assert.Equal(DesktopHomeScreenshotMigration.Screenshot, (String)Key(doc, "Home")["pressAction"]);
                Assert.Equal(DesktopTasksMenuMigration.Search, (String)Key(doc, "Tools")["pressAction"]);
                Assert.Equal(original, File.ReadAllText(appFile));
                Assert.False(SelfRegistration.UpdateOwnedDefaultProfileIfNeeded(Package(), null, temp, windows: false));
            }
            finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        }
    }
}
