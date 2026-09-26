namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;

    internal enum WindowsHookHealthStatus
    {
        Healthy,
        Unavailable,
        AwaitingFresh,
    }

    /// <summary>
    /// Execution evidence for one product's packaged Windows hook. An existing file is not proof
    /// that it can execute, and restoring it cannot make state from before a failure current.
    /// Receipts come from successful, keyed IPC delivery; quiet healthy sessions never expire.
    /// </summary>
    internal sealed class WindowsHookHealth
    {
        private const Int32 MaxEvidenceFiles = 1024;
        private readonly Object _gate = new Object();
        private readonly String _helperPath;
        private readonly String _ipcRoot;
        private readonly Func<DateTime> _utcNow;
        private readonly Dictionary<String, Int64> _receipts = new Dictionary<String, Int64>(StringComparer.Ordinal);
        private Boolean _missingEpisode;
        private Boolean _unreadableEpisode;
        private Int64 _invalidatedTicks;
        private Int64 _helperVersionTicks;
        private String _failureReason;
        private Int64 _revision;
        private WindowsHookHealthStatus _status = WindowsHookHealthStatus.AwaitingFresh;
        private String _message = "Waiting for fresh hook information. Trigger an event in the coding session.";

        internal WindowsHookHealth(String expectedHelperPath, String ipcRoot, String productSlug, Func<DateTime> utcNow = null)
        {
            if (String.IsNullOrWhiteSpace(ipcRoot)) { throw new ArgumentException("An IPC root is required.", nameof(ipcRoot)); }
            if (String.IsNullOrWhiteSpace(productSlug)) { throw new ArgumentException("A product is required.", nameof(productSlug)); }
            this._helperPath = expectedHelperPath;
            this._ipcRoot = ipcRoot;
            this._utcNow = utcNow ?? (() => DateTime.UtcNow);
            this.HealthDirectory = String.IsNullOrWhiteSpace(expectedHelperPath) ? null : HealthDirectoryFor(expectedHelperPath, ipcRoot);
        }

        internal String HealthDirectory { get; }
        internal WindowsHookHealthStatus Status { get { lock (this._gate) { return this._status; } } }
        internal String Message { get { lock (this._gate) { return this._message; } } }
        internal DateTime InvalidatedAtUtc { get { lock (this._gate) { return new DateTime(this._invalidatedTicks, DateTimeKind.Utc); } } }
        internal DateTime FreshAfterUtc { get { lock (this._gate) { return new DateTime(Math.Max(this._invalidatedTicks, this._helperVersionTicks), DateTimeKind.Utc); } } }
        internal Int64 Revision { get { lock (this._gate) { return this._revision; } } }

        // Also implemented by the standalone helper and PowerShell launcher. The product's IPC
        // root separates products; the hash separates installed copies, upgrades and dev links.
        internal static String HealthDirectoryFor(String expectedHelperPath, String ipcRoot)
        {
            var path = Path.GetFullPath(expectedHelperPath).ToUpperInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
            return Path.Combine(ipcRoot, "hook-health", hash);
        }

        internal WindowsHookHealthStatus Refresh()
        {
            lock (this._gate) { return this.RefreshCore(); }
        }

        /// <summary>
        /// Requires this exact session's delivery after the latest failure. The caller refreshes
        /// at load, on its polling loop and before executing an action; painting stays free of I/O.
        /// Another tab's successful hook cannot authorize this tab.
        /// </summary>
        internal Boolean IsSessionObservationCurrent(String sessionKey)
        {
            lock (this._gate)
            {
                return this._status == WindowsHookHealthStatus.Healthy &&
                    IsSessionKey(sessionKey) && this._receipts.TryGetValue(sessionKey, out var started) &&
                    started > this._invalidatedTicks && started >= this._helperVersionTicks;
            }
        }

        // The timestamp belongs to the actual payload, not another event's success receipt.
        internal Boolean IsObservationStartedCurrent(Int64 startedTicks)
        {
            lock (this._gate)
            {
                return startedTicks > Math.Max(this._invalidatedTicks, this._helperVersionTicks) &&
                    startedTicks <= this._utcNow().AddSeconds(5).Ticks;
            }
        }

        // Rollout state may replace a hook envelope. The independent receipt still proves that
        // the currently installed hook ran, without permanently caching an old Active flag.
        internal Boolean HasCurrentObservationSince(DateTime installedAtUtc)
        {
            lock (this._gate)
            {
                return this._status == WindowsHookHealthStatus.Healthy &&
                    this._receipts.Values.Any(started => started >= installedAtUtc.Ticks &&
                        started > this._invalidatedTicks && started >= this._helperVersionTicks);
            }
        }

        /// <summary>
        /// The registry already discovers live processes. Use only a successful discovery result
        /// to retire dead-session receipts, never their age alone. A short grace period protects a
        /// hook for a process born just after the discovery snapshot. Failure barriers survive.
        /// </summary>
        internal void PruneDeadSessions(IEnumerable<String> liveKeys)
        {
            if (liveKeys == null || this.HealthDirectory == null) { return; }
            lock (this._gate)
            {
                try
                {
                    if (!Directory.Exists(this.HealthDirectory)) { return; }
                    var live = new HashSet<String>(liveKeys, StringComparer.Ordinal);
                    var cutoff = this._utcNow().AddMinutes(-1).Ticks;
                    foreach (var file in Directory.EnumerateFiles(this.HealthDirectory, "success-*.json").Take(MaxEvidenceFiles))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        var session = name.Substring("success-".Length);
                        if (!IsSessionKey(session) || live.Contains(session)) { continue; }
                        using var json = ReadRecord(file);
                        if (json != null && ReadTicks(json.RootElement, "completedUtcTicks", out var completed) && completed < cutoff &&
                            json.RootElement.TryGetProperty("sessionKey", out var key) && key.ValueKind == JsonValueKind.String && key.GetString() == session)
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
                catch { /* Cleanup cannot make an otherwise healthy session unavailable. */ }
            }
        }

        private WindowsHookHealthStatus RefreshCore()
        {
            var previous = new Dictionary<String, Int64>(this._receipts, StringComparer.Ordinal);
            var previousStatus = this._status;
            var previousFailure = this._invalidatedTicks;
            var previousVersion = this._helperVersionTicks;
            var status = this.RefreshEvidence();
            if (previousStatus != status || previousFailure != this._invalidatedTicks || previousVersion != this._helperVersionTicks ||
                previous.Count != this._receipts.Count || previous.Any(pair => !this._receipts.TryGetValue(pair.Key, out var value) || value != pair.Value))
            {
                ++this._revision;
            }
            return status;
        }

        private WindowsHookHealthStatus RefreshEvidence()
        {
            this._receipts.Clear();
            var evidenceReadable = this.ReadEvidence();
            if (String.IsNullOrWhiteSpace(this._helperPath) || !File.Exists(this._helperPath))
            {
                if (!this._missingEpisode)
                {
                    this._missingEpisode = true;
                    this.RecordFailure("missing");
                }
                return this.SetStatus(WindowsHookHealthStatus.Unavailable,
                    "Windows hook helper is missing or unavailable. Check security software or contact IT; after recovery, trigger a fresh hook event.");
            }

            this._missingEpisode = false;
            try { this._helperVersionTicks = File.GetLastWriteTimeUtc(this._helperPath).Ticks; }
            catch { evidenceReadable = false; }

            if (!evidenceReadable)
            {
                if (!this._unreadableEpisode)
                {
                    this._unreadableEpisode = true;
                    this.RecordFailure("health-unreadable");
                }
                return this.SetStatus(WindowsHookHealthStatus.Unavailable,
                    "Windows hook health cannot be read. Approval actions are blocked until fresh hook information can be verified.");
            }
            this._unreadableEpisode = false;
            if (this._receipts.Values.Any(started => started > this._invalidatedTicks && started >= this._helperVersionTicks))
            {
                return this.SetStatus(WindowsHookHealthStatus.Healthy, String.Empty);
            }
            if (this._invalidatedTicks > 0 && this._failureReason != "missing")
            {
                return this.SetStatus(WindowsHookHealthStatus.Unavailable,
                    "Windows hook helper could not complete delivery. Check antivirus detections and trigger a fresh hook event after recovery.");
            }
            return this.SetStatus(WindowsHookHealthStatus.AwaitingFresh,
                "Waiting for fresh hook information. Trigger an event in the coding session.");
        }

        private WindowsHookHealthStatus SetStatus(WindowsHookHealthStatus status, String message)
        {
            this._status = status;
            this._message = message;
            return status;
        }

        private Boolean ReadEvidence()
        {
            if (this.HealthDirectory == null) { return false; }
            try
            {
                if (!Directory.Exists(this.HealthDirectory)) { return true; }
                var failures = Directory.EnumerateFiles(this.HealthDirectory, "failure-*.json").Take(MaxEvidenceFiles + 1).ToArray();
                var successes = Directory.EnumerateFiles(this.HealthDirectory, "success-*.json").Take(MaxEvidenceFiles + 1).ToArray();
                if (failures.Length > MaxEvidenceFiles || successes.Length > MaxEvidenceFiles) { return false; }
                foreach (var file in failures)
                {
                    using var json = ReadRecord(file);
                    if (json == null || !ReadTicks(json.RootElement, "observedUtcTicks", out var observed) ||
                        !json.RootElement.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }
                    if (observed > this._invalidatedTicks)
                    {
                        this._invalidatedTicks = observed;
                        this._failureReason = reason.GetString();
                    }
                }

                foreach (var file in successes)
                {
                    using var json = ReadRecord(file);
                    if (json == null || !ReadTicks(json.RootElement, "startedUtcTicks", out var started) ||
                        !ReadTicks(json.RootElement, "completedUtcTicks", out var completed) || completed < started ||
                        !json.RootElement.TryGetProperty("sessionKey", out var key) || key.ValueKind != JsonValueKind.String ||
                        !json.RootElement.TryGetProperty("event", out var hookEvent) || hookEvent.ValueKind != JsonValueKind.String ||
                        String.IsNullOrWhiteSpace(hookEvent.GetString()))
                    {
                        continue;
                    }
                    var session = key.GetString();
                    if (IsSessionKey(session) && Path.GetFileName(file) == "success-" + session + ".json")
                    {
                        this._receipts[session] = started;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        private void RecordFailure(String reason)
        {
            var observed = Math.Max(this._utcNow().Ticks, this._invalidatedTicks);
            this._invalidatedTicks = observed;
            this._failureReason = reason;
            if (this.HealthDirectory == null) { return; }
            String temporary = null;
            try
            {
                PrivateFiles.EnsurePrivateDirectory(this._ipcRoot);
                PrivateFiles.EnsurePrivateDirectory(Path.GetDirectoryName(this.HealthDirectory));
                PrivateFiles.EnsurePrivateDirectory(this.HealthDirectory);
                var path = Path.Combine(this.HealthDirectory, $"failure-{observed}-{Guid.NewGuid():N}.json");
                temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(new { schema = 1, observedUtcTicks = observed, reason }));
                PrivateFiles.EnsurePrivateFile(temporary);
                File.Move(temporary, path);
                // Immutable records avoid writer races. Keep a small tail, preserving the latest
                // failure even if an older invocation finally publishes its record afterwards.
                foreach (var old in Directory.EnumerateFiles(this.HealthDirectory, "failure-*.json")
                    .Take(MaxEvidenceFiles + 1).OrderByDescending(p => Path.GetFileName(p), StringComparer.Ordinal).Skip(16))
                {
                    try { File.Delete(old); } catch { }
                }
            }
            catch { /* Keep the in-memory barrier even when the marker cannot be persisted. */ }
            finally
            {
                if (temporary != null) { try { File.Delete(temporary); } catch { } }
            }
        }

        private static JsonDocument ReadRecord(String path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.LinkTarget != null || info.Length > 4096) { return null; }
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var bytes = new Byte[4097];
                var read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
                if (read > 4096) { return null; }
                var document = JsonDocument.Parse(bytes.AsMemory(0, read));
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("schema", out var schema) && schema.TryGetInt32(out var version) && version == 1)
                {
                    return document;
                }
                document.Dispose();
            }
            catch { }
            return null;
        }

        private Boolean ReadTicks(JsonElement record, String name, out Int64 ticks)
        {
            ticks = 0;
            return record.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out ticks) &&
                ticks > 0 && ticks <= this._utcNow().AddSeconds(5).Ticks;
        }

        private static Boolean IsSessionKey(String key)
        {
            if (String.IsNullOrEmpty(key)) { return false; }
            var parts = key.Split('-');
            return parts.Length == 3 && parts[0] == "pid" &&
                parts[1].All(Char.IsAsciiDigit) && Int32.TryParse(parts[1], out var pid) && pid > 0 &&
                parts[2].All(Char.IsAsciiDigit) && Int64.TryParse(parts[2], out var started) && started > 0;
        }
    }
}
