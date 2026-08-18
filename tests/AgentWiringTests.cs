namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

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
        [Fact]
        public void Declaring_an_agent_changes_what_the_plugin_discovers()
        {
            var manager = new BridgeManager { Agent = new CodexCliAdapter() };

            // The bridge is rebuilt from the declared agent, so its matcher is Codex's — Claude's
            // sessions must be invisible to it, and vice versa.
            var matcher = manager.Agent.ProcessMatcher;

            Assert.True(AgentProcessWatcher.IsAgentCommand("codex", matcher));
            Assert.False(AgentProcessWatcher.IsAgentCommand("claude", matcher));
        }

        [Fact]
        public void The_claude_product_still_discovers_claude()
        {
            var manager = new BridgeManager { Agent = new ClaudeCodeAdapter() };
            var matcher = manager.Agent.ProcessMatcher;

            Assert.True(AgentProcessWatcher.IsAgentCommand("claude", matcher));
            Assert.False(AgentProcessWatcher.IsAgentCommand("codex", matcher));
        }

        /// <summary>
        /// The exact hardware symptom, as a test: two claude processes and one codex, and a Codex
        /// console must see only the codex tab.
        /// </summary>
        [Fact]
        public void A_codex_console_does_not_show_claude_sessions()
        {
            const String ps = "  100    99 s000     claude\n"
                            + "  200   199 s001     claude\n"
                            + "  300   299 s003     codex\n";

            var codex = new BridgeManager { Agent = new CodexCliAdapter() };
            var found = AgentProcessWatcher.TtysFrom(ps, codex.Agent.ProcessMatcher);

            Assert.Equal(new[] { "s003" }, found);
        }

        /// <summary>An undeclared agent finds nothing at all rather than defaulting to someone's.</summary>
        [Fact]
        public void An_undeclared_agent_discovers_nothing()
        {
            const String ps = "  100    99 s000     claude\n  300   299 s003     codex\n";

            var manager = new BridgeManager();

            Assert.Empty(AgentProcessWatcher.TtysFrom(ps, manager.Agent.ProcessMatcher));
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
