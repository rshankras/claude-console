namespace Loupedeck.ClaudeConsolePlugin.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Loupedeck.ClaudeConsolePlugin.Agents;
using Loupedeck.ClaudeConsolePlugin.Platform;
using Xunit;

public sealed class HelperHealthIntegrationTests : IDisposable
{
    private const String Session = "pid-500-638900000000000000";
    private readonly String root = Path.Combine(Path.GetTempPath(), "hook-integration-" + Guid.NewGuid().ToString("N"));
    private DateTime now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
    private String Helper => Path.Combine(root, "claude-console-hook.exe");

    public HelperHealthIntegrationTests() { Directory.CreateDirectory(root); }
    public void Dispose() { Directory.Delete(root, recursive: true); }

    private BridgeManager Rig(List<(PluginStatus Status, String Message)> notices)
    {
        File.WriteAllText(Helper, "fixture");
        File.SetLastWriteTimeUtc(Helper, now.AddHours(-1));
        return new BridgeManager(new PlatformSeamTests.FakePlatformBridge())
        {
            Agent = new CodexCliAdapter(),
            HookHealth = new WindowsHookHealth(Helper, root, "codex-console", () => now),
            Notify = (status, message, _, _) => notices.Add((status, message)),
        };
    }

    private void Receipt(BridgeManager bridge)
    {
        Directory.CreateDirectory(bridge.HookHealth.HealthDirectory);
        File.WriteAllText(Path.Combine(bridge.HookHealth.HealthDirectory, "success-" + Session + ".json"),
            JsonSerializer.Serialize(new { schema = 1, sessionKey = Session, @event = "Stop",
                startedUtcTicks = now.Ticks, completedUtcTicks = now.AddTicks(1).Ticks }));
    }

    [Fact]
    public void Missing_after_active_posts_once_and_recovers_only_after_new_delivery()
    {
        var notices = new List<(PluginStatus, String)>();
        var bridge = Rig(notices);
        Receipt(bridge);
        bridge.RefreshHelperHealth();
        Assert.Equal(AgentBridgeStatus.Ready, bridge.AgentBridgeState);
        notices.Clear();
        now = now.AddSeconds(1);
        File.Delete(Helper);
        bridge.RefreshHelperHealth();
        bridge.RefreshHelperHealth();
        Assert.Single(notices);
        Assert.Equal(PluginStatus.Warning, notices[0].Item1);
        Assert.Equal(AgentBridgeStatus.HelperUnavailable, bridge.AgentBridgeState);

        File.WriteAllText(Helper, "fixture");
        File.SetLastWriteTimeUtc(Helper, now.AddHours(-1));
        bridge.RefreshHelperHealth();
        Assert.Single(notices);
        Assert.Equal(AgentBridgeStatus.HelperUnavailable, bridge.AgentBridgeState);
        now = now.AddSeconds(1);
        Receipt(bridge);
        bridge.RefreshHelperHealth();
        Assert.Equal(AgentBridgeStatus.Ready, bridge.AgentBridgeState);
        Assert.Equal(PluginStatus.Normal, notices[^1].Item1);
        Assert.Equal(2, notices.Count);
    }

    [Fact]
    public void Recovery_preserves_an_unrelated_warning()
    {
        var notices = new List<(PluginStatus, String)>();
        var bridge = Rig(notices);
        Receipt(bridge);
        bridge.RefreshHelperHealth();
        bridge.Notify(PluginStatus.Warning, "Voice model download failed", "support", "Voice");
        now = now.AddSeconds(1);
        File.Delete(Helper);
        bridge.RefreshHelperHealth();
        File.WriteAllText(Helper, "fixture");
        File.SetLastWriteTimeUtc(Helper, now.AddHours(-1));
        now = now.AddSeconds(1);
        Receipt(bridge);
        bridge.RefreshHelperHealth();
        Assert.Equal((PluginStatus.Warning, "Voice model download failed"), notices[^1]);
    }

    [Fact]
    public void A_late_old_status_or_activity_payload_cannot_use_a_newer_events_receipt()
    {
        var bridge = Rig(new List<(PluginStatus, String)>());
        var oldStart = now.AddSeconds(-1);
        Receipt(bridge);
        bridge.RefreshHelperHealth();
        File.Delete(Helper);
        bridge.RefreshHelperHealth();
        File.WriteAllText(Helper, "fixture");
        File.SetLastWriteTimeUtc(Helper, now.AddHours(-1));
        now = now.AddSeconds(1);
        Receipt(bridge);
        bridge.RefreshHelperHealth();
        Assert.True(bridge.IsSessionObservationCurrent(Session));
        Assert.False(bridge.IsObservationPayloadCurrent(JsonSerializer.Serialize(new
            { hookStartedUtcTicks = oldStart.Ticks, state = "waiting" }), Session));
        Assert.False(bridge.IsObservationPayloadCurrent("{\"state\":\"waiting\"}", Session));
        Assert.True(bridge.IsObservationPayloadCurrent(JsonSerializer.Serialize(new
            { hookStartedUtcTicks = now.Ticks, state = "done" }), Session));
    }

    [Fact]
    public void Codex_status_transitions_preserve_unrelated_notices()
    {
        var notices = new List<(PluginStatus, String)>();
        var bridge = Rig(notices);
        Receipt(bridge);
        bridge.RefreshHelperHealth();
        bridge.Notify(PluginStatus.Warning, "Voice model download failed", "support", "Voice");
        bridge.SetAgentBridgeStatus(AgentBridgeStatus.HelperUnavailable);
        bridge.SetAgentBridgeStatus(AgentBridgeStatus.AwaitingTrust);
        bridge.SetAgentBridgeStatus(AgentBridgeStatus.Ready);
        Assert.Equal((PluginStatus.Warning, "Voice model download failed"), notices[^1]);
    }

