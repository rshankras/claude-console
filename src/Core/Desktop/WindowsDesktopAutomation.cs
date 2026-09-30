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

        internal Func<List<String>, Int32, String> Runner { get; set; } =
            (args, timeoutMs) =>
            {
                var helper = PluginPaths.PackagedFile(HelperFileName);
                return helper == null ? null : BoundedProcess.Run(helper, args, timeoutMs, wantOutput: true);
            };

        internal static Boolean IsPackaged => PluginPaths.PackagedFile(HelperFileName) != null;

        public WindowsDesktopAutomation(IDesktopAppAdapter app) => _app = app;

        public Boolean? IsAppFrontmost()
        {
            // A process check only: passive polling backs off in other apps without asking
            // Chromium to build or traverse its accessibility tree.
            var json = this.Runner(BaseArgs("frontmost"), 1000);
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
            this.KeepFront(root);
            return true;
        }

        /// <summary>
        /// A press is a click to Chromium, and a click activates the window — so on Windows an
        /// Approve pressed from the editor would leave the user in the chat app. The helper
        /// reports the move; a second, UIA-free invocation hands the foreground back (the
        /// process that made the UIA call is refused; measured live 2026-09-30). The press has
        /// already landed, so a refused restore is logged, never a failure.
        /// </summary>
        private void KeepFront(JsonElement press)
        {
            if (!press.TryGetProperty("frontMoved", out var moved) || moved.ValueKind != JsonValueKind.True
                || !press.TryGetProperty("frontBeforeHwnd", out var hwnd) || hwnd.ValueKind != JsonValueKind.Number)
            {
                return;
            }
            var args = new List<String> { "restore-front", "--hwnd", hwnd.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) };
            AddEach(args, "--process", _app.WindowsProcessNames);
            var json = this.Runner(args, 1500);
            if (!TryParseOk(json, out _))
            {
                PluginLog.Warning($"WindowsDesktopAutomation: the press activated the app and focus could not be handed back: {Describe(json)}");
            }
        }

        public Boolean PressConversation(String title)
        {
            if (String.IsNullOrWhiteSpace(title) || String.IsNullOrEmpty(_app.ConversationItemMarker))
            {
                return false;
            }
            // No focus hand-back here: the conversation key shows the chat it opened, so its
            // caller brings the app forward on purpose right after this.
            var args = BaseArgs("press");
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

            var args = BaseArgs("write");
            AddOne(args, "--text", text);
            if (send)
            {
                // Send is confirmed by the helper's exact, container-scoped rule, which also
                // refuses while a task runs or an approval waits.
                AddOne(args, "--send-label", _app.SendLabel);
                AddEach(args, "--stop", _app.StopLabels);
                AddEach(args, "--approve", _app.ApproveLabels);
            }

            var json = this.Runner(args, 8000);
            if (!TryParseOk(json, out _))
            {
                error = Describe(json);
                PluginLog.Warning($"WindowsDesktopAutomation.WriteComposer: {error}");
                return false;
            }

            return true;
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
