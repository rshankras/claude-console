namespace Loupedeck.ClaudeConsolePlugin.Agents
{
    using System;

    /// <summary>
    /// The "no product has declared itself" adapter — the agent-side counterpart to
    /// UnsupportedPlatformBridge, and it exists for the same reason: a working object rather than a
    /// null, so a missed declaration degrades to keys that don't appear instead of a
    /// NullReferenceException thrown inside an SDK callback during load.
    ///
    /// It is also why Core names no concrete agent. Defaulting to Claude Code would have compiled
    /// that adapter into every product — including one that ships a different agent entirely — and
    /// a product that forgot to declare itself would have silently behaved like Claude Console,
    /// typing Claude's slash commands at whatever was actually running.
    ///
    /// Each product declares its agent in its Plugin CONSTRUCTOR, not in Load(): the SDK constructs
    /// every action between the two, and an action decides there and then which keys to add.
    /// </summary>
    internal sealed class NoAgentAdapter : IAgentAdapter
    {
        public String Id => "none";

        public String DisplayName => "No agent";

        public String CliCommand => null;

        public String[] ProcessNames => Array.Empty<String>();

        public String ProductSlug => "agent-console";

        // Reports nothing, because nothing is known. Every capability-gated key hides.
        public AgentCapabilities Capabilities => default;

        // No vocabulary at all: a key that somehow exists types nothing rather than guessing.
        public String SlashCommand(AgentVerb verb) => null;
    }
}
