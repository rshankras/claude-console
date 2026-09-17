namespace Loupedeck.ClaudeConsolePlugin.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Loupedeck.ClaudeConsolePlugin.Actions;
using Loupedeck.ClaudeConsolePlugin.Agents;
using Loupedeck.ClaudeConsolePlugin.Platform;
using Xunit;

public sealed class VizhiAcceptanceRegressionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vizhi-acceptance-" + Guid.NewGuid().ToString("N"));
    public VizhiAcceptanceRegressionTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "sessions"));
        Directory.CreateDirectory(Path.Combine(root, "activity"));
    }
    public void Dispose() => Directory.Delete(root, true);
    private SessionRegistry Grid(IAgentAdapter agent) => new(Path.Combine(root, "sessions"), Path.Combine(root, "activity"), Path.Combine(root, "registry.json")) { Agent = agent };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Session_end_timeout_is_within_cli_limit_on_both_platforms(bool windows)
    {
        var bridge = new CodexStateBridge(root, Path.Combine(root, "sessions"));
        var hooks = JsonNode.Parse(bridge.BuildHooksJson(windows))["hooks"];
        foreach (var name in CodexStateBridge.Events)
            Assert.Equal(name == "SessionEnd" ? 3 : 5, (int)hooks[name][0]["hooks"][0]["timeout"]);
    }

    [Fact]
    public void Live_process_directory_labels_codex_before_any_hook_or_prompt()
    {
        var grid = Grid(new CodexCliAdapter());
        grid.DiscoveredProjectDirs = new Dictionary<string, string> { ["ttys002"] = "/projects/alpha" };
        grid.Refresh(new HashSet<string> { "ttys002" });
        Assert.Equal("alpha", grid.SlotSession(1).Project);
        Assert.Equal("/projects/alpha", grid.SlotSession(1).ProjectDir);
        Assert.True(grid.SlotSession(1).IsProvisional);
        Assert.Equal("Ready", SessionSlotCommand.StateWord(grid.SlotSession(1), null, "codex-cli"));
        Assert.Null(grid.SlotSession(1).SessionId); // cwd is not evidence of a conversation id
        grid.DiscoveredProjectDirs = new Dictionary<string, string>();
        grid.Refresh(new HashSet<string>());
        grid.Refresh(new HashSet<string> { "ttys002" });
        Assert.Null(grid.SlotSession(1).Project); // recycled tty must not inherit alpha
    }

    [Fact]
    public void Hook_directory_wins_over_process_hint()
    {
        File.WriteAllText(Path.Combine(root, "sessions", "ttys002.json"), """
            {"event":"Stop","payload":{"session_id":"real","cwd":"/projects/beta"}}
            """);
        var grid = Grid(new CodexCliAdapter());
        grid.DiscoveredProjectDirs = new Dictionary<string, string> { ["ttys002"] = "/projects/alpha" };
        grid.Refresh(new HashSet<string> { "ttys002" });
        Assert.Equal("beta", grid.SlotSession(1).Project);
    }

    [Fact]
    public void Fork_end_cannot_keep_the_parent_context_or_id_on_a_live_key()
    {
        var path = Path.Combine(root, "sessions", "ttys002.json");
        File.WriteAllText(path, """{"event":"SessionEnd","payload":{"session_id":"parent","cwd":"/projects/alpha","transcript_path":"old"}}""");
        var grid = Grid(new CodexCliAdapter());
        var live = new HashSet<string> { "ttys002" };
        grid.Refresh(live);
        Assert.Equal("alpha", grid.SlotSession(1).Project);
        Assert.True(grid.SlotSession(1).IsProvisional);
        Assert.Equal("Ready", SessionSlotCommand.StateWord(grid.SlotSession(1), null, "codex-cli"));
        Assert.Null(grid.SlotSession(1).SessionId);
        Assert.Null(grid.SlotSession(1).TranscriptPath);
        File.WriteAllText(path, """{"event":"SessionStart","payload":{"session_id":"child","cwd":"/projects/alpha"}}""");
        grid.Refresh(live);
        Assert.Equal("child", grid.SlotSession(1).SessionId);
        Assert.False(grid.SlotSession(1).IsProvisional);
    }

    [Theory]
    [InlineData(true, InjectionOutcome.Ok)]
    [InlineData(false, InjectionOutcome.Ok)]
    [InlineData(true, InjectionOutcome.Failed)]
    [InlineData(false, InjectionOutcome.SessionMissing)]
    public void Claude_approval_delivery_preserves_existing_clear_and_retry_behavior(bool approve, InjectionOutcome outcome)
    {
        File.WriteAllText(Path.Combine(root, "sessions", "ttys002.json"), """
            {"session_id":"claude-session","workspace":{"current_dir":"/projects/alpha"}}
            """);
        File.WriteAllText(Path.Combine(root, "activity", "ttys002.json"), """{"state":"waiting","ts":1}""");
        var pending = Path.Combine(root, "activity", "pending-ttys002.json");
        const string payload = """{"tool_name":"Bash","tool_input":{"command":"git status"}}""";
        File.WriteAllText(pending, payload);
        var grid = Grid(new ClaudeCodeAdapter());
        var live = new HashSet<string> { "ttys002" };
        grid.Refresh(live);
        var platform = new PlatformSeamTests.FakePlatformBridge { Outcome = outcome };
        var bridge = new BridgeManager(platform) { Agent = new ClaudeCodeAdapter(), Grid = grid };
        // Use the actual decision and delivery helper after setup, without modifying real settings.
        Assert.Equal(approve ? AnswerCommand.AnswerVia.MenuConfirm : AnswerCommand.AnswerVia.MenuReject,
            AnswerCommand.Decide(approve, grid.Sessions["ttys002"].PendingTool != null));
        AnswerCommand.Answered(bridge, "ttys002", bridge.InjectKeyTo("ttys002", approve ? KeyStroke.Return : KeyStroke.Escape), "test");
        grid.Refresh(live);
        Assert.Equal(outcome != InjectionOutcome.Ok, File.Exists(pending));
        Assert.Equal(outcome != InjectionOutcome.Ok, grid.Sessions["ttys002"].PendingTool != null);
        File.WriteAllText(pending, payload);
        grid.Refresh(live);
        Assert.Equal("Bash", grid.Sessions["ttys002"].PendingTool);
    }

    [Fact]
    public void Disappearing_Claude_pending_file_is_not_mistaken_for_inline_Codex_state()
    {
        var state = Path.Combine(root, "sessions", "ttys002.json");
        File.WriteAllText(state, """{"session_id":"claude-session","workspace":{"current_dir":"/projects/alpha"}}""");
        File.WriteAllText(Path.Combine(root, "activity", "ttys002.json"), """{"state":"waiting","ts":1}""");
        var pending = Path.Combine(root, "activity", "pending-ttys002.json");
        const string payload = """{"tool_name":"Bash","tool_input":{"command":"git status"}}""";
        File.WriteAllText(pending, payload);
        var grid = Grid(new ClaudeCodeAdapter());
        var live = new HashSet<string> { "ttys002" };
        grid.Refresh(live);
        File.Delete(pending); // hook cleaned up while the keypad retained its last snapshot
        Assert.True(grid.ClearPendingApproval("ttys002"));
        Assert.Equal("waiting", grid.Sessions["ttys002"].State);
        File.WriteAllText(pending, payload);
        File.SetLastWriteTimeUtc(pending, File.GetLastWriteTimeUtc(state));
        grid.Refresh(live);
        Assert.Equal("Bash", grid.Sessions["ttys002"].PendingTool);
    }

    [Fact]
    public void Pin_and_frontmost_changes_notify_decision_faces_without_state_writes()
    {
        var grid = Grid(new CodexCliAdapter());
        grid.Refresh(new HashSet<string> { "ttys002", "ttys003" });
        var platform = new PlatformSeamTests.FakePlatformBridge();
        var bridge = new BridgeManager(platform) { Agent = new CodexCliAdapter(), Grid = grid };
        var changes = 0;
        bridge.OnTargetChanged += () => changes++;
        bridge.ActiveTty = "ttys002";
        var before = changes;
        bridge.ActiveTty = "ttys002";
        Assert.Equal(before, changes); // no steady-state redraws
        bridge.SelectSlot(2);
        Assert.True(changes > before);
        before = changes;
        bridge.SelectSlot(2); // unpin
        Assert.True(changes > before);
    }

    [Fact]
    public void Plan_uses_native_toggle_twice_without_typing_or_submitting()
    {
        var platform = new PlatformSeamTests.FakePlatformBridge();
        var bridge = new BridgeManager(platform) { Agent = new CodexCliAdapter(), Grid = Grid(new CodexCliAdapter()) };
        bridge.ActiveTty = "ttys002";
        ControlCommand.TogglePlan(bridge);
        ControlCommand.TogglePlan(bridge);
        Assert.Equal(2, platform.Keys.Count);
        Assert.All(platform.Keys, key => Assert.Equal(("ttys002", KeyStroke.ShiftTab), key));
        Assert.Empty(platform.Texts);
    }

    [Fact]
    public void Claude_runtime_path_is_unchanged_and_codex_cannot_replace_its_helper()
    {
        Assert.Equal(Path.Combine(root, ".claude", "claude-console"), BridgeManager.VoiceRuntimeHome(root, "claude-console"));
        Assert.NotEqual(BridgeManager.VoiceRuntimeHome(root, "claude-console"), BridgeManager.VoiceRuntimeHome(root, "codex-console"));
    }

    [Fact]
    public void Context_help_preserves_claude_setup_and_codex_compact_guidance()
    {
        Assert.Contains("turn live status", ContextCommand.DescriptionFor("claude-code"));
        Assert.DoesNotContain("Codex", ContextCommand.DescriptionFor("claude-code"));
        Assert.Contains("hold to compact", ContextCommand.DescriptionFor("codex-cli"));
    }

    [Fact]
    public void Mac_discovery_attaches_directory_only_to_drivable_codex_processes()
    {
        var platform = new MacPlatformBridge(AgentProcessMatcher.CodexCli, "codex")
        {
            PsRunner = () => "100 1 ?? /System/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal\n101 100 ttys002 /bin/zsh\n102 101 ttys002 codex\n103 1 ?? codex app-server",
            ProcessDirectoryReader = pid => pid == 102 ? "/projects/alpha" : throw new Exception("wrong process"),
        };
        Assert.Contains("ttys002", platform.DiscoverSessions());
        Assert.Equal("/projects/alpha", platform.SessionDirectories["ttys002"]);
        platform.PsRunner = () => "";
        Assert.Empty(platform.DiscoverSessions());
        Assert.Empty(platform.SessionDirectories);
    }

    [Fact]
    public void Mac_native_directory_reader_reports_this_process_without_subprocesses()
    {
        if (!OperatingSystem.IsMacOS()) { return; }
        var cwd = MacProcessDirectory.Read(Environment.ProcessId);
        Assert.False(string.IsNullOrWhiteSpace(cwd));
        Assert.True(Directory.Exists(cwd));
    }
}
