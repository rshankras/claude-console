namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Agents;

    /// <summary>
    /// Claude Console — Logitech MX Creative Keypad plugin for Claude Code.
    /// Physical LCD-key controls for AI-assisted coding, bridged to Claude Code over file IPC.
    ///
    /// Commands and adjustments are AUTO-DISCOVERED by the SDK — every PluginDynamicCommand /
    /// PluginDynamicAdjustment subclass with a parameterless constructor is registered
    /// automatically. They reach the shared IPC bridge via BridgeManager.Instance, so Load()
    /// only has to start the bridge polling Claude Code's state.
    ///
    /// v1 "Core essentials" actions:
    ///   Live displays: Model, Cost, Activity (read state.json — no terminal needed)
    ///   Controls:      Plan, Compact, Context, Voice
    ///   Prompts:       Fix Bug, Write Tests
    ///   Git:           Commit, Diff
    /// </summary>
    public class ClaudeConsolePlugin : Plugin
    {
        public override Boolean UsesApplicationApiOnly => true;
        public override Boolean HasNoApplication => true;

        public ClaudeConsolePlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);

            // Declared HERE, not in Load(): the SDK constructs every action in between, and an
            // action reads the agent to decide which keys to add and the product to resolve its
            // IPC paths. Declaring late would build the keys against no agent at all.
            var agent = new ClaudeCodeAdapter();
            IpcPaths.UseProduct(agent.ProductSlug);
            BridgeManager.Instance.Agent = agent;

            // The engine composes what the user should be told about an edit to their settings; only
            // this class can put it in front of them (Options+'s message centre, with a link) — #31.
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
                    PluginLog.Warning(ex, "ClaudeConsolePlugin: could not post the plugin status");
                }
            };
        }

        public override void Load()
        {
            // Hand the SDK's real on-disk plugin path to the bridge (Assembly.Location is empty in
            // the SDK's load context) so it can locate the in-package voice payload on first use.
            BridgeManager.Instance.PluginAssemblyFilePath = this.AssemblyFilePath;

            // All actions are auto-discovered; we just start the IPC bridge.
            BridgeManager.Instance.StartPolling();

            // Self-install the status-line + activity scripts (the plugin's own folder), honour an Off
            // marker, and read what settings.json says about the live keys. It never edits that file:
            // the user does, by pressing Enable Live Status (#31). Background thread, idempotent.
            BridgeManager.Instance.EnsureBridgeAutoWired();

            // No application registration to write, heal, or sweep: this is a universal plugin
            // (HasNoApplication in the package yaml), decided with Logitech on 2026-08-28 (#23).
            // The keypad layout is a profile the user imports or builds, on Options+'s own entry
            // for Terminal — not something the package carries. Everything that used to happen here
            // (SelfRegistration, RegistrationHeal, RegistrationCleanup, and the service restarts they
            // scheduled) existed only to manage an entry this plugin no longer has.

            PluginLog.Info("ClaudeConsolePlugin: Loaded — actions auto-discovered; bridge polling started");
        }

        public override void Unload()
        {
            BridgeManager.Instance.StopPolling();
            PluginLog.Info("ClaudeConsolePlugin: Unloaded");
        }
    }
}
