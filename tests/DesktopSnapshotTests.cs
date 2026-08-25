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
