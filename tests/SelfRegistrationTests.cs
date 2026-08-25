namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Linq;
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
        /// The Codex package must be a COMPLETE registration in its own right, and must not collide
        /// with Claude Console's. Identity, profile GUID and the plugin the keys bind to all differ;
        /// a shared GUID in particular would have the service dedupe one profile away.
        /// </summary>
        [Fact]
        public void The_codex_package_registers_as_its_own_application()
        {
            var lp5 = CodexProfilePath();
            var claude = PackagedProfilePath();

            using var zip = System.IO.Compression.ZipFile.OpenRead(lp5);
            var appInfo = ReadJsonEntry(zip, "ApplicationInfo.json");
            var profileInfo = ReadJsonEntry(zip, "ProfileInfo.json");

            Assert.Equal("@_codexconsole", (String)appInfo["name"]);
            Assert.Equal("@_codexconsole", (String)profileInfo["applicationName"]);
            Assert.Equal((String)profileInfo["name"], (String)appInfo["defaultProfileName"]);

            Assert.NotEqual(SelfRegistration.ReadApplicationName(claude),
                            SelfRegistration.ReadApplicationName(lp5));

            using var claudeZip = System.IO.Compression.ZipFile.OpenRead(claude);
            Assert.NotEqual((String)ReadJsonEntry(claudeZip, "ProfileInfo.json")["name"],
                            (String)profileInfo["name"]);
        }

        /// <summary>
        /// Key bindings name the plugin that owns them ("<PluginShortName>___<Type>___<param>"), so
        /// a profile copied from another product binds every key to a plugin this package does not
        /// contain — the layout would import and do nothing at all.
        /// </summary>
        [Fact]
        public void Every_key_in_the_codex_profile_binds_to_the_codex_plugin()
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(CodexProfilePath());
            using var entry = zip.GetEntry("ProfileInfo.json").Open();
            using var reader = new System.IO.StreamReader(entry);
            var body = reader.ReadToEnd();

            Assert.DoesNotContain("ClaudeConsole___", body);
            Assert.Contains("VizhiCodex___", body);
        }

        /// <summary>
        /// The profile and the product must agree. Gating an action in code while the profile still
        /// binds it does not remove the key — it turns it into an unresolvable binding, a key that
        /// looks live and cannot fire. These two are what Codex genuinely does not have.
        /// </summary>
        [Fact]
        public void The_codex_profile_binds_nothing_the_product_cannot_do()
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(CodexProfilePath());
            using var entry = zip.GetEntry("ProfileInfo.json").Open();
            using var reader = new System.IO.StreamReader(entry);
            var body = reader.ReadToEnd();

            Assert.DoesNotContain("ControlCommand___tab", body);      // no completion to accept
            Assert.DoesNotContain("CostDisplayCommand", body);        // reports no spend
        }

        /// <summary>
        /// Voice is agent-neutral and this package embeds the payload, so the Codex profile binds it
        /// like Claude Console does. The keys are only honest while pack-release.sh ships the helper
        /// for this product — the guard for that lives in Pack_release_ships_voice_for_both_products.
        /// </summary>
        [Fact]
        public void The_codex_profile_binds_voice()
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(CodexProfilePath());
            using var entry = zip.GetEntry("ProfileInfo.json").Open();
            using var reader = new System.IO.StreamReader(entry);
            var body = reader.ReadToEnd();

            Assert.Contains("VoiceCommand", body);
            Assert.Contains("ProjectVoiceCommand", body);
        }

        /// <summary>
        /// Page 1 is the page a user actually looks at, so it carries no holes. Dropping Tab left
        /// one, and the fix is a rearrangement rather than filler: Esc moves down beside Yes and No
        /// — yes, no and escape are the three ways to answer an approval prompt — which frees the
        /// slot next to Voice for its Draft twin (same capture, types without submitting).
        /// </summary>
        [Fact]
        public void The_codex_first_page_has_no_empty_keys()
        {
            var page = CodexPressPage(0);

            for (var i = 0; i < 9; i++)
            {
                Assert.False(
                    page[i]!["pressAction"] is null,
                    $"page 1 key {i + 1} is unbound — the first page must be full");
            }

            Assert.Contains("ScreenshotCommand", (String)page[3]!["pressAction"]!);
            Assert.Contains("VoiceCommand", (String)page[4]!["pressAction"]!);
            Assert.Contains("VoiceDraftCommand", (String)page[5]!["pressAction"]!);
            Assert.Contains("ControlCommand___esc", (String)page[8]!["pressAction"]!);
        }

        /// <summary>
        /// Cost's freed slot on page 2 carries Review — Codex's own first-class verb — and Clear
        /// is demoted to the far corner, not dropped. A rearrangement that silently lost a key
        /// would be the profile-vs-product bug wearing a new coat.
        /// </summary>
        [Fact]
        public void Page_two_leads_with_review_and_keeps_clear_in_the_corner()
        {
            var page = CodexPressPage(1);

            Assert.Contains("ControlCommand___review", (String)page[0]!["pressAction"]!);
            Assert.Contains("ControlCommand___clear", (String)page[8]!["pressAction"]!);
        }

        /// <summary>Reads one press page's controls out of the packaged Codex profile.</summary>
        private static JsonArray CodexPressPage(Int32 index)
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(CodexProfilePath());
            using var entry = zip.GetEntry("ProfileInfo.json").Open();
            using var reader = new System.IO.StreamReader(entry);
            var doc = JsonNode.Parse(reader.ReadToEnd());

            return (JsonArray)doc!["layout"]!["layoutModes"]![0]!["workspaces"]![0]!
                ["pressPages"]![index]!["controls"]!;
        }

        /// <summary>Claude Console keeps all four — this is a per-product difference, not a removal.</summary>
        [Fact]
        public void The_claude_profile_still_binds_them()
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(PackagedProfilePath());
            using var entry = zip.GetEntry("ProfileInfo.json").Open();
            using var reader = new System.IO.StreamReader(entry);
            var body = reader.ReadToEnd();

            Assert.Contains("ControlCommand___tab", body);
            Assert.Contains("CostDisplayCommand", body);
            Assert.Contains("VoiceCommand", body);
        }

        /// <summary>
        /// A bound voice key with no payload in the package is exactly the failure this whole file
        /// guards against: the action registers, the key looks live, and the press finds no helper.
        /// The profile above is only honest because the packer embeds the payload for both products.
        /// </summary>
        [Fact]
        public void Pack_release_ships_voice_for_every_product()
        {
            var script = File.ReadAllText(RepoFile("tools", "voice", "pack-release.sh"));

            Assert.Contains("ClaudeConsole|VizhiCodex|VizhiDesktop) SHIPS_VOICE=1", script);
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

        private static String CodexProfilePath()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(
                    dir, "src", "Products", "VizhiCodex", "package", "profiles", "DefaultProfile70.lp5");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate the Codex profile");
        }

        /// <summary>The name is read from the package, never assumed.</summary>
        [Fact]
        public void The_application_name_comes_from_the_packaged_profile()
        {
            Assert.Equal("@_claudeconsole", SelfRegistration.ReadApplicationName(PackagedProfilePath()));
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
                // Each product owns its own package tree; this suite is about Claude Console's.
                var candidate = Path.Combine(
                    dir, "src", "Products", "ClaudeConsole", "package", "profiles", "DefaultProfile70.lp5");
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
