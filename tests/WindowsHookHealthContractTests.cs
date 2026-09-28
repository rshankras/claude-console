namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
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

        // ---------------------------------------------------------------------------------------
        // #126 — Codex runs the same guarded launcher, aimed at the codex-console root. Before this
        // the hooks.json command ran the exe directly, and a quarantined or unlaunchable helper
        // left nothing anywhere the plugin reads (replicated 2026-09-28: exit 1, zero files).
        // ---------------------------------------------------------------------------------------

        private String CodexRoot => Path.Combine(_temp, "codex-console");
        private String CodexHealthDirectory(String helper) => WindowsHookHealth.HealthDirectoryFor(helper, CodexRoot);
        private static String CodexLauncher(String helper, String eventName) =>
            new Agents.CodexStateBridge { HookExe = helper }.HookCommand(eventName, windows: true);

        [WindowsFact]
        public void Codex_launcher_records_a_missing_helper_under_the_codex_root()
        {
            var missing = Path.Combine(_bin, "missing ' café helper.exe");
            Assert.Equal(0, this.LaunchLauncher(CodexLauncher(missing, "Stop"), "{}", false).ExitCode);
            var marker = Assert.Single(Directory.GetFiles(CodexHealthDirectory(missing), "failure-*.json"));
            using var json = JsonDocument.Parse(File.ReadAllText(marker));
            Assert.Equal("missing", json.RootElement.GetProperty("reason").GetString());
            Assert.Equal("helper", json.RootElement.GetProperty("scope").GetString());
            // The product roots stay separate: nothing of Codex's lands under claude-console.
            Assert.False(Directory.Exists(Path.Combine(Root, "hook-health")));
        }

        [WindowsFact]
        public void Codex_launcher_records_an_unlaunchable_helper_and_returns_nonzero()
        {
            var invalid = Path.Combine(_bin, "invalid codex helper.exe");
            File.WriteAllText(invalid, "This is not a Windows executable.");
            Assert.NotEqual(0, this.LaunchLauncher(CodexLauncher(invalid, "PermissionRequest"), "{}", false).ExitCode);
            var marker = Assert.Single(Directory.GetFiles(CodexHealthDirectory(invalid), "failure-*.json"));
            using var json = JsonDocument.Parse(File.ReadAllText(marker));
            Assert.Equal("launch-failed", json.RootElement.GetProperty("reason").GetString());
            Assert.Equal("helper", json.RootElement.GetProperty("scope").GetString());
        }

        [WindowsFact]
        public void Codex_launcher_forwards_the_payload_to_the_helpers_codex_verb()
        {
            var before = DateTime.UtcNow.Ticks;
            Assert.Equal(0, this.LaunchLauncher(CodexLauncher(_hook, "PermissionRequest"),
                File.ReadAllText(Path.Combine(_bin, "permission.json")), false).ExitCode);
            using var envelope = JsonDocument.Parse(File.ReadAllText(Path.Combine(CodexRoot, "sessions", "shared.json")));
            Assert.Equal("PermissionRequest", envelope.RootElement.GetProperty("event").GetString());
            Assert.Equal("PowerShell", envelope.RootElement.GetProperty("payload").GetProperty("tool_name").GetString());
            Assert.InRange(envelope.RootElement.GetProperty("hookStartedUtcTicks").GetInt64(), before, DateTime.UtcNow.Ticks);
            var health = CodexHealthDirectory(_hook);
            Assert.Empty(Directory.Exists(health) ? Directory.GetFiles(health, "failure-*.json") : Array.Empty<String>());
        }

        [WindowsFact]
        public void Codex_launcher_with_a_stdin_that_never_closes_still_delivers_what_arrived()
        {
            // The exe's own bounded read exists because Codex can leave the pipe without EOF. A
            // launcher that waited for EOF and then skipped the helper would lose the EVENT; this
            // one takes the bytes that arrived, notes the timeout, and runs the helper anyway.
            // Warm the copied binary first: the first launch of a freshly copied exe pays an
            // antivirus scan, and on the laptop that alone pushed one run to 5.55 s. The 1.5 s
            // stdin wait plus PowerShell and the helper is what the 5 s budget measures.
            this.LaunchDirect("{}", false, "codex", "SessionStart");
            var run = this.LaunchLauncher(CodexLauncher(_hook, "Stop"), "{\"session_id\":\"held-open\"}", holdStdin: true);
            Assert.Equal(0, run.ExitCode);
            Assert.True(run.Elapsed < TimeSpan.FromSeconds(5), $"launcher took {run.Elapsed.TotalSeconds:F2}s against Codex's 5 s deadline");
            using var envelope = JsonDocument.Parse(File.ReadAllText(Path.Combine(CodexRoot, "sessions", "shared.json")));
            Assert.Equal("Stop", envelope.RootElement.GetProperty("event").GetString());
            Assert.Equal("held-open", envelope.RootElement.GetProperty("payload").GetProperty("session_id").GetString());
            var marker = Assert.Single(Directory.GetFiles(CodexHealthDirectory(_hook), "failure-*.json"));
            using var json = JsonDocument.Parse(File.ReadAllText(marker));
            Assert.Equal("input-timeout", json.RootElement.GetProperty("reason").GetString());
            Assert.Equal("delivery", json.RootElement.GetProperty("scope").GetString());
        }

        [WindowsFact]
        public void Cold_codex_session_end_with_held_stdin_finishes_within_its_configured_deadline()
        {
            // Codex starts PowerShell and executes commandWindows as source in that shell.
            // Do not warm the helper: the previous 3s budget failed on a cold copied executable.
            var bridge = new Agents.CodexStateBridge(Path.Combine(_temp, "codex-home")) { HookExe = _hook };
            using var hooks = JsonDocument.Parse(bridge.BuildHooksJson(windows: true));
            var handler = hooks.RootElement.GetProperty("hooks").GetProperty("SessionEnd")[0].GetProperty("hooks")[0];
            var deadline = handler.GetProperty("timeout").GetInt32() * 1000;
            var run = this.LaunchLauncher(handler.GetProperty("commandWindows").GetString(),
                "{\"session_id\":\"cold-session-end\"}", holdStdin: true, timeoutMs: deadline);
            Assert.Equal(0, run.ExitCode);
            Assert.True(run.Elapsed.TotalMilliseconds < deadline, $"SessionEnd took {run.Elapsed.TotalSeconds:F3}s");
            using var envelope = JsonDocument.Parse(File.ReadAllText(Path.Combine(CodexRoot, "sessions", "shared.json")));
            Assert.Equal("SessionEnd", envelope.RootElement.GetProperty("event").GetString());
            Assert.Equal("cold-session-end", envelope.RootElement.GetProperty("payload").GetProperty("session_id").GetString());
        }

        /// <summary>
        /// Run the installed command through Codex's PowerShell shell — no agent ancestor, so no
        /// session key — with stdin written and then either closed or held open.
        /// </summary>
        private DirectRun LaunchLauncher(String commandLine, String stdin, Boolean holdStdin, Int32 timeoutMs = 15000)
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = _bin,
            };
            foreach (var a in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", commandLine })
            {
                psi.ArgumentList.Add(a);
            }
            psi.Environment["TEMP"] = _temp;
            psi.Environment["TMP"] = _temp;

            var clock = Stopwatch.StartNew();
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start powershell.exe");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (stdin != null)
            {
                p.StandardInput.Write(stdin);
                p.StandardInput.Flush();
            }
            if (!holdStdin)
            {
                p.StandardInput.Close();
            }

            var exited = p.WaitForExit(timeoutMs);
            clock.Stop();
            if (holdStdin)
            {
                try { p.StandardInput.Close(); } catch { /* the launcher is gone; the pipe may be too */ }
            }
            if (!exited)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* gone */ }
                throw new Xunit.Sdk.XunitException($"the launcher did not exit within {timeoutMs} ms");
            }
            Task.WaitAll(new Task[] { stdout, stderr }, 2000);
            return new DirectRun(p.ExitCode, clock.Elapsed);
        }
    }
}
