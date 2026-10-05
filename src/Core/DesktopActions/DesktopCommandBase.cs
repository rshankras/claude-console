namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>Consume repeat events locally. Only one completed tap reaches a command;
    /// the input callback never waits for the command's helper process.</summary>
    public abstract class DesktopCommandBase : PluginDynamicCommand
    {
        private readonly Dictionary<String, DesktopVoiceDraftCommand.ButtonHandler> _taps = new();
        private DesktopBusyFeedback _busyFeedback;
        protected DesktopCommandBase() => RegisterCleanup();
        protected DesktopCommandBase(String displayName, String description, String groupName)
            : base(displayName, description, groupName) => RegisterCleanup();

        private void RegisterCleanup()
        {
            _busyFeedback = new DesktopBusyFeedback(() => this.ActionImageChanged());
            DesktopServices.Lifetime.OnStop(() =>
            { lock (_taps) _taps.Clear(); _busyFeedback.Dispose(); });
        }

        protected Boolean RunDesktopAction(String parameter, Action work)
        {
            // Clear before admission: clearing after TryRun could erase feedback from a second
            // press that was rejected on another SDK callback thread in the meantime.
            _busyFeedback.Clear(parameter);
            return DesktopServices.Run(work, () => ShowBusy(parameter));
        }

        protected void ShowBusy(String parameter) => _busyFeedback.Show(parameter);
        internal Boolean ShowsBusy(String parameter) => _busyFeedback.Contains(parameter);

        protected sealed override BitmapImage GetCommandImage(String parameter, PluginImageSize size) =>
            ShowsBusy(parameter) ? KeyImage.RenderIntentTile(size, "Busy", "waiting", "PRESS AGAIN")
                : GetDesktopCommandImage(parameter, size);
        protected abstract BitmapImage GetDesktopCommandImage(String parameter, PluginImageSize size);

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
