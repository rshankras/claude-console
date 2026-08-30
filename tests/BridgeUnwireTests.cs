namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Text.Json.Nodes;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// Taking the wiring back out of settings.json touches ONLY what the plugin put in (#31).
    ///
    /// QA's complaint had two halves. The plugin rewrote a user's Claude Code settings on first
    /// load, and its only undo was "restore the backup" — a snapshot taken once and never again,
    /// a month stale on the test machine, so restoring it would have rolled back every unrelated
    /// change since. The fix is a surgical unwire, and these tests pin "surgical": a user's own
    /// hook in the same event survives, their other events survive, a status line we chained
    /// comes back as it was, and containers collapse only when we are the ones who emptied them.
    /// </summary>
    public class BridgeUnwireTests
    {
        private const String Ours = "bash /Users/me/.claude/claude-console/scripts/activity-hook.sh busy";
        private const String OurStatus = "bash /Users/me/.claude/claude-console/scripts/statusline-handler.sh";

        private static JsonObject Parse(String json) => (JsonObject)JsonNode.Parse(json);

        private static JsonObject Wired(String extraHooks = "", String statusLine = null) => Parse($$"""
            {
              "model": "opus",
              "permissions": { "defaultMode": "auto" },
              "statusLine": {{statusLine ?? $$"""{ "type": "command", "command": "{{OurStatus}}" }"""}},
              "hooks": {
                "UserPromptSubmit": [ { "hooks": [ { "type": "command", "command": "{{Ours}}" } ] } ],
                "PostToolUse": [ { "matcher": "*", "hooks": [
                    { "type": "command", "command": "{{Ours}}" },
                    { "type": "command", "command": "echo user-hook-in-same-entry" } ] } ],
                "Stop": [ { "hooks": [ { "type": "command", "command": "bash ~/.claude/claude-console/scripts/activity-hook.sh done" } ] } ]
                {{extraHooks}}
              }
            }
            """);

        [Fact]
        public void Removes_our_hooks_and_nothing_else()
        {
            var root = Wired(extraHooks: """, "SessionStart": [ { "hooks": [ { "type": "command", "command": "echo mine" } ] } ]""");

            Assert.True(BridgeWiring.Unwire(root, chainedCommand: null));

            var hooks = (JsonObject)root["hooks"];
            // Ours are gone: events we owned outright disappear...
            Assert.Null(hooks["UserPromptSubmit"]);
            Assert.Null(hooks["Stop"]);
            // ...the user's hook that shared an entry with ours stays, alone...
            var post = (JsonArray)hooks["PostToolUse"];
            Assert.Single(post);
            var inner = (JsonArray)post[0]["hooks"];
            Assert.Single(inner);
            Assert.Equal("echo user-hook-in-same-entry", inner[0]["command"].GetValue<String>());
            // ...and the user's own event is untouched.
            Assert.Equal("echo mine", hooks["SessionStart"][0]["hooks"][0]["command"].GetValue<String>());

            // Everything unrelated to hooks is exactly as it was.
            Assert.Equal("opus", root["model"].GetValue<String>());
            Assert.Equal("auto", root["permissions"]["defaultMode"].GetValue<String>());
        }

        [Fact]
        public void A_chained_status_line_is_put_back_to_what_it_was()
        {
            // THE important one. The user had their own status bar; we recorded it and ran it
            // through our handler. Unwiring must hand it back, not delete it — deleting it would be
            // the stale-backup failure in miniature: a user setting lost because the plugin left.
            var root = Wired();

            Assert.True(BridgeWiring.Unwire(root, chainedCommand: "my-statusline --fancy"));

            Assert.Equal("my-statusline --fancy", root["statusLine"]["command"].GetValue<String>());
            Assert.Equal("command", root["statusLine"]["type"].GetValue<String>());
        }

        [Fact]
        public void An_unchained_status_line_is_removed_entirely()
        {
            // There was nothing before us, so there is nothing to put back.
            var root = Wired();

            Assert.True(BridgeWiring.Unwire(root, chainedCommand: null));

            Assert.Null(root["statusLine"]);
        }

        [Fact]
        public void A_status_line_that_is_not_ours_is_left_alone()
        {
            var root = Wired(statusLine: """{ "type": "command", "command": "starship prompt" }""");

            BridgeWiring.Unwire(root, chainedCommand: "should-not-be-used");

            Assert.Equal("starship prompt", root["statusLine"]["command"].GetValue<String>());
        }

        [Fact]
        public void The_hooks_object_goes_only_when_we_emptied_it()
        {
            // All three events were ours: nothing of the user's remains, so "hooks" itself goes.
            var root = Parse($$"""
                { "hooks": {
                    "UserPromptSubmit": [ { "hooks": [ { "type": "command", "command": "{{Ours}}" } ] } ],
                    "Stop": [ { "hooks": [ { "type": "command", "command": "{{Ours}}" } ] } ] } }
                """);

            Assert.True(BridgeWiring.Unwire(root, null));
            Assert.Null(root["hooks"]);
        }

        [Fact]
        public void An_entry_that_was_already_empty_is_the_users_and_stays()
        {
            // We collapse only containers WE emptied. An odd empty entry is not our leftover.
            var root = Parse("""{ "hooks": { "Stop": [ { "hooks": [] } ] } }""");

            Assert.False(BridgeWiring.Unwire(root, null));
            Assert.NotNull(root["hooks"]["Stop"]);
        }

        [Fact]
        public void Unwiring_twice_changes_nothing_the_second_time()
        {
            var root = Wired();
            Assert.True(BridgeWiring.Unwire(root, "orig"));

            var snapshot = root.ToJsonString();
            Assert.False(BridgeWiring.Unwire(root, "orig"));
            Assert.Equal(snapshot, root.ToJsonString());
        }

        [Theory]
        [InlineData("""{ }""")]
        [InlineData("""{ "hooks": "not an object" }""")]
        [InlineData("""{ "hooks": { "Stop": "not an array" }, "statusLine": "not an object" }""")]
        [InlineData("""{ "hooks": { "Stop": [ { "hooks": [ { "command": 42 } ] } ] } }""")]
        public void Malformed_or_foreign_shapes_are_not_ours_and_are_not_touched(String json)
        {
            var root = Parse(json);
            var before = root.ToJsonString();

            Assert.False(BridgeWiring.Unwire(root, null));
            Assert.Equal(before, root.ToJsonString());
        }

        [Fact]
        public void The_windows_shim_counts_as_ours_too()
        {
            var root = Parse("""
                { "statusLine": { "type": "command", "command": "\"C:\\Users\\me\\AppData\\Local\\Logi\\LogiPluginService\\Plugins\\ClaudeConsole\\bin\\claude-console-hook.exe\" statusline" },
                  "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "\"C:\\x\\claude-console-hook.exe\" activity done" } ] } ] } }
                """);

            Assert.True(BridgeWiring.Unwire(root, null));
            Assert.Null(root["statusLine"]);
            Assert.Null(root["hooks"]);
        }
    }
}
