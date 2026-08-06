namespace Loupedeck.ClaudeConsolePlugin.Models
{
    using System;

    /// <summary>
    /// One Claude Code session as the keypad sees it — the row behind a single session key.
    ///
    /// Identity is the <see cref="Tty"/>, not the session id: the bash scripts already key their
    /// state files by terminal tab, `ps` reports a tab, and focusing a tab needs one. The session
    /// id rides along so an explicit selection can survive a session id change on the same tab.
    ///
    /// Rows come from two sources merged in SessionRegistry:
    ///   • the per-tab statusline/activity files — rich, but only refreshed when Claude renders
    ///   • the `ps` scan — authoritative about which tabs are actually alive
    /// A live tab with no state file yet is <see cref="IsProvisional"/>: it takes a key immediately
    /// (labelled "Claude") instead of waiting for the session's first assistant message.
    /// </summary>
    public class GridSession
    {
        /// <summary>Bare tab name, e.g. "ttys003". The grid's identity.</summary>
        public String Tty { get; set; }

        /// <summary>Basename of the workspace project dir; "Claude" until the session reports one.</summary>
        public String Project { get; set; } = "Claude";

        /// <summary>"busy" | "waiting" | "ready" — from the activity hooks.</summary>
        public String State { get; set; } = "ready";

        /// <summary>Context-window usage, or null when not yet known (provisional sessions).</summary>
        public Int32? CtxPercent { get; set; }

        public String SessionId { get; set; }

        /// <summary>Claude Code's session name, for the key tooltip.</summary>
        public String SessionName { get; set; }

        /// <summary>Full project path, for the key tooltip.</summary>
        public String ProjectDir { get; set; }

        /// <summary>Newest write we've seen for this tab; drives oldest-first slot ordering.</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>True when discovered by `ps` alone — no statusline file has landed yet.</summary>
        public Boolean IsProvisional { get; set; }

        /// <summary>
        /// Fields that change what the key LOOKS like. Compared to decide whether to repaint, so a
        /// heartbeat-only update (UpdatedAt moving) doesn't churn the LCD every poll.
        /// </summary>
        public String VisualKey => $"{this.Tty}|{this.Project}|{this.State}|{this.CtxPercent}";
    }
}
