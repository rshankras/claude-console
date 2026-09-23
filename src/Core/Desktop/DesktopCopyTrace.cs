namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading;

    /// <summary>Opt-in, expiring Copy Reply diagnostics. Only fixed codes, never app content.</summary>
    internal sealed class DesktopCopyTrace
    {
        private readonly String _enableFile;
        private readonly Action<String> _write;
        private readonly Func<DateTime> _utcNow;
        private Int32 _written;
        internal DesktopCopyTrace(String enableFile, Action<String> write, Func<DateTime> utcNow = null)
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
                    _write("DesktopCopyReply: result=" + SafeCode(code));
            }
            catch { /* Diagnostics must never change the copy result or block the key. */ }
        }

        internal static String SafeCode(String code) => code switch
        {
            "key-pressed" or "command-busy" or "action-unavailable" or "requested" or "copied"
                or "voice-busy" or "context-busy" or "empty-response" or "helper-exception"
                or "unavailable" or "unsupported" or "app-not-running" or "app-not-frontmost"
                or "not-trusted" or "surface-unavailable" or "answer-not-ready" or "no-answer"
                or "reply-unrecognized" or "copy-control-unavailable" or "ambiguous-answer"
                or "reply-web-area-missing" or "reply-web-area-multiple" or "reply-composer-missing"
                or "reply-composer-multiple" or "reply-dialog-open" or "reply-selection-multiple"
                or "reply-copy-multiple" or "reply-copy-outside-latest" or "reply-copy-not-found"
                or "reply-copy-wrong-role" or "reply-copy-nested-control" or "reply-action-not-found"
                or "reply-action-row-unrecognized" or "answer-changed" or "window-changed"
                or "composer-target-changed" or "mode-changed" or "copy-unconfirmed" => code,
            _ => "unknown-error",
        };
    }
}
