namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// Find Chat, in ChatGPT and in Codex: the app's search is the same in both modes, so the
    /// key never changes meaning (it was View Changes in Codex, which Tasks already offers).
    /// Bound directly to a key: Options+ opens a folder only from a key binding; a command
    /// asking it to open one (the old Find Chat / View Changes command) is received and
    /// ignored, which left that key on "Opening".
    /// </summary>
    public sealed class FindChatDynamicFolder : DesktopActionFolder
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
            this.Description = "Find a conversation by speaking or typing a query";
            this.GroupName = "Conversations";
        }
        public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType _) => PluginDynamicFolderNavigation.ButtonArea;

        public override Boolean Load()
        {
            if (!_loaded && DesktopServices.Declared)
            {
                _loaded = true; _search = DesktopServices.Search;
                DesktopServices.Lifetime.OnStop(() => Deactivate());
            }
            return true;
        }
        public override Boolean Unload()
        {
            return Deactivate();
        }

        // The host has already pushed this folder before Activate; a non-search result must
        // explicitly Close (the SDK ignores Activate's return value on MX Creative Keypad).
        internal static Boolean Open(IDesktopAutomation automation, IDesktopAppAdapter app, DesktopSearch search)
        {
            var state = automation.Status();
            if (!state.SurfaceAvailable) { PluginLog.Info("FindChatDynamicFolder: closed, the app's surface is unavailable"); return false; }
            var mode = app.ModeNames.FirstOrDefault(m => String.Equals(m, state.Mode, StringComparison.OrdinalIgnoreCase));
            // An open search box hides the mode switcher (#147), so a second Find Chat press read
            // "mode unreadable" and bounced the keypad back. Open with the mode the last search was
            // pinned to (ChatGPT before any): the helper takes over a box that is already open and
            // still refuses a visible, different mode — then the retry page shows why.
            if (mode == null) PluginLog.Info($"FindChatDynamicFolder: mode unreadable, opening as {search.Mode}");
            // The search is pinned to the mode it opened in. Keep the retry/Use App page
            // visible if the app's layout is unsupported.
            search.Begin(mode ?? search.Mode);
            return true;
        }

        public override Boolean Activate()
        {
            if (!DesktopServices.Declared) return false;
            ClearRetry();
            _search ??= DesktopServices.Search;
            var generation = Interlocked.Increment(ref _generation);
            var search = _search;
            var automation = DesktopServices.Automation;
            var app = DesktopServices.App;
            search.Changed -= OnChanged;
            search.Changed += OnChanged;
            search.Selected -= OnSelected;
            search.Selected += OnSelected;
            _timer?.Dispose(); _timer = null;
            PluginLog.Info("FindChatDynamicFolder: opened from the keypad");
            if (!DesktopServices.Run(() =>
            {
                if (generation != Interlocked.Read(ref _generation)) return;
                var opened = Open(automation, app, search);
                if (generation != Interlocked.Read(ref _generation)) { PluginLog.Info("FindChatDynamicFolder: left before the app answered"); search.End(); return; }
                if (!opened) { this.Close(); return; }
                lock (_timerGate)
                    if (generation == Interlocked.Read(ref _generation))
                        _timer = new Timer(_ => PollSearch(generation), null, 1000, Timeout.Infinite);
                RefreshActions();
            }, operation: nameof(FindChatDynamicFolder))) { PluginLog.Info("FindChatDynamicFolder: busy, waiting for the user to retry"); search.Changed -= OnChanged; search.Selected -= OnSelected; ShowRetry(); }
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
            ClearRetry();
            lock (_timerGate)
            {
                Interlocked.Increment(ref _generation);
                _timer?.Dispose(); _timer = null;
            }
            if (_search == null) return true;
            _search.Changed -= OnChanged;
            _search.Selected -= OnSelected;
            _search.End();
            BridgeManager.Instance.StopSearchCapture();
            return true;
        }

        private void OnChanged() => RefreshActions();

        // The chat is open; leave the page. Only the folder's own Close() does that (#151), and the
        // host then calls Deactivate, which ends the session, so the next Find Chat press starts
        // a fresh search in whatever mode the app is in by then.
        private void OnSelected()
        {
            PluginLog.Info("FindChatDynamicFolder: closed after opening the chat");
            this.Close();
        }
        internal static String[] Actions(String plugin, DesktopSearch search) =>
            new[] { "speak", "status" }.Concat(search.Current.Results.Select(search.ResultParameter))
                .Select(p => ActionString.ToString(plugin, typeof(DesktopSearchCommand).FullName, p)).ToArray();
        public override IEnumerable<String> GetButtonPressActionNames(DeviceType _) =>
            RetryPending ? RetryActions : DesktopServices.Declared ? Actions(this.Plugin.Name, DesktopServices.Search) : Array.Empty<String>();
        public override String GetButtonDisplayName(PluginImageSize _) => Face.Label;
        public override BitmapImage GetButtonImage(PluginImageSize size) => KeyImage.Render(size, Face.Label, KeyImage.Blue, Face.Icon);

        // One face in every mode: the key is Find Chat wherever the app is.
        internal static (String Label, String Icon) Face => ("Find Chat", "search");
    }
}
