namespace Loupedeck.ClaudeConsolePlugin.Agents
{
    using System;

    /// <summary>
    /// Everything the plugin needs to know about the CODING AGENT it is driving, and nothing it
    /// doesn't. The mirror of IPlatformBridge: that seam hides the operating system, this one
    /// hides the agent. The two are orthogonal, and neither may look through the other —
    /// an adapter never learns which OS it runs on, a bridge never learns which agent it types to.
    ///
    /// Everything else in the plugin — the session grid, the file IPC bus, targeting, voice,
    /// self-registration, key rendering, risk classification — is neutral to both, which is why
    /// a second agent costs an adapter rather than a fork. See docs/multi-agent-architecture.md.
    ///
    /// TWO RULES FOR IMPLEMENTERS.
    ///
    /// 1. NEVER INVENT A VALUE. If the agent doesn't report cost, Capabilities.Cost is false and
    ///    the key hides — it does not render "$0.00". A grid that lies is worse than a grid that
    ///    admits it doesn't know, because the whole point of the hardware is glanceability.
    ///    This is the lesson from Codex, which has no per-token cost at all.
    ///
    /// 2. KEEP THE STATE BRIDGE'S ON-DISK FOOTPRINT STABLE ACROSS UPDATES. Codex trusts hook
    ///    commands BY HASH and re-prompts when one changes; Claude Code chains a pre-existing
    ///    statusline. In both cases a plugin update that rewrites the wiring nags or breaks the
    ///    user. Install a small stable launcher that delegates to versioned logic elsewhere.
    /// </summary>
    internal interface IAgentAdapter
    {
        /// <summary>Stable id, used in IPC payloads and logs. e.g. "claude-code", "codex-cli".</summary>
        String Id { get; }

        /// <summary>Human name for key faces and logs. e.g. "Claude Code", "Codex".</summary>
        String DisplayName { get; }

        /// <summary>The CLI binary a session runs, e.g. "claude" / "codex".</summary>
        String CliCommand { get; }

        /// <summary>
        /// Executable names the process watcher matches to find live sessions. Bare names, no
        /// extension — the Windows watcher appends ".exe" itself. Both agents ship a native
        /// binary, so a basename match is enough; an agent distributed as a script would need
        /// its command line inspected instead.
        /// </summary>
        String[] ProcessNames { get; }

        /// <summary>
        /// Namespace for everything this agent owns on disk: the IPC root (/tmp/&lt;name&gt;),
        /// the runtime home (~/.&lt;name&gt;), and the Options+ registration (@_&lt;name&gt;).
        /// Two consoles installed side by side must never share one of these.
        /// </summary>
        String ProductSlug { get; }

        /// <summary>
        /// How this agent's sessions look in a process listing. The platform bridge is built from
        /// this, so discovery finds THIS agent's sessions and no one else's — without the bridge
        /// ever learning which agent it is.
        /// </summary>
        AgentProcessMatcher ProcessMatcher { get; }

        /// <summary>What this agent can honestly report. Drives key visibility, never a fake value.</summary>
        AgentCapabilities Capabilities { get; }

        /// <summary>
        /// Read ONE session state file, in whatever format this agent writes, into the neutral
        /// shape the grid renders. Null when the document is unusable. The grid must never parse an
        /// agent's format itself: doing so is why Codex sessions sat at "ready" forever, their
        /// hook envelopes silently deserialising as Claude's statusline with every field null.
        /// </summary>
        AgentSessionState ParseSessionState(String json);

        /// <summary>
        /// The agent's own word for a verb, e.g. "/compact", or null when it has no equivalent —
        /// in which case the key hides rather than typing something the agent will reject.
        /// </summary>
        String SlashCommand(AgentVerb verb);
    }

    /// <summary>
    /// The verbs a key can ask of an agent. Deliberately small and behavioural: this enumerates
    /// what the USER wants ("compact this conversation"), not what any one CLI happens to call it.
    /// </summary>
    internal enum AgentVerb
    {
        /// <summary>Open the model picker.</summary>
        Model,

        /// <summary>Compact / summarise the conversation to reclaim context.</summary>
        Compact,

        /// <summary>Show context usage.</summary>
        Context,

        /// <summary>Clear the conversation and start fresh.</summary>
        Clear,

        /// <summary>Leave the session.</summary>
        Exit,

        /// <summary>Native code review, where the agent has one as a first-class command.</summary>
        Review,

        /// <summary>Resume the most recent previous session.</summary>
        ResumeLast,
    }
}
