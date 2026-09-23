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
        private readonly DesktopLifetime _lifetime;
        private readonly DesktopActionRunner _actions;
        private readonly DesktopSearchVoiceModel _searchVoice;

        /// <summary>
        /// Vizhi Desktop drives the same OpenAI app as Vizhi for Codex, through the desktop
        /// surface instead of the terminal: one product family, one colour. Declared here, never
        /// in Core — the engine draws state and must not know whose keypad it is.
        /// </summary>
        private static readonly BitmapColor CodexBlue = new BitmapColor(0x81, 0xA8, 0xED);

        public VizhiDesktopPlugin()
        {
            PluginLog.Init(this.Log, "Vizhi Desktop");
            PluginResources.Init(this.Assembly);
            // Identity through the engine's seams, like the other two products: every action icon
            // resolves under desktop_icons, state-semantic art stays shared, and the bracket and
            // routed-session bar take the family colour.
            KeyImage.UseIdentityIconFolder("desktop_icons");
            KeyImage.UseIdentityColors(CodexBlue, CodexBlue);

            // Before any action is constructed — actions resolve IPC paths and DesktopServices.
            IpcPaths.UseProduct("vizhi-desktop");

            _app = new OpenAiDesktopAdapter();
            IDesktopAutomation automation = OperatingSystem.IsWindows()
                ? new WindowsDesktopAutomation(_app)
                : new MacDesktopAutomation(_app, DesktopVoiceShortcut.Load(DesktopVoiceShortcut.ConfigPath));
            _monitor = new DesktopMonitor(automation);
            DesktopServices.Declare(_app, automation, _monitor);
            _lifetime = DesktopServices.Lifetime;
            _actions = DesktopServices.Actions;
            _searchVoice = DesktopServices.SearchVoice;
            if (automation is MacDesktopAutomation mac) mac.IsEnabled = () => _lifetime.Active;

            // The voice keys are aimed at the app's composer, not a terminal. The engine keeps
            // capture, routing and the named failure faces; the product only says where the words
            // go. A draft is brought forward so it can be read before it is sent.
            var bridge = BridgeManager.Instance;
            var recovery = DesktopServices.DraftRecovery;
            var workflow = DesktopServices.WorkflowVoice;
            _lifetime.Bind(() =>
            {
                bridge.TranscriptSink = (text, send) => _lifetime.Active
                    ? DesktopTranscriptDelivery.Write(automation, text, send, recovery.NotifyReady) : "Cancelled";
                bridge.DraftRecoverySink = recovery.Retain;
                bridge.VoiceModelOverride = intent =>
                    DesktopSearchVoiceModel.AppliesTo(intent, OperatingSystem.IsMacOS())
                        ? (true, _searchVoice.EnsureReady() ? _searchVoice.ModelPath : null)
                        : (false, null);
                bridge.SearchAudioHasSignal = DesktopSearchAudio.HasSignal;
                bridge.DesktopCaptureAllowed = _lifetime.CaptureGuard();
            }, bridge.ClearDesktopRouting);
            DesktopServices.OnVoiceFailed(workflow.Fail);
            DesktopServices.OnSearchVoiceChanged(this.SearchVoiceChanged);

            // Same family as Vizhi for Codex, same hold: a failure word is an instruction, held
            // long enough to read and act on.
            VoiceFailure.UseHold(8000);
        }

        public override void Load()
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
            {
                PluginLog.Warning("VizhiDesktopPlugin: unsupported platform");
                return;
            }

            _lifetime.Start();
            _actions.Start();
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

            // Prepare Speak Query once on load. The download contains only local inference
            // weights, never audio. The keypad and Options+ show progress; no microphone opens.
            if (OperatingSystem.IsMacOS())
            {
                _searchVoice.EnsureReady();
                this.SearchVoiceChanged();
            }

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

        private void SearchVoiceChanged()
        {
            var model = _searchVoice.Status;
            try
            {
                if (model.Phase == SpeechModelPhase.Ready)
                {
                    if (DesktopServices.Search.Feedback == VoiceFailure.ModelLoading)
                        DesktopServices.Search.ShowFeedback(null);
                    this.OnPluginStatusChanged(Loupedeck.PluginStatus.Normal, String.Empty);
                }
                else
                    this.OnPluginStatusChanged(Loupedeck.PluginStatus.Warning,
                        model.Phase == SpeechModelPhase.Failed
                            ? "Speak Query download failed. Press Speak Query to retry."
                            : $"Preparing Speak Query (574 MB, one time): {model.Footer}");
            }
            catch (Exception ex) { PluginLog.Warning(ex, "VizhiDesktopPlugin: voice model status unavailable"); }
        }

        public override void Unload()
        {
            _actions.Stop();
            _monitor.Stop();
            _lifetime.Stop();
            _searchVoice.Suspend();
            PluginLog.Info("VizhiDesktopPlugin: Unloaded");
        }
    }
}
