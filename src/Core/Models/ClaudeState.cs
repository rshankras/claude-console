namespace Loupedeck.ClaudeConsolePlugin.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// State model read from Claude Code's statusline via /tmp/claude-console-state.json.
    /// </summary>
    public class ClaudeState
    {
        [JsonPropertyName("model")]
        public ModelInfo Model { get; set; }

        [JsonPropertyName("cost")]
        public CostInfo Cost { get; set; }

        [JsonPropertyName("context_window")]
        public ContextInfo ContextWindow { get; set; }

        [JsonPropertyName("session")]
        public SessionInfo Session { get; set; }

        [JsonPropertyName("status")]
        public String Status { get; set; } // "waiting_approval" when hook is blocking

        [JsonPropertyName("tool")]
        public String Tool { get; set; } // Tool awaiting approval

        [JsonPropertyName("session_id")]
        public String SessionId { get; set; }

        [JsonPropertyName("workspace")]
        public WorkspaceInfo Workspace { get; set; }

        /// <summary>
        /// Claude Code's own name for the session, e.g. "port-vizhi-features-claude-console".
        /// Too long for a key label, but good tooltip material.
        /// </summary>
        [JsonPropertyName("session_name")]
        public String SessionName { get; set; }
    }

    /// <summary>
    /// Where the session is working. <c>project_dir</c> is the repo/project root and stays put as
    /// Claude moves around, so its basename is the stable label for a session grid key;
    /// <c>current_dir</c> can point at a subdirectory.
    /// </summary>
    public class WorkspaceInfo
    {
        [JsonPropertyName("project_dir")]
        public String ProjectDir { get; set; }

        [JsonPropertyName("current_dir")]
        public String CurrentDir { get; set; }
    }

    public class ModelInfo
    {
        [JsonPropertyName("display_name")]
        public String DisplayName { get; set; }

        [JsonPropertyName("model_id")]
        public String ModelId { get; set; }
    }

    // NOTE: every number below is NULLABLE on purpose. Claude Code sends `null` — not 0 — for
    // figures it doesn't have yet, which is the normal state of a session that hasn't done any work.
    // Declared non-nullable, a single null makes System.Text.Json throw and the WHOLE state object
    // fails to parse, blanking every live key. That is exactly what happened in 1.6.0: a fresh
    // session sends "used_percentage": null, the parse threw, the session was dropped, and the grid
    // fell back to showing it as an unnamed "Claude". Keep these nullable.
    public class CostInfo
    {
        [JsonPropertyName("total_cost_usd")]
        public Decimal? TotalCostUsd { get; set; }

        [JsonPropertyName("total_lines_added")]
        public Int32? TotalLinesAdded { get; set; }

        [JsonPropertyName("total_lines_removed")]
        public Int32? TotalLinesRemoved { get; set; }
    }

    public class ContextInfo
    {
        [JsonPropertyName("used_percentage")]
        public Double? UsedPercentage { get; set; }

        [JsonPropertyName("total_input_tokens")]
        public Int32? TotalInputTokens { get; set; }

        [JsonPropertyName("total_output_tokens")]
        public Int32? TotalOutputTokens { get; set; }

        [JsonPropertyName("context_window_size")]
        public Int32? MaxTokens { get; set; }
    }

    public class SessionInfo
    {
        [JsonPropertyName("id")]
        public String Id { get; set; }

        [JsonPropertyName("turns")]
        public Int32? Turns { get; set; }
    }

    /// <summary>
    /// Activity pushed by the Claude Code hooks (scripts/activity-hook.sh) into
    /// /tmp/claude-console-activity.json. Drives the Status key's working / waiting / idle face.
    /// </summary>
    public class ActivityState
    {
        [JsonPropertyName("state")]
        public String State { get; set; }   // "busy" | "waiting" | "done"

        [JsonPropertyName("ts")]
        public Int64 Ts { get; set; }       // unix seconds — used for the staleness guard
    }
}
