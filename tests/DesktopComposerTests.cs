namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;

    public class DesktopComposerTests
    {
        [Theory]
        [InlineData("{\"ok\":true,\"sent\":true}", true, null)]
        [InlineData("{\"ok\":false,\"error\":\"composer-target-changed\"}", false, "composer-target-changed")]
        [InlineData("{\"ok\":false,\"error\":\"no-sendable-draft\"}", false, "no-sendable-draft")]
        public void Sending_is_a_single_guarded_verb_without_replacing_text(String reply, Boolean success, String failure)
        {
            var calls = new List<List<String>>();
            var automation = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            {
                Runner = (args, _) => { calls.Add(args); return reply; },
            };
            Assert.Equal(success, automation.SendComposer(out var error));
            Assert.Equal(failure, error);
            var call = Assert.Single(calls);
            Assert.Equal("send", call[0]);
            Assert.Contains("--send-label", call);
            Assert.Contains("--stop", call);
            Assert.Contains("--approve", call);
            Assert.DoesNotContain("--text", call);
        }

        [Fact]
        public void Unsupported_copy_never_uses_generic_press()
        {
            IDesktopAutomation automation = new MacDesktopAutomation(new OpenAiDesktopAdapter())
            {
                Runner = (_, _) => throw new InvalidOperationException("Must not guess a copy selector"),
            };
            Assert.False(automation.CopyAnswer(out var error));
            Assert.Equal("unsupported", error);
        }

        [Theory]
        [InlineData(false, false, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        public void Busy_or_approval_state_disables_submission_and_copy(Boolean busy, Boolean approval, Boolean enabled)
        {
            var state = DesktopMonitor.Map(new DesktopSnapshot
            {
                SurfaceAvailable = true, StopPresent = busy, ApprovalPresent = approval,
                CanSend = true, CanCopyAnswer = true, Mode = "ChatGPT",
            });
            Assert.Equal(enabled, state.CanSend);
            Assert.Equal(enabled, state.CanCopyAnswer);
            Assert.Equal(enabled ? "Send" : "No Draft", DesktopComposerCommand.LabelFor("send", state));
            Assert.Equal(enabled ? "Copy Answer" : "No Answer", DesktopComposerCommand.LabelFor("output", state));
        }

        [Fact]
        public void Snapshot_absent_flags_fail_closed_and_present_flags_parse()
        {
            var missing = DesktopSnapshot.Parse("{\"ok\":true,\"surface\":true}");
            Assert.False(missing.CanSend);
            Assert.False(missing.CanCopyAnswer);
            var present = DesktopSnapshot.Parse("{\"ok\":true,\"surface\":true,\"canSend\":true,\"canCopyAnswer\":true}");
            Assert.True(present.CanSend);
            Assert.True(present.CanCopyAnswer);
        }

        [Fact]
        public void Output_is_mode_aware_and_unknown_mode_does_not_guess()
        {
            Assert.Equal("Changes", DesktopComposerCommand.LabelFor("output", DesktopMonitor.Map(new DesktopSnapshot
            { SurfaceAvailable = true, Mode = "Codex", AvailableControls = DesktopControl.Changes })));
            Assert.Equal("No Changes", DesktopComposerCommand.LabelFor("output", DesktopMonitor.Map(new DesktopSnapshot
            { SurfaceAvailable = true, Mode = "Codex" })));
            Assert.Equal("Mode?", DesktopComposerCommand.LabelFor("output", DesktopMonitor.Map(new DesktopSnapshot
            { SurfaceAvailable = true })));
            Assert.Equal("Unavailable", DesktopComposerCommand.LabelFor("send", DesktopState.Unavailable));
        }
    }
}
