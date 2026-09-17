namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    /// <summary>An explicitly configured app hotkey, never inferred from a missing AX button.</summary>
    internal sealed class DesktopVoiceShortcut
    {
        internal static String ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "claude-console", "desktop-voice-shortcut.json");

        // macOS ANSI virtual-key positions, from HIToolbox/Events.h. Configuration accepts
        // letter keys plus modifiers; Return, Escape, and bare text are deliberately excluded.
        private static readonly Dictionary<String, Int32> Keys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = 0, ["S"] = 1, ["D"] = 2, ["F"] = 3, ["H"] = 4, ["G"] = 5,
            ["Z"] = 6, ["X"] = 7, ["C"] = 8, ["V"] = 9, ["B"] = 11, ["Q"] = 12,
            ["W"] = 13, ["E"] = 14, ["R"] = 15, ["Y"] = 16, ["T"] = 17,
            ["O"] = 31, ["U"] = 32, ["I"] = 34, ["P"] = 35, ["L"] = 37,
            ["J"] = 38, ["K"] = 40, ["N"] = 45, ["M"] = 46,
        };
        private static readonly String[] ModifierNames = { "control", "shift", "option", "command" };

        internal Int32 KeyCode { get; }
        internal String Modifiers { get; }
        private DesktopVoiceShortcut(Int32 keyCode, String modifiers) { KeyCode = keyCode; Modifiers = modifiers; }

        internal static DesktopVoiceShortcut Parse(String value)
        {
            var parts = value?.Split('+', StringSplitOptions.TrimEntries);
            if (parts == null || parts.Length < 2 || !Keys.TryGetValue(parts[^1], out var key)) { return null; }
            var modifiers = parts[..^1].Select(p => p.ToLowerInvariant()).ToArray();
            if (modifiers.Distinct().Count() != modifiers.Length ||
                modifiers.Any(m => !ModifierNames.Contains(m)) ||
                !modifiers.Any(m => m == "control" || m == "command")) { return null; }
            return new DesktopVoiceShortcut(key, String.Join(",", ModifierNames.Where(modifiers.Contains)));
        }

        internal static DesktopVoiceShortcut Load(String path)
        {
            try
            {
                if (!File.Exists(path)) { return null; }
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                return doc.RootElement.TryGetProperty("toggleVoiceChat", out var value) && value.ValueKind == JsonValueKind.String
                    ? Parse(value.GetString()) : null;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "DesktopVoiceShortcut: invalid shortcut configuration; using observed controls");
                return null;
            }
        }
    }
}
