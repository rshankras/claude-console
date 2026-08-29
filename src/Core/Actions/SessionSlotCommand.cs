namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Models;

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
    /// Face layout (2026-08 design): the whole face is a custom bitmap — the session NAME wrapped
    /// on top, and a colour-filled STATE-WORD bar below it (Thinking / Allow? / Waiting / Ready),
    /// amber when your approval is wanted. The bar lives in the bitmap because the service label
    /// strip cannot be coloured; that strip is left to show the registered slot name.
    /// </summary>
    public class SessionSlotCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;

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
            this.ActionImageChanged();   // repaint every slot when the grid changes
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

        // The whole face (name + state-word bar) is drawn in the bitmap, so the service's own label
        // strip should be BLANK. Returning String.Empty makes the service fall back to the registered
        // slot name ("Session 1"); a zero-width space is non-empty, so it suppresses that fallback
        // and renders as nothing.
        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            "\u200B";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (!TryGetSlot(actionParameter, out var slot) || _bridge.Grid.SlotSession(slot) is not { } session)
            {
                // Empty slot: a plain dark face.
                return KeyImage.RenderSessionSlot(imageSize, null, null, KeyImage.Slate, darkText: false, selected: false);
            }

            var selected = session.SessionKey == _bridge.TargetTty();
            var name = String.IsNullOrWhiteSpace(session.Project) ? _bridge.Agent.DisplayName : session.Project;
            var (word, color, dark) = StateFace(session);
            return KeyImage.RenderSessionSlot(imageSize, name, word, color, dark, selected);
        }

        // Map a session's live state to the design's state word + bar colour. Amber (your approval
        // is wanted) is the only loud one — the same reservation as #51: colour means "answer me".
        private static (String Word, BitmapColor Color, Boolean DarkText) StateFace(GridSession session)
        {
            if (session.Risk != ApprovalRisk.None)
            {
                return ("Allow?", KeyImage.Amber, true);   // dark text reads best on amber
            }

            switch (session.State)
            {
                case "busy": return ("Thinking", KeyImage.Gray, false);
                case "waiting": return ("Waiting", KeyImage.Gray, false);
                default: return ("Ready", KeyImage.Gray, false);
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
