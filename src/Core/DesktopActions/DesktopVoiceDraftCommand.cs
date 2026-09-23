namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Threading;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// Record, insert automatically, review, then use the separate Send Draft keypad action.
    /// A refused insertion keeps the original transcript for an explicit keypad retry.
    /// </summary>
    public class DesktopVoiceDraftCommand : DesktopCommandBase
    {
        private readonly ListeningFace _face;
        private readonly FailureFace _fail;
        private readonly FailureFace _ready;
        private readonly ButtonHandler _buttons = new();
        public DesktopVoiceDraftCommand()
            : base(displayName: "Dictate", description: "Tap to record, tap again to add your words below the current draft. Pending draft: tap to retry, hold to discard the retained recording. Review in the app, then use Send.", groupName: "Agent")
        {
            this.SetWidget(true);
            _face = new ListeningFace(() => this.ActionImageChanged());
            DesktopServices.Lifetime.Bind(() => _face.SetEnabled(true), () => _face.SetEnabled(false));
            // Keep the outcome visible until the next attempt; a brief failure was easy to miss
            // while the user looked at the composer waiting for their words.
            _fail = new FailureFace(() => this.ActionImageChanged(), holdMs: Timeout.Infinite);
            DesktopServices.Lifetime.OnStop(_fail.Dispose);
            _ready = new FailureFace(() => this.ActionImageChanged());
            DesktopServices.Lifetime.OnStop(_ready.Dispose);
            if (DesktopServices.Declared)
            {
                DesktopServices.OnWorkflowChanged(() => this.ActionImageChanged());
                DesktopServices.OnContextChanged(() => this.ActionImageChanged());
                DesktopServices.OnDraftChanged(() => this.ActionImageChanged());
                DesktopServices.OnDraftReady(() => _ready.Show("Draft Ready"));
                DesktopServices.OnDraftDiscarded(() =>
                {
                    _fail.Clear();
                    _ready.Show("Discarded");
                });
            }

            // A dictation that failed says so on the key that was pressed, for a moment (#18). Only
            // this key's own captures: a failure routed to another key is that key's to show.
            DesktopServices.OnVoiceFailed((intent, text) =>
            {
                if (intent == VoiceIntent.DesktopDraft) { _fail.Show(text); }
            });

            // The engine owns "is the mic running, and for whom" (#28). This key only reflects it,
            // so a capture stopped from ANOTHER voice key clears this face too.
            DesktopServices.OnVoiceChanged(() =>
            {
                if (BridgeManager.Instance.Voice.IsRecording(VoiceIntent.DesktopDraft))
                {
                    if (!_face.IsActive) { _face.Start(); }
                }
                else if (_face.IsActive)
                {
                    _face.Stop();
                }
                this.ActionImageChanged();
            });
        }

        // Handle both gestures ourselves: default SDK dispatch would insert on button-down,
        // before it knew a long press was coming. A tap runs only on release.
        protected override Boolean ProcessButtonEvent2(String actionParameter, DeviceButtonEvent2 buttonEvent) =>
            _buttons.Handle(buttonEvent.EventType,
                () => DesktopServices.Declared
                    ? DesktopServices.DraftRecovery.DiscardableId(BridgeManager.Instance.Voice.Phase) : null,
                () => this.RunCommand(actionParameter),
                id =>
                {
                    if (DesktopServices.Declared)
                        DesktopServices.Run(() => DesktopServices.DraftRecovery.Discard(id, BridgeManager.Instance.Voice.Phase));
                });

        internal sealed class ButtonHandler
        {
            private Boolean _pressed;
            private Boolean _held;
            private Int64? _draftAtPress;

            internal Boolean Handle(DeviceButtonEventType type, Func<Int64?> pendingDraft,
                Action tap, Action<Int64> discard)
            {
                switch (type)
                {
                    case DeviceButtonEventType.Press:
                        _pressed = true;
                        _held = false;
                        _draftAtPress = pendingDraft();
                        break;
                    case DeviceButtonEventType.LongPress:
                        if (!_pressed || _held) { break; }
                        _held = true;
                        if (_draftAtPress.HasValue && pendingDraft() == _draftAtPress)
                        { discard(_draftAtPress.Value); }
                        break;
                    case DeviceButtonEventType.Release:
                        if (!_pressed) { break; }
                        _pressed = false;
                        if (!_held) { tap(); }
                        _draftAtPress = null;
                        break;
                }
                return true; // RepeatPress and duplicate releases never invoke the default action.
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            DesktopServices.Run(() => this.RunDesktopCommand(actionParameter));
        }

        private void RunDesktopCommand(String actionParameter)
        {
            if (!DesktopServices.Declared)
            {
                PluginLog.Warning("DesktopVoiceDraftCommand: no desktop surface declared");
                return;
            }

            // One door for every voice key. Whether this press starts, stops, or is refused — and
            // where a stopped capture's transcript is routed — is the engine's call, not this key's.
            _fail.Clear();
            _ready.Clear();
            if (DesktopServices.DraftRecovery.Pending && !BridgeManager.Instance.Voice.IsTranscribing(VoiceIntent.DesktopDraft))
            {
                var result = DesktopServices.WorkflowVoice.OwnsPending
                    ? DesktopServices.WorkflowVoice.RetryFromHome(BridgeManager.Instance.Voice, DesktopServices.VoiceActions)
                    : DesktopServices.DraftRecovery.Insert();
                if (result == "Draft Ready") { _ready.Show(result); }
                else if (result != null) { _fail.Show(result); }
                return;
            }
            if (DesktopServices.Context.IsBusy) { _fail.Show("Finish Capture"); return; }
            var mode = DesktopServices.Automation.Status().Mode;
            if (DesktopServices.Automation.SupportsAppend || DesktopServices.Context.Count > 0 || DesktopServices.WorkflowVoice.Face("context_voice", mode, BridgeManager.Instance.Voice) != null)
            {
                var feedbackWithContext = DesktopServices.WorkflowVoice.Press(new DesktopWorkflowCommand.WorkflowDef
                    { Id = "context_voice", Label = "Dictate", Input = "voice", Prompt = "{brief}", Submit = false },
                    mode, BridgeManager.Instance.Voice, DesktopServices.VoiceActions, (intent, sink) =>
                    {
                        BridgeManager.Instance.DraftTranscriptSink = sink;
                        try { BridgeManager.Instance.ToggleVoice(intent); }
                        finally { BridgeManager.Instance.DraftTranscriptSink = null; }
                    }, allowSend: false, append: DesktopServices.Automation.SupportsAppend);
                if (feedbackWithContext != null) _fail.Show(feedbackWithContext);
                return;
            }
            DesktopServices.VoiceActions.RequestDictation(VoiceIntent.DesktopDraft, BridgeManager.Instance.Voice,
                intent => BridgeManager.Instance.ToggleVoice(intent), out var feedback);
            if (feedback != null) { _fail.Show(feedback); }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "\u200B";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var contextual = DesktopServices.Declared ? DesktopServices.WorkflowVoice.Face("context_voice", DesktopServices.Monitor.Current.Mode, BridgeManager.Instance.Voice) : null;
            if (contextual.HasValue && !_fail.IsActive)
            {
                var shown = contextual.Value.Label == "Send Draft" ? ("Dictate", "voice_draft", "ADD TO DRAFT") : contextual.Value;
                return KeyImage.RenderIntentTile(imageSize, shown.Item1, shown.Item2, shown.Item3);
            }
            var face = DesktopDictationFace.For(VoiceIntent.DesktopDraft, BridgeManager.Instance.Voice,
                _fail.IsActive ? _fail.Text : _ready.IsActive ? _ready.Text : null, _face.Icon,
                DesktopServices.Declared && DesktopServices.DraftRecovery.Pending);
            return KeyImage.RenderIntentTile(imageSize, face.Label, face.Icon, face.Footer);
        }
    }
}
