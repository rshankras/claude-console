namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// The flagship pair — Approve ("Allow once") and Deny — for the approval card of a desktop
    /// agent app, pressed without the app ever gaining focus (the D2 property, proven on
    /// hardware-free spike 2026-08-24).
    ///
    /// Honesty rules, in order: when no card is pending both keys are visibly idle and a press
    /// is a logged no-op (never a blind press that might hit a stale tree); when a card is
    /// pending the Approve face carries the CARD'S OWN TEXT and the risk badge —
    /// amber for routine, red when RiskClassifier flags the visible text — so the decision is
    /// readable from the key before the thumb lands. "Always allow" is deliberately not a key.
    /// </summary>
    public class DesktopApprovalCommand : PluginDynamicCommand
    {
        private const String Approve = "approve";
        private const String Deny = "deny";

        public DesktopApprovalCommand()
            : base()
        {
            this.AddParameter(Approve, "Approve", "Agent")
                .SetDescription("Approve the pending request (Allow once) — the app stays in the background");
            this.AddParameter(Deny, "Deny", "Agent")
                .SetDescription("Deny the pending request — the app stays in the background");

            if (DesktopServices.Declared)
            {
                DesktopServices.Monitor.OnChanged += _ => this.ActionImageChanged();
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            if (!DesktopServices.Declared)
            {
                PluginLog.Warning("DesktopApprovalCommand: no desktop surface declared");
                return;
            }

            var state = DesktopServices.Monitor.Current;
            if (state.Activity != DesktopActivity.WaitingApproval)
            {
                PluginLog.Info($"DesktopApprovalCommand({actionParameter}): nothing pending — ignored");
                return;
            }

            var app = DesktopServices.App;
            var labels = actionParameter == Approve ? app.ApproveLabels : app.DenyLabels;
            if (!DesktopServices.Automation.Press(labels, out var matched))
            {
                PluginLog.Warning($"DesktopApprovalCommand({actionParameter}): press failed");
                return;
            }

            PluginLog.Info($"DesktopApprovalCommand: pressed “{matched}”");
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            actionParameter == Approve ? "Approve" : "Deny";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var state = DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable;
            var pending = state.Activity == DesktopActivity.WaitingApproval;
            var icon = actionParameter == Approve ? "yes" : "no";

            if (actionParameter == Approve && pending)
            {
                return KeyImage.RenderWithApprovalBadge(imageSize, "Approve", KeyImage.Green, icon, state.Risk);
            }

            if (actionParameter == Deny && pending)
            {
                return KeyImage.RenderWithApprovalBadge(imageSize, "Deny", KeyImage.Red, icon, state.Risk);
            }

            return KeyImage.Render(imageSize, actionParameter == Approve ? "Approve" : "Deny", KeyImage.Slate, icon);
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
