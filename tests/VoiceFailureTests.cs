namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Threading;

    using Xunit;

    /// <summary>
    /// A failed dictation has to say so, and say what to do (#18).
    ///
    /// Reproduced on the device 2026-08-28 with the microphone grant revoked: the key went green,
    /// came back to "Voice", nothing was typed, no beep — and the stop press was refused for 20s
    /// because the plugin was waiting for a transcript that would never come. The helper had
    /// written "microphone permission DENIED" to stderr, which for a detached process is nowhere.
    /// </summary>
    public class VoiceFailureTests
    {
        [Fact]
        public void VoiceFailuresHaveAReadableHoldAndRetryClearsImmediately()
        {
            Assert.Equal(8000, VoiceFailure.HoldMs);
            using var face = new FailureFace(() => { }, VoiceFailure.HoldMs);
            face.Show(VoiceFailure.NoSpeech);
            Assert.True(face.IsActive);
            face.Clear();
            Assert.False(face.IsActive);
            Assert.Null(face.Text);
        }

        // ---------------------------------------------------------------------------------------
        // The words on the key are chosen by what the user should DO
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void ADeniedMicrophoneTellsTheUserToGrantIt()
        {
            // This is the field report. "Voice failed" would be true and useless; the user action
            // is a System Settings toggle, and the key has to point at it.
            var sidecar = "microphone permission denied — allow ClaudeVoiceHelper in System Settings › Privacy & Security › Microphone";
            Assert.Equal(VoiceFailure.MicDenied, VoiceFailure.FromSidecar(sidecar));
        }

        [Fact]
        public void AMissingModelTellsTheUserToWait()
        {
            Assert.Equal(VoiceFailure.ModelLoading, VoiceFailure.FromSidecar("speech model not found at /x/ggml-base.en.bin"));
        }

        [Theory]
        [InlineData("whisper-cli exited with status 134: GGML_ASSERT(device) failed")]
        [InlineData("whisper-cli not found — the voice bundle is missing or incomplete")]
        [InlineData("failed to create AVAudioRecorder")]
        public void AnythingElseIsAGenericFailureWithTheDetailInTheLog(String sidecar)
        {
            Assert.Equal(VoiceFailure.Failed, VoiceFailure.FromSidecar(sidecar));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void AnEmptySidecarIsStillAFailureNotASuccess(String sidecar)
        {
            // The sidecar's EXISTENCE is the failure signal; its text is a courtesy. A helper that
            // wrote an empty one must not be read as having succeeded.
            Assert.Equal(VoiceFailure.Failed, VoiceFailure.FromSidecar(sidecar));
        }

        [Fact]
        public void TheWordsFitOnAKey()
        {
            // The face renders the text as the key title. Anything longer than this wraps or
            // truncates on the LCD, and a truncated "Mic den…" helps nobody.
            foreach (var words in new[] { VoiceFailure.MicDenied, VoiceFailure.NoSpeech, VoiceFailure.ModelLoading, VoiceFailure.NoHelper, VoiceFailure.NoResponse, VoiceFailure.Failed })
            {
                Assert.True(words.Length <= 13, $"'{words}' is too long for a key face");
            }
        }

        // ---------------------------------------------------------------------------------------
        // The face shows, then gets out of the way
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void TheFaceShowsImmediatelyAndClearsAfterTheHold()
        {
            var repaints = 0;
            using var face = new FailureFace(() => Interlocked.Increment(ref repaints), holdMs: 80);

            Assert.False(face.IsActive);

            face.Show(VoiceFailure.MicDenied);
            Assert.True(face.IsActive);
            Assert.Equal(VoiceFailure.MicDenied, face.Text);
            Assert.Equal(1, repaints);

            Thread.Sleep(400);

            Assert.False(face.IsActive);
            Assert.Null(face.Text);
            Assert.Equal(2, repaints);   // once to show, once to clear — never a storm
        }

        [Fact]
        public void ASecondFailureReplacesTheFirstAndRestartsTheHold()
        {
            using var face = new FailureFace(() => { }, holdMs: 200);

            face.Show(VoiceFailure.NoSpeech);
            Thread.Sleep(120);
            face.Show(VoiceFailure.MicDenied);
            Thread.Sleep(120);

            // 240ms after the first Show, but only 120ms after the second: still up, and the newer one.
            Assert.True(face.IsActive);
            Assert.Equal(VoiceFailure.MicDenied, face.Text);
        }

        // ---------------------------------------------------------------------------------------
        // Every exit path reports — a contract across two languages, so read the sources
        // ---------------------------------------------------------------------------------------

        private static String RepoFile(params String[] relative)
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir, Path.Combine(relative));
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate " + Path.Combine(relative));
        }

        [Fact]
        public void TheHelperNamesADeniedMicrophoneInsteadOfExitingSilently()
        {
            var swift = File.ReadAllText(RepoFile("tools", "voice", "ClaudeVoiceHelper.swift"));

            // THE important one. exit(2) on denial ran BEFORE the sidecar helper existed, so the
            // commonest failure was the one that could not be reported. The sidecar writer must be
            // defined ahead of the permission check, and the denial must use it.
            var failDefined = swift.IndexOf("func fail(", StringComparison.Ordinal);
            var permission = swift.IndexOf("requestAccess(for: .audio)", StringComparison.Ordinal);
            Assert.True(failDefined > 0 && permission > 0);
            Assert.True(failDefined < permission, "fail() is defined after the permission check, so a denial cannot use it");

            Assert.Contains("fail(\"microphone permission denied", swift);

            // No exit path may bypass the sidecar. Raw exits were how #18 stayed silent.
            Assert.DoesNotContain("exit(2)", swift);
            Assert.DoesNotContain("exit(3)", swift);
        }

        [Fact]
        public void ThePluginReportsEveryWayAWaitCanEnd()
        {
            var bridge = File.ReadAllText(RepoFile("src", "Core", "BridgeManager.cs"));

            // A named failure, a blank transcript, and a wait that ran out — each used to end in a
            // log line and nothing else. Each must now go through the one reporting path.
            Assert.Contains("VoiceFailure.FromSidecar(", bridge);
            Assert.Contains("VoiceFailure.NoSpeech", bridge);
            Assert.Contains("VoiceFailure.NoResponse", bridge);
            Assert.Contains("VoiceFailure.NoHelper", bridge);
            Assert.Contains("VoiceFailure.ModelLoading", bridge);
        }
    }
}
