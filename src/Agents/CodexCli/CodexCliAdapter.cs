namespace Loupedeck.ClaudeConsolePlugin.Agents
{
    using System;
    using System.IO;

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

        public AgentProcessMatcher ProcessMatcher => AgentProcessMatcher.CodexCli;

        public AgentCapabilities Capabilities => new AgentCapabilities
        {
            Cost = false,                // subscription pricing — no spend is reported at all
            ContextPercent = false,      // no documented interface
            BestEffortContext = true,    // rollout JSONL tail, explicitly unstable — implemented
                                         // by CodexContextReader, null on any surprise
            Model = true,                // present on every hook payload
            TabCompletion = false,       // no completion to accept — verified on hardware
            InputModes = false,          // approval policy is a flag/picker, not a cycle chord

            // The two below are TRANSPORT-dependent, so they differ by OS. Codex's hook runner
            // creates no process on Windows — proven on hardware 2026-08-20 with a known-good
            // probe exe that logs every invocation and cannot exit nonzero, never invoked while
            // codex reported "hook exited with code 1" (docs/spike-windows-codex-hooks.md).
            // Windows therefore drives state from the rollout stream instead, which carries the
            // busy/idle edges but no approval event: claiming ApprovalSignal there would light
            // keys amber on evidence that does not exist.
            ApprovalSignal = !OperatingSystem.IsWindows(),  // PermissionRequest hook (macOS)
            HooksNeedTrust = !OperatingSystem.IsWindows(),  // no hooks installed on Windows at all

            MultiConsumerHooks = true,   // matcher groups; concurrent handlers per event
            ImageInConversation = true,  // not via the composer — the MODEL reads the file with its
                                         // image-viewing tool when told the path (proven on hardware
                                         // by the July Vizhi plugin, VizhiActionRouter.cs:444)
            ImageAtLaunch = true,        // -i/--image also exists, but only for the initial prompt
        };

        /// <summary>
        /// The Codex end of the state bridge — installing the hook, and knowing whether the user
        /// still owes it a /hooks trust grant.
        ///
        /// Deliberately NOT on IAgentAdapter yet. Claude Code's equivalent still lives inside
        /// BridgeManager, from before this seam existed, so declaring it on the interface would
        /// mean one real implementation and one no-op pretending to be one. It moves up when
        /// BridgeManager is split.
        /// </summary>
        public CodexStateBridge StateBridge { get; } = new CodexStateBridge();

        /// <summary>
        /// The launcher's contents, embedded at build time so scripts/codex-hook.sh stays the one
        /// source of truth. Null if it is missing from the assembly — which would mean a package
        /// that silently cannot install its own hook, so the tests assert it is present.
        /// </summary>
        public static String HookScriptContents()
        {
            using var stream = typeof(CodexCliAdapter).Assembly
                .GetManifestResourceStream("CodexConsole.codex-hook.sh");

            if (stream == null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Codex has no statusline: every fact arrives on a lifecycle event, so one file carries
        /// the project, the activity AND the pending approval — already graded by CodexStateReader.
        /// </summary>
        public AgentSessionState ParseSessionState(String json)
        {
            var snap = CodexStateReader.Parse(json);
            if (snap == null)
            {
                return null;
            }

            return new AgentSessionState
            {
                ProjectDir = snap.ProjectDir,
                SessionId = snap.SessionId,
                Activity = snap.Activity,
                PendingTool = snap.PendingTool,
                PendingCommand = snap.PendingCommand,
                Risk = snap.Risk,
                ReportsApproval = true,
                // Best-effort, and the only reader of an unstable format in the plugin — it
                // returns null rather than a guess whenever the transcript surprises it.
                CtxPercent = CodexContextReader.PercentFrom(snap.TranscriptPath),
                // The rollout file. Codex reports its own activity, so the stall rule does not
                // currently consult this — it is surfaced so the two agents describe themselves the
                // same way, and NOT as a claim that Codex's stall behaviour has been verified (#30
                // says a Codex equivalent needs its own check, and it has not had one).
                TranscriptPath = snap.TranscriptPath,
            };
        }

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
                // Review is a first-class TUI command (tui/chatwidget/review_popups.rs in the
                // 0.148 binary): typing "/review" opens the picker — uncommitted, against a base
                // branch, or a commit. The earlier note here called it subcommand-only; that was
                // the CLI's `codex review`, and it missed the TUI door. Same trap as images.
                AgentVerb.Review => "/review",
                // ResumeLast really is launch-only (`codex resume --last`) — a launch path verb.
                AgentVerb.ResumeLast => null,
                _ => null,
            };
    }
}
