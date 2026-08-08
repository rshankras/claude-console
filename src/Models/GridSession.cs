namespace Loupedeck.ClaudeConsolePlugin.Models
{
    using System;

    /// <summary>
    /// One Claude Code session as the keypad sees it — the row behind a single session key.
    ///
    /// Identity is the <see cref="SessionKey"/> minted by the platform backend, NOT Claude's own
    /// session id: the state files, the process scan and the focus call must all agree on one key,
    /// and only the OS can supply it (macOS: the tab's TTY; Windows: the Claude process). Claude's
    /// session id rides along so an explicit selection survives a conversation change in the tab.
    /// Treat the value as opaque — see IPlatformBridge.
    ///
    /// Rows come from two sources merged in SessionRegistry:
    ///   • the per-tab statusline/activity files — rich, but only refreshed when Claude renders
    ///   • the `ps` scan — authoritative about which tabs are actually alive
    /// A live tab with no state file yet is <see cref="IsProvisional"/>: it takes a key immediately
    /// (labelled "Claude") instead of waiting for the session's first assistant message.
    /// </summary>
    public class GridSession
    {
        /// <summary>
        /// The grid's identity — an opaque, filename-safe token from the platform backend
        /// (macOS: "ttys003"; Windows: "pid-1234-638…"). Never parse it. Distinct from
        /// <see cref="SessionId"/>, which is Claude's own id for the conversation.
        /// </summary>
        public String SessionKey { get; set; }

        /// <summary>Basename of the workspace project dir; "Claude" until the session reports one.</summary>
        public String Project { get; set; } = "Claude";

        /// <summary>"busy" | "waiting" | "ready" — from the activity hooks.</summary>
        public String State { get; set; } = "ready";

        /// <summary>Context-window usage, or null when not yet known (provisional sessions).</summary>
        public Int32? CtxPercent { get; set; }

        /// <summary>Claude Code's own id for the conversation in this tab (not the grid key).</summary>
        public String SessionId { get; set; }

        /// <summary>Claude Code's session name, for the key tooltip.</summary>
        public String SessionName { get; set; }

        /// <summary>Full project path, for the key tooltip.</summary>
        public String ProjectDir { get; set; }

        /// <summary>Newest write we've seen for this tab; drives oldest-first slot ordering.</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>True when discovered by `ps` alone — no statusline file has landed yet.</summary>
        public Boolean IsProvisional { get; set; }

        /// <summary>The tool awaiting approval, e.g. "Bash". Null when nothing is pending.</summary>
        public String PendingTool { get; set; }

        /// <summary>The shell command awaiting approval, when the pending tool is Bash.</summary>
        public String PendingCommand { get; set; }

        /// <summary>How much attention the pending approval deserves. See RiskClassifier.</summary>
        public ApprovalRisk Risk { get; set; } = ApprovalRisk.None;

        /// <summary>
        /// Fields that change what the key LOOKS like. Compared to decide whether to repaint, so a
        /// heartbeat-only update (UpdatedAt moving) doesn't churn the LCD every poll.
        /// </summary>
        public String VisualKey => $"{this.SessionKey}|{this.Project}|{this.State}|{this.CtxPercent}|{this.Risk}";
    }
}
