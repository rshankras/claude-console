namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using Loupedeck.ClaudeConsolePlugin.Platform;
    using Xunit;

    // Uses the existing isolated TEMP / renamed Claude process rig. These execute the published,
    // trimmed Windows helper; a Mac run reports them skipped rather than claiming Windows proof.
    public partial class WindowsHookContractTests
    {
        private String HealthDirectory(String helper) => Path.Combine(Root, "hook-health",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(helper).ToUpperInvariant()))).ToLowerInvariant());

        private String Receipt(String key) => Path.Combine(HealthDirectory(_hook), "success-" + key + ".json");

        [WindowsFact]
        public void Missing_helper_records_a_path_scoped_failure_without_statusline_output()
        {
            using var claude = this.StartClaude();
            var missing = Path.Combine(_bin, "missing ' café helper.exe");
            var output = Path.Combine(_bin, "missing-output.txt");
            Assert.Equal(0, claude.Run(BridgeWiring.StatuslineCommand(true, missing) + " < status.json > missing-output.txt", 30000));
            Assert.Equal("", File.ReadAllText(output));
            var marker = Assert.Single(Directory.GetFiles(HealthDirectory(missing), "failure-*.json"));
            using var json = JsonDocument.Parse(File.ReadAllText(marker));
            Assert.Equal(1, json.RootElement.GetProperty("schema").GetInt32());
            Assert.Equal("missing", json.RootElement.GetProperty("reason").GetString());
            Assert.Equal("helper", json.RootElement.GetProperty("scope").GetString());
            Assert.InRange(json.RootElement.GetProperty("observedUtcTicks").GetInt64(),
                DateTime.UtcNow.AddMinutes(-1).Ticks, DateTime.UtcNow.Ticks);
            // The three writers agree on the directory: the launcher's baked-in literal landed
            // where the plugin's own computation looks.
            Assert.Equal(WindowsHookHealth.HealthDirectoryFor(missing, Root), Path.GetDirectoryName(marker));
        }

        [WindowsFact]
        public void Present_but_unlaunchable_helper_records_failure_and_returns_nonzero()
        {
            using var claude = this.StartClaude();
            var invalid = Path.Combine(_bin, "invalid helper.exe");
            File.WriteAllText(invalid, "This is not a Windows executable.");
            Assert.NotEqual(0, claude.Run(BridgeWiring.StatuslineCommand(true, invalid) + " < status.json", 30000));
            var marker = Assert.Single(Directory.GetFiles(HealthDirectory(invalid), "failure-*.json"));
            using var json = JsonDocument.Parse(File.ReadAllText(marker));
            Assert.Equal("launch-failed", json.RootElement.GetProperty("reason").GetString());
        }

        [WindowsFact]
        public void Launcher_preserves_native_exit_code_and_standard_output()
        {
            using var claude = this.StartClaude();
            var command = Path.Combine(_bin, "exit-probe.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), command);
            Assert.Equal(7, claude.Run(BridgeWiring.WindowsCommand(command, "/d", "/c", "echo chained-output & exit 7") +
                " < status.json > exit-output.txt", 30000));
            Assert.Equal("chained-output", File.ReadAllText(Path.Combine(_bin, "exit-output.txt")).Trim());
            var marker = Assert.Single(Directory.GetFiles(HealthDirectory(command), "failure-*.json"));
            using var json = JsonDocument.Parse(File.ReadAllText(marker));
            Assert.Equal("nonzero-exit", json.RootElement.GetProperty("reason").GetString());
        }

        [WindowsFact]
        public void Successful_keyed_permission_write_produces_receipt_after_pending_payload()
        {
            using var claude = this.StartClaude();
            var before = DateTime.UtcNow.Ticks;
            Assert.Equal(0, claude.Run(BridgeWiring.ActivityCommand(true, _hook, "permission") + " < permission.json", 30000));
            using var json = JsonDocument.Parse(File.ReadAllText(Receipt(claude.Key)));
            var receipt = json.RootElement;
            Assert.Equal(1, receipt.GetProperty("schema").GetInt32());
            Assert.Equal(claude.Key, receipt.GetProperty("sessionKey").GetString());
            Assert.Equal("activity:permission", receipt.GetProperty("event").GetString());
            var started = receipt.GetProperty("startedUtcTicks").GetInt64();
            var completed = receipt.GetProperty("completedUtcTicks").GetInt64();
            Assert.InRange(started, before, completed);
            Assert.True(File.GetLastWriteTimeUtc(Path.Combine(ActivityDir, "pending-" + claude.Key + ".json")).Ticks <= completed);
            using var pending = JsonDocument.Parse(File.ReadAllText(Path.Combine(ActivityDir, "pending-" + claude.Key + ".json")));
            Assert.Equal(started, pending.RootElement.GetProperty("hookStartedUtcTicks").GetInt64());
            Assert.False(Directory.GetFiles(HealthDirectory(_hook), "failure-*.json").Any());
        }

        [WindowsFact]
        public void Approval_invocation_timestamp_is_owned_by_the_helper_not_the_input()
        {
            using var claude = this.StartClaude();
            var input = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(_bin, "permission.json"))).AsObject();
            input["hookStartedUtcTicks"] = 1L;
            File.WriteAllText(Path.Combine(_bin, "permission.json"), input.ToJsonString());
            var before = DateTime.UtcNow.Ticks;
            Assert.Equal(0, claude.Run(BridgeWiring.ActivityCommand(true, _hook, "permission") + " < permission.json", 30000));
            using var pending = JsonDocument.Parse(File.ReadAllText(Path.Combine(ActivityDir, "pending-" + claude.Key + ".json")));
            Assert.InRange(pending.RootElement.GetProperty("hookStartedUtcTicks").GetInt64(), before, DateTime.UtcNow.Ticks);
            Assert.Equal("PowerShell", pending.RootElement.GetProperty("tool_name").GetString());
        }

        [WindowsFact]
        public void Statusline_and_activity_snapshots_are_bound_to_their_own_invocations()
        {
            using var claude = this.StartClaude();
            Assert.Equal(0, claude.Run(BridgeWiring.StatuslineCommand(true, _hook) + " < status.json", 30000));
            using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(SessionsDir, claude.Key + ".json")));
            using var statusReceipt = JsonDocument.Parse(File.ReadAllText(Receipt(claude.Key)));
            Assert.Equal(statusReceipt.RootElement.GetProperty("startedUtcTicks").GetInt64(),
                status.RootElement.GetProperty("hookStartedUtcTicks").GetInt64());

            Assert.Equal(0, claude.Run(BridgeWiring.ActivityCommand(true, _hook, "busy") + " < nul", 30000));
            using var activity = JsonDocument.Parse(File.ReadAllText(Path.Combine(ActivityDir, claude.Key + ".json")));
            using var activityReceipt = JsonDocument.Parse(File.ReadAllText(Receipt(claude.Key)));
            Assert.Equal(activityReceipt.RootElement.GetProperty("startedUtcTicks").GetInt64(),
                activity.RootElement.GetProperty("hookStartedUtcTicks").GetInt64());
        }

        [WindowsFact]
        public void Codex_envelope_carries_the_hook_start_even_before_session_resolution()
        {
            var before = DateTime.UtcNow.Ticks;
            var run = this.LaunchDirect("{}", false, "codex", "PermissionRequest");
            Assert.Equal(0, run.ExitCode);
            using var envelope = JsonDocument.Parse(File.ReadAllText(Path.Combine(_temp, "codex-console", "sessions", "shared.json")));
            Assert.InRange(envelope.RootElement.GetProperty("hookStartedUtcTicks").GetInt64(), before, DateTime.UtcNow.Ticks);
        }

        [WindowsFact]
        public void Failed_keyed_write_cannot_publish_a_success_receipt()
        {
            using var claude = this.StartClaude();
            Directory.CreateDirectory(ActivityDir);
            var target = Path.Combine(ActivityDir, claude.Key + ".json");
            using var locked = new FileStream(target, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            Assert.Equal(0, claude.Run(BridgeWiring.ActivityCommand(true, _hook, "busy") + " < nul", 30000));
            Assert.False(File.Exists(Receipt(claude.Key)));
            var marker = Assert.Single(Directory.GetFiles(HealthDirectory(_hook), "failure-*.json"));
            using var json = JsonDocument.Parse(File.ReadAllText(marker));
            Assert.Equal("observation-failed", json.RootElement.GetProperty("reason").GetString());
            Assert.Equal("delivery", json.RootElement.GetProperty("scope").GetString());
            // The exe's own hash of Environment.ProcessPath matches the plugin's from the package path.
            Assert.Equal(WindowsHookHealth.HealthDirectoryFor(_hook, Root), Path.GetDirectoryName(marker));
        }

        [WindowsFact]
        public void Failed_receipt_replacement_invalidates_earlier_health_evidence()
        {
            using var claude = this.StartClaude();
            Assert.Equal(0, claude.Run(BridgeWiring.ActivityCommand(true, _hook, "busy") + " < nul", 30000));
            var original = File.ReadAllText(Receipt(claude.Key));
            using var locked = new FileStream(Receipt(claude.Key), FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Equal(0, claude.Run(BridgeWiring.ActivityCommand(true, _hook, "done") + " < nul", 30000));
            Assert.Equal(original, File.ReadAllText(Receipt(claude.Key)));
            Assert.NotEmpty(Directory.GetFiles(HealthDirectory(_hook), "failure-*.json"));
            Assert.Empty(Directory.GetFiles(HealthDirectory(_hook), "*.tmp"));
        }

        [WindowsFact]
        public void Malformed_statusline_input_cannot_claim_recovery()
        {
            using var claude = this.StartClaude();
            File.WriteAllText(Path.Combine(_bin, "invalid.json"), "{ unfinished payload");
            Assert.Equal(0, claude.Run(BridgeWiring.StatuslineCommand(true, _hook) + " < invalid.json", 30000));
            Assert.False(File.Exists(Receipt(claude.Key)));
            Assert.NotEmpty(Directory.GetFiles(HealthDirectory(_hook), "failure-*.json"));
        }

        [WindowsFact]
        public void Empty_new_permission_payload_removes_old_pending_and_does_not_refresh_receipt()
        {
            using var claude = this.StartClaude();
            Assert.Equal(0, claude.Run(BridgeWiring.ActivityCommand(true, _hook, "permission") + " < permission.json", 30000));
            var receipt = File.ReadAllText(Receipt(claude.Key));
            Assert.Equal(0, claude.Run(BridgeWiring.ActivityCommand(true, _hook, "permission") + " < nul", 30000));
            Assert.False(File.Exists(Path.Combine(ActivityDir, "pending-" + claude.Key + ".json")));
            Assert.Equal(receipt, File.ReadAllText(Receipt(claude.Key)));
            Assert.NotEmpty(Directory.GetFiles(HealthDirectory(_hook), "failure-*.json"));
        }
    }
}
