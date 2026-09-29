namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    public sealed class DesktopFileCommand : DesktopCommandBase
    {
        public DesktopFileCommand()
        {
            this.SetWidget(true);
            this.AddParameter("attach", "Attach selected files", "Files");
            this.AddParameter("browse", "Browse in app", "Files");
            this.AddParameter("refresh", "Refresh Downloads", "Files");
            this.AddParameter("empty", "No recent files", "Files");
            if (DesktopServices.Declared)
            { var picker = DesktopServices.Files; DesktopServices.Lifetime.Bind(() => picker.Changed += Refresh, () => picker.Changed -= Refresh); }
        }
        private void Refresh() => this.ActionImageChanged();
        protected override void RunCommand(String parameter)
        {
            if (!DesktopServices.Declared) return;
            DesktopServices.Run(() =>
            {
                if (DesktopServices.Files.Execute(parameter, DesktopServices.App, BridgeManager.Instance.Voice))
                    this.Plugin.ExecuteGenericAction(ActionString.FromString(PluginDynamicFolder.NavigateUpActionName).ActionName, null, 0);
            });
        }
        protected override String GetCommandDisplayName(String _, PluginImageSize __) => "\u200B";
        protected override BitmapImage GetCommandImage(String parameter, PluginImageSize size)
        {
            var picker = DesktopServices.Files; var state = picker?.Current ?? new();
            if (parameter == "attach") return KeyImage.RenderIntentTile(size, state.Busy ? state.Feedback ?? "Loading" : "Attach " + state.Selected.Length,
                "attach", state.Busy ? "WAIT" : state.Feedback ?? (state.Selected.Length == 0 ? "SELECT FILES" : "TO CHAT"));
            if (parameter == "browse") return KeyImage.RenderIntentTile(size, "Browse", "project", "USE APP");
            if (parameter == "refresh") return KeyImage.RenderIntentTile(size, "Downloads", "document", "TAP TO REFRESH");
            var file = picker?.Resolve(parameter);
            if (file == null) return KeyImage.RenderIntentTile(size, state.Feedback ?? "No Recent Files", "document", "TRY BROWSE");
            var selected = state.Selected.Contains(file.Id);
            var type = System.IO.Path.GetExtension(file.Name).TrimStart('.').ToUpperInvariant();
            return DesktopConversationRenderer.Render(size, file.Name, selected ? "SELECTED" : (type.Length > 8 ? "FILE" : type) + " · SELECT",
                selected ? KeyImage.Green : KeyImage.Gray, selected);
        }
    }
}
