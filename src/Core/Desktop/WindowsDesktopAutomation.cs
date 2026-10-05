namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// Windows desktop automation client. The packaged helper owns every UI Automation call so
    /// a hung or malformed Electron tree can be killed without wedging LogiPluginService. Its
    /// wire contract intentionally mirrors VizhiAxBridge: the monitor and every key consume the
    /// same snapshot and result JSON on both operating systems.
    /// </summary>
    internal sealed class WindowsDesktopAutomation : IDesktopAutomation
    {
        internal const String HelperFileName = "vizhi-desktop-uia.exe";

        private readonly IDesktopAppAdapter _app;

        // The monitor's polls and keys use separate serving helpers (#155),
        // and a status read must never hold up a press. Each lane falls back to a one-shot
        // process when it is busy, so neither waits on the other's work.
        private readonly UiaHelperHost _poll;
        private readonly UiaHelperHost _keys;
        private readonly UiaHelperHost _foreground;

        internal Func<List<String>, Int32, String> Runner { get; set; }

        internal static Boolean IsPackaged => PluginPaths.PackagedFile(HelperFileName) != null;

        public WindowsDesktopAutomation(IDesktopAppAdapter app)
        {
            _app = app;
            _poll = new UiaHelperHost("poll", () => PluginPaths.PackagedFile(HelperFileName));
            _keys = new UiaHelperHost("keys", () => PluginPaths.PackagedFile(HelperFileName));
            // A Win32-only process never invokes UIA. Windows can refuse restoration from the
            // process that performed the UIA click; sharing the key server broke that boundary.
            _foreground = new UiaHelperHost("foreground", () => PluginPaths.PackagedFile(HelperFileName), serveVerb: "serve-win32");
            this.Runner = (args, timeoutMs) => (UseForegroundLane(args) ? _foreground : UsePollLane(args) ? _poll : _keys).Run(args, timeoutMs);
        }

        internal static Boolean IsPollVerb(List<String> args) => args.Count > 0 && args[0] is "frontmost" or "status";
        // A key's safety/status checks belong with the key too, even if a monitor status read
        // is already in flight. Routing only by verb forced these checks into one-shot fallbacks.
        internal static Boolean UsePollLane(List<String> args) => IsPollVerb(args) && !DesktopActionRunner.IsExecuting;
        internal static Boolean UseForegroundLane(List<String> args) => args.Count > 0 && args[0] is "focus" or "restore-front";

        /// <summary>Ends all serving helpers; called when the plugin stops, so nothing outlives it.</summary>
        internal void Shutdown()
        {
            _poll.Shutdown();
            _keys.Shutdown();
            _foreground.Shutdown();
        }
        internal void Start() { _poll.Start(); _keys.Start(); _foreground.Start(); }
        internal void WarmUp() { _poll.WarmUp(); _keys.WarmUp(); _foreground.WarmUp(); }

        public Boolean? IsAppFrontmost()
        {
            // A process check only: passive polling backs off in other apps without asking
            // Chromium to build or traverse its accessibility tree. Two seconds, not one: when the
            // serving helper is busy this runs as a one-shot process, and the 14 MB single-file
            // helper can take over a second to start on an idle or EDR-managed machine; a killed
            // check reads as a false "not in front" (seen 2026-09-30).
            var json = this.Runner(BaseArgs("frontmost"), 2000);
            return TryParseOk(json, out var root) && root.TryGetProperty("frontmost", out var front)
                && front.ValueKind is JsonValueKind.True or JsonValueKind.False ? front.GetBoolean() : null;
        }

        public DesktopSnapshot Status()
        {
            // The same arguments the macOS client sends, so the two helpers answer the same
            // questions from the same adapter and one snapshot contract serves both.
            var args = BaseArgs("status");
            AddEach(args, "--approve", _app.ApproveLabels);
            AddEach(args, "--deny", _app.DenyLabels);
            AddEach(args, "--stop", _app.StopLabels);
            AddOne(args, "--attention", _app.AttentionMarker);
            AddOne(args, "--mode-prefix", _app.ModePrefix);
            if (!String.IsNullOrEmpty(_app.ConversationItemMarker))
            {
                AddOne(args, "--conv-marker", _app.ConversationItemMarker);
                AddOne(args, "--state-awaiting", _app.ConversationAwaitingText);
                AddEach(args, "--state-unread", _app.ConversationUnreadTexts);
                AddEach(args, "--state-running", _app.ConversationRunningTexts);
            }
            foreach (var mode in _app.ModeNames)
            {
                if (_app.ConversationIdleImages(mode) is Int32 count)
                {
                    AddOne(args, "--idle-images", $"{mode}={count}");
                }
            }
            AddContextLabels(args);
            AddEach(args, "--voice-start", _app.StartVoiceLabels);
            AddEach(args, "--voice-end", _app.EndVoiceLabels);
            this.AddReplyLabels(args);
            AddOne(args, "--send-label", _app.SendLabel);

            // The cached subtree fetch measured ~320 ms for the live app's 365 nodes; the margin
            // covers a long conversation, not a hung window server — BoundedProcess kills those.
            return DesktopSnapshot.Parse(this.Runner(args, 3000));
        }

        public Boolean Press(String[] labels, out String matched) =>
            this.PressGuarded(labels, null, out matched, out _);

        public Boolean PressExact(String[] labels)
        {
            if (labels?.Length is not > 0) { return false; }
            var args = BaseArgs("press-exact");
            AddEach(args, "--label", labels);
            if (!TryParseOk(this.Runner(args, 4000), out var root)) { return false; }
            this.KeepFront(root);
            return true;
        }

        public Boolean PressGuarded(String[] labels, String expectCard, out String matched, out String error)
        {
            matched = null;
            error = null;
            if (labels == null || labels.Length == 0)
            {
                return false;
            }

            var args = BaseArgs("press");
            AddEach(args, "--label", labels);
            AddOne(args, "--expect-near", expectCard);
            var json = this.Runner(args, 4000);
            if (!TryParseOk(json, out var root))
            {
                error = Describe(json);
                PluginLog.Warning($"WindowsDesktopAutomation.Press({String.Join("|", labels)}): {error}");
                return false;
            }

            matched = ReadString(root, "matched");
            var restored = this.KeepFront(root);
            // A landed press is the record Logitech QA and the device pass need; the macOS client
            // logs only failures, but on Windows where the foreground went matters too.
            PluginLog.Info($"WindowsDesktopAutomation.Press: '{matched}' landed; front {ReadString(root, "frontBefore")} -> {ReadString(root, "frontAfter")}"
                + (restored is Boolean handed ? $"; focus handed back: {(handed ? "yes" : "refused")}" : ""));
            return true;
        }

        /// <summary>
        /// A press is a click to Chromium, and a click activates the window — so on Windows an
        /// Approve pressed from the editor would leave the user in the chat app. The helper
        /// reports the move; a separate, persistent UIA-free process hands the foreground back (the
        /// process that made the UIA call is refused; measured live 2026-09-30). The press has
        /// already landed, so a refused restore is logged, never a failure.
        /// </summary>
        private Boolean? KeepFront(JsonElement press)
        {
            if (!press.TryGetProperty("frontMoved", out var moved) || moved.ValueKind != JsonValueKind.True
                || !press.TryGetProperty("frontBeforeHwnd", out var hwnd) || hwnd.ValueKind != JsonValueKind.Number)
            {
                return null;
            }
            var args = new List<String> { "restore-front", "--hwnd", hwnd.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) };
            AddEach(args, "--process", _app.WindowsProcessNames);
            var json = this.Runner(args, 1500);
            if (!TryParseOk(json, out _))
            {
                PluginLog.Warning($"WindowsDesktopAutomation: the press activated the app and focus could not be handed back: {Describe(json)}");
                return false;
            }
            return true;
        }

        public Boolean PressInMode(String[] labels, String mode, out String matched)
        {
            matched = null;
            if (String.IsNullOrEmpty(mode) || String.IsNullOrEmpty(_app.ModePrefix) || labels?.Length is not > 0)
            {
                return false;
            }
            var args = BaseArgs("press");
            AddOne(args, "--mode-prefix", _app.ModePrefix);
            AddOne(args, "--expect-mode", mode);
            AddEach(args, "--label", labels);
            if (!TryParseOk(this.Runner(args, 4000), out var root)) { return false; }
            matched = ReadString(root, "matched");
            this.KeepFront(root);
            return true;
        }

        public Boolean SetVoiceChat(Boolean active, out String error)
        {
            if (_app.StartVoiceLabels.Length == 0 || _app.EndVoiceLabels.Length == 0)
            {
                error = "unsupported";
                return false;
            }
            var args = BaseArgs("voice");
            AddOne(args, "--action", active ? "start" : "end");
            AddEach(args, "--voice-start", _app.StartVoiceLabels);
            AddEach(args, "--voice-end", _app.EndVoiceLabels);
            var json = this.Runner(args, 4000);
            error = TryParseOk(json, out _) ? null : Describe(json);
            return error == null;
        }

        internal Action<String> ChangesTrace { get; set; }

        public Boolean OpenChanges(out String error)
        {
            void Trace(String code) { try { ChangesTrace?.Invoke(DesktopChangesTrace.SafeCode(code)); } catch { } }
            Trace("requested");
            var args = BaseArgs("open-panel");
            AddOne(args, "--mode-prefix", _app.ModePrefix);
            AddOne(args, "--expect-mode", "Codex");
            AddEach(args, "--panel-open", _app.ShowDiffLabels);
            AddEach(args, "--panel-open-turn", _app.ShowDiffTurnLabels);
            AddEach(args, "--panel-visible", _app.ChangesPanelLabels);
            AddOne(args, "--conv-marker", _app.ConversationItemMarker);
            // The helper waits up to 3 s for the panel after its checks and the press.
            var json = this.Runner(args, 7000);
            if (!TryParseOk(json, out var result) || !result.TryGetProperty("opened", out var opened) || opened.ValueKind != JsonValueKind.True)
            {
                error = TryParseOk(json, out _) ? "panel-unconfirmed" : DesktopChangesTrace.ErrorFrom(json);
                Trace(error);
                return false;
            }
            Trace(result.TryGetProperty("alreadyOpen", out var already) && already.ValueKind == JsonValueKind.True ? "already-open" : "opened");
            error = null;
            return true;
        }

        public DesktopSearchSnapshot Search(String action, String target = null, String query = null, String value = null, String title = null, String origin = null, String mode = null)
        {
            var args = BaseArgs("search");
            AddOne(args, "--action", action);
            AddOne(args, "--mode-prefix", _app.ModePrefix);
            AddOne(args, "--expect-mode", mode ?? "ChatGPT");
            AddEach(args, "--search", _app.ControlLabels(DesktopControl.Search));
            AddEach(args, "--search-field", _app.SearchFieldLabels);
            AddEach(args, "--result-host", _app.SearchResultHosts);
            AddEach(args, "--result-path", _app.SearchResultPaths);
            AddEach(args, "--result-group", _app.SearchResultGroups);
            // An empty query is a real value here (the field just opened), so these are added
            // whenever the caller supplied them, empty or not.
            if (target != null) args.AddRange(new[] { "--target", target });
            if (query != null) args.AddRange(new[] { "--query", query });
            if (value != null) args.AddRange(new[] { "--value", value });
            if (title != null) args.AddRange(new[] { "--title", title });
            if (origin != null) args.AddRange(new[] { "--origin", origin });
            // Opening presses Search, waits for the field and scans three times; the first
            // morning run after the laptop slept took over 5 s and was killed, which left the
            // keypad on Search unavailable while the app's search box stood open.
            var snapshot = DesktopSearchSnapshot.Parse(this.Runner(args, 10000));
            // Outcomes only — never the query or a result title. The timer's read and probe
            // steps repeat every second, so only the explicit steps are recorded.
            if (action is "open" or "focus" or "write" or "select")
            {
                PluginLog.Info($"WindowsDesktopAutomation.Search: {action} in {mode ?? "ChatGPT"} "
                    + (snapshot.Available ? $"ready; {snapshot.Results.Count} result(s)" : $"refused: {snapshot.Error}"));
            }
            return snapshot;
        }

        public Boolean PressConversation(String title)
            => PressConversation(title, focus: false);

        public Boolean OpenConversation(String title)
            => PressConversation(title, focus: true);

        private Boolean PressConversation(String title, Boolean focus)
        {
            if (String.IsNullOrWhiteSpace(title) || String.IsNullOrEmpty(_app.ConversationItemMarker))
            {
                return false;
            }
            // No focus hand-back: navigation deliberately brings the chosen chat forward.
            var args = BaseArgs("press");
            args.AddRange(new[] { "--label", title, "--conversation", _app.ConversationItemMarker });
            if (focus) args.Add("--focus-after");
            if (!TryParseOk(this.Runner(args, focus ? 6000 : 4000), out var result)) { return false; }
            if (focus)
            {
                if (!result.TryGetProperty("focused", out var focused)) this.FocusApp(); // pre-1.1.1 helper
                else if (focused.ValueKind == JsonValueKind.False)
                    PluginLog.Warning("WindowsDesktopAutomation.OpenConversation: chat opened but Windows refused focus; press not repeated");
            }
            PluginLog.Info("WindowsDesktopAutomation.PressConversation: opened the chosen chat");
            return true;
        }

        public Boolean WriteComposer(String text, Boolean send, out String error) =>
            this.WriteComposer(text, send, false, out error);

        public Boolean RecoverDraft(String text, out String error) =>
            this.WriteComposer(text, false, true, out error);

        // The same argument shapes as the macOS client, so the two helpers apply the same
        // guards: the prepared target, the composer hints, and the labels that mean the
        // composer is not ready (a running task, a waiting approval, a voice session).
        private Boolean WriteComposer(String text, Boolean send, Boolean acceptExisting, out String error,
            String mode = null, String target = null)
        {
            error = null;
            if (String.IsNullOrWhiteSpace(text))
            {
                error = "empty text";
                return false;
            }

            var args = BaseArgs("write");
            AddOne(args, "--text", text);
            AddEach(args, "--draft-placeholder", _app.ComposerPlaceholderLabels);
            AddOne(args, "--composer-send-label", _app.SendLabel);
            if (target != null) this.AddDraftTarget(args, mode, target);
            else
            {
                AddOne(args, "--mode-prefix", _app.ModePrefix);
                AddOne(args, "--conv-marker", _app.ConversationItemMarker);
            }
            if (acceptExisting) { args.Add("--accept-existing"); }
            AddEach(args, "--stop", _app.StopLabels);
            AddEach(args, "--approve", _app.ApproveLabels);
            AddEach(args, "--voice-end", _app.EndVoiceLabels);
            if (send)
            {
                // Send is confirmed by the helper's exact, container-scoped rule.
                AddOne(args, "--send-label", _app.SendLabel);
            }

            var json = this.Runner(args, 8000);
            if (!TryParseOk(json, out var root))
            {
                error = Describe(json);
                PluginLog.Warning($"WindowsDesktopAutomation.WriteComposer: {error}");
                return false;
            }

            // No text in the log, only the outcome: the device pass needs the record, the
            // draft is the user's.
            PluginLog.Info($"WindowsDesktopAutomation.WriteComposer: {ReadString(root, "method")} write landed"
                + (send ? ", sent" : "") + (target != null ? " (prepared target)" : ""));
            return true;
        }

        private void AddDraftTarget(List<String> args, String mode, String target = null)
        {
            AddOne(args, "--mode-prefix", _app.ModePrefix);
            AddOne(args, "--expect-mode", mode);
            AddOne(args, "--conv-marker", _app.ConversationItemMarker);
            AddOne(args, "--expect-target", target);
        }

        public String PrepareDraft(String mode, Boolean requireEmpty, out String error)
        {
            var args = BaseArgs("draft-target");
            AddOne(args, "--composer-send-label", _app.SendLabel);
            AddEach(args, "--draft-placeholder", _app.ComposerPlaceholderLabels);
            this.AddDraftTarget(args, mode);
            if (!requireEmpty) args.Add("--allow-existing");
            var json = this.Runner(args, 4000);
            error = TryParseOk(json, out var root) ? null : Describe(json);
            return error == null ? ReadString(root, "target") : null;
        }

        public Boolean WritePreparedDraft(String text, String mode, String target, Boolean retry, out String error)
            => this.WriteComposer(text, false, retry, out error, mode, target);

        public Boolean WritePreparedPrompt(String text, String mode, String target, Boolean send, out String error)
            => this.WriteComposer(text, send, false, out error, mode, target);

        public Boolean SupportsAppend => true;

        private List<String> AppendArgs(String verb, String mode)
        {
            var args = BaseArgs(verb);
            AddOne(args, "--composer-send-label", _app.SendLabel);
            this.AddDraftTarget(args, mode);
            AddEach(args, "--draft-placeholder", _app.ComposerPlaceholderLabels);
            AddEach(args, "--stop", _app.StopLabels);
            AddEach(args, "--approve", _app.ApproveLabels);
            AddEach(args, "--voice-end", _app.EndVoiceLabels);
            return args;
        }

        public DesktopAppendTarget PrepareAppend(String mode, out String error)
        {
            var json = this.Runner(this.AppendArgs("append-target", mode), 4000);
            error = TryParseOk(json, out var root) ? null : Describe(json);
            if (error != null) return null;
            var target = ReadString(root, "target");
            var fingerprint = ReadString(root, "fingerprint");
            if (String.IsNullOrEmpty(target) || String.IsNullOrEmpty(fingerprint))
            { error = "composer-target-changed"; return null; }
            return new() { Target = target, Fingerprint = fingerprint,
                HasContent = root.TryGetProperty("hasContent", out var content) && content.ValueKind == JsonValueKind.True };
        }

        public Boolean AppendPreparedDraft(String text, String mode, DesktopAppendTarget target, Boolean retry, out String error)
        {
            if (target == null || String.IsNullOrWhiteSpace(text)) { error = "empty-text"; return false; }
            var args = this.AppendArgs("append", mode);
            args.AddRange(new[] { "--text", text, "--expect-target", target.Target, "--expect-draft", target.Fingerprint });
            if (retry) args.Add("--accept-existing");
            var json = this.Runner(args, 8000);
            error = TryParseOk(json, out _) ? null : Describe(json);
            return error == null;
        }

        public Boolean AttachPreparedImage(String path, String mode, String target, out String error)
        {
            var args = this.AppendArgs("attach-image", mode);
            args.AddRange(new[] { "--image", path, "--expect-target", target });
            var json = this.Runner(args, 6000);
            error = TryParseOk(json, out _) ? null : Describe(json);
            if (error != null && !TryReadHelperError(json)) error = "attachment-unconfirmed";
            return error == null;
        }

        public Boolean AttachPreparedFiles(DesktopFile[] files, String mode, String target, out String error)
        {
            var args = this.AppendArgs("attach-files", mode);
            args.AddRange(new[] { "--expect-target", target, "--files", JsonSerializer.Serialize(files) });
            var json = this.Runner(args, 8000);
            error = TryParseOk(json, out _) ? null : Describe(json);
            if (error != null && !TryReadHelperError(json)) error = "attachment-unconfirmed";
            return error == null;
        }

        private static Boolean TryReadHelperError(String json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json ?? "{}");
                return doc.RootElement.TryGetProperty("error", out var value) && value.ValueKind == JsonValueKind.String
                    && !String.IsNullOrEmpty(value.GetString());
            }
            catch (JsonException) { return false; }
        }

        /// <summary>
        /// Context in and out: copy the latest answer, capture the clipboard, a selection, a
        /// screen region or the window behind the app, return to the source app, paste a reply
        /// there. The same verbs and results as macOS; on Windows the region picker is the
        /// shared toolkit's snip, run by the helper.
        /// </summary>
        public DesktopCaptureResult Context(String action, String source = null, String text = null)
        {
            var args = BaseArgs(action == "copy" ? "copy-reply" : "context-" + action);
            if (action == "copy")
            {
                this.AddReplyLabels(args);
                AddEach(args, "--stop", _app.StopLabels);
                AddEach(args, "--approve", _app.ApproveLabels);
                AddEach(args, "--voice-end", _app.EndVoiceLabels);
                AddEach(args, "--state-running", _app.ConversationRunningTexts);
                AddOne(args, "--state-awaiting", _app.ConversationAwaitingText);
                AddOne(args, "--conv-marker", _app.ConversationItemMarker);
                AddOne(args, "--mode-prefix", _app.ModePrefix);
            }
            if (source != null) args.AddRange(new[] { "--source", source });
            if (text != null) args.AddRange(new[] { "--text", text });
            var json = this.Runner(args, action == "screenshot" ? 130000 : 6000);
            var result = DesktopCaptureResult.Parse(json);
            if (action is "screenshot" or "window")
            {
                // What the picker was shown over is the whole point on Windows; keep the record.
                var record = json == null ? "no output" : json.Length > 400 ? json[..400] : json;
                PluginLog.Info($"WindowsDesktopAutomation.Context({action}): {record}");
            }
            else
            {
                // Outcome only: the copied answer and the clipboard are the user's.
                PluginLog.Info($"WindowsDesktopAutomation.Context({action}): {(result.Ok ? "ok" : result.Error ?? "no output")}"
                    + (result.Ok && !String.IsNullOrEmpty(result.AppName) ? $" (source app: {result.AppName})" : ""));
            }
            return result;
        }

        private void AddReplyLabels(List<String> args)
        {
            AddEach(args, "--copy-response", _app.CopyResponseLabels);
            AddEach(args, "--copy-button", _app.CopyButtonLabels);
            AddEach(args, "--copy-completed", _app.CopyCompletedLabels);
            AddEach(args, "--response-action", _app.ResponseActionLabels);
            AddEach(args, "--assistant-heading", _app.AssistantHeadingLabels);
            AddEach(args, "--user-heading", _app.UserHeadingLabels);
        }

        public Boolean SendComposer(out String error)
            => this.SendPreparedDraft(null, null, null, out error);

        public Boolean SendPreparedDraft(String mode, String target, out String error)
            => this.SendPreparedDraft(mode, target, null, out error);

        public Boolean SendPreparedPrompt(String text, String mode, String target, out String error)
        {
            if (String.IsNullOrWhiteSpace(text) || String.IsNullOrEmpty(target)) { error = "empty-text"; return false; }
            return this.SendPreparedDraft(mode, target, text, out error);
        }

        private Boolean SendPreparedDraft(String mode, String target, String expectedText, out String error)
        {
            var args = BaseArgs("send");
            AddOne(args, "--send-label", _app.SendLabel);
            if (target != null) this.AddDraftTarget(args, mode, target);
            AddOne(args, "--expect-text", expectedText);
            AddEach(args, "--stop", _app.StopLabels);
            AddEach(args, "--approve", _app.ApproveLabels);
            var json = this.Runner(args, 4000);
            error = TryParseOk(json, out _) ? null : Describe(json);
            if (error == null) PluginLog.Info("WindowsDesktopAutomation.Send: the draft was sent");
            return error == null;
        }

        public Boolean SwitchMode(String modeName)
        {
            var menuLabel = _app.ModeMenuLabel(modeName);
            if (String.IsNullOrEmpty(menuLabel) || !this.Press(new[] { _app.ModeSwitcherLabel }, out _))
            {
                return false;
            }

            Thread.Sleep(400);
            return this.Press(new[] { menuLabel }, out _);
        }

        public Boolean FocusApp() =>
            TryParseOk(this.Runner(BaseArgs("focus"), 2000), out _);

        private List<String> BaseArgs(String verb)
        {
            // Plugin actions require a confirmed executable identity. Visible-title matching is
            // available only to the helper's manual inspect workflow; otherwise one browser tab
            // titled "ChatGPT" could be mistaken for the desktop app.
            var args = new List<String> { verb, "--require-process" };
            AddEach(args, "--process", _app.WindowsProcessNames);
            AddEach(args, "--window", _app.WindowsWindowTitles);
            return args;
        }

        private static void AddEach(List<String> args, String flag, String[] values)
        {
            foreach (var value in values ?? Array.Empty<String>())
            {
                AddOne(args, flag, value);
            }
        }

        private void AddContextLabels(List<String> args)
        {
            AddEach(args, "--search", _app.ControlLabels(DesktopControl.Search));
            AddEach(args, "--changes", _app.ControlLabels(DesktopControl.Changes));
            AddEach(args, "--changes-turn", _app.ShowDiffTurnLabels);
            AddEach(args, "--panel-visible", _app.ChangesPanelLabels);
            AddOne(args, "--panel-mode", "Codex");
            AddEach(args, "--projects", _app.ControlLabels(DesktopControl.Projects));
            AddEach(args, "--plugins", _app.ControlLabels(DesktopControl.Plugins));
            AddEach(args, "--attach-files", _app.ControlLabels(DesktopControl.AttachFiles));
            AddEach(args, "--permissions", _app.ControlLabels(DesktopControl.Permissions));
            AddEach(args, "--scheduled", _app.ControlLabels(DesktopControl.Scheduled));
            AddEach(args, "--pull-requests", _app.ControlLabels(DesktopControl.PullRequests));
            AddEach(args, "--explore", _app.ControlLabels(DesktopControl.Explore));
            AddEach(args, "--quick-chat", _app.ControlLabels(DesktopControl.QuickChat));
        }

        private static void AddOne(List<String> args, String flag, String value)
        {
            if (!String.IsNullOrWhiteSpace(value))
            {
                args.Add(flag);
                args.Add(value);
            }
        }

        private static Boolean TryParseOk(String json, out JsonElement root)
        {
            root = default;
            if (String.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                root = doc.RootElement.Clone();
                return root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static String ReadString(JsonElement root, String name) =>
            root.TryGetProperty(name, out var value) ? value.ToString() : "";

        private static String Describe(String json)
        {
            if (String.IsNullOrWhiteSpace(json))
            {
                return "helper produced no output (missing binary, timeout, or spawn failure)";
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    return error.ToString();
                }
            }
            catch (JsonException)
            {
            }

            return json.Length > 120 ? json.Substring(0, 120) : json;
        }
    }
}
