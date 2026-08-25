namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    using Xunit;

    /// <summary>
    /// The helper's JSON is an external input crossing a process boundary — a version-skewed
    /// helper, a truncated pipe, or a crash mid-write must all degrade to "we cannot see"
    /// (SurfaceAvailable=false), never to a guessed state on a key. This class is the contract's
    /// receiving end; AxBridgeContractTests pins the sending end.
    /// </summary>
    public class DesktopSnapshotTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not json at all")]
        [InlineData("{\"truncated\":tru")]
        [InlineData("{}")]                                    // no ok, no surface
        [InlineData("{\"ok\":false,\"error\":\"not-trusted\"}")]
        [InlineData("{\"ok\":true,\"surface\":false}")]       // the screen-lock report
        public void Anything_short_of_a_healthy_report_reads_as_unavailable(String json)
        {
            var snap = DesktopSnapshot.Parse(json);

            Assert.False(snap.SurfaceAvailable);
        }

        [Fact]
        public void A_full_report_round_trips_every_field()
        {
            var snap = DesktopSnapshot.Parse(
                "{\"ok\":true,\"surface\":true,\"attention\":true,\"approvalPresent\":true," +
                "\"denyPresent\":true,\"stopPresent\":false," +
                "\"cardText\":\"Run npm install\",\"mode\":\"Codex\"}");

            Assert.True(snap.SurfaceAvailable);
            Assert.True(snap.Attention);
            Assert.True(snap.ApprovalPresent);
            Assert.True(snap.DenyPresent);
            Assert.False(snap.StopPresent);
            Assert.Equal("Run npm install", snap.CardText);
            Assert.Equal("Codex", snap.Mode);
        }

        [Fact]
        public void Conversations_parse_with_states_and_order_preserved()
        {
            var snap = DesktopSnapshot.Parse(
                "{\"ok\":true,\"surface\":true,\"conversations\":[" +
                "{\"title\":\"Q3 report\",\"state\":\"awaiting\"}," +
                "{\"title\":\"Bug triage\",\"state\":\"unread\"}," +
                "{\"title\":\"API redesign\",\"state\":\"running\"}," +
                "{\"title\":\"Blog draft\",\"state\":\"idle\"}," +
                "{\"title\":\"Odd one\",\"state\":\"someday-new-state\"}," +
                "{\"state\":\"awaiting\"}]}");   // no title: dropped, not rendered blank

            Assert.Equal(5, snap.Conversations.Count);
            Assert.Equal("Q3 report", snap.Conversations[0].Title);
            Assert.Equal(ConversationState.Awaiting, snap.Conversations[0].State);
            Assert.Equal(ConversationState.Unread, snap.Conversations[1].State);
            Assert.Equal(ConversationState.Running, snap.Conversations[2].State);
            Assert.Equal(ConversationState.Idle, snap.Conversations[3].State);
            // A state word this build doesn't know degrades to idle — never to a guess.
            Assert.Equal(ConversationState.Idle, snap.Conversations[4].State);
        }

        [Fact]
        public void Missing_fields_default_to_absent_not_to_error()
        {
            // A newer C# against an older helper: unknown state reads as "nothing present",
            // which shows idle keys — wrong-but-safe, and honest about what was reported.
            var snap = DesktopSnapshot.Parse("{\"ok\":true,\"surface\":true}");

            Assert.True(snap.SurfaceAvailable);
            Assert.False(snap.ApprovalPresent);
            Assert.False(snap.Attention);
            Assert.Equal("", snap.CardText);
            Assert.Equal("", snap.Mode);
        }
    }
}
