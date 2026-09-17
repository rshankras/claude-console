namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// The desktop app's control keys: Stop (interrupt the running task), New chat, Mode
    /// (ChatGPT ⇄ Codex — the switch drives the app's own mode menu from outside, two AX
    /// presses), and Focus (the ONE key whose whole job is to bring the app forward; every
    /// other key's virtue is that it doesn't).
    ///
    /// Keys gate on the adapter's capabilities at construction — an app with no mode switcher
    /// never grows a Mode key. Capability false ⇒ the parameter is never added, the honest-hide
    /// rule inherited from the terminal products.
    /// </summary>
    public class DesktopControlCommand : PluginDynamicCommand
    {
        private const String Stop = "stop";
        private const String NewChat = "new_chat";
        private const String Mode = "mode";
        private const String Focus = "focus";
        private const String ShowDiff = "show_diff";

        public DesktopControlCommand()
            : base()
        {
            if (!DesktopServices.Declared)
            {
                // A product without a desktop surface compiled this by mistake; add nothing.
                return;
            }

            var caps = DesktopServices.App.Capabilities;

            if (caps.Stop)
            {
                this.AddParameter(Stop, "Stop", "Agent")
                    .SetDescription("Interrupt the running task");
            }

            this.AddParameter(NewChat, "New Chat", "Agent")
                .SetDescription("Start a fresh conversation");

            if (DesktopServices.App.ShowDiffLabels.Length > 0)
            {
                this.AddParameter(ShowDiff, "Show Diff", "Agent")
                    .SetDescription("Open the current task's changes/review view");
            }

            if (caps.ModeSwitch)
            {
                this.AddParameter(Mode, "Mode", "Agent")
                    .SetDescription("Switch between the app's modes (ChatGPT ⇄ Codex)");
            }

            // "Open" read as "launch it" — but the app is already running; this key GOES there.
            // Most useful on an always-on profile: it is the escape hatch from the editor you
            // were working in, and on the app-bound page it is redundant by definition.
            this.AddParameter(Focus, $"Show {DesktopServices.App.ShortName}", "Agent")
                .SetDescription($"Bring {DesktopServices.App.ShortName} to the front — for when you need to look before answering");

            DesktopServices.Monitor.OnChanged += _ => this.ActionImageChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            if (!DesktopServices.Declared)
            {
                return;
            }

            var app = DesktopServices.App;
            var auto = DesktopServices.Automation;

            switch (actionParameter)
            {
                case Stop:
                    // Press-time truth: a different ChatGPT/Codex window may have become focused
                    // since the keypad face last refreshed.
                    if (!auto.Status().StopPresent)
                    {
                        PluginLog.Info("DesktopControlCommand(stop): nothing running — ignored");
                        return;
                    }
                    auto.Press(app.StopLabels, out _);
                    break;

                case NewChat:
                    auto.Press(new[] { app.NewChatLabel }, out _);
                    break;

                case Mode:
                    // Toggle to the other mode. Unknown current mode (surface just came back,
                    // or the label drifted) → do nothing rather than guess a direction.
                    var current = auto.Status().Mode;
                    var target = app.ModeNames.FirstOrDefault(n => !String.Equals(n, current, StringComparison.Ordinal));
                    if (!app.ModeNames.Contains(current, StringComparer.Ordinal) || target == null)
                    {
                        PluginLog.Info("DesktopControlCommand(mode): current mode unknown — ignored");
                        return;
                    }
                    auto.SwitchMode(target);
                    break;

                case ShowDiff:
                    // The Review-surface controls exist in the tree ("Toggle file diff",
                    // "Show files" — captured in the button inventory); first match wins and a
                    // no-match logs rather than guesses.
                    auto.Press(app.ShowDiffLabels, out _);
                    break;

                case Focus:
                    auto.FocusApp();
                    break;
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            if (actionParameter == Mode && DesktopServices.Declared)
            {
                var mode = DesktopServices.Monitor.Current.Mode;
                if (!String.IsNullOrEmpty(mode))
                {
                    return mode;   // the key names the mode you're IN; pressing goes to the other
                }
            }

            return actionParameter switch
            {
                Stop => "Stop",
                NewChat => "New Chat",
                Mode => "Mode",
                ShowDiff => "Show Diff",
                Focus => DesktopServices.Declared ? $"Show {DesktopServices.App.ShortName}" : "Show App",
                _ => actionParameter,
            };
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var label = this.GetCommandDisplayName(actionParameter, imageSize);
            var icon = actionParameter switch
            {
                Stop => "stop",
                NewChat => "new_claude",
                Mode => "switch_mode",
                ShowDiff => "diff",
                Focus => "terminal",
                _ => null,
            };
            return KeyImage.Render(imageSize, label, KeyImage.Blue, icon);
        }
    }
}
