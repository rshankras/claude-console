namespace Loupedeck.ClaudeConsolePlugin.Agents
{
    using System;

    /// <summary>
    /// OpenAI's Codex CLI. Second adapter, and the one that proves the seam earns its keep.
    ///
    /// State reaches the plugin through Codex's LIFECYCLE HOOKS, whose event set is close enough
    /// to Claude Code's that one hook script serves both: SessionStart, UserPromptSubmit,
    /// PreToolUse, PermissionRequest, PostToolUse, Stop. Every payload carries session_id, cwd,
    /// and the active model, so there is no statusline to chain and no polling to schedule.
    ///
    /// TWO DIFFERENCES THAT SHAPE THE PRODUCT.
    ///
    /// No cost. Codex bills a subscription, not tokens, and reports no spend — so the Cost key is
    /// absent on a Codex profile rather than showing a zero. Context usage exists only inside the
    /// rollout transcript (~/.codex/sessions/**/rollout-*.jsonl, event_msg/token_count →
    /// total_token_usage), a format the documentation explicitly declines to keep stable. Hence
    /// BestEffortContext: read it defensively, show nothing when it surprises us. This is the
    /// single most likely thing to break on a Codex release.
    ///
    /// Hooks are trusted BY HASH. Codex loads hooks from ~/.codex/config.toml or hooks.json, lists
    /// them for review, and runs them only once the user grants trust via /hooks — and re-flags
    /// them whenever the command changes. Installation therefore cannot be silent (unlike Claude
    /// Code's settings.json), and an update that rewrites the hook command re-nags every user.
    /// The wiring must install a SMALL STABLE LAUNCHER and version the logic it delegates to.
    /// Never suggest --dangerously-bypass-hook-trust: it disables the review for everything.
    ///
    /// Verified locally against codex-cli 0.145.0 (native binary, both platforms). Project-local
    /// .codex/hooks.json loads only in a TRUSTED project — an untrusted one skips them silently,
    /// which is why the wiring targets the user layer.
    /// </summary>
    internal sealed class CodexCliAdapter : IAgentAdapter
    {
        public String Id => "codex-cli";

        public String DisplayName => "Codex";

        public String CliCommand => "codex";

        // Codex ships a native standalone binary on both platforms (~/.codex/packages/standalone/
        // on macOS), so the basename match used for Claude works unchanged. If a future build is
        // distributed as a node script this must grow into a command-line match.
        public String[] ProcessNames => new[] { "codex" };

        public String ProductSlug => "codex-console";

        public AgentCapabilities Capabilities => new AgentCapabilities
        {
            Cost = false,                // subscription pricing — no spend is reported at all
            ContextPercent = false,      // no documented interface
            BestEffortContext = true,    // rollout JSONL tail, explicitly unstable
            Model = true,                // present on every hook payload
            InputModes = false,          // approval policy is a flag/picker, not a cycle chord
            ApprovalSignal = true,       // PermissionRequest, with a structured decision protocol
            MultiConsumerHooks = true,   // matcher groups; concurrent handlers per event
            HooksNeedTrust = true,       // one-time /hooks trust grant, re-flagged on change
        };

        public String SlashCommand(AgentVerb verb) =>
            verb switch
            {
                AgentVerb.Model => "/model",
                AgentVerb.Compact => "/compact",
                AgentVerb.Clear => "/new",
                AgentVerb.Exit => "/exit",
                // No context command: the number isn't exposed, so the key hides rather than
                // typing something Codex would reject.
                AgentVerb.Context => null,
                // Codex has these as first-class verbs Claude Code lacks. They are subcommands
                // rather than slash commands, so they belong on a launch path, not a typed key —
                // recorded here so the capability isn't lost when the keys get built.
                AgentVerb.Review => null,
                AgentVerb.ResumeLast => null,
                _ => null,
            };
    }
}
