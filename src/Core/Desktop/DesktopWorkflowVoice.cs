namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using DesktopActions;

    /// <summary>One spoken task brief. The recipe and destination belong to the starting key.</summary>
    internal sealed class DesktopWorkflowVoice
    {
        private readonly Object _gate = new();
        private readonly IDesktopAutomation _automation;
        private readonly DesktopDraftRecovery _recovery;
        private readonly DesktopContextCapture _context;
        private String _owner, _mode, _target, _failure;
        private DesktopAppendTarget _appendTarget;
        private Boolean _waiting, _ready, _recovering;
        private Int64 _generation, _lastSend = Int64.MinValue;
        private Int64? _pending;
        private Int64 _contextRevision;
        internal Func<Int64> Clock { get; set; } = () => Environment.TickCount64;
        internal event Action Changed;

        internal DesktopWorkflowVoice(IDesktopAutomation automation, DesktopDraftRecovery recovery, DesktopContextCapture context = null)
        {
            _automation = automation; _recovery = recovery; _context = context;
            recovery.Discarded += Reset;
            recovery.Changed += () =>
            {
                lock (_gate)
                    if (!_recovering && _pending != null && _pending != recovery.PendingId) Reset();
            };
        }

        internal void Reset()
        {
            lock (_gate) { _owner = _mode = _target = _failure = null; _appendTarget = null; _waiting = _ready = false; _pending = null; _generation++; }
            Changed?.Invoke();
        }

        internal void Fail(VoiceIntent intent, String failure)
        {
            lock (_gate)
            {
                if (intent != VoiceIntent.DesktopDraft || !_waiting) return;
                _waiting = false; _failure = failure;
            }
            Changed?.Invoke();
        }

        internal Boolean OwnsPending { get { lock (_gate) return _pending != null && _pending == _recovery.PendingId; } }
        internal String RetryFromHome(VoiceCaptureState capture, DesktopVoiceActions voice)
        {
            lock (_gate)
            {
                if (!OwnsPending) return null;
                var mode = _automation.Status().Mode;
                if (mode != _mode) return "Use " + _mode;
                return Press(new() { Id = _owner }, mode, capture, voice, (_, _) => { });
            }
        }

        internal String Press(DesktopWorkflowCommand.WorkflowDef workflow, String mode,
            VoiceCaptureState capture, DesktopVoiceActions voice, Action<VoiceIntent, Func<String, String>> toggle, Boolean allowSend = true, Boolean append = false,
            DesktopAppendTarget preparedAppend = null, Boolean submitImmediately = false)
        {
            lock (_gate)
            {
                if (_context?.IsBusy == true) return "Finish Capture";
                // Any local recording can be stopped, but its original callback is never changed.
                if (capture.Phase != VoicePhase.Idle)
                {
                    if (capture.Phase == VoicePhase.Transcribing) return null;
                    voice.RequestDictation(VoiceIntent.DesktopDraft, capture, i => toggle(i, null), out var stopping);
                    return stopping;
                }
                if (_lastSend != Int64.MinValue && Clock() - _lastSend < 1000) return "Sent";
                if (_owner == workflow.Id && _mode == mode && _ready)
                {
                    if (!allowSend && !append) return "Draft Ready";
                    if (allowSend && !_automation.SendPreparedDraft(_mode, _target, out var error))
                    {
                        if (error is "composer-target-changed" or "mode-changed" or "no-sendable-draft") Reset();
                        return Problem(error);
                    }
                    if (allowSend) { Reset(); _lastSend = Clock(); return "Sent"; }
                    // Dictate remains Dictate after insertion: a fresh tap adds another instruction.
                    Reset();
                }
                if (_recovery.Pending)
                {
                    if (_owner != workflow.Id || _mode != mode || _pending != _recovery.PendingId) return "Insert Draft";
                    String error = null;
                    var target = _appendTarget?.Target ?? _automation.PrepareDraft(mode, false, out error);
                    if (String.IsNullOrEmpty(target)) return Problem(error);
                    if (_context != null && !_context.Prepare(mode, target, out _, out _, out var contextRetryError)) return contextRetryError;
                    String outcome;
                    _recovering = true;
                    try { outcome = _recovery.Insert(text =>
                    {
                        String why;
                        var success = _appendTarget == null
                            ? _automation.WritePreparedDraft(text, mode, target, true, out why)
                            : _automation.AppendPreparedDraft(text, mode, _appendTarget, true, out why);
                        return (success, why);
                    }); }
                    finally { _recovering = false; }
                    if (outcome == "Draft Ready") { _context?.Consume(_contextRevision); _ready = true; _target = target; _pending = null; _failure = null; }
                    Changed?.Invoke();
                    return outcome;
                }
                String prepareError = null;
                prepareError = null;
                _appendTarget = append ? preparedAppend ?? _automation.PrepareAppend(mode, out prepareError) : null;
                var destination = append ? _appendTarget?.Target : _automation.PrepareDraft(mode, true, out prepareError);
                if (String.IsNullOrEmpty(destination)) return Problem(prepareError);
                String context = ""; Int64 contextRevision = -1;
                if (_context != null && !_context.Prepare(mode, destination, out context, out contextRevision, out var contextError)) return contextError;
                _contextRevision = contextRevision;
                var recipe = workflow.Prompt; // immutable snapshot, independent of later slots/mode/config
                _owner = workflow.Id; _mode = mode; _target = destination;
                _ready = false; _waiting = true; _failure = null; _pending = null;
                var generation = ++_generation;
                if (!workflow.RequiresSpeech)
                {
                    var delivery = Deliver(generation, recipe, null, contextRevision, context);
                    if (delivery != null) return delivery;
                    // Existing material and recovered insertions always wait for explicit Send.
                    // A fresh empty-composer preset keeps its one-tap behavior, but never sends
                    // text the user changed between the insertion and submission helpers.
                    if (!submitImmediately || !String.IsNullOrEmpty(context) || _appendTarget?.HasContent == true)
                        return "Draft Ready";
                    if (!_automation.SendPreparedPrompt(recipe, _mode, _target, out var sendError)) return Problem(sendError);
                    Reset(); _lastSend = Clock(); return "Sent";
                }
                voice.RequestDictation(VoiceIntent.DesktopDraft, capture,
                    i => toggle(i, text => Deliver(generation, recipe, text, contextRevision, context)), out var feedback);
                if (feedback != null || capture.Phase == VoicePhase.Idle) { _waiting = false; _failure = feedback ?? _failure ?? "Not Recording"; }
                Changed?.Invoke();
                return feedback ?? _failure;
            }
        }

        private String Deliver(Int64 generation, String recipe, String brief, Int64 contextRevision, String context)
        {
            lock (_gate)
            {
                if (generation != _generation || !_waiting) return "Cancelled";
                if (brief != null && String.IsNullOrWhiteSpace(brief)) { _waiting = false; _failure = "No Speech"; Changed?.Invoke(); return _failure; }
                var composed = (brief == null ? recipe : recipe.Replace("{brief}", brief.Trim(), StringComparison.Ordinal)) + context;
                Boolean inserted;
                String insertionError = null;
                try { inserted = _appendTarget == null
                    ? _automation.WritePreparedDraft(composed, _mode, _target, false, out insertionError)
                    : _automation.AppendPreparedDraft(composed, _mode, _appendTarget, false, out insertionError); }
                catch { inserted = false; insertionError = "insertion-exception"; }
                _waiting = false;
                if (!inserted)
                {
                    _recovery.Retain(composed, insertionError ?? "unclassified"); _pending = _recovery.PendingId; _failure = "Insert Draft";
                    Changed?.Invoke(); return _failure;
                }
                _context?.Consume(contextRevision);
                _ready = true; _failure = null;
                try { _automation.FocusApp(); } catch { }
                _recovery.NotifyReady(); Changed?.Invoke(); return null;
            }
        }

        internal (String Label, String Icon, String Footer)? Face(String id, String mode, VoiceCaptureState capture)
        {
            // Rendering and button-down eligibility must never wait on speech delivery.
            if (!System.Threading.Monitor.TryEnter(_gate))
                return _owner == id && _mode == mode ? ("Preparing", "voice_draft", "WAIT") : null;
            try
            {
                if (_owner != id || _mode != mode) return null;
                if (_ready) return ("Send Draft", "send", "REVIEW FIRST");
                if (_pending != null && _pending == _recovery.PendingId) return ("Insert Draft", "voice_draft", _recovery.RetryHint);
                if (_waiting) return capture.IsRecording(VoiceIntent.DesktopDraft)
                    ? ("Listening", "voice", "TAP TO FINISH") : ("Preparing", "voice_draft", "WAIT");
                return _failure != null ? (_failure, "voice_draft", "TAP TO RETRY") : null;
            }
            finally { System.Threading.Monitor.Exit(_gate); }
        }

        internal static String Problem(String error) => error switch
        {
            "draft-exists" => "Draft Exists",
            "draft-changed" => "Draft Changed",
            "composer-target-changed" or "mode-changed" => "Chat Changed",
            "no-sendable-draft" => "No Draft",
            "unsupported" => "Use App",
            _ => "Check App",
        };
    }
}
