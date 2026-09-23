namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>Adaptive navigation on Tools. Only ChatGPT navigation opens a keypad folder.</summary>
    public sealed class DesktopNavigateCommand : DesktopCommandBase
    {
        private readonly FailureFace _feedback;
        private String _shownMode;
        private String _feedbackMode;
        public DesktopNavigateCommand()
        {
            this.SetWidget(true);
            this.AddParameter("find", "Find Chat / View Changes", "Conversations")
                .SetDescription("ChatGPT: find a conversation. Codex: open the diff without changing the keypad page.");
            this.AddParameter("search", "Find Chat (ChatGPT)", "Conversations")
                .SetDescription("Find a ChatGPT conversation; blank in Codex, whose View Changes key is inside Tasks.");
            _feedback = new FailureFace(() => this.ActionImageChanged(), holdMs: 1800);
            DesktopServices.Lifetime.OnStop(_feedback.Dispose);
            if (DesktopServices.Declared) DesktopServices.OnMonitorChanged(_ => this.ActionImageChanged());
        }

        internal static String SearchFolderParameter => PluginDynamicFolder.DynamicFolderNamePrefix + typeof(FindChatDynamicFolder).FullName;
        internal new static Boolean IsHidden(String parameter, String mode) => parameter == "search" && mode != "ChatGPT";

        protected override void RunCommand(String parameter)
        {
            if (parameter is not ("find" or "search") || !DesktopServices.Declared) return;
            var mode = _shownMode;
            if (IsHidden(parameter, mode)) return;
            _feedbackMode = mode;
            var state = DesktopServices.Monitor.Current;
            var face = FaceFor(state);
            if (state.Mode == mode && !face.Enabled)
            {
                _feedback.Show(face.Status);
                return;
            }
            _feedback.Show("Opening");
            if (!DesktopServices.Run(() => _feedback.Show(Execute(mode, DesktopServices.Automation,
                () => this.Plugin.ExecuteGenericAction(GenericActionNames.DynamicFolder, SearchFolderParameter, 0)))))
                _feedback.Show("Busy");
        }

        internal static String Execute(String shownMode, IDesktopAutomation automation, Action openSearch)
        {
            if (shownMode is not ("ChatGPT" or "Codex")) return "Unavailable";
            var current = automation.Status();
            if (!current.SurfaceAvailable) return "Open App";
            if (current.Mode != shownMode) return "Mode Changed";
            if (shownMode == "Codex")
                return current.AvailableControls.HasFlag(DesktopControl.Changes) ? OpenChanges(automation) : "Not available";
            openSearch();
            return null;
        }

        internal static String OpenChanges(IDesktopAutomation automation) =>
            automation.OpenChanges(out var error) ? "Opened" : error is "unsupported" or "panel-not-available"
                or "panel-opener-missing" or "panel-opener-disabled" ? "Not available" : "Couldn't open";

        internal static (String Label, String Icon, Boolean Enabled, String Status) ReviewFace(DesktopState state)
        {
            var enabled = state.Available && state.Mode == "Codex" && state.AvailableControls.HasFlag(DesktopControl.Changes);
            return ("View Changes", "diff", enabled, !state.Available ? "Open App" : enabled ? null : "Not available");
        }

        internal static (String Label, String Icon, Boolean Enabled, String Status) FaceFor(DesktopState state) =>
            state.Mode == "Codex" ? ReviewFace(state) : ("Find Chat", "search", state.Available && state.Mode == "ChatGPT",
                !state.Available ? "Open App" : state.Mode == "ChatGPT" ? null : "Unavailable");

        protected override String GetCommandDisplayName(String parameter, PluginImageSize size) => "\u200B";
        protected override BitmapImage GetCommandImage(String parameter, PluginImageSize size)
        {
            var state = DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable;
            _shownMode = state.Mode;
            if (IsHidden(parameter, state.Mode)) return KeyImage.Render(size, String.Empty, KeyImage.Blue);
            var face = FaceFor(state);
            return KeyImage.RenderControlTile(size, face.Label, face.Icon, face.Enabled,
                _feedback.IsActive && _feedbackMode == state.Mode ? _feedback.Text : face.Status);
        }
    }
}
