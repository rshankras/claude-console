namespace Loupedeck.ClaudeConsolePlugin.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Loupedeck.ClaudeConsolePlugin.Actions;
using Loupedeck.ClaudeConsolePlugin.Agents;
using Loupedeck.ClaudeConsolePlugin.Platform;
using Xunit;

/// <summary>
/// #131, from the 1.6.2 laptop pass (28 Sep 20:33): hooks trusted, bridge Ready, a real
/// PermissionRequest re-keyed onto the pinned session — and every Yes refused with "needs fresh
/// approval state after helper failure". The #120 gate wanted a success receipt for the session,
/// and under Codex's app-server daemon the helper can never write one: its ancestry never reaches
/// the terminal's codex.exe. The plugin now registers the helper-stamped envelope it attributed as
/// that session's delivery, with the same barrier checks a helper-written receipt gets.
/// Real files, registry parsing and action delivery; only the OS injection boundary is faked.
/// </summary>
public sealed class AttributedDeliveryTests : IDisposable
{
    private const string Session = "pid-4852-639262044198225498";
    private const string Other = "pid-101-1001";
    private readonly string root = Path.Combine(Path.GetTempPath(), "attributed-delivery-" + Guid.NewGuid().ToString("N"));
    private readonly HashSet<string> live = new() { Session, Other };
    private DateTime now = new(2026, 9, 28, 15, 3, 30, DateTimeKind.Utc);
    private string Sessions => Path.Combine(root, "sessions");
    private string Activity => Path.Combine(root, "activity");
    private string Helper => Path.Combine(root, "claude-console-hook.exe");

