namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Nodes;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// Opt-in live status, end to end against a real settings.json in a temp home (#31): a load never
    /// adds wiring (but upgrades legacy commands already ours); Enable writes exactly our entries and
    /// says so; Disable takes exactly them out and leaves the Off marker; the keys' state follows the
    /// file. These are the facts Logitech QA will retest by hand — pinned here first.
    /// </summary>
    public class LiveStatusRoundTripTests
    {
        private static readonly String[] Events = { "UserPromptSubmit", "PostToolUse", "Notification", "Stop", "PermissionRequest" };

        private sealed class Rig : IDisposable
        {
            public TempHome Home { get; } = new TempHome();
            public BridgeManager Bridge { get; }
            public List<(PluginStatus Status, String Message)> Cards { get; } = new();
            public List<LiveStatusState> States { get; } = new();

            public Rig()
            {
                this.Bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
                this.Bridge.Notify = (status, message, url, title) => { if (message != null) { this.Cards.Add((status, message)); } };   // a null message is the load-time clear, not a card
                this.Bridge.OnLiveStatusChanged += s => this.States.Add(s);
            }

            public JsonObject Settings() => JsonNode.Parse(this.Home.ReadSettings()).AsObject();

            public void Dispose() => this.Home.Dispose();
        }

        private static Int32 OurHooks(JsonObject root) =>
            root["hooks"] is JsonObject hooks
                ? hooks.Sum(kv => kv.Value is JsonArray entries
                    ? entries.Sum(e => e?["hooks"] is JsonArray inner ? inner.Count(h => BridgeWiring.IsOurHook(BridgeWiring.Str(h?["command"]))) : 0)
                    : 0)
                : 0;

        [Fact]
        public void A_load_installs_scripts_but_writes_nothing_to_settings_json()
        {
            using var rig = new Rig();
            rig.Home.WriteSettings("{\"model\":\"opus\"}");

            rig.Bridge.RunLoadWiringForTests();

            Assert.Equal("{\"model\":\"opus\"}", rig.Home.ReadSettings());
            Assert.False(File.Exists(rig.Home.Backup));
            Assert.True(Directory.Exists(Path.Combine(rig.Home.RuntimeHome, "scripts")), "the scripts directory is the plugin's own; it is created on load");
            Assert.Equal(LiveStatusState.NotEnabled, rig.Bridge.LiveStatus);
            Assert.Empty(rig.Cards);
        }

        [Fact]
        public void A_load_with_no_settings_file_at_all_creates_none()
        {
            using var rig = new Rig();

            rig.Bridge.RunLoadWiringForTests();

            Assert.False(File.Exists(rig.Home.Settings));
            Assert.Equal(LiveStatusState.NotEnabled, rig.Bridge.LiveStatus);
        }

        [Fact]
        public void Enable_writes_exactly_our_entries_backs_up_and_says_so_once()
        {
            using var rig = new Rig();
            rig.Home.WriteSettings("{\"model\":\"opus\",\"permissions\":{\"allow\":[\"Bash\"]}}");
            rig.Bridge.RunLoadWiringForTests();

            Assert.True(rig.Bridge.EnableLiveStatus());

            var root = rig.Settings();
            Assert.Equal("opus", root["model"].GetValue<String>());
            Assert.Equal("Bash", root["permissions"]["allow"][0].GetValue<String>());
            Assert.Equal(Events, ((JsonObject)root["hooks"]).Select(kv => kv.Key).ToArray());
            Assert.Equal(5, OurHooks(root));
            Assert.True(BridgeWiring.IsOurs(root["statusLine"]["command"].GetValue<String>()));
            Assert.Equal(LiveStatusWiring.Enabled, BridgeWiring.Inspect(root));

            Assert.Equal("{\"model\":\"opus\",\"permissions\":{\"allow\":[\"Bash\"]}}", File.ReadAllText(rig.Home.Backup));
            Assert.Equal(LiveStatusState.JustEnabled, rig.Bridge.LiveStatus);
            var card = Assert.Single(rig.Cards);
            Assert.Equal(PluginStatus.Warning, card.Status);
            Assert.Equal(BridgeNotice.Wired(5, settingsApplyLive: true), card.Message);
            Assert.Empty(rig.Home.LeftoverTemps());
        }

        [Fact]
        public void Enable_clears_the_Off_marker()
        {
            using var rig = new Rig();
            Directory.CreateDirectory(rig.Home.RuntimeHome);
            File.WriteAllText(rig.Home.Marker, String.Empty);
            rig.Bridge.RunLoadWiringForTests();
            Assert.Equal(LiveStatusState.Off, rig.Bridge.LiveStatus);

            Assert.True(rig.Bridge.EnableLiveStatus());

            Assert.False(File.Exists(rig.Home.Marker));
            Assert.Equal(LiveStatusState.JustEnabled, rig.Bridge.LiveStatus);
        }

        [Fact]
        public void Enable_on_an_already_enabled_file_writes_nothing_and_posts_nothing()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());
            var written = rig.Home.ReadSettings();
            var stamp = File.GetLastWriteTimeUtc(rig.Home.Settings);
            rig.Cards.Clear();

            Assert.True(rig.Bridge.EnableLiveStatus());

            Assert.Equal(written, rig.Home.ReadSettings());
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(rig.Home.Settings));
            Assert.Empty(rig.Cards);
        }

        [Fact]
        public void Enable_from_needs_repair_completes_the_set_and_keeps_what_was_there()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());

            // A hand edit removes two events; the keys read "Set up" again.
            var root = rig.Settings();
            ((JsonObject)root["hooks"]).Remove("Stop");
            ((JsonObject)root["hooks"]).Remove("PermissionRequest");
            root["mine"] = "kept";
            rig.Home.WriteSettings(root.ToJsonString());
            rig.Bridge.CheckSettingsMovedForTests();
            Assert.Equal(LiveStatusState.NeedsRepair, rig.Bridge.LiveStatus);
            rig.Cards.Clear();

            Assert.True(rig.Bridge.EnableLiveStatus());

            var repaired = rig.Settings();
            Assert.Equal(5, OurHooks(repaired));
            Assert.Equal("kept", repaired["mine"].GetValue<String>());
            Assert.Equal(LiveStatusState.JustEnabled, rig.Bridge.LiveStatus);
            Assert.Single(rig.Cards);
        }

        [Fact]
        public void Enable_chains_a_users_status_line_and_records_it()
        {
            using var rig = new Rig();
            rig.Home.WriteSettings("{\"statusLine\":{\"type\":\"command\",\"command\":\"starship prompt\",\"padding\":0}}");
            rig.Bridge.RunLoadWiringForTests();

            Assert.True(rig.Bridge.EnableLiveStatus());

            var sl = (JsonObject)rig.Settings()["statusLine"];
            Assert.True(BridgeWiring.IsOurs(sl["command"].GetValue<String>()));
            Assert.Equal(0, sl["padding"].GetValue<Int32>());   // their sibling keys survive
            Assert.Equal("starship prompt", File.ReadAllText(rig.Home.ChainFile));
        }

        [Fact]
        public void Disable_removes_ours_restores_the_chained_status_line_sets_the_marker_and_says_so()
        {
            using var rig = new Rig();
            rig.Home.WriteSettings("{\"model\":\"opus\",\"statusLine\":{\"type\":\"command\",\"command\":\"starship prompt\"}}");
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());
            rig.Cards.Clear();

            Assert.True(rig.Bridge.DisableLiveStatus());

            var root = rig.Settings();
            Assert.Equal("opus", root["model"].GetValue<String>());
            Assert.Equal("starship prompt", root["statusLine"]["command"].GetValue<String>());
            Assert.Null(root["hooks"]);
            Assert.Equal(0, OurHooks(root));
            Assert.True(File.Exists(rig.Home.Marker));
            Assert.False(File.Exists(rig.Home.ChainFile));
            Assert.Equal(LiveStatusState.Off, rig.Bridge.LiveStatus);
            var card = Assert.Single(rig.Cards);
            Assert.Equal(PluginStatus.Warning, card.Status);
            Assert.Equal(BridgeNotice.Unwired(), card.Message);
        }

        [Fact]
        public void Disable_leaves_a_status_line_that_is_no_longer_ours_alone()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());

            // The user replaced our status line by hand after enabling.
            var root = rig.Settings();
            root["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = "starship prompt" };
            rig.Home.WriteSettings(root.ToJsonString());

            Assert.True(rig.Bridge.DisableLiveStatus());

            Assert.Equal("starship prompt", rig.Settings()["statusLine"]["command"].GetValue<String>());
            Assert.Equal(0, OurHooks(rig.Settings()));
        }

        [Fact]
        public void Disable_with_nothing_wired_still_sets_the_marker_and_posts_nothing()
        {
            using var rig = new Rig();
            rig.Home.WriteSettings("{\"model\":\"opus\"}");
            rig.Bridge.RunLoadWiringForTests();

            Assert.True(rig.Bridge.DisableLiveStatus());

            Assert.Equal("{\"model\":\"opus\"}", rig.Home.ReadSettings());
            Assert.True(File.Exists(rig.Home.Marker));
            Assert.Equal(LiveStatusState.Off, rig.Bridge.LiveStatus);
            Assert.Empty(rig.Cards);
        }

        [Fact]
        public void An_existing_wired_install_loads_Enabled_with_no_card()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());
            var wired = rig.Home.ReadSettings();

            // A fresh engine, as after a plugin reload or an upgrade from 2.1.0.
            using var again = new Rig2(rig.Home);
            again.Bridge.RunLoadWiringForTests();

            Assert.Equal(LiveStatusState.Enabled, again.Bridge.LiveStatus);
            Assert.Equal(wired, rig.Home.ReadSettings());
            Assert.Empty(again.Cards);
        }

        [Fact]
        public void A_load_migrates_only_legacy_owned_commands_and_backs_up_the_old_form()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());

            // Recreate the unguarded form written by 2.2.0, plus a user's hook beside ours. A
            // plugin update must make already-owned entries safe when their handler disappears,
            // but must not treat that migration as permission to add or rewrite anything else.
            var root = rig.Settings();
            var statusHandler = rig.Bridge.BridgeHandlerPath(null);
            var activityHandler = rig.Bridge.BridgeHandlerPath("busy");
            root["statusLine"]["command"] = $"bash \"{statusHandler}\"";
            foreach (var spec in BridgeWiring.HookSpecs)
            {
                var inner = root["hooks"][spec.Event][0]["hooks"].AsArray();
                inner[0]["command"] = $"bash \"{activityHandler}\" {spec.State}";
            }
            root["hooks"]["Stop"][0]["hooks"].AsArray().Add(
                new JsonObject { ["type"] = "command", ["command"] = "my-stop-handler" });
            rig.Home.WriteSettings(root.ToJsonString());
            var legacy = rig.Home.ReadSettings();

            using var again = new Rig2(rig.Home);
            again.Bridge.RunLoadWiringForTests();

            var migrated = rig.Settings();
            Assert.Equal(
                BridgeWiring.StatuslineCommand(false, statusHandler),
                migrated["statusLine"]["command"].GetValue<String>());
            foreach (var spec in BridgeWiring.HookSpecs)
            {
                Assert.Equal(
                    BridgeWiring.ActivityCommand(false, activityHandler, spec.State),
                    migrated["hooks"][spec.Event][0]["hooks"][0]["command"].GetValue<String>());
            }
            Assert.Equal("my-stop-handler", migrated["hooks"]["Stop"][0]["hooks"][1]["command"].GetValue<String>());
            Assert.Equal(legacy, File.ReadAllText(rig.Home.Backup));
            Assert.Equal(LiveStatusState.Enabled, again.Bridge.LiveStatus);
            Assert.Empty(again.Cards);
        }

        [Fact]
        public void A_marker_created_by_hand_while_wired_unwires_on_the_next_load()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());
            File.WriteAllText(rig.Home.Marker, String.Empty);

            using var again = new Rig2(rig.Home);
            again.Bridge.RunLoadWiringForTests();

            Assert.Equal(0, OurHooks(rig.Settings()));
            Assert.Equal(LiveStatusState.Off, again.Bridge.LiveStatus);
            var card = Assert.Single(again.Cards);
            Assert.Equal(BridgeNotice.Unwired(), card.Message);
        }

        [Fact]
        public void Just_enabled_becomes_enabled_when_the_first_state_arrives()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());
            Assert.Equal(LiveStatusState.JustEnabled, rig.Bridge.LiveStatus);

            rig.Bridge.NoteStateArrivedForTests();

            Assert.Equal(LiveStatusState.Enabled, rig.Bridge.LiveStatus);
        }

        [Fact]
        public void An_edit_made_outside_the_plugin_reaches_the_keys_without_a_reload()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            Assert.True(rig.Bridge.EnableLiveStatus());
            rig.Bridge.NoteStateArrivedForTests();
            Assert.Equal(LiveStatusState.Enabled, rig.Bridge.LiveStatus);

            // uninstall.sh --unwire, or an editor: our entries vanish and the marker appears.
            rig.Home.WriteSettings("{\"model\":\"opus\"}");
            File.WriteAllText(rig.Home.Marker, String.Empty);
            rig.Bridge.CheckSettingsMovedForTests();

            Assert.Equal(LiveStatusState.Off, rig.Bridge.LiveStatus);
        }

        [Fact]
        public void The_state_event_fires_only_on_change()
        {
            using var rig = new Rig();
            rig.Bridge.RunLoadWiringForTests();
            rig.Bridge.RunLoadWiringForTests();
            rig.Bridge.CheckSettingsMovedForTests();

            Assert.Empty(rig.States);   // NotEnabled is the starting state; nothing moved

            Assert.True(rig.Bridge.EnableLiveStatus());
            Assert.True(rig.Bridge.EnableLiveStatus());

            Assert.Equal(new[] { LiveStatusState.JustEnabled }, rig.States);
        }

        // A second engine over the same home, for "the plugin was reloaded" cases.
        private sealed class Rig2 : IDisposable
        {
            public BridgeManager Bridge { get; }
            public List<(PluginStatus Status, String Message)> Cards { get; } = new();

            public Rig2(TempHome home)
            {
                BridgeManager.HomeOverride = home.Dir;
                this.Bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
                this.Bridge.Notify = (status, message, url, title) => { if (message != null) { this.Cards.Add((status, message)); } };   // a null message is the load-time clear, not a card
            }

            public void Dispose() { }
        }
    }
}
