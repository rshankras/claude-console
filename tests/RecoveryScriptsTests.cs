namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;

    using Xunit;

    /// <summary>
    /// The uninstall remedy has to reach users who do not have the repo (#45).
    ///
    /// Uninstalling through Options+ deletes the package but leaves the application registration
    /// behind, still claiming Terminal. When the uninstalled product was the LAST of ours there is
    /// no plugin left to sweep it — and the only script that could was in a developer doc. The fix
    /// installs the recovery scripts to the runtime home, which outlives the package, by the same
    /// embed-and-extract route as the bridge scripts.
    ///
    /// Nothing checks that route end to end: the csproj names a resource, the C# names the same
    /// resource as a string, and neither compiler sees the other. These tests read both, the way
    /// VoiceRuntimeInstallTests pins the Swift/C# sidecar contract — so renaming a script in one
    /// place without the other fails here rather than as a silent "embedded resource not found"
    /// warning in a log nobody reads until the keypad is full of exclamation marks.
    /// </summary>
    public class RecoveryScriptsTests
    {
        private static readonly String[] Scripts = { "uninstall-registration.sh", "repair-registration.sh", "uninstall.sh" };

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

        private static String Read(String relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

        [Fact]
        public void EveryRecoveryScriptExistsInTheRepo()
        {
            foreach (var name in Scripts)
            {
                Assert.True(File.Exists(Path.Combine(RepoRoot(), "scripts", name)), $"scripts/{name} is missing");
            }
        }

        [Fact]
        public void TheProductEmbedsEachScriptUnderTheNameTheEngineExtracts()
        {
            var csproj = Read(Path.Combine("src", "Products", "ClaudeConsole", "ClaudeConsolePlugin.csproj"));
            var engine = Read(Path.Combine("src", "Core", "BridgeManager.cs"));

            foreach (var name in Scripts)
            {
                // The csproj must both include the file and give it the logical name...
                Assert.Contains($"scripts\\{name}", csproj);
                Assert.Contains($"<LogicalName>ClaudeConsole.{name}</LogicalName>", csproj);

                // ...and the engine must ask for exactly that logical name. It builds the name
                // from a prefix and the RecoveryScripts list, so pin both halves.
                Assert.Contains($"\"{name}\"", engine);
            }

            Assert.Contains("\"ClaudeConsole.\" + name", engine);
        }

        [Fact]
        public void TheRecoveryInstallRunsBeforeTheBridgeOptOut()
        {
            // THE important one. The bridge opt-out (~/.claude/claude-console/no-autowire) lets a
            // user decline settings.json wiring. It must not also cost them the uninstall remedy:
            // the whole point of #45 is that the remedy is otherwise unreachable. So the recovery
            // install has to happen BEFORE the opt-out check returns — pin the order in source.
            var engine = Read(Path.Combine("src", "Core", "BridgeManager.cs"));
            var body = engine.Substring(engine.IndexOf("public void EnsureBridgeAutoWired()", StringComparison.Ordinal));

            var install = body.IndexOf("EnsureRecoveryScriptsInstalled();", StringComparison.Ordinal);
            var optOut = body.IndexOf("File.Exists(BridgeOptOutFile)", StringComparison.Ordinal);

            Assert.True(install > 0, "EnsureBridgeAutoWired no longer installs the recovery scripts");
            Assert.True(optOut > 0, "the opt-out check moved — re-check this test's premise");
            Assert.True(install < optOut, "the recovery scripts are installed AFTER the opt-out, so opting out loses the uninstall remedy");
        }

        [Fact]
        public void UninstallSweepsTheOrphanBeforeDeletingTheHomeItLivesIn()
        {
            // The installed copy of uninstall-registration.sh lives INSIDE ~/.claude/claude-console.
            // uninstall.sh deletes that directory; if it did so first, the sweep it then calls would
            // be gone and the orphan would survive the very script meant to clear it.
            var script = Read(Path.Combine("scripts", "uninstall.sh"));

            var sweep = script.IndexOf("bash \"$SWEEP\" --remove", StringComparison.Ordinal);
            var delete = script.IndexOf("rm -rf \"$RUNTIME\"", StringComparison.Ordinal);

            Assert.True(sweep > 0, "uninstall.sh no longer runs the orphan sweep");
            Assert.True(delete > 0, "uninstall.sh no longer removes the runtime home");
            Assert.True(sweep < delete, "uninstall.sh deletes the runtime home before sweeping — the sweep script is inside it");
        }

        [Fact]
        public void TheReadmePointsUsersAtTheInstalledCopiesNotTheRepo()
        {
            // A Marketplace user has no `scripts/` directory. Every recovery instruction has to
            // name the path the plugin actually installs to.
            var readme = Read("README.md");

            Assert.Contains("~/.claude/claude-console/scripts/uninstall.sh", readme);
            Assert.Contains("~/.claude/claude-console/scripts/repair-registration.sh", readme);
            Assert.DoesNotContain("bash scripts/uninstall.sh", readme);
            Assert.DoesNotContain("bash scripts/repair-registration.sh", readme);
        }

        [Fact]
        public void RepairTakesTheRegistrationNameSoTheOtherProductsCanUseIt()
        {
            var script = Read(Path.Combine("scripts", "repair-registration.sh"));

            Assert.Matches(new Regex(@"NAME=""\$\{1:-claudeconsole\}"""), script);
            Assert.DoesNotContain("@_claudeconsole\"", script);   // no hard-coded product left
        }
    }
}
