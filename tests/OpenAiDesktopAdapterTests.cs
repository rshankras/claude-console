namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    using Xunit;

    /// <summary>
    /// The OpenAI desktop app's control map. Every string here is UI copy verified on the live
    /// app (2026-08-24 sitting); these tests pin the map so a "harmless" edit can't silently
    /// unbind a key — and pin the one policy decision that must never regress by accident:
    /// no label anywhere grants standing permission.
    /// </summary>
    public class OpenAiDesktopAdapterTests
    {
        private readonly OpenAiDesktopAdapter _app = new OpenAiDesktopAdapter();

        [Fact]
        public void The_identity_is_the_real_app()
        {
            // Yes: the ChatGPT app's bundle id names Codex. Verified on disk, not guessed.
            Assert.Equal("com.openai.codex", _app.BundleId);
            Assert.Equal("ChatGPT", _app.MacProcessName);
        }

        [Fact]
        public void The_card_labels_are_the_ones_seen_on_the_live_card()
        {
            Assert.Contains("Allow once", _app.ApproveLabels);
            Assert.Contains("Deny", _app.DenyLabels);
        }

        [Fact]
        public void No_label_grants_standing_permission()
        {
            // "Always allow" exists in the app, behind the Approval options popup. It is policy,
            // not oversight, that no keypad key ever carries it — standing permission should not
            // be one elbow away on hardware.
            var all = _app.ApproveLabels
                .Concat(_app.DenyLabels)
                .Concat(_app.StopLabels)
                .Concat(new[] { _app.SendLabel, _app.NewChatLabel, _app.ModeSwitcherLabel })
                .Concat(_app.ModeNames.Select(_app.ModeMenuLabel));

            Assert.DoesNotContain(all, l => l != null && l.Contains("Always", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Mode_menu_labels_are_distinctive_not_bare_mode_words()
        {
            // The bare word "Codex" would also match the switcher's own label ("Switch mode,
            // current mode: Codex") and re-open the menu instead of picking from it.
            foreach (var mode in _app.ModeNames)
            {
                var label = _app.ModeMenuLabel(mode);
                Assert.NotNull(label);
                Assert.True(label.Length > mode.Length, $"menu label for {mode} is not distinctive: {label}");
            }
        }

        [Fact]
        public void An_unknown_mode_has_no_menu_label()
        {
            Assert.Null(_app.ModeMenuLabel("Claude"));
        }

        [Fact]
        public void The_attention_marker_is_a_fragment_of_the_relabel_not_the_whole_control_name()
        {
            // The signal is "View activity" RELABELLING to "View activity, needs attention" —
            // matching the suffix keeps the idle label from reading as attention.
            Assert.Equal("needs attention", _app.AttentionMarker);
            Assert.DoesNotContain("View activity", _app.AttentionMarker);
        }

        [Fact]
        public void Every_spike_proven_capability_is_declared_and_nothing_more()
        {
            var caps = _app.Capabilities;

            Assert.True(caps.ApprovalSignal);
            Assert.True(caps.Attention);
            Assert.True(caps.Stop);
            Assert.True(caps.ModeSwitch);
            Assert.True(caps.ComposerWrite);
        }
    }
}
