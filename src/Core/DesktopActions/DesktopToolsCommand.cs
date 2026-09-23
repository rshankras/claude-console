namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>Five direct Tools positions; each tap is pinned to the mode that was displayed.</summary>
    public sealed class DesktopToolsCommand : DesktopCommandBase
    {
        private readonly FailureFace _feedback;
        private readonly DesktopApprovalConfirmation _confirmation = new();
        private readonly ConcurrentDictionary<String, DesktopState> _shown = new();
        private readonly Dictionary<String, DesktopVoiceDraftCommand.ButtonHandler> _buttons = new();
        private readonly IReadOnlyList<DesktopWorkflowCommand.WorkflowDef> _tasks;
        private String _parameter, _working, _feedbackMode;
        private Int32 _busy;
        public DesktopToolsCommand()
        {
            this.SetWidget(true);
            _feedback = new FailureFace(() => this.ActionImageChanged(), 4000);
            DesktopServices.Lifetime.OnStop(_feedback.Dispose);
            DesktopServices.Lifetime.OnStop(() => { _working = null; Volatile.Write(ref _busy, 0); });
            _tasks = Array.Empty<DesktopWorkflowCommand.WorkflowDef>();
            if (!DesktopServices.Declared) return;
            this.AddParameter("copy_approve", "Copy Reply / Approve", "Tools");
            this.AddParameter("return_deny", "Return to App / Deny", "Tools");
            this.AddParameter("deny", "Deny (Codex)", "Tools")
                .SetDescription("Deny the displayed Codex request; this key is blank in ChatGPT");
            this.AddParameter("approve", "Approve (Codex)", "Tools")
                .SetDescription("Approve the displayed Codex request; this key is blank in ChatGPT");
            this.AddParameter("clipboard_review", "Paste into Chat / Review Code", "Tools");
            this.AddParameter("screenshot_tests", "Screenshot / Run Tests", "Tools");
            this.AddParameter("clear_screenshot", "Clear Added / Screenshot", "Tools");
            _tasks = DesktopWorkflowCommand.LoadWorkflows(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "claude-console", "desktop-workflows.json")).Where(DesktopWorkflowCommand.IsUsable).Take(9).ToArray();
            DesktopServices.OnMonitorChanged(state => { _confirmation.Observe(state); this.ActionImageChanged(); });
            DesktopServices.OnContextChanged(() => this.ActionImageChanged());
            DesktopServices.OnWorkflowChanged(() => this.ActionImageChanged());
            DesktopServices.OnVoiceChanged(() => this.ActionImageChanged());
            DesktopServices.OnDraftDiscarded(() => { _feedback.Clear(); this.ActionImageChanged(); });
        }

        internal static (String Kind, String Id) Resolve(String parameter, String mode) => (parameter, mode) switch
        {
            ("copy_approve", "ChatGPT") => ("capture", "copy"), ("copy_approve", "Codex") => ("approval", "approve"),
            ("return_deny", "ChatGPT") => ("capture", "return"), ("return_deny", "Codex") => ("approval", "deny"),
            ("deny", "Codex") => ("approval", "deny"),
            ("approve", "Codex") => ("approval", "approve"),
            ("clipboard_review", "ChatGPT") => ("capture", "clipboard"), ("clipboard_review", "Codex") => ("workflow", "review_changes"),
            ("screenshot_tests", "ChatGPT") => ("capture", "screenshot"), ("screenshot_tests", "Codex") => ("workflow", "run_tests"),
            ("clear_screenshot", "ChatGPT") => ("capture", "clear"), ("clear_screenshot", "Codex") => ("capture", "screenshot"),
            _ => (null, null),
        };

        internal new static Boolean IsHidden(String parameter, String mode) => parameter is "deny" or "approve" && mode != "Codex";

        internal static String Execute(String parameter, DesktopState shown, IDesktopAutomation automation,
            IDesktopAppAdapter app, DesktopContextCapture context, VoiceCaptureState capture,
            DesktopApprovalConfirmation confirmation, Action invalidate, Func<String, String> workflow)
        {
            if (IsHidden(parameter, shown?.Mode)) return null;
            var actual = automation.Status();
            if (!actual.SurfaceAvailable || shown == null || actual.Mode != shown.Mode)
            {
                return "Mode Changed";
            }
            var route = Resolve(parameter, shown.Mode);
            if (route.Kind == "capture") return context.Execute(route.Id, capture);
            if (route.Kind == "workflow") return workflow(route.Id);
            if (route.Kind == "approval")
            {
                DesktopApprovalCommand.Execute(route.Id, shown, app, automation, confirmation, DateTime.UtcNow, invalidate);
                return null;
            }
            return "Unavailable";
        }

        protected override Boolean ProcessButtonEvent2(String parameter, DeviceButtonEvent2 buttonEvent)
        {
            if (!_buttons.TryGetValue(parameter, out var handler)) _buttons[parameter] = handler = new();
            return handler.Handle(buttonEvent.EventType, () =>
            {
                if (!DesktopServices.Declared || !_shown.TryGetValue(parameter, out var state)) return null;
                var route = Resolve(parameter, state.Mode);
                return route.Kind == "workflow" && DesktopServices.WorkflowVoice.Face(route.Id, state.Mode, BridgeManager.Instance.Voice)?.Label == "Insert Draft"
                    ? DesktopServices.DraftRecovery.DiscardableId(BridgeManager.Instance.Voice.Phase) : null;
            }, () => this.RunCommand(parameter), id => DesktopServices.Run(() => DesktopServices.DraftRecovery.Discard(id, BridgeManager.Instance.Voice.Phase)));
        }

        protected override void RunCommand(String parameter)
        {
            if (!DesktopServices.Declared || !_shown.TryGetValue(parameter, out var shown) || Interlocked.Exchange(ref _busy, 1) != 0) return;
            _parameter = parameter; _working = parameter; _feedbackMode = shown.Mode;
            // Region capture can wait for the user. Keep the SDK's drawing/dispatch thread free.
            if (!DesktopServices.Run(() =>
            {
                try
                {
                    _feedback.Clear(); this.ActionImageChanged();
                    var feedback = Execute(parameter, shown, DesktopServices.Automation, DesktopServices.App,
                        DesktopServices.Context, BridgeManager.Instance.Voice, _confirmation, () => this.ActionImageChanged(), id =>
                        DesktopWorkflowCommand.Execute(id, DesktopServices.Automation, (p, _) => _tasks.FirstOrDefault(w => w.Id == p),
                            DesktopServices.WorkflowVoice, BridgeManager.Instance.Voice, DesktopServices.VoiceActions, (intent, sink) =>
                            {
                                BridgeManager.Instance.DraftTranscriptSink = sink;
                                try { BridgeManager.Instance.ToggleVoice(intent); }
                                finally { BridgeManager.Instance.DraftTranscriptSink = null; }
                            }, DesktopServices.Context));
                    if (feedback != null) _feedback.Show(feedback);
                }
                catch { _feedback.Show("Check App"); }
                finally { _working = null; Volatile.Write(ref _busy, 0); this.ActionImageChanged(); }
            }))
            {
                _working = null; Volatile.Write(ref _busy, 0);
            }
        }

        protected override String GetCommandDisplayName(String _, PluginImageSize __) => "\u200B";
        protected override BitmapImage GetCommandImage(String parameter, PluginImageSize size)
        {
            var state = DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable;
            if (IsHidden(parameter, state.Mode))
            {
                _shown.TryRemove(parameter, out _);
                return KeyImage.Render(size, String.Empty, KeyImage.Blue);
            }
            _shown[parameter] = state;
            var route = Resolve(parameter, state.Mode);
            var feedback = _feedback.IsActive && _parameter == parameter && _feedbackMode == state.Mode ? _feedback.Text : null;
            if (route.Kind == "capture")
            {
                if (route.Id == "copy") return DesktopCaptureCommand.RenderCopy(size, state, feedback,
                    _working == parameter && _feedbackMode == state.Mode);
                var face = DesktopCaptureCommand.Face(route.Id, DesktopServices.Context, feedback, _working == parameter && _feedbackMode == state.Mode);
                return KeyImage.RenderIntentTile(size, face.Label, face.Icon, face.Footer);
            }
            if (route.Kind == "workflow")
            {
                var task = _tasks.FirstOrDefault(w => w.Id == route.Id);
                var workflowFace = DesktopServices.WorkflowVoice.Face(task?.Id, state.Mode, BridgeManager.Instance.Voice);
                var face = workflowFace ?? DesktopWorkflowCommand.FaceFor(task, state.Mode, feedback);
                if (workflowFace == null && feedback == null && task != null && !task.RequiresSpeech && DesktopServices.Context.Count > 0)
                    face.Footer = "DRAFT";
                if (feedback != null && feedback is not ("Draft Ready" or "Insert Draft")) face = DesktopWorkflowCommand.FaceFor(task, state.Mode, feedback);
                return KeyImage.RenderIntentTile(size, face.Label, face.Icon, face.Footer);
            }
            if (route.Kind == "approval")
            {
                var pending = state.Activity == DesktopActivity.WaitingApproval;
                var armed = _confirmation.IsArmed(route.Id, state, DateTime.UtcNow);
                return KeyImage.RenderApprovalTile(size, armed ? "Press again" : route.Id == "approve" ? "Approve" : "Deny",
                    route.Id == "approve" ? "yes" : "no", feedback ?? (pending ? "REVIEW REQUEST" : "NO REQUEST"), pending ? state.Risk : ApprovalRisk.None);
            }
            return KeyImage.RenderControlTile(size, "Tools", "more", false, "Mode unavailable");
        }
    }
}
