namespace Loupedeck.ClaudeConsolePlugin.Actions
{
    using System;
    using System.Threading;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// Answer keys (group "Answer") — for responding when Claude Code prompts a question.
    /// One auto-discovered command, one SDK action per response via AddParameter.
    ///   Yes    → Return on a captured permission prompt (confirms the highlighted option)
    ///   No     → Escape on a captured permission prompt (dismisses it; the tool never runs)
    ///   Up     → Up-arrow keystroke    (move the selection up in a menu)
    ///   Down   → Down-arrow keystroke  (move the selection down in a menu)
    ///   Enter  → Return keystroke      (confirm the highlighted menu option / submit)
    ///
    /// Up/Down/Enter drive Claude Code's numbered selection menus (permission prompts,
    /// AskUserQuestion, plan-mode confirmation): arrow to an option, then Enter. Yes/No answer a
    /// permission prompt the plugin can SEE, and beep instead of guessing when an approval signal
    /// should exist but does not (see AnswerApproval / Decide below — #21). Windows Codex cannot
    /// observe approval prompts because its hook runner creates no process there, so that one
    /// declared capability gap uses the safest possible manual fallback: Yes sends Return and No
    /// sends Escape, never a typed word. All are key codes sent to the focused terminal.
    /// </summary>
    public class AnswerCommand : PluginDynamicCommand
    {
        private const String Yes = "yes";
        private const String No = "no";
        private const String Up = "up";
        private const String Down = "down";
        private const String Enter = "enter";

        public AnswerCommand()
            : base()
        {
            var agentName = BridgeManager.Instance.Agent.DisplayName;
            // Yes/No are full coloured live faces in the supplied design. Widget rendering
            // bypasses Options+' inset icon layer without freezing the action into a static .ict.
            // This flag applies to the whole dynamic command, so Up/Down/Enter also render their
            // own complete faces below.
            this.SetWidget(true);

            var canObserveApprovals = BridgeManager.Instance.Agent?.Capabilities.ApprovalSignal ?? true;

            // Repaint Yes/No when the targeted session starts or stops waiting, so the badge is live.
            BridgeManager.Instance.Grid.OnGridChanged += () =>
            {
                this.ActionImageChanged(Yes);
                this.ActionImageChanged(No);
            };

            // And when live status is turned on or off: without that wiring the faces read
            // "Set up" / "Off" instead of looking ready (#58).
            BridgeManager.Instance.OnLiveStatusChanged += _ =>
            {
                this.ActionImageChanged(Yes);
                this.ActionImageChanged(No);
            };
            BridgeManager.Instance.OnAgentBridgeStatusChanged += _ =>
            {
                this.ActionImageChanged(Yes);
                this.ActionImageChanged(No);
            };

            this.AddParameter(Yes, "Yes", "Answer")
                .SetDescription(canObserveApprovals
                    ? $"Approve the permission prompt {agentName} is waiting on (confirms the highlighted option); beeps if there is nothing to approve"
                    : $"Confirm the visible {agentName} prompt with Return; approval lighting is unavailable on this platform");
            this.AddParameter(No, "No", "Answer")
                .SetDescription(canObserveApprovals
                    ? $"Reject the permission prompt {agentName} is waiting on (dismisses it, the tool does not run); beeps if there is nothing to reject"
                    : $"Dismiss the visible {agentName} prompt with Escape; approval lighting is unavailable on this platform");
            this.AddParameter(Up, "Arrow Up", "Answer")
                .SetDescription($"Move the selection up in a {agentName} menu (Up arrow)");
            this.AddParameter(Down, "Arrow Down", "Answer")
                .SetDescription($"Move the selection down in a {agentName} menu (Down arrow)");
            this.AddParameter(Enter, "Return", "Answer")
                .SetDescription("Confirm the highlighted menu option / submit (Return)");
        }

        protected override void RunCommand(String actionParameter)
        {
            var bridge = BridgeManager.Instance;
            switch (actionParameter)
            {
                case Yes:
                    AnswerApproval(bridge, approve: true);
                    break;
                case No:
                    AnswerApproval(bridge, approve: false);
                    break;
                case Up:
                    bridge.InjectKey(KeyStroke.ArrowUp);
                    break;
                case Down:
                    bridge.InjectKey(KeyStroke.ArrowDown);
                    break;
                case Enter:
                    bridge.InjectKey(KeyStroke.Return);
                    break;
            }

            PluginLog.Info($"AnswerCommand: {actionParameter}");
        }

