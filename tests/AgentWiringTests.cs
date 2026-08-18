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
            manager.PsRunner = () => psOutput;
            return manager.Platform.DiscoverSessions();
        }

        private const String TwoClaudeOneCodex =
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

            Assert.Equal(new[] { "s003" }, Discover(codex, TwoClaudeOneCodex).OrderBy(t => t));
        }

        [Fact]
        public void A_claude_console_discovers_only_claude_sessions()
        {
            var claude = new BridgeManager { Agent = new ClaudeCodeAdapter() };

            Assert.Equal(new[] { "s000", "s001" }, Discover(claude, TwoClaudeOneCodex).OrderBy(t => t));
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
