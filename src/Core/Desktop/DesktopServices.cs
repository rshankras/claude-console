namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;

    /// <summary>
    /// The desktop product's service locator — the same shape (and the same apology) as
    /// <c>BridgeManager.Instance</c>: the SDK constructs every action through a parameterless
    /// constructor between the plugin's constructor and Load(), so constructor injection is
    /// impossible and actions must pull.
    ///
    /// The plugin constructor MUST call <see cref="Declare"/> before the SDK builds actions —
    /// the ordering rule that already governs IpcPaths.UseProduct. An action that finds these
    /// null was constructed by a product that never declared a desktop surface (i.e. a terminal
    /// product accidentally compiling DesktopActions) — it hides rather than guesses.
    /// </summary>
    internal static class DesktopServices
    {
        public static IDesktopAppAdapter App { get; private set; }
        public static IDesktopAutomation Automation { get; private set; }
        public static DesktopMonitor Monitor { get; private set; }
        public static DesktopVoiceActions VoiceActions { get; private set; }
        public static DesktopSearch Search { get; private set; }
        public static DesktopSearchVoiceModel SearchVoice { get; private set; }
        public static DesktopDraftRecovery DraftRecovery { get; private set; }
        public static DesktopContextCapture Context { get; private set; }
        public static DesktopFilePicker Files { get; private set; }
        public static DesktopWorkflowVoice WorkflowVoice { get; private set; }
        internal static DesktopLifetime Lifetime { get; private set; } = new();
        internal static DesktopActionRunner Actions { get; private set; } = new();

        internal static Boolean Run(Action work, Action rejected = null)
        {
            if (!Declared || !Actions.Active) return false;
            if (Actions.TryRun(work)) return true;
            rejected?.Invoke();
            return false;
        }

        internal static void OnMonitorChanged(Action<DesktopState> handler)
        { var source = Monitor; Lifetime.Bind(() => source.OnChanged += handler, () => source.OnChanged -= handler); }
        internal static void OnVoiceChanged(Action handler)
        { var source = BridgeManager.Instance.Voice; Lifetime.Bind(() => source.Changed += handler, () => source.Changed -= handler); }
        internal static void OnVoiceFailed(Action<VoiceIntent, String> handler)
        { var source = BridgeManager.Instance; Lifetime.Bind(() => source.OnVoiceFailed += handler, () => source.OnVoiceFailed -= handler); }
        internal static void OnWorkflowChanged(Action handler)
        { var source = WorkflowVoice; Lifetime.Bind(() => source.Changed += handler, () => source.Changed -= handler); }
        internal static void OnContextChanged(Action handler)
        { var source = Context; Lifetime.Bind(() => source.Changed += handler, () => source.Changed -= handler); }
        internal static void OnSearchChanged(Action handler)
        { var source = Search; Lifetime.Bind(() => source.Changed += handler, () => source.Changed -= handler); }
        internal static void OnSearchVoiceChanged(Action handler)
        { var source = SearchVoice; Lifetime.Bind(() => source.Changed += handler, () => source.Changed -= handler); }
        internal static void OnDraftChanged(Action handler)
        { var source = DraftRecovery; Lifetime.Bind(() => source.Changed += handler, () => source.Changed -= handler); }
        internal static void OnDraftReady(Action handler)
        { var source = DraftRecovery; Lifetime.Bind(() => source.Ready += handler, () => source.Ready -= handler); }
        internal static void OnDraftDiscarded(Action handler)
        { var source = DraftRecovery; Lifetime.Bind(() => source.Discarded += handler, () => source.Discarded -= handler); }

        public static Boolean Declared => App != null && Automation != null && Monitor != null;

        public static void Declare(IDesktopAppAdapter app, IDesktopAutomation automation, DesktopMonitor monitor)
        {
            Actions.Stop();
            Monitor?.Stop();
            Lifetime.Dispose();
            Lifetime = new();
            Actions = new();
            App = app ?? throw new ArgumentNullException(nameof(app));
            Automation = automation ?? throw new ArgumentNullException(nameof(automation));
            Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            var actions = Actions;
            var voice = BridgeManager.Instance.Voice;
            Monitor.IsCommandBusy = () => actions.IsBusy || voice.Phase is VoicePhase.Starting or VoicePhase.Transcribing;
            VoiceActions = new DesktopVoiceActions(automation);
            DraftRecovery = new DesktopDraftRecovery(automation);
            Context = new DesktopContextCapture(automation) { DraftPending = () => DraftRecovery.Pending };
            Context.CopyTrace = new DesktopCopyTrace(System.IO.Path.Combine(
                BridgeManager.HomeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "claude-console", "desktop-copy-trace-until"), message => PluginLog.Info(message)).Write;
            if (automation is MacDesktopAutomation mac)
                mac.ChangesTrace = new DesktopChangesTrace(System.IO.Path.Combine(
                    BridgeManager.HomeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".claude", "claude-console", "desktop-changes-trace-until"), message => PluginLog.Info(message)).Write;
            Files = new DesktopFilePicker(automation, Context, System.IO.Path.Combine(
                BridgeManager.HomeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            Lifetime.OnStop(Files.End);
            WorkflowVoice = new DesktopWorkflowVoice(automation, DraftRecovery, Context);
            Search = new DesktopSearch(automation);
            SearchVoice?.Dispose();
            SearchVoice = new DesktopSearchVoiceModel(System.IO.Path.Combine(
                BridgeManager.HomeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "claude-console", "whisper"));
        }
    }
}
