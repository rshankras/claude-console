namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    /// <summary>Display-only aliases. Original titles remain the sole navigation/approval identity.</summary>
    internal sealed class DesktopConversationLabels
    {
        private readonly Dictionary<String, Dictionary<String, String>> _modes;
        private static readonly Lazy<DesktopConversationLabels> Default = new(() => Load(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "claude-console",
            "desktop-conversation-labels.json")));

        private DesktopConversationLabels(Dictionary<String, Dictionary<String, String>> modes) => _modes = modes;

        internal static String Display(String mode, String title) => Default.Value.Resolve(mode, title);

        internal String Resolve(String mode, String title)
        {
            if (mode != null && title != null && _modes.TryGetValue(mode, out var labels)
                && labels != null && labels.TryGetValue(title, out var alias)
                && !String.IsNullOrWhiteSpace(alias))
            {
                // Duplicate short labels obscure identity even though navigation stays exact.
                if (labels.Count(p => String.Equals(p.Value?.Trim(), alias.Trim(), StringComparison.OrdinalIgnoreCase)) == 1)
                {
                    return alias.Trim();
                }
            }
            return title;
        }

        internal static DesktopConversationLabels Load(String path)
        {
            try { return Parse(File.Exists(path) ? File.ReadAllText(path) : "{}"); }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "DesktopConversationLabels: unreadable aliases; using original titles");
                return Parse("{}");
            }
        }

        internal static DesktopConversationLabels Parse(String json)
        {
            try
            {
                var modes = JsonSerializer.Deserialize<Dictionary<String, Dictionary<String, String>>>(json);
                return new DesktopConversationLabels(modes ?? new());
            }
            catch (JsonException) { return new DesktopConversationLabels(new()); }
        }
    }
}
