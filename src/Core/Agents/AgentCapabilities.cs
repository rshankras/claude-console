namespace Loupedeck.ClaudeConsolePlugin.Agents
{
    using System;

    /// <summary>
    /// What an agent can honestly report about itself, and which of its own verbs exist.
    ///
    /// This is the alternative to a lowest-common-denominator feature set: rather than dropping
    /// the Cost key because one agent lacks cost, or showing a zero on that agent, the key asks
    /// the adapter and hides itself where the number would be a fiction. Adding an agent can
    /// therefore only ever REMOVE keys from that agent's profile — it can never degrade another's.
    ///
    /// A capability is about what the agent EXPOSES, not what the plugin has implemented. If
    /// Codex starts reporting cost tomorrow, exactly one boolean changes.
    /// </summary>
    internal readonly struct AgentCapabilities
    {
        /// <summary>Reports a running spend figure (Claude Code: statusline total_cost_usd).</summary>
        public Boolean Cost { get; init; }

        /// <summary>
        /// Reports context usage as a percentage of the window, from a documented interface.
        /// False does NOT mean the number is unobtainable — only that getting it means reading
        /// something the vendor calls unstable, which belongs behind BestEffortContext.
        /// </summary>
        public Boolean ContextPercent { get; init; }

        /// <summary>
        /// Context usage is derivable, but only by parsing a format the vendor explicitly declines
        /// to keep stable (Codex's rollout JSONL). Such a reader must degrade to "unknown" on any
        /// surprise, never to a stale or guessed number, and must never be on a hot path.
        /// </summary>
        public Boolean BestEffortContext { get; init; }

        /// <summary>Reports which model is active.</summary>
        public Boolean Model { get; init; }

        /// <summary>Has an input-mode cycle the keypad can drive with a chord (Claude Code: Shift+Tab).</summary>
        public Boolean InputModes { get; init; }

        /// <summary>
        /// Tab accepts a highlighted completion, so a key can send Tab-then-Return to complete and
        /// submit in one press. Not universal: where the agent does nothing with Tab, the key looks
        /// broken to anyone who presses it.
        /// </summary>
        public Boolean TabCompletion { get; init; }

        /// <summary>
        /// Signals that a specific tool call is waiting on the user, with enough payload to grade
        /// its risk. Both agents do this through a PermissionRequest hook — it is what lights the
        /// approval key amber, and red when RiskClassifier flags the pending command.
        /// </summary>
        public Boolean ApprovalSignal { get; init; }

        /// <summary>
        /// Lifecycle hooks accept multiple independent subscribers, so wiring in cannot displace
        /// another tool (Codex: matcher groups; Claude Code's single statusline needs chaining).
        /// </summary>
        public Boolean MultiConsumerHooks { get; init; }

        /// <summary>Requires a one-time interactive trust grant before the plugin's hooks run.</summary>
        public Boolean HooksNeedTrust { get; init; }

        /// <summary>
        /// The agent reads its hooks and status line from the user's own settings file
        /// (Claude Code: ~/.claude/settings.json), so wiring the live keys means EDITING that file —
        /// which is the user's to say yes to. Where false, the plugin installs its own hooks file
        /// and the agent gates it itself (Codex: ~/.codex/hooks.json behind a trust prompt), so the
        /// Enable / Disable Live Status keys have nothing to do and are not added.
        /// </summary>
        public Boolean SettingsFileWiring { get; init; }

        /// <summary>
        /// An image FILE PATH typed into the composer reaches the CURRENT conversation — whether
        /// the composer attaches it (Claude Code) or the model opens it with its own image-viewing
        /// tool when told the path (Codex). The delivery differs; the honest question this flag
        /// answers is the same: can a screenshot join the running session? Where false, it cannot,
        /// whatever the key face implies.
        /// </summary>
        public Boolean ImageInConversation { get; init; }

        /// <summary>
        /// The CLI accepts images only when a session STARTS (Codex: `-i, --image` — "attach to
        /// the initial prompt", verified against codex-cli 0.147.0). A screenshot key on such an
        /// agent honestly means "new session seeded with this image", and must say so.
        /// </summary>
        public Boolean ImageAtLaunch { get; init; }
    }
}
