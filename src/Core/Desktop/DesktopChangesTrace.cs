namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading;

    /// <summary>Opt-in, expiring View Changes diagnostics. Only fixed codes, never app content.</summary>
    internal sealed class DesktopChangesTrace
    {
        private readonly String _enableFile;
        private readonly Action<String> _write;
        private readonly Func<DateTime> _utcNow;
        private Int32 _written;
        internal DesktopChangesTrace(String enableFile, Action<String> write, Func<DateTime> utcNow = null)
        { _enableFile = enableFile; _write = write; _utcNow = utcNow ?? (() => DateTime.UtcNow); }

        internal void Write(String code)
        {
            try
            {
                if (Volatile.Read(ref _written) >= 80) return;
                var file = new FileInfo(_enableFile);
                if (!file.Exists || file.Length > 100) return;
                if (!DateTime.TryParse(File.ReadAllText(_enableFile), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var until) || until.Kind != DateTimeKind.Utc) return;
                var remaining = until - _utcNow();
                if (remaining <= TimeSpan.Zero || remaining > TimeSpan.FromMinutes(30)) return;
                if (Interlocked.Increment(ref _written) <= 80)
                    _write("DesktopViewChanges: result=" + SafeCode(code));
            }
            catch { /* Diagnostics must never change the action result or block the key. */ }
        }

        internal static String SafeCode(String code) => code switch
        {
            "requested" or "opened" or "already-open" or "panel-arguments" or "panel-unavailable"
                or "panel-not-available"
                or "panel-foreground-changed" or "panel-window-changed" or "panel-surface-missing" or "panel-conversation-changed"
                or "panel-target-changed" or "mode-changed" or "panel-obstructed" or "panel-ambiguous"
                or "panel-opener-unavailable" or "panel-opener-missing" or "panel-opener-multiple" or "panel-opener-disabled" or "panel-shortcut-failed" or "panel-shortcut-unconfirmed" or "panel-press-failed" or "panel-unconfirmed"
                or "app-not-running" or "not-trusted" or "surface-unavailable"
                or "composer-target-changed" or "no-helper-output" or "unexpected-reply" => code,
            _ => "unknown-error",
        };

        internal static String ErrorFrom(String json)
        {
            if (String.IsNullOrWhiteSpace(json)) return "no-helper-output";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == System.Text.Json.JsonValueKind.Object
                    && root.TryGetProperty("error", out var value)
                    && value.ValueKind == System.Text.Json.JsonValueKind.String)
                    return SafeCode(value.GetString());
            }
            catch (System.Text.Json.JsonException) { }
            return "unexpected-reply";
        }
    }
}
