namespace Loupedeck.ClaudeConsolePlugin.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Loupedeck.ClaudeConsolePlugin.Actions;
using Loupedeck.ClaudeConsolePlugin.Agents;
using Loupedeck.ClaudeConsolePlugin.Platform;
using Xunit;

// Exercise Codex envelopes through the real grid, routing, and answer delivery. Only the OS
// injection/focus boundary is faked; no installed hooks, settings, or live IPC are touched.
public class VizhiIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vizhi-integration-" + Guid.NewGuid().ToString("N"));
    private readonly HashSet<string> live = new() { "pid-101-a", "pid-202-b" };
    public void Dispose() => Directory.Delete(root, true);

    private (BridgeManager Bridge, PlatformSeamTests.FakePlatformBridge Platform) Rig(bool selectPending = true)
    {
        Directory.CreateDirectory(Path.Combine(root, "sessions"));
        Directory.CreateDirectory(Path.Combine(root, "activity"));
        Write("pid-101-a", "Stop");
        Write("pid-202-b", "PermissionRequest");
        var agent = new CodexCliAdapter();
        var grid = new SessionRegistry(Path.Combine(root, "sessions"), Path.Combine(root, "activity"), Path.Combine(root, "registry.json")) { Agent = agent };
        grid.Refresh(live);
        var platform = new PlatformSeamTests.FakePlatformBridge();
        var bridge = new BridgeManager(platform) { Agent = agent, Grid = grid };
        if (selectPending) bridge.SelectSlot(PendingSlot(bridge));
        return (bridge, platform);
    }

    private static int PendingSlot(BridgeManager bridge) =>
        Enumerable.Range(1, SessionRegistry.SlotCount).Single(slot =>
            bridge.Grid.SlotSession(slot)?.SessionKey == "pid-202-b");

    private void Write(string key, string kind) => File.WriteAllText(Path.Combine(root, "sessions", key + ".json"), JsonSerializer.Serialize(new
    {
        schema = 1, agent = "codex-cli", @event = kind,
        ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        payload = new { session_id = key, cwd = "/projects/" + key, tool_name = "Bash", tool_input = new { command = "git push" } },
    }));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Answers_route_to_the_selected_codex_session_and_clear_only_after_delivery(bool approve)
    {
        var (bridge, platform) = Rig();
        // The badge and delivery agree on the explicitly selected session.
        Assert.Equal("pid-202-b", bridge.RoutingTty());
        AnswerCommand.AnswerApproval(bridge, approve);
        Assert.Equal(("pid-202-b", approve ? KeyStroke.Return : KeyStroke.Escape), Assert.Single(platform.Keys));
        Assert.Empty(platform.Texts);
        Assert.Null(bridge.Grid.Sessions["pid-202-b"].PendingTool);
        bridge.Grid.Refresh(live); // stale on-disk hook must not relight the answered prompt
        Assert.Null(bridge.Grid.Sessions["pid-202-b"].PendingTool);
        Assert.Null(bridge.Grid.Sessions["pid-101-a"].PendingTool);
    }

    [Theory]
    [InlineData(InjectionOutcome.Failed)]
    [InlineData(InjectionOutcome.SessionMissing)]
    [InlineData(InjectionOutcome.SessionElevated)]
    public void Failed_answer_delivery_preserves_the_codex_approval(InjectionOutcome outcome)
    {
        var (bridge, platform) = Rig();
        platform.Outcome = outcome;
        AnswerCommand.AnswerApproval(bridge, false);
        Assert.Equal(("pid-202-b", KeyStroke.Escape), Assert.Single(platform.Keys));
        bridge.Grid.Refresh(live);
        Assert.Equal("Bash", bridge.Grid.Sessions["pid-202-b"].PendingTool);
        Assert.Equal("waiting", bridge.Grid.Sessions["pid-202-b"].State);
    }

    [Theory]
    [InlineData((int)AgentBridgeStatus.AwaitingTrust)]
    [InlineData((int)AgentBridgeStatus.InstallFailed)]
    [InlineData((int)AgentBridgeStatus.ForeignConfiguration)]
    public void Setup_problem_blocks_an_answer_even_with_a_stale_pending_envelope(int status)
    {
        var (bridge, platform) = Rig();
        bridge.SetAgentBridgeStatus((AgentBridgeStatus)status);
        AnswerCommand.AnswerApproval(bridge, true);
        Assert.Empty(platform.Keys);
        Assert.Equal("Bash", bridge.Grid.Sessions["pid-202-b"].PendingTool);
        bridge.SetAgentBridgeStatus(AgentBridgeStatus.Ready);
        AnswerCommand.AnswerApproval(bridge, false);
        Assert.Equal(("pid-202-b", KeyStroke.Escape), Assert.Single(platform.Keys));
    }

    [Fact]
    public void Failed_codex_slot_focus_preserves_the_pin_and_subsequent_answer_target()
    {
        var (bridge, platform) = Rig();
        var pendingSlot = bridge.Grid.SlotSession(1).SessionKey == "pid-202-b" ? 1 : 2;
        platform.FocusSucceeds = false;
        bridge.SelectSlot(pendingSlot == 1 ? 2 : 1);
        Assert.Equal("pid-202-b", bridge.PinnedTty);
        Assert.Equal("pid-202-b", bridge.Grid.FocusedSession);
        AnswerCommand.AnswerApproval(bridge, false);
        Assert.Equal(("pid-202-b", KeyStroke.Escape), Assert.Single(platform.Keys));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_unselected_pending_session_neither_lights_nor_receives_an_answer(bool approve)
    {
        var (bridge, platform) = Rig(selectPending: false);
        // Generic routing still sees the request; approval routing must not adopt that guess.
        Assert.Equal("pid-202-b", bridge.RoutingTty());
        var decision = AnswerCommand.TargetState(bridge);
        Assert.True(decision.NeedsSelection);
        Assert.Null(decision.Key);
        Assert.Equal(ApprovalRisk.None, decision.Risk);

        AnswerCommand.AnswerApproval(bridge, approve);
        Assert.Empty(platform.Keys);
        Assert.Equal("Bash", bridge.Grid.Sessions["pid-202-b"].PendingTool);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    public void Extra_session_slots_require_selection_and_identify_the_approval_target(int slot, bool approve)
    {
        var (bridge, platform) = Rig(selectPending: false);
        Write("pid-202-b", "Stop");
        for (var i = 3; i <= slot; i++)
        {
            var key = "extra-" + i;
            Write(key, i == slot ? "PermissionRequest" : "Stop");
            live.Add(key);
            bridge.Grid.Refresh(live);
        }
        var pending = bridge.Grid.SlotSession(slot);
        Assert.Equal("extra-" + slot, pending.SessionKey);
        AnswerCommand.AnswerApproval(bridge, approve);
        Assert.Empty(platform.Keys);
        Assert.True(AnswerCommand.TargetState(bridge).NeedsSelection);

        bridge.SelectSlot(slot);
        var decision = AnswerCommand.TargetState(bridge);
        Assert.False(decision.NeedsSelection);
        Assert.Equal(pending.SessionKey, decision.Key);
        Assert.Equal($"{slot}: extra-{slot}", decision.Label);
        Assert.Equal(ApprovalRisk.High, decision.Risk);
        AnswerCommand.AnswerApproval(bridge, approve);
        Assert.Equal((pending.SessionKey, approve ? KeyStroke.Return : KeyStroke.Escape), Assert.Single(platform.Keys));
    }

    [Fact]
    public void Losing_the_selection_does_not_arm_another_session_even_when_only_one_remains()
    {
        var (bridge, platform) = Rig();
        Assert.Equal("pid-202-b", AnswerCommand.TargetState(bridge).Key);
        Write("pid-101-a", "PermissionRequest");
        live.Remove("pid-202-b");
        bridge.Grid.Refresh(live);
        bridge.RoutingTty(); // discovery/routing also clears the departed pin

        Assert.True(AnswerCommand.TargetState(bridge).NeedsSelection);
        AnswerCommand.AnswerApproval(bridge, true);
        Assert.Empty(platform.Keys);
    }

    [Fact]
    public void Releasing_the_pin_requires_another_deliberate_selection()
    {
        var (bridge, platform) = Rig();
        Assert.Equal("pid-202-b", AnswerCommand.TargetState(bridge).Key);
        bridge.SelectSlot(PendingSlot(bridge));
        AnswerCommand.AnswerApproval(bridge, true);
        Assert.True(AnswerCommand.TargetState(bridge).NeedsSelection);
        Assert.Empty(platform.Keys);
    }

    [Fact]
    public void Failed_initial_focus_does_not_enable_approval()
    {
        var (bridge, platform) = Rig(selectPending: false);
        platform.FocusSucceeds = false;
        bridge.SelectSlot(PendingSlot(bridge));
        AnswerCommand.AnswerApproval(bridge, true);
        Assert.Empty(platform.Keys);
        Assert.True(AnswerCommand.TargetState(bridge).NeedsSelection);
    }

    [Fact]
    public void Selecting_an_idle_session_does_not_answer_another_sessions_approval()
    {
        var (bridge, platform) = Rig(selectPending: false);
        bridge.SelectSlot(PendingSlot(bridge) == 1 ? 2 : 1);
        var decision = AnswerCommand.TargetState(bridge);
        Assert.Equal("pid-101-a", decision.Key);
        Assert.Equal(ApprovalRisk.None, decision.Risk);
        AnswerCommand.AnswerApproval(bridge, true);
        Assert.Empty(platform.Keys);
        Assert.Equal("Bash", bridge.Grid.Sessions["pid-202-b"].PendingTool);
    }

    [Fact]
    public void A_fresh_single_session_still_answers_without_an_extra_selection()
    {
        var (bridge, platform) = Rig(selectPending: false);
        live.Remove("pid-101-a");
        bridge.Grid.Refresh(live);
        Assert.False(AnswerCommand.TargetState(bridge).NeedsSelection);
        AnswerCommand.AnswerApproval(bridge, true);
        Assert.Equal(("pid-202-b", KeyStroke.Return), Assert.Single(platform.Keys));
    }
}
