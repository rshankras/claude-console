namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Linq;
    using System.Text.Json.Nodes;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// The pure half of opt-in live status (#31): what a settings.json document says about our
    /// wiring (BridgeWiring.Inspect), what the keys show for it (DisplayState / LiveStatusFace),
    /// and the one hook table both the wirer and the detector read.
    /// </summary>
    public class LiveStatusDetectorTests
    {
        private const String Mac = "/Users/me/.claude/claude-console/scripts/activity-hook.sh";
        private const String MacStatus = "/Users/me/.claude/claude-console/scripts/statusline-handler.sh";
        private const String Exe = @"C:\Users\me\AppData\Local\Logi\LogiPluginService\Plugins\ClaudeConsole\bin\claude-console-hook.exe";

        // A document wired the way EnsureBridgeWired wires it, minus whatever the caller strips.
        private static JsonObject Wired(Boolean windows = false, params String[] dropEvents)
        {
            var hooks = new JsonObject();
            foreach (var spec in BridgeWiring.HookSpecs)
            {
                if (dropEvents.Contains(spec.Event))
                {
                    continue;
                }
                var entry = new JsonObject
                {
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "command", ["command"] = BridgeWiring.ActivityCommand(windows, windows ? Exe : Mac, spec.State) },
                    },
                };
                if (spec.Matcher != null)
                {
                    entry["matcher"] = spec.Matcher;
                }
                hooks[spec.Event] = new JsonArray { entry };
            }
            return new JsonObject
            {
                ["model"] = "opus",
                ["hooks"] = hooks,
                ["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = BridgeWiring.StatuslineCommand(windows, windows ? Exe : MacStatus) },
            };
        }

        [Fact]
        public void All_five_hooks_and_our_status_line_is_Enabled()
        {
            Assert.Equal(LiveStatusWiring.Enabled, BridgeWiring.Inspect(Wired()));
        }

        [Fact]
        public void The_windows_shim_counts_as_ours_too()
        {
            Assert.Equal(LiveStatusWiring.Enabled, BridgeWiring.Inspect(Wired(windows: true)));
        }

        [Fact]
        public void An_empty_document_is_Disabled()
        {
            Assert.Equal(LiveStatusWiring.Disabled, BridgeWiring.Inspect(new JsonObject()));
        }

        [Fact]
        public void An_unreadable_document_is_Disabled()
        {
            // null = symlink / invalid JSON / not an object. Nothing to repair, nothing to claim.
            Assert.Equal(LiveStatusWiring.Disabled, BridgeWiring.Inspect(null));
        }

        [Fact]
        public void A_users_own_hooks_and_status_line_are_Disabled_not_ours()
        {
            var root = JsonNode.Parse("""
                {
                  "statusLine": { "type": "command", "command": "starship prompt" },
                  "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "echo mine" } ] } ] }
                }
                """).AsObject();

            Assert.Equal(LiveStatusWiring.Disabled, BridgeWiring.Inspect(root));
        }

        [Fact]
        public void Four_of_five_hooks_is_NeedsRepair()
        {
            Assert.Equal(LiveStatusWiring.NeedsRepair, BridgeWiring.Inspect(Wired(dropEvents: "PermissionRequest")));
        }

        [Fact]
        public void Hooks_present_but_a_foreign_status_line_is_NeedsRepair()
        {
            var root = Wired();
            root["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = "starship prompt" };

            Assert.Equal(LiveStatusWiring.NeedsRepair, BridgeWiring.Inspect(root));
        }

        [Fact]
        public void Only_our_status_line_and_no_hooks_is_NeedsRepair()
        {
            var root = Wired();
            root.Remove("hooks");

            Assert.Equal(LiveStatusWiring.NeedsRepair, BridgeWiring.Inspect(root));
        }

        [Fact]
        public void Our_hook_under_an_event_we_do_not_wire_still_counts_as_a_trace()
        {
            // A leftover from an older layout: not Enabled (the five are missing), not Disabled
            // (there is a trace of us) — NeedsRepair, so Enable completes and nothing is orphaned.
            var root = JsonNode.Parse("""
                { "hooks": { "SessionStart": [ { "hooks": [ { "type": "command", "command": "bash /x/activity-hook.sh busy" } ] } ] } }
                """).AsObject();

            Assert.Equal(LiveStatusWiring.NeedsRepair, BridgeWiring.Inspect(root));
        }

        [Fact]
        public void Malformed_command_nodes_are_not_ours()
        {
            var root = JsonNode.Parse("""
                { "statusLine": { "command": 42 }, "hooks": { "Stop": [ { "hooks": [ { "command": null } ] } ] } }
                """).AsObject();

            Assert.Equal(LiveStatusWiring.Disabled, BridgeWiring.Inspect(root));
        }

        [Fact]
        public void The_hook_table_names_the_five_events_in_wiring_order()
        {
            Assert.Equal(
                new[] { "UserPromptSubmit", "PostToolUse", "Notification", "Stop", "PermissionRequest" },
                BridgeWiring.HookSpecs.Select(s => s.Event).ToArray());
            Assert.Equal("*", BridgeWiring.HookSpecs.Single(s => s.Event == "PostToolUse").Matcher);
            Assert.Equal("permission", BridgeWiring.HookSpecs.Single(s => s.Event == "PermissionRequest").State);
        }

        [Theory]
        [InlineData(LiveStatusWiring.Enabled, false, false, LiveStatusState.Enabled)]
        [InlineData(LiveStatusWiring.Enabled, true, false, LiveStatusState.Enabled)]      // marker is moot once wired
        [InlineData(LiveStatusWiring.Enabled, false, true, LiveStatusState.JustEnabled)]
        [InlineData(LiveStatusWiring.NeedsRepair, false, false, LiveStatusState.NeedsRepair)]
        [InlineData(LiveStatusWiring.NeedsRepair, true, false, LiveStatusState.NeedsRepair)]
        [InlineData(LiveStatusWiring.Disabled, false, false, LiveStatusState.NotEnabled)]
        [InlineData(LiveStatusWiring.Disabled, true, false, LiveStatusState.Off)]
        public void Display_state_combines_the_file_the_marker_and_just_enabled(LiveStatusWiring wiring, Boolean marker, Boolean justEnabled, LiveStatusState expected)
        {
            Assert.Equal(expected, BridgeWiring.DisplayState(wiring, marker, justEnabled));
        }

        [Theory]
        [InlineData(LiveStatusState.NotEnabled, "Set up", true)]
        [InlineData(LiveStatusState.NeedsRepair, "Set up", true)]
        [InlineData(LiveStatusState.Off, "Off", true)]
        [InlineData(LiveStatusState.JustEnabled, "Restart Claude", false)]
        [InlineData(LiveStatusState.Enabled, null, false)]
        public void Each_state_has_its_words_and_knows_whether_setup_is_owed(LiveStatusState state, String label, Boolean needsSetup)
        {
            // Words for a platform where a running session does NOT pick the wiring up (Windows,
            // on QA's evidence). The macOS variant differs in one state only — see the next test.
            Assert.Equal(label, LiveStatusFace.Label(state, settingsApplyLive: false));
            Assert.Equal(needsSetup, LiveStatusFace.NeedsSetup(state));
        }

        [Fact]
        public void Where_settings_apply_live_the_only_word_that_changes_is_the_one_after_turn_on()
        {
            // Measured on macOS 2026-09-02: a session started without hooks wrote its status line
            // a second after Turn on and fired PermissionRequest three minutes later — no restart.
            // "Restart Claude" there sends the user to do something unnecessary (#58).
            Assert.Equal("Turned on", LiveStatusFace.Label(LiveStatusState.JustEnabled, settingsApplyLive: true));
            foreach (var state in Enum.GetValues<LiveStatusState>())
            {
                if (state != LiveStatusState.JustEnabled)
                {
                    Assert.Equal(LiveStatusFace.Label(state, settingsApplyLive: false), LiveStatusFace.Label(state, settingsApplyLive: true));
                }
            }
        }

        [Fact]
        public void Every_face_fits_a_key()
        {
            // The voice failure faces are held to thirteen characters; these live on the same keys.
            foreach (var state in Enum.GetValues<LiveStatusState>())
            {
                foreach (var live in new[] { true, false })
                {
                    var label = LiveStatusFace.Label(state, live);
                    Assert.True(label == null || label.Length <= 14, $"'{label}' is too long for a key face");
                }
            }
            Assert.True(LiveStatusFace.PressHint.Length <= 13, $"'{LiveStatusFace.PressHint}' is too long for a flash");
        }
    }
}