    [Fact]
    public void Initial_missing_event_preserves_trust_guidance_but_real_disappearance_overrides_it()
    {
        var notices = new List<(PluginStatus, String)>();
        var bridge = Rig(notices);
        bridge.SetAgentBridgeStatus(AgentBridgeStatus.AwaitingTrust);
        bridge.RefreshHelperHealth();
        Assert.Equal(AgentBridgeStatus.AwaitingTrust, bridge.AgentBridgeState);
        File.Delete(Helper);
        bridge.RefreshHelperHealth();
        Assert.Equal(AgentBridgeStatus.HelperUnavailable, bridge.AgentBridgeState);
    }

    [Fact]
    public void A_present_helper_with_no_evidence_yet_is_not_reported_as_a_failure()
    {
        // Fresh install, new day, or no session open: the bridge is Ready, nothing has failed.
        // No Blocked on the keys and no "contact IT" in Options+ until something actually fails.
        var notices = new List<(PluginStatus, String)>();
        var bridge = Rig(notices);
        bridge.SetAgentBridgeStatus(AgentBridgeStatus.Ready);
        bridge.RefreshHelperHealth();
        var gate = new LiveStatusGate(bridge, "Context", () => { });

        Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, bridge.HookHealth.Status);
        Assert.False(bridge.HookHelperUnavailable);
        Assert.Equal(AgentBridgeStatus.Ready, bridge.AgentBridgeState);
        Assert.Null(gate.Label);
        Assert.DoesNotContain(notices, n => n.Item1 == PluginStatus.Warning);
        // Actions still need per-session proof; they just refuse quietly.
        Assert.False(bridge.IsSessionObservationCurrent(Session));
    }

    [Fact]
    public void Explicitly_disabled_Claude_live_status_does_not_report_a_hook_failure()
    {
        var notices = new List<(PluginStatus, String)>();
        var bridge = Rig(notices);
        bridge.Agent = new ClaudeCodeAdapter(); // Not enabled: no helper is needed yet.
        File.Delete(Helper);
        bridge.RefreshHelperHealth();
        Assert.Equal(AgentBridgeStatus.Ready, bridge.AgentBridgeState);
        Assert.DoesNotContain(notices, n => n.Item1 == PluginStatus.Warning);
    }

    [Fact]
    public void Codex_status_rejects_historical_active_when_the_expected_executable_is_missing()
    {
        var home = Path.Combine(root, "codex");
        var sessions = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessions);
        File.WriteAllText(Helper, "fixture");
        File.SetLastWriteTimeUtc(Helper, now.AddHours(-1));
        var bridge = new CodexStateBridge(home, sessions) { HookExe = Helper };
        bridge.EnsureInstalled("#!/bin/sh\n");
        File.SetLastWriteTimeUtc(bridge.HooksFile, now.AddHours(-1));
        var state = Path.Combine(sessions, Session + ".json");
        File.WriteAllText(state, "{\"event\":\"Stop\",\"payload\":{}}");
        File.SetLastWriteTimeUtc(state, now);
        Assert.Equal(CodexBridgeStatus.Active, bridge.StatusFor(windows: true));
        File.Delete(Helper);
        Assert.Equal(CodexBridgeStatus.HelperUnavailable, bridge.StatusFor(windows: true));
    }

    [Fact]
    public void Codex_keeps_fresh_hook_proof_when_rollout_replaces_the_envelope_but_not_after_reinstallation()
    {
        var manager = Rig(new List<(PluginStatus, String)>());
        Receipt(manager);
        manager.RefreshHelperHealth();
        var sessions = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessions);
        var codex = new CodexStateBridge(Path.Combine(root, "codex"), sessions) { HookExe = Helper };
        codex.EnsureInstalled("#!/bin/sh\n");
        File.SetLastWriteTimeUtc(codex.HooksFile, now.AddMinutes(-1));
        File.WriteAllText(Path.Combine(sessions, Session + ".json"),
            "{\"transport\":\"rollout\",\"event\":\"Stop\",\"payload\":{}}");

        Assert.Equal(CodexBridgeStatus.Active, codex.StatusFor(true, manager.HookHealth));
        File.SetLastWriteTimeUtc(codex.HooksFile, now.AddSeconds(1));
        Assert.Equal(CodexBridgeStatus.AwaitingTrust, codex.StatusFor(true, manager.HookHealth));
        File.Delete(Helper);
        manager.RefreshHelperHealth();
        Assert.Equal(CodexBridgeStatus.HelperUnavailable, codex.StatusFor(true, manager.HookHealth));
    }

    [Fact]
    public void A_missing_packaged_Codex_executable_does_not_change_the_installed_command_path()
    {
        var previous = PluginPaths.PluginAssemblyFilePath;
        try
        {
            PluginPaths.PluginAssemblyFilePath = Path.Combine(root, "VizhiCodexPlugin.dll");
            var bridge = new CodexStateBridge(Path.Combine(root, "codex"), Path.Combine(root, "sessions"));
            File.WriteAllText(Helper, "fixture");
            var installed = bridge.HookCommand("PermissionRequest", windows: true);
            File.Delete(Helper);
            Assert.Equal(installed, bridge.HookCommand("PermissionRequest", windows: true));
            Assert.Contains(Helper.Replace("'", "''"), installed);
        }
        finally { PluginPaths.PluginAssemblyFilePath = previous; }
    }
}
