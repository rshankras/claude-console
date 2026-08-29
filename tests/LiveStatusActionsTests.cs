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
    /// The user-facing half of opt-in live status (#31): the live keys are the switch. A short press
    /// before setup arms and discloses, a second press enables; a long press once on asks the mirror
    /// question, a second long press disables; a short press once on is the key's own job, and runs
    /// on release. Inert where the agent has no settings file (Codex). A PluginDynamicCommand cannot
    /// be constructed outside the host, so the gate is tested directly and the keys by their source.
    /// </summary>
    public class LiveStatusActionsTests
    {
        [Fact]
        public void Only_an_agent_that_reads_hooks_from_the_users_settings_file_has_a_switch()
        {
            Assert.True(new ClaudeCodeAdapter().Capabilities.SettingsFileWiring);
            Assert.False(new CodexCliAdapter().Capabilities.SettingsFileWiring);   // ~/.codex/hooks.json, behind Codex's own trust prompt
            Assert.False(new NoAgentAdapter().Capabilities.SettingsFileWiring);
        }

        [Fact]
        public void The_three_live_keys_own_their_button_and_route_it_through_the_gate()
        {
            foreach (var file in new[] { "CostDisplayCommand.cs", "ContextCommand.cs", "StatusCommand.cs" })
            {
                var source = File.ReadAllText(RepoFile("src", "Core", "Actions", file));
                Assert.Contains("new LiveStatusGate(", source);
                Assert.Contains("protected override Boolean ProcessButtonEvent2(", source);
                Assert.Contains("_gate.HandleButton(buttonEvent.EventType, () => this.RunCommand(actionParameter))", source);
                Assert.DoesNotContain("_gate.Press()", source);   // the gate decides; the key never asks twice
                Assert.Contains("_gate.Label", source);
            }
        }

        [Fact]
        public void There_are_no_labelled_switch_keys_any_more()
        {
            Assert.False(File.Exists(RepoFile("src", "Core", "Actions", "CostDisplayCommand.cs").Replace("CostDisplayCommand.cs", "EnableLiveStatusCommand.cs")));
            Assert.False(File.Exists(RepoFile("src", "Core", "Actions", "CostDisplayCommand.cs").Replace("CostDisplayCommand.cs", "DisableLiveStatusCommand.cs")));
        }

        private sealed class Rig : IDisposable
        {
            public TempHome Home { get; } = new TempHome();
            public BridgeManager Bridge { get; }
            public List<(PluginStatus Status, String Message)> Cards { get; } = new();
            public Int32 Repaints;
            public Int32 ShortPresses;
            public LiveStatusGate Gate { get; }

            public Rig(Int32 armWindowMs = 10_000, String key = "Cost", Boolean applies = true)
            {
                this.Bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
                this.Bridge.Notify = (status, message, url, title) => { if (message != null) { this.Cards.Add((status, message)); } };
                this.Gate = new LiveStatusGate(this.Bridge, key, () => Interlocked.Increment(ref this.Repaints), armWindowMs, applies);
            }

            public LiveStatusGate AnotherKey(String key) =>
                new LiveStatusGate(this.Bridge, key, () => Interlocked.Increment(ref this.Repaints), applies: true);

            public void Tap() { this.Gate.HandleButton(DeviceButtonEventType.Press, this.Short); this.Gate.HandleButton(DeviceButtonEventType.Release, this.Short); }

            public void Hold()
            {
                this.Gate.HandleButton(DeviceButtonEventType.Press, this.Short);
                this.Gate.HandleButton(DeviceButtonEventType.LongPress, this.Short);
                this.Gate.HandleButton(DeviceButtonEventType.RepeatPress, this.Short);
                this.Gate.HandleButton(DeviceButtonEventType.Release, this.Short);
            }

            private void Short() => Interlocked.Increment(ref this.ShortPresses);

            public void Dispose() => this.Home.Dispose();
        }

        // ----- turning on: short press --------------------------------------------------------

        [Fact]
        public void The_first_press_arms_flashes_discloses_and_changes_nothing()
        {
            using var rig = new Rig();
            rig.Home.WriteSettings("{\"model\":\"opus\"}");
            rig.Bridge.RunLoadWiringForTests();
            Assert.Equal("Set up", rig.Gate.Label);

            rig.Tap();

            Assert.True(rig.Gate.Armed);
            Assert.Equal(LiveStatusFace.PressHint, rig.Gate.Label);          // "Press again", while the window is open
            Assert.Equal("{\"model\":\"opus\"}", rig.Home.ReadSettings());  // nothing edited
            Assert.False(File.Exists(rig.Home.Backup));
            Assert.Equal(0, rig.ShortPresses);                               // the key's own job did not run
            var card = Assert.Single(rig.Cards);
            Assert.Equal(PluginStatus.Warning, card.Status);
            Assert.Equal(BridgeNotice.PressAgain("Cost", 10), card.Message);
        }

        [Fact]
        public void A_second_press_inside_the_window_enables()
        {
            using var rig = new Rig();
            rig.Home.WriteSettings("{\"model\":\"opus\"}");
            rig.Bridge.RunLoadWiringForTests();

            rig.Tap();
            rig.Tap();

            Assert.Equal(LiveStatusState.JustEnabled, rig.Bridge.LiveStatus);
            Assert.Equal(LiveStatusWiring.Enabled, BridgeWiring.Inspect(System.Text.Json.Nodes.JsonNode.Parse(rig.Home.ReadSettings()).AsObject()));
            Assert.False(rig.Gate.Armed);
            Assert.Equal("Restart Claude", rig.Gate.Label);
            Assert.Equal(0, rig.ShortPresses);
            Assert.Equal(BridgeNotice.Wired(5), rig.Cards[1].Message);
        }

        [Fact]
        public void A_late_second_press_only_arms_again()
        {
            using var rig = new Rig(armWindowMs: 40);
            rig.Home.WriteSettings("{\"model\":\"opus\"}");
            rig.Bridge.RunLoadWiringForTests();

            rig.Tap();
            Thread.Sleep(250);
            Assert.False(rig.Gate.Armed);
            Assert.Equal("Set up", rig.Gate.Label);          // the flash cleared with the window
            rig.Tap();

            Assert.Equal(LiveStatusState.NotEnabled, rig.Bridge.LiveStatus);
            Assert.Equal("{\"model\":\"opus\"}", rig.Home.ReadSettings());
            Assert.Equal(2, rig.Cards.Count);
        }

        [Fact]
        public void Arming_is_per_key_so_two_different_keys_never_add_up_to_consent()
        {
            using var rig = new Rig();
            rig.Home.WriteSettings("{\"model\":\"opus\"}");
            rig.Bridge.RunLoadWiringForTests();
            var context = rig.AnotherKey("Context");

            rig.Tap();                          // Cost, armed
            Assert.True(context.Press());       // Context, armed — not Cost's second press

            Assert.Equal(LiveStatusState.NotEnabled, rig.Bridge.LiveStatus);
            Assert.Contains("Press Context again", rig.Cards[1].Message);
        }

        [Fact]
        public void A_long_press_before_setup_is_just_a_first_press()
        {
            using var rig = new Rig();
            rig.Home.WriteSettings("{\"model\":\"opus\"}");
            rig.Bridge.RunLoadWiringForTests();

            rig.Hold();

            Assert.True(rig.Gate.Armed);
            Assert.False(rig.Gate.OffArmed);
            Assert.Equal(LiveStatusState.NotEnabled, rig.Bridge.LiveStatus);
            Assert.Equal(0, rig.ShortPresses);
        }

        // ----- once on: short press is the key's own, long press is the way off ----------------

        [Fact]
        public void Once_on_a_short_press_runs_the_keys_own_job_on_release()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());
            rig.Bridge.NoteStateArrivedForTests();
            Assert.Null(rig.Gate.Label);
            rig.Cards.Clear();   // the Enable card is not what this test is about

            rig.Gate.HandleButton(DeviceButtonEventType.Press, () => Interlocked.Increment(ref rig.ShortPresses));
            Assert.Equal(0, rig.ShortPresses);   // not on the way down...
            rig.Gate.HandleButton(DeviceButtonEventType.Release, () => Interlocked.Increment(ref rig.ShortPresses));
            Assert.Equal(1, rig.ShortPresses);   // ...on release
            Assert.Empty(rig.Cards);
        }

        [Fact]
        public void Once_on_a_long_press_arms_the_way_off_and_does_not_run_the_keys_job()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());
            rig.Bridge.NoteStateArrivedForTests();
            rig.Cards.Clear();

            rig.Hold();

            Assert.True(rig.Gate.OffArmed);
            Assert.Equal(LiveStatusFace.OffHint, rig.Gate.Label);            // "Turn off?"
            Assert.Equal(0, rig.ShortPresses);                                // no /cost typed on release
            Assert.Equal(LiveStatusState.Enabled, rig.Bridge.LiveStatus);     // nothing removed yet
            var card = Assert.Single(rig.Cards);
            Assert.Equal(BridgeNotice.LongPressAgain("Cost", 10), card.Message);
        }

        [Fact]
        public void A_second_long_press_inside_the_window_turns_it_off()
        {
            using var rig = new Rig();
            rig.Home.WriteSettings("{\"model\":\"opus\"}");
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());
            rig.Bridge.NoteStateArrivedForTests();

            rig.Hold();
            rig.Hold();

            Assert.Equal(LiveStatusState.Off, rig.Bridge.LiveStatus);
            Assert.DoesNotContain("claude-console", rig.Home.ReadSettings());
            Assert.True(File.Exists(rig.Home.Marker));
            Assert.Equal("Off", rig.Gate.Label);
            Assert.Equal(0, rig.ShortPresses);
        }

        [Fact]
        public void A_late_second_long_press_only_arms_again()
        {
            using var rig = new Rig(armWindowMs: 40);
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());
            rig.Bridge.NoteStateArrivedForTests();

            rig.Hold();
            Thread.Sleep(250);
            Assert.False(rig.Gate.OffArmed);
            rig.Hold();

            Assert.Equal(LiveStatusState.Enabled, rig.Bridge.LiveStatus);
            Assert.True(rig.Gate.OffArmed);
        }

        [Fact]
        public void After_off_a_short_press_arms_the_way_back_on()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());
            Assert.True(rig.Bridge.DisableLiveStatus());
            Assert.Equal("Off", rig.Gate.Label);

            rig.Tap();
            rig.Tap();

            Assert.Equal(LiveStatusState.JustEnabled, rig.Bridge.LiveStatus);
            Assert.False(File.Exists(rig.Home.Marker));
        }

        // ----- where there is nothing to consent to -------------------------------------------

        [Fact]
        public void Where_the_agent_has_no_settings_file_the_gate_is_inert()
        {
            using var rig = new Rig(applies: false);
            rig.Bridge.RunLoadWiringForTests();   // the engine still says NotEnabled...

            Assert.Null(rig.Gate.Label);          // ...but the key shows its own value
            Assert.False(rig.Gate.NeedsSetup);
            rig.Tap();
            Assert.Equal(1, rig.ShortPresses);    // the press is the key's
            rig.Hold();
            Assert.False(rig.Gate.OffArmed);
            Assert.Empty(rig.Cards);
            Assert.Equal(0, rig.Repaints);
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
