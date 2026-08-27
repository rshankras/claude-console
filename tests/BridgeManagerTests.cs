namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    using Xunit;

    /// <summary>
    /// Pruning of dead-session IPC files, TTY normalisation, and the voice "Go to Project"
    /// fuzzy matcher.
    /// </summary>
    public class BridgeManagerTests : IDisposable
    {
        private readonly String _root =
            Path.Combine(Path.GetTempPath(), "cc-bridge-" + Guid.NewGuid().ToString("N"));

        public BridgeManagerTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        private String WriteFile(String name, TimeSpan age)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, "{}");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
            return path;
        }

        // -------------------------------------------------------------------------------------
        // Stale IPC pruning — closed tabs must not leave state on disk forever.
        // -------------------------------------------------------------------------------------

        [Fact]
        public void PruneStaleFiles_removes_files_older_than_the_cutoff()
        {
            var stale = WriteFile("ttys001.json", TimeSpan.FromMinutes(30));

            BridgeManager.PruneStaleFiles(new[] { _root }, DateTime.UtcNow - TimeSpan.FromMinutes(10));

            Assert.False(File.Exists(stale));
        }

        [Fact]
        public void PruneStaleFiles_keeps_files_from_live_sessions()
        {
            // A live session's statusline rewrites its file on every assistant message.
            var fresh = WriteFile("ttys002.json", TimeSpan.FromMinutes(1));

            BridgeManager.PruneStaleFiles(new[] { _root }, DateTime.UtcNow - TimeSpan.FromMinutes(10));

            Assert.True(File.Exists(fresh));
        }

        [Fact]
        public void PruneStaleFiles_ignores_directories_that_do_not_exist()
        {
            var missing = Path.Combine(_root, "not-created-yet");

            BridgeManager.PruneStaleFiles(new[] { missing }, DateTime.UtcNow);   // must not throw

            Assert.False(Directory.Exists(missing));
        }

        // -------------------------------------------------------------------------------------
        // TTY normalisation — osascript reports "/dev/ttys003", ps reports "ttys003".
        // -------------------------------------------------------------------------------------

        [Theory]
        [InlineData("/dev/ttys003", "ttys003")]
        [InlineData("ttys003", "ttys003")]
        [InlineData("  /dev/ttys012  ", "ttys012")]
        public void NormalizeTty_reduces_both_forms_to_the_bare_name(String raw, String expected)
        {
            Assert.Equal(expected, BridgeManager.NormalizeTty(raw));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("??")]      // ps: process has no controlling terminal
        public void NormalizeTty_returns_null_when_there_is_no_tty(String raw)
        {
            Assert.Null(BridgeManager.NormalizeTty(raw));
        }

        // -------------------------------------------------------------------------------------
        // Voice "Go to Project" matching.
        // -------------------------------------------------------------------------------------

        [Theory]
        [InlineData("open indie app autopilot", "indieappautopilot")]
        [InlineData("switch to headroom", "headroom")]
        [InlineData("go to the vizhi project", "vizhi")]
        public void NormalizeForMatch_strips_filler_words_and_punctuation(String spoken, String expected)
        {
            Assert.Equal(expected, BridgeManager.NormalizeForMatch(spoken));
        }

        [Theory]
        [InlineData("go to the claude console project", "claude-console")]
        [InlineData("open claude console", "claude-console")]
        public void Spoken_phrase_and_folder_name_normalize_alike(String spoken, String folder)
        {
            // "claude" is stripped as a command word ("launch claude in headroom"), which also
            // strips it from a folder called claude-console. That's fine — and load-bearing:
            // normalisation runs over BOTH sides, so the two still meet exactly.
            var spokenKey = BridgeManager.NormalizeForMatch(spoken);
            var folderKey = BridgeManager.NormalizeForMatch(folder);

            Assert.Equal(folderKey, spokenKey);
            Assert.Equal(1000, BridgeManager.MatchScore(spokenKey, folderKey));
        }

        [Fact]
        public void MatchScore_ranks_exact_over_prefix_over_substring()
        {
            var exact = BridgeManager.MatchScore("headroom", "headroom");
            var prefix = BridgeManager.MatchScore("head", "headroom");
            var substring = BridgeManager.MatchScore("droom", "headroom");

            Assert.Equal(1000, exact);
            Assert.True(exact > prefix, "exact should beat prefix");
            Assert.True(prefix > substring, "prefix should beat substring");
        }

        [Fact]
        public void MatchScore_accepts_a_close_mishearing()
        {
            // Whisper hears "claude consol" — still clearly the same project.
            var score = BridgeManager.MatchScore("claudeconsol", "claudeconsole");

            Assert.True(score >= 300, $"expected a usable match, got {score}");
        }

        [Fact]
        public void MatchScore_rejects_an_unrelated_name()
        {
            // Below the 300 floor MatchProject refuses to launch anything — a wrong guess would
            // cd into the wrong project and start a session there.
            Assert.True(BridgeManager.MatchScore("headroom", "vizhi") < 300);
        }

        [Theory]
        [InlineData("", "abc", 0)]
        [InlineData("abc", "", 0)]
        [InlineData("abcdef", "zzabcdzz", 4)]
        [InlineData("abc", "xyz", 0)]
        public void LongestCommonSubstringLength_measures_the_longest_run(String a, String b, Int32 expected)
        {
            Assert.Equal(expected, BridgeManager.LongestCommonSubstringLength(a, b));
        }

        // -------------------------------------------------------------------------------------
        // Voice runtime/capture ownership — every voice key shares the same IPC files.
        // -------------------------------------------------------------------------------------

        [Fact]
        public void Only_one_voice_action_can_own_the_shared_capture_pipeline()
        {
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());

            Assert.True(bridge.TryReserveVoiceCapture());
            Assert.True(bridge.VoiceCaptureActive);
            Assert.False(bridge.TryReserveVoiceCapture());

            bridge.ReleaseVoiceCapture();

            Assert.False(bridge.VoiceCaptureActive);
            Assert.True(bridge.TryReserveVoiceCapture());
            bridge.ReleaseVoiceCapture();
        }

        [Fact]
        public void Voice_capture_fails_closed_when_runtime_validation_fails()
        {
            if (!BridgeManager.VoiceSupported)
            {
                return;
            }

            var platform = new PlatformSeamTests.FakePlatformBridge();
            var bridge = new BridgeManager(platform)
            {
                VoiceRuntimeInstaller = () => false,
            };

            Assert.False(bridge.StartVoiceCapture());
            Assert.False(bridge.VoiceCaptureActive);
            Assert.Equal(1, platform.Alerts);
        }

        [Fact]
        public void Voice_runtime_match_requires_every_packaged_file_at_the_same_length()
        {
            var package = Path.Combine(_root, "package");
            var runtime = Path.Combine(_root, "runtime");
            Directory.CreateDirectory(Path.Combine(package, "backends"));
            Directory.CreateDirectory(Path.Combine(runtime, "backends"));
            File.WriteAllText(Path.Combine(package, "whisper-cli"), "cli-v2");
            File.WriteAllText(Path.Combine(package, "backends", "libggml.dylib"), "backend");
            File.WriteAllText(Path.Combine(runtime, "whisper-cli"), "cli-v2");

            Assert.False(BridgeManager.RuntimeTreeMatchesPackage(package, runtime));

            File.WriteAllText(Path.Combine(runtime, "backends", "libggml.dylib"), "backend");
            Assert.True(BridgeManager.RuntimeTreeMatchesPackage(package, runtime));

            // Same length, different content: catches a stale CLI whose broken linkage changed
            // without changing the executable size.
            File.WriteAllText(Path.Combine(runtime, "whisper-cli"), "old-v2");
            Assert.False(BridgeManager.RuntimeTreeMatchesPackage(package, runtime));
        }

        [Fact]
        public void Empty_voice_package_is_never_considered_a_valid_runtime()
        {
            var package = Path.Combine(_root, "empty-package");
            var runtime = Path.Combine(_root, "empty-runtime");
            Directory.CreateDirectory(package);
            Directory.CreateDirectory(runtime);

            Assert.False(BridgeManager.RuntimeTreeMatchesPackage(package, runtime));
        }
    }
}
