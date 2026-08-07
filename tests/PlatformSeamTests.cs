namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// The Phase 0 platform seam. These tests pin the DIVISION OF LABOUR rather than any one
    /// backend: BridgeManager resolves which session the keys act on and hands the request across
    /// IPlatformBridge; the backend performs it. A Windows backend added later inherits every
    /// contract asserted here without a line of these tests changing.
    /// </summary>
    public class PlatformSeamTests : IDisposable
    {
        // A throwaway IPC root per test — SessionRegistry deletes files it judges dead, so it must
        // never be pointed at the live root (see SessionRegistryTests).
        private readonly String _root =
            Path.Combine(Path.GetTempPath(), "cc-seam-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        private SessionRegistry NewGrid()
        {
            var sessions = Path.Combine(_root, "sessions");
            var activity = Path.Combine(_root, "activity");
            Directory.CreateDirectory(sessions);
            Directory.CreateDirectory(activity);
            return new SessionRegistry(sessions, activity, Path.Combine(_root, "registry.json"));
        }

        // A backend that performs nothing and records everything.
        internal sealed class FakePlatformBridge : IPlatformBridge
        {
            public String Name => "fake";
            public Boolean IsSupported => true;

            public List<(String Session, String Text, Boolean Enter)> Texts { get; } = new();
            public List<(String Session, KeyStroke Key)> Keys { get; } = new();
            public List<String> TabEnters { get; } = new();
            public List<String> Focused { get; } = new();
            public List<TerminalAction> Navigations { get; } = new();
            public List<String> Launches { get; } = new();
            public Int32 Alerts { get; private set; }

            public HashSet<String> SessionsToReport { get; set; }
            public String FrontmostToReport { get; set; }
            public InjectionOutcome Outcome { get; set; } = InjectionOutcome.Ok;

            public HashSet<String> DiscoverSessions() => this.SessionsToReport;
            public String QueryFrontmostSession() => this.FrontmostToReport;

            public InjectionOutcome InjectText(String sessionId, String text, Boolean pressEnter)
            {
                this.Texts.Add((sessionId, text, pressEnter));
                return this.Outcome;
            }

            public InjectionOutcome InjectKey(String sessionId, KeyStroke key)
            {
                this.Keys.Add((sessionId, key));
                return this.Outcome;
            }

            public InjectionOutcome InjectTabThenEnter(String sessionId)
            {
                this.TabEnters.Add(sessionId);
                return this.Outcome;
            }

            public void FocusSession(String sessionId) => this.Focused.Add(sessionId);
            public void Navigate(TerminalAction action) => this.Navigations.Add(action);
            public void LaunchClaudeInProject(String projectDir) => this.Launches.Add(projectDir);
            public void Alert() => this.Alerts++;
        }

        private static (BridgeManager Bridge, FakePlatformBridge Fake) Rig(String activeTty = "ttys001")
        {
            var fake = new FakePlatformBridge();
            return (new BridgeManager(fake) { ActiveTty = activeTty }, fake);
        }

        // ---------------------------------------------------------------------------------------
        // Everything OS-touching goes through the seam
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Text_injection_is_delegated_with_the_resolved_target()
        {
            var (bridge, fake) = Rig("ttys007");

            bridge.InjectText("hello", pressEnter: false);

            var call = Assert.Single(fake.Texts);
            Assert.Equal("ttys007", call.Session);
            Assert.Equal("hello", call.Text);
            Assert.False(call.Enter);
        }

        [Fact]
        public void SendPrompt_delegates_as_a_submitting_text_injection()
        {
            var (bridge, fake) = Rig("ttys002");

            bridge.SendPrompt("/context");

            var call = Assert.Single(fake.Texts);
            Assert.Equal("/context", call.Text);
            Assert.True(call.Enter);
        }

        [Fact]
        public void Key_injection_is_delegated_with_the_resolved_target()
        {
            var (bridge, fake) = Rig("ttys003");

            bridge.InjectKey(KeyStroke.Escape);

            var call = Assert.Single(fake.Keys);
            Assert.Equal("ttys003", call.Session);
            Assert.Equal(KeyStroke.Escape, call.Key);
        }

        [Fact]
        public void TabThenEnter_is_delegated_as_one_operation()
        {
            var (bridge, fake) = Rig("ttys004");

            bridge.InjectTabThenEnter();

            // One call, not a Tab call followed by a Return call — an app switch could slip between.
            Assert.Equal("ttys004", Assert.Single(fake.TabEnters));
            Assert.Empty(fake.Keys);
        }

        [Fact]
        public void Navigation_is_delegated_verbatim()
        {
            var (bridge, fake) = Rig();

            bridge.Navigate(TerminalAction.NewClaudeTab);
            bridge.Navigate(TerminalAction.PreviousWindow);

            Assert.Equal(
                new[] { TerminalAction.NewClaudeTab, TerminalAction.PreviousWindow },
                fake.Navigations);
        }

        // ---------------------------------------------------------------------------------------
        // Session ids are opaque above the seam
        // ---------------------------------------------------------------------------------------

        [Theory]
        [InlineData("ttys003")]                 // macOS
        [InlineData("pid-1234-638000000000")]   // Windows (planned form)
        [InlineData("weird_id.v2")]
        public void Session_ids_are_passed_through_unparsed(String sessionId)
        {
            // Nothing above IPlatformBridge may assume a session id's shape. The manager must hand
            // back exactly the token the backend minted — this is what lets Windows key on a
            // process instead of a TTY without touching a line of the orchestration code.
            var (bridge, fake) = Rig(sessionId);

            bridge.InjectText("x", pressEnter: false);
            bridge.InjectKey(KeyStroke.Return);

            Assert.Equal(sessionId, fake.Texts.Single().Session);
            Assert.Equal(sessionId, fake.Keys.Single().Session);
        }

        [Fact]
        public void A_session_carries_two_distinct_identifiers()
        {
            // SessionKey is the platform's token (the grid/IPC/focus key); SessionId is Claude's
            // own id for the conversation. Collapsing them is an easy and silent mistake: the two
            // are both strings, both "the session id" in conversation, and only one is safe to
            // key files by. They must never alias.
            var session = new Models.GridSession { SessionKey = "ttys003", SessionId = "sid-abc" };

            Assert.Equal("ttys003", session.SessionKey);
            Assert.Equal("sid-abc", session.SessionId);
            Assert.Contains("ttys003", session.VisualKey);   // repaint identity tracks the grid key
        }

        // ---------------------------------------------------------------------------------------
        // Discovery contract: null means "I don't know", not "none"
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void A_failed_scan_reports_null_rather_than_an_empty_set()
        {
            // The distinction is load-bearing: an empty set tells the registry every session died
            // and reaps them. A backend that can't scan must say so, not claim emptiness.
            var fake = new FakePlatformBridge { SessionsToReport = null };

            Assert.Null(fake.DiscoverSessions());
            Assert.Null(new UnsupportedPlatformBridge().DiscoverSessions());
        }

        // ---------------------------------------------------------------------------------------
        // Project launch: neutral bookkeeping here, terminal specifics behind the seam
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Launching_a_project_releases_the_pin_before_delegating()
        {
            // Opening a project starts a session in a tab with no key yet; a stale pin would send
            // the next Yes / Clear / voice press to the OLD session.
            var fake = new FakePlatformBridge();
            var bridge = new BridgeManager(fake);
            bridge.Grid = this.NewGrid();
            bridge.Grid.Refresh(new HashSet<String> { "ttys009" });
            bridge.SelectSlot(1);
            Assert.NotNull(bridge.PinnedTty);

            bridge.LaunchClaudeInProject("/tmp/some-project");

            Assert.Null(bridge.PinnedTty);
            Assert.Equal("/tmp/some-project", Assert.Single(fake.Launches));
        }

        [Fact]
        public void Selecting_a_slot_focuses_that_session_through_the_seam()
        {
            var fake = new FakePlatformBridge();
            var bridge = new BridgeManager(fake);
            bridge.Grid = this.NewGrid();
            bridge.Grid.Refresh(new HashSet<String> { "ttys011" });

            bridge.SelectSlot(1);

            Assert.Equal("ttys011", Assert.Single(fake.Focused));
        }

        // ---------------------------------------------------------------------------------------
        // The no-backend platform degrades; it never throws
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void An_unsupported_platform_reports_unsupported_and_never_throws()
        {
            var platform = new UnsupportedPlatformBridge();

            Assert.False(platform.IsSupported);
            Assert.Equal(InjectionOutcome.Unsupported, platform.InjectText("s", "hi", true));
            Assert.Equal(InjectionOutcome.Unsupported, platform.InjectKey("s", KeyStroke.Return));
            Assert.Equal(InjectionOutcome.Unsupported, platform.InjectTabThenEnter("s"));
            Assert.Null(platform.QueryFrontmostSession());

            // Void members must be no-ops, not exceptions — they run inside SDK callbacks.
            platform.FocusSession("s");
            platform.Navigate(TerminalAction.NewTab);
            platform.LaunchClaudeInProject("/tmp");
            platform.Alert();
        }

        [Fact]
        public void A_key_press_on_an_unsupported_platform_is_survivable()
        {
            // The whole point of shipping UnsupportedPlatformBridge instead of null.
            var bridge = new BridgeManager(new UnsupportedPlatformBridge());

            bridge.SendPrompt("hello");
            bridge.InjectKey(KeyStroke.Escape);
            bridge.Navigate(TerminalAction.Activate);
        }

        [Fact]
        public void The_factory_selects_a_supported_backend_for_this_machine()
        {
            var platform = PlatformBridgeFactory.Create();

            if (OperatingSystem.IsMacOS())
            {
                Assert.IsType<MacPlatformBridge>(platform);
                Assert.True(platform.IsSupported);
            }
            else
            {
                Assert.IsType<UnsupportedPlatformBridge>(platform);
            }
        }
    }
}
