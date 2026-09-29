namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    public sealed class DesktopMoreDynamicFolder : PluginDynamicFolder
    {
        private DesktopMonitor _monitor;
        private DesktopContextCapture _context;
        private Boolean _cleanupRegistered;
        private IReadOnlyList<DesktopWorkflowCommand.WorkflowDef> _tasks;
        private IReadOnlyList<DesktopWorkflowCommand.WorkflowDef> _prompts;
        public DesktopMoreDynamicFolder()
        { this.DisplayName = "More"; this.Description = "Additional app tools and workflows"; this.GroupName = "Agent"; }
        public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType _) => PluginDynamicFolderNavigation.ButtonArea;
        public override Boolean Activate()
        {
            if (DesktopServices.Declared)
            {
                if (!_cleanupRegistered)
                { DesktopServices.Lifetime.OnStop(() => Deactivate()); _cleanupRegistered = true; }
                _tasks ??= DesktopWorkflowCommand.LoadCodexFavorites();
                _prompts ??= DesktopWorkflowCommand.LoadChatGptFavorites();
                if (_monitor != null) _monitor.OnChanged -= OnChanged;
                _monitor = DesktopServices.Monitor;
                _monitor.OnChanged += OnChanged;
                if (_context != null) _context.Changed -= OnContextChanged;
                _context = DesktopServices.Context; _context.Changed += OnContextChanged;
            }
            this.ButtonActionNamesChanged(); return true;
        }
        public override Boolean Deactivate()
        {
            if (_monitor != null) _monitor.OnChanged -= OnChanged;
            _monitor = null;
            if (_context != null) _context.Changed -= OnContextChanged;
            _context = null;
            return true;
        }
        private void OnChanged(DesktopState _) => this.ButtonActionNamesChanged();
        private void OnContextChanged() => this.ButtonActionNamesChanged();
        internal static String[] Actions(String plugin, String mode, DesktopControl available = (DesktopControl)(-1), Boolean staged = false,
            IReadOnlyList<DesktopWorkflowCommand.WorkflowDef> tasks = null, IReadOnlyList<DesktopWorkflowCommand.WorkflowDef> prompts = null)
        {
            if (mode is not ("ChatGPT" or "Codex")) return Array.Empty<String>();
            // App screens worth one press: Projects (ChatGPT), Permissions and Pull Requests (Codex).
            // Plugins, Explore, Quick Chat and ChatGPT's Scheduled open screens that need a pointer
            // or typing, and leave no chat to write into; they stay assignable, not in the menu.
            var context = mode == "Codex" ? new[] { "secondary_1", "secondary_3" } : new[] { "secondary_1" };
            var actions = context.Where(p => DesktopContextCommand.FaceFor(p, mode, available).Enabled)
                .Select(p => ActionString.ToString(plugin, typeof(DesktopContextCommand).FullName, p));
            if (mode == "Codex") actions = actions.Concat(new[] { "review_pr", "write_tests" }
                .Select(p => ActionString.ToString(plugin, typeof(DesktopWorkflowCommand).FullName, p)))
                .Concat((tasks ?? DesktopWorkflowCommand.CodexDefaults).Select((w, i) => (w, i))
                    .Where(item => DesktopSavedPromptsDynamicFolder.IsMoreTask(item.w.Id))
                    .Select(item => DesktopSavedPromptsDynamicFolder.TaskAction(plugin, item.i)));
            // ChatGPT's Continue: the ninth prompt, kept here so Prompts fits one page.
            if (mode == "ChatGPT") actions = actions.Concat((prompts ?? DesktopWorkflowCommand.ChatGptDefaults).Select((w, i) => (w, i))
                    .Where(item => DesktopSavedPromptsDynamicFolder.IsMoreTask(item.w.Id))
                    .Select(item => DesktopSavedPromptsDynamicFolder.PromptAction(plugin, item.i)));
            if (staged) actions = actions.Append(ActionString.ToString(plugin, typeof(DesktopCaptureCommand).FullName, "clear"));
            return actions.ToArray();
        }
        public override IEnumerable<String> GetButtonPressActionNames(DeviceType _) =>
            DesktopServices.Declared ? Actions(this.Plugin.Name, DesktopServices.Monitor.Current.Mode,
                DesktopServices.Monitor.Current.AvailableControls, DesktopServices.Context.Count > 0, _tasks, _prompts) : Array.Empty<String>();
        public override String GetButtonDisplayName(PluginImageSize _) => "More";
        public override BitmapImage GetButtonImage(PluginImageSize size) => KeyImage.Render(size, "More", KeyImage.Blue, "more");
    }
}
