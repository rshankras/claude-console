namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Agents;

    using Xunit;

    /// <summary>
    /// Codex session ids are UUIDv7, so the id itself says when the session began. The registry
    /// uses that to tell two terminals in one folder apart; nothing else may read meaning into it.
    /// </summary>
    public sealed class CodexSessionIdTests
    {
        [Fact]
        public void A_codex_session_id_decodes_to_its_creation_instant()
        {
            // Two live sessions on 2026-09-28: each id decoded to within a second of its process start.
            var codex = new CodexCliAdapter();
            Assert.Equal(new DateTime(2026, 9, 28, 13, 38, 2, 201, DateTimeKind.Utc), codex.SessionStartedAtUtc("01a0e83c-9f59-7c83-ae1d-14dc5aad0789"));
            Assert.Equal(new DateTime(2026, 9, 25, 11, 1, 46, 565, DateTimeKind.Utc), codex.SessionStartedAtUtc("01a0d83a-7bc5-7c83-97d9-10e8f9b5ed18"));
        }

        [Fact]
        public void Anything_but_a_version_7_uuid_carries_no_time()
        {
            var codex = new CodexCliAdapter();
            Assert.Null(codex.SessionStartedAtUtc(null));
            Assert.Null(codex.SessionStartedAtUtc(""));
            Assert.Null(codex.SessionStartedAtUtc("sid-ttys001"));
            Assert.Null(codex.SessionStartedAtUtc("3f2504e0-4f89-41d3-9a0c-0305e82c3301"));   // v4
            Assert.Null(codex.SessionStartedAtUtc("zz2504e0-4f89-71d3-9a0c-0305e82c3301"));   // not hex
        }

        [Fact]
        public void Other_agents_report_no_session_time()
        {
            IAgentAdapter claude = new ClaudeCodeAdapter();
            Assert.Null(claude.SessionStartedAtUtc("01a0e83c-9f59-7c83-ae1d-14dc5aad0789"));
            Assert.False(claude.Capabilities.HooksMayReportAnotherTerminal);
            Assert.True(new CodexCliAdapter().Capabilities.HooksMayReportAnotherTerminal);
        }
    }
}
