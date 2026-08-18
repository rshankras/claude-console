namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Linq;
    using System.Threading;

    /// <summary>
    /// Session keys (group "Sessions") — one LCD key per running Claude Code session.
    ///
    /// Each key shows the session's project name, its context usage, and a face for what it's doing
    /// (working / waiting on you / ready). Pressing one focuses that Terminal tab AND pins every
    /// other key to it, so you can answer a prompt in session 2 while looking at session 1 — or at
    /// a browser. The pin holds until you press another session key or that session exits; it is
    /// deliberately NOT undone by switching Terminal tabs (see BridgeManager.TargetTty). Slots are
    /// stable: when a session exits, the others keep their keys.
    ///
    /// Six slots fill one 9-key page alongside Yes / No / Voice. Empty slots draw a blank face and
    /// do nothing on press.
    ///
    /// Face layout (tuned on hardware): the PROJECT NAME is the single-line SDK label — the
    /// service's one-line label font is the largest, crispest text a key can carry, and it matches
    /// every other key's design language. The bitmap (which covers only the upper square of the
    /// key) holds the state icon with the small slate context % under it. Two-line labels shrink
    /// and clip (the 1.6.1 bug); large in-bitmap text clips at the edges — both dead ends, tried.
    /// </summary>
    public class SessionSlotCommand : PluginDynamicCommand
    {
        // Frames cycled while a session is working — the same pair the Activity key animates.
        private static readonly String[] BusyFrames = { "busy0", "busy1" };

        private readonly BridgeManager _bridge;
        private Timer _animTimer;
        private Int32 _frame;
        private Boolean _animating;

        public SessionSlotCommand()
            : base()
        {
            _bridge = BridgeManager.Instance;

            for (var slot = 1; slot <= SessionRegistry.SlotCount; slot++)
            {
                this.AddParameter(slot.ToString(), $"Session {slot}", "Sessions")
                    .SetDescription($"Claude session {slot}: shows its project, state and context usage; press to focus that Terminal tab and keep every other key aimed at it until you pick another session");
            }

            _bridge.Grid.OnGridChanged += this.OnGridChanged;
        }

        private void OnGridChanged()
        {
            this.SyncAnimation();
            this.ActionImageChanged();   // repaint every slot
        }

        // Animate only while at least one session is working, so an idle keypad isn't redrawing
        // twice a second for nothing.
        private void SyncAnimation()
        {
            var anyBusy = _bridge.Grid.LiveSessions().Any(s => s.State == "busy");
            if (anyBusy == _animating)
            {
                return;
            }

            _animating = anyBusy;
            if (anyBusy)
            {
                _frame = 0;
                _animTimer = new Timer(
                    _ =>
                    {
                        _frame = (_frame + 1) % BusyFrames.Length;
                        this.ActionImageChanged();
                    },
                    null, 400, 400);
            }
            else
            {
                _animTimer?.Dispose();
                _animTimer = null;
            }
        }

        private static Boolean TryGetSlot(String actionParameter, out Int32 slot) =>
            Int32.TryParse(actionParameter, out slot) && slot >= 1 && slot <= SessionRegistry.SlotCount;

        protected override void RunCommand(String actionParameter)
        {
            if (!TryGetSlot(actionParameter, out var slot))
            {
                return;
            }

            var session = _bridge.Grid.SlotSession(slot);
            if (session == null)
            {
                PluginLog.Info($"SessionSlotCommand: slot {slot} is empty — ignoring");
                return;   // empty key is inert; nothing to focus
            }

            _bridge.SelectSlot(slot);
            this.ActionImageChanged();
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            if (!TryGetSlot(actionParameter, out var slot))
            {
                return String.Empty;
            }

            var session = _bridge.Grid.SlotSession(slot);
            if (session == null)
            {
                // Empty slot: String.Empty deliberately — the service then falls back to the
                // registered name ("Session 3"), which is a useful hint on an unused key.
                return String.Empty;
            }

            // The project name, SINGLE line — the service renders a one-line label in the same
            // large crisp font as every other key's ("Clear", "Draft"); a second line makes it
            // shrink both (and is what clipped in 1.6.1). The context % lives in the bitmap.
            return Truncate(session.Project, 12);
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (!TryGetSlot(actionParameter, out var slot))
            {
                return KeyImage.RenderSessionSlot(imageSize, null, null, selected: false);
            }

            var session = _bridge.Grid.SlotSession(slot);
            if (session == null)
            {
                return KeyImage.RenderSessionSlot(imageSize, null, null, selected: false);
            }

            var selected = session.SessionKey == _bridge.TargetTty();
            // Null CtxPercent (no context used yet) draws nothing — a blank beats a misleading "0%".
            return KeyImage.RenderSessionSlot(
                imageSize, IconFor(session.State), session.CtxPercent, selected, session.Risk);
        }

        private String IconFor(String state)
        {
            switch (state)
            {
                case "busy": return BusyFrames[_frame % BusyFrames.Length];
                case "waiting": return "waiting";
                default: return "done";
            }
        }

        // The key is ~12 characters wide; the tooltip carries the full detail.
        internal static String Truncate(String value, Int32 max)
        {
            if (String.IsNullOrEmpty(value))
            {
                return String.Empty;
            }
            return value.Length <= max ? value : value.Substring(0, max - 1) + "…";
        }
    }
}
