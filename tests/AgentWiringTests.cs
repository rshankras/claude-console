namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Agents;
    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// Declaring an agent must actually reach the parts that DO something with it.
    ///
    /// These exist because of a bug hardware found and every unit test missed. The matcher was
    /// right, the factory threaded it correctly, and the adapters returned the correct one — but
    /// nothing connected them: BridgeManager built its platform bridge in its constructor, before
    /// the product declared its agent, so a Codex console discovered `claude` processes and put two
    /// Claude sessions on its grid. Every piece was correct in isolation and none were wired.
    ///
    /// So these assert the WIRING, not the pieces: that declaring an agent changes what the plugin
    /// looks for and what it launches.
    /// </summary>
    public class AgentWiringTests
    {
        // Drives the REAL bridge the manager holds, through its ps seam. Asking the adapter for
        // its matcher instead — which the first version of these tests did — proves only that the
        // adapter is right, and passes happily while the manager ignores it. That is precisely the
        // bug this file exists for, so it must be exercised end to end or not at all.
        private static HashSet<String> Discover(BridgeManager manager, String psOutput)
        {
            // Feed whichever seam the host's bridge exposes. The wiring under test — does
            // declaring an agent change what discovery looks for — is identical on both, but a
            // `ps` string means nothing to the Windows bridge, which would otherwise scan the
            // REAL process table and answer with whatever happened to be running.
            if (OperatingSystem.IsWindows())
            {
                manager.ProcessEnumerator = () => WindowsRowsFrom(psOutput);
            }
            else
            {
                manager.PsRunner = () => psOutput;
            }

            return manager.Platform.DiscoverSessions();
        }

        /// <summary>
        /// The same fixture, in Windows shape: one process per `ps` row, named &lt;agent&gt;.exe.
        /// Session keys differ by platform (tty vs pid+start), so callers compare COUNTS on
        /// Windows and tty names on macOS — the question is which processes were recognised.
        /// </summary>
        private static IEnumerable<WindowsProcessInfo> WindowsRowsFrom(String psOutput)
        {
            var rows = new List<WindowsProcessInfo>();
            var start = new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);

            foreach (var line in psOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var pid = Int32.Parse(parts[0]);
                var name = parts[3];

                rows.Add(new WindowsProcessInfo
                {
                    Pid = pid,
                    ParentPid = 0,
                    Name = name + ".exe",
                    CommandLine = $@"C:\Users\me\{name}.exe",
                    StartTime = start.AddSeconds(pid),
                });
            }

            return rows;
        }

        /// <summary>How many sessions this fixture should yield for the given agent.</summary>
        private static void AssertDiscovered(HashSet<String> found, Int32 expectedCount, params String[] macTtys)
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(expectedCount, found.Count);
            }
            else
            {
                Assert.Equal(macTtys, found.OrderBy(t => t));
            }
        }

        // Each session hangs off a Terminal.app process: since #29 a session whose owning terminal
        // the keys cannot drive takes no key at all, so a fixture without the owner would be a test
        // of the eligibility filter, not of the agent wiring it is here to prove.
        private const String TwoClaudeOneCodex =
            "   99     1 ??       /System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal\n" +
            "  199     1 ??       /System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal\n" +
            "  299     1 ??       /System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal\n" +
            "  100    99 s000     claude\n" +
            "  200   199 s001     claude\n" +
            "  300   299 s003     codex\n";

        /// <summary>
        /// The exact hardware symptom, end to end: two claude tabs and one codex, and a Codex
        /// console's own bridge must return only the codex tab.
        /// </summary>
        [Fact]
        public void A_codex_console_discovers_only_codex_sessions()
        {
            var codex = new BridgeManager { Agent = new CodexCliAdapter() };

            AssertDiscovered(Discover(codex, TwoClaudeOneCodex), 1, "s003");
        }

        [Fact]
        public void A_claude_console_discovers_only_claude_sessions()
        {
            var claude = new BridgeManager { Agent = new ClaudeCodeAdapter() };

            AssertDiscovered(Discover(claude, TwoClaudeOneCodex), 2, "s000", "s001");
        }

        /// <summary>
        /// Declaring an agent must REPLACE the bridge built before the agent was known. The public
        /// constructor makes a default one, and for a while it was indistinguishable from a bridge
        /// a test had supplied — so the replacement never happened.
        /// </summary>
        [Fact]
        public void Declaring_an_agent_replaces_the_default_bridge()
        {
            var manager = new BridgeManager();
            var before = manager.Platform;

            manager.Agent = new CodexCliAdapter();

            Assert.NotSame(before, manager.Platform);
        }

        /// <summary>An undeclared agent finds nothing at all rather than defaulting to someone's.</summary>
        [Fact]
        public void An_undeclared_agent_discovers_nothing()
        {
            var manager = new BridgeManager();

            Assert.Empty(Discover(manager, TwoClaudeOneCodex));
        }

        /// <summary>
        /// A grid slot with no reported project must never borrow another agent's name. The default
        /// used to be the literal "Claude", which is why a Codex keypad showed Claude sessions even
        /// once discovery was right.
        /// </summary>
        [Fact]
        public void A_session_with_no_project_yet_carries_no_agent_name()
        {
            var session = new Models.GridSession();

            Assert.True(String.IsNullOrEmpty(session.Project),
                "an unlabelled session must not default to any agent's name");
        }

        /// <summary>A test-supplied bridge survives declaring an agent; otherwise suites lose their fake.</summary>
        [Fact]
        public void An_injected_bridge_is_not_replaced_by_declaring_an_agent()
        {
            var fake = new UnsupportedPlatformBridge();
            var manager = new BridgeManager(fake) { Agent = new CodexCliAdapter() };

            Assert.Same(fake, manager.Platform);
        }
    }
}
