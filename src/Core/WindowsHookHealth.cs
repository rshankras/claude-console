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
    ///
    /// Three answers, and only one of them is a problem:
    ///   Unavailable   — the newest thing we saw was the HELPER failing (file missing, would not
    ///                   launch, crashed) and no hook has succeeded since. Keys say Blocked and
    ///                   Options+ gets a warning.
    ///   Healthy       — a live session delivered after the last such failure.
    ///   AwaitingFresh — no failure since the last success, but no live receipt either: a fresh
    ///                   install, a new day (receipts die with their sessions), an upgraded exe,
    ///                   or no session open. Not a failure; the keys keep their neutral faces.
    ///
    /// Failure records carry a scope. "helper" failures move the recovery barrier; "delivery"
    /// failures (a hook that ran but could not complete one write, a slow stdin) only withhold
    /// that invocation's receipt — the exe ran, so the helper is not blocked.
    /// </summary>
    internal sealed class WindowsHookHealth
    {
        private const Int32 MaxEvidenceFiles = 1024;
        private const String WatermarkFile = "last-success.json";
        private readonly Object _gate = new Object();
        private readonly String _helperPath;
        private readonly String _ipcRoot;
        private readonly Func<DateTime> _utcNow;
        private readonly Dictionary<String, Int64> _receipts = new Dictionary<String, Int64>(StringComparer.Ordinal);
        private Boolean _missingEpisode;
        private Boolean _unreadableEpisode;
        private Int64 _invalidatedTicks;
        private Int64 _helperVersionTicks;
        // The newest success ever seen, live or pruned. A failure older than this is history,
        // not the current state; without it every reboot after any past failure would read as
        // Unavailable once the receipts of yesterday's sessions were pruned.
        private Int64 _lastSuccessStartedTicks;
        private Int64 _lastDeliveryFailureTicks;
        private String _failureReason;
        private String _deliveryFailureReason;
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

        /// <summary>The newest delivery-scope failure seen, for the log; it never blocks anything.</summary>
        internal (DateTime ObservedUtc, String Reason)? LastDeliveryFailure
        {
            get
            {
                lock (this._gate)
                {
                    return this._lastDeliveryFailureTicks > 0
                        ? (new DateTime(this._lastDeliveryFailureTicks, DateTimeKind.Utc), this._deliveryFailureReason)
                        : null;
                }
            }
        }

        // Also implemented by the standalone helper (from Environment.ProcessPath); the PowerShell
        // launcher gets the finished directory name baked in at wiring time, so it carries no
        // hashing code. The product's IPC root separates products; the hash separates installed
        // copies, upgrades and dev links.
        internal static String HealthDirectoryFor(String expectedHelperPath, String ipcRoot) =>
            Path.Combine(ipcRoot, "hook-health", HealthDirectoryName(expectedHelperPath));

        internal static String HealthDirectoryName(String expectedHelperPath)
        {
            var path = Path.GetFullPath(expectedHelperPath).ToUpperInvariant();
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
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
        /// hook for a process born just after the discovery snapshot. Failure barriers survive,
        /// and so does the fact that the helper once worked: a pruned receipt is folded into the
        /// last-success watermark before it goes.
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
                    var watermark = this.ReadWatermark();
                    foreach (var file in Directory.EnumerateFiles(this.HealthDirectory, "success-*.json").Take(MaxEvidenceFiles))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        var session = name.Substring("success-".Length);
                        if (!IsSessionKey(session) || live.Contains(session)) { continue; }
                        using var json = ReadRecord(file);
                        if (json != null && ReadTicks(json.RootElement, "completedUtcTicks", out var completed) && completed < cutoff &&
                            json.RootElement.TryGetProperty("sessionKey", out var key) && key.ValueKind == JsonValueKind.String && key.GetString() == session)
                        {
                            if (ReadTicks(json.RootElement, "startedUtcTicks", out var started) && started > watermark)
                            {
                                watermark = started;
                                this.WriteWatermark(started, completed);
                            }
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
            // Unavailable means "the newest evidence is a helper failure". A success that started
            // after the failure — even one whose session has since ended — retires it. Restoring
            // a quarantined file is not such a success: the next hook has to actually run.
            // A failure recorded against an older file than the one installed now is history too:
            // an upgrade or reinstall starts from AwaitingFresh, not Blocked. (A quarantined file
            // put back by IT keeps its original, older mtime, so that path stays Unavailable.)
            var lastSuccess = Math.Max(this._lastSuccessStartedTicks,
                this._receipts.Count == 0 ? 0 : this._receipts.Values.Max());
            if (this._invalidatedTicks > 0 && lastSuccess <= this._invalidatedTicks && this._helperVersionTicks <= this._invalidatedTicks)
            {
                return this.SetStatus(WindowsHookHealthStatus.Unavailable, this._failureReason == "missing"
                    ? "Windows hook helper is missing or unavailable. Check security software or contact IT; after recovery, trigger a fresh hook event."
                    : "Windows hook helper could not run. Check antivirus detections and trigger a fresh hook event after recovery.");
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
                var failures = TrimFailures(this.HealthDirectory);
                var successes = Directory.EnumerateFiles(this.HealthDirectory, "success-*.json").Take(MaxEvidenceFiles + 1).ToArray();
                if (failures.Length > MaxEvidenceFiles || successes.Length > MaxEvidenceFiles) { return false; }
                this._lastSuccessStartedTicks = Math.Max(this._lastSuccessStartedTicks, this.ReadWatermark());
                foreach (var file in failures)
                {
                    using var json = ReadRecord(file);
                    if (json == null || !ReadTicks(json.RootElement, "observedUtcTicks", out var observed) ||
                        !json.RootElement.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }
                    // Records written before scopes existed are the launcher's: helper scope.
                    var delivery = json.RootElement.TryGetProperty("scope", out var scope) &&
                        scope.ValueKind == JsonValueKind.String && scope.GetString() == "delivery";
                    if (delivery)
                    {
                        if (observed > this._lastDeliveryFailureTicks)
                        {
                            this._lastDeliveryFailureTicks = observed;
                            this._deliveryFailureReason = reason.GetString();
                        }
                    }
                    else if (observed > this._invalidatedTicks)
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
                File.WriteAllText(temporary, JsonSerializer.Serialize(new { schema = 1, observedUtcTicks = observed, reason, scope = "helper" }));
                PrivateFiles.EnsurePrivateFile(temporary);
                File.Move(temporary, path);
                TrimFailures(this.HealthDirectory);
            }
            catch { /* Keep the in-memory barrier even when the marker cannot be persisted. */ }
            finally
            {
                if (temporary != null) { try { File.Delete(temporary); } catch { } }
            }
        }

        // Immutable records avoid writer races; somebody still has to sweep. The plugin does it on
        // every read, so the launcher (six copies of it, in settings.json) carries no cleanup
        // code, and a burst of markers written while the service was down is trimmed the moment
        // it is back. Newest 16 by name — the name starts with the observed ticks — so the latest
        // failure survives even if an older invocation finally publishes its record afterwards.
        private static String[] TrimFailures(String directory)
        {
            var failures = Directory.EnumerateFiles(directory, "failure-*.json").Take(MaxEvidenceFiles + 1)
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToArray();
            foreach (var old in failures.Skip(16))
            {
                try { File.Delete(old); } catch { }
            }
            return failures.Take(16).ToArray();
        }

        // {schema, startedUtcTicks, completedUtcTicks} of the newest pruned receipt. Written only
        // by the plugin, only while pruning, only when it moves forward.
        private Int64 ReadWatermark()
        {
            try
            {
                var path = Path.Combine(this.HealthDirectory, WatermarkFile);
                if (!File.Exists(path)) { return 0; }
                using var json = ReadRecord(path);
                return json != null && ReadTicks(json.RootElement, "startedUtcTicks", out var started) ? started : 0;
            }
            catch { return 0; }
        }

        private void WriteWatermark(Int64 started, Int64 completed)
        {
            this._lastSuccessStartedTicks = Math.Max(this._lastSuccessStartedTicks, started);
            var path = Path.Combine(this.HealthDirectory, WatermarkFile);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(new { schema = 1, startedUtcTicks = started, completedUtcTicks = completed }));
                PrivateFiles.EnsurePrivateFile(temporary);
                File.Move(temporary, path, overwrite: true);
            }
            catch { /* The in-memory watermark still covers this process; the next prune retries. */ }
            finally { try { File.Delete(temporary); } catch { } }
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
