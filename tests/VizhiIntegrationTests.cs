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

    private (BridgeManager Bridge, PlatformSeamTests.FakePlatformBridge Platform) Rig()
    {
        Directory.CreateDirectory(Path.Combine(root, "sessions"));
        Directory.CreateDirectory(Path.Combine(root, "activity"));
        Write("pid-101-a", "Stop");
        Write("pid-202-b", "PermissionRequest");
        var agent = new CodexCliAdapter();
        var grid = new SessionRegistry(Path.Combine(root, "sessions"), Path.Combine(root, "activity"), Path.Combine(root, "registry.json")) { Agent = agent };
        grid.Refresh(live);
        var platform = new PlatformSeamTests.FakePlatformBridge();
        return (new BridgeManager(platform) { Agent = agent, Grid = grid }, platform);
    }

    private void Write(string key, string kind) => File.WriteAllText(Path.Combine(root, "sessions", key + ".json"), JsonSerializer.Serialize(new
    {
        schema = 1, agent = "codex-cli", @event = kind,
        ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        payload = new { session_id = key, cwd = "/projects/" + key, tool_name = "Bash", tool_input = new { command = "git push" } },
    }));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Answers_route_to_the_pending_codex_session_and_clear_only_after_delivery(bool approve)
    {
        var (bridge, platform) = Rig();
        // main's pending-approval priority must work with Codex's embedded state as well.
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
        bridge.SelectSlot(pendingSlot);
        platform.FocusSucceeds = false;
        bridge.SelectSlot(pendingSlot == 1 ? 2 : 1);
        Assert.Equal("pid-202-b", bridge.PinnedTty);
        Assert.Equal("pid-202-b", bridge.Grid.FocusedSession);
        AnswerCommand.AnswerApproval(bridge, false);
        Assert.Equal(("pid-202-b", KeyStroke.Escape), Assert.Single(platform.Keys));
    }
}
