namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// Stable physical positions whose meaning follows the focused OpenAI window's mode. The
    /// bindings themselves never move; only their face and verified target change. Every press
    /// takes a fresh snapshot before resolving, so a mode switch between glance and thumb cannot
    /// dispatch a ChatGPT action into Codex (or the reverse).
    /// </summary>
    public class DesktopContextCommand : DesktopCommandBase
    {
        internal const String Primary = "primary";
        internal const String Secondary1 = "secondary_1";
        internal const String Secondary2 = "secondary_2";
        internal const String Secondary3 = "secondary_3";
        internal const String Secondary4 = "secondary_4";
        internal const String Files = "files";
        private readonly FailureFace _feedback;
        private String _feedbackParameter;

        private static readonly IReadOnlyDictionary<String, Pair> Pairs =
            new Dictionary<String, Pair>(StringComparer.Ordinal)
            {
                [Files] = new Pair(
                    new Choice(DesktopControl.AttachFiles, "Attach Files", "attach"),
                    new Choice(DesktopControl.AttachFiles, "Attach Files", "attach")),
                [Primary] = new Pair(
                    new Choice(DesktopControl.Search, "Search", "search"),
                    new Choice(DesktopControl.Changes, "View Changes", "diff", "Not available")),
                [Secondary1] = new Pair(
                    new Choice(DesktopControl.Projects, "Projects", "project"),
                    new Choice(DesktopControl.Permissions, "Permissions", "security")),
                [Secondary2] = new Pair(
                    new Choice(DesktopControl.Plugins, "Plugins", "model"),
                    new Choice(DesktopControl.AttachFiles, "Attach Files", "attach")),
                [Secondary3] = new Pair(
                    new Choice(DesktopControl.Scheduled, "Scheduled", "scheduled"),
                    new Choice(DesktopControl.PullRequests, "Pull Requests", "create_pr")),
                [Secondary4] = new Pair(
                    new Choice(DesktopControl.Explore, "Explore", "explore"),
                    new Choice(DesktopControl.QuickChat, "Quick Chat", "quick_chat")),
            };

        public DesktopContextCommand()
            : base()
        {
            this.SetWidget(true);
            _feedback = new FailureFace(() => this.ActionImageChanged());
            DesktopServices.Lifetime.OnStop(_feedback.Dispose);
            if (!DesktopServices.Declared)
            {
                return;
            }

            foreach (var id in Pairs.Keys)
            {
                this.AddParameter(id, "Context Action", "Adaptive")
                    .SetDescription("Changes with the focused ChatGPT/Codex mode and only acts when its target is visible");
            }

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
            _feedback.Show("Working");
            _feedback.Show(Execute(actionParameter, DesktopServices.App, DesktopServices.Automation, () => this.ActionImageChanged()));
        }

        internal static String Execute(String actionParameter, IDesktopAppAdapter app,
            IDesktopAutomation automation, Action invalidate)
        {
            // Press-time truth, not the last rendered snapshot. The target window or mode may
            // have changed since the keypad was drawn.
            var snapshot = automation.Status();
            var face = FaceFor(actionParameter, snapshot.Mode, snapshot.AvailableControls);
            if (!face.Enabled)
            {
                invalidate();
                return face.Status ?? "Unavailable";
            }
            if (face.Control == DesktopControl.Changes) return DesktopNavigateCommand.OpenChanges(automation);

            var labels = app.ControlLabels(face.Control);
            if (!automation.PressInMode(labels, snapshot.Mode, out _))
            {
                PluginLog.Warning($"DesktopContextCommand({actionParameter}): '{face.Label}' disappeared before press");
                return "Not Opened";
            }
            return "Requested";
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "\u200B";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var state = DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable;
            var face = FaceFor(actionParameter, state.Mode, state.AvailableControls);
            return KeyImage.RenderControlTile(imageSize, face.Label, face.Icon, face.Enabled,
                _feedback.IsActive && _feedbackParameter == actionParameter ? _feedback.Text : face.Status);
        }

        internal static Face FaceFor(String slot, String mode, DesktopControl available)
        {
            if (!Pairs.TryGetValue(slot ?? "", out var pair))
            {
                return new Face(DesktopControl.None, "Unavailable", "status", false);
            }

            Choice choice;
            if (String.Equals(mode, "ChatGPT", StringComparison.OrdinalIgnoreCase))
            {
                choice = pair.ChatGpt;
            }
            else if (String.Equals(mode, "Codex", StringComparison.OrdinalIgnoreCase))
            {
                choice = pair.Codex;
            }
            else
            {
                return new Face(DesktopControl.None, "Mode?", "status", false);
            }

            var enabled = choice.Control != DesktopControl.None && available.HasFlag(choice.Control);
            return new Face(
                choice.Control,
                choice.Label,
                choice.Icon,
                enabled,
                enabled ? null : choice.DisabledLabel);
        }

        internal readonly struct Face
        {
            public Face(DesktopControl control, String label, String icon, Boolean enabled, String status = null)
            {
                this.Control = control;
                this.Label = label;
                this.Icon = icon;
                this.Enabled = enabled;
                this.Status = status;
            }

            public DesktopControl Control { get; }
            public String Label { get; }
            public String Icon { get; }
            public Boolean Enabled { get; }
            public String Status { get; }
        }

        private readonly struct Choice
        {
            public Choice(DesktopControl control, String label, String icon, String disabledLabel = "Unavailable")
            {
                this.Control = control;
                this.Label = label;
                this.Icon = icon;
                this.DisabledLabel = disabledLabel;
            }

            public DesktopControl Control { get; }
            public String Label { get; }
            public String Icon { get; }
            public String DisabledLabel { get; }
        }

        private readonly struct Pair
        {
            public Pair(Choice chatGpt, Choice codex)
            {
                this.ChatGpt = chatGpt;
                this.Codex = codex;
            }

            public Choice ChatGpt { get; }
            public Choice Codex { get; }
        }
    }
}