    public AttributedDeliveryTests()
    {
        Directory.CreateDirectory(Sessions);
        Directory.CreateDirectory(Activity);
        File.WriteAllText(Helper, "test helper");
        File.SetLastWriteTimeUtc(Helper, now.AddHours(-1));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    /// <summary>A Codex envelope as the helper writes it, keyed to <paramref name="key"/> (the registry's re-key is byte-identical).</summary>
    private void Envelope(string key, string kind, DateTime written, DateTime? started = null, string transport = null)
    {
        var path = Path.Combine(Sessions, key + ".json");
        var record = new Dictionary<string, object>
        {
            ["schema"] = 1, ["agent"] = "codex-cli", ["event"] = kind,
            ["ts"] = new DateTimeOffset(written).ToUnixTimeSeconds(),
            ["payload"] = new { session_id = "01a0e888", cwd = "C:\\work\\" + key, tool_name = "Bash", tool_input = new { command = "echo test" } },
        };
        if (transport == null) { record["hookStartedUtcTicks"] = (started ?? written).Ticks; }
        else { record["transport"] = transport; record["observationStartedUtcTicks"] = (started ?? written).Ticks; record["rolloutEventUtcTicks"] = (started ?? written).Ticks; }
        File.WriteAllText(path, JsonSerializer.Serialize(record));
        File.SetLastWriteTimeUtc(path, written);
    }

    private (BridgeManager Bridge, PlatformSeamTests.FakePlatformBridge Platform, WindowsHookHealth Health) Rig()
    {
        var agent = new CodexCliAdapter();
        var grid = new SessionRegistry(Sessions, Activity, Path.Combine(root, "registry.json")) { Agent = agent };
        grid.Refresh(live);
        var platform = new PlatformSeamTests.FakePlatformBridge();
        var health = new WindowsHookHealth(Helper, root, "codex-console", () => now);
        var bridge = new BridgeManager(platform) { Agent = agent, Grid = grid, HookHealth = health };
        bridge.RefreshHelperHealth();   // plugin load: latches the helper version; no receipts exist
        var slot = grid.SlotSession(1).SessionKey == Session ? 1 : 2;
        bridge.SelectSlot(slot);
        return (bridge, platform, health);
    }

    private static void Await(Func<bool> condition, string what)
    {
        for (var i = 0; i < 200 && !condition(); i++) { System.Threading.Thread.Sleep(10); }
        Assert.True(condition(), what);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_approval_the_registry_attributed_is_answerable_without_a_helper_receipt(bool approve)
    {
        Envelope(Session, "PermissionRequest", now.AddSeconds(-13));
        Envelope(Other, "Stop", now.AddMinutes(-1));
        var (bridge, platform, health) = Rig();

        // Before: exactly the laptop failure — pending, attributed, refused.
        Assert.False(bridge.IsSessionObservationCurrent(Session));
        Assert.True(AnswerCommand.TargetState(bridge).NeedsObservation);
        AnswerCommand.AnswerApproval(bridge, approve);
        Assert.Empty(platform.Keys);

        // The poll's registration step.
        Assert.True(bridge.RegisterAttributedDeliveries());
        Assert.Equal(WindowsHookHealthStatus.Healthy, health.Status);
        Assert.True(bridge.IsSessionObservationCurrent(Session));
        var decision = AnswerCommand.TargetState(bridge);
        Assert.False(decision.NeedsObservation);
        Assert.True(decision.HasPending);

        AnswerCommand.AnswerApproval(bridge, approve);
        Assert.Equal((Session, approve ? KeyStroke.Return : KeyStroke.Escape), Assert.Single(platform.Keys));
        Assert.True(File.Exists(Path.Combine(health.HealthDirectory, "success-" + Session + ".json")));
        Assert.False(bridge.RegisterAttributedDeliveries());   // idempotent: nothing newer to record
    }

    [Fact]
    public void A_rollout_envelope_is_not_helper_execution_and_registers_nothing()
    {
        Envelope(Session, "PermissionRequest", now.AddSeconds(-13), transport: "rollout-code-mode");
        Envelope(Other, "Stop", now.AddMinutes(-1), transport: "rollout");
        var (bridge, platform, health) = Rig();

        Assert.False(bridge.RegisterAttributedDeliveries());
        Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, health.Status);
        AnswerCommand.AnswerApproval(bridge, true);
        Assert.Empty(platform.Keys);
    }

    [Fact]
    public void A_stamp_from_before_the_helper_failed_cannot_recover_it()
    {
        Envelope(Session, "PermissionRequest", now.AddMinutes(-5), started: now.AddMinutes(-5));
        Envelope(Other, "Stop", now.AddMinutes(-5));
        var (bridge, platform, health) = Rig();
        File.Delete(Helper);
        bridge.RefreshHelperHealth();                       // the barrier: a helper failure at `now`
        Assert.Equal(WindowsHookHealthStatus.Unavailable, health.Status);
        File.WriteAllText(Helper, "restored by IT");
        File.SetLastWriteTimeUtc(Helper, now.AddHours(-1));   // put back with its old mtime
        bridge.RefreshHelperHealth();

        Assert.False(bridge.RegisterAttributedDeliveries());
        Assert.Equal(WindowsHookHealthStatus.Unavailable, health.Status);
        AnswerCommand.AnswerApproval(bridge, true);
        Assert.Empty(platform.Keys);

        // A hook that runs AFTER the failure recovers it, exactly as a helper receipt would.
        now = now.AddSeconds(30);
        Envelope(Session, "PermissionRequest", now.AddSeconds(-1));
        bridge.Grid.Refresh(live);
        Assert.True(bridge.RegisterAttributedDeliveries());
        Assert.Equal(WindowsHookHealthStatus.Healthy, health.Status);
    }

    [Fact]
    public void The_registered_receipt_survives_a_plugin_restart()
    {
        Envelope(Session, "PermissionRequest", now.AddSeconds(-13));
        Envelope(Other, "Stop", now.AddMinutes(-1));
        var (bridge, _, _) = Rig();
        Assert.True(bridge.RegisterAttributedDeliveries());

        var restarted = new WindowsHookHealth(Helper, root, "codex-console", () => now);
        Assert.Equal(WindowsHookHealthStatus.Healthy, restarted.Refresh());
        Assert.True(restarted.IsSessionObservationCurrent(Session));
    }

    [Fact]
    public void An_agent_whose_hooks_key_their_own_session_is_left_to_the_helper()
    {
        // Claude Code's hook keys the session itself and writes the receipt; the plugin must not
        // invent one from a statusline it merely read.
        Envelope(Session, "PermissionRequest", now.AddSeconds(-13));
        Envelope(Other, "Stop", now.AddMinutes(-1));
        var (bridge, _, health) = Rig();
        bridge.Agent = new ClaudeCodeAdapter();

        Assert.False(bridge.RegisterAttributedDeliveries());
        Assert.Equal(WindowsHookHealthStatus.AwaitingFresh, health.Status);
    }

    // --- PR #133 review: the re-key path itself, from shared.json -------------------------------

    private void SharedEnvelope(string kind, DateTime written, string cwd, bool withPayload = true)
    {
        var record = new Dictionary<string, object>
        {
            ["schema"] = 1, ["agent"] = "codex-cli", ["event"] = kind,
            ["ts"] = new DateTimeOffset(written).ToUnixTimeSeconds(),
            ["hookStartedUtcTicks"] = written.Ticks,
            ["payload"] = withPayload
                ? new { session_id = "01a0e888", cwd, tool_name = "Bash", tool_input = new { command = "echo test" } }
                : null,
        };
        var path = Path.Combine(Sessions, "shared.json");
        File.WriteAllText(path, JsonSerializer.Serialize(record));
        File.SetLastWriteTimeUtc(path, written);
    }

    private (BridgeManager Bridge, PlatformSeamTests.FakePlatformBridge Platform, WindowsHookHealth Health, SessionRegistry Grid) RoutedRig(DateTime sessionStarted)
    {
        var agent = new CodexCliAdapter();
        var grid = new SessionRegistry(Sessions, Activity, Path.Combine(root, "registry.json"))
        {
            Agent = agent,
            DiscoveredProjectDirs = new Dictionary<string, string> { [Session] = @"C:\work\alert", [Other] = @"C:\work\console" },
            DiscoveredSessionStarts = new Dictionary<string, DateTime> { [Session] = sessionStarted, [Other] = now.AddHours(-3) },
        };
        grid.Refresh(live);
        var platform = new PlatformSeamTests.FakePlatformBridge();
        var health = new WindowsHookHealth(Helper, root, "codex-console", () => now);
        var bridge = new BridgeManager(platform) { Agent = agent, Grid = grid, HookHealth = health };
        bridge.RefreshHelperHealth();
        bridge.SelectSlot(grid.SlotSession(1).SessionKey == Session ? 1 : 2);
        return (bridge, platform, health, grid);
    }

    [Fact]
    public void A_shared_approval_re_keyed_by_folder_is_answered_on_its_own_terminal()
    {
        SharedEnvelope("PermissionRequest", now.AddSeconds(-10), @"C:\work\alert");
        var (bridge, platform, health, grid) = RoutedRig(sessionStarted: now.AddMinutes(-5));

        Assert.Equal("Bash", grid.Sessions[Session].PendingTool);   // routed by the registry, not by the fixture
        Assert.True(bridge.RegisterAttributedDeliveries());
        Assert.True(bridge.IsSessionObservationCurrent(Session));
        AnswerCommand.AnswerApproval(bridge, true);
        Assert.Equal((Session, KeyStroke.Return), Assert.Single(platform.Keys));
    }

    [Fact]
    public void A_stale_shared_approval_is_not_handed_to_a_terminal_that_opened_after_it()
    {
        // Session A asked, was answered in its terminal and closed; its PermissionRequest is still the
        // last thing in shared.json. A new terminal opens in the same folder five minutes later.
        SharedEnvelope("PermissionRequest", now.AddMinutes(-6), @"C:\work\alert");
        var (bridge, platform, health, grid) = RoutedRig(sessionStarted: now.AddMinutes(-1));

        Assert.Null(grid.Sessions[Session].PendingTool);
        Assert.False(File.Exists(Path.Combine(Sessions, Session + ".json")));
        Assert.False(bridge.RegisterAttributedDeliveries());
        AnswerCommand.AnswerApproval(bridge, true);
        Assert.Empty(platform.Keys);
    }

    [Fact]
    public void An_envelope_whose_payload_never_arrived_earns_no_receipt()
    {
        Envelope(Other, "Stop", now.AddMinutes(-1));
        var (bridge, _, health) = Rig();
        var path = Path.Combine(Sessions, Session + ".json");
        File.WriteAllText(path, $$"""{"schema":1,"agent":"codex-cli","event":"PermissionRequest","ts":1,"hookStartedUtcTicks":{{now.AddSeconds(-3).Ticks}},"payload":null}""");
        File.SetLastWriteTimeUtc(path, now.AddSeconds(-3));
        bridge.Grid.Refresh(live);

        bridge.RegisterAttributedDeliveries();   // Other's complete envelope may register; this one must not
        Assert.False(File.Exists(Path.Combine(health.HealthDirectory, "success-" + Session + ".json")));
        Assert.False(bridge.IsSessionObservationCurrent(Session));
    }
}
