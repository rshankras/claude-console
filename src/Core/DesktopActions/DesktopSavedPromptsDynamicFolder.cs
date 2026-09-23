namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    public sealed class DesktopSavedPromptsDynamicFolder : PluginDynamicFolder
    {
        private Boolean _loaded;
        private String _mode;
        private IReadOnlyList<DesktopWorkflowCommand.WorkflowDef> _tasks = Array.Empty<DesktopWorkflowCommand.WorkflowDef>();
        public DesktopSavedPromptsDynamicFolder()
        { this.DisplayName = "Saved Prompts"; this.Description = "Optional workflow favorites for the current app mode"; this.GroupName = "Tools"; }
        public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType _) => PluginDynamicFolderNavigation.ButtonArea;
        public override Boolean Load()
        {
            if (!_loaded && DesktopServices.Declared)
            {
                _loaded = true;
                _tasks = DesktopWorkflowCommand.LoadCodexFavorites();
                _mode = DesktopServices.Monitor.Current.Mode;
                DesktopServices.OnMonitorChanged(state =>
                {
                    if (_mode == state.Mode) return;
                    _mode = state.Mode;
                    this.ButtonActionNamesChanged();
                    this.Plugin.OnActionImageChanged("#DynamicFolder", this.Name, false);
                });
            }
            return true;
        }
        internal static String[] Actions(String plugin, String mode, IReadOnlyList<DesktopWorkflowCommand.WorkflowDef> tasks = null)
        {
            if (mode == "ChatGPT") return Enumerable.Range(1, 9)
                .Select(i => ActionString.ToString(plugin, typeof(DesktopWorkflowCommand).FullName, "slot_" + i)).ToArray();
            if (mode != "Codex") return Array.Empty<String>();
            tasks ??= DesktopWorkflowCommand.CodexDefaults;
            var slots = Enumerable.Range(0, tasks.Count).Where(i => !IsMoreTask(tasks[i].Id));
            // Keep a customized order. Only the stock sequence gets the inspect/review/test row.
            if (tasks.Select(w => w.Id).SequenceEqual(DesktopWorkflowCommand.CodexDefaults.Select(w => w.Id)))
                slots = new[] { 0, 3, 1, 2, 4, 5, 6 };
            return new[] { ActionString.ToString(plugin, typeof(DesktopControlCommand).FullName, "show_diff") }
                .Concat(slots.Select(i => TaskAction(plugin, i))).ToArray();
        }
        internal static String TaskAction(String plugin, Int32 index) =>
            ActionString.ToString(plugin, typeof(DesktopWorkflowCommand).FullName, "task_" + (index + 1));
        internal static Boolean IsMoreTask(String id) => id is "update_deps" or "continue";
        public override IEnumerable<String> GetButtonPressActionNames(DeviceType _) =>
            DesktopServices.Declared ? Actions(this.Plugin.Name, DesktopServices.Monitor.Current.Mode, _tasks) : Array.Empty<String>();
        public override String GetButtonDisplayName(PluginImageSize _) => Label;
        private static String Label => DesktopServices.Declared && DesktopServices.Monitor.Current.Mode == "Codex" ? "Tasks" : "Prompts";
        public override BitmapImage GetButtonImage(PluginImageSize size) => KeyImage.Render(size, Label, KeyImage.Blue, "writing");
    }
}
