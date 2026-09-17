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

        public static Boolean Declared => App != null && Automation != null && Monitor != null;

        public static void Declare(IDesktopAppAdapter app, IDesktopAutomation automation, DesktopMonitor monitor)
        {
            App = app ?? throw new ArgumentNullException(nameof(app));
            Automation = automation ?? throw new ArgumentNullException(nameof(automation));
            Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            VoiceActions = new DesktopVoiceActions(automation);
        }
    }
}
