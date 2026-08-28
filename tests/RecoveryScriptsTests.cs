namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    using Xunit;

    /// <summary>
    /// The cleanup script has to reach users who do not have the repo (#45).
    ///
    /// Uninstalling through Options+ deletes the package and nothing else — the voice runtime, the
    /// speech model, the hooks in settings.json all stay — and the only cleanup was a script in the
    /// repo. The fix installs it to the runtime home, which outlives the package, by the same
    /// embed-and-extract route as the bridge scripts. (It once carried two registration scripts as
    /// well; those left with the application registration itself when the plugin went universal, #23.)
    ///
    /// Nothing checks that route end to end: the csproj names a resource, the C# names the same
    /// resource as a string, and neither compiler sees the other. These tests read both, the way
    /// VoiceRuntimeInstallTests pins the Swift/C# sidecar contract — so renaming a script in one
    /// place without the other fails here rather than as a silent "embedded resource not found"
    /// warning in a log nobody reads until the keypad is full of exclamation marks.
    /// </summary>
    public class RecoveryScriptsTests
    {
        // Once three; the registration repair and the orphan sweep left with the application
        // registration itself (#23 — a universal plugin has no entry to orphan or repair).
        private static readonly String[] Scripts = { "uninstall.sh" };

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
        public void TheReadmePointsUsersAtTheInstalledCopiesNotTheRepo()
        {
            // A Marketplace user has no `scripts/` directory. Every recovery instruction has to
            // name the path the plugin actually installs to.
            var readme = Read("README.md");

            Assert.Contains("~/.claude/claude-console/scripts/uninstall.sh", readme);
            Assert.DoesNotContain("bash scripts/uninstall.sh", readme);
        }
    }
}
