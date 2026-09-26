namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.IO;
    using System.Text.Json;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    public partial class BridgeManager
    {
        private readonly Object _helperHealthLock = new Object();
        private WindowsHookHealth _hookHealth;
        private Boolean _hookHealthInjected;
        private String _hookHealthIdentity;
        private Int64 _seenHealthRevision = -1;
        private AgentBridgeStatus _publishedBridgeState = AgentBridgeStatus.Ready;

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

        internal Boolean HookHelperUnavailable => this.HookHealthApplies &&
            _hookHealth.Status != WindowsHookHealthStatus.Healthy &&
            // Before any event, retain Codex's normal trust/setup instructions. A real failure
            // overrides even a previously cached trust status, and cannot be hidden by it.
            ((_agentBridgeStatus == AgentBridgeStatus.Ready && _liveStatus != LiveStatusState.JustEnabled) ||
             _hookHealth.Status == WindowsHookHealthStatus.Unavailable ||
             _hookHealth.InvalidatedAtUtc > DateTime.MinValue);

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

        internal InjectionOutcome InjectApprovalTo(String sessionKey, KeyStroke key)
        {
            this.RefreshHelperHealth();
            if (this.AgentBridgeState != AgentBridgeStatus.Ready ||
                !this.IsSessionApprovalCurrent(sessionKey) ||
                (_hookHealth != null && !this.Grid.IsApprovalSourceCurrent(sessionKey)))
            {
                return InjectionOutcome.Skipped;
            }
            return _platform.InjectKey(sessionKey, key);
        }

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
