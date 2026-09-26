namespace Loupedeck.ClaudeConsolePlugin.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Loupedeck.ClaudeConsolePlugin.Actions;
using Loupedeck.ClaudeConsolePlugin.Agents;
using Loupedeck.ClaudeConsolePlugin.Platform;
using Xunit;

// Real files, registry parsing and action delivery; only the OS injection boundary is faked.
public sealed class HelperHealthActionTests : IDisposable
{
    private const string First = "pid-101-1001";
    private const string Second = "pid-202-2002";
    private readonly string root = Path.Combine(Path.GetTempPath(), "helper-actions-" + Guid.NewGuid().ToString("N"));
    private readonly HashSet<string> live = new() { First, Second };
    private DateTime now = new(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc);
    private string Sessions => Path.Combine(root, "sessions");
    private string Activity => Path.Combine(root, "activity");
    private string Helper => Path.Combine(root, "claude-console-hook.exe");

    public HelperHealthActionTests()
    {
        Directory.CreateDirectory(Sessions);
        Directory.CreateDirectory(Activity);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private void RestoreHelper()
    {
        File.WriteAllText(Helper, "test helper");
        File.SetLastWriteTimeUtc(Helper, now.AddHours(-1));
    }

    private void WriteCodex(string key, string kind, DateTime written, DateTime? started = null)
    {
        var path = Path.Combine(Sessions, key + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schema = 1, agent = "codex-cli", @event = kind,
            hookStartedUtcTicks = (started ?? written).Ticks,
            ts = new DateTimeOffset(written).ToUnixTimeSeconds(),
            payload = new { session_id = key, cwd = "/projects/" + key, tool_name = "Bash", tool_input = new { command = "echo test" } },
        }));
        File.SetLastWriteTimeUtc(path, written);
    }

    private void Receipt(WindowsHookHealth health, string key, DateTime started)
    {
        Directory.CreateDirectory(health.HealthDirectory);
        File.WriteAllText(Path.Combine(health.HealthDirectory, "success-" + key + ".json"), JsonSerializer.Serialize(new
        {
            schema = 1, sessionKey = key, @event = "PermissionRequest",
            startedUtcTicks = started.Ticks, completedUtcTicks = started.AddMilliseconds(10).Ticks,
        }));
    }

    private (BridgeManager Bridge, PlatformSeamTests.FakePlatformBridge Platform, WindowsHookHealth Health) Rig()
    {
        RestoreHelper();
        WriteCodex(First, "PermissionRequest", now.AddMinutes(-2));
        WriteCodex(Second, "Stop", now.AddMinutes(-2));
        var agent = new CodexCliAdapter();
        var grid = new SessionRegistry(Sessions, Activity, Path.Combine(root, "registry.json")) { Agent = agent };
        grid.Refresh(live);
        var platform = new PlatformSeamTests.FakePlatformBridge();
        var health = new WindowsHookHealth(Helper, root, "codex-console", () => now);
        Receipt(health, First, now.AddMinutes(-1));
        Receipt(health, Second, now.AddMinutes(-1));
        var bridge = new BridgeManager(platform) { Agent = agent, Grid = grid, HookHealth = health };
        bridge.SelectSlot(grid.SlotSession(1).SessionKey == First ? 1 : 2);
        bridge.RefreshHelperHealth();
        return (bridge, platform, health);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_missing_helper_is_detected_on_the_answer_press_and_sends_no_key(bool approve)
    {
        var (bridge, platform, _) = Rig();
        Assert.True(AnswerCommand.TargetState(bridge).HasPending);

        File.Delete(Helper); // No poll or repaint between disappearance and this press.
        AnswerCommand.AnswerApproval(bridge, approve);

        Assert.Equal(AgentBridgeStatus.HelperUnavailable, bridge.AgentBridgeState);
        Assert.Empty(platform.Keys);
        Assert.Empty(platform.Texts);
        Assert.True(platform.Alerts > 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Another_session_recovering_and_a_fresh_status_update_do_not_revalidate_an_old_approval(bool approve)
    {
        var (bridge, platform, health) = Rig();
        File.Delete(Helper);
        bridge.RefreshHelperHealth();
        now = now.AddSeconds(10);
        RestoreHelper();
        Receipt(health, Second, now);
        bridge.RefreshHelperHealth();

        Assert.False(bridge.HookHelperUnavailable); // The helper works again, but not for this target yet.
        Assert.True(AnswerCommand.TargetState(bridge).NeedsObservation);
        Assert.Equal(ApprovalRisk.None, AnswerCommand.TargetState(bridge).Risk);
        AnswerCommand.AnswerApproval(bridge, approve);
        Assert.Empty(platform.Keys);

        // The hook has delivered new bytes, but the grid still holds the previous parsed
        // approval. A fresh receipt/file timestamp must not bless that old snapshot.
        now = now.AddSeconds(1);
        WriteCodex(First, "PermissionRequest", now);
        Receipt(health, First, now);
        bridge.RefreshHelperHealth();
        Assert.True(bridge.IsSessionObservationCurrent(First));
        Assert.True(AnswerCommand.TargetState(bridge).NeedsObservation);
        AnswerCommand.AnswerApproval(bridge, approve);
        Assert.Empty(platform.Keys);

        now = now.AddSeconds(1);
        WriteCodex(First, "PermissionRequest", now);
        bridge.Grid.Refresh(live);
        Receipt(health, First, now);
        bridge.RefreshHelperHealth();
        Assert.True(AnswerCommand.TargetState(bridge).HasPending);
        AnswerCommand.AnswerApproval(bridge, approve);
        Assert.Equal((First, approve ? KeyStroke.Return : KeyStroke.Escape), Assert.Single(platform.Keys));
        Assert.Null(bridge.Grid.Sessions[First].ApprovalObservedAtUtc);
    }

    [Fact]
    public void Codex_live_keys_show_and_repaint_helper_failure_without_a_settings_switch()
    {
        var (bridge, _, _) = Rig();
        var repaints = 0;
        var gate = new LiveStatusGate(bridge, "Context", () => repaints++);
        Assert.Null(gate.Label);

        File.Delete(Helper);
        gate.HandleButton(DeviceButtonEventType.Press, () => { });

        Assert.Equal("Blocked", gate.Label);
        Assert.True(repaints > 0);
        Assert.False(gate.NeedsSetup);
        Assert.False(gate.Armed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_invocation_started_before_failure_cannot_rearm_an_approval_by_finishing_late(bool approve)
    {
        var (bridge, platform, health) = Rig();
        var oldInvocation = now.AddSeconds(-1);
        File.Delete(Helper);
        bridge.RefreshHelperHealth();
        now = now.AddSeconds(10);
        RestoreHelper();
        WriteCodex(First, "PermissionRequest", now, started: oldInvocation);
        bridge.Grid.Refresh(live);
        Receipt(health, First, now); // A later invocation proves execution, not this old approval.
        bridge.RefreshHelperHealth();

        Assert.True(bridge.IsSessionObservationCurrent(First));
        Assert.False(bridge.IsSessionApprovalCurrent(First));
        Assert.False(bridge.IsSessionActivityCurrent(First));
        Assert.True(AnswerCommand.TargetState(bridge).NeedsObservation);
        AnswerCommand.AnswerApproval(bridge, approve);
        Assert.Empty(platform.Keys);
    }

    [Theory]
    [InlineData("PermissionRequest")]
    [InlineData("Stop")]
    [InlineData(null)]
    public void A_pending_source_replaced_or_deleted_after_the_grid_poll_cannot_be_answered(string nextEvent)
    {
        var (bridge, platform, health) = Rig();
        Assert.True(AnswerCommand.TargetState(bridge).HasPending);
        now = now.AddSeconds(1);
        if (nextEvent == null)
        {
            File.Delete(Path.Combine(Sessions, First + ".json"));
        }
        else
        {
            WriteCodex(First, nextEvent, now);
        }
        Receipt(health, First, now);
        bridge.RefreshHelperHealth(); // Deliberately leave the grid's earlier snapshot untouched.

        AnswerCommand.AnswerApproval(bridge, approve: true);

        Assert.Empty(platform.Keys);
        Assert.NotNull(bridge.Grid.Sessions[First].PendingTool);
    }

    [Fact]
    public void A_session_that_has_not_reported_since_recovery_is_withheld_not_marked_blocked()
    {
        // The helper failed, then Second delivered a fresh hook: the helper works. First has
        // not reported since. Its stale values are withheld (dash), but "Blocked" would claim a
        // failure that is not there — and nothing says so in Options+ either.
        var (bridge, _, health) = Rig();
        var notices = new List<(PluginStatus Status, string Message)>();
        bridge.Notify = (status, message, _, _) => notices.Add((status, message));
        File.Delete(Helper);
        bridge.RefreshHelperHealth();
        Assert.Equal(PluginStatus.Warning, notices[^1].Status);
        now = now.AddSeconds(10);
        RestoreHelper();
        WriteCodex(Second, "Stop", now);
        bridge.Grid.Refresh(live);
        Receipt(health, Second, now);
        bridge.RefreshHelperHealth();
        bridge.ActiveTty = Second;
        Assert.Equal(First, bridge.RoutingTty());
        var context = new LiveStatusGate(bridge, "Context", () => { });
        var activity = new LiveStatusGate(bridge, "Activity", () => { });

        Assert.False(bridge.HookHelperUnavailable);
        Assert.Equal(PluginStatus.Normal, notices[^1].Status);
        Assert.Null(context.Label);
        Assert.Null(activity.Label);
        Assert.False(bridge.IsSessionActivityCurrent(First));
        Assert.True(bridge.IsSessionActivityCurrent(Second));

        now = now.AddSeconds(1);
        WriteCodex(First, "Stop", now);
        bridge.Grid.Refresh(live);
        Receipt(health, First, now);
        bridge.RefreshHelperHealth();
        Assert.True(bridge.IsSessionActivityCurrent(First));
        Assert.Null(activity.Label);
    }

    [Fact]
    public void A_stale_pending_approval_shows_no_pending_and_refuses_the_press()
    {
        // First's approval was observed before the helper failed. After recovery by Second the
        // face is the ordinary no-pending tile (risk None), and a press sends nothing.
        var (bridge, platform, health) = Rig();
        File.Delete(Helper);
        bridge.RefreshHelperHealth();
        now = now.AddSeconds(10);
        RestoreHelper();
        Receipt(health, Second, now);
        bridge.RefreshHelperHealth();

        var decision = AnswerCommand.TargetState(bridge);
        Assert.Equal(First, decision.Key);
        Assert.False(decision.HasPending);
        Assert.Equal(ApprovalRisk.None, decision.Risk);
        Assert.True(decision.NeedsObservation);
        AnswerCommand.AnswerApproval(bridge, approve: true);
        Assert.Empty(platform.Keys);
    }

    [Fact]
    public void Pressing_Yes_while_helper_is_missing_does_not_leave_its_warning_after_recovery()
    {
        var (bridge, _, health) = Rig();
        var notices = new List<(PluginStatus Status, string Message)>();
        bridge.Notify = (status, message, _, _) => notices.Add((status, message));
        File.Delete(Helper);
        AnswerCommand.AnswerApproval(bridge, approve: true);
        Assert.Equal(PluginStatus.Warning, notices[^1].Status);

        now = now.AddSeconds(10);
        RestoreHelper();
        WriteCodex(First, "Stop", now);
        bridge.Grid.Refresh(live);
        Receipt(health, First, now);
        bridge.RefreshHelperHealth();

        Assert.Equal(PluginStatus.Normal, notices[^1].Status);
        Assert.Null(notices[^1].Message);
    }

    [Fact]
    public void Claude_approval_timestamp_is_its_pending_file_not_a_newer_statusline()
    {
        var pendingAt = now.AddMinutes(-5);
        var statePath = Path.Combine(Sessions, First + ".json");
        var pendingPath = Path.Combine(Activity, "pending-" + First + ".json");
        File.WriteAllText(statePath, "{\"session_id\":\"sid\",\"workspace\":{\"project_dir\":\"/project\"}}");
        File.SetLastWriteTimeUtc(statePath, now);
        File.WriteAllText(Path.Combine(Activity, First + ".json"), "{\"state\":\"waiting\",\"ts\":1}");
        File.WriteAllText(pendingPath, "{\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"echo test\"}}");
        File.SetLastWriteTimeUtc(pendingPath, pendingAt);
        var grid = new SessionRegistry(Sessions, Activity, Path.Combine(root, "registry.json")) { Agent = new ClaudeCodeAdapter() };
        grid.Refresh(new HashSet<string> { First });

        Assert.Equal(now, grid.Sessions[First].UpdatedAt);
        Assert.Equal(pendingAt, grid.Sessions[First].ApprovalObservedAtUtc);
        Assert.True(grid.ClearPendingApproval(First));
        Assert.Null(grid.Sessions[First].ApprovalObservedAtUtc);
    }
}
