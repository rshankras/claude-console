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
    public class DesktopControlCommand : DesktopCommandBase
    {
        private const String Stop = "stop";
        private const String NewChat = "new_chat";
        private const String Mode = "mode";
        private const String Focus = "focus";
        private const String ShowDiff = "show_diff";
        private readonly FailureFace _feedback;
        private String _feedbackParameter;

        public DesktopControlCommand()
            : base()
        {
            this.SetWidget(true);
            _feedback = new FailureFace(() => this.ActionImageChanged());
            DesktopServices.Lifetime.OnStop(_feedback.Dispose);
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
                this.AddParameter(ShowDiff, "View Changes", "Agent")
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

            DesktopServices.OnMonitorChanged(_ => this.ActionImageChanged());
        }

        protected override void RunCommand(String actionParameter)
        {
            DesktopServices.Run(() => this.RunDesktopCommand(actionParameter));
        }

        private void RunDesktopCommand(String actionParameter)
        {
            if (!DesktopServices.Declared)
            {
                return;
            }

            _feedbackParameter = actionParameter;
            _feedback.Show(actionParameter == ShowDiff ? "Opening" : "Working");
            _feedback.Show(Execute(actionParameter, DesktopServices.App, DesktopServices.Automation));
        }

        // The SDK and the command rig use this same press handler.
        internal static String Execute(String actionParameter, IDesktopAppAdapter app, IDesktopAutomation auto)
        {

            switch (actionParameter)
            {
                case Stop:
                    // Resolve and press in one invocation. "Stop voice chat" must NEVER match.
                    return auto.PressExact(app.StopLabels) ? "Requested" : "Nothing Running";

                case NewChat:
                    return auto.Press(new[] { app.NewChatLabel }, out _) ? "Requested" : "Not Opened";

                case Mode:
                    // Toggle to the other mode. Unknown current mode (surface just came back,
                    // or the label drifted) → do nothing rather than guess a direction.
                    var current = auto.Status().Mode;
                    var target = app.ModeNames.FirstOrDefault(n => !String.Equals(n, current, StringComparison.Ordinal));
                    if (!app.ModeNames.Contains(current, StringComparer.Ordinal) || target == null)
                    {
                        return "Open App";
                    }
                    return auto.SwitchMode(target) ? "Requested" : "Not Switched";

                case ShowDiff:
                    return DesktopNavigateCommand.Execute("Codex", auto, () => { });

                case Focus:
                    return auto.FocusApp() ? "Opened" : "Not Opened";
            }
            return "Unavailable";
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "\u200B";

        private String LabelFor(String actionParameter)
        {
            if (actionParameter == NewChat && DesktopServices.Declared && DesktopServices.Monitor.Current.Mode == "Codex") return "New Task";
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
            if (actionParameter == ShowDiff)
            {
                var face = DesktopNavigateCommand.ReviewFace(DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable);
                return KeyImage.RenderControlTile(imageSize, face.Label, face.Icon, face.Enabled,
                    _feedback.IsActive && _feedbackParameter == actionParameter ? _feedback.Text : face.Status);
            }
            var label = this.LabelFor(actionParameter);
            var icon = actionParameter switch
            {
                Stop => "stop",
                NewChat => "new_chat",
                Mode => "switch_mode",
                ShowDiff => "diff",
                Focus => "terminal",
                _ => null,
            };
            var status = _feedback.IsActive && _feedbackParameter == actionParameter ? _feedback.Text
                : actionParameter == Mode ? DestinationFor(label) : null;
            return KeyImage.RenderControlTile(imageSize, label, icon, true, status);
        }

        internal static String DestinationFor(String mode) => mode switch
        { "ChatGPT" => "TO CODEX", "Codex" => "TO CHATGPT", _ => "Open App" };
    }
}
