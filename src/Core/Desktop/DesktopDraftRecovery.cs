namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.IO;
    using System.Text.Json;

    /// <summary>One unsent transcript, held in memory independently of the system clipboard.</summary>
    internal sealed class DesktopDraftRecovery
    {
        private readonly Object _gate = new();
        private readonly IDesktopAutomation _automation;
        private readonly String _path;
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
            "app-not-running" => "OPEN APP · TAP TO RETRY",
            "not-trusted" => "ALLOW ACCESS",
            "composer-focus-changed" or "app-not-frontmost" => "FOCUS CHAT INPUT",
            "composer-selection-changed" => "CURSOR NOT READY",
            "draft-changed" or "draft-exists" => "INPUT CHANGED",
            // A retry goes to the original chat only; once that is gone it cannot succeed.
            "composer-target-changed" or "mode-changed" => "HOLD TO DISCARD",
            "append-unconfirmed" => "CHECK CHAT INPUT",
            "composer-unavailable" => "CHAT IS BUSY",
            _ => "HOLD TO DISCARD",
        }; } }
        internal Int64? PendingId { get { lock (_gate) { return _text != null ? _revision : null; } } }
        internal Int64? DiscardableId(VoicePhase phase) { lock (_gate) return phase == VoicePhase.Idle && !_inserting && _text != null ? _revision : null; }

        internal DesktopDraftRecovery(IDesktopAutomation automation, String path = null)
        {
            _automation = automation; _path = path;
            try { if (path != null && File.Exists(path)) {
                var saved = JsonSerializer.Deserialize<String[]>(File.ReadAllText(path));
                if (saved?.Length == 2) { _text = saved[0]; _error = saved[1]; _revision++; }
            } } catch { }
        }
        private void Save()
        {
            if (_path == null) return;
            try {
                if (_text == null)
                {
                    // The memory copy is already gone. A delete that fails (a scanner holding the file,
                    // the directory mid-removal) must not leave the old draft to come back on the next
                    // load: overwrite it with an empty document, which the constructor ignores.
                    try { File.Delete(_path); }
                    catch (Exception ex) when (File.Exists(_path))
                    {
                        PluginLog.Warning(ex, "Desktop draft file could not be deleted; emptied instead");
                        File.WriteAllText(_path, "[]");
                    }
                    return;
                }
                PrivateFiles.EnsurePrivateDirectory(Path.GetDirectoryName(_path));
                var temp = _path + ".tmp-" + Guid.NewGuid().ToString("N");
                try {
                    File.WriteAllText(temp, JsonSerializer.Serialize(new[] { _text, _error }));
                    PrivateFiles.EnsurePrivateFile(temp); File.Move(temp, _path, true);
                } finally { if (File.Exists(temp)) File.Delete(temp); }
            } catch (Exception ex) {
                PluginLog.Warning(ex, _text == null
                    ? "Desktop draft file could not be cleared; a consumed draft may reappear after a reload"
                    : "Desktop draft persistence failed; the draft is held in memory only until the next reload");
            }
        }

        internal Boolean Retain(String text) => Retain(text, null);

        internal Boolean Retain(String text, String error)
        {
            if (String.IsNullOrWhiteSpace(text)) { return false; }
            lock (_gate) { _text = text; _error = error == null ? null : SafeError(error); _revision++; Save(); }
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
                _text = null; _error = null; Save();
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
                    if (_revision == revision) { _text = null; _error = null; Save(); }
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
            lock (_gate) if (_revision == revision) { _error = code; Save(); }
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
