namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// What a live key (Cost / Context / Activity) shows and does while live status is not set up
    /// (#31). One owner for the three keys, so they cannot drift: the same words for the same
    /// state, the same refusal on press, and a repaint only when the words change (#27).
    ///
    /// A press before setup changes NOTHING. It flashes "Set up first" for a moment (the #18
    /// failure-face pattern) and posts a card in Options+ saying where the switch is — the key face
    /// has no room to explain, and a silent key reads as a broken one (#29). Enabling is the job of
    /// Enable Live Status alone, whose description says what will change before the user presses.
    /// </summary>
    internal sealed class LiveStatusGate
    {
        private readonly BridgeManager _bridge;
        private readonly FailureFace _flash;
        private readonly Action _repaint;
        private String _label;

        public LiveStatusGate(BridgeManager bridge, Action repaint, Int32 holdMs = 2500)
        {
            _bridge = bridge;
            _repaint = repaint;
            _flash = new FailureFace(repaint, holdMs);
            _label = LiveStatusFace.Label(bridge.LiveStatus);
            bridge.OnLiveStatusChanged += _ => this.Refresh();
        }

        /// <summary>The words that replace the key's live value right now, or null when the key shows its own.</summary>
        public String Label => _flash.IsActive ? _flash.Text : LiveStatusFace.Label(_bridge.LiveStatus);

        /// <summary>True while a press should be refused rather than acted on.</summary>
        public Boolean NeedsSetup => LiveStatusFace.NeedsSetup(_bridge.LiveStatus);

        /// <summary>
        /// Handle a press while setup is owed: flash, point at the switch, change nothing. Returns
        /// true when the press was consumed here and the key must not act on it.
        /// </summary>
        public Boolean Refuse()
        {
            if (!this.NeedsSetup)
            {
                return false;
            }

            _flash.Show(LiveStatusFace.PressHint);
            _bridge.Notify?.Invoke(PluginStatus.Normal, BridgeNotice.SetupRequired(), BridgeNotice.SupportUrl, BridgeNotice.SupportTitle);
            PluginLog.Info("Live status: a live key was pressed before setup — nothing changed; pointed at " + LiveStatusFace.SetupGroup);
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