        /// <summary>
        /// Answer Yes/No, branching on whether the targeted session has a CAPTURED APPROVAL
        /// pending (a permission menu we can see) or not.
        ///
        /// THE 2.0.1 DEFECT (#21). Both keys typed a literal word and pressed Return. A permission
        /// prompt is a NUMBERED MENU, so the word did nothing and the Return confirmed whichever
        /// option was highlighted — option 1, Yes. Pressing **No** therefore approved the action,
        /// and the leftover word was then submitted as a stray chat message. Reproduced on hardware
        /// 2026-08-27: a file the session had been asked to delete was deleted by a press of No,
        /// after which "no" arrived as a user turn and the agent explained it was too late.
        ///
        /// WHY NOT "SEND THE OPTION NUMBER". That is the report's suggested fix and it cannot be
        /// done from what the plugin knows: the captured PermissionRequest payload carries
        /// tool_name and tool_input and nothing else (scripts/activity-hook.sh). The option list
        /// and its length live in the TUI, which we cannot see, so any digit would be a guess —
        /// and a guess that lands on the wrong row approves something.
        ///
        /// WHAT IS RELIABLE, without seeing the menu:
        ///   • option 1 is Yes and starts highlighted, so RETURN approves. Count-independent.
        ///   • ESCAPE dismisses the prompt without running the tool. Also count-independent, and
        ///     it FAILS SAFE: if Escape ever did something broader than reject — interrupting the
        ///     turn, say — the command still does not run, which is the direction a No key should
        ///     err in. A wrong digit errs the other way.
        /// Both are key codes, so neither inherits the keyboard-layout defect (#22).
        ///
        /// WHEN THERE IS NO CAPTURED APPROVAL, WE DO NOTHING. A session can be "waiting" without a
        /// pending payload — Claude idling at a plain prompt (the Notification hook marks that
        /// "waiting" too), or an agent too old for the PermissionRequest hook sitting on a real
        /// menu. From here the two are indistinguishable, and BOTH bad guesses are unsafe: typing a
        /// word at a hidden menu is the very P0 above, and a bare Return/Escape at an idle prompt
        /// does the wrong thing quietly (the 2.0.1-retest regression — Yes sent Return, No sent
        /// Escape, at a session with nothing to approve). So the key beeps and reports instead.
        /// Refusing to answer is recoverable; approving what the user tried to refuse is not.
        ///
        /// This is why the decision keys on the captured PAYLOAD, not on State=="waiting": #51 made
        /// the amber badge require that payload, and the keys must agree with the badge they draw —
        /// an amber Yes must not describe a menu that Yes will not actually answer. (Cost, accepted
        /// and shared with #51: to answer a plain free-text question you type into the session, not
        /// with these keys.)
        /// </summary>
        /// <summary>How a Yes/No press should be delivered.</summary>
        internal enum AnswerVia
        {
            /// <summary>An approval is captured: confirm the highlighted option (always Yes) with Return.</summary>
            MenuConfirm,

            /// <summary>An approval is captured: dismiss it with Escape, so the tool does not run.</summary>
            MenuReject,

            /// <summary>
            /// This agent/transport cannot report approvals: honour the user's visible-prompt Yes
            /// press with Return, without pretending an approval was observed.
            /// </summary>
            UnobservedConfirm,

            /// <summary>
            /// This agent/transport cannot report approvals: Escape is the fail-safe No because it
            /// cannot confirm the highlighted affirmative option.
            /// </summary>
            UnobservedReject,

            /// <summary>Nothing we can confirm is a menu: beep and do nothing, rather than guess.</summary>
            NoOp,
        }

        /// <summary>
        /// The decision, kept pure so tests exercise the real logic on values rather than on a
        /// singleton bridge. <paramref name="hasPendingApproval"/> is whether the targeted session
        /// carries a captured PermissionRequest payload — the SAME signal the amber badge is drawn
        /// from (SessionRegistry.ApplyPendingApproval), so the key and its badge cannot disagree.
        /// </summary>
        internal static AnswerVia Decide(Boolean approve, Boolean hasPendingApproval, Boolean canObserveApprovals = true)
        {
            // The captured payload is the only thing that tells a permission MENU from anything
            // else. It is set only while a tool is genuinely blocked on approval, and — now the
            // hook no longer deletes it on the delayed Notification — it stays set for the whole
            // time the menu is up. No payload means we cannot confirm a menu, so we refuse to guess:
            // typing a word is #21, and a bare Return/Escape at an idle prompt is the retest
            // regression. Both are unsafe; doing nothing is not.
            if (hasPendingApproval)
            {
                return approve ? AnswerVia.MenuConfirm : AnswerVia.MenuReject;
            }

            // A transport that cannot observe PermissionRequest cannot distinguish an approval
            // from an idle prompt. A manual key press while the user can see the prompt is still
            // meaningful: Return confirms the highlighted option and Escape safely rejects it.
            // Current Codex hooks report approvals on both platforms, so this remains only as the
            // capability-driven fallback for agents or older transports without that signal.
            if (!canObserveApprovals)
            {
                return approve ? AnswerVia.UnobservedConfirm : AnswerVia.UnobservedReject;
            }

            return AnswerVia.NoOp;
        }

