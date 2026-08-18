namespace Loupedeck.ClaudeConsolePlugin.Agents
{
    using System;
    using System.Text.Json;

    /// <summary>
    /// Claude Code. The reference adapter: it describes the agent this plugin was built around,
    /// so every value here is a fact already proven in the field rather than a design intention.
    ///
    /// State reaches the plugin two ways, both wired by BridgeManager.EnsureBridgeAutoWired:
    /// a STATUSLINE handler (model, cost, context percentage, project) and HOOKS (activity, and
    /// the PermissionRequest payload that grades approval risk). The statusline is single-slot —
    /// only one program can own it — which is why the wiring chains any handler it displaces
    /// instead of overwriting it.
    /// </summary>
    internal sealed class ClaudeCodeAdapter : IAgentAdapter
    {
        public String Id => "claude-code";

        public String DisplayName => "Claude Code";

        public String CliCommand => "claude";

        // Matched case-sensitively against the executable basename: the desktop app's binary
        // differs in case, and must not be mistaken for a CLI session (see AgentProcessWatcher).
        public String[] ProcessNames => new[] { "claude" };

        public String ProductSlug => "claude-console";

        public AgentProcessMatcher ProcessMatcher => AgentProcessMatcher.ClaudeCode;

        public AgentCapabilities Capabilities => new AgentCapabilities
        {
            Cost = true,                 // statusline cost.total_cost_usd
            ContextPercent = true,       // statusline context_window.used_percentage
            BestEffortContext = false,   // no need — the documented number is available
            Model = true,
            InputModes = true,           // Shift+Tab cycles normal → auto-accept → plan
            ApprovalSignal = true,       // PermissionRequest hook carries tool_name + tool_input
            MultiConsumerHooks = false,  // the statusline is single-slot; wiring must chain
            HooksNeedTrust = false,      // settings.json edits take effect with no trust prompt
        };

        /// <summary>
        /// Claude Code's statusline document. Activity and the pending approval live in SEPARATE
        /// files, so both are left null here and SessionRegistry keeps reading them as it always
        /// has — this adapter changes how the file is parsed, not where Claude Code puts things.
        /// </summary>
        public AgentSessionState ParseSessionState(String json)
        {
            if (String.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var state = JsonSerializer.Deserialize<Models.ClaudeState>(json);
                if (state == null)
                {
                    return null;
                }

                return new AgentSessionState
                {
                    ProjectDir = state.Workspace?.ProjectDir ?? state.Workspace?.CurrentDir,
                    SessionId = state.SessionId,
                    SessionName = state.SessionName,
                    CtxPercent = ContextPercent(state),
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // Delegates to the engine's existing rule rather than restating it. Restating it lost a
        // subtlety: a fresh session reports 0, and "0%" is a misleading answer where "unknown" is
        // the honest one — the same reasoning as the Cost key.
        private static Int32? ContextPercent(Models.ClaudeState state) =>
            SessionRegistry.ContextPercent(state);

        public String SlashCommand(AgentVerb verb) =>
            verb switch
            {
                AgentVerb.Model => "/model",
                AgentVerb.Compact => "/compact",
                AgentVerb.Context => "/context",
                AgentVerb.Clear => "/clear",
                AgentVerb.Exit => "/exit",
                // Review is a prompt on Claude Code, not a command — the Prompts group covers it,
                // and a key that typed "/review" would just produce an unknown-command error.
                AgentVerb.Review => null,
                AgentVerb.ResumeLast => null,
                _ => null,
            };
    }
}
