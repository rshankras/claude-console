namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    using Loupedeck.ClaudeConsolePlugin.Actions;
    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// Which session the typing keys act on.
    ///
    /// This is the whole point of the grid: pressing Yes must answer the session you meant, even
    /// when you're looking at a different tab — or at a browser. Getting it wrong sends an approval
    /// to the wrong Claude, which is worse than sending nothing.
    /// </summary>
    public class SessionTargetingTests : IDisposable
    {
        /// <summary>
        /// The routed-session bar is product identity, and the engine's default is the colour
        /// Claude Console shipped. A product that wants its own says so from its constructor; the
        /// engine never picks by agent name, so Vizhi's blue cannot follow Claude Console into a
        /// rebuild. Restores the default afterwards — KeyImage is static for the process.
        /// </summary>
        [Fact]
        public void Selected_session_bar_is_the_products_to_declare_and_defaults_to_Claudes()
        {
            Assert.Equal(KeyImage.SelectionOrange, KeyImage.SessionBar);

            var codexBlue = new BitmapColor(0x81, 0xA8, 0xED);
            try
            {
                KeyImage.UseIdentityColors(codexBlue, codexBlue);
                Assert.Equal(codexBlue, KeyImage.SessionBar);
            }
            finally
            {
                KeyImage.UseIdentityColors(new BitmapColor(0x60, 0xA5, 0xFA), KeyImage.SelectionOrange);
            }

            Assert.Equal(KeyImage.SelectionOrange, KeyImage.SessionBar);
        }

        private readonly String _root =
            Path.Combine(Path.GetTempPath(), "cc-target-" + Guid.NewGuid().ToString("N"));

        private readonly String _sessionsDir;
        private readonly String _activityDir;

        public SessionTargetingTests()
        {
            _sessionsDir = Path.Combine(_root, "sessions");
            _activityDir = Path.Combine(_root, "activity");
            Directory.CreateDirectory(_sessionsDir);
            Directory.CreateDirectory(_activityDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        private void WriteSession(String tty, String project)
        {
            File.WriteAllText(Path.Combine(_sessionsDir, tty + ".json"), JsonSerializer.Serialize(new
            {
                session_id = "sid-" + tty,
                workspace = new { project_dir = "/Users/x/" + project },
                context_window = new { used_percentage = 10 },
            }));
        }

        private void WriteActivity(String tty, String state) =>
            File.WriteAllText(Path.Combine(_activityDir, tty + ".json"), JsonSerializer.Serialize(new
            {
                state,
                ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            }));

        // What the PermissionRequest hook leaves beside a waiting session: the captured payload.
        private void WritePending(String tty, String tool, String command) =>
            File.WriteAllText(Path.Combine(_activityDir, "pending-" + tty + ".json"), JsonSerializer.Serialize(new
            {
                tool_name = tool,
                tool_input = new { command },
            }));

        // A bridge whose grid is rooted in this test's temp dir, populated from the given live TTYs.
        private BridgeManager BridgeWith(params String[] liveTtys)
        {
            var grid = new SessionRegistry(_sessionsDir, _activityDir, Path.Combine(_root, "registry.json")) { Agent = new Agents.ClaudeCodeAdapter() };
            grid.Refresh(new HashSet<String>(liveTtys, StringComparer.Ordinal));
            return new BridgeManager(new MacPlatformBridge()) { Grid = grid };
        }

        [Fact]
        public void Follows_the_frontmost_tab_when_it_is_a_known_session()
        {
            WriteSession("ttys001", "alpha");
            WriteSession("ttys002", "beta");
            var bridge = BridgeWith("ttys001", "ttys002");

            bridge.ActiveTty = "ttys002";

            Assert.Equal("ttys002", bridge.RoutingTty());
        }

        [Fact]
        public void Targets_the_only_session_when_nothing_is_selected()
        {
            // The single-session case must keep working with no slot press at all — that is how
            // every existing user runs the plugin today.
            WriteSession("ttys004", "solo");
            var bridge = BridgeWith("ttys004");

            bridge.ActiveTty = null;

            Assert.Equal("ttys004", bridge.RoutingTty());
        }

        [Fact]
        public void Targets_the_session_that_is_waiting_on_you()
        {
            // Frontmost is a terminal tab with no Claude in it (or Terminal isn't frontmost at all);
            // exactly one session wants an answer, so that is the obvious target.
            WriteSession("ttys001", "alpha");
            WriteSession("ttys002", "beta");
            WriteActivity("ttys002", "waiting");
            var bridge = BridgeWith("ttys001", "ttys002");

            bridge.ActiveTty = "ttys009";   // some other, non-Claude tab

            Assert.Equal("ttys002", bridge.RoutingTty());
        }

        [Fact]
        public void A_pending_approval_outranks_a_session_idling_at_its_prompt()
        {
            // Both are "waiting": one has a permission menu up (the hook captured the payload), the
            // other has sat idle at its prompt for a minute (the Notification hook, no payload —
            // #51). Only the first is something Yes/No can answer. Counting both as "waiting" left
            // the answer keys with "(no target)" whenever a second session idled — on Windows,
            // where no frontmost tab breaks the tie, that was every second session (QA's Mode B).
            WriteSession("ttys001", "alpha");
            WriteSession("ttys002", "beta");
            WriteActivity("ttys001", "waiting");
            WriteActivity("ttys002", "waiting");
            WritePending("ttys002", "Bash", "git status");
            var bridge = BridgeWith("ttys001", "ttys002");

            bridge.ActiveTty = null;   // Windows: no frontmost tab is ever known

            Assert.Equal("ttys002", bridge.RoutingTty());
        }

        [Fact]
        public void Does_not_guess_when_two_sessions_both_have_approvals_pending()
        {
            WriteSession("ttys001", "alpha");
            WriteSession("ttys002", "beta");
            WriteActivity("ttys001", "waiting");
            WriteActivity("ttys002", "waiting");
            WritePending("ttys001", "Bash", "git status");
            WritePending("ttys002", "Bash", "npm test");
            var bridge = BridgeWith("ttys001", "ttys002");

            bridge.ActiveTty = null;

            Assert.Null(bridge.RoutingTty());
        }

        [Fact]
        public void Does_not_guess_when_two_sessions_are_waiting()
        {
            // Ambiguous: answering the wrong one is worse than answering none. Falls back to the
            // tracked tab, and the injection guard beeps rather than typing somewhere unintended.
            WriteSession("ttys001", "alpha");
            WriteSession("ttys002", "beta");
            WriteActivity("ttys001", "waiting");
            WriteActivity("ttys002", "waiting");
            var bridge = BridgeWith("ttys001", "ttys002");

            bridge.ActiveTty = "ttys009";

            Assert.Equal("ttys009", bridge.RoutingTty());
        }

        [Fact]
        public void Selecting_a_slot_retargets_immediately()
        {
            // Pressing a session key must retarget with no wait for the next frontmost poll —
            // otherwise "press slot 2, press Yes" races and lands in slot 1.
            WriteSession("ttys001", "alpha");
            WriteSession("ttys002", "beta");
            var bridge = BridgeWith("ttys001", "ttys002");
            bridge.ActiveTty = "ttys001";
            bridge.OsascriptRunner = (args, timeout, wantOutput) => "ok";   // don't drive the window server

            bridge.SelectSlot(2);

            Assert.Equal("ttys002", bridge.RoutingTty());
        }

        [Fact]
        public void Selection_survives_the_frontmost_tab_probe()
        {
            // THE bug this pin exists for. The poll re-reads Terminal's front tab every ~2.5s and
            // used to write it into the same field SelectSlot had just set, so a selection decayed
            // within seconds: press slot 2, look back at session 1, press Clear — and /clear wiped
            // session 1. Assigning ActiveTty here IS that probe.
            WriteSession("ttys001", "alpha");
            WriteSession("ttys002", "beta");
            var bridge = BridgeWith("ttys001", "ttys002");
            bridge.OsascriptRunner = (args, timeout, wantOutput) => "ok";

            bridge.SelectSlot(2);
            bridge.ActiveTty = "ttys001";   // poll: you are now looking at session 1's tab

            Assert.Equal("ttys002", bridge.RoutingTty());
        }

        [Fact]
        public void Selection_outranks_a_session_that_is_waiting_on_you()
        {
            // With nothing pinned, a lone waiting session is the obvious target. Once you have
            // picked one explicitly, it isn't — answering the other Claude is the exact mistake
            // the grid exists to prevent.
            WriteSession("ttys001", "alpha");
            WriteSession("ttys002", "beta");
            WriteActivity("ttys001", "waiting");
            var bridge = BridgeWith("ttys001", "ttys002");
            bridge.OsascriptRunner = (args, timeout, wantOutput) => "ok";

            bridge.SelectSlot(2);
            bridge.ActiveTty = "ttys009";   // Terminal isn't frontmost at all

            Assert.Equal("ttys002", bridge.RoutingTty());
        }

        [Fact]
        public void Injection_still_goes_to_the_selected_session_after_a_poll()
        {
            // End to end, with the poll running in between — this is "press slot 2, then Clear
            // three minutes later", which is how the bug was reported.
            WriteSession("ttys001", "alpha");
            WriteSession("ttys002", "beta");
            var bridge = BridgeWith("ttys001", "ttys002");

            List<String> lastArgs = null;
            bridge.OsascriptRunner = (args, timeout, wantOutput) => { lastArgs = args; return "ok"; };

            bridge.SelectSlot(2);
            bridge.ActiveTty = "ttys001";
            bridge.SendPrompt("/clear");

            Assert.Equal("/dev/ttys002", lastArgs[2]);
        }

        [Fact]
        public void Selecting_another_slot_moves_the_selection()
        {
            WriteSession("ttys001", "alpha");
            WriteSession("ttys002", "beta");
            var bridge = BridgeWith("ttys001", "ttys002");
            bridge.OsascriptRunner = (args, timeout, wantOutput) => "ok";

            bridge.SelectSlot(2);
            bridge.SelectSlot(1);

            Assert.Equal("ttys001", bridge.RoutingTty());
        }

        [Fact]
        public void Selection_is_released_when_that_session_exits()
        {
            // A pin must never strand the keys on a tab that no longer exists — that would beep on
            // every press with no way back short of pressing another session key.
            WriteSession("ttys001", "alpha");
            WriteSession("ttys002", "beta");
            var grid = new SessionRegistry(_sessionsDir, _activityDir, Path.Combine(_root, "registry.json")) { Agent = new Agents.ClaudeCodeAdapter() };
            grid.Refresh(new HashSet<String>(new[] { "ttys001", "ttys002" }, StringComparer.Ordinal));
            var bridge = new BridgeManager(new MacPlatformBridge()) { Grid = grid };
            bridge.OsascriptRunner = (args, timeout, wantOutput) => "ok";

            bridge.SelectSlot(2);
            File.Delete(Path.Combine(_sessionsDir, "ttys002.json"));
            grid.Refresh(new HashSet<String>(new[] { "ttys001" }, StringComparer.Ordinal));   // tab closed

            Assert.Equal("ttys001", bridge.RoutingTty());
            Assert.Null(bridge.PinnedTty);
            Assert.Null(grid.FocusedSession);
        }

        [Fact]
        public void Selection_survives_a_plugin_reload()
        {
            // Rebuilding the plugin reloads it; losing your selection every time you iterate is the
            // same annoyance the persisted slot assignments already fix.
            var registryFile = Path.Combine(_root, "registry.json");
            WriteSession("ttys001", "alpha");
            WriteSession("ttys002", "beta");

            var grid = new SessionRegistry(_sessionsDir, _activityDir, registryFile) { Agent = new Agents.ClaudeCodeAdapter() };
            grid.Refresh(new HashSet<String>(new[] { "ttys001", "ttys002" }, StringComparer.Ordinal));
            var bridge = new BridgeManager(new MacPlatformBridge()) { Grid = grid };
            bridge.OsascriptRunner = (args, timeout, wantOutput) => "ok";
            bridge.SelectSlot(2);

            var reloaded = new SessionRegistry(_sessionsDir, _activityDir, registryFile) { Agent = new Agents.ClaudeCodeAdapter() };
            reloaded.LoadPersisted();

            Assert.Equal("ttys002", reloaded.FocusedSession);
        }

        [Fact]
        public void Selecting_an_empty_slot_does_nothing()
        {
            WriteSession("ttys001", "alpha");
            var bridge = BridgeWith("ttys001");
            bridge.ActiveTty = "ttys001";
            var calls = 0;
            bridge.OsascriptRunner = (args, timeout, wantOutput) => { calls++; return "ok"; };

            bridge.SelectSlot(5);   // nothing in slot 5

            Assert.Equal(0, calls);
            Assert.Equal("ttys001", bridge.RoutingTty());
        }

        [Fact]
        public void Injection_goes_to_the_selected_session()
        {
            // End to end: select slot 2, type — the osascript must target slot 2's tab.
            WriteSession("ttys001", "alpha");
            WriteSession("ttys002", "beta");
            var bridge = BridgeWith("ttys001", "ttys002");
            bridge.ActiveTty = "ttys001";

            List<String> lastArgs = null;
            bridge.OsascriptRunner = (args, timeout, wantOutput) => { lastArgs = args; return "ok"; };

            bridge.SelectSlot(2);
            bridge.InjectText("yes", pressEnter: true);

            Assert.Equal("/dev/ttys002", lastArgs[2]);
        }

        // --- key face -----------------------------------------------------------------------

        [Theory]
        [InlineData("headroom", "headroom")]              // fits
        [InlineData("claude-conso", "claude-conso")]      // exactly at the limit
        [InlineData("claude-console", "claude-cons…")]    // 14 chars — real project, gets clipped
        [InlineData("a-very-long-project-name", "a-very-long…")]
        public void Project_labels_fit_the_key(String project, String expected)
        {
            // NOTE: 12 is Vizhi's limit, carried over. It clips common real names like
            // "claude-console", so it is worth re-checking against an actual key on hardware —
            // the full name is always available in the key's tooltip.
            Assert.Equal(expected, SessionSlotCommand.Truncate(project, 12));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void Truncate_handles_no_project(String project)
        {
            Assert.Equal(String.Empty, SessionSlotCommand.Truncate(project, 12));
        }
    }
}
