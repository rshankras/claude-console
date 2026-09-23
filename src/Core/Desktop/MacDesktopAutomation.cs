namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// macOS desktop automation: every verb is one short-lived VizhiAxBridge invocation through
    /// <see cref="BoundedProcess"/> (async pipe drain, kill-on-timeout — the only sanctioned
    /// child-process shape in this codebase). The helper is app-agnostic; this class feeds it
    /// the adapter's labels, so all app knowledge stays in one place.
    ///
    /// The helper lives in the SHARED runtime home (~/.claude/claude-console), beside the voice
    /// helper and for the same reason: one binary serves every product that ships it. It is a
    /// plain signed binary spawned as a DIRECT child — its AX calls attribute to
    /// LogiPluginService, which already holds the Accessibility grant the terminal products
    /// required. (The voice helper's `open` launch exists to give it its OWN TCC identity for
    /// the mic; here we want exactly the opposite.)
    /// </summary>
    internal sealed class MacDesktopAutomation : IDesktopAutomation
    {
        // Same home as BridgeManager.ClaudeConsoleHome — shared across products on purpose.
        internal static readonly String HelperPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "claude-console", "VizhiAxBridge");

        private readonly IDesktopAppAdapter _app;
        private readonly DesktopVoiceShortcut _voiceShortcut;

        /// <summary>
        /// The process seam, settable so tests exercise argument construction and JSON handling
        /// with no helper binary and no AX (the OsascriptRunner pattern from MacPlatformBridge).
        /// Returns trimmed stdout, or null on spawn failure / timeout.
        /// </summary>
        internal Func<List<String>, Int32, String> Runner { get; set; } =
            (args, timeoutMs) => BoundedProcess.Run(HelperPath, args, timeoutMs, wantOutput: true);

        public MacDesktopAutomation(IDesktopAppAdapter app, DesktopVoiceShortcut voiceShortcut = null)
        {
            _app = app;
            _voiceShortcut = voiceShortcut;
        }

        public Boolean HasVoiceShortcut => _voiceShortcut != null;
        internal Func<Boolean> IsEnabled { get; set; } = () => true;
        private String Invoke(List<String> args, Int32 timeoutMs) => IsEnabled() ? Runner(args, timeoutMs) : null;

        public Boolean? IsAppFrontmost()
        {
            var json = Invoke(new() { "frontmost", "--app", _app.BundleId }, 1000);
            return TryParseOk(json, out var root) && root.TryGetProperty("frontmost", out var front)
                && front.ValueKind is JsonValueKind.True or JsonValueKind.False ? front.GetBoolean() : null;
        }

        public DesktopSearchSnapshot Search(String action, String target = null, String query = null, String value = null, String title = null, String origin = null)
        {
            var args = new List<String> { "search", "--app", _app.BundleId, "--action", action,
                "--mode-prefix", _app.ModePrefix, "--expect-mode", "ChatGPT" };
            AddEach(args, "--search", _app.ControlLabels(DesktopControl.Search));
            AddEach(args, "--search-field", _app.SearchFieldLabels);
            AddEach(args, "--result-host", _app.SearchResultHosts);
            AddEach(args, "--result-path", _app.SearchResultPaths);
            if (target != null) args.AddRange(new[] { "--target", target });
            if (query != null) args.AddRange(new[] { "--query", query });
            if (value != null) args.AddRange(new[] { "--value", value });
            if (title != null) args.AddRange(new[] { "--title", title });
            if (origin != null) args.AddRange(new[] { "--origin", origin });
            return DesktopSearchSnapshot.Parse(this.Invoke(args, 5000));
        }

        public Boolean ToggleVoiceChat(out String error)
        {
            if (_voiceShortcut == null) { error = "shortcut-unconfigured"; return false; }
            var args = new List<String> { "shortcut", "--app", _app.BundleId,
                "--key-code", _voiceShortcut.KeyCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--modifiers", _voiceShortcut.Modifiers };
            var json = this.Invoke(args, 2500);
            error = TryParseOk(json, out _) ? null : Describe(json);
            return error == null;
        }

        public DesktopSnapshot Status()
        {
            var args = new List<String> { "status", "--app", _app.BundleId };
            AddEach(args, "--approve", _app.ApproveLabels);
            AddEach(args, "--deny", _app.DenyLabels);
            AddEach(args, "--stop", _app.StopLabels);
            if (!String.IsNullOrEmpty(_app.AttentionMarker))
            {
                args.Add("--attention");
                args.Add(_app.AttentionMarker);
            }
            if (!String.IsNullOrEmpty(_app.ModePrefix))
            {
                args.Add("--mode-prefix");
                args.Add(_app.ModePrefix);
            }
            if (!String.IsNullOrEmpty(_app.ConversationItemMarker))
            {
                args.Add("--conv-marker");
                args.Add(_app.ConversationItemMarker);
                AddEach(args, "--state-awaiting", new[] { _app.ConversationAwaitingText });
                AddEach(args, "--state-unread", _app.ConversationUnreadTexts);
                AddEach(args, "--state-running", _app.ConversationRunningTexts);
            }
            foreach (var mode in _app.ModeNames)
            {
                if (_app.ConversationIdleImages(mode) is Int32 count)
                {
                    args.AddRange(new[] { "--idle-images", $"{mode}={count}" });
                }
            }
            AddContextLabels(args);
            AddVoiceLabels(args);
            AddReplyLabels(args);
            args.AddRange(new[] { "--send-label", _app.SendLabel });

            // 2.5s budget: the walk measured ~130ms end-to-end; the margin covers a cold
            // Chromium tree, not a hung one — BoundedProcess kills anything slower.
            return DesktopSnapshot.Parse(this.Invoke(args, 2500));
        }

        public Boolean Press(String[] labels, out String matched) =>
            this.PressGuarded(labels, expectCard: null, out matched, out _);

        public Boolean PressExact(String[] labels)
        {
            if (labels?.Length is not > 0) { return false; }
            var args = new List<String> { "press-exact", "--app", _app.BundleId };
            AddEach(args, "--label", labels);
            return TryParseOk(this.Invoke(args, 4000), out _);
        }

        public Boolean SetVoiceChat(Boolean active, out String error)
        {
            if (_app.StartVoiceLabels.Length == 0 || _app.EndVoiceLabels.Length == 0)
            {
                error = "unsupported";
                return false;
            }
            var args = new List<String> { "voice", "--app", _app.BundleId,
                "--action", active ? "start" : "end" };
            AddVoiceLabels(args);
            var json = this.Invoke(args, 4000);
            error = TryParseOk(json, out _) ? null : Describe(json);
            return error == null;
        }

        public Boolean PressGuarded(String[] labels, String expectCard, out String matched, out String error)
        {
            matched = null;
            error = null;
            if (labels == null || labels.Length == 0)
            {
                return false;
            }

            var args = new List<String> { "press", "--app", _app.BundleId };
            AddEach(args, "--label", labels);
            if (!String.IsNullOrWhiteSpace(expectCard))
            {
                args.Add("--expect-near");
                args.Add(expectCard);
            }

            var json = this.Invoke(args, 4000);
            if (!TryParseOk(json, out var root))
            {
                error = Describe(json);
                PluginLog.Warning($"MacDesktopAutomation.Press({String.Join("|", labels)}): {error}");
                return false;
            }

            matched = ReadString(root, "matched");
            return true;
        }

        public Boolean PressInMode(String[] labels, String mode, out String matched)
        {
            matched = null;
            if (String.IsNullOrEmpty(mode) || String.IsNullOrEmpty(_app.ModePrefix) || labels?.Length is not > 0)
            {
                return false;
            }
            var args = new List<String> { "press", "--app", _app.BundleId,
                "--mode-prefix", _app.ModePrefix, "--expect-mode", mode };
            AddEach(args, "--label", labels);
            if (!TryParseOk(this.Invoke(args, 4000), out var result)) { return false; }
            matched = ReadString(result, "matched");
            return true;
        }

        internal Action<String> ChangesTrace { get; set; }
        private void TraceChanges(String code)
        { try { ChangesTrace?.Invoke(DesktopChangesTrace.SafeCode(code)); } catch { } }

        public Boolean OpenChanges(out String error)
        {
            TraceChanges("requested");
            var args = new List<String> { "open-panel", "--app", _app.BundleId,
                "--mode-prefix", _app.ModePrefix, "--expect-mode", "Codex" };
            AddEach(args, "--panel-open", _app.ShowDiffLabels);
            AddEach(args, "--panel-visible", _app.ChangesPanelLabels);
            AddEach(args, "--conv-marker", new[] { _app.ConversationItemMarker });
            var json = this.Invoke(args, 5000);
            if (!TryParseOk(json, out var result) || !result.TryGetProperty("opened", out var opened) || opened.ValueKind != JsonValueKind.True)
            {
                error = TryParseOk(json, out _) ? "panel-unconfirmed" : DesktopChangesTrace.ErrorFrom(json);
                TraceChanges(error); return false;
            }
            TraceChanges(result.TryGetProperty("alreadyOpen", out var already) && already.ValueKind == JsonValueKind.True ? "already-open" : "opened");
            error = null; return true;
        }

        public Boolean PressConversation(String title)
        {
            if (String.IsNullOrWhiteSpace(title) || String.IsNullOrEmpty(_app.ConversationItemMarker))
            {
                return false;
            }
            var args = new List<String> { "press", "--app", _app.BundleId };
            args.AddRange(new[] { "--label", title, "--conversation", _app.ConversationItemMarker });
            return TryParseOk(this.Invoke(args, 4000), out _);
        }

        public Boolean WriteComposer(String text, Boolean send, out String error) =>
            WriteComposer(text, send, false, out error);

        public Boolean RecoverDraft(String text, out String error) =>
            WriteComposer(text, false, true, out error);

        private Boolean WriteComposer(String text, Boolean send, Boolean acceptExisting, out String error, String mode = null, String target = null)
        {
            error = null;
            if (String.IsNullOrWhiteSpace(text))
            {
                error = "empty text";
                return false;
            }

            var args = new List<String> { "write", "--app", _app.BundleId, "--text", text };
            AddEach(args, "--draft-placeholder", _app.ComposerPlaceholderLabels);
            args.Add("--composer-send-label");
            args.Add(_app.SendLabel);
            if (target != null) AddDraftTarget(args, mode, target);
            else
            {
                args.AddRange(new[] { "--mode-prefix", _app.ModePrefix });
                if (!String.IsNullOrEmpty(_app.ConversationItemMarker))
                    args.AddRange(new[] { "--conv-marker", _app.ConversationItemMarker });
            }
            if (acceptExisting) { args.Add("--accept-existing"); }
            AddEach(args, "--stop", _app.StopLabels);
            AddEach(args, "--approve", _app.ApproveLabels);
            AddEach(args, "--voice-end", _app.EndVoiceLabels);
            if (send)
            {
                args.Add("--send-label");
                args.Add(_app.SendLabel);
            }

            // Write does settle-and-verify passes inside the helper; give it real room.
            var json = this.Invoke(args, 8000);
            if (!TryParseOk(json, out var root))
            {
                error = Describe(json);
                PluginLog.Warning($"MacDesktopAutomation.WriteComposer: {error}");
                return false;
            }

            return true;
        }

        public Boolean SendComposer(out String error)
            => SendPreparedDraft(null, null, out error);

        private void AddDraftTarget(List<String> args, String mode, String target = null)
        {
            args.AddRange(new[] { "--mode-prefix", _app.ModePrefix, "--expect-mode", mode });
            if (!String.IsNullOrEmpty(_app.ConversationItemMarker)) args.AddRange(new[] { "--conv-marker", _app.ConversationItemMarker });
            if (target != null) args.AddRange(new[] { "--expect-target", target });
        }

        public String PrepareDraft(String mode, Boolean requireEmpty, out String error)
        {
            var args = new List<String> { "draft-target", "--app", _app.BundleId,
                "--composer-send-label", _app.SendLabel };
            AddEach(args, "--draft-placeholder", _app.ComposerPlaceholderLabels);
            AddDraftTarget(args, mode);
            if (!requireEmpty) args.Add("--allow-existing");
            var json = this.Invoke(args, 4000);
            error = TryParseOk(json, out var root) ? null : Describe(json);
            return error == null ? ReadString(root, "target") : null;
        }

        public Boolean WritePreparedDraft(String text, String mode, String target, Boolean retry, out String error)
            => WriteComposer(text, false, retry, out error, mode, target);

        public Boolean SupportsAppend => true;

        public DesktopAppendTarget PrepareAppend(String mode, out String error)
        {
            var args = AppendArgs("append-target", mode);
            var json = this.Invoke(args, 4000);
            error = TryParseOk(json, out var root) ? null : Describe(json);
            if (error != null) return null;
            var target = ReadString(root, "target");
            var fingerprint = ReadString(root, "fingerprint");
            if (String.IsNullOrEmpty(target) || String.IsNullOrEmpty(fingerprint))
            { error = "composer-target-changed"; return null; }
            return new() { Target = target, Fingerprint = fingerprint,
                HasContent = root.TryGetProperty("hasContent", out var content) && content.ValueKind == JsonValueKind.True };
        }

        public Boolean WritePreparedPrompt(String text, String mode, String target, Boolean send, out String error)
            => WriteComposer(text, send, false, out error, mode, target);

        public Boolean AppendPreparedDraft(String text, String mode, DesktopAppendTarget target, Boolean retry, out String error)
        {
            if (target == null || String.IsNullOrWhiteSpace(text)) { error = "empty-text"; return false; }
            var args = AppendArgs("append", mode);
            args.AddRange(new[] { "--text", text, "--expect-target", target.Target, "--expect-draft", target.Fingerprint });
            if (retry) args.Add("--accept-existing");
            var json = this.Invoke(args, 8000);
            error = TryParseOk(json, out _) ? null : Describe(json);
            return error == null;
        }

        private List<String> AppendArgs(String verb, String mode)
        {
            var args = new List<String> { verb, "--app", _app.BundleId, "--composer-send-label", _app.SendLabel };
            AddDraftTarget(args, mode);
            AddEach(args, "--draft-placeholder", _app.ComposerPlaceholderLabels);
            AddEach(args, "--stop", _app.StopLabels);
            AddEach(args, "--approve", _app.ApproveLabels);
            AddEach(args, "--voice-end", _app.EndVoiceLabels);
            return args;
        }

        public Boolean SendPreparedDraft(String mode, String target, out String error)
            => SendPreparedDraft(mode, target, null, out error);

        public Boolean SendPreparedPrompt(String text, String mode, String target, out String error)
        {
            if (String.IsNullOrWhiteSpace(text) || String.IsNullOrEmpty(target)) { error = "empty-text"; return false; }
            return SendPreparedDraft(mode, target, text, out error);
        }

        private Boolean SendPreparedDraft(String mode, String target, String expectedText, out String error)
        {
            var args = new List<String> { "send", "--app", _app.BundleId, "--send-label", _app.SendLabel };
            if (target != null) AddDraftTarget(args, mode, target);
            if (expectedText != null) args.AddRange(new[] { "--expect-text", expectedText });
            AddEach(args, "--stop", _app.StopLabels);
            AddEach(args, "--approve", _app.ApproveLabels);
            var json = this.Invoke(args, 4000);
            error = TryParseOk(json, out _) ? null : Describe(json);
            return error == null;
        }

        public DesktopCaptureResult Context(String action, String source = null, String text = null)
        {
            var args = new List<String> { action == "copy" ? "copy-reply" : "context-" + action, "--app", action == "copy" ? _app.BundleId : "@frontmost", "--chat-app", _app.BundleId };
            if (action == "copy")
            {
                AddReplyLabels(args);
                AddEach(args, "--stop", _app.StopLabels);
                AddEach(args, "--approve", _app.ApproveLabels);
                AddEach(args, "--voice-end", _app.EndVoiceLabels);
                AddEach(args, "--state-running", _app.ConversationRunningTexts);
                AddEach(args, "--state-awaiting", new[] { _app.ConversationAwaitingText });
                args.AddRange(new[] { "--conv-marker", _app.ConversationItemMarker });
            }
            if (source != null) args.AddRange(new[] { "--source", source });
            if (text != null) args.AddRange(new[] { "--text", text });
            return DesktopCaptureResult.Parse(this.Invoke(args, action == "screenshot" ? 120000 : 6000));
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

        public Boolean AttachPreparedImage(String path, String mode, String target, out String error)
        {
            var args = new List<String> { "attach-image", "--app", _app.BundleId, "--image", path, "--composer-send-label", _app.SendLabel };
            AddEach(args, "--draft-placeholder", _app.ComposerPlaceholderLabels);
            AddDraftTarget(args, mode, target);
            AddEach(args, "--stop", _app.StopLabels);
            AddEach(args, "--approve", _app.ApproveLabels);
            AddEach(args, "--voice-end", _app.EndVoiceLabels);
            var json = this.Invoke(args, 6000);
            error = TryParseOk(json, out _) ? null : Describe(json);
            if (error != null && !TryReadHelperError(json, out _)) error = "attachment-unconfirmed";
            return error == null;
        }

        public Boolean AttachPreparedFiles(DesktopFile[] files, String mode, String target, out String error)
        {
            var args = AppendArgs("attach-files", mode);
            args.AddRange(new[] { "--expect-target", target, "--files", JsonSerializer.Serialize(files) });
            var json = this.Invoke(args, 8000);
            error = TryParseOk(json, out _) ? null : Describe(json);
            if (error != null && !TryReadHelperError(json, out _)) error = "attachment-unconfirmed";
            return error == null;
        }

        private static Boolean TryReadHelperError(String json, out String error)
        {
            error = null;
            try
            {
                using var doc = JsonDocument.Parse(json ?? "{}");
                if (doc.RootElement.TryGetProperty("error", out var value) && value.ValueKind == JsonValueKind.String)
                    error = value.GetString();
            }
            catch (JsonException) { }
            return !String.IsNullOrEmpty(error);
        }

        public Boolean SwitchMode(String modeName)
        {
            var menuLabel = _app.ModeMenuLabel(modeName);
            if (String.IsNullOrEmpty(menuLabel))
            {
                return false;
            }

            if (!this.Press(new[] { _app.ModeSwitcherLabel }, out _))
            {
                return false;
            }

            // The menu renders asynchronously after the switcher press (observed live: present
            // on the next scan). One settle beats a retry loop on a two-step gesture.
            Thread.Sleep(400);
            if (!this.Press(new[] { menuLabel }, out _)) return false;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                Thread.Sleep(150);
                var state = this.Status();
                if (state.SurfaceAvailable && state.Mode == modeName) return true;
            }
            return false;
        }

        public Boolean FocusApp()
        {
            var json = this.Invoke(new List<String> { "focus", "--app", _app.BundleId }, 2000);
            return TryParseOk(json, out _);
        }

        // ------------------------------------------------------------------------------------------

        private static void AddEach(List<String> args, String flag, String[] values)
        {
            foreach (var v in values ?? Array.Empty<String>())
            {
                if (!String.IsNullOrEmpty(v))
                {
                    args.Add(flag);
                    args.Add(v);
                }
            }
        }

        private void AddContextLabels(List<String> args)
        {
            AddEach(args, "--search", _app.ControlLabels(DesktopControl.Search));
            AddEach(args, "--changes", _app.ControlLabels(DesktopControl.Changes));
            AddEach(args, "--panel-visible", _app.ChangesPanelLabels);
            args.AddRange(new[] { "--panel-mode", "Codex" });
            AddEach(args, "--projects", _app.ControlLabels(DesktopControl.Projects));
            AddEach(args, "--plugins", _app.ControlLabels(DesktopControl.Plugins));
            AddEach(args, "--attach-files", _app.ControlLabels(DesktopControl.AttachFiles));
            AddEach(args, "--permissions", _app.ControlLabels(DesktopControl.Permissions));
            AddEach(args, "--scheduled", _app.ControlLabels(DesktopControl.Scheduled));
            AddEach(args, "--pull-requests", _app.ControlLabels(DesktopControl.PullRequests));
            AddEach(args, "--explore", _app.ControlLabels(DesktopControl.Explore));
            AddEach(args, "--quick-chat", _app.ControlLabels(DesktopControl.QuickChat));
        }

        private void AddVoiceLabels(List<String> args)
        {
            AddEach(args, "--voice-start", _app.StartVoiceLabels);
            AddEach(args, "--voice-end", _app.EndVoiceLabels);
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
                return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static String ReadString(JsonElement root, String name) =>
            root.TryGetProperty(name, out var v) ? v.ToString() : "";

        // For log lines: surface the helper's error field when there is one, else the raw tail.
        private static String Describe(String json)
        {
            if (String.IsNullOrWhiteSpace(json))
            {
                return "helper produced no output (timeout, missing binary, or spawn failure)";
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    return err.ToString();
                }
            }
            catch (JsonException)
            {
            }

            return json.Length > 120 ? json.Substring(0, 120) : json;
        }
    }
}
