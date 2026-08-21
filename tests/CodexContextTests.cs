namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    using Loupedeck.ClaudeConsolePlugin.Agents;

    using Xunit;

    /// <summary>
    /// Context usage for Codex, read out of the rollout transcript.
    ///
    /// The fixture is a real token_count event from codex-cli 0.145.0, shortened but not reshaped.
    /// This is the plugin's only reader of a format its vendor calls unstable, so the tests care
    /// less about the happy path than about every way it must decline to answer: a wrong percentage
    /// on a key you glance at is worse than a blank one.
    /// </summary>
    public class CodexContextTests : IDisposable
    {
        private readonly String _dir =
            Path.Combine(Path.GetTempPath(), "cx-ctx-" + Guid.NewGuid().ToString("N"));

        public CodexContextTests() => Directory.CreateDirectory(this._dir);

        public void Dispose()
        {
            try { Directory.Delete(this._dir, recursive: true); } catch { /* best effort */ }
        }

        // Real shape: last_token_usage is what occupies the window; total_token_usage is cumulative.
        private const String RealTokenCount =
            "{\"timestamp\":\"2026-08-18T10:48:12.895Z\",\"ordinal\":74,\"type\":\"event_msg\"," +
            "\"payload\":{\"type\":\"token_count\",\"info\":{" +
            "\"total_token_usage\":{\"input_tokens\":166596,\"total_tokens\":167572}," +
            "\"last_token_usage\":{\"input_tokens\":31634,\"total_tokens\":31667}," +
            "\"model_context_window\":258400}}}";

        private String WriteRollout(params String[] lines)
        {
            var path = Path.Combine(this._dir, "rollout-test.jsonl");
            File.WriteAllLines(path, lines);
            return path;
        }

        /// <summary>31667 of 258400 is 12%. The cumulative total would say 65% — a different
        /// quantity, not a rounding difference.</summary>
        [Fact]
        public void Reads_the_window_occupancy_not_the_cumulative_total()
        {
            Assert.Equal(12, CodexContextReader.PercentFromLine(RealTokenCount));
        }

        [Fact]
        public void Takes_the_newest_reading_in_the_transcript()
        {
            var older = RealTokenCount.Replace("\"total_tokens\":31667", "\"total_tokens\":2584");
            var path = this.WriteRollout(older, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"agent_message\"}}", RealTokenCount);

            Assert.Equal(12, CodexContextReader.PercentFrom(path));
        }

        /// <summary>A tail read starts mid-file, so the first line is usually a fragment.</summary>
        [Fact]
        public void A_truncated_first_line_is_skipped_not_fatal()
        {
            var path = this.WriteRollout("token_count\":{\"info\":{\"broken", RealTokenCount);

            Assert.Equal(12, CodexContextReader.PercentFrom(path));
        }

        [Fact]
        public void A_transcript_with_no_readings_yields_unknown()
        {
            var path = this.WriteRollout("{\"type\":\"event_msg\",\"payload\":{\"type\":\"agent_message\"}}");

            Assert.Null(CodexContextReader.PercentFrom(path));
        }

        /// <summary>Every shape change the vendor might make must read as "unknown", never a number.</summary>
        [Theory]
        [InlineData("{\"payload\":{\"type\":\"token_count\",\"info\":{\"model_context_window\":0,\"last_token_usage\":{\"total_tokens\":5}}}}")]
        [InlineData("{\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{\"total_tokens\":5}}}}")]
        [InlineData("{\"payload\":{\"type\":\"token_count\",\"info\":{\"model_context_window\":100}}}")]
        [InlineData("{\"payload\":{\"type\":\"token_count\",\"info\":{\"model_context_window\":\"lots\",\"last_token_usage\":{\"total_tokens\":5}}}}")]
        [InlineData("{\"payload\":{\"type\":\"token_count\"}}")]
        [InlineData("{\"type\":\"token_count\"}")]
        [InlineData("not json but mentions token_count")]
        public void A_changed_or_broken_reading_is_unknown(String line) =>
            Assert.Null(CodexContextReader.PercentFromLine(line));

        /// <summary>A percentage over 100 is a bug somewhere; clamp rather than render nonsense.</summary>
        [Fact]
        public void An_impossible_reading_is_clamped()
        {
            var over = RealTokenCount.Replace("\"total_tokens\":31667", "\"total_tokens\":999999999");

            Assert.Equal(100, CodexContextReader.PercentFromLine(over));
        }

        /// <summary>
        /// The path arrives in a hook payload, so it is input. Only a rollout file is ever opened,
        /// and never one reached by climbing out of its directory.
        /// </summary>
        [Theory]
        [InlineData("/etc/passwd")]
        [InlineData("/Users/dev/.codex/sessions/../../.ssh/id_rsa")]
        [InlineData("/Users/dev/.codex/sessions/notes.txt")]
        [InlineData("")]
        [InlineData(null)]
        public void A_path_that_is_not_a_rollout_is_refused(String path) =>
            Assert.Null(CodexContextReader.PercentFrom(path));

        [Fact]
        public void A_missing_transcript_is_unknown() =>
            Assert.Null(CodexContextReader.PercentFrom(Path.Combine(this._dir, "gone.jsonl")));

        /// <summary>Runs on the poll loop for every session; it may never throw.</summary>
        [Fact]
        public void Nothing_here_throws()
        {
            var path = this.WriteRollout("\0\0\0 garbage", RealTokenCount, "");

            var ex = Record.Exception(() => CodexContextReader.PercentFrom(path));

            Assert.Null(ex);
        }

        /// <summary>The whole point: the number reaches the grid through the adapter.</summary>
        [Fact]
        public void The_percentage_reaches_the_grid()
        {
            var path = this.WriteRollout(RealTokenCount);
            var envelope =
                "{\"schema\":1,\"agent\":\"codex-cli\",\"event\":\"Stop\",\"ts\":1,\"payload\":" +
                "{\"cwd\":\"/Users/dev/project\",\"transcript_path\":\"" + path.Replace("\\", "\\\\") + "\"}}";

            var state = new CodexCliAdapter().ParseSessionState(envelope);

            Assert.Equal(12, state.CtxPercent);
        }
    }
}
