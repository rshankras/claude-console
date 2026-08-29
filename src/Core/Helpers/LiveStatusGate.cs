namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Threading;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// What a live key (Cost / Context / Activity) shows and does while live status is not set up
    /// (#31). One owner for the three keys, so they cannot drift: the same words for the same
    /// state, the same prompt, and a repaint only when the words change (#27).
    ///
    /// The press IS the prompt. The first press on a not-enabled key changes NOTHING: it arms this
    /// key for a short window, flashes "Press again" (the #18 face pattern — the face has no room
    /// for more), posts a card in Options+ as the record, and — where the product can — opens a
    /// dialog on screen that names the exact change and offers "Not now" / "Turn on". "Turn on", or
    /// a second press on the SAME key inside the window, enables: the same merge the Enable Live
    /// Status key runs. "Not now", no answer, or a late press: nothing. Nobody has to drag a key
    /// they will press once; the labelled Enable / Disable keys stay for the layout download and
    /// for anyone who prefers them.
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
        private readonly Object _lock = new Object();
        private DateTime _armedUntil;             // a second press before this enables; per key, on purpose
        private CancellationTokenSource _pending; // the open dialog, if any — cancelled by a key press
        private String _label;

        public LiveStatusGate(BridgeManager bridge, String keyName, Action repaint, Int32 armWindowMs = DefaultArmWindowMs)
        {
            _bridge = bridge;
            _keyName = keyName;
            _repaint = repaint;
            _armWindowMs = armWindowMs;
            _flash = new FailureFace(repaint, armWindowMs);
            _label = LiveStatusFace.Label(bridge.LiveStatus);
            bridge.OnLiveStatusChanged += _ => this.Refresh();
        }

        /// <summary>The words that replace the key's live value right now, or null when the key shows its own.</summary>
        public String Label => _flash.IsActive ? _flash.Text : LiveStatusFace.Label(_bridge.LiveStatus);

        /// <summary>True while a press belongs to setup rather than to the key's own job.</summary>
        public Boolean NeedsSetup => LiveStatusFace.NeedsSetup(_bridge.LiveStatus);

        /// <summary>True while a second press would enable.</summary>
        public Boolean Armed => DateTime.UtcNow < _armedUntil;

        /// <summary>
        /// Handle a press. Returns true when the press belonged to setup (armed, or enabled) and the
        /// key must not act on it; false when live status is on and the key should do its own job.
        /// </summary>
        public Boolean Press()
        {
            if (!this.NeedsSetup)
            {
                return false;
            }

            lock (_lock)
            {
                if (this.Armed)
                {
                    this.EnableLocked("pressed again within the window");
                    return true;
                }
                _armedUntil = DateTime.UtcNow.AddMilliseconds(_armWindowMs);
            }

            _flash.Show(LiveStatusFace.PressHint);
            var seconds = _armWindowMs / 1000;
            _bridge.Notify?.Invoke(PluginStatus.Warning, BridgeNotice.PressAgain(_keyName, seconds), BridgeNotice.SupportUrl, BridgeNotice.SupportTitle);
            if (_bridge.Prompt != null)
            {
                this.AskInBackground(BridgeNotice.TurnOnDialog(_keyName, seconds), seconds);
            }
            else
            {
                _bridge.Toast?.Invoke("Turn on live status?", BridgeNotice.PressAgain(_keyName, seconds));
            }
            PluginLog.Info($"Live status: {_keyName} pressed before setup — nothing changed; Turn on, or a second press within {seconds}s, enables");
            return true;
        }

        // Caller holds _lock.
        private void EnableLocked(String why)
        {
            _armedUntil = DateTime.MinValue;
            var pending = _pending;
            _pending = null;
            pending?.Cancel();   // closes the dialog, if one is open
            _flash.Clear();
            PluginLog.Info($"Live status: {_keyName} {why} — enabling");
            _bridge.EnableLiveStatus();
        }

        // The dialog blocks until answered, so it runs on its own thread — never on the thread the
        // SDK delivers key presses on. A key press that settles the question first wins: the answer
        // arriving later finds it is no longer the pending one and does nothing.
        private void AskInBackground(String text, Int32 seconds)
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
                    answer = _bridge.Prompt("Claude Console", text, seconds, cts.Token);
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

                    if (answer == true && this.NeedsSetup)
                    {
                        this.EnableLocked("confirmed in the dialog");
                        return;
                    }

                    _armedUntil = DateTime.MinValue;
                    _flash.Clear();
                    PluginLog.Info(answer == false
                        ? $"Live status: {_keyName} — declined in the dialog; nothing changed"
                        : $"Live status: {_keyName} — no answer within {seconds}s; nothing changed");
                }
            })
            { IsBackground = true, Name = "claude-live-status-prompt" }.Start();
        }

        private void Refresh()
        {
            var label = LiveStatusFace.Label(_bridge.LiveStatus);
            if (String.Equals(label, _label, StringComparison.Ordinal))
            {
                return;
            }
            _label = label;
            _repaint();
        }
    }
}
