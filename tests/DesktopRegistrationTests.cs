namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Nodes;

    using Loupedeck.ClaudeConsolePlugin.Platform;
    using Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration;

    using Xunit;

    /// <summary>
    /// The registration path for a BUNDLE-BOUND product. CreateRegistration has always patched
    /// the packaged document to WindowsTerminal on Windows — right for the terminal pair, and a
    /// silent rebind for a product that drives a desktop app. The guard keys on the packaged
    /// value, and this class pins both sides of it against the REAL packaged profile, so a
    /// regression in either the guard or the generator fails here first.
    /// </summary>
    public sealed class DesktopRegistrationTests : IDisposable
    {
        private readonly String _root = Path.Combine(
            Path.GetTempPath(), $"vd-reg-{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(this._root, recursive: true); } catch { }
        }

        private static String DesktopLp5() =>
            RepoFile("src", "Products", "VizhiDesktop", "package", "profiles", "DefaultProfile70.lp5");

        private static JsonNode Registered(String root)
        {
            var appInfo = Directory.GetFiles(root, "ApplicationInfo.json", SearchOption.AllDirectories).Single();
            return JsonNode.Parse(File.ReadAllText(appInfo));
        }

        [Fact]
        public void A_desktop_bundle_survives_the_windows_patch_untouched()
        {
            var winRoot = Path.Combine(this._root, "win");

            SelfRegistration.CreateRegistration(DesktopLp5(), null, winRoot, windows: true);

            // The whole point: NOT rewritten to WindowsTerminal.
            Assert.Equal("com.openai.codex", (String)Registered(winRoot)["processOrBundleName"]);
        }

        [Fact]
        public void Confirmed_windows_process_replaces_the_packaged_mac_bundle_in_registration()
        {
            var winRoot = Path.Combine(this._root, "win-confirmed");

            SelfRegistration.CreateRegistration(
                DesktopLp5(), null, winRoot, windows: true, windowsProcessName: "ChatGPT-confirmed");

            Assert.Equal("ChatGPT-confirmed", (String)Registered(winRoot)["processOrBundleName"]);
        }

        [Fact]
        public void The_packaged_identity_is_the_desktop_products_own()
        {
            var macRoot = Path.Combine(this._root, "mac");

            SelfRegistration.CreateRegistration(DesktopLp5(), null, macRoot, windows: false);
            var appInfo = Registered(macRoot);

            Assert.Equal("@_vizhidesktop", (String)appInfo["name"]);
            Assert.Equal("VizhiDesktop", (String)appInfo["nativePluginName"]);
            Assert.Equal("com.openai.codex", (String)appInfo["processOrBundleName"]);
            // Ownership stamped, so RegistrationCleanup can tell ours from another vendor's.
            Assert.Equal("VizhiDesktop", (String)appInfo[RegistrationCleanup.OwnerKey]);
        }

        [Fact]
        public void The_profile_guid_agrees_across_all_four_locations()
        {
            // ProfileInfo name + packageName, ApplicationInfo defaultProfileName, metadata yaml
            // name. The fourth is where VizhiCodex's package still carries its donor's GUID —
            // this product ships all four correct, and this test keeps it that way.
            using var zip = System.IO.Compression.ZipFile.OpenRead(DesktopLp5());

            String ReadEntry(String name)
            {
                using var reader = new StreamReader(zip.GetEntry(name).Open());
                return reader.ReadToEnd();
            }

            var profile = JsonNode.Parse(ReadEntry("ProfileInfo.json"));
            var appInfo = JsonNode.Parse(ReadEntry("ApplicationInfo.json"));
            var yamlName = ReadEntry("metadata/LoupedeckPackage.yaml")
                .Split('\n').First(l => l.StartsWith("name:")).Substring(5).Trim();

            var guid = (String)profile["name"];
            Assert.False(String.IsNullOrWhiteSpace(guid));
            Assert.Equal(guid, (String)profile["packageName"]);
            Assert.Equal(guid, (String)appInfo["defaultProfileName"]);
            Assert.Equal(guid, yamlName);
        }

        [Fact]
        public void Every_bound_key_names_this_plugin()
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(DesktopLp5());
            using var reader = new StreamReader(zip.GetEntry("ProfileInfo.json").Open());
            var profile = JsonNode.Parse(reader.ReadToEnd());

            var pages = profile["layout"]["layoutModes"][0]["workspaces"][0]["pressPages"].AsArray();

            var bound = pages
                .SelectMany(p => p["controls"].AsArray())
                .Select(c => (String)c["pressAction"])
                .Where(a => a != null)
                .ToList();

            Assert.NotEmpty(bound);
            Assert.All(bound, a => Assert.StartsWith("$VizhiDesktop___", a));
        }

        [Fact]
        public void Desktop_package_is_mac_only_until_a_windows_automation_backend_exists()
        {
            var yaml = File.ReadAllText(RepoFile(
                "src", "Products", "VizhiDesktop", "package", "metadata", "LoupedeckPackage.yaml"));

            Assert.Contains("pluginFolderMac: bin", yaml);
            Assert.DoesNotContain("pluginFolderWin:", yaml);
        }

        [Fact]
        public void Adaptive_profile_upgrade_preserves_the_previous_profile()
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(DesktopLp5());
            using var reader = new StreamReader(zip.GetEntry("ApplicationInfo.json").Open());
            var packaged = JsonNode.Parse(reader.ReadToEnd());
            var next = (String)packaged["defaultProfileName"];
            const String previous = "EF7972524F2B4BEABD3B7D8BD57DB350";

            var appDir = Path.Combine(this._root, "upgrade", "Loupedeck70", "@_vizhidesktop");
            var previousDir = Path.Combine(appDir, "Profiles", previous);
            Directory.CreateDirectory(previousDir);
            File.WriteAllText(Path.Combine(previousDir, "user-customization.ict"), "keep me");
            File.WriteAllText(Path.Combine(appDir, "ApplicationInfo.json"),
                $"{{\"name\":\"@_vizhidesktop\",\"deviceType\":\"Loupedeck70\"," +
                $"\"nativePluginName\":\"VizhiDesktop\",\"selfRegisteredBy\":\"VizhiDesktop\"," +
                $"\"defaultProfileName\":\"{previous}\",\"isEnabled\":true}}");

            Assert.True(SelfRegistration.UpdateOwnedDefaultProfileIfNeeded(
                DesktopLp5(), null, Path.Combine(this._root, "upgrade"), windows: false));

            var installed = JsonNode.Parse(File.ReadAllText(Path.Combine(appDir, "ApplicationInfo.json")));
            Assert.Equal(previous, (String)installed["defaultProfileName"]);
            Assert.Equal(next, File.ReadAllText(Path.Combine(appDir, ".vizhi-packaged-profile")));
            Assert.True(File.Exists(Path.Combine(appDir, "Profiles", next, "ProfileInfo.json")));
            Assert.True(File.Exists(Path.Combine(previousDir, "user-customization.ict")));
            Assert.False(SelfRegistration.UpdateOwnedDefaultProfileIfNeeded(
                DesktopLp5(), null, Path.Combine(this._root, "upgrade"), windows: false));
        }

        [Fact]
        public void Adaptive_profile_upgrade_never_touches_an_unowned_registration()
        {
            var appDir = Path.Combine(this._root, "unowned", "Loupedeck70", "@_vizhidesktop");
            Directory.CreateDirectory(appDir);
            File.WriteAllText(Path.Combine(appDir, "ApplicationInfo.json"),
                "{\"name\":\"@_vizhidesktop\",\"deviceType\":\"Loupedeck70\"," +
                "\"nativePluginName\":\"SomeoneElse\",\"defaultProfileName\":\"someone-elses\"}");

            Assert.False(SelfRegistration.UpdateOwnedDefaultProfileIfNeeded(
                DesktopLp5(), null, Path.Combine(this._root, "unowned"), windows: false));
            Assert.Equal("someone-elses", (String)JsonNode.Parse(
                File.ReadAllText(Path.Combine(appDir, "ApplicationInfo.json")))["defaultProfileName"]);
        }

        [Fact]
        public void Adaptive_profile_upgrade_accepts_native_ownership_when_options_removed_the_stamp()
        {
            var root = Path.Combine(this._root, "native-owner");
            var appDir = Path.Combine(root, "Loupedeck70", "@_vizhidesktop");
            Directory.CreateDirectory(appDir);
            File.WriteAllText(Path.Combine(appDir, "ApplicationInfo.json"),
                "{\"name\":\"@_vizhidesktop\",\"deviceType\":\"Loupedeck70\"," +
                "\"nativePluginName\":\"VizhiDesktop\",\"defaultProfileName\":\"OLD\"}");

            Assert.True(SelfRegistration.UpdateOwnedDefaultProfileIfNeeded(
                DesktopLp5(), null, root, windows: false));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Changing_default_never_reinstalls_or_requests_restart(Boolean legacy)
        {
            var root = Path.Combine(_root, "choice");
            SelfRegistration.CreateRegistration(DesktopLp5(), null, root, windows: false);
            var appDir = Path.Combine(root, "Loupedeck70", "@_vizhidesktop");
            var infoPath = Path.Combine(appDir, "ApplicationInfo.json");
            var installed = JsonNode.Parse(File.ReadAllText(infoPath));
            installed["defaultProfileName"] = "MY-CUSTOM-PROFILE";
            File.WriteAllText(infoPath, installed.ToJsonString());
            var before = File.ReadAllText(infoPath);
            if (legacy) File.Delete(Path.Combine(appDir, ".vizhi-packaged-profile"));

            Assert.False(SelfRegistration.UpdateOwnedDefaultProfileIfNeeded(DesktopLp5(), null, root, false));
            Assert.Equal(before, File.ReadAllText(infoPath));
            Assert.True(File.Exists(Path.Combine(appDir, ".vizhi-packaged-profile")));
            Assert.False(SelfRegistration.UpdateOwnedDefaultProfileIfNeeded(DesktopLp5(), null, root, false));
        }

        [Fact]
        public void New_revision_is_installed_alongside_custom_default_only_once()
        {
            var root = Path.Combine(_root, "new-revision");
            var appDir = Path.Combine(root, "Loupedeck70", "@_vizhidesktop");
            Directory.CreateDirectory(appDir);
            File.WriteAllText(Path.Combine(appDir, ".vizhi-packaged-profile"), "OLD-REVISION");
            File.WriteAllText(Path.Combine(appDir, "ApplicationInfo.json"),
                "{\"nativePluginName\":\"VizhiDesktop\",\"defaultProfileName\":\"MY-CUSTOM-PROFILE\"}");
            Assert.True(SelfRegistration.UpdateOwnedDefaultProfileIfNeeded(DesktopLp5(), null, root, false));
            Assert.Equal("MY-CUSTOM-PROFILE", (String)JsonNode.Parse(
                File.ReadAllText(Path.Combine(appDir, "ApplicationInfo.json")))["defaultProfileName"]);
            Assert.False(SelfRegistration.UpdateOwnedDefaultProfileIfNeeded(DesktopLp5(), null, root, false));
        }

        [Fact]
        public void Page_one_is_the_appendix_home_page()
        {
            // The home page: conversations above, answers below — the appendix's shape, with
            // the owner's post-hardware revision (2026-08-25) of six conversation keys down to
            // three plus a glance-and-act middle row. Deviates from the appendix DRAWING; the
            // shape and the bottom row are unchanged. (Default-profile placement for
            // answering-from-anywhere is separate and unchanged.)
            using var zip = System.IO.Compression.ZipFile.OpenRead(DesktopLp5());
            using var reader = new StreamReader(zip.GetEntry("ProfileInfo.json").Open());
            var profile = JsonNode.Parse(reader.ReadToEnd());

            var pages = profile["layout"]["layoutModes"][0]["workspaces"][0]["pressPages"].AsArray();
            Assert.Equal(3, pages.Count);   // Conversations · Actions · Workflows

            var pageOne = pages[0]["controls"].AsArray()
                .Select(c => (String)c["pressAction"]).ToList();

            // Top row: three conversation slots (the owner's post-hardware revision of the
            // appendix's six — three scannable identities beat six near-identical ones).
            for (var slot = 1; slot <= 3; slot++)
            {
                Assert.EndsWith($"DesktopConversationCommand___{slot}", pageOne[slot - 1]);
            }

            // Middle row: browse + create + inspect. Session state already lives in each card,
            // so a second global Activity key would duplicate the grid.
            Assert.Equal("$VizhiDesktop___#DynamicFolder___DynamicFolder#Loupedeck.ClaudeConsolePlugin.DesktopActions.AllChatsDynamicFolder", pageOne[3]);
            Assert.EndsWith("DesktopControlCommand___new_chat", pageOne[4]);
            Assert.EndsWith("DesktopContextCommand___primary", pageOne[5]);

            // Bottom row: Approve / Deny / Voice.
            Assert.EndsWith("DesktopApprovalCommand___approve", pageOne[6]);
            Assert.EndsWith("DesktopApprovalCommand___deny", pageOne[7]);
            Assert.EndsWith("DesktopVoiceCommand", pageOne[8]);

            // Actions: stable controls first, then four mode-aware positions.
            var pageTwo = pages[1]["controls"].AsArray()
                .Select(c => (String)c["pressAction"]).ToList();
            Assert.EndsWith("DesktopControlCommand___mode", pageTwo[0]);
            Assert.EndsWith("DesktopControlCommand___stop", pageTwo[1]);
            Assert.EndsWith("DesktopVoiceDraftCommand", pageTwo[2]);
            for (var slot = 1; slot <= 4; slot++)
            {
                Assert.EndsWith($"DesktopContextCommand___secondary_{slot}", pageTwo[slot + 2]);
            }

            // Workflows keep physical positions and resolve by focused mode at runtime.
            var pageThree = pages[2]["controls"].AsArray()
                .Select(c => (String)c["pressAction"]).ToList();
            for (var slot = 1; slot <= 9; slot++)
            {
                Assert.EndsWith($"DesktopWorkflowCommand___slot_{slot}", pageThree[slot - 1]);
            }
        }

        private static String RepoFile(params String[] parts)
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new FileNotFoundException($"not found walking up from {AppContext.BaseDirectory}: {String.Join("/", parts)}");
        }
    }
}
