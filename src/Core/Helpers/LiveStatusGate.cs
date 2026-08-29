namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// What a live key (Cost / Context / Activity) shows and does while live status is not set up
    /// (#31). One owner for the three keys, so they cannot drift: the same words for the same
    /// state, the same two-step press, and a repaint only when the words change (#27).
    ///
    /// The press IS the prompt, in two steps. The first press on a not-enabled key changes
    /// NOTHING: it arms this key for a short window, flashes "Press again" (the #18 face pattern —
    /// the face has no room for more), and posts a card in Options+ that names the key and the
    /// exact change a second press will make to ~/.claude/settings.json. A second press on the
    /// SAME key inside the window enables — the same merge the Enable Live Status key runs. A late
    /// press just arms again. Nobody has to drag a key they will press once; the labelled
    /// Enable / Disable keys stay for the layout download and for anyone who prefers them.
    /// </summary>
    internal sealed class LiveStatusGate
    {
        // Long enough to read the flash and the card, short enough that a press on Monday and a
        // press on Tuesday are two first presses.
        private const Int32 DefaultArmWindowMs = 10_000;

        private readonly BridgeManager _bridge;
        private readonly String _keyName;
        private readonly FailureFace _flash;
        private readonly Action _repaint;
        private readonly Int32 _armWindowMs;
        private DateTime _armedUntil;     // a second press before this enables; per key, on purpose
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

            if (this.Armed)
            {
                _armedUntil = DateTime.MinValue;
                _flash.Clear();
                PluginLog.Info($"Live status: {_keyName} pressed again within the window — enabling");
                _bridge.EnableLiveStatus();
                return true;
            }

            _armedUntil = DateTime.UtcNow.AddMilliseconds(_armWindowMs);
            _flash.Show(LiveStatusFace.PressHint);
            _bridge.Notify?.Invoke(PluginStatus.Warning, BridgeNotice.PressAgain(_keyName, _armWindowMs / 1000), BridgeNotice.SupportUrl, BridgeNotice.SupportTitle);
            PluginLog.Info($"Live status: {_keyName} pressed before setup — nothing changed; a second press within {_armWindowMs / 1000}s enables");
            return true;
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
