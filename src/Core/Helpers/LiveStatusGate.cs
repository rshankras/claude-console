namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Threading;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// What a live key (Cost / Context / Activity) shows and does about live status (#31). One
    /// owner for the three keys, so they cannot drift: the same words for the same state, the same
    /// gestures, and a repaint only when the words change (#27).
    ///
    /// The key IS the switch — nothing to drag, nothing to find in a menu:
    ///  - Off / never set up: a press changes NOTHING. It arms this key for a short window, flashes
    ///    "Press again", posts a card in Options+ as the record and, where the product can, opens a
    ///    dialog on screen naming the exact change with "Not now" / "Turn on". "Turn on", or a
    ///    second press of the SAME key inside the window, enables — the surgical merge in
    ///    BridgeManager.EnableLiveStatus. "Not now", silence, or a late press: nothing.
    ///  - On: a short press does the key's own job (on release — see HandleButton). A LONG press
    ///    asks the mirror question, "Keep" / "Turn off"; "Turn off", or a second long press inside
    ///    the window, runs DisableLiveStatus. A long press before setup counts as a first press.
    ///
    /// The key owns its button events: the service would otherwise run the short action the
    /// instant the key goes down, before it can know a long press is coming.
    ///
    /// Inert where the agent has no settings file to consent to (Codex): the keys show their own
    /// values and every press is theirs.
    /// </summary>
    internal sealed class LiveStatusGate
    {
        // Long enough to read the dialog and answer at a human pace — on the device, unhurried
        // second presses landed at 11 and 12 s, just outside a 10 s window — short enough that a
        // press on Monday and a press on Tuesday are two first presses.
        private const Int32 DefaultArmWindowMs = 15_000;

        private readonly BridgeManager _bridge;
        private readonly String _keyName;
        private readonly FailureFace _flash;
        private readonly Action _repaint;
        private readonly Int32 _armWindowMs;
        private readonly Boolean _applies;
        private readonly Object _lock = new Object();
        private DateTime _armedUntil;             // a second press before this enables; per key, on purpose
        private DateTime _offArmedUntil;          // a second long press before this disables
        private CancellationTokenSource _pending; // the open dialog, if any — cancelled by a key press
        private Boolean _longPressed;             // this hold already fired LongPress; Release must not act
        private String _label;

        /// <param name="applies">Test seam: whether this agent has a settings file to consent to.
        /// Null reads it from the agent (Capabilities.SettingsFileWiring).</param>
        public LiveStatusGate(BridgeManager bridge, String keyName, Action repaint, Int32 armWindowMs = DefaultArmWindowMs, Boolean? applies = null)
        {
            _bridge = bridge;
            _keyName = keyName;
            _repaint = repaint;
            _armWindowMs = armWindowMs;
            _applies = applies ?? (bridge.Agent?.Capabilities.SettingsFileWiring ?? false);
            _flash = new FailureFace(repaint, armWindowMs);
            _label = this.StateLabel();
            bridge.OnLiveStatusChanged += _ => this.Refresh();
            bridge.OnAgentBridgeStatusChanged += _ => this.Refresh();
            bridge.OnHelperHealthChanged += this.Refresh;
        }

        /// <summary>The words that replace the key's live value right now, or null when the key shows its own.</summary>
        public String Label => this.HealthLabel()
            ?? (!_applies ? null : (_flash.IsActive ? _flash.Text : this.StateLabel()));

        /// <summary>True while a press belongs to setup rather than to the key's own job.</summary>
        public Boolean NeedsSetup => _applies && LiveStatusFace.NeedsSetup(_bridge.LiveStatus);

        /// <summary>True while a second press would enable.</summary>
        public Boolean Armed => DateTime.UtcNow < _armedUntil;

        /// <summary>True while a second long press would disable.</summary>
        public Boolean OffArmed => DateTime.UtcNow < _offArmedUntil;

        /// <summary>
        /// Route one button event from the SDK. Always returns true — the key owns its button. The
        /// short action (<paramref name="shortPress"/>) runs on Release when no long press happened
        /// during the hold and live status does not need setting up.
        /// </summary>
        public Boolean HandleButton(DeviceButtonEventType type, Action shortPress, Action longPress = null)
        {
            switch (type)
            {
                case DeviceButtonEventType.Press:
                    _bridge.RefreshHelperHealth();
                    _longPressed = false;
                    return true;

                case DeviceButtonEventType.LongPress:
                    _longPressed = true;
                    if (_applies)
                    {
                        this.LongPress();
                    }
                    else
                    {
                        longPress?.Invoke();
                    }
                    return true;

                case DeviceButtonEventType.Release:
                    var wasLong = _longPressed;
                    _longPressed = false;
                    if (!wasLong && !this.Press())
                    {
                        shortPress?.Invoke();
                    }
                    return true;

                default:
                    return true;   // RepeatPress and anything else: nothing to do
            }
        }

        /// <summary>
        /// Handle a short press. Returns true when the press belonged to setup (armed, or enabled)
        /// and the key must not act on it; false when live status is on and the key should do its
        /// own job.
        /// </summary>
        public Boolean Press()
        {
            _bridge.RefreshHelperHealth();
            if (!this.NeedsSetup)
            {
                return false;
            }

            lock (_lock)
            {
                if (this.Armed)
                {
                    this.SettleLocked(enable: true, "pressed again within the window");
                    return true;
                }
                _armedUntil = DateTime.UtcNow.AddMilliseconds(_armWindowMs);
                _offArmedUntil = DateTime.MinValue;
            }

            var seconds = _armWindowMs / 1000;
            _flash.Show(LiveStatusFace.PressHint);
            _bridge.Notify?.Invoke(PluginStatus.Warning, BridgeNotice.PressAgain(_keyName, seconds), BridgeNotice.SupportUrl, BridgeNotice.SupportTitle);
            if (_bridge.Prompt != null)
            {
                this.AskInBackground(BridgeNotice.TurnOnDialog(_keyName, seconds), "Turn on", "Not now", enable: true, seconds);
            }
            else
            {
                _bridge.Toast?.Invoke("Turn on live status?", BridgeNotice.PressAgain(_keyName, seconds));
            }
            PluginLog.Info($"Live status: {_keyName} pressed before setup — nothing changed; Turn on, or a second press within {seconds}s, enables");
            return true;
        }

        /// <summary>A long press: the way off. Before setup it is just a first press.</summary>
        public void LongPress()
        {
            if (!_applies)
            {
                return;
            }
            if (this.NeedsSetup)
            {
                this.Press();
                return;
            }

            lock (_lock)
            {
                if (this.OffArmed)
                {
                    this.SettleLocked(enable: false, "long-pressed again within the window");
                    return;
                }
                _offArmedUntil = DateTime.UtcNow.AddMilliseconds(_armWindowMs);
                _armedUntil = DateTime.MinValue;
            }

            var seconds = _armWindowMs / 1000;
            _flash.Show(LiveStatusFace.OffHint);
            _bridge.Notify?.Invoke(PluginStatus.Warning, BridgeNotice.LongPressAgain(_keyName, seconds), BridgeNotice.SupportUrl, BridgeNotice.SupportTitle);
            if (_bridge.Prompt != null)
            {
                this.AskInBackground(BridgeNotice.TurnOffDialog(_keyName, seconds), "Turn off", "Keep", enable: false, seconds);
            }
            else
            {
                _bridge.Toast?.Invoke("Turn off live status?", BridgeNotice.LongPressAgain(_keyName, seconds));
            }
            PluginLog.Info($"Live status: {_keyName} long-pressed — nothing changed; Turn off, or a second long press within {seconds}s, disables");
        }

        // Caller holds _lock. Closes any open dialog, takes the flash down, and does the deed.
        private void SettleLocked(Boolean enable, String why)
        {
            _armedUntil = DateTime.MinValue;
            _offArmedUntil = DateTime.MinValue;
            var pending = _pending;
            _pending = null;
            pending?.Cancel();   // closes the dialog, if one is open
            _flash.Clear();
            PluginLog.Info($"Live status: {_keyName} {why} — {(enable ? "enabling" : "disabling")}");
            if (enable)
            {
                _bridge.EnableLiveStatus();
            }
            else
            {
                _bridge.DisableLiveStatus();
            }
        }

        // The dialog blocks until answered, so it runs on its own thread — never on the thread the
        // SDK delivers key presses on. A key press that settles the question first wins: the answer
        // arriving later finds it is no longer the pending one and does nothing.
        private void AskInBackground(String text, String yes, String no, Boolean enable, Int32 seconds)
        {
            var cts = new CancellationTokenSource();
            lock (_lock)
            {
                _pending?.Cancel();
                _pending = cts;
            }

            new Thread(() =>
            {
                Boolean? answer = null;
                try
                {
                    answer = _bridge.Prompt("Claude Console", text, yes, no, seconds, cts.Token);
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, "Live status: the prompt failed");
                }

                lock (_lock)
                {
                    if (!ReferenceEquals(_pending, cts))
                    {
                        return;   // a key press already settled it
                    }
                    _pending = null;

                    if (answer == true && this.NeedsSetup == enable)
                    {
                        this.SettleLocked(enable, "confirmed in the dialog");
                        return;
                    }

                    _armedUntil = DateTime.MinValue;
                    _offArmedUntil = DateTime.MinValue;
                    _flash.Clear();
                    PluginLog.Info(answer == false
                        ? $"Live status: {_keyName} — declined in the dialog; nothing changed"
                        : $"Live status: {_keyName} — no answer within {seconds}s; nothing changed");
                }
            })
            { IsBackground = true, Name = "claude-live-status-prompt" }.Start();
        }

        // The agent bridge's own word (Run /hooks, Setup failed, Blocked, …) outranks the key's
        // value. A session that simply has not reported since the helper recovered is NOT
        // blocked: the bridge withholds its stale value, and the key shows its dash.
        private String HealthLabel() => AgentBridgeNotice.FaceLabel(_bridge.AgentBridgeState);

        private String StateLabel() => this.HealthLabel()
            ?? (_applies ? LiveStatusFace.Label(_bridge.LiveStatus, _bridge.SettingsApplyLive) : null);

        private void Refresh()
        {
            var label = this.StateLabel();
            if (String.Equals(label, _label, StringComparison.Ordinal))
            {
                return;
            }
            _label = label;
            _repaint();
        }
    }
}
