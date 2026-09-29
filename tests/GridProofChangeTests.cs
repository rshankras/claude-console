namespace Loupedeck.ClaudeConsolePlugin.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Loupedeck.ClaudeConsolePlugin.Agents;
using Xunit;

public sealed class GridProofChangeTests : IDisposable
{
    private const string Key = "pid-101-1001";
    private readonly string root = Path.Combine(Path.GetTempPath(), "grid-proof-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { Directory.Delete(root, recursive: true); }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void New_proof_for_identical_pending_data_repaints_once_after_parsing(bool codex)
    {
        var sessions = Path.Combine(root, "sessions");
        var activity = Path.Combine(root, "activity");
        Directory.CreateDirectory(sessions);
        Directory.CreateDirectory(activity);
        var registry = new SessionRegistry(sessions, activity, Path.Combine(root, "registry.json"));
        registry.Agent = codex ? new CodexCliAdapter() : new ClaudeCodeAdapter();
        var live = new HashSet<string> { Key };
        var started = DateTime.UtcNow.AddMinutes(-1).Ticks;
        void Write(long proof)
        {
            if (codex)
            {
                File.WriteAllText(Path.Combine(sessions, Key + ".json"), JsonSerializer.Serialize(new
                {
                    schema = 1, agent = "codex-cli", @event = "PermissionRequest", hookStartedUtcTicks = proof,
                    payload = new { cwd = "/project", tool_name = "Bash", tool_input = new { command = "echo test" } },
                }));
            }
            else
            {
                File.WriteAllText(Path.Combine(sessions, Key + ".json"), "{\"workspace\":{\"project_dir\":\"/project\"}}");
                File.WriteAllText(Path.Combine(activity, Key + ".json"), JsonSerializer.Serialize(new
                { state = "waiting", ts = 1, hookStartedUtcTicks = started }));
                File.WriteAllText(Path.Combine(activity, "pending-" + Key + ".json"), JsonSerializer.Serialize(new
                { tool_name = "Bash", tool_input = new { command = "echo test" }, hookStartedUtcTicks = proof }));
            }
        }
        Write(started);
        registry.Refresh(live);
        var changes = 0;
        registry.OnGridChanged += () => ++changes;

        Write(started + TimeSpan.TicksPerSecond);
        registry.Refresh(live);
        Assert.Equal(1, changes);
        Assert.Equal(started + TimeSpan.TicksPerSecond, registry.Sessions[Key].ApprovalObservationStartedAtUtc.Value.Ticks);

        registry.Refresh(live);
        Assert.Equal(1, changes); // Polling unchanged evidence must not keep repainting.
    }
}
