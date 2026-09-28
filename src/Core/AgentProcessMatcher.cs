namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    /// <summary>
    /// A description of what an agent's process looks like in `ps` output — and deliberately
    /// nothing more.
    ///
    /// This is how discovery spans both seams without either learning about the other. Finding
    /// sessions is inherently a cell of the matrix (agent X running on OS Y), so the platform
    /// bridge is handed one of these at construction: it learns what to match, never which agent
    /// it is matching, and an adapter still never learns which OS it is on.
    ///
    /// Two shapes have to be recognised, because both agents can arrive either way:
    ///   • a native binary — `codex …`, `/usr/local/bin/claude …`
    ///   • an interpreter running a script — `node …/@anthropic-ai/claude-code/cli.js`, and the
    ///     same for an npm-installed Codex
    /// </summary>
    internal sealed class AgentProcessMatcher
    {
        /// <summary>
        /// Executable basenames, matched CASE-SENSITIVELY on macOS. Case matters: Claude's desktop
        /// app binary is "Claude", and matching it would put a GUI app on the session grid.
        /// </summary>
        public String[] ExeNames { get; init; } = Array.Empty<String>();

        /// <summary>
        /// Command-line fragments that identify the CLI when an interpreter launched it. Only
        /// consulted for a recognised interpreter, so a fragment as short as "/.codex/" can't
        /// match an unrelated process that merely mentions the path.
        /// </summary>
        public String[] ScriptHints { get; init; } = Array.Empty<String>();

        /// <summary>Native CLI subcommands that run helpers rather than interactive sessions.</summary>
        public String[] NonSessionSubcommands { get; init; } = Array.Empty<String>();

        /// <summary>
        /// Path fragments (backslash form; compared with either separator, case-insensitively)
        /// that mark a process as the agent's OWN machinery rather than a session, whatever else
        /// its command line says. Checked before the interpreter hints, which they can otherwise
        /// satisfy by accident.
        /// </summary>
        public String[] NonSessionPathMarkers { get; init; } = Array.Empty<String>();

        /// <summary>
        /// Matches nothing. The default everywhere in Core, because the engine must not name an
        /// agent: defaulting to a real one means an undeclared product silently adopts that agent's
        /// sessions, which is how a Codex keypad showed Claude tabs.
        /// </summary>
        public static AgentProcessMatcher None => new AgentProcessMatcher();

        /// <summary>Claude Code. The shapes here are the ones proven in the field since 1.4.</summary>
        public static AgentProcessMatcher ClaudeCode => new AgentProcessMatcher
        {
            ExeNames = new[] { "claude" },
            ScriptHints = new[] { "/claude-code/", "/.claude/" },
        };

        /// <summary>
        /// Codex CLI. Ships as a native binary (verified on 0.145.0:
        /// ~/.codex/packages/standalone/releases/&lt;ver&gt;-&lt;arch&gt;/bin/codex), but it is also
        /// distributed through npm as @openai/codex, which runs under node — hence the hints.
        /// </summary>
        public static AgentProcessMatcher CodexCli => new AgentProcessMatcher
        {
            ExeNames = new[] { "codex" },
            ScriptHints = new[] { "/@openai/codex/", "/.codex/" },
            // `sandbox`: Codex App's long-lived workers. `app-server`: the shared daemon that
            // hosts every session since 0.157 (`…\app-server-daemon\…\codex.exe app-server
            // --listen unix:// --managed-daemon`, plus its `app-server daemon pid-update-loop`
            // sibling). Both share the CLI's image name and neither is a terminal: on the
            // 1.6.2 laptop pass the two daemon processes took session slots 1 and 2, and the
            // slot key's focus failed because there was no tab to find (#132). The daemon's
            // parent has exited, so the "child of a candidate" rule cannot drop it.
            NonSessionSubcommands = new[] { "sandbox", "app-server" },
            // The daemon also spawns Codex App's bundled node runtime to run plugins out of
            // ~/.codex/plugins/cache (captured 28 Sep: `…\OpenAI\Codex\runtimes\cua_node\…\node.exe
            // "…\.codex\plugins\cache\openai-bundled\unified-computer-use\…\scripts\launch.mjs"`).
            // The "/.codex/" hint accepted it as an npm-installed CLI, it took slot 1 in the
            // session's own folder, and the daemon routing then attributed the session's events
            // to it (#132). Neither location ever hosts an interactive session.
            NonSessionPathMarkers = new[] { @"\OpenAI\Codex\runtimes\", @"\.codex\plugins\" },
        };
    }
}
