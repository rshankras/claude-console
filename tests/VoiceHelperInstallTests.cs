namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    using Xunit;

    /// <summary>
    /// The packaged voice helper is installed by REPLACING the runtime bundle, never by writing
    /// into it (#59).
    ///
    /// Logitech QA's 2.2.0 retest: the package shipped a 30 August helper, a 26 June copy sat in the
    /// runtime home, and three Dictate presses each logged "installing voice helper from package"
    /// while not a byte changed. Reproduced 2026-09-03 with the install path finally reporting
    /// errors: every ditto and xattr call inside the existing bundle returned "Operation not
    /// permitted", and macOS announced "LogiPluginService was prevented from modifying apps on your
    /// Mac". A launched, permission-granted app bundle is protected from other apps; renaming it and
    /// creating a new one are not. These tests drive the replacement against ordinary directories —
    /// the mechanism is the same, only the OS refusal is absent here.
    /// </summary>
    public class VoiceHelperInstallTests : IDisposable
    {
        private readonly String _root = Path.Combine(Path.GetTempPath(), "cc-helper-" + Guid.NewGuid().ToString("N"));
        private readonly String _package;
        private readonly String _runtime;

        public VoiceHelperInstallTests()
        {
            _package = Path.Combine(_root, "pkg", "Helper.app");
            _runtime = Path.Combine(_root, "home", "Helper.app");
            WriteBundle(_package, "new build");
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        private static void WriteBundle(String bundle, String binaryText)
        {
            Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
            File.WriteAllText(Path.Combine(bundle, "Contents", "Info.plist"), "<plist/>");
            File.WriteAllText(Path.Combine(bundle, "Contents", "MacOS", "Helper"), binaryText);
        }

        private static Boolean OnMac => OperatingSystem.IsMacOS() && File.Exists("/usr/bin/ditto");

        [Fact]
        public void A_first_install_creates_the_bundle()
        {
            if (!OnMac) { return; }   // ditto is the copier; the mechanism is macOS-only by nature

            Assert.True(BridgeManager.InstallBundleByReplacement(_package, _runtime));

            Assert.True(BridgeManager.RuntimeTreeMatchesPackage(_package, _runtime));
            Assert.False(Directory.Exists(_runtime + ".previous"));
        }

        [Fact]
        public void An_older_copy_is_replaced_not_written_into()
        {
            if (!OnMac) { return; }

            // The QA case: an older build already on disk. Note its inode-level identity by a file
            // the package does not carry — after a replacement it must be gone with the old bundle,
            // where an in-place ditto (a merge) would have left it behind.
            WriteBundle(_runtime, "26 June build");
            File.WriteAllText(Path.Combine(_runtime, "Contents", "stray.txt"), "only in the old copy");

            Assert.True(BridgeManager.InstallBundleByReplacement(_package, _runtime));

            Assert.True(BridgeManager.RuntimeTreeMatchesPackage(_package, _runtime));
            Assert.Equal("new build", File.ReadAllText(Path.Combine(_runtime, "Contents", "MacOS", "Helper")));
            Assert.False(File.Exists(Path.Combine(_runtime, "Contents", "stray.txt")));
            Assert.False(Directory.Exists(_runtime + ".previous"));   // deletable here, so it is gone
        }

        [Fact]
        public void A_leftover_from_an_earlier_attempt_does_not_block_the_next()
        {
            if (!OnMac) { return; }

            // On a real Mac the old bundle may refuse deletion and stay under ".previous". The next
            // install must still succeed rather than fail on Directory.Move into an existing name.
            WriteBundle(_runtime, "26 June build");
            WriteBundle(_runtime + ".previous", "an even older build that would not delete");

            Assert.True(BridgeManager.InstallBundleByReplacement(_package, _runtime));

            Assert.True(BridgeManager.RuntimeTreeMatchesPackage(_package, _runtime));
        }

        [Fact]
        public void A_failed_copy_puts_the_old_bundle_back()
        {
            if (!OnMac) { return; }

            // A package path that does not exist stands in for a copy that fails: voice must keep
            // working on the bundle that was there, not be left with nothing.
            WriteBundle(_runtime, "26 June build");

            Assert.False(BridgeManager.InstallBundleByReplacement(Path.Combine(_root, "missing.app"), _runtime));

            Assert.True(Directory.Exists(_runtime));
            Assert.Equal("26 June build", File.ReadAllText(Path.Combine(_runtime, "Contents", "MacOS", "Helper")));
        }

        [Fact]
        public void A_quarantine_failure_is_not_reported_as_a_success()
        {
            // Quarantine is metadata and therefore invisible to RuntimeTreeMatchesPackage. Simulate
            // a successful copy followed by xattr failure: the new bundle must not be accepted as
            // installed, and the previous working copy must come back.
            WriteBundle(_runtime, "26 June build");
            var sawXattr = false;

            Int32 Run(String file, String[] args)
            {
                if (file == "/usr/bin/ditto")
                {
                    CopyBundle(args[0], args[1]);
                    return 0;
                }

                sawXattr = true;
                return 1;
            }

            Assert.False(BridgeManager.InstallBundleByReplacement(_package, _runtime, Run));

            Assert.True(sawXattr);
            Assert.Equal("26 June build", File.ReadAllText(Path.Combine(_runtime, "Contents", "MacOS", "Helper")));
            Assert.False(Directory.Exists(_runtime + ".previous"));
        }

        [Fact]
        public void The_install_path_never_dittos_into_an_existing_helper_again()
        {
            // Pinned at the source: the in-place ditto is the bug, and it is one tempting line.
            var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Core", "BridgeManager.cs"));
            var install = source.Substring(source.IndexOf("private void EnsureVoiceRuntimeInstalled()", StringComparison.Ordinal));
            install = install.Substring(0, install.IndexOf("private void EnsureVoiceRuntimeInstalledWindows()", StringComparison.Ordinal));

            Assert.Contains("InstallBundleByReplacement(pkgHelper, VoiceHelperApp)", install);
            Assert.DoesNotContain("RunSync(\"/usr/bin/ditto\", pkgHelper, VoiceHelperApp)", install);
        }

        private static String RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "src", "Core")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate the repo root");
        }

        private static void CopyBundle(String source, String destination)
        {
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
            }
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, overwrite: true);
            }
        }
    }
}
