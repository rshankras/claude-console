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
