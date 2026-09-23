namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;

    /// <summary>One unsent transcript, held in memory independently of the system clipboard.</summary>
    internal sealed class DesktopDraftRecovery
    {
        private readonly Object _gate = new();
        private readonly IDesktopAutomation _automation;
        private String _text;
        private String _error;
        private Int64 _revision;
        private Boolean _inserting;
        internal event Action Changed;
        internal event Action Ready;
        internal event Action Discarded;
        internal void NotifyReady() => Ready?.Invoke();
        internal Boolean Pending { get { lock (_gate) { return _text != null; } } }
        internal String RetryHint { get { lock (_gate) return _error switch
        {
            "composer-focus-changed" or "app-not-frontmost" => "FOCUS CHAT INPUT",
            "composer-selection-changed" => "CURSOR NOT READY",
            "draft-changed" or "draft-exists" => "INPUT CHANGED",
            "composer-target-changed" or "mode-changed" => "CHAT CHANGED",
            "append-unconfirmed" => "CHECK CHAT INPUT",
            "composer-unavailable" => "CHAT IS BUSY",
            _ => "HOLD TO DISCARD",
        }; } }
        internal Int64? PendingId { get { lock (_gate) { return _text != null ? _revision : null; } } }
        internal Int64? DiscardableId(VoicePhase phase) { lock (_gate) return phase == VoicePhase.Idle && !_inserting && _text != null ? _revision : null; }

        internal DesktopDraftRecovery(IDesktopAutomation automation) => _automation = automation;

        internal Boolean Retain(String text) => Retain(text, null);

        internal Boolean Retain(String text, String error)
        {
            if (String.IsNullOrWhiteSpace(text)) { return false; }
            lock (_gate) { _text = text; _error = error == null ? null : SafeError(error); _revision++; }
            if (error != null) PluginLog.Warning("Desktop insertion initial: " + SafeError(error));
            Changed?.Invoke();
            return true;
        }

        // A hold belongs to the draft present at button-down. A newer result arriving during
        // that hold must survive. Discard touches only our memory, never the app or clipboard.
        internal Boolean Discard(Int64 expectedId, VoicePhase phase = VoicePhase.Idle)
        {
            if (phase != VoicePhase.Idle) { return false; }
            lock (_gate)
            {
                if (_inserting || _text == null || _revision != expectedId) { return false; }
                _text = null; _error = null;
            }
            Changed?.Invoke();
            Discarded?.Invoke();
            return true;
        }

        // An explicit keypad retry selects the current composer. It never submits, overwrites
        // an existing draft, or reads whatever another application copied in the meantime.
        internal String Insert(Func<String, (Boolean Success, String Error)> insert = null)
        {
            String text; Int64 revision;
            lock (_gate)
            {
                if (_text == null) return null;
                if (_inserting) return "Please Wait";
                _inserting = true; text = _text; revision = _revision;
            }
            try
            {
                String error = null;
                var result = insert != null ? insert(text) : (_automation.RecoverDraft(text, out error), error);
                if (!result.Item1)
                {
                    RecordFailure(result.Item2, revision);
                    return result.Item2 switch
                    {
                        "draft-exists" => "Draft Exists",
                        "draft-changed" => "Draft Changed",
                        "composer-target-changed" or "mode-changed" => "Chat Changed",
                        "append-unconfirmed" => "Check Draft",
                        "composer-focus-changed" or "app-not-frontmost" => "Focus Input",
                        "composer-selection-changed" => "Check Cursor",
                        _ => "Insert Failed",
                    };
                }
                lock (_gate)
                {
                    // A new transcript arriving during the slow native call belongs to its
                    // own retry; confirming this one must not clear the newer recording.
                    if (_revision == revision) { _text = null; _error = null; }
                }
                try { _automation.FocusApp(); } catch { }
                Changed?.Invoke();
                return "Draft Ready";
            }
            catch { RecordFailure("insertion-exception", revision); return "Insert Failed"; }
            finally { lock (_gate) _inserting = false; }

        }

        private void RecordFailure(String error, Int64 revision)
        {
            var code = SafeError(error);
            lock (_gate) if (_revision == revision) _error = code;
            // Only explicit failed insertions log a fixed code. Never include prompt,
            // clipboard, image, chat title, helper response, or exception text.
            PluginLog.Warning("Desktop insertion retry: " + code);
            Changed?.Invoke();
        }

        internal static String SafeError(String error) => error is
            "draft-exists" or "draft-changed" or "composer-target-changed" or "mode-changed"
            or "append-unconfirmed" or "composer-focus-changed" or "composer-selection-changed"
            or "composer-value-unavailable" or "surface-unavailable" or "composer-unavailable"
            or "no-unique-composer" or "app-not-frontmost" or "write-not-applied" or "clipboard-changed"
            or "clipboard-busy" or "text-too-long" or "not-trusted" or "app-not-running"
            or "unsupported" or "empty-text" or "insertion-exception" ? error : "unclassified";
    }
}
