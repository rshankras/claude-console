namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

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
        public override Boolean HasNoApplication => true;

        private readonly DesktopMonitor _monitor;

        public VizhiDesktopPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);

            // Before any action is constructed — actions resolve IPC paths and DesktopServices.
            IpcPaths.UseProduct("vizhi-desktop");

            var app = new OpenAiDesktopAdapter();
            var automation = new MacDesktopAutomation(app);
            _monitor = new DesktopMonitor(automation);
            DesktopServices.Declare(app, automation, _monitor);
        }

        public override void Load()
        {
            BridgeManager.Instance.PluginAssemblyFilePath = this.AssemblyFilePath;

            // Package-only installs: copy the AX helper out of the .lplug4 (dev builds already
            // have it from tools/desktop/build.sh). Voice installs itself lazily on first press.
            DesktopRuntime.EnsureInstalled(this.AssemblyFilePath);

            _monitor.Start();

            // Sweep orphans first, then register-or-heal — same sequence and same reasons as
            // the terminal products (an orphaned registration steals activation and looks like
            // THIS plugin being broken).
            Platform.RegistrationCleanup.RemoveOrphans(
                Platform.RegistrationHeal.ApplicationsRoot(),
                Platform.PluginPaths.PluginsRoot,
                "VizhiDesktop");

            if (!Platform.SelfRegistration.RegisterIfMissing())
            {
                Platform.RegistrationHeal.HealIfNeeded();
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
