namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;

    using Xunit;

    /// <summary>
    /// The guard that decides whether the packaged voice runtime has to be re-installed (#24).
    ///
    /// 2.0.1 shipped a whisper bundle with no ggml compute backends, and the install guard was
    /// `Directory.Exists(runtimeRoot)`. Because the runtime home lives outside the plugin and
    /// survives an uninstall, that meant the broken bundle counted as installed forever: shipping
    /// corrected files would have repaired nobody who had ever pressed Voice. These tests pin the
    /// property that actually matters — a runtime tree is accepted only when it matches the
    /// package file for file.
    /// </summary>
    public class VoiceRuntimeInstallTests : IDisposable
    {
        private readonly String _root =
            Path.Combine(Path.GetTempPath(), "cc-voiceinstall-" + Guid.NewGuid().ToString("N"));

        private readonly String _package;
        private readonly String _runtime;

        public VoiceRuntimeInstallTests()
        {
            this._package = Path.Combine(this._root, "package");
            this._runtime = Path.Combine(this._root, "runtime");
            Directory.CreateDirectory(this._package);
            Directory.CreateDirectory(this._runtime);
        }

        public void Dispose()
        {
            try { Directory.Delete(this._root, recursive: true); } catch { /* best effort */ }
        }

        private static void Write(String dir, String relative, String content)
        {
            var path = Path.Combine(dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private void WriteBoth(String relative, String content)
        {
            Write(this._package, relative, content);
            Write(this._runtime, relative, content);
        }

        [Fact]
        public void An_identical_tree_matches()
        {
            this.WriteBoth("whisper-cli", "binary");
            this.WriteBoth("libggml-metal.so", "metal backend");

            Assert.True(BridgeManager.RuntimeTreeMatchesPackage(this._package, this._runtime));
        }

        /// <summary>The exact 2.0.1 shape: the CLI is there, the compute backends are not.</summary>
        [Fact]
        public void A_bundle_missing_its_backends_does_not_match()
        {
            this.WriteBoth("whisper-cli", "binary");
            Write(this._package, "libggml-metal.so", "metal backend");
            Write(this._package, "libggml-cpu-apple_m4.so", "cpu backend");

            Assert.False(BridgeManager.RuntimeTreeMatchesPackage(this._package, this._runtime));
        }

        [Fact]
        public void Same_size_but_different_content_does_not_match()
        {
            Write(this._package, "libggml-metal.so", "AAAA");
            Write(this._runtime, "libggml-metal.so", "BBBB");

            Assert.False(BridgeManager.RuntimeTreeMatchesPackage(this._package, this._runtime));
        }

        [Fact]
        public void A_file_of_a_different_length_does_not_match()
        {
            Write(this._package, "whisper-cli", "the shipped binary");
            Write(this._runtime, "whisper-cli", "an older binary");

            Assert.False(BridgeManager.RuntimeTreeMatchesPackage(this._package, this._runtime));
        }

        /// <summary>Nested files count too — the helper .app is a tree, not a flat directory.</summary>
        [Fact]
        public void A_difference_nested_inside_the_tree_is_detected()
        {
            this.WriteBoth("Contents/Info.plist", "plist");
            Write(this._package, "Contents/MacOS/ClaudeVoiceHelper", "new helper");
            Write(this._runtime, "Contents/MacOS/ClaudeVoiceHelper", "old helper!");

            Assert.False(BridgeManager.RuntimeTreeMatchesPackage(this._package, this._runtime));
        }

        /// <summary>Extra files in the runtime tree are not a reason to re-install.</summary>
        [Fact]
        public void Extra_runtime_files_do_not_force_a_reinstall()
        {
            this.WriteBoth("whisper-cli", "binary");
            Write(this._runtime, "TRANSCRIPTION_SMOKE_OK", "model=ggml-base.en.bin");

            Assert.True(BridgeManager.RuntimeTreeMatchesPackage(this._package, this._runtime));
        }

        [Fact]
        public void A_missing_runtime_directory_does_not_match()
        {
            this.WriteBoth("whisper-cli", "binary");

            Assert.False(BridgeManager.RuntimeTreeMatchesPackage(
                this._package, Path.Combine(this._root, "never-installed")));
        }

        /// <summary>
        /// An empty package must never be read as "matches", or a dev build with no payload would
        /// silently certify whatever happens to be installed.
        /// </summary>
        [Fact]
        public void An_empty_package_does_not_match()
        {
            Write(this._runtime, "whisper-cli", "binary");

            Assert.False(BridgeManager.RuntimeTreeMatchesPackage(this._package, this._runtime));
        }

        [Fact]
        public void Null_or_empty_paths_do_not_match()
        {
            Assert.False(BridgeManager.RuntimeTreeMatchesPackage(null, this._runtime));
            Assert.False(BridgeManager.RuntimeTreeMatchesPackage(this._package, null));
            Assert.False(BridgeManager.RuntimeTreeMatchesPackage("", ""));
        }

        // -------------------------------------------------------------------------------------
        // The failure sidecar is a contract between two languages, and neither compiler can see
        // the other side of it. If the Swift helper stops writing "<transcript>.error", or the
        // plugin stops reading it, voice goes back to reporting every hard failure as silence —
        // with no build error and no failing test anywhere else.
        // -------------------------------------------------------------------------------------

        private static String RepoFile(params String[] relative)
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(new[] { dir }.Concat(relative).ToArray());
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate " + Path.Combine(relative));
        }

        [Fact]
        public void The_helper_writes_the_error_sidecar_the_plugin_reads()
        {
            var swift = File.ReadAllText(RepoFile("tools", "voice", "ClaudeVoiceHelper.swift"));

            Assert.Contains("transcriptPath + \".error\"", swift);

            var bridge = File.ReadAllText(RepoFile("src", "Core", "BridgeManager.cs"));
            Assert.Contains("VoiceTranscriptFile + \".error\"", bridge);
            Assert.Contains("File.Exists(VoiceErrorFile)", bridge);
        }

        /// <summary>
        /// The two lines that made a crash indistinguishable from a quiet room: stderr thrown away,
        /// and exit(0) without ever reading terminationStatus.
        /// </summary>
        [Fact]
        public void The_helper_does_not_discard_whispers_failure()
        {
            var swift = File.ReadAllText(RepoFile("tools", "voice", "ClaudeVoiceHelper.swift"));

            Assert.DoesNotContain("p.standardError = FileHandle.nullDevice", swift);
            Assert.Contains("p.terminationStatus", swift);
        }

        /// <summary>
        /// The Windows helper is a THIRD party to the same contract, and it carried the identical
        /// defect: every failure path returned "" and exited 0, with stderr explicitly discarded.
        /// The plugin's reader is platform-neutral, so without this the Windows half is inert.
        /// </summary>
        [Fact]
        public void The_windows_helper_reports_failure_the_same_way()
        {
            var win = File.ReadAllText(RepoFile("tools", "windows", "ClaudeConsoleVoice", "Program.cs"));

            Assert.Contains("transcriptPath + \".error\"", win);
            Assert.Contains("p.ExitCode", win);
            Assert.DoesNotContain("content irrelevant on success", win);
        }

        /// <summary>
        /// The bundler must copy ggml's compute backends. They are dlopened at runtime, so they are
        /// absent from the link-time closure the script otherwise walks — and a bundle without them
        /// aborts on every machine that has no Homebrew, while passing every check on one that does.
        /// </summary>
        [Fact]
        public void The_bundler_ships_the_compute_backends_and_proves_it()
        {
            var script = File.ReadAllText(RepoFile("tools", "voice", "bundle-whisper.sh"));

            Assert.Contains("libexec", script);          // where Homebrew keeps the backends
            Assert.Contains("sandbox-exec", script);     // the smoke test must not see Homebrew
            Assert.Contains("TRANSCRIPTION_SMOKE_OK", script);

            // Packaging must refuse a bundle that never transcribed anything.
            var pack = File.ReadAllText(RepoFile("tools", "voice", "pack-release.sh"));
            Assert.Contains("TRANSCRIPTION_SMOKE_OK", pack);
        }
    }
}
