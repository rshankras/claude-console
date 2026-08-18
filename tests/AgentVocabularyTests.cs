namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Agents;

    using Xunit;

    /// <summary>
    /// Keys speaking the agent's language rather than Claude Code's.
    ///
    /// The failure this guards against is quiet and embarrassing on hardware: a key that types
    /// "/context" at an agent with no such command puts an error on screen and looks broken. So a
    /// verb the agent has no word for produces no key at all, and the words that do exist come from
    /// the adapter rather than a literal in an action.
    ///
    /// These assert the adapter contract the actions consume. The actions themselves can't be
    /// constructed off-device — PluginDynamicCommand needs a live SDK host — so the contract is
    /// what's pinned here, and it is what the actions were rewritten to read.
    /// </summary>
    public class AgentVocabularyTests
    {
        private static readonly IAgentAdapter Claude = new ClaudeCodeAdapter();
        private static readonly IAgentAdapter Codex = new CodexCliAdapter();

        /// <summary>
        /// The shipped product must not change. Every Core key Claude Console has today still has a
        /// word behind it, so no existing user's profile loses a binding to this refactor.
        /// </summary>
        [Fact]
        public void Claude_Code_keeps_every_word_it_had()
        {
            Assert.Equal("/model", Claude.SlashCommand(AgentVerb.Model));
            Assert.Equal("/compact", Claude.SlashCommand(AgentVerb.Compact));
            Assert.Equal("/context", Claude.SlashCommand(AgentVerb.Context));
            Assert.Equal("/clear", Claude.SlashCommand(AgentVerb.Clear));
            Assert.Equal("/exit", Claude.SlashCommand(AgentVerb.Exit));
        }

        /// <summary>
        /// Codex clears with "/new", not "/clear". This is the case that makes the whole indirection
        /// worth it: same key, same user intent, different word.
        /// </summary>
        [Fact]
        public void The_same_key_types_a_different_word_per_agent()
        {
            Assert.Equal("/clear", Claude.SlashCommand(AgentVerb.Clear));
            Assert.Equal("/new", Codex.SlashCommand(AgentVerb.Clear));
        }

        /// <summary>
        /// Codex exposes no context figure, so it has no command to show one. The key must be
        /// absent rather than typing something that errors.
        /// </summary>
        [Fact]
        public void A_verb_the_agent_lacks_yields_no_word()
        {
            Assert.Null(Codex.SlashCommand(AgentVerb.Context));
            Assert.NotNull(Claude.SlashCommand(AgentVerb.Context));
        }

        /// <summary>
        /// Mode is a keystroke, not a command, so its key is gated on the capability instead. Codex
        /// has no input-mode cycle to drive — its approval policy is a flag, not a chord.
        /// </summary>
        [Fact]
        public void The_mode_key_exists_only_where_there_are_modes_to_cycle()
        {
            Assert.True(Claude.Capabilities.InputModes);
            Assert.False(Codex.Capabilities.InputModes);
        }

        /// <summary>
        /// Every word an agent does return must be a well-formed slash command. An empty string
        /// would be typed as a bare Return, which submits whatever the user had half-written.
        /// </summary>
        [Fact]
        public void No_agent_returns_a_word_that_would_misfire()
        {
            foreach (var agent in new[] { Claude, Codex })
            {
                foreach (AgentVerb verb in Enum.GetValues<AgentVerb>())
                {
                    var word = agent.SlashCommand(verb);
                    if (word == null)
                    {
                        continue;
                    }

                    Assert.StartsWith("/", word);
                    Assert.True(word.Length > 1, $"{agent.DisplayName}/{verb}: a bare slash");
                    Assert.DoesNotContain(word, Char.IsWhiteSpace);
                }
            }
        }

        /// <summary>
        /// A new session runs the agent's own CLI. The bridge receives this as data — it opens a
        /// terminal and types it, exactly as it would any other command.
        /// </summary>
        [Fact]
        public void Launching_a_project_runs_the_agents_own_cli()
        {
            Assert.Equal("claude", Claude.CliCommand);
            Assert.Equal("codex", Codex.CliCommand);
        }

        /// <summary>
        /// The default matters: anything constructed before a product assigns its agent must behave
        /// as the shipped plugin does, not as an empty or throwing stub.
        /// </summary>
        [Fact]
        public void The_default_agent_is_claude_code()
        {
            var manager = new BridgeManager();

            Assert.Equal("claude-code", manager.Agent.Id);
            Assert.Equal("/clear", manager.Agent.SlashCommand(AgentVerb.Clear));
        }

        /// <summary>Each product sets its own; the keys follow it immediately.</summary>
        [Fact]
        public void A_product_can_declare_which_agent_its_keys_drive()
        {
            var manager = new BridgeManager { Agent = new CodexCliAdapter() };

            Assert.Equal("codex-cli", manager.Agent.Id);
            Assert.Equal("/new", manager.Agent.SlashCommand(AgentVerb.Clear));
            Assert.Null(manager.Agent.SlashCommand(AgentVerb.Context));
        }
    }
}
