namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;

    using Xunit;

    /// <summary>
    /// What a release package must not carry, pinned at the source of the build (#62, #64).
    ///
    /// Logitech QA's 2.2.0 retest read two things off the shipped package that the build had no
    /// test for: the PDB still named the author's worktree (the Release PathMap covered the product
    /// folder only, and Source Link keyed its map on the git root), and the macOS whisper bundle
    /// shipped its transcription smoke marker while the Windows one did not — read as "the smoke
    /// test was run for Mac only". A real package is the only place either can be verified in
    /// full (tools/voice/pack-release.sh does, on every pack); these keep the settings that make
    /// the pack pass from drifting between packs.
    /// </summary>
    public class ReleaseHygieneTests
    {
        [Fact]
        public void Release_builds_map_every_source_root_and_ship_no_source_link()
        {
            var props = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Directory.Build.props"));
            var release = Regex.Match(props, @"<PropertyGroup Condition=""'\$\(Configuration\)' == 'Release'"">(.*?)</PropertyGroup>", RegexOptions.Singleline).Groups[1].Value;

            Assert.Contains("<Deterministic>true</Deterministic>", release);
            // PathMap alone covers $(MSBuildProjectDirectory) — the product folder — and the
            // engine's sources are two levels up. DeterministicSourcePaths maps every root to /_/.
            Assert.Contains("<DeterministicSourcePaths>true</DeterministicSourcePaths>", release);
            // The repo is private; a Source Link map into it is a leak with no benefit.
            Assert.Contains("<EnableSourceLink>false</EnableSourceLink>", release);
        }

        [Fact]
        public void The_pack_checks_the_pdb_as_well_as_the_dll()
        {
            // A portable PDB stores path segments as separate blobs, so the whole-path pattern that
            // guards the DLL never matches one; the PDB check looks for the two segments that name
            // a machine, and for the Source Link host.
            var pack = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "voice", "pack-release.sh"));

            Assert.Contains("rglob('*.pdb')", pack);
            Assert.Contains("basename \"$HOME\"", pack);
            Assert.Contains("raw.githubusercontent.com", pack);
        }

        [Fact]
        public void The_pack_clears_release_intermediates_and_verifies_embedded_resources()
        {
            // A direct Release Compile target can leave a newer intermediate DLL containing no
            // resources. Unless obj/Release is cleared, the following normal build may reuse it and
            // ship no icons, bridge scripts, uninstall script, or PluginConfiguration.xml.
            var pack = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "voice", "pack-release.sh"));

            Assert.Contains("INTERMEDIATE_DIR=", pack);
            Assert.Contains("rm -rf \"$BUILD_DIR\" \"$INTERMEDIATE_DIR\"", pack);
            Assert.Contains("Loupedeck.ClaudeConsolePlugin.PluginConfiguration.xml", pack);
            Assert.Contains("Loupedeck.ClaudeConsolePlugin.Resources.icons.allow.png", pack);
            Assert.Contains("ClaudeConsole.statusline-handler.sh", pack);
            Assert.Contains("CodexConsole.codex-hook.sh", pack);
            Assert.Contains("grep -aFq \"$resource\" \"$PLUGIN_DLL\"", pack);
        }

        [Fact]
        public void The_pack_strips_the_smoke_marker_from_both_whisper_bundles()
        {
            var pack = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "voice", "pack-release.sh"));

            // Both markers in one rm, so they cannot drift apart again.
            Assert.Contains("rm -f \"$PKG_VOICE/whisper-bin/TRANSCRIPTION_SMOKE_OK\" \"$PKG_VOICE/whisper-bin-win/TRANSCRIPTION_SMOKE_OK\"", pack);
            // The proof still exists — in the pack's output, not in the package.
            Assert.Contains("smoke-tested: macOS bundle", pack);
            // And the pack still refuses a bundle that never transcribed, on either platform.
            Assert.Contains("$WBIN/TRANSCRIPTION_SMOKE_OK", pack.Substring(0, pack.IndexOf("building plugin (Release)", StringComparison.Ordinal)));
            Assert.Contains("$WIN_WBIN/TRANSCRIPTION_SMOKE_OK\" ] ||", pack);
        }

        [Fact]
        public void Mac_release_scripts_hard_fail_all_three_signature_gates_before_and_after_packaging()
        {
            var root = RepoRoot();
            var sign = File.ReadAllText(Path.Combine(root, "tools", "voice", "sign-and-notarize.sh"));
            var pack = File.ReadAllText(Path.Combine(root, "tools", "voice", "pack-release.sh"));

            Assert.Contains("codesign --verify --deep --strict", sign);
            Assert.Contains("spctl --assess --type execute", sign);
            Assert.Contains("xcrun stapler validate \"$APP\"", sign);
            Assert.DoesNotContain("spctl -a -vvv -t exec \"$APP\" 2>&1 || true", sign);
            Assert.DoesNotContain("xcrun stapler validate \"$APP\" 2>&1 || true", sign);

            Assert.Contains("verify_macos_helper \"$APP\"", pack);
            Assert.Contains("ditto -x -k \"$OUT\" \"$VERIFY_DIR\"", pack);
            Assert.Contains("verify_macos_helper \"$PACKED_APP\"", pack);
            Assert.Contains("codesign --verify --deep --strict", pack);
            Assert.Contains("spctl --assess --type execute", pack);
            Assert.Contains("xcrun stapler validate", pack);
        }

        [Fact]
        public void Vizhi_package_metadata_exposes_only_public_product_and_support_urls()
        {
            var metadata = File.ReadAllText(Path.Combine(
                RepoRoot(), "src", "Products", "VizhiCodex", "package", "metadata", "LoupedeckPackage.yaml"));

            Assert.Contains("supportPageUrl: https://github.com/rshankras/keypad-profiles/issues", metadata);
            Assert.Contains("homePageUrl: https://www.rshankar.com/keypad-profiles/", metadata);
            Assert.DoesNotContain("github.com/rshankras/claude-console", metadata);
        }

        private static String RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "src", "Products")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate the repo root");
        }
    }
}
