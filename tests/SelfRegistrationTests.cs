namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Text.Json.Nodes;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// Self-registration of the @_claudeconsole application entry (SelfRegistration).
    ///
    /// A sideloaded .lplug4 install never creates the registration — a clean machine gets no
    /// Options+ icon and no keypad layout (proven on a fresh macOS account and the Windows
    /// laptop, 2026-08-08). The plugin therefore writes the registration itself from the
    /// packaged profile. These tests pin the two things that must stay true for that to work:
    /// the package keeps carrying a complete registration document, and the creation logic
    /// reproduces the exact on-disk layout the service is known to adopt.
    /// </summary>
    public class SelfRegistrationTests : IDisposable
    {
        private readonly String _root = Path.Combine(
            Path.GetTempPath(), "cc-selfreg-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(this._root, recursive: true); } catch { }
        }

        [Fact]
        public void The_packaged_profile_carries_a_complete_registration_document()
        {
            using var zip = ZipFile.OpenRead(PackagedProfilePath());

            var appInfo = ReadJsonEntry(zip, "ApplicationInfo.json");
            var profileInfo = ReadJsonEntry(zip, "ProfileInfo.json");

            // Self-registration extracts the profile into Profiles/<defaultProfileName>/, so the
            // application document and the profile it wraps must agree on the name.
            Assert.Equal((String)profileInfo["name"], (String)appInfo["defaultProfileName"]);
            Assert.Equal("@_claudeconsole", (String)appInfo["name"]);
            Assert.Equal("@_claudeconsole", (String)profileInfo["applicationName"]);
            Assert.Equal("ClaudeConsole", (String)appInfo["nativePluginName"]);
            Assert.Equal("Loupedeck70", (String)appInfo["deviceType"]);
            Assert.True((Boolean)appInfo["isEnabled"]);
        }

        [Fact]
        public void A_clean_machine_gets_the_full_registration_layout()
        {
            var appsRoot = Path.Combine(this._root, "Applications");

            SelfRegistration.CreateRegistration(
                PackagedProfilePath(), iconPath: null, appsRoot, windows: false);

            var appDir = Path.Combine(appsRoot, "Loupedeck70", "@_claudeconsole");
            var appInfo = JsonNode.Parse(File.ReadAllText(Path.Combine(appDir, "ApplicationInfo.json")));
            var profileDir = Path.Combine(appDir, "Profiles", (String)appInfo["defaultProfileName"]);

            Assert.True(File.Exists(Path.Combine(profileDir, "ProfileInfo.json")));
            Assert.True(File.Exists(Path.Combine(profileDir, "metadata", "ProfilePreview.json")));
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(profileDir, "ActionIcons")));

            // The application document belongs at the top only — a copy inside the profile dir
            // is not part of the working layout the service adopts.
            Assert.False(File.Exists(Path.Combine(profileDir, "ApplicationInfo.json")));
        }

        [Fact]
        public void MacOS_binds_terminal_and_windows_binds_windows_terminal()
        {
            var macRoot = Path.Combine(this._root, "mac");
            var winRoot = Path.Combine(this._root, "win");

            SelfRegistration.CreateRegistration(PackagedProfilePath(), null, macRoot, windows: false);
            SelfRegistration.CreateRegistration(PackagedProfilePath(), null, winRoot, windows: true);

            Assert.Equal("com.apple.Terminal", ReadProcessName(macRoot));
            Assert.Equal("WindowsTerminal", ReadProcessName(winRoot));
        }

        [Fact]
        public void The_payload_icon_becomes_the_application_icon()
        {
            var appsRoot = Path.Combine(this._root, "Applications");
            var icon = Path.Combine(this._root, "Icon256x256.png");
            Directory.CreateDirectory(this._root);
            File.WriteAllBytes(icon, new Byte[] { 0x89, 0x50, 0x4E, 0x47 });

            SelfRegistration.CreateRegistration(PackagedProfilePath(), icon, appsRoot, windows: false);

            Assert.True(File.Exists(Path.Combine(
                appsRoot, "Loupedeck70", "@_claudeconsole", "ApplicationIcon.png")));
        }

        [Fact]
        public void An_existing_registration_on_any_device_type_blocks_creation()
        {
            var appsRoot = Path.Combine(this._root, "Applications");
            var existing = Path.Combine(appsRoot, "Loupedeck71", "@_claudeconsole");
            Directory.CreateDirectory(existing);
            File.WriteAllText(Path.Combine(existing, "ApplicationInfo.json"), "{}");

            Assert.True(SelfRegistration.RegistrationExists(appsRoot));
        }

        [Fact]
        public void No_applications_directory_means_no_registration_yet()
        {
            Assert.False(SelfRegistration.RegistrationExists(Path.Combine(this._root, "nope")));
            Assert.False(SelfRegistration.RegistrationExists(null));
        }

        [Fact]
        public void A_package_without_the_registration_document_leaves_no_half_entry()
        {
            var appsRoot = Path.Combine(this._root, "Applications");
            var badLp5 = Path.Combine(this._root, "bad.lp5");
            Directory.CreateDirectory(this._root);
            using (var zip = ZipFile.Open(badLp5, ZipArchiveMode.Create))
            {
                zip.CreateEntry("ProfileInfo.json");
            }

            Assert.ThrowsAny<Exception>(() =>
                SelfRegistration.CreateRegistration(badLp5, null, appsRoot, windows: false));
            Assert.False(Directory.Exists(Path.Combine(appsRoot, "Loupedeck70", "@_claudeconsole")));
        }

        private static String ReadProcessName(String appsRoot)
        {
            var json = File.ReadAllText(Path.Combine(
                appsRoot, "Loupedeck70", "@_claudeconsole", "ApplicationInfo.json"));
            return (String)JsonNode.Parse(json)["processOrBundleName"];
        }

        private static JsonNode ReadJsonEntry(ZipArchive zip, String name)
        {
            var entry = zip.GetEntry(name);
            Assert.True(entry != null, $"packaged profile is missing {name}");
            using var stream = entry.Open();
            return JsonNode.Parse(stream);
        }

        private static String PackagedProfilePath()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(
                    dir, "src", "package", "profiles", "DefaultProfile70.lp5");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate the packaged profile");
        }
    }
}
