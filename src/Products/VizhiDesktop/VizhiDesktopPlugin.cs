namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration;

    /// <summary>
    /// Vizhi Desktop — the same engine as the terminal products, driving the OpenAI DESKTOP app
    /// (ChatGPT.app, bundle com.openai.codex, both ChatGPT and Codex surfaces) through the
    /// macOS Accessibility API instead of a TTY.
    ///
    /// The product is thin on purpose. This class declares WHICH app (the adapter), WHICH
    /// mechanism (the automation), and the product's namespaces — everything the keys do lives
    /// in Core/Desktop and Core/DesktopActions.
    ///
    /// ORDER MATTERS in the constructor, same law as the terminal products: the SDK constructs
    /// every action between this constructor and Load(), and the actions pull
    /// DesktopServices/IpcPaths. Declaring late builds the keys against nothing.
    ///
    /// What this product deliberately does NOT do: no BridgeManager.StartPolling (no terminal
    /// sessions to discover — the terminal poll stays dormant), no hook/statusline auto-wiring
    /// (nothing to wire — the desktop install mutates no user settings at all), no session
    /// grid. Voice ships: the capture pipeline is surface-neutral and only the sink differs.
    /// </summary>
    public class VizhiDesktopPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;
        // Unlike the terminal products, this plugin owns a real foreground application
        // (VizhiDesktopApplication). Reporting HasNoApplication here makes Options+ expose the
        // imported profile in the editor but skip the ClientApplication when ChatGPT becomes
        // frontmost, so the keypad remains on its previous/default profile.
        public override Boolean HasNoApplication => false;

        private readonly DesktopMonitor _monitor;
        private readonly IDesktopAppAdapter _app;

        public VizhiDesktopPlugin()
        {
            PluginLog.Init(this.Log, "Vizhi Desktop");
            PluginResources.Init(this.Assembly);

            // Before any action is constructed — actions resolve IPC paths and DesktopServices.
            IpcPaths.UseProduct("vizhi-desktop");

            _app = new OpenAiDesktopAdapter();
            IDesktopAutomation automation = OperatingSystem.IsWindows()
                ? new WindowsDesktopAutomation(_app)
                : new MacDesktopAutomation(_app);
            _monitor = new DesktopMonitor(automation);
            DesktopServices.Declare(_app, automation, _monitor);
        }

        public override void Load()
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
            {
                PluginLog.Warning("VizhiDesktopPlugin: unsupported platform");
                return;
            }

            BridgeManager.Instance.PluginAssemblyFilePath = this.AssemblyFilePath;

            if (OperatingSystem.IsWindows() && !WindowsDesktopAutomation.IsPackaged)
            {
                PluginLog.Warning("VizhiDesktopPlugin: Windows UIA helper missing — desktop keys will report No Signal");
            }

            if (OperatingSystem.IsWindows() && _app.WindowsProcessNames.Length == 0)
            {
                // Do not start polling or self-register a guessed application. The standalone
                // helper's inspect verb is the only enabled Windows path until W0 supplies the
                // real executable name.
                PluginLog.Warning("VizhiDesktopPlugin: Windows app identity unconfirmed — run vizhi-desktop-uia inspect; plugin remains disabled");
                return;
            }

            // Package-only installs: copy the AX helper out of the .lplug4 (dev builds already
            // have it from tools/desktop/build.sh). Voice installs itself lazily on first press.
            DesktopRuntime.EnsureInstalled(this.AssemblyFilePath);

            _monitor.Start();

            // Sweep orphans first, then register-or-heal — same sequence and same reasons as
            // the terminal products (an orphaned registration steals activation and looks like
            // THIS plugin being broken).
            RegistrationCleanup.RemoveOrphans(
                RegistrationHeal.ApplicationsRoot(),
                Platform.PluginPaths.PluginsRoot,
                "VizhiDesktop");

            var windowsProcess = OperatingSystem.IsWindows() ? _app.WindowsProcessNames[0] : null;
            if (!SelfRegistration.RegisterIfMissing(windowsProcess))
            {
                // Marketplace installs can retain an older ApplicationInfo timestamp even after
                // correctly adopting it. The shared timestamp heuristic would then restart LPS
                // during a healthy install. Desktop still self-registers a genuinely missing
                // sideload entry above, but never restarts merely because timestamps differ.
                RegistrationHeal.HealIfNeeded(automaticRestartAllowed: false);
            }

            PluginLog.Info("VizhiDesktopPlugin: Loaded — driving the ChatGPT/Codex desktop app");
        }

        public override void Unload()
        {
            _monitor.Stop();
            PluginLog.Info("VizhiDesktopPlugin: Unloaded");
        }
    }
}
