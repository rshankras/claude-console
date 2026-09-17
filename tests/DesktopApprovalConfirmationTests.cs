namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Xunit;

    public class DesktopApprovalConfirmationTests
    {
        private static DesktopState Request(String card = "rm -rf build", String title = "A", String mode = "Codex") =>
            new DesktopState { Activity = DesktopActivity.WaitingApproval, Risk = ApprovalRisk.High,
                CardText = card, ActiveTitle = title, Mode = mode };

        [Fact]
        public void Same_request_requires_two_presses_and_consumes_confirmation()
        {
            var arm = new DesktopApprovalConfirmation();
            var now = DateTime.UtcNow;
            Assert.False(arm.Confirm("approve", Request(), now));
            Assert.True(arm.Confirm("approve", Request(), now.AddSeconds(1)));
            Assert.False(arm.Confirm("approve", Request(), now.AddSeconds(2)));
        }

        [Theory]
        [InlineData("rm -rf other", "A", "Codex")]
        [InlineData("rm -rf build", "B", "Codex")]
        [InlineData("rm -rf build", "A", "ChatGPT")]
        public void Changed_request_must_be_armed_again(String card, String title, String mode)
        {
            var arm = new DesktopApprovalConfirmation();
            var now = DateTime.UtcNow;
            arm.Confirm("approve", Request(), now);
            var next = Request(card, title, mode);
            arm.Observe(next);
            Assert.False(arm.IsArmed("approve", next, now.AddSeconds(1)));
            Assert.False(arm.Confirm("approve", next, now.AddSeconds(1)));
            Assert.True(arm.Confirm("approve", next, now.AddSeconds(2)));
        }

        [Fact]
        public void Disappearing_request_clears_arm_even_if_identical_text_returns()
        {
            var arm = new DesktopApprovalConfirmation();
            var now = DateTime.UtcNow;
            arm.Confirm("approve", Request(), now);
            arm.Observe(DesktopState.Unavailable);
            Assert.False(arm.Confirm("approve", Request(), now.AddSeconds(1)));
        }

        [Fact]
        public void Expiry_and_changed_action_require_fresh_confirmation()
        {
            var arm = new DesktopApprovalConfirmation();
            var now = DateTime.UtcNow;
            arm.Confirm("approve", Request(), now);
            Assert.False(arm.Confirm("approve", Request(), now.AddSeconds(3)));
            Assert.False(arm.Confirm("deny", Request(), now.AddSeconds(4)));
        }

        [Fact]
        public void Unreadable_card_cannot_be_armed()
        {
            var arm = new DesktopApprovalConfirmation();
            var now = DateTime.UtcNow;
            Assert.False(arm.Confirm("approve", Request(""), now));
            Assert.False(arm.Confirm("approve", Request(""), now.AddSeconds(1)));
        }
    }
}
