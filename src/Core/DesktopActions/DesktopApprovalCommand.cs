namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Diagnostics;
    using System.Threading;

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
        private readonly FailureFace _feedback;
        private String _feedbackParameter, _checkingParameter;
        private DesktopState _shownApprove = DesktopState.Unavailable, _shownDeny = DesktopState.Unavailable;

        public DesktopApprovalCommand()
            : base()
        {
            // A widget draws its own label (the decision tile), so Options+ adds no caption.
            this.SetWidget(true);
            _feedback = new FailureFace(() => this.ActionImageChanged(), holdMs: 3000);
            DesktopServices.Lifetime.OnStop(() =>
            {
                _feedback.Dispose(); _confirmation.Reset();
                Volatile.Write(ref _checkingParameter, null);
                Volatile.Write(ref _shownApprove, DesktopState.Unavailable);
                Volatile.Write(ref _shownDeny, DesktopState.Unavailable);
            });
            this.AddParameter(Approve, "Approve", "Agent")
                .SetDescription("Approve the pending request (Allow once) — the app stays in the background");
            this.AddParameter(Deny, "Deny", "Agent")
                .SetDescription("Deny the pending request — the app stays in the background");

            if (DesktopServices.Declared)
            {
                DesktopServices.OnMonitorChanged(state =>
                {
                    _confirmation.Observe(state);
                    if (HasRequest(state) && _feedback.Text is not (null or "Press again")) _feedback.Clear();
                    this.ActionImageChanged();
                });
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            if (actionParameter is not (Approve or Deny) || !DesktopServices.Declared) return;
            var shown = actionParameter == Approve ? Volatile.Read(ref _shownApprove) : Volatile.Read(ref _shownDeny);
            var monitor = DesktopServices.Monitor;
            var app = DesktopServices.App;
            var automation = DesktopServices.Automation;
            var current = DesktopServices.Lifetime.CaptureGuard();
            RunDesktopAction(actionParameter, () =>
            {
                if (!current()) return;
                _feedbackParameter = actionParameter;
                _feedback.Clear();
                try
                {
                    var result = Execute(actionParameter, shown, app, automation, _confirmation,
                        DateTime.UtcNow, () => { if (current()) this.ActionImageChanged(); }, monitor,
                        () => { Volatile.Write(ref _checkingParameter, actionParameter); this.ActionImageChanged(); });
                    if (current() && result != null) _feedback.Show(result);
                }
                finally
                {
                    Volatile.Write(ref _checkingParameter, null);
                    if (current()) this.ActionImageChanged();
                }
            });
        }

        private static Boolean HasRequest(DesktopState state) => state?.Activity == DesktopActivity.WaitingApproval
            && !String.IsNullOrWhiteSpace(state.CardText);

        internal static String Execute(String actionParameter, DesktopState state, IDesktopAppAdapter app,
            IDesktopAutomation automation, DesktopApprovalConfirmation confirmation, DateTime now, Action invalidate,
            DesktopMonitor monitor = null, Action checking = null)
        {
            if (actionParameter is not (Approve or Deny)) { return null; }
            state ??= DesktopState.Unavailable;
            confirmation.Observe(state);
            if (!HasRequest(state))
            {
                PluginLog.Info($"DesktopApprovalCommand({actionParameter}): result=checking");
                checking?.Invoke();
                var elapsed = Stopwatch.StartNew();
                // Refresh once on the action lane, then SHOW the request. This press must not
                // also approve a card that the keypad had not presented when the user tapped.
                var fresh = monitor != null ? monitor.RefreshForCommand() : DesktopMonitor.Map(automation.Status());
                confirmation.Observe(fresh);
                if (!HasRequest(fresh))
                {
                    var readable = fresh.Available && fresh.Activity != DesktopActivity.WaitingApproval;
                    PluginLog.Info($"DesktopApprovalCommand({actionParameter}): result={(readable ? "no-request" : "unavailable")}");
                    invalidate();
                    return readable ? "No request" : "Check app";
                }
                // Start the confirmation window AFTER the possibly slow read. This discovery
                // tap arms high-risk cards too; it must not turn two deliberate taps into three.
                confirmation.Confirm(actionParameter, fresh, now.Add(elapsed.Elapsed));
                PluginLog.Info($"DesktopApprovalCommand({actionParameter}): result=request-refreshed");
                invalidate();
                return "Press again";
            }

            if (state.Risk == ApprovalRisk.High && !confirmation.Confirm(actionParameter, state, now))
            {
                PluginLog.Info($"DesktopApprovalCommand({actionParameter}): result=armed-high-risk");
                invalidate();
                return "Press again";
            }

            confirmation.Reset();

            var labels = actionParameter == Approve ? app.ApproveLabels : app.DenyLabels;

            // The expected-card guard: press only the card the keypad RENDERED. If it changed
            // between the glance and the thumb, the helper refuses and the honest outcome is
            // "look at the screen", not a silent approval of something unseen.
            if (!automation.PressGuarded(labels, state.CardText, out _, out _))
            {
                PluginLog.Warning($"DesktopApprovalCommand({actionParameter}): result=press-unconfirmed");
                monitor?.RequestRefresh();
                invalidate();
                return "Check app";
            }
            PluginLog.Info($"DesktopApprovalCommand({actionParameter}): result={(actionParameter == Approve ? "approved" : "denied")}");
            monitor?.RequestRefresh();
            return actionParameter == Approve ? "Approved" : "Denied";
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "\u200B";

        // The verb stays the label; "Press again" overrides it once armed.
        internal static String LabelFor(String actionParameter, DesktopState state,
            DesktopApprovalConfirmation confirmation, DateTime now)
        {
            var pending = state.Activity == DesktopActivity.WaitingApproval;
            if (pending && confirmation.IsArmed(actionParameter, state, now))
            {
                return "Press again";
            }

            return actionParameter == Approve ? "Approve" : "Deny";
        }

        // IDENTITY when it is knowable: the open conversation's title, as the tile's small
        // caption, so "Approve" always refers to one nameable request. It used to replace the
        // verb, which wrapped onto two cramped lines on the decision tile. When the open
        // conversation is not knowable, the verb plus the badge is the honest maximum.
        internal static String TargetFor(String actionParameter, DesktopState state) =>
            actionParameter == Approve && state.Activity == DesktopActivity.WaitingApproval && !String.IsNullOrEmpty(state.ActiveTitle)
                ? DesktopConversationLabels.Display(state.Mode, state.ActiveTitle) : null;

        // The family decision tile, as on Claude Console and Vizhi for Codex: solid green / red
        // with the white check / cross, coloured whether or not a request waits (their Yes/No
        // are too). The corner badge is the "something is waiting" cue; grey means the app
        // cannot be read at all.
        protected override BitmapImage GetDesktopCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var state = DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable;
            // A poll can publish between the last paint and a tap. Bind the tap to the state
            // actually used to draw this key, not the latest unseen monitor value.
            var face = FaceFor(actionParameter, state, _confirmation, DateTime.UtcNow);
            var feedback = _feedbackParameter == actionParameter ? _feedback.Text : null;
            var label = Volatile.Read(ref _checkingParameter) == actionParameter ? "Checking"
                : feedback != null && feedback != "Press again" ? feedback : face.Label;
            var shown = label is "Approve" or "Deny" or "Press again" ? state : DesktopState.Unavailable;
            if (actionParameter == Approve) Volatile.Write(ref _shownApprove, shown);
            else if (actionParameter == Deny) Volatile.Write(ref _shownDeny, shown);
            return KeyImage.RenderDecisionTile(imageSize, label, face.Color,
                approve: actionParameter == Approve, risk: face.Risk, targetLabel: TargetFor(actionParameter, state));
        }

        internal static (String Label, BitmapColor Color, ApprovalRisk Risk) FaceFor(String actionParameter,
            DesktopState state, DesktopApprovalConfirmation confirmation, DateTime now)
        {
            var label = LabelFor(actionParameter, state, confirmation, now);
            if (!state.Available) return (label, KeyImage.Gray, ApprovalRisk.None);
            var pending = state.Activity == DesktopActivity.WaitingApproval;
            return (label, actionParameter == Approve ? KeyImage.Green : KeyImage.Red, pending ? state.Risk : ApprovalRisk.None);
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
