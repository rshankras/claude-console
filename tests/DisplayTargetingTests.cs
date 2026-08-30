namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// Two questions, two answers (#25).
    ///
    /// One resolver used to serve both "where does this key act?" and "what am I looking at?", so
    /// pressing a Session key froze Cost, Model and Context on the pinned session. QA's evidence:
    /// with one session frontmost, Cost showed the OTHER session's $16.38; pressing Session 1 made
    /// it jump to $2.62 and it never moved again.
    ///
    /// The pin is right for routing — acting on session 2 while looking at session 1 is the entire
    /// point of the grid — and wrong for a read-only total.
    /// </summary>
    public class DisplayTargetingTests : IDisposable
    {
        private readonly String _root =
            Path.Combine(Path.GetTempPath(), "cc-display-" + Guid.NewGuid().ToString("N"));

        private readonly String _sessionsDir;
        private readonly String _activityDir;

        public DisplayTargetingTests()
        {
            this._sessionsDir = Path.Combine(this._root, "sessions");
            this._activityDir = Path.Combine(this._root, "activity");
            Directory.CreateDirectory(this._sessionsDir);
            Directory.CreateDirectory(this._activityDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(this._root, recursive: true); } catch { /* best effort */ }
        }

        private void WriteSession(String tty, String project) =>
            File.WriteAllText(Path.Combine(this._sessionsDir, tty + ".json"), JsonSerializer.Serialize(new
            {
                session_id = "sid-" + tty,
                workspace = new { project_dir = "/Users/x/" + project },
                context_window = new { used_percentage = 10 },
            }));

        private BridgeManager BridgeWith(params String[] liveTtys)
        {
            var grid = new SessionRegistry(this._sessionsDir, this._activityDir, Path.Combine(this._root, "registry.json"))
            {
                Agent = new Agents.ClaudeCodeAdapter(),
            };
            grid.Refresh(new HashSet<String>(liveTtys, StringComparer.Ordinal));
            return new BridgeManager(new MacPlatformBridge()) { Grid = grid };
        }

        // -----------------------------------------------------------------------------------------
        // The defect QA reproduced
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The heart of it: pin one session, look at another, and the display must follow your eyes
        /// while the typing keys still act on the pin.
        /// </summary>
        [Fact]
        public void A_pin_does_not_move_the_display_off_the_tab_you_are_looking_at()
        {
            this.WriteSession("ttys001", "alpha");
            this.WriteSession("ttys002", "beta");
            var bridge = this.BridgeWith("ttys001", "ttys002");

            // Pressing a session key also FOCUSES that tab, so the order matters: you pin beta
            // (and are taken there), then switch back to alpha yourself. That is QA's scenario —
            // and the moment Cost used to freeze on beta and never move again.
            bridge.SelectSlot(2);           // pin beta
            bridge.ActiveTty = "ttys001";   // ...then look at alpha

            Assert.Equal("ttys002", bridge.RoutingTty());   // keys still act on the pin
            Assert.Equal("ttys001", bridge.DisplayTty());   // numbers follow your eyes
        }

        [Fact]
        public void With_nothing_pinned_both_answers_agree()
        {
            this.WriteSession("ttys001", "alpha");
            this.WriteSession("ttys002", "beta");
            var bridge = this.BridgeWith("ttys001", "ttys002");

            bridge.ActiveTty = "ttys002";

            Assert.Equal("ttys002", bridge.RoutingTty());
            Assert.Equal("ttys002", bridge.DisplayTty());
        }

        /// <summary>Switching tabs after pinning must move the display — the "frozen" symptom.</summary>
        [Fact]
        public void Switching_tabs_after_a_pin_moves_the_display_but_not_the_routing()
        {
            this.WriteSession("ttys001", "alpha");
            this.WriteSession("ttys002", "beta");
            var bridge = this.BridgeWith("ttys001", "ttys002");

            bridge.SelectSlot(1);   // pin alpha
            bridge.ActiveTty = "ttys002";              // then look at beta

            Assert.Equal("ttys001", bridge.RoutingTty());
            Assert.Equal("ttys002", bridge.DisplayTty());
        }

        // -----------------------------------------------------------------------------------------
        // Windows: no frontmost-tab probe exists, so the display must fall back to the pin
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Windows Terminal exposes no way to ask which tab is in front, so `_activeTty` is null
        /// there. The display must then follow the routing target rather than showing nothing —
        /// achieved by the fallback, with no platform branch.
        /// </summary>
        [Fact]
        public void With_no_frontmost_tab_the_display_follows_the_routing_target()
        {
            this.WriteSession("ttys001", "alpha");
            this.WriteSession("ttys002", "beta");
            var bridge = this.BridgeWith("ttys001", "ttys002");

            bridge.SelectSlot(2);
            bridge.ActiveTty = null;   // as on Windows

            Assert.Equal("ttys002", bridge.RoutingTty());
            Assert.Equal("ttys002", bridge.DisplayTty());
        }

        /// <summary>A frontmost tab that is not a live session must not become the display target.</summary>
        [Fact]
        public void An_unknown_frontmost_tab_falls_back_to_the_routing_target()
        {
            this.WriteSession("ttys001", "alpha");
            var bridge = this.BridgeWith("ttys001");

            bridge.ActiveTty = "ttys099";   // a tab with no Claude in it

            Assert.Equal("ttys001", bridge.DisplayTty());
        }

        // -----------------------------------------------------------------------------------------
        // Releasing the pin from the device — QA's literal complaint
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void Pressing_the_pinned_slot_again_releases_it()
        {
            this.WriteSession("ttys001", "alpha");
            this.WriteSession("ttys002", "beta");
            var bridge = this.BridgeWith("ttys001", "ttys002");

            const Int32 slot = 2;
            bridge.SelectSlot(slot);
            Assert.Equal("ttys002", bridge.PinnedTty);

            bridge.SelectSlot(slot);
            Assert.Null(bridge.PinnedTty);
        }

        /// <summary>After releasing, the keys follow the frontmost tab again.</summary>
        [Fact]
        public void After_releasing_the_routing_follows_the_frontmost_tab()
        {
            this.WriteSession("ttys001", "alpha");
            this.WriteSession("ttys002", "beta");
            var bridge = this.BridgeWith("ttys001", "ttys002");

            const Int32 slot = 2;
            bridge.SelectSlot(slot);
            bridge.SelectSlot(slot);                  // release
            bridge.ActiveTty = "ttys001";

            Assert.Equal("ttys001", bridge.RoutingTty());
            Assert.Equal("ttys001", bridge.DisplayTty());
        }

        /// <summary>Pressing a DIFFERENT slot moves the pin rather than releasing it.</summary>
        [Fact]
        public void Pressing_a_different_slot_moves_the_pin()
        {
            this.WriteSession("ttys001", "alpha");
            this.WriteSession("ttys002", "beta");
            var bridge = this.BridgeWith("ttys001", "ttys002");

            bridge.SelectSlot(1);
            bridge.SelectSlot(2);

            Assert.Equal("ttys002", bridge.PinnedTty);
        }

        /// <summary>Pin, release, pin again — the toggle must not latch.</summary>
        [Fact]
        public void The_toggle_survives_repetition()
        {
            this.WriteSession("ttys001", "alpha");
            var bridge = this.BridgeWith("ttys001");
            const Int32 slot = 1;

            bridge.SelectSlot(slot);
            Assert.Equal("ttys001", bridge.PinnedTty);
            bridge.SelectSlot(slot);
            Assert.Null(bridge.PinnedTty);
            bridge.SelectSlot(slot);
            Assert.Equal("ttys001", bridge.PinnedTty);
        }
    }
}
