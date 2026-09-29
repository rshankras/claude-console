namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Linq;
    using System.IO;
    using System.IO.Compression;
    using System.Text.Json.Nodes;

    using Loupedeck.ClaudeConsolePlugin.Platform;
    using Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration;

    using Xunit;

    /// <summary>
    /// Self-registration of an application entry (SelfRegistration) — today only Vizhi Desktop's.
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
            Assert.Equal("@_vizhidesktop", (String)appInfo["name"]);
            Assert.Equal("@_vizhidesktop", (String)profileInfo["applicationName"]);
            Assert.Equal("VizhiDesktop", (String)appInfo["nativePluginName"]);
            Assert.Equal("Loupedeck70", (String)appInfo["deviceType"]);
            Assert.True((Boolean)appInfo["isEnabled"]);
        }

        [Fact]
        public void A_clean_machine_gets_the_full_registration_layout()
        {
            var appsRoot = Path.Combine(this._root, "Applications");

            SelfRegistration.CreateRegistration(
                PackagedProfilePath(), iconPath: null, appsRoot, windows: false);

            var appDir = Path.Combine(appsRoot, "Loupedeck70", "@_vizhidesktop");
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
        public void The_payload_icon_becomes_the_application_icon()
        {
            var appsRoot = Path.Combine(this._root, "Applications");
            var icon = Path.Combine(this._root, "Icon256x256.png");
            Directory.CreateDirectory(this._root);
            File.WriteAllBytes(icon, new Byte[] { 0x89, 0x50, 0x4E, 0x47 });

            SelfRegistration.CreateRegistration(PackagedProfilePath(), icon, appsRoot, windows: false);

            Assert.True(File.Exists(Path.Combine(
                appsRoot, "Loupedeck70", "@_vizhidesktop", "ApplicationIcon.png")));
        }

        [Fact]
        public void An_existing_registration_on_any_device_type_blocks_creation()
        {
            var appsRoot = Path.Combine(this._root, "Applications");
            var existing = Path.Combine(appsRoot, "Loupedeck71", "@_claudeconsole");
            Directory.CreateDirectory(existing);
            File.WriteAllText(Path.Combine(existing, "ApplicationInfo.json"), "{}");

            Assert.True(SelfRegistration.RegistrationExists(appsRoot, "@_claudeconsole"));
        }

        /// <summary>
        /// Two products built from this repo must not claim one another's entry. The identity comes
        /// from each package's own ApplicationInfo.json, so a registration for one is invisible to
        /// the other — without this, installing the second console would overwrite the first's
        /// application row and its imported layout.
        /// </summary>
        [Fact]
        public void One_products_registration_is_not_mistaken_for_anothers()
        {
            var appsRoot = Path.Combine(this._root, "Applications");
            var claude = Path.Combine(appsRoot, "Loupedeck70", "@_claudeconsole");
            Directory.CreateDirectory(claude);
            File.WriteAllText(Path.Combine(claude, "ApplicationInfo.json"), "{}");

            Assert.True(SelfRegistration.RegistrationExists(appsRoot, "@_claudeconsole"));
            Assert.False(SelfRegistration.RegistrationExists(appsRoot, "@_codexconsole"));
        }


        /// <summary>
        /// A bound voice key with no payload in the package is exactly the failure this whole file
        /// guards against: the action registers, the key looks live, and the press finds no helper.
        /// A bound voice key is only honest because the packer embeds the payload for every product.
        /// </summary>
        [Fact]
        public void Pack_release_ships_voice_for_every_product()
        {
            var script = File.ReadAllText(RepoFile("tools", "voice", "pack-release.sh"));

            Assert.Contains("ClaudeConsole|VizhiCodex|VizhiDesktop) SHIPS_VOICE=1", script);
            Assert.Contains("ClaudeConsole|VizhiCodex) SHIPS_WINDOWS=1", script);
            Assert.Contains("TRANSCRIPTION_SMOKE_OK", script);
            Assert.Contains("WINDOWS_WHISPER_DIR", script);
            Assert.Contains("whisper-bin-win", script);
        }

        /// <summary>
        /// The voice actions must be compiled INTO the Codex product, not excluded from it.
        /// (The blanket no-Compile-Remove form of this test died when the desktop surface
        /// arrived: every product now carves out the OTHER surface's actions, which is correct —
        /// what must never be carved out of a terminal product is its own action set.)
        /// </summary>
        [Fact]
        public void The_codex_build_includes_the_voice_actions()
        {
            var csproj = File.ReadAllText(
                RepoFile("src", "Products", "VizhiCodex", "VizhiCodexPlugin.csproj"));

            Assert.DoesNotContain(@"Compile Remove=""..\..\Core\Actions", csproj);
        }

        /// <summary>
        /// The carve-outs, both directions: terminal products must not compile the desktop
        /// surface (the SDK auto-discovers every command in the assembly — they'd grow GUI keys
        /// for an app they don't drive), and the desktop product must not compile the terminal
        /// actions. One product, one surface, enforced by the compiler.
        /// </summary>
        [Theory]
        [InlineData("ClaudeConsole", "ClaudeConsolePlugin.csproj")]
        [InlineData("VizhiCodex", "VizhiCodexPlugin.csproj")]
        public void Terminal_products_carve_out_the_desktop_surface(String product, String csprojName)
        {
            var csproj = File.ReadAllText(RepoFile("src", "Products", product, csprojName));

            Assert.Contains(@"Compile Remove=""..\..\Core\Desktop\**\*.cs""", csproj);
            Assert.Contains(@"Compile Remove=""..\..\Core\DesktopActions\**\*.cs""", csproj);
        }

        [Fact]
        public void The_desktop_product_carves_out_the_terminal_actions()
        {
            var csproj = File.ReadAllText(
                RepoFile("src", "Products", "VizhiDesktop", "VizhiDesktopPlugin.csproj"));

            Assert.Contains(@"Compile Remove=""..\..\Core\Actions\**\*.cs""", csproj);
            Assert.DoesNotContain(@"Compile Remove=""..\..\Core\DesktopActions", csproj);
        }

        /// <summary>Walks up from the test binary to a repo-relative file.</summary>
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

        /// <summary>The name is read from the package, never assumed.</summary>
        [Fact]
        public void The_application_name_comes_from_the_packaged_profile()
        {
            Assert.Equal("@_vizhidesktop", SelfRegistration.ReadApplicationName(PackagedProfilePath()));
        }

        /// <summary>An unreadable package yields null rather than throwing during plugin load.</summary>
        [Fact]
        public void An_unreadable_package_has_no_application_name()
        {
            var junk = Path.Combine(this._root, "not-a-zip.lp5");
            Directory.CreateDirectory(this._root);
            File.WriteAllText(junk, "definitely not a zip");

            Assert.Null(SelfRegistration.ReadApplicationName(junk));
            Assert.Null(SelfRegistration.ReadApplicationName(Path.Combine(this._root, "missing.lp5")));
        }

        [Fact]
        public void No_applications_directory_means_no_registration_yet()
        {
            Assert.False(SelfRegistration.RegistrationExists(Path.Combine(this._root, "nope"), "@_claudeconsole"));
            Assert.False(SelfRegistration.RegistrationExists(null, "@_claudeconsole"));
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
            Assert.False(Directory.Exists(Path.Combine(appsRoot, "Loupedeck70", "@_vizhidesktop")));
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
                // Only Vizhi Desktop still self-registers: the terminal products are universal and ship no
                // profile (#23), so the registration mechanics are pinned against the Desktop package.
                var candidate = Path.Combine(
                    dir, "src", "Products", "VizhiDesktop", "package", "profiles", "DefaultProfile70.lp5");
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
