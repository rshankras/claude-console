namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
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
        [InlineData("Go to Project Cloud Code.", "cloudcode")]
        // Whole words off the edges only — the old substring Replace ate "the" out of "theme"
        // and "open" out of "openai" (2.2.1 Windows retest, item 8).
        [InlineData("open theme", "theme")]
        [InlineData("go to openai", "openai")]
        [InlineData("open the project", "opentheproject")]
        public void NormalizeForMatch_strips_carrier_words_off_the_edges(String spoken, String expected)
        {
            Assert.Equal(expected, BridgeManager.NormalizeForMatch(spoken));
        }

        [Theory]
        [InlineData("go to the claude console project", "claude-console")]
        [InlineData("open claude console", "claude-console")]
        [InlineData("go to project claude", "claude")]
        public void Spoken_phrase_and_folder_name_meet_exactly(String spoken, String folder)
        {
            // Folder names are never stripped — "claude" used to be cut out of every claude-*
            // folder, and a project called exactly "claude" could not be reached at all.
            var spokenKey = BridgeManager.NormalizeForMatch(spoken);
            var folderKey = BridgeManager.SquashForMatch(folder);

            Assert.Equal(folderKey, spokenKey);
            Assert.Equal(1000, BridgeManager.MatchScore(spokenKey, folderKey));
        }

        [Fact]
        public void MatchProject_prefers_the_full_name_over_a_carrier_stripped_reading()
        {
            // "claude code" said in full must reach claude-code, not the project whose name merely
            // contains "code" — the as-spoken reading scores an exact 1000 and wins.
            var candidates = new[] { "/w/vscode-ext", "/w/claude-code", "/w/claude-console" };

            Assert.Equal("/w/claude-code", BridgeManager.MatchProject("go to project claude code", candidates));
            Assert.Equal("/w/claude-console", BridgeManager.MatchProject("open claude console", candidates));
            Assert.Equal("/w/claude-console", BridgeManager.MatchProject("launch claude in claude console", candidates));
        }

        [Fact]
        public void MatchProject_reaches_a_project_named_like_a_carrier_word()
        {
            var candidates = new[] { "/w/claude", "/w/claude-console", "/w/open-source-kit" };

            Assert.Equal("/w/claude", BridgeManager.MatchProject("go to project claude", candidates));
            Assert.Equal("/w/open-source-kit", BridgeManager.MatchProject("open open source kit", candidates));
        }

        [Fact]
        public void MatchProject_still_refuses_a_mishearing_that_matches_nothing()
        {
            // QA's dictation: whisper heard "Cloud" for "Claude". Four letters of overlap is below
            // the fuzzy floor, and the key now says No match rather than nothing (item 8).
            Assert.Null(BridgeManager.MatchProject("Go to Project Cloud Code.", new[] { "/w/claude-code" }));
        }

        [Theory]
        [InlineData("go to open source kit")]
        [InlineData("open the open source kit project")]
        [InlineData("open source kit")]
        public void MatchProject_preserves_carrier_words_inside_the_longest_complete_name(String spoken)
        {
            var shortName = Path.Combine("projects", "source-kit");
            var fullName = Path.Combine("projects", "open-source-kit");
            Assert.Equal(fullName, BridgeManager.MatchProject(spoken, new[] { shortName, fullName }));
            Assert.Equal(fullName, BridgeManager.MatchProject(spoken, new[] { fullName, shortName }));
        }

        [Fact]
        public void MatchProject_refuses_equally_good_folders()
        {
            var first = Path.Combine("work", "console");
            var second = Path.Combine("personal", "console");
            Assert.Null(BridgeManager.MatchProject("open console", new[] { first, second }));
            Assert.Null(BridgeManager.MatchProject("open console", new[] { second, first }));
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

        // -------------------------------------------------------------------------------------
        // The transcript sink — a product's voice keys aimed at something that is not a terminal.
        // The engine still owns delivery failures, so they reach the key like every other (#18).
        // -------------------------------------------------------------------------------------

        [Fact]
        public void A_desktop_capture_with_no_sink_installed_says_No_target_on_its_key()
        {
            var platform = new PlatformSeamTests.FakePlatformBridge();
            var bridge = new BridgeManager(platform);
            var failures = new List<(VoiceIntent Intent, String Text)>();
            bridge.OnVoiceFailed += (intent, text) => failures.Add((intent, text));

            bridge.DeliverToSink("hello", submit: true);

            Assert.Equal((VoiceIntent.Desktop, VoiceFailure.NoTarget), Assert.Single(failures));
            Assert.Equal(1, platform.Alerts);
        }

        [Fact]
        public void The_sink_gets_the_words_and_the_submit_flag_and_only_a_refusal_reaches_the_key()
        {
            var platform = new PlatformSeamTests.FakePlatformBridge();
            var bridge = new BridgeManager(platform);
            var failures = new List<(VoiceIntent Intent, String Text)>();
            bridge.OnVoiceFailed += (intent, text) => failures.Add((intent, text));
            var delivered = new List<(String Text, Boolean Submit)>();
            bridge.TranscriptSink = (text, submit) =>
            {
                delivered.Add((text, submit));
                return text == "bad" ? "composer hidden" : null;
            };

            bridge.DeliverToSink("draft me", submit: false);
            bridge.DeliverToSink("bad", submit: true);

            Assert.Equal(new[] { ("draft me", false), ("bad", true) }, delivered);
            Assert.Equal((VoiceIntent.Desktop, VoiceFailure.NotTyped), Assert.Single(failures));
            Assert.Equal(1, platform.Alerts);
        }

        [Fact]
        public void A_sink_that_throws_is_a_Not_typed_failure_not_a_crash()
        {
            var platform = new PlatformSeamTests.FakePlatformBridge();
            var bridge = new BridgeManager(platform);
            var failures = new List<(VoiceIntent Intent, String Text)>();
            bridge.OnVoiceFailed += (intent, text) => failures.Add((intent, text));
            bridge.TranscriptSink = (_, _) => throw new InvalidOperationException("boom");

            bridge.DeliverToSink("x", submit: false);

            Assert.Equal((VoiceIntent.DesktopDraft, VoiceFailure.NotTyped), Assert.Single(failures));
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
