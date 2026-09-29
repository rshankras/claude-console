namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System.Globalization;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Xunit;

    public class DesktopChangesTraceTests
    {
        [Theory]
        [InlineData("{\"ok\":false,\"error\":\"panel-opener-unavailable\"}", "panel-opener-unavailable")]
        [InlineData("{\"ok\":false,\"error\":\"panel-unconfirmed\"}", "panel-unconfirmed")]
        [InlineData("{\"ok\":false,\"error\":\"private chat text\"}", "unknown-error")]
        [InlineData("{\"ok\":true}", "panel-unconfirmed")]
        [InlineData(null, "no-helper-output")]
        [InlineData("[]", "unexpected-reply")]
        [InlineData("private helper output", "unexpected-reply")]
        public void Mac_keeps_the_specific_allowlisted_failure_without_exposing_content(String reply, String expected)
        {
            var codes = new List<String>();
            var helperCalls = 0;
            var auto = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            { Runner = (_, _) => { helperCalls++; return reply; }, ChangesTrace = codes.Add };
            Assert.False(auto.OpenChanges(out var error));
            Assert.Equal(expected, error);
            Assert.Equal(new[] { "requested", expected }, codes);
            Assert.Equal(1, helperCalls);
        }

        [Fact]
        public void Broken_diagnostic_sink_cannot_change_success_or_run_an_extra_helper()
        {
            var calls = 0;
            var auto = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            { Runner = (_, _) => { calls++; return "{\"ok\":true,\"opened\":true,\"alreadyOpen\":true}"; },
                ChangesTrace = _ => throw new IOException("fixture log unavailable") };
            Assert.True(auto.OpenChanges(out var error));
            Assert.Null(error); Assert.Equal(1, calls);
        }

        [Fact]
        public void Trace_is_opt_in_expires_and_has_a_strict_concurrent_record_limit()
        {
            using var home = new TempHome();
            var file = Path.Combine(home.Dir, "trace-until");
            var now = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
            var lines = new System.Collections.Concurrent.ConcurrentBag<String>();
            var trace = new DesktopChangesTrace(file, lines.Add, () => now);
            trace.Write("requested");
            Assert.Empty(lines);
            foreach (var value in new[] { "invalid", new String('x', 101),
                now.AddMinutes(-1).ToString("O"), now.AddMinutes(31).ToString("O"),
                DateTime.SpecifyKind(now.AddMinutes(5), DateTimeKind.Unspecified).ToString("O") })
            {
                File.WriteAllText(file, value); trace.Write("requested"); Assert.Empty(lines);
            }
            File.WriteAllText(file, now.AddMinutes(20).ToString("O", CultureInfo.InvariantCulture));
            trace.Write("panel-opener-unavailable");
            Assert.Equal("DesktopViewChanges: result=panel-opener-unavailable", Assert.Single(lines));
            now = now.AddMinutes(20);
            trace.Write("requested"); Assert.Single(lines);
            File.WriteAllText(file, now.AddMinutes(20).ToString("O"));
            Parallel.For(0, 200, _ => trace.Write("opened"));
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
            var trace = new DesktopChangesTrace(file, lines.Add, () => now);
            foreach (var code in new[] { null, "private title\nDesktopViewChanges: result=copied", "Copied", "" }) trace.Write(code);
            Assert.Equal(4, lines.Count);
            Assert.All(lines, line => Assert.Equal("DesktopViewChanges: result=unknown-error", line));
        }

    }
}