        // The Options+ card that explains an inert Yes/No is posted once per load, not per press.
        private static Int32 _setupNoticePosted;

        private static void AnswerApproval(BridgeManager bridge, Boolean approve)
        {
            var agentSetup = AgentBridgeNotice.FaceLabel(bridge.AgentBridgeState);
            if (agentSetup != null)
            {
                bridge.Alert();
                if (Interlocked.Exchange(ref _setupNoticePosted, 1) == 0)
                {
                    bridge.Notify?.Invoke(
                        PluginStatus.Warning,
                        AgentBridgeNotice.Message(bridge.AgentBridgeState),
                        AgentBridgeNotice.PublicHelpUrl,
                        AgentBridgeNotice.Title(bridge.AgentBridgeState));
                }
                PluginLog.Info($"AnswerCommand: {(approve ? "Yes" : "No")} pressed while the agent bridge reads '{agentSetup}' — no approval was sent");
                return;
            }

            // Yes/No see a prompt only through the PermissionRequest hook, which is part of the
            // opt-in wiring. With it absent this press cannot do anything — and a bare beep left
            // the owner pressing Yes four times at a real prompt (#58). Say why, once, where the
            // user is looking; the face already says "Set up" / "Off".
            var setup = LiveStatusFace.SetupWord(bridge.LiveStatusApplies, bridge.LiveStatus);
            if (setup != null)
            {
                bridge.Alert();
                if (Interlocked.Exchange(ref _setupNoticePosted, 1) == 0)
                {
                    // Same README section as the live-status cards, but this card changed nothing,
                    // so its button must not claim it did.
                    bridge.Notify?.Invoke(PluginStatus.Warning, BridgeNotice.AnswerNeedsSetup(), BridgeNotice.SupportUrl, BridgeNotice.AnswerNeedsSetupTitle);
                }
                PluginLog.Info($"AnswerCommand: {(approve ? "Yes" : "No")} pressed while live status reads '{setup}' — the PermissionRequest hook is not installed; press a live key to turn it on");
                return;
            }

            var target = bridge.RoutingTty();
            var hasPending = false;
            if (!String.IsNullOrEmpty(target) && bridge.Grid.Sessions.TryGetValue(target, out var session))
            {
                // PendingTool is filled only from a captured PermissionRequest payload, and only
                // while the session is waiting on it — the same field that lights the risk badge.
                hasPending = !String.IsNullOrEmpty(session.PendingTool);
            }

            var canObserve = bridge.Agent?.Capabilities.ApprovalSignal ?? true;
            switch (Decide(approve, hasPending, canObserve))
            {
                case AnswerVia.MenuConfirm:
                    Answered(bridge, target, bridge.InjectKeyTo(target, KeyStroke.Return), "approved");
                    break;

                case AnswerVia.MenuReject:
                    Answered(bridge, target, bridge.InjectKeyTo(target, KeyStroke.Escape), "rejected");
                    break;

                case AnswerVia.UnobservedConfirm:
                    bridge.InjectKeyTo(target, KeyStroke.Return);
                    PluginLog.Info($"AnswerCommand: sent Yes by Return to {target} without approval observation (agent transport does not report approvals)");
                    break;

                case AnswerVia.UnobservedReject:
                    bridge.InjectKeyTo(target, KeyStroke.Escape);
                    PluginLog.Info($"AnswerCommand: sent No by Escape to {target} without approval observation (agent transport does not report approvals)");
                    break;

                default:
                    // No approval we can see. Beep rather than type a word at a prompt we cannot
                    // confirm is a menu (#21), or send a bare Return/Escape at an idle session.
                    bridge.Alert();
                    PluginLog.Info($"AnswerCommand: {(approve ? "Yes" : "No")} with no pending approval on {target ?? "(no target)"} — ignored");
                    break;
            }
        }

