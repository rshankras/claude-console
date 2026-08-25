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

        /// <summary>
        /// The process seam, settable so tests exercise argument construction and JSON handling
        /// with no helper binary and no AX (the OsascriptRunner pattern from MacPlatformBridge).
        /// Returns trimmed stdout, or null on spawn failure / timeout.
        /// </summary>
        internal Func<List<String>, Int32, String> Runner { get; set; } =
            (args, timeoutMs) => BoundedProcess.Run(HelperPath, args, timeoutMs, wantOutput: true);

        public MacDesktopAutomation(IDesktopAppAdapter app) => _app = app;

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

            // 2.5s budget: the walk measured ~130ms end-to-end; the margin covers a cold
            // Chromium tree, not a hung one — BoundedProcess kills anything slower.
            return DesktopSnapshot.Parse(this.Runner(args, 2500));
        }

        public Boolean Press(String[] labels, out String matched)
        {
            matched = null;
            if (labels == null || labels.Length == 0)
            {
                return false;
            }

            var args = new List<String> { "press", "--app", _app.BundleId };
            AddEach(args, "--label", labels);

            var json = this.Runner(args, 4000);
            if (!TryParseOk(json, out var root))
            {
                PluginLog.Warning($"MacDesktopAutomation.Press({String.Join("|", labels)}): {Describe(json)}");
                return false;
            }

            matched = ReadString(root, "matched");
            PluginLog.Info($"MacDesktopAutomation.Press: “{matched}” (frontmost stayed {ReadString(root, "frontAfter")})");
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

            var args = new List<String> { "write", "--app", _app.BundleId, "--text", text };
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
