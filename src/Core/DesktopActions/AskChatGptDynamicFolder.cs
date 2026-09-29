namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>Can be placed on System or source-app profiles; opening it keeps the source in front.</summary>
    public sealed class AskChatGptDynamicFolder : PluginDynamicFolder
    {
        public AskChatGptDynamicFolder()
        { this.DisplayName = "Ask ChatGPT"; this.Description = "Bring text or a screenshot into a spoken task, then take the reply back"; this.GroupName = "Context"; }
        public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType _) => PluginDynamicFolderNavigation.ButtonArea;
        internal static String[] Actions(String plugin) => new[] { "selection", "clipboard", "screenshot", "copy", "return", "paste", "clear" }
            .Select(p => ActionString.ToString(plugin, typeof(DesktopCaptureCommand).FullName, p))
            .Prepend(ActionString.ToString(plugin, typeof(DesktopWorkflowCommand).FullName, "draft_reply")).ToArray();
        public override IEnumerable<String> GetButtonPressActionNames(DeviceType _) => Actions(this.Plugin.Name);
        public override String GetButtonDisplayName(PluginImageSize _) => "Ask ChatGPT";
        public override BitmapImage GetButtonImage(PluginImageSize size) => KeyImage.Render(size, "Ask ChatGPT", KeyImage.Blue, "writing");
    }
}
