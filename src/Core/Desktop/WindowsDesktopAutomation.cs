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

        public DesktopSnapshot Status()
        {
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
                AddOne(args, "--state-unread", _app.ConversationUnreadText);
            }
            AddContextLabels(args);

            return DesktopSnapshot.Parse(this.Runner(args, 3000));
        }

        public Boolean Press(String[] labels, out String matched) =>
            this.PressGuarded(labels, null, out matched, out _);

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
            PluginLog.Info($"WindowsDesktopAutomation.Press: “{matched}” (frontmost stayed {ReadString(root, "frontAfter")})");
            return true;
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
                AddOne(args, "--send-label", _app.SendLabel);
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
