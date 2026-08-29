namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    using Loupedeck.ClaudeConsolePlugin.Agents;
    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// The user-facing half of opt-in live status (#31): which agents get the Enable / Disable keys,
    /// what Enable promises before the press, that Disable can only ever disable, and what a live
    /// key does while setup is owed — the two-step press: arm and disclose, then enable; never on
    /// one press, never on a late one. A PluginDynamicCommand cannot be constructed outside the
    /// host, so the gate is tested directly and the commands by their source.
    /// </summary>
    public class LiveStatusActionsTests
    {
        [Fact]
        public void Only_an_agent_that_reads_hooks_from_the_users_settings_file_gets_the_keys()
        {
            Assert.True(new ClaudeCodeAdapter().Capabilities.SettingsFileWiring);
            Assert.False(new CodexCliAdapter().Capabilities.SettingsFileWiring);   // ~/.codex/hooks.json, behind Codex's own trust prompt
            Assert.False(new NoAgentAdapter().Capabilities.SettingsFileWiring);

            foreach (var file in new[] { "EnableLiveStatusCommand.cs", "DisableLiveStatusCommand.cs" })
            {
                var source = File.ReadAllText(RepoFile("src", "Core", "Actions", file));
                var gate = source.IndexOf("Capabilities.SettingsFileWiring", StringComparison.Ordinal);
                var add = source.IndexOf("this.AddParameter(", StringComparison.Ordinal);
                Assert.True(gate > 0 && add > gate, $"{file} must check SettingsFileWiring before adding its key");
            }
        }

        [Fact]
        public void Disable_can_only_ever_disable()
        {
            var source = File.ReadAllText(RepoFile("src", "Core", "Actions", "DisableLiveStatusCommand.cs"));
            Assert.Contains("DisableLiveStatus()", source);
            Assert.DoesNotContain("EnableLiveStatus(", source);
        }

        [Fact]
        public void Enable_says_what_it_will_change_before_the_press()
        {
            // The description is the only readable text the Options+ editor shows (the spike found
            // Label/Hyperlink controls render as hover icons), so it carries the disclosure.
            var source = File.ReadAllText(RepoFile("src", "Core", "Actions", "EnableLiveStatusCommand.cs"));
            Assert.Contains("5 hooks", source);
            Assert.Contains("status line", source);
            Assert.Contains("~/.claude/settings.json", source);
            Assert.Contains("backs the", source);
            Assert.Contains("Chains a status line", source);
            Assert.Contains("next Claude Code session", source);
        }

        [Fact]
        public void The_three_live_keys_go_through_the_gate_before_acting()
        {
            foreach (var file in new[] { "CostDisplayCommand.cs", "ContextCommand.cs", "StatusCommand.cs" })
            {
                var source = File.ReadAllText(RepoFile("src", "Core", "Actions", file));
                Assert.Contains("new LiveStatusGate(", source);
                var run = source.IndexOf("protected override void RunCommand(", StringComparison.Ordinal);
                var press = source.IndexOf("_gate.Press()", StringComparison.Ordinal);
                Assert.True(run > 0 && press > run, $"{file} must hand the press to the gate first thing in RunCommand");
                Assert.Contains("_gate.Label", source);
            }
        }

        private sealed class Rig : IDisposable
        {
            public TempHome Home { get; } = new TempHome();
            public BridgeManager Bridge { get; }
            public List<(PluginStatus Status, String Message)> Cards { get; } = new();
            public Int32 Repaints;
            public LiveStatusGate Gate { get; }

            public Rig(Int32 armWindowMs = 10_000, String key = "Cost")
            {
                this.Bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
                this.Bridge.Notify = (status, message, url, title) => { if (message != null) { this.Cards.Add((status, message)); } };
                this.Gate = new LiveStatusGate(this.Bridge, key, () => Interlocked.Increment(ref this.Repaints), armWindowMs);
            }

            public LiveStatusGate AnotherKey(String key) =>
                new LiveStatusGate(this.Bridge, key, () => Interlocked.Increment(ref this.Repaints));

            public void Dispose() => this.Home.Dispose();
        }

        [Fact]
        public void The_first_press_arms_flashes_discloses_and_changes_nothing()
        {
            using var rig = new Rig();
            rig.Home.WriteSettings("{\"model\":\"opus\"}");
            rig.Bridge.RunLoadWiringForTests();
            Assert.Equal("Set up", rig.Gate.Label);

            var consumed = rig.Gate.Press();

            Assert.True(consumed);
            Assert.True(rig.Gate.Armed);
            Assert.Equal(LiveStatusFace.PressHint, rig.Gate.Label);          // "Press again", while the window is open
            Assert.Equal("{\"model\":\"opus\"}", rig.Home.ReadSettings());  // nothing edited
            Assert.False(File.Exists(rig.Home.Backup));
            Assert.Equal(LiveStatusState.NotEnabled, rig.Bridge.LiveStatus);
            var card = Assert.Single(rig.Cards);
            Assert.Equal(PluginStatus.Warning, card.Status);
            Assert.Equal(BridgeNotice.PressAgain("Cost", 10), card.Message);   // the prompt, with the change in it
        }

        [Fact]
        public void A_second_press_inside_the_window_enables()
        {
            using var rig = new Rig();
            rig.Home.WriteSettings("{\"model\":\"opus\"}");
            rig.Bridge.RunLoadWiringForTests();

            Assert.True(rig.Gate.Press());
            Assert.True(rig.Gate.Press());

            Assert.Equal(LiveStatusState.JustEnabled, rig.Bridge.LiveStatus);
            Assert.Equal(LiveStatusWiring.Enabled, BridgeWiring.Inspect(System.Text.Json.Nodes.JsonNode.Parse(rig.Home.ReadSettings()).AsObject()));
            Assert.False(rig.Gate.Armed);
            Assert.Equal("Restart Claude", rig.Gate.Label);
            Assert.Equal(2, rig.Cards.Count);
            Assert.Equal(BridgeNotice.PressAgain("Cost", 10), rig.Cards[0].Message);
            Assert.Equal(BridgeNotice.Wired(5), rig.Cards[1].Message);
        }

        [Fact]
        public void A_late_second_press_only_arms_again()
        {
            using var rig = new Rig(armWindowMs: 40);
            rig.Home.WriteSettings("{\"model\":\"opus\"}");
            rig.Bridge.RunLoadWiringForTests();

            Assert.True(rig.Gate.Press());
            Thread.Sleep(250);
            Assert.False(rig.Gate.Armed);
            Assert.Equal("Set up", rig.Gate.Label);          // the flash cleared with the window
            Assert.True(rig.Gate.Press());

            Assert.Equal(LiveStatusState.NotEnabled, rig.Bridge.LiveStatus);
            Assert.Equal("{\"model\":\"opus\"}", rig.Home.ReadSettings());
            Assert.Equal(2, rig.Cards.Count);                 // two first presses, two prompts, no write
            Assert.True(rig.Cards.TrueForAll(c => c.Message == BridgeNotice.PressAgain("Cost", 0)));
        }

        [Fact]
        public void Arming_is_per_key_so_two_different_keys_never_add_up_to_consent()
        {
            using var rig = new Rig();
            rig.Home.WriteSettings("{\"model\":\"opus\"}");
            rig.Bridge.RunLoadWiringForTests();
            var context = rig.AnotherKey("Context");

            Assert.True(rig.Gate.Press());      // Cost, armed
            Assert.True(context.Press());       // Context, armed — not Cost's second press

            Assert.Equal(LiveStatusState.NotEnabled, rig.Bridge.LiveStatus);
            Assert.Equal("{\"model\":\"opus\"}", rig.Home.ReadSettings());
            Assert.Contains("Press Context again", rig.Cards[1].Message);
        }

        [Fact]
        public void A_press_once_enabled_is_the_keys_own_and_the_key_shows_its_own_value()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());
            Assert.Equal("Restart Claude", rig.Gate.Label);   // wired, no session has reported yet
            Assert.False(rig.Gate.NeedsSetup);

            rig.Bridge.NoteStateArrivedForTests();

            Assert.Null(rig.Gate.Label);
            Assert.False(rig.Gate.Press());
            Assert.DoesNotContain(rig.Cards, c => c.Message.StartsWith("Press ", StringComparison.Ordinal));
        }

        [Fact]
        public void After_disable_the_key_reads_Off_and_two_presses_turn_it_back_on()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());
            Assert.True(rig.Bridge.DisableLiveStatus());
            Assert.Equal("Off", rig.Gate.Label);

            Assert.True(rig.Gate.Press());
            Assert.True(rig.Gate.Press());

            Assert.Equal(LiveStatusState.JustEnabled, rig.Bridge.LiveStatus);
            Assert.False(File.Exists(rig.Home.Marker));
        }

        [Fact]
        public void The_gate_repaints_only_when_its_words_change()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();          // NotEnabled: "Set up" — the gate started there, no repaint
            rig.Bridge.RunLoadWiringForTests();
            Assert.Equal(0, rig.Repaints);

            Assert.True(rig.Bridge.DisableLiveStatus()); // NotEnabled -> Off: "Set up" -> "Off"
            Assert.Equal(1, rig.Repaints);

            Assert.True(rig.Bridge.DisableLiveStatus()); // still Off
            Assert.Equal(1, rig.Repaints);

            Assert.True(rig.Bridge.EnableLiveStatus());  // Off -> JustEnabled: "Restart Claude"
            Assert.Equal(2, rig.Repaints);
        }

        private static String RepoFile(params String[] relative)
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir, Path.Combine(relative));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                dir = Path.GetDirectoryName(dir);
            }
            throw new InvalidOperationException("could not locate " + Path.Combine(relative));
        }
    }
}
