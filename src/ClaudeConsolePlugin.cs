namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

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
        }

        public override void Load()
        {
            // Hand the SDK's real on-disk plugin path to the bridge (Assembly.Location is empty in
            // the SDK's load context) so it can locate the in-package voice payload on first use.
            BridgeManager.Instance.PluginAssemblyFilePath = this.AssemblyFilePath;

            // All actions are auto-discovered; we just start the IPC bridge.
            BridgeManager.Instance.StartPolling();

            // Self-install the status-line + activity scripts and wire them into ~/.claude/settings.json
            // so the live keys (Cost / Context / Activity) work with zero user setup. Idempotent, runs
            // on a background thread, and takes effect on the user's next Claude Code session.
            BridgeManager.Instance.EnsureBridgeAutoWired();

            PluginLog.Info("ClaudeConsolePlugin: Loaded — actions auto-discovered; bridge polling started");
        }

        public override void Unload()
        {
            BridgeManager.Instance.StopPolling();
            PluginLog.Info("ClaudeConsolePlugin: Unloaded");
        }
    }
}
