namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;

    using Xunit;

    /// <summary>
    /// Windows voice capture (Phase 5). The helper is a separate executable, so — like the hook
    /// and focus helpers — the things it MUST agree with the plugin about are pinned by reading
    /// its source: the argument names BridgeManager passes, the audio format whisper requires,
    /// and the transcript-file contract the plugin's 20-second wait depends on.
    /// </summary>
    public class WindowsVoiceTests
    {
        [Fact]
        public void Voice_is_supported_on_this_platform()
        {
            // Runs on macOS AND Windows. The same class of test that was missing for Phase 4:
            // the capture gate read `if (!OperatingSystem.IsMacOS())` and no test noticed.
            Assert.True(BridgeManager.VoiceSupported);
        }

        // ---------------------------------------------------------------------------------------
        // Contract with the capture helper
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void The_helper_accepts_exactly_the_arguments_the_plugin_passes()
        {
            // StartVoiceCaptureWindows passes these names; the macOS helper takes the same ones.
            // If either side renames one, recording silently loses that setting.
            var source = ReadHelperSource();

            Assert.Contains("--maxsec", source);
            Assert.Contains("--out", source);
            Assert.Contains("--stopflag", source);
            Assert.Contains("--transcript", source);
            Assert.Contains("--model", source);
            Assert.Contains("--whisper", source);
        }

        [Fact]
        public void The_helper_records_the_format_whisper_requires()
        {
            // 16 kHz mono 16-bit PCM, produced by the wave mapper directly — there is no
            // resampler in the pipeline to fix a drift here.
            var source = ReadHelperSource();

            Assert.Contains("16000", source);
            Assert.Contains("WAVE_FORMAT_PCM", source);
        }

        [Fact]
        public void The_helper_stops_on_the_stop_flag()
        {
            // The plugin's stop press writes this file; a helper that only honors --maxsec
            // would keep the mic open for a minute after the user asked it to stop.
            var source = ReadHelperSource();

            Assert.Contains("File.Exists(stopFlag)", source);
        }

        [Fact]
        public void The_transcript_is_written_atomically_and_always()
        {
            // The plugin polls the transcript file and types what it reads. Two ways to break
            // that: a partial write (types half a sentence) and a missing file on failure
            // (burns the plugin's whole 20 s wait). The helper guards both.
            var source = ReadHelperSource();

            Assert.Contains("File.Move(tmp, path, overwrite: true)", source);
            Assert.Contains("WriteAtomic(transcriptPath, \"\")", source);   // the failure path
        }

        [Fact]
        public void Whispers_non_speech_annotations_are_never_typed()
        {
            // Silence transcribes as "[BLANK_AUDIO]", a breath as "(sighs)". Typing those into
            // a terminal — and submitting — would be worse than typing nothing.
            var source = ReadHelperSource();

            Assert.Contains("CleanTranscript", source);
        }

        [Fact]
        // Every product ships a Windows payload, each its own: the terminal products the hook and
        // the toolkit, the desktop product the toolkit and its UI Automation helper (self-contained
        // since 2026-09-30; every Windows helper has had to bundle its runtime since #83) and no
        // hook, since it installs none. The verifier reads the platform off the package yaml
        // (pluginFolderWin) and the product off the DLL, and refuses a helper nothing launches.
        // Voice on Windows is a verb of the one self-contained toolkit — never a standalone voice
        // exe, which the branch this test came from still listed.
        public void Each_product_ships_its_own_Windows_helpers_and_the_toolkit_carries_voice()
        {
            var builder = ReadRepoFile("tools", "windows", "build-windows-payload.sh");
            var packer = ReadRepoFile("tools", "voice", "pack-release.sh");
            var verifier = ReadRepoFile("tools", "verify-package.sh");
            var signer = ReadRepoFile("tools", "windows", "sign-windows-payload.sh");

            Assert.Contains("ClaudeConsole|VizhiCodex|VizhiDesktop) SHIPS_WINDOWS=1", packer);
            Assert.Contains("VizhiDesktop) SHIPS_DESKTOP=1", packer);
            Assert.Contains("desktop/VizhiAxBridge", packer);
            Assert.Contains("pluginFolderWin", verifier);
            Assert.Contains("bin/vizhi-desktop-uia.exe", verifier);
            Assert.Contains("shipped in the desktop package", verifier);
            Assert.Contains("shipped in a terminal package", verifier);

            Assert.Contains("ClaudeConsole|VizhiCodex) PROJECTS=\"ClaudeConsoleHook ClaudeConsoleTools\"", builder);
            Assert.Contains("VizhiDesktop)             PROJECTS=\"ClaudeConsoleTools VizhiDesktopUia\"", builder);
            Assert.Contains("ClaudeConsoleTools|VizhiDesktopUia)", builder);
            Assert.Contains("SelfContained", builder);
            Assert.Contains("VizhiDesktop)  NAME=\"Vizhi Desktop\"", signer);

            Assert.Contains("WINDOWS_WHISPER_DIR", packer);
            Assert.Contains("$PKG_VOICE/whisper-bin-win", packer);
            Assert.Contains("$WIN_WBIN/whisper-cli.exe", packer);
        }

        private static String ReadHelperSource()
            => ReadRepoFile("tools", "windows", "ClaudeConsoleVoice", "Program.cs");

        private static String ReadRepoFile(params String[] parts)
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate the voice helper source");
        }
    }
}
