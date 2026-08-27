namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Collections.Generic;

    using Loupedeck.ClaudeConsolePlugin.Agents;
    using Loupedeck.ClaudeConsolePlugin.Models;

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
                // Marketplace-distributed: never restart the service on a stale-timestamp guess.
                // The installer writes the registration before it finishes copying the payload, so
                // the timestamps ALWAYS look desynced on a healthy install (#20). A genuinely
                // missing entry is still written by RegisterIfMissing above; a stale one is
                // repaired by hand with scripts/repair-registration.sh.
                Platform.RegistrationHeal.HealIfNeeded(automaticRestartAllowed: false);
            }

            PluginLog.Info("VizhiCodexPlugin: Loaded — driving Codex CLI");
        }

        /// <summary>
        /// The Windows transport: pull state from codex's rollout transcript on every poll, since
        /// its hook runner never spawns a process to push it (docs/spike-windows-codex-hooks.md).
        ///
        /// The bridge needs to know which sessions are live and when each started, to attach a
        /// rollout file to a key. Both are already in the key itself — Windows keys are
        /// "pid-&lt;pid&gt;-&lt;utcStartTicks&gt;" — so this reads the grid rather than asking the
        /// platform for a second process scan on every poll.
        /// </summary>
        private void WireRolloutBridge()
        {
            // Lay down the sandbox grants, so the day codex's hook runner is fixed the hooks can
            // both LAUNCH and WRITE without a plugin update. Best effort: the sandbox group
            // exists only after codex's own setup has run (Platform.CodexSandboxAccess). Off the
            // Load path: the service fails a Load that exceeds 10s, and ACL propagation across
            // the Logi tree ate that whole budget on hardware — the 1.5.0 install failure.
            // Nothing in Load depends on these grants; they are for a future codex.
            _ = System.Threading.Tasks.Task.Run(Platform.CodexSandboxAccess.EnsureGranted);

            var bridge = new CodexRolloutBridge();
            var manager = BridgeManager.Instance;

            manager.PullState = () =>
            {
                var live = new List<(String Key, DateTime Start)>();
                var sessions = manager.Grid?.LiveSessions();
                foreach (var session in sessions ?? new List<GridSession>())
                {
                    if (Platform.WindowsInjection.TryParseSessionKey(session.SessionKey, out _, out var ticks))
                    {
                        live.Add((session.SessionKey, new DateTime(ticks, DateTimeKind.Utc)));
                    }
                }

                bridge.LiveSessions = live;
                bridge.Poll();
            };

            PluginLog.Info(
                "VizhiCodexPlugin: Windows — reading state from codex's rollout transcript (no hooks; " +
                "codex's hook runner spawns nothing on this platform)");
        }

        private void WireStateBridge()
        {
            // Windows takes the hook-free path: codex's hook runner creates no process there, so
            // there is nothing to install and no trust to ask for. State arrives from the rollout
            // stream instead (docs/windows-codex-hookless-bridge.md).
            if (OperatingSystem.IsWindows())
            {
                this.WireRolloutBridge();
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
