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
    }

    public class ModelInfo
    {
        [JsonPropertyName("display_name")]
        public String DisplayName { get; set; }

        [JsonPropertyName("model_id")]
        public String ModelId { get; set; }
    }

    public class CostInfo
    {
        [JsonPropertyName("total_cost_usd")]
        public Decimal TotalCostUsd { get; set; }

        [JsonPropertyName("total_lines_added")]
        public Int32 TotalLinesAdded { get; set; }

        [JsonPropertyName("total_lines_removed")]
        public Int32 TotalLinesRemoved { get; set; }
    }

    public class ContextInfo
    {
        [JsonPropertyName("used_percentage")]
        public Double UsedPercentage { get; set; }

        [JsonPropertyName("total_input_tokens")]
        public Int32 TotalInputTokens { get; set; }

        [JsonPropertyName("total_output_tokens")]
        public Int32 TotalOutputTokens { get; set; }

        [JsonPropertyName("context_window_size")]
        public Int32 MaxTokens { get; set; }
    }

    public class SessionInfo
    {
        [JsonPropertyName("id")]
        public String Id { get; set; }

        [JsonPropertyName("turns")]
        public Int32 Turns { get; set; }
    }

    public class SessionRegistryEntry
    {
        [JsonPropertyName("id")]
        public String Id { get; set; }

        [JsonPropertyName("started")]
        public String Started { get; set; }

        [JsonPropertyName("status")]
        public String Status { get; set; }

        [JsonPropertyName("project_dir")]
        public String ProjectDir { get; set; }

        [JsonPropertyName("project")]
        public String Project { get; set; }
    }
}
