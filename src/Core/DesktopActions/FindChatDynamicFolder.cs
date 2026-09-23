namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>ChatGPT search content, opened from the adaptive Home command.</summary>
    public sealed class FindChatDynamicFolder : PluginDynamicFolder
    {
        private Timer _timer;
        private Int32 _polling;
        private Int64 _generation;
        private Boolean _loaded;
        private DesktopSearch _search;
        private readonly Object _timerGate = new();
        public FindChatDynamicFolder()
        {
            this.DisplayName = "Find Chat";
            this.Description = "Find a ChatGPT conversation by speaking or typing a query";
            this.GroupName = "Conversations";
        }
        public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType _) => PluginDynamicFolderNavigation.ButtonArea;

        public override Boolean Load()
        {
            if (!_loaded && DesktopServices.Declared)
            {
                _loaded = true; _search = DesktopServices.Search;
                DesktopServices.OnMonitorChanged(OnModeChanged);
                DesktopServices.Lifetime.OnStop(() => Deactivate());
            }
            return true;
        }
        public override Boolean Unload()
        {
            return Deactivate();
        }
        private void OnModeChanged(DesktopState _) => this.Plugin.OnActionImageChanged("#DynamicFolder", this.Name, false);

        // The host has already pushed this folder before Activate; a non-search result must
        // explicitly Close (the SDK ignores Activate's return value on MX Creative Keypad).
        internal static Boolean Open(IDesktopAutomation automation, IDesktopAppAdapter app, DesktopSearch search)
        {
            var state = automation.Status();
            if (!state.SurfaceAvailable) return false;
            if (!String.Equals(state.Mode, "ChatGPT", StringComparison.OrdinalIgnoreCase)) return false;
            search.Begin(); // Keep the retry/Use App page visible if the app's layout is unsupported.
            return true;
        }

        public override Boolean Activate()
        {
            if (!DesktopServices.Declared) return false;
            _search ??= DesktopServices.Search;
            var generation = Interlocked.Increment(ref _generation);
            var search = _search;
            var automation = DesktopServices.Automation;
            var app = DesktopServices.App;
            search.Changed -= OnChanged;
            search.Changed += OnChanged;
            _timer?.Dispose(); _timer = null;
            if (!DesktopServices.Run(() =>
            {
                if (generation != Interlocked.Read(ref _generation)) return;
                var opened = Open(automation, app, search);
                if (generation != Interlocked.Read(ref _generation)) { search.End(); return; }
                if (!opened) { this.Close(); return; }
                lock (_timerGate)
                    if (generation == Interlocked.Read(ref _generation))
                        _timer = new Timer(_ => PollSearch(generation), null, 1000, Timeout.Infinite);
                this.ButtonActionNamesChanged();
            })) { search.Changed -= OnChanged; this.Close(); }
            return true;
        }

        private void PollSearch(Int64 generation)
        {
            if (generation != Interlocked.Read(ref _generation)) return;
            if (Interlocked.Exchange(ref _polling, 1) != 0) { ArmSearch(generation, 250); return; }
            var start = Environment.TickCount64;
            try
            {
                if (!DesktopServices.Actions.IsBusy && BridgeManager.Instance.Voice.Phase == VoicePhase.Idle)
                    _search.Refresh();
            }
            catch { _search.ShowFeedback("Use App"); }
            finally
            {
                Volatile.Write(ref _polling, 0);
                ArmSearch(generation, Environment.TickCount64 - start >= 1000 ? 5000 : 1000);
            }
        }

        private void ArmSearch(Int64 generation, Int32 delay)
        {
            lock (_timerGate)
                if (generation == Interlocked.Read(ref _generation)) _timer?.Change(delay, Timeout.Infinite);
        }

        public override Boolean Deactivate()
        {
            lock (_timerGate)
            {
                Interlocked.Increment(ref _generation);
                _timer?.Dispose(); _timer = null;
            }
            if (_search == null) return true;
            _search.Changed -= OnChanged;
            _search.End();
            BridgeManager.Instance.StopSearchCapture();
            return true;
        }

        private void OnChanged() => this.ButtonActionNamesChanged();
        internal static String[] Actions(String plugin, DesktopSearch search) =>
            new[] { "speak", "status" }.Concat(search.Current.Results.Select(search.ResultParameter))
                .Select(p => ActionString.ToString(plugin, typeof(DesktopSearchCommand).FullName, p)).ToArray();
        public override IEnumerable<String> GetButtonPressActionNames(DeviceType _) =>
            DesktopServices.Declared ? Actions(this.Plugin.Name, DesktopServices.Search) : Array.Empty<String>();
        public override String GetButtonDisplayName(PluginImageSize _) => "Find Chat";
        public override BitmapImage GetButtonImage(PluginImageSize size) =>
            KeyImage.Render(size, "Find Chat", KeyImage.Blue, "search");
    }
}
