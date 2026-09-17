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

        public Boolean ToggleVoiceChat(out String error)
        {
            if (_voiceShortcut == null) { error = "shortcut-unconfigured"; return false; }
            var args = new List<String> { "shortcut", "--app", _app.BundleId,
                "--key-code", _voiceShortcut.KeyCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--modifiers", _voiceShortcut.Modifiers };
            var json = this.Runner(args, 2500);
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
                AddEach(args, "--state-unread", new[] { _app.ConversationUnreadText });
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
            args.AddRange(new[] { "--send-label", _app.SendLabel });

            // 2.5s budget: the walk measured ~130ms end-to-end; the margin covers a cold
            // Chromium tree, not a hung one — BoundedProcess kills anything slower.
            return DesktopSnapshot.Parse(this.Runner(args, 2500));
        }

        public Boolean Press(String[] labels, out String matched) =>
            this.PressGuarded(labels, expectCard: null, out matched, out _);

        public Boolean PressExact(String[] labels)
        {
            if (labels?.Length is not > 0) { return false; }
            var args = new List<String> { "press-exact", "--app", _app.BundleId };
            AddEach(args, "--label", labels);
            return TryParseOk(this.Runner(args, 4000), out _);
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
            var json = this.Runner(args, 4000);
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

            var json = this.Runner(args, 4000);
            if (!TryParseOk(json, out var root))
            {
                error = Describe(json);
                PluginLog.Warning($"MacDesktopAutomation.Press({String.Join("|", labels)}): {error}");
                return false;
            }

            matched = ReadString(root, "matched");
            PluginLog.Info($"MacDesktopAutomation.Press: “{matched}” (frontmost stayed {ReadString(root, "frontAfter")})");
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
            if (!TryParseOk(this.Runner(args, 4000), out var result)) { return false; }
            matched = ReadString(result, "matched");
            return true;
        }

        public Boolean PressConversation(String title)
        {
            if (String.IsNullOrWhiteSpace(title) || String.IsNullOrEmpty(_app.ConversationItemMarker))
            {
                return false;
            }
            var args = new List<String> { "press", "--app", _app.BundleId };
            args.AddRange(new[] { "--label", title, "--conversation", _app.ConversationItemMarker });
            return TryParseOk(this.Runner(args, 4000), out _);
        }

        public Boolean WriteComposer(String text, Boolean send, out String error)
        {
            error = null;
            if (String.IsNullOrWhiteSpace(text))
            {
                error = "empty text";
                return false;
            }

            var args = new List<String> { "write", "--app", _app.BundleId, "--text", text };
            AddEach(args, "--stop", _app.StopLabels);
            AddEach(args, "--approve", _app.ApproveLabels);
            if (send)
            {
                args.Add("--send-label");
                args.Add(_app.SendLabel);
            }

            // Write does settle-and-verify passes inside the helper; give it real room.
            var json = this.Runner(args, 8000);
            if (!TryParseOk(json, out var root))
            {
                error = Describe(json);
                PluginLog.Warning($"MacDesktopAutomation.WriteComposer: {error}");
                return false;
            }

            PluginLog.Info($"MacDesktopAutomation.WriteComposer: {text.Length} chars via {ReadString(root, "method")}, sent={ReadString(root, "sent")}");
            return true;
        }

        public Boolean SendComposer(out String error)
        {
            var args = new List<String> { "send", "--app", _app.BundleId, "--send-label", _app.SendLabel };
            AddEach(args, "--stop", _app.StopLabels);
            AddEach(args, "--approve", _app.ApproveLabels);
            var json = this.Runner(args, 4000);
            error = TryParseOk(json, out _) ? null : Describe(json);
            return error == null;
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
            return this.Press(new[] { menuLabel }, out _);
        }

        public Boolean FocusApp()
        {
            var json = this.Runner(new List<String> { "focus", "--app", _app.BundleId }, 2000);
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
                return root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
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
