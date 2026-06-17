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
    ///   Live displays: Model, Cost, Status   (read state.json — no terminal needed)
    ///   Controls:      Plan, Compact, Context, Voice
    ///   Prompts:       Fix Bug, Write Tests
    ///   Git:           Commit, Diff
    ///   (Accept / Reject ship too, but stay inert until the PreToolUse hook is wired.)
    ///
    /// The dial adjustments (history scroll / session switch) compile and are ready for the MX
    /// Creative Dialpad, but are unbindable on the Keypad-only device.
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
            // All actions are auto-discovered; we just start the IPC bridge.
            BridgeManager.Instance.StartPolling();
            PluginLog.Info("ClaudeConsolePlugin: Loaded — actions auto-discovered; bridge polling started");
        }

        public override void Unload()
        {
            BridgeManager.Instance.StopPolling();
            PluginLog.Info("ClaudeConsolePlugin: Unloaded");
        }
    }
}
