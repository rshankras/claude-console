namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Agents;

    /// <summary>
    /// Vizhi for Codex — the same engine as Claude Console, driving OpenAI's Codex CLI.
    ///
    /// The product is thin on purpose. Everything the keys do lives in Core and is agent-neutral;
    /// this class only declares WHICH agent, and the two declarations below are the whole of it:
    /// the IPC root this process owns, and the adapter the keys ask for their vocabulary.
    ///
    /// ORDER MATTERS in Load(). The product slug must be declared before anything resolves a path,
    /// because the SDK constructs every action while loading the plugin and an action that read a
    /// path first would send this console into Claude Console's IPC tree — where it would not only
    /// read the wrong sessions but reap them, since the grid deletes state for sessions whose
    /// process it cannot see, and this plugin cannot see `claude` processes.
    /// </summary>
    public class VizhiCodexPlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;
        public override Boolean HasNoApplication => true;

        private readonly CodexCliAdapter _agent = new CodexCliAdapter();

        public VizhiCodexPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);

            // Before any action is constructed — see the ordering note above.
            IpcPaths.UseProduct(this._agent.ProductSlug);
            BridgeManager.Instance.Agent = this._agent;
        }

        public override void Load()
        {
            BridgeManager.Instance.PluginAssemblyFilePath = this.AssemblyFilePath;

            BridgeManager.Instance.StartPolling();

            // Codex reports nothing until its hooks are installed AND trusted. Installing is ours;
            // trusting is the user's, in /hooks, and cannot be automated — so log which of the two
            // is outstanding rather than leaving a keypad that just looks broken.
            this.WireStateBridge();

            // Sweep orphans first: an entry whose plugin was uninstalled still holds the terminal
            // and shows a keypad of unresolvable keys, which looks like THIS plugin being broken.
            Platform.RegistrationCleanup.RemoveOrphans(
                Platform.RegistrationHeal.ApplicationsRoot(),
                Platform.PluginPaths.PluginsRoot,
                "VizhiCodex");

            if (!Platform.SelfRegistration.RegisterIfMissing())
            {
                Platform.RegistrationHeal.HealIfNeeded();
            }

            PluginLog.Info("VizhiCodexPlugin: Loaded — driving Codex CLI");
        }

        private void WireStateBridge()
        {
            // Windows takes the hook-free path: codex's hook runner creates no process there, so
            // there is nothing to install and no trust to ask for. State arrives from the rollout
            // stream instead (docs/windows-codex-hookless-bridge.md).
            if (OperatingSystem.IsWindows())
            {
                PluginLog.Info("VizhiCodexPlugin: Windows — state bridge is the rollout reader, no hooks installed");
                return;
            }

            var script = CodexCliAdapter.HookScriptContents();
            if (script == null)
            {
                // The launcher is embedded at build time; missing means a broken package, and the
                // plugin would otherwise run happily and silently never report a session.
                PluginLog.Info("VizhiCodexPlugin: codex-hook.sh is not embedded — state bridge cannot install");
                return;
            }

            var bridge = this._agent.StateBridge;
            bridge.EnsureInstalled(script);

            switch (bridge.Status)
            {
                case CodexBridgeStatus.AwaitingTrust:
                    PluginLog.Info(
                        "VizhiCodexPlugin: hooks installed but no event has arrived — run /hooks in Codex and " +
                        "trust the entry, then start a new session. (Never --dangerously-bypass-hook-trust.)");
                    break;

                case CodexBridgeStatus.ForeignHooksFile:
                    PluginLog.Info(
                        $"VizhiCodexPlugin: {bridge.HooksFile} was not written by us and will not be touched — " +
                        "merge the hooks by hand to get live keys");
                    break;

                case CodexBridgeStatus.Active:
                    PluginLog.Info("VizhiCodexPlugin: state bridge active");
                    break;

                default:
                    PluginLog.Info("VizhiCodexPlugin: state bridge is not installed");
                    break;
            }
        }

        public override void Unload()
        {
            BridgeManager.Instance.StopPolling();
            PluginLog.Info("VizhiCodexPlugin: Unloaded");
        }
    }
}
