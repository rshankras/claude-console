namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Xunit;

    public class DesktopDraftRecoveryTests
    {
        [Theory]
        [InlineData("draft-exists")]
        [InlineData("write-not-applied")]
        [InlineData("no-unique-composer")]
        public void Refused_draft_copies_the_original_words_and_reports_manual_paste(String error)
        {
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
            var failures = new List<(VoiceIntent, String)>();
            var copied = new List<String>();
            bridge.OnVoiceFailed += (intent, text) => failures.Add((intent, text));
            bridge.TranscriptSink = (_, submit) => { Assert.False(submit); return error; };
            bridge.DraftClipboardFallback = text => { copied.Add(text); return true; };

            bridge.DeliverToSink("Keep this draft", submit: false);

            Assert.Equal("Keep this draft", Assert.Single(copied));
            Assert.Equal((VoiceIntent.DesktopDraft, VoiceFailure.PasteDraft), Assert.Single(failures));
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, true)]
        [InlineData(true, false)]
        public void Successful_delivery_and_auto_send_never_replace_the_clipboard(Boolean submit, Boolean success)
        {
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
            var copies = 0;
            bridge.TranscriptSink = (_, _) => success ? null : "send-not-found";
            bridge.DraftClipboardFallback = _ => { copies++; return true; };

            bridge.DeliverToSink("hello", submit);

            Assert.Equal(0, copies);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Copy_failure_cannot_claim_the_draft_is_on_the_clipboard(Boolean throws)
        {
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
            var failures = new List<String>();
            bridge.OnVoiceFailed += (_, text) => failures.Add(text);
            bridge.TranscriptSink = (_, _) => "write-not-applied";
            bridge.DraftClipboardFallback = _ => throws ? throw new IOException("unavailable") : false;

            bridge.DeliverToSink("hello", submit: false);

            Assert.Equal(VoiceFailure.NotTyped, Assert.Single(failures));
        }

        [Fact]
        public void A_throwing_sink_still_allows_draft_recovery()
        {
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
            var failures = new List<String>();
            bridge.OnVoiceFailed += (_, text) => failures.Add(text);
            bridge.TranscriptSink = (_, _) => throw new IOException("unavailable");
            bridge.DraftClipboardFallback = _ => true;
            bridge.DeliverToSink("hello", submit: false);
            Assert.Equal(VoiceFailure.PasteDraft, Assert.Single(failures));
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(null, false)]
        public void Clipboard_transport_preserves_words_without_shell_interpolation_and_cleans_up(Int32? exit, Boolean success)
        {
            var directory = Path.Combine(Path.GetTempPath(), "vizhi-copy-test-" + Guid.NewGuid().ToString("N"), "quote' $() `test`");
            var words = "வணக்கம் café\n`command` $(command) ' \" \\ &";
            String inputPath = null;
            try
            {
                var copied = DesktopDraftClipboard.Copy(words, directory, (file, args, timeout) =>
                {
                    Assert.Equal("/bin/sh", file);
                    Assert.Equal(new[] { "-c", "exec /usr/bin/pbcopy < \"$1\"", "vizhi-draft-copy" }, args.GetRange(0, 3));
                    Assert.Equal(4, args.Count);
                    Assert.Equal(2000, timeout);
                    inputPath = args[3];
                    Assert.Equal(words, File.ReadAllText(inputPath));
                    Assert.False(File.ReadAllBytes(inputPath).AsSpan().StartsWith(new Byte[] { 0xef, 0xbb, 0xbf }));
                    if (!OperatingSystem.IsWindows())
                    {
                        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(inputPath));
                    }
                    return exit;
                });
                Assert.Equal(success, copied);
                Assert.NotNull(inputPath);
                Assert.False(File.Exists(inputPath));
            }
            finally { if (Directory.Exists(directory)) { Directory.Delete(Path.GetDirectoryName(directory), true); } }
        }

        [Fact]
        public void Processing_remains_visible_for_the_starting_intent_until_delivery_finishes()
        {
            var capture = new VoiceCaptureState();
            capture.Press(VoiceIntent.DesktopDraft, DateTime.UtcNow);
            Assert.False(capture.IsTranscribing(VoiceIntent.DesktopDraft));
            capture.Press(VoiceIntent.Desktop, DateTime.UtcNow); // another key can finish recording
            Assert.True(capture.IsTranscribing(VoiceIntent.DesktopDraft));
            Assert.False(capture.IsTranscribing(VoiceIntent.Desktop));
            Assert.Equal(VoiceAction.Refuse, capture.Press(VoiceIntent.DesktopDraft, DateTime.UtcNow).Action);
            Assert.True(capture.IsTranscribing(VoiceIntent.DesktopDraft));
            capture.Finish();
            Assert.False(capture.IsTranscribing(VoiceIntent.DesktopDraft));
        }
    }
}
