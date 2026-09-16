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

    // Asking which session an approval belongs to must not itself be what makes the answer
    // "you must choose". The key faces call ApprovalTty on every repaint, and the latch it used to
    // raise never clears — so a session the user never saw could have left them permanently unable
    // to answer without selecting. The poll and the session keys record that fact; the query reads it.
    [Fact]
    public void Painting_the_answer_keys_cannot_latch_the_selection_requirement()
    {
        var (bridge, _) = Rig(selectPending: false);

        // Two live sessions: ambiguous on its own merits, however many times it is asked.
        for (var i = 0; i < 5; i++) { Assert.Null(bridge.ApprovalTty()); }

        // One goes away. Nothing recorded a deliberate choice, so the survivor is unambiguous.
        live.Remove("pid-202-b");
        bridge.Grid.Refresh(live);
        Assert.Equal("pid-101-a", bridge.ApprovalTty());

        // Raised the way the poll and SelectSlot raise it, the latch still holds.
        bridge.NoteCodexSelectionNeeded();
        Assert.Null(bridge.ApprovalTty());
    }

    // The beep answers every press; the card explains it once. Repeating the same sentence per
    // press filled the Options+ message centre and read as a new problem each time.
    [Fact]
    public void The_select_a_session_card_is_posted_once_per_episode()
    {
        var (bridge, platform) = Rig(selectPending: false);
        var cards = 0;
        bridge.Notify = (_, _, _, _) => cards++;

        for (var i = 0; i < 4; i++) { AnswerCommand.AnswerApproval(bridge, true); }
        Assert.Equal(1, cards);
        Assert.Empty(platform.Keys);

        // Choosing a session ends the episode and delivers the answer.
        var slot = PendingSlot(bridge);
        bridge.SelectSlot(slot);
        AnswerCommand.AnswerApproval(bridge, true);
        Assert.Equal(("pid-202-b", KeyStroke.Return), Assert.Single(platform.Keys));
        Assert.Equal(1, cards);

        // A later episode is a different question and explains itself again. Pressing the same
        // session key releases the pin, which is how a user drops back to "which session?".
        bridge.SelectSlot(slot);
        Assert.Null(bridge.PinnedTty);
        AnswerCommand.AnswerApproval(bridge, true);
        Assert.Equal(2, cards);
    }

    private void AddExtraSessions(BridgeManager bridge, int lastSlot)
    {
        Write("pid-202-b", "Stop");
        for (var i = 3; i <= lastSlot; i++)
        {
            var key = "extra-" + i;
            Write(key, i == lastSlot ? "PermissionRequest" : "Stop");
            live.Add(key);
            bridge.Grid.Refresh(live);
        }
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(6, false)]
    public void Hidden_sessions_cannot_be_selected_or_light_or_receive_an_answer(int slot, bool approve)
    {
        var (bridge, platform) = Rig(selectPending: false);
        AddExtraSessions(bridge, slot);
        Assert.Equal(3, bridge.SessionSlotCount);
        Assert.Equal("extra-" + slot, bridge.Grid.SlotSession(slot).SessionKey);
        bridge.SelectSlot(slot);
        Assert.Null(bridge.PinnedTty);
        Assert.Empty(platform.Focused);
        var decision = AnswerCommand.TargetState(bridge);
        Assert.True(decision.NeedsSelection);
        Assert.Null(decision.Key);
        Assert.False(decision.HasPending);
        Assert.Equal(ApprovalRisk.None, decision.Risk);
        AnswerCommand.AnswerApproval(bridge, approve);
        Assert.Empty(platform.Keys);
        Assert.Equal("Bash", bridge.Grid.SlotSession(slot).PendingTool);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void A_pin_from_the_old_six_slot_layout_cannot_arm_a_hidden_codex_session(int slot)
    {
        var (bridge, platform) = Rig(selectPending: false);
        AddExtraSessions(bridge, slot);
        // Establish the same pin the previous six-slot version could save. Claude still
        // supports all six slots, so it can arrange this migration case without reflection.
        bridge.Agent = new ClaudeCodeAdapter();
        Assert.Equal(6, bridge.SessionSlotCount);
        bridge.SelectSlot(slot);
        Assert.Equal("extra-" + slot, bridge.PinnedTty);
        bridge.Agent = new CodexCliAdapter();
        Assert.Null(AnswerCommand.TargetState(bridge).Key);
        Assert.Equal(ApprovalRisk.None, AnswerCommand.TargetState(bridge).Risk);
        AnswerCommand.AnswerApproval(bridge, true);
        AnswerCommand.AnswerApproval(bridge, false);
        Assert.Empty(platform.Keys);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void A_lone_session_in_a_hidden_slot_cannot_auto_arm_on_reload(int slot)
    {
        var (bridge, platform) = Rig(selectPending: false);
        AddExtraSessions(bridge, slot);
        var key = "extra-" + slot;
        live.Clear();
        live.Add(key);
        bridge.Grid.Refresh(live);
        var reloaded = new BridgeManager(platform) { Agent = new CodexCliAdapter(), Grid = bridge.Grid };
        Assert.Single(reloaded.Grid.LiveSessions());
        Assert.Equal(key, reloaded.Grid.SlotSession(slot).SessionKey);
        Assert.Null(AnswerCommand.TargetState(reloaded).Key);
        Assert.Equal(ApprovalRisk.None, AnswerCommand.TargetState(reloaded).Risk);
        AnswerCommand.AnswerApproval(reloaded, true);
        AnswerCommand.AnswerApproval(reloaded, false);
        Assert.Empty(platform.Keys);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void With_five_sessions_answers_stay_on_the_selected_visible_slot(bool approve)
    {
        var (bridge, platform) = Rig(selectPending: false);
        AddExtraSessions(bridge, 5);
        bridge.SelectSlot(3);
        Assert.Equal(ApprovalRisk.None, AnswerCommand.TargetState(bridge).Risk);
        AnswerCommand.AnswerApproval(bridge, approve);
        Assert.Empty(platform.Keys);
        Write("extra-3", "PermissionRequest");
        bridge.Grid.Refresh(live);
        Assert.Equal("extra-3", AnswerCommand.TargetState(bridge).Key);
        AnswerCommand.AnswerApproval(bridge, approve);
        Assert.Equal(("extra-3", approve ? KeyStroke.Return : KeyStroke.Escape), Assert.Single(platform.Keys));
        Assert.Equal("Bash", bridge.Grid.SlotSession(5).PendingTool);
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
