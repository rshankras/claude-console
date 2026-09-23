namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;

    /// <summary>Consume repeat events locally. Only one completed tap reaches a command;
    /// the input callback never waits for the command's helper process.</summary>
    public abstract class DesktopCommandBase : PluginDynamicCommand
    {
        private readonly Dictionary<String, DesktopVoiceDraftCommand.ButtonHandler> _taps = new();
        protected DesktopCommandBase() => RegisterCleanup();
        protected DesktopCommandBase(String displayName, String description, String groupName)
            : base(displayName, description, groupName) => RegisterCleanup();

        private void RegisterCleanup() => Desktop.DesktopServices.Lifetime.OnStop(() =>
        { lock (_taps) _taps.Clear(); });

        protected override Boolean ProcessButtonEvent2(String parameter, DeviceButtonEvent2 buttonEvent)
        {
            parameter ??= "";
            lock (_taps)
            {
                if (buttonEvent.EventType == DeviceButtonEventType.Press)
                {
                    if (!_taps.ContainsKey(parameter) && _taps.Count < 32)
                        _taps[parameter] = new DesktopVoiceDraftCommand.ButtonHandler();
                }
                if (!_taps.TryGetValue(parameter, out var tap)) return true;
                tap.Handle(buttonEvent.EventType, () => null, () => this.RunCommand(parameter), _ => { });
                if (buttonEvent.EventType == DeviceButtonEventType.Release) _taps.Remove(parameter);
            }
            return true;
        }
    }
}
