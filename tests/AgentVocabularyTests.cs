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
        /// Clear means "start over", and the two agents spell it differently — this is the mapping
        /// a user asked about, pinned so it cannot drift: Claude /clear, Codex /new.
        /// </summary>
        /// <summary>
        /// Review is Codex's own first-class verb — "/review" opens the TUI picker
        /// (review_popups.rs) — and on Claude Code it is a PROMPT, not a command, so the verb maps
        /// to null and the key never appears there. The null is what keeps the Claude keypad from
        /// typing "/review" into an agent that would reject it.
        /// </summary>
        [Fact]
        public void Review_is_a_codex_command_and_absent_on_claude()
        {
            Assert.Equal("/review", Codex.SlashCommand(AgentVerb.Review));
            Assert.Null(Claude.SlashCommand(AgentVerb.Review));
        }

        [Fact]
        public void Clear_is_slash_clear_on_claude_and_slash_new_on_codex()
        {
            Assert.Equal("/clear", Claude.SlashCommand(AgentVerb.Clear));
            Assert.Equal("/new", Codex.SlashCommand(AgentVerb.Clear));
        }

        /// <summary>
        /// Tab only earns a key where there is a completion to accept. Codex has none, and a key
        /// whose press does nothing reads as broken — verified on hardware.
        /// </summary>
        [Fact]
        public void The_tab_key_exists_only_where_completion_can_be_accepted()
        {
            Assert.True(Claude.Capabilities.TabCompletion);
            Assert.False(Codex.Capabilities.TabCompletion);
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
        /// An undeclared agent supports NOTHING, rather than quietly behaving like Claude Code.
        ///
        /// This is the safer default of the two. If Core defaulted to a real agent, that adapter
        /// would be compiled into every product — including ones shipping a different agent — and a
        /// product that forgot to declare itself would type another agent's slash commands at
        /// whatever was actually running. Supporting nothing makes the mistake visible as keys that
        /// don't appear, and it is still an object rather than a null, so nothing throws inside an
        /// SDK callback during load.
        /// </summary>
        [Fact]
        public void An_undeclared_agent_supports_nothing_rather_than_impersonating_one()
        {
            var manager = new BridgeManager();

            Assert.Equal("none", manager.Agent.Id);
            Assert.Null(manager.Agent.SlashCommand(AgentVerb.Clear));
            Assert.False(manager.Agent.Capabilities.Cost);
            Assert.False(manager.Agent.Capabilities.ApprovalSignal);
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
