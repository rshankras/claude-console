namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    public sealed class DesktopFilesDynamicFolder : PluginDynamicFolder
    {
        private DesktopFilePicker _picker;
        private Boolean _registered;
        private Int64 _generation;
        public DesktopFilesDynamicFolder()
        { this.DisplayName = "Attach Files"; this.Description = "Choose recent Downloads on the keypad, then attach to the current chat"; this.GroupName = "Tools"; }
        public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType _) => PluginDynamicFolderNavigation.ButtonArea;
        public override Boolean Activate()
        {
            if (!DesktopServices.Declared) return false;
            if (!_registered) { DesktopServices.Lifetime.OnStop(() => Deactivate()); _registered = true; }
            _picker = DesktopServices.Files;
            var generation = Interlocked.Increment(ref _generation);
            _picker.Changed -= OnChanged; _picker.Changed += OnChanged;
            if (!DesktopServices.Run(() => { if (generation == Interlocked.Read(ref _generation)) _picker.Begin(); })) { Deactivate(); this.Close(); }
            return true;
        }
        public override Boolean Deactivate()
        { Interlocked.Increment(ref _generation); if (_picker != null) { _picker.Changed -= OnChanged; _picker.End(); } return true; }
        private void OnChanged() => this.ButtonActionNamesChanged();
        internal static String[] Actions(String plugin, DesktopFilePicker picker) =>
            new[] { "attach", "browse", "refresh" }.Concat(picker.Current.Files.Length == 0 ? new[] { "empty" }
                : picker.Current.Files.Select(picker.Parameter)).Select(p => ActionString.ToString(plugin, typeof(DesktopFileCommand).FullName, p)).ToArray();
        public override IEnumerable<String> GetButtonPressActionNames(DeviceType _) =>
            DesktopServices.Declared ? Actions(this.Plugin.Name, DesktopServices.Files) : Array.Empty<String>();
        public override String GetButtonDisplayName(PluginImageSize _) => "Attach Files";
        public override BitmapImage GetButtonImage(PluginImageSize size) => KeyImage.Render(size, "Attach Files", KeyImage.Blue, "attach");
    }

}