        // The keystroke landed or it did not — injection is atomic, so there is no third case. Only
        // a keystroke that landed answered the prompt, so only then is the captured payload cleared:
        // the rejection path fires no hook, and left the Yes dot and the "Allow?" bar lit until the
        // session's next prompt (#60). A keystroke that did not land leaves the badge, which is
        // still the truth, and says so.
        private static void Answered(BridgeManager bridge, String target, InjectionOutcome outcome, String verb)
        {
            if (outcome == InjectionOutcome.Ok)
            {
                bridge.Grid.ClearPendingApproval(target);
                PluginLog.Info($"AnswerCommand: {verb} the pending prompt on {target} by key");
                return;
            }

            bridge.Alert();
            PluginLog.Warning($"AnswerCommand: could not answer the pending prompt on {target} — {outcome}; the badge stays");
        }

        private static String LabelFor(String actionParameter)
        {
            switch (actionParameter)
            {
                case Yes: return "Yes";
                case No: return "No";
                case Up: return "Up";
                case Down: return "Down";
                case Enter: return "Enter";
                default: return actionParameter;
            }
        }

        // Every widget face draws its own label. A zero-width display name prevents Options+ from
        // adding a second, static label strip below the live bitmap.
        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            "\u200B";

        // The risk of whatever the targeted session is waiting on — i.e. what pressing Yes right now
        // would approve. None when nothing is pending, which leaves the key looking normal.
        private static ApprovalRisk TargetRisk()
        {
            var bridge = BridgeManager.Instance;
            var target = bridge.RoutingTty();
            if (String.IsNullOrEmpty(target))
            {
                return ApprovalRisk.None;
            }

            return bridge.Grid.Sessions.TryGetValue(target, out var session) ? session.Risk : ApprovalRisk.None;
        }

        /// <summary>
        /// The pending indicator belongs on both decisions: it tells the user that either key can
        /// answer now. Yes preserves the request's actual risk (amber or red); No is always amber
        /// because rejecting never authorizes the destructive command. Kept pure for #60 tests.
        /// </summary>
        internal static ApprovalRisk IndicatorRisk(Boolean approve, ApprovalRisk pendingRisk) =>
            approve ? pendingRisk : pendingRisk == ApprovalRisk.None ? ApprovalRisk.None : ApprovalRisk.Normal;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            // Icon basename == actionParameter (yes/no/up/down/enter .png in Resources/icons), matching
            // ControlCommand. Colours are currently unused by KeyImage but follow the palette convention
            // so a future switch to coloured tiles (see KeyImage) renders these sensibly.
            BitmapColor color;
            switch (actionParameter)
            {
                case Yes: color = KeyImage.Green; break;
                case No: color = KeyImage.Red; break;
                case Enter: color = KeyImage.Green; break;
                default: color = KeyImage.Slate; break; // Up, Down
            }

            var label = LabelFor(actionParameter);

            // Yes/No carry an approval badge for the session they'd answer: amber when it's waiting,
            // red when what's waiting is destructive. That's the "glance from across the room" cue —
            // you can see an answer is wanted, and whether to look first, before pressing anything.
            if (actionParameter == Yes || actionParameter == No)
            {
                var agentSetup = AgentBridgeNotice.FaceLabel(BridgeManager.Instance.AgentBridgeState);
                if (agentSetup != null)
                {
                    return KeyImage.RenderDecisionTile(imageSize, agentSetup, KeyImage.Gray, approve: actionParameter == Yes, risk: ApprovalRisk.None);
                }

                // Not wired: a grey tile keeps the check / cross, so the key is still recognisably
                // Yes or No, and the word says what to do about it. No badge — nothing can be
                // pending that the plugin could see (#58).
                var bridge = BridgeManager.Instance;
                var setup = LiveStatusFace.SetupWord(bridge.LiveStatusApplies, bridge.LiveStatus);
                if (setup != null)
                {
                    return KeyImage.RenderDecisionTile(imageSize, setup, KeyImage.Gray, approve: actionParameter == Yes, risk: ApprovalRisk.None);
                }

                var pendingRisk = TargetRisk();
                return KeyImage.RenderDecisionTile(
                    imageSize, label, color,
                    approve: actionParameter == Yes,
                    risk: IndicatorRisk(actionParameter == Yes, pendingRisk));
            }

            return KeyImage.RenderWidgetAction(imageSize, label, actionParameter);
        }
    }
}
