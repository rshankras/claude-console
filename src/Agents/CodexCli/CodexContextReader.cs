namespace Loupedeck.ClaudeConsolePlugin.Agents
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// Context usage for a Codex session, read from the rollout transcript.
    ///
    /// This is the one BEST-EFFORT reader in the plugin, and it is deliberate. Codex exposes no
    /// context figure through hooks; the number exists only inside the rollout JSONL, whose format
    /// the documentation explicitly declines to keep stable. So every failure path here returns
    /// null — "unknown" — and never a stale or guessed percentage. A wrong number on a key you
    /// glance at is worse than a blank one, which is the same rule the Cost key follows.
    ///
    /// USE last_token_usage, NOT total_token_usage. The window holds the last request; the total is
    /// cumulative across the session and grows past the window. On a real session the two read 12%
    /// and 65% — the second is not a slightly-off answer, it is a different quantity.
    ///
    /// Reads a bounded TAIL and caches on (length, mtime), because this is called from the poll
    /// loop for every session: a transcript grows to megabytes and re-reading it several times a
    /// second would be the most expensive thing the plugin does.
    /// </summary>
    internal static class CodexContextReader
    {
        // Enough to hold many token_count events; they are frequent and we only want the newest.
        private const Int32 TailBytes = 64 * 1024;

        private static readonly Object Gate = new Object();
        private static String _cachedPath;
        private static Int64 _cachedLength;
        private static DateTime _cachedWrite;
        private static Int32? _cachedPercent;

        /// <summary>
        /// Percentage of the context window in use, or null when it cannot be known — no transcript,
        /// an unreadable one, or a format that no longer matches.
        /// </summary>
        public static Int32? PercentFrom(String transcriptPath)
        {
            if (String.IsNullOrWhiteSpace(transcriptPath))
            {
                return null;
            }

            try
            {
                // The path comes from a hook payload, so treat it as input: only ever read a
                // rollout file, never whatever else a malformed payload might name.
                if (!transcriptPath.EndsWith(".jsonl", StringComparison.Ordinal)
                    || transcriptPath.Contains("..", StringComparison.Ordinal))
                {
                    return null;
                }

                var file = new FileInfo(transcriptPath);
                if (!file.Exists)
                {
                    return null;
                }

                lock (Gate)
                {
                    if (_cachedPath == transcriptPath
                        && _cachedLength == file.Length
                        && _cachedWrite == file.LastWriteTimeUtc)
                    {
                        return _cachedPercent;
                    }
                }

                var percent = ReadTail(file);

                lock (Gate)
                {
                    _cachedPath = transcriptPath;
                    _cachedLength = file.Length;
                    _cachedWrite = file.LastWriteTimeUtc;
                    _cachedPercent = percent;
                }

                return percent;
            }
            catch (Exception)
            {
                // Unstable by the vendor's own account: anything surprising means "unknown".
                return null;
            }
        }

        private static Int32? ReadTail(FileInfo file)
        {
            using var stream = new FileStream(
                file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            var take = (Int32)Math.Min(TailBytes, stream.Length);
            stream.Seek(-take, SeekOrigin.End);

            var buffer = new Byte[take];
            var read = stream.Read(buffer, 0, take);
            var text = Encoding.UTF8.GetString(buffer, 0, read);

            var lines = text.Split('\n');

            // Newest first: the last complete token_count wins. The first line is very likely a
            // fragment (we cut mid-file), which simply fails to parse and is skipped.
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var percent = PercentFromLine(lines[i]);
                if (percent != null)
                {
                    return percent;
                }
            }

            return null;
        }

        internal static Int32? PercentFromLine(String line)
        {
            if (String.IsNullOrWhiteSpace(line) || !line.Contains("token_count", StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);

                if (!doc.RootElement.TryGetProperty("payload", out var payload)
                    || payload.ValueKind != JsonValueKind.Object
                    || !payload.TryGetProperty("info", out var info)
                    || info.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                // ValueKind FIRST: TryGetInt64 throws rather than returning false when the element
                // is not a number, so a field that changes type would escape the JsonException
                // catch below. Precisely the kind of change this reader exists to survive.
                if (!info.TryGetProperty("model_context_window", out var windowEl)
                    || windowEl.ValueKind != JsonValueKind.Number
                    || !windowEl.TryGetInt64(out var window)
                    || window <= 0)
                {
                    return null;
                }

                if (!info.TryGetProperty("last_token_usage", out var usage)
                    || usage.ValueKind != JsonValueKind.Object
                    || !usage.TryGetProperty("total_tokens", out var usedEl)
                    || usedEl.ValueKind != JsonValueKind.Number
                    || !usedEl.TryGetInt64(out var used)
                    || used < 0)
                {
                    return null;
                }

                var percent = (Int32)Math.Round(100.0 * used / window);
                return Math.Clamp(percent, 0, 100);
            }
            catch (Exception)
            {
                // Deliberately broad. This parses a format the vendor calls unstable, on the poll
                // loop, inside an SDK callback — every surprise must read as "unknown", not throw.
                return null;
            }
        }
    }
}
