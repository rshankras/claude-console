namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// The flagship pair — Approve ("Allow once") and Deny — for the approval card of a desktop
    /// agent app, pressed without the app ever gaining focus (the D2 property, proven on
    /// hardware-free spike 2026-08-24).
    ///
    /// Honesty rules, in order: with nothing pending both keys are visibly idle and a press is
    /// a logged no-op; when a card is pending the Approve face names the conversation it will
    /// answer (when knowable) and the badge carries the judgement — amber routine, red when
    /// RiskClassifier flags the visible text. Red takes TWO presses. Every approval press
    /// carries the expected-card guard, so a card that changed between the glance and the thumb
    /// is refused, never approved unseen. "Always allow" is deliberately not a key.
    /// </summary>
    public class DesktopApprovalCommand : DesktopCommandBase
    {
        private const String Approve = "approve";
        private const String Deny = "deny";

        // Two-step red (review round): a High-risk card takes TWO presses — the first arms
        // (face flips to "Press again"), the second within the window fires. Amber stays
        // one-press; the risk grade is an extra warning, not the security boundary.
        private readonly DesktopApprovalConfirmation _confirmation = new DesktopApprovalConfirmation();

        public DesktopApprovalCommand()
            : base()
        {
            this.AddParameter(Approve, "Approve", "Agent")
                .SetDescription("Approve the pending request (Allow once) — the app stays in the background");
            this.AddParameter(Deny, "Deny", "Agent")
                .SetDescription("Deny the pending request — the app stays in the background");

            if (DesktopServices.Declared)
            {
                DesktopServices.OnMonitorChanged(state =>
                {
                    _confirmation.Observe(state);
                    this.ActionImageChanged();
                });
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            var shown = DesktopServices.Monitor?.Current;
            DesktopServices.Run(() => this.RunDesktopCommand(actionParameter, shown));
        }

        private void RunDesktopCommand(String actionParameter, DesktopState shown)
        {
            if (!DesktopServices.Declared)
            {
                PluginLog.Warning("DesktopApprovalCommand: no desktop surface declared");
                return;
            }

            Execute(actionParameter, shown, DesktopServices.App,
                DesktopServices.Automation, _confirmation, DateTime.UtcNow, () => this.ActionImageChanged());
        }

        internal static void Execute(String actionParameter, DesktopState state, IDesktopAppAdapter app,
            IDesktopAutomation automation, DesktopApprovalConfirmation confirmation, DateTime now, Action invalidate)
        {
            if (actionParameter is not (Approve or Deny)) { return; }
            confirmation.Observe(state);
            if (state.Activity != DesktopActivity.WaitingApproval || String.IsNullOrWhiteSpace(state.CardText))
            {
                return;
            }

            if (state.Risk == ApprovalRisk.High && !confirmation.Confirm(actionParameter, state, now))
            {
                invalidate();
                return;
            }

            confirmation.Reset();

            var labels = actionParameter == Approve ? app.ApproveLabels : app.DenyLabels;

            // The expected-card guard: press only the card the keypad RENDERED. If it changed
            // between the glance and the thumb, the helper refuses and the honest outcome is
            // "look at the screen", not a silent approval of something unseen.
            if (!automation.PressGuarded(labels, state.CardText, out _, out var error))
            {
                PluginLog.Warning($"DesktopApprovalCommand({actionParameter}): {error ?? "press failed"}");
                invalidate();
                return;
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            this.FaceLabel(actionParameter);

        // The face carries IDENTITY when it is knowable: the open conversation's short title
        // over the generic verb, so "Approve" always refers to one nameable request. When
        // several conversations wait and the open one is not knowable, the generic verb plus
        // the badge is the honest maximum. Armed red overrides everything: "Press again".
        private String FaceLabel(String actionParameter)
        {
            var state = DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable;
            return LabelFor(actionParameter, state, _confirmation, DateTime.UtcNow);
        }

        internal static String LabelFor(String actionParameter, DesktopState state,
            DesktopApprovalConfirmation confirmation, DateTime now)
        {
            var pending = state.Activity == DesktopActivity.WaitingApproval;
            if (pending && confirmation.IsArmed(actionParameter, state, now))
            {
                return "Press again";
            }

            if (actionParameter == Approve && pending && !String.IsNullOrEmpty(state.ActiveTitle))
            {
                var t = DesktopConversationLabels.Display(state.Mode, state.ActiveTitle);
                return t.Length <= 18 ? t : t.Substring(0, 17) + "…";
            }

            return actionParameter == Approve ? "Approve" : "Deny";
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var state = DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable;
            var pending = state.Activity == DesktopActivity.WaitingApproval;
            var icon = actionParameter == Approve ? "yes" : "no";

            if (pending)
            {
                return KeyImage.RenderWithApprovalBadge(
                    imageSize, this.FaceLabel(actionParameter),
                    actionParameter == Approve ? KeyImage.Green : KeyImage.Red, icon, state.Risk);
            }

            return KeyImage.Render(
                imageSize, actionParameter == Approve ? "Approve" : "Deny", KeyImage.Gray, icon + "_idle");
        }

        // WHY THE CARD TEXT IS NOT ON THE KEY. It was, until hardware said otherwise: the card's
        // own sentence rendered as three lines of unreadable micro-type, and any character the
        // LCD's font lacks comes out as "?". Every key that reads at arm's length is one or two
        // words — Waiting, Codex, Deny.
        //
        // So the division of labour is: the BADGE carries the judgement (amber routine, red when
        // RiskClassifier flags the visible text), and anyone who needs the exact wording presses
        // Show ChatGPT, which exists for precisely that. The card text still earns its keep — it
        // is what the classifier grades and what the log records — it just isn't key material.
    }
}
