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
    /// deliberately NOT undone by switching Terminal tabs (see BridgeManager.RoutingTty); pressing the
    /// pinned slot AGAIN releases it. Slots are
    /// stable: when a session exits, the others keep their keys.
    ///
    /// Six slots fill one 9-key page alongside Yes / No / Voice. Empty slots draw a blank face and
    /// do nothing on press.
    ///
    /// Face layout (2026-08-30 design): the project (directory) name is centred in the black title
    /// region, with the live state in a bar along the bottom. The active/routed session uses its
    /// product identity colour (Claude orange or Codex blue); inactive sessions use grey. The word
    /// in the bar says what that session is doing (Thinking / Allow? / Waiting / Complete),
    /// independently of the bar colour.
    /// </summary>
    public class SessionSlotCommand : PluginDynamicCommand
    {
        private readonly BridgeManager _bridge;

        public SessionSlotCommand()
            : base()
        {
            _bridge = BridgeManager.Instance;

            // A normal command image is placed in Options+' inset, user-resizable icon layer; the
            // remaining key area belongs to its static label compositor. Session faces are live,
            // full-surface information rather than icons, so request widget rendering instead.
            // This keeps the image dynamic while allowing its state bar to reach the button edge.
            this.SetWidget(true);

            for (var slot = 1; slot <= _bridge.SessionSlotCount; slot++)
            {
                this.AddParameter(slot.ToString(), $"Session {slot}", "Sessions")
                    .SetDescription($"{_bridge.Agent.DisplayName} session {slot}: shows its project and what it is doing; press to focus that terminal tab and keep every other key aimed at it until you pick another session (press again to release)");
            }

            _bridge.Grid.OnGridChanged += this.OnGridChanged;
            _bridge.OnLiveStatusChanged += _ => this.OnGridChanged();   // the setup word comes and goes with the wiring (#58)
            _bridge.OnAgentBridgeStatusChanged += _ => this.OnGridChanged();
        }

        private void OnGridChanged()
        {
            this.ActionImageChanged();   // repaint every slot when the grid changes
        }

        private Boolean TryGetSlot(String actionParameter, out Int32 slot) =>
            Int32.TryParse(actionParameter, out slot) && slot >= 1 && slot <= _bridge.SessionSlotCount;

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

        // The name is drawn centred in the bitmap, so the service's own label
        // strip should be BLANK. Returning String.Empty makes the service fall back to the registered
        // slot name ("Session 1"); a zero-width space is non-empty, so it suppresses that fallback
        // and renders as nothing.
        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            "\u200B";

        // A black title region with a state bar below it; an empty slot is bare black.
        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (!TryGetSlot(actionParameter, out var slot) || _bridge.Grid.SlotSession(slot) is not { } session)
            {
                return KeyImage.RenderSessionSlot(imageSize, null, null, KeyImage.Gray, darkText: false);
            }

            var active = session.SessionKey == _bridge.RoutingTty();
            var name = String.IsNullOrWhiteSpace(session.Project) ? _bridge.Agent.DisplayName : session.Project;
            // Routing stays the cue: inactive sessions remain grey. Only the selected bar follows
            // product identity, which the product declares — the engine does not know whose it is.
            var barColor = active ? KeyImage.SessionBar : KeyImage.Gray;
            var setupWord = AgentBridgeNotice.FaceLabel(_bridge.AgentBridgeState)
                ?? LiveStatusFace.SessionBarWord(_bridge.LiveStatusApplies, _bridge.LiveStatus);
            return KeyImage.RenderSessionSlot(imageSize, name, StateWord(session, setupWord, _bridge.Agent.Id), barColor, darkText: false);
        }

        // Colour communicates routing; this word communicates session state. Keeping those two
        // signals independent means an inactive session can still say "Allow?" without looking active.
        // setupWord is the bar's live-status word ("Set up" / "Status off") while the wiring is absent, else null.
        internal static String StateWord(GridSession session, String setupWord, String agentId = null)
        {
            // With the wiring off, nothing can write a session's state or its pending payload, so
            // whatever the registry holds is frozen at best — the owner read "Complete" under a
            // live permission prompt, and a relaunched tab wore its predecessor's "Waiting" (#58).
            // The bar says what would change that, for every session, until the wiring is on.
            if (setupWord != null)
            {
                return setupWord;
            }

            if (session.Risk != ApprovalRisk.None)
            {
                return "Allow?";
            }

            if (agentId == "codex-cli" && session.IsProvisional) { return "Ready"; }

            switch (session.State)
            {
                case "busy": return "Thinking";
                case "waiting": return "Waiting";
                default: return "Complete";
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
