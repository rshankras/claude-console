namespace Loupedeck.ClaudeConsolePlugin.Agents
{
    using System;

    /// <summary>
    /// One session, as the grid needs it, after the agent's own format has been read.
    ///
    /// Each agent writes a different document — Claude Code a statusline snapshot, Codex a hook
    /// envelope — and the grid must not know either shape. Before this existed, SessionRegistry
    /// deserialised EVERY state file as Claude's format; a Codex file parsed "successfully" with
    /// every field null, so its sessions sat at "ready" forever and borrowed a project name they
    /// never reported. Nothing failed, which is what made it survive so long.
    ///
    /// Fields left null mean "this agent doesn't report it here", not "empty" — the caller keeps
    /// its existing source in that case rather than overwriting a good value with nothing.
    /// </summary>
    internal sealed class AgentSessionState
    {
        public String ProjectDir { get; init; }

        public String SessionId { get; init; }

        public String SessionName { get; init; }

        /// <summary>Context used, as a percentage, where the agent reports one.</summary>
        public Int32? CtxPercent { get; init; }

        /// <summary>
        /// The file this agent appends to as a turn progresses, when it exposes one — Claude Code's
        /// `transcript_path`, Codex's rollout file. Null when the agent reports none.
        ///
        /// It exists here because a turn that is INTERRUPTED fires no lifecycle hook (#30): the only
        /// exit from "busy" is the agent saying so, and on Esc it never does. A file that grows while
        /// the agent works is the one signal that separates a stuck session from a slow one, which an
        /// age threshold cannot do — a long tool call and a dead turn look identical by age alone.
        /// </summary>
        public String TranscriptPath { get; init; }

        /// <summary>
        /// Last time the transport actually observed the transcript grow, as Unix seconds. Some
        /// Windows filesystems do not advance LastWriteTime for Codex's open rollout handle, so a
        /// transport-provided observation is more authoritative than file metadata when present.
        /// </summary>
        public Int64? TranscriptActivityTs { get; init; }

        /// <summary>
        /// busy | waiting | done — or null when the agent reports activity somewhere else and the
        /// caller should keep looking (Claude Code writes it to a separate activity file).
        /// </summary>
        public String Activity { get; init; }

        /// <summary>
        /// When <see cref="Activity"/> was reported, as Unix seconds. Agents that keep activity in
        /// a separate document leave this null; the caller reads that document's timestamp instead.
        /// </summary>
        public Int64? ActivityTs { get; init; }

        /// <summary>
        /// True when interrupting a turn appends an abort record to the transcript. In that case a
        /// post-Escape transcript write corroborates the interrupt instead of disproving it.
        /// </summary>
        public Boolean TranscriptWritesOnInterrupt { get; init; }

        /// <summary>Set only while an approval is pending; null means "not reported here".</summary>
        public String PendingTool { get; init; }

        public String PendingCommand { get; init; }

        public ApprovalRisk Risk { get; init; } = ApprovalRisk.None;

        /// <summary>True when this agent reported the pending approval itself, so the caller must
        /// not also consult its own pending-approval file and overwrite what's here.</summary>
        public Boolean ReportsApproval { get; init; }
    }
}
