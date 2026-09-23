namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Diagnostics;
    using System.Threading;

    using Loupedeck.ClaudeConsolePlugin.Agents;

    /// <summary>
    /// Claude Console — Logitech MX Creative Keypad plugin for Claude Code.
    /// Physical LCD-key controls for AI-assisted coding, bridged to Claude Code over file IPC.
    ///
    /// Commands and adjustments are AUTO-DISCOVERED by the SDK — every PluginDynamicCommand /
    /// PluginDynamicAdjustment subclass with a parameterless constructor is registered
    /// automatically. They reach the shared IPC bridge via BridgeManager.Instance, so Load()
    /// only has to start the bridge polling Claude Code's state.
    ///
    /// v1 "Core essentials" actions:
    ///   Live displays: Model, Cost, Activity (read state.json — no terminal needed)
    ///   Controls:      Plan, Compact, Context, Voice
    ///   Prompts:       Fix Bug, Write Tests
    ///   Git:           Commit, Diff
    /// </summary>
    public class ClaudeConsolePlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;
        public override Boolean HasNoApplication => true;

        public ClaudeConsolePlugin()
        {
            PluginLog.Init(this.Log, "Claude Console");
            PluginResources.Init(this.Assembly);

            // Declared HERE, not in Load(): the SDK constructs every action in between, and an
            // action reads the agent to decide which keys to add and the product to resolve its
            // IPC paths. Declaring late would build the keys against no agent at all.
            var agent = new ClaudeCodeAdapter();
            IpcPaths.UseProduct(agent.ProductSlug);
            BridgeManager.Instance.Agent = agent;

            // The engine composes what the user should be told about an edit to their settings; only
            // this class can put it in front of them (Options+'s message centre, with a link) — #31.
            BridgeManager.Instance.Notify = (status, message, url, title) =>
            {
                try
                {
                    if (message == null)
                    {
                        this.OnPluginStatusChanged(status, String.Empty);
                    }
                    else
                    {
                        this.OnPluginStatusChanged(status, message, url, title);
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, "ClaudeConsolePlugin: could not post the plugin status");
                }
            };

            // The system-notification half of the same seam (#31): a press on the keypad deserves a
            // reply where the user is looking, not only inside Options+. The SDK's ShowBalloonTip
            // shows NOTHING on macOS (device, 2026-08-29 13:52), so there it goes through
            // Notification Center via osascript; the SDK call is kept for Windows, where the name
            // is native. Fire-and-forget: a notification must never hold up a key press.
            BridgeManager.Instance.Toast = (title, text) =>
            {
                try
                {
                    if (OperatingSystem.IsMacOS())
                    {
                        ShowMacNotification(title, text);
                    }
                    else
                    {
                        this.NativeGui.ShowBalloonTip(text, title, BalloonTipIcon.Info);
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, "ClaudeConsolePlugin: could not show the notification");
                }
            };

            // The yes/no half (#31): a dialog centred on screen with the change spelled out and a
            // way to say no — the prompt QA asked for. macOS only; osascript through System Events
            // brings it to the front from a background service (verified 2026-08-29). Windows has
            // no dialog here yet, so it keeps the two-step press.
            BridgeManager.Instance.Prompt = OperatingSystem.IsMacOS() ? AskMacDialog : null;
        }

        // AppleScript string literal: backslashes and double quotes escaped.
        private static String AppleScriptString(String s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static void ShowMacNotification(String title, String text)
        {
            var script = $"display notification {AppleScriptString(text)} with title {AppleScriptString(title)}";
            var psi = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);
            using var p = Process.Start(psi);   // not awaited on purpose
        }

        // true = the yes button, false = the no button (the cancel button: osascript exits non-zero),
        // null = gave up after the timeout, or cancelled by a key press (the dialog is killed).
        private static Boolean? AskMacDialog(String title, String text, String yes, String no, Int32 timeoutSeconds, CancellationToken cancel)
        {
            var script =
                "tell application \"System Events\" to display dialog " + AppleScriptString(text) +
                " with title " + AppleScriptString(title) +
                " buttons {" + AppleScriptString(no) + ", " + AppleScriptString(yes) + "}" +
                " default button " + AppleScriptString(yes) + " cancel button " + AppleScriptString(no) +
                $" giving up after {timeoutSeconds}";
            var psi = new ProcessStartInfo("/usr/bin/osascript")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);

            using var p = Process.Start(psi);
            using var closeOnCancel = cancel.Register(() => { try { p.Kill(); } catch { /* already gone */ } });
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            if (cancel.IsCancellationRequested)
            {
                return null;
            }
            if (p.ExitCode != 0)
            {
                return false;   // the no button is the cancel button: osascript exits with -128
            }
            if (output.Contains("gave up:true", StringComparison.Ordinal))
            {
                return null;
            }
            return output.Contains("button returned:" + yes, StringComparison.Ordinal);
        }

        private static ClaudePluginLifecycle Lifecycle => new ClaudePluginLifecycle(
            BridgeManager.HomeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        public override Boolean Uninstall() =>
            !OperatingSystem.IsWindows() || Lifecycle.Uninstall();

        public override Boolean Install()
        {
            if (OperatingSystem.IsWindows()) { Lifecycle.Restore(this.AssemblyFilePath); }
            // The host removes the NEW package if Install returns false. A temporarily locked
            // settings file should defer integration restoration, not destroy a valid install.
            // The receipt and log survive; Load retries the same idempotent transaction.
            return true;
        }

        public override void Load()
        {
            // Hand the SDK's real on-disk plugin path to the bridge (Assembly.Location is empty in
            // the SDK's load context) so it can locate the in-package voice payload on first use.
            BridgeManager.Instance.PluginAssemblyFilePath = this.AssemblyFilePath;

            if (OperatingSystem.IsWindows()) { Lifecycle.Restore(this.AssemblyFilePath); }

            // All actions are auto-discovered; we just start the IPC bridge.
            BridgeManager.Instance.StartPolling();

            // Self-install the status-line + activity scripts (the plugin's own folder), honour an Off
            // marker, and read what settings.json says about the live keys. This adds no new wiring;
            // it may migrate existing owned commands. Background thread, idempotent.
            BridgeManager.Instance.EnsureBridgeAutoWired();

            // No application registration to write, heal, or sweep: this is a universal plugin
            // (HasNoApplication in the package yaml), decided with Logitech on 2026-08-28 (#23).
            // The keypad layout is a profile the user imports or builds, on Options+'s own entry
            // for Terminal — not something the package carries. Everything that used to happen here
            // (SelfRegistration, RegistrationHeal, RegistrationCleanup, and the service restarts they
            // scheduled) existed only to manage an entry this plugin no longer has.

            PluginLog.Info("ClaudeConsolePlugin: Loaded — actions auto-discovered; bridge polling started");
        }

        public override void Unload()
        {
            BridgeManager.Instance.StopPolling();
            PluginLog.Info("ClaudeConsolePlugin: Unloaded");
        }
    }
}
