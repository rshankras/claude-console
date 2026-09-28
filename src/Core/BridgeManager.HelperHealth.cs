namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    public partial class BridgeManager
    {
        private readonly Object _helperHealthLock = new Object();
        private WindowsHookHealth _hookHealth;
        private Boolean _hookHealthInjected;
        private String _hookHealthIdentity;
        private Int64 _seenHealthRevision = -1;
        private DateTime _loggedDeliveryFailureUtc = DateTime.MinValue;
        private AgentBridgeStatus _publishedBridgeState = AgentBridgeStatus.Ready;
        private readonly System.Collections.Generic.HashSet<String> _deliveryLogged = new(StringComparer.Ordinal);

        /// <summary>Inject isolated Windows health evidence without changing the host platform.</summary>
        internal WindowsHookHealth HookHealth
        {
            get => _hookHealth;
            set
            {
                lock (_helperHealthLock)
                {
                    _hookHealth = value;
                    _hookHealthInjected = true;
                    _seenHealthRevision = -1;
                }
            }
        }

        internal event Action OnHelperHealthChanged;

        private Boolean HookHealthApplies => _hookHealth != null &&
            (!this.LiveStatusApplies || _liveStatus is LiveStatusState.Enabled or LiveStatusState.JustEnabled);

        // Only an observed helper failure is "unavailable". AwaitingFresh — no receipts yet — is
        // the normal state of a fresh install, a new day, an upgraded exe, or no session open,
        // and must never put Blocked on the keys or a warning in Options+. Actions still gate on
        // per-session freshness below; they just refuse quietly. A real failure overrides even a
        // cached Codex trust status and cannot be hidden by it.
        internal Boolean HookHelperUnavailable => this.HookHealthApplies &&
            _hookHealth.Status == WindowsHookHealthStatus.Unavailable;

        /// <summary>
        /// Build and latch the helper-health monitor before a product reads a status through it.
        /// VizhiCodexPlugin.Load() evaluates the Codex bridge status BEFORE StartPolling(), which
        /// was where the monitor was first built — so that first evaluation had no latched floor
        /// and fell back to the exe's raw mtime: the future-dated time #125 exists to ignore. On a
        /// machine behind the packing zone every service start inside the window flashed Run /hooks
        /// and its Options+ card until the first poll corrected it (found in review, 2026-09-28).
        /// The same local file checks as RefreshHelperHealth; a no-op on macOS. Call it BEFORE
        /// subscribing to OnHelperHealthChanged: the monitor's first refresh raises that event, and a
        /// status refresh ahead of EnsureInstalled is the #69 race.
        /// </summary>
        internal void PrimeHelperHealth() => this.RefreshHelperHealth();

        /// <summary>
        /// Local file checks only: called at load, on the existing poll, and on key presses. Never
        /// launches a probe or depends on event age; a quiet working session stays working.
        /// </summary>
        internal void RefreshHelperHealth()
        {
            Boolean changed;
            lock (_helperHealthLock)
            {
                if (!_hookHealthInjected && OperatingSystem.IsWindows())
                {
                    var directory = PluginPaths.PluginDirectory;
                    if (!String.IsNullOrEmpty(directory))
                    {
                        // PackagedFile returns null after quarantine. Keep the expected path so
                        // absence is an observable failure, not a reason to skip the check.
                        var expected = Path.Combine(directory, "claude-console-hook.exe");
                        var identity = expected + "|" + IpcPaths.Root;
                        if (_hookHealthIdentity != identity)
                        {
                            _hookHealth = new WindowsHookHealth(expected, IpcPaths.Root, this.Agent.ProductSlug);
                            _hookHealthIdentity = identity;
                            _seenHealthRevision = -1;
                        }
                    }
                }

                if (_hookHealth == null) { return; }
                _hookHealth.Refresh();
                changed = _seenHealthRevision != _hookHealth.Revision;
                _seenHealthRevision = _hookHealth.Revision;
            }

            this.PublishBridgeHealth();
            if (!changed) { return; }

            // A delivery failure is diagnostic only: the exe ran. Say so once per new record, so
            // a forfeited Codex payload or a slow stdin leaves a trail without blocking anything.
            var delivery = _hookHealth.LastDeliveryFailure;
            if (delivery.HasValue && delivery.Value.ObservedUtc > _loggedDeliveryFailureUtc)
            {
                _loggedDeliveryFailureUtc = delivery.Value.ObservedUtc;
                PluginLog.Info($"Windows hook: a hook ran but could not complete delivery ({delivery.Value.Reason}) at {delivery.Value.ObservedUtc:O} — not a helper failure; that invocation's receipt was withheld");
            }

            if (this.HookHelperUnavailable)
            {
                // Reset only when invalidated, so recovery republishes even identical values.
                // A normal activity receipt must not force an unchanged cost snapshot to repaint.
                _lastStateText = null;
                _currentState = null;
                _activity = null;
                _displayStateKnown = false;
                OnStateUnavailable?.Invoke();
                OnActivityChanged?.Invoke(null);
            }
            OnHelperHealthChanged?.Invoke();
        }

        internal Boolean IsSessionObservationCurrent(String sessionKey) =>
            _hookHealth == null || (this.HookHealthApplies &&
                !this.HookHelperUnavailable && _hookHealth.IsSessionObservationCurrent(sessionKey));

        /// <summary>
        /// Give every session whose state is a helper-stamped hook envelope a receipt (#131). Only
        /// for an agent whose hooks may report another terminal: there the helper cannot key the
        /// session and never writes one, so the registry's attribution (#129) is the only thing
        /// that says whose delivery it was. Rollout-derived state is not helper execution and is
        /// skipped. Runs after each grid refresh; idempotent, and a no-op off Windows.
        /// </summary>
        internal Boolean RegisterAttributedDeliveries()
        {
            if (_hookHealth == null || this.Agent?.Capabilities.HooksMayReportAnotherTerminal != true)
            {
                return false;
            }

            var changed = false;
            foreach (var session in Grid.Sessions.Values.ToList())
            {
                var delivery = HookDelivery(session.StateSourceRaw);
                if (delivery.HasValue && _hookHealth.NoteDelivery(session.SessionKey, delivery.Value.StartedUtcTicks, delivery.Value.Event))
                {
                    changed = true;
                    // Once per session: a busy session advances its stamp on every tool call.
                    if (_deliveryLogged.Add(session.SessionKey))
                    {
                        PluginLog.Info($"Windows hook: recording hook deliveries for {session.SessionKey} from its state envelopes ({delivery.Value.Event} first) — the helper could not write a receipt for this session itself");
                    }
                }
            }

            if (changed) { this.RefreshHelperHealth(); }
            return changed;
        }

        // The helper's stamp on a hook-transport envelope; null for rollout state or anything unstamped.
        private static (Int64 StartedUtcTicks, String Event)? HookDelivery(String raw)
        {
            if (String.IsNullOrEmpty(raw)) { return null; }
            try
            {
                using var json = JsonDocument.Parse(raw);
                var record = json.RootElement;
                if (record.ValueKind != JsonValueKind.Object) { return null; }
                var transport = record.TryGetProperty("transport", out var source) && source.ValueKind == JsonValueKind.String
                    ? source.GetString() : null;
                if (!String.IsNullOrEmpty(transport) && transport != "hook") { return null; }
                // The helper withholds its own receipt when the payload never arrived (stdin timed
                // out, payload null) and records a delivery failure instead; mirror that, or such a
                // run would retire a real helper failure (PR #133 review).
                if (!record.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) { return null; }
                if (!record.TryGetProperty("hookStartedUtcTicks", out var started) || started.ValueKind != JsonValueKind.Number ||
                    !started.TryGetInt64(out var ticks) || ticks <= 0) { return null; }
                var evt = record.TryGetProperty("event", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
                return (ticks, String.IsNullOrWhiteSpace(evt) ? "hook" : evt);
            }
            catch { return null; }
        }

        private String CurrentObservationFile(String path, String sessionKey)
        {
            if (_hookHealth == null) { return path; }
            if (path == null || !this.IsSessionObservationCurrent(sessionKey)) { return null; }
            try
            {
                // A fresh activity event proves execution, but cannot make a cost/status-line
                // snapshot from before the failure current (or vice versa).
                return File.GetLastWriteTimeUtc(path) > _hookHealth.FreshAfterUtc ? path : null;
            }
            catch { return null; }
        }

        internal Boolean IsObservationPayloadCurrent(String payload, String sessionKey)
        {
            if (_hookHealth == null) { return true; }
            if (!this.IsSessionObservationCurrent(sessionKey) || String.IsNullOrEmpty(payload)) { return false; }
            try
            {
                using var json = JsonDocument.Parse(payload);
                var record = json.RootElement;
                var transport = record.TryGetProperty("transport", out var source) && source.ValueKind == JsonValueKind.String
                    ? source.GetString() : null;
                if (transport is "rollout" or "rollout-code-mode")
                {
                    if (!CurrentTicks("observationStartedUtcTicks")) { return false; }
                    var isApproval = record.TryGetProperty("event", out var evt) && evt.ValueKind == JsonValueKind.String &&
                        evt.GetString() == "PermissionRequest";
                    return !isApproval || (transport == "rollout-code-mode" && CurrentTicks("rolloutEventUtcTicks"));
                }
                return CurrentTicks("hookStartedUtcTicks");

                Boolean CurrentTicks(String name) => record.TryGetProperty(name, out var started) &&
                    started.ValueKind == JsonValueKind.Number && started.TryGetInt64(out var ticks) &&
                    _hookHealth.IsObservationStartedCurrent(ticks);
            }
            catch { return false; }
        }

        internal Boolean IsSessionApprovalCurrent(String sessionKey)
        {
            if (_hookHealth == null) { return true; }
            return this.IsSessionObservationCurrent(sessionKey) &&
                sessionKey != null && this.Grid.Sessions.TryGetValue(sessionKey, out var session) &&
                !String.IsNullOrEmpty(session.PendingTool) &&
                session.ApprovalObservedAtUtc.HasValue &&
                session.ApprovalObservedAtUtc.Value > _hookHealth.FreshAfterUtc &&
                session.ApprovalObservationStartedAtUtc.HasValue && session.ApprovalSourceEventAtUtc.HasValue &&
                _hookHealth.IsObservationStartedCurrent(session.ApprovalObservationStartedAtUtc.Value.Ticks) &&
                _hookHealth.IsObservationStartedCurrent(session.ApprovalSourceEventAtUtc.Value.Ticks);
        }

        internal Boolean IsSessionActivityCurrent(String sessionKey)
        {
            if (_hookHealth == null) { return true; }
            return this.IsSessionObservationCurrent(sessionKey) &&
                sessionKey != null && this.Grid.Sessions.TryGetValue(sessionKey, out var session) &&
                session.StateObservationStartedAtUtc.HasValue &&
                _hookHealth.IsObservationStartedCurrent(session.StateObservationStartedAtUtc.Value.Ticks);
        }

        internal InjectionOutcome InjectApprovalTo(String sessionKey, KeyStroke key) => this.RunRoutedInput(() =>
        {
            this.RefreshHelperHealth();
            if (this.AgentBridgeState != AgentBridgeStatus.Ready ||
                !this.IsSessionApprovalCurrent(sessionKey) ||
                (_hookHealth != null && !this.Grid.IsApprovalSourceCurrent(sessionKey)))
            {
                return InjectionOutcome.Skipped;
            }
            return _platform.InjectKey(sessionKey, key);
        });

        private void PublishBridgeHealth()
        {
            var state = this.AgentBridgeState;
            if (_publishedBridgeState != state)
            {
                _publishedBridgeState = state;
                PluginLog.Info($"Windows hook / agent bridge state: {state}");
                OnAgentBridgeStatusChanged?.Invoke(state);
            }
            this.PublishPluginStatus();
        }

        // Agent/helper notices are derived from their state, independently of other notices.
        // Recovering the bridge must not clear a voice or terminal failure belonging to another
        // subsystem, even when Codex briefly returns to AwaitingTrust during recovery.
        private sealed record PluginNotice(PluginStatus Status, String Message, String Url, String Title);
        private readonly Object _notificationLock = new Object();
        private Action<PluginStatus, String, String, String> _notificationSink;
        private PluginNotice _otherNotice = new PluginNotice(PluginStatus.Normal, null, null, null);
        private PluginNotice _publishedNotice;

        private void ReportPluginStatus(PluginStatus status, String message, String url, String title)
        {
            lock (_notificationLock)
            {
                _otherNotice = new PluginNotice(status, message, url, title);
            }
            this.PublishPluginStatus(forceOtherNotice: true);
        }

        private void PublishPluginStatus(Boolean forceOtherNotice = false)
        {
            lock (_notificationLock)
            {
                if (_notificationSink == null) { return; }
                var bridgeState = this.AgentBridgeState;
                var bridgeNotice = bridgeState != AgentBridgeStatus.Ready && _otherNotice.Status != PluginStatus.Error;
                var notice = bridgeNotice
                    ? new PluginNotice(PluginStatus.Warning, AgentBridgeNotice.Message(bridgeState),
                        bridgeState == AgentBridgeStatus.HelperUnavailable ? BridgeNotice.SupportUrl : AgentBridgeNotice.PublicHelpUrl,
                        AgentBridgeNotice.Title(bridgeState))
                    : _otherNotice;
                // Explicit action notices may begin a new user interaction with identical words.
                // Deduplicate automatic health polling, without suppressing those new episodes.
                if (notice == _publishedNotice && (!forceOtherNotice || bridgeNotice)) { return; }
                _publishedNotice = notice;
                _notificationSink(notice.Status, notice.Message, notice.Url, notice.Title);
            }
        }
    }
}
