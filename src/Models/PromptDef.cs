namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One user-customizable prompt key, loaded from ~/.claude/claude-console/prompts.json.
    /// <c>Icon</c> is an icon basename — resolved against ~/.claude/claude-console/icons/ first,
    /// then the embedded set (e.g. "fix_bug", "review", "deploy"); its baked colour is the key's
    /// colour. An unknown icon falls back to centred text. With <c>Voice</c> true the key becomes a
    /// dictation toggle: press to listen, press again to stop — the prompt text is sent with the
    /// transcript appended (e.g. "/apple:brainstorm " + what you said).
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

        [JsonPropertyName("voice")]
        public Boolean Voice { get; set; }
    }
}
