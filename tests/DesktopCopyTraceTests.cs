namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System.Globalization;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Xunit;

    public class DesktopCopyTraceTests
    {
        [Fact]
        public void Trace_is_opt_in_expires_and_has_a_strict_concurrent_record_limit()
        {
            using var home = new TempHome();
            var file = Path.Combine(home.Dir, "trace-until");
            var now = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
            var lines = new System.Collections.Concurrent.ConcurrentBag<String>();
            var trace = new DesktopCopyTrace(file, lines.Add, () => now);
            trace.Write("requested");
            Assert.Empty(lines);
            foreach (var value in new[] { "invalid", new String('x', 101),
                now.AddMinutes(-1).ToString("O"), now.AddMinutes(31).ToString("O"),
                DateTime.SpecifyKind(now.AddMinutes(5), DateTimeKind.Unspecified).ToString("O") })
            {
                File.WriteAllText(file, value); trace.Write("requested"); Assert.Empty(lines);
            }
            File.WriteAllText(file, now.AddMinutes(20).ToString("O", CultureInfo.InvariantCulture));
            trace.Write("reply-action-row-unrecognized");
            Assert.Equal("DesktopCopyReply: result=reply-action-row-unrecognized", Assert.Single(lines));
            now = now.AddMinutes(20);
            trace.Write("requested"); Assert.Single(lines);
            File.WriteAllText(file, now.AddMinutes(20).ToString("O"));
            Parallel.For(0, 200, _ => trace.Write("copied"));
            Assert.Equal(80, lines.Count);
        }

        [Fact]
        public void Unknown_codes_never_emit_arbitrary_helper_or_response_content()
        {
            using var home = new TempHome();
            var file = Path.Combine(home.Dir, "trace-until");
            var now = DateTime.UtcNow;
            File.WriteAllText(file, now.AddMinutes(5).ToString("O"));
            var lines = new List<String>();
            var trace = new DesktopCopyTrace(file, lines.Add, () => now);
            foreach (var code in new[] { null, "private title\nDesktopCopyReply: result=copied", "Copied", "" }) trace.Write(code);
            Assert.Equal(4, lines.Count);
            Assert.All(lines, line => Assert.Equal("DesktopCopyReply: result=unknown-error", line));
        }

        [Fact]
        public void Explicit_copy_reports_the_actual_result_but_never_its_text()
        {
            var codes = new List<String>();
            var fake = new DesktopCommandRig.Automation
            { CaptureResult = _ => new() { Error = "reply-copy-not-found", Text = "Private response" } };
            var context = new DesktopContextCapture(fake) { CopyTrace = codes.Add };
            Assert.Equal("Use App", context.Execute("copy", new()));
            Assert.Equal(new[] { "requested", "reply-copy-not-found" }, codes);
            codes.Clear();
            fake.CaptureResult = _ => new() { Ok = true, Text = "Private response" };
            Assert.Equal("Copied", context.Execute("copy", new()));
            Assert.Equal(new[] { "requested", "copied" }, codes);
            codes.Clear();
            fake.CaptureResult = _ => new() { Ok = true, Text = " " };
            Assert.NotEqual("Copied", context.Execute("copy", new()));
            Assert.Equal(new[] { "requested", "empty-response" }, codes);
            codes.Clear();
            fake.CaptureResult = _ => throw new IOException("Private helper output");
            Assert.Equal("Try Again", context.Execute("copy", new()));
            Assert.Equal(new[] { "requested", "helper-exception" }, codes);
            codes.Clear();
            context.Execute("clear", new());
            Assert.Empty(codes);
        }

        [Fact]
        public void Busy_and_voice_refusals_are_visible_without_running_an_extra_helper()
        {
            var codes = new List<String>();
            var fake = new DesktopCommandRig.Automation();
            var context = new DesktopContextCapture(fake) { CopyTrace = codes.Add };
            var voice = new VoiceCaptureState(); voice.Press(VoiceIntent.DesktopDraft, DateTime.UtcNow);
            Assert.Equal("Finish Speaking", context.Execute("copy", voice));
            Assert.Equal(new[] { "voice-busy" }, codes); Assert.Empty(fake.Calls);
            codes.Clear();
            fake.CaptureResult = _ =>
            {
                Assert.Equal("Please Wait", context.Execute("copy", new()));
                return new() { Ok = true, Text = "Private response" };
            };
            Assert.Equal("Copied", context.Execute("copy", new()));
            Assert.Equal(new[] { "requested", "context-busy", "copied" }, codes);
            Assert.Single(fake.Calls);
        }

        [Fact]
        public void Broken_diagnostics_cannot_change_the_copy_outcome()
        {
            using var home = new TempHome();
            var file = Path.Combine(home.Dir, "trace-until");
            File.WriteAllText(file, DateTime.UtcNow.AddMinutes(5).ToString("O"));
            var trace = new DesktopCopyTrace(file, _ => throw new IOException());
            trace.Write("copied"); // logger failure is contained
            var fake = new DesktopCommandRig.Automation { CaptureResult = _ => new() { Ok = true, Text = "Private response" } };
            var context = new DesktopContextCapture(fake) { CopyTrace = _ => throw new IOException() };
            Assert.Equal("Copied", context.Execute("copy", new()));
            Assert.True(context.HasReply); Assert.False(context.IsBusy);
        }
    }
}
