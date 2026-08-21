namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One user-customizable prompt key, loaded from ~/.claude/claude-console/prompts.json.
    /// <c>Icon</c> is an embedded icon basename (e.g. "fix_bug", "review", "deploy"); its baked
    /// colour is the key's colour. An unknown icon falls back to centred text.
    /// </summary>
    public class PromptDef
    {
        [JsonPropertyName("id")]
        public String Id { get; set; }

        [JsonPropertyName("label")]
        public String Label { get; set; }

        [JsonPropertyName("prompt")]
        public String Prompt { get; set; }

        [JsonPropertyName("icon")]
        public String Icon { get; set; }

        /// <summary>
        /// False = type the prompt but do NOT press Return, leaving it in Claude's input box to
        /// edit or extend before sending (with Return — keyboard or the keypad's Return key).
        /// Nullable so an entry that doesn't mention it keeps today's behaviour: absent == true.
        /// </summary>
        [JsonPropertyName("submit")]
        public Boolean? Submit { get; set; }

        /// <summary>Whether pressing this key submits the prompt (the default) or drafts it.</summary>
        [JsonIgnore]
        public Boolean Submits => this.Submit ?? true;
    }
}
