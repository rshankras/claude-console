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
        private CodexBridgeStatus? _reportedBridgeStatus;

        /// <summary>
        /// Exact Codex/ChatGPT blue from the supplied Codex keypad artwork and designer icon pack.
        /// It lives with the product, never in Core: the engine draws state, the product supplies
        /// the identity it is drawn in.
        /// </summary>
        private static readonly BitmapColor CodexBlue = new BitmapColor(0x81, 0xA8, 0xED);

        public VizhiCodexPlugin()
        {
            PluginLog.Init(this.Log, "Vizhi for Codex");
            PluginResources.Init(this.Assembly);

            // This product's identity, declared here rather than picked inside the engine: Core
            // must stay neutral about whose keypad it is drawing. The exact Codex/ChatGPT blue
            // from the supplied keypad artwork and designer icon pack, on both the corner bracket
            // and the routed-session bar. State colour (Amber/Red/Green, the grey quiet bar) is
            // the engine's and is deliberately NOT overridden.
            KeyImage.UseIdentityIconFolder("icons_codex");
            KeyImage.UseIdentityColors(CodexBlue, CodexBlue);
            // Codex surfaces longer setup and trust failures than Claude Code does, and the words
            // are instructions ("Run /hooks"), so they are held long enough to read and act on.
            VoiceFailure.UseHold(8000);

            // Before any action is constructed — see the ordering note above.
            IpcPaths.UseProduct(this._agent.ProductSlug);
            BridgeManager.Instance.Agent = this._agent;
            BridgeManager.Instance.Notify = (status, message, url, title) =>
            {
                try
                {
                    if (message == null)
                    {
                        this.OnPluginStatusChanged(status, String.Empty);
                    }
                    else
                    {
                        this.OnPluginStatusChanged(status, message, url, title);
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, "VizhiCodexPlugin: could not post the plugin status");
                }
            };
        }

        public override void Load()
        {
            BridgeManager.Instance.PluginAssemblyFilePath = this.AssemblyFilePath;

            BridgeManager.Instance.Grid.OnGridChanged += this.OnGridChanged;
            BridgeManager.Instance.OnHelperHealthChanged += this.RefreshStateBridgeStatus;

            // Codex reports nothing until its hooks are installed AND trusted. Installing is ours;
            // trusting is the user's, in /hooks, and cannot be automated — so log which of the two
            // is outstanding rather than leaving a keypad that just looks broken.
            this.WireStateBridge();
            // Start only after WireStateBridge has established the initial UI state. The timer's
            // first callback is immediate; starting it earlier can race installation and flash a
            // false "Setup failed" card before EnsureInstalled has run (#69).
            BridgeManager.Instance.StartPolling();

            // No application registration to write, heal, or sweep: a universal plugin
            // (HasNoApplication), same decision and same reasoning as ClaudeConsolePlugin (#23).

            PluginLog.Info("VizhiCodexPlugin: Loaded — driving Codex CLI");
        }

        /// <summary>
        /// The Windows fallback transport. Official hooks provide exact lifecycle and approval
        /// events; rollout polling preserves discovery and activity when hooks are not yet trusted
        /// or an older Codex build does not run them.
        ///
        /// The bridge needs to know which sessions are live and when each started, to attach a
        /// rollout file to a key. Both are already in the key itself — Windows keys are
        /// "pid-&lt;pid&gt;-&lt;utcStartTicks&gt;" — so this reads the grid rather than asking the
        /// platform for a second process scan on every poll.
        /// </summary>
        private void WireRolloutBridge()
        {
            // Lay down sandbox grants so hook helpers can launch and write. Best effort: the group
            // exists only after codex's own setup has run (Platform.CodexSandboxAccess). Off the
            // Load path: the service fails a Load that exceeds 10s, and ACL propagation across
            // the Logi tree ate that whole budget on hardware — the 1.5.0 install failure.
            // Nothing in Load depends on these grants; they are for a future codex.
            _ = System.Threading.Tasks.Task.Run(Platform.CodexSandboxAccess.EnsureGranted);

            var bridge = new CodexRolloutBridge { RequireSessionDirectories = true };
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
                bridge.LiveSessionDirectories = manager.Grid?.DiscoveredProjectDirs;
                bridge.Poll();
            };

            PluginLog.Info("VizhiCodexPlugin: Windows — rollout recovery fallback enabled");
        }

        private void WireStateBridge()
        {
            if (OperatingSystem.IsWindows())
            {
                this.WireRolloutBridge();
            }

            var script = CodexCliAdapter.HookScriptContents();
            if (script == null)
            {
                // The launcher is embedded at build time; missing means a broken package, and the
                // plugin would otherwise run happily and silently never report a session.
                PluginLog.Info("VizhiCodexPlugin: codex-hook.sh is not embedded — state bridge cannot install");
                this.ReportBridgeProblem(AgentBridgeStatus.InstallFailed);
                return;
            }

            var bridge = this._agent.StateBridge;
            bridge.EnsureInstalled(script);

            this.RefreshStateBridgeStatus();
        }

        private void OnGridChanged()
        {
            // Active is evidence of an earlier delivery, not permanent health. Recheck after
            // later grid changes as well, and when helper health changes without a grid repaint.
            this.RefreshStateBridgeStatus();
        }

        private void RefreshStateBridgeStatus()
        {
            var bridge = this._agent.StateBridge;
            var status = bridge.StatusFor(OperatingSystem.IsWindows(), BridgeManager.Instance.HookHealth);
            if (_reportedBridgeStatus == status)
            {
                return;
            }

            _reportedBridgeStatus = status;

            switch (status)
            {
                case CodexBridgeStatus.AwaitingTrust:
                    this.ReportBridgeProblem(AgentBridgeStatus.AwaitingTrust);
                    PluginLog.Info(
                        "VizhiCodexPlugin: hooks installed but no event has arrived — run /hooks in Codex and " +
                        "trust the entry, then start a new session. (Never --dangerously-bypass-hook-trust.)");
                    break;

                case CodexBridgeStatus.ForeignHooksFile:
                    this.ReportBridgeProblem(AgentBridgeStatus.ForeignConfiguration);
                    PluginLog.Info(
                        $"VizhiCodexPlugin: {bridge.HooksFile} was not written by us and will not be touched — " +
                        "merge the hooks by hand to get live keys");
                    break;

                case CodexBridgeStatus.Active:
                    BridgeManager.Instance.SetAgentBridgeStatus(AgentBridgeStatus.Ready);
                    PluginLog.Info("VizhiCodexPlugin: state bridge active");
                    break;

                case CodexBridgeStatus.HelperUnavailable:
                    // The shared health monitor owns this warning and its recovery. Caching it
                    // as an unrelated product notice would leave it visible after recovery.
                    BridgeManager.Instance.SetAgentBridgeStatus(AgentBridgeStatus.HelperUnavailable);
                    break;

                default:
                    this.ReportBridgeProblem(AgentBridgeStatus.InstallFailed);
                    PluginLog.Info("VizhiCodexPlugin: state bridge is not installed");
                    break;
            }
        }

        private void ReportBridgeProblem(AgentBridgeStatus status)
        {
            BridgeManager.Instance.SetAgentBridgeStatus(status);
        }

        public override void Unload()
        {
            BridgeManager.Instance.Grid.OnGridChanged -= this.OnGridChanged;
            BridgeManager.Instance.OnHelperHealthChanged -= this.RefreshStateBridgeStatus;
            BridgeManager.Instance.StopPolling();
            PluginLog.Info("VizhiCodexPlugin: Unloaded");
        }
    }
}
