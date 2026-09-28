namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using Loupedeck.ClaudeConsolePlugin.Platform;
    using Xunit;

    public class WindowsLauncherHealthTests
    {
        /// <summary>
        /// #126: Codex runs the same launcher, aimed at its own IPC root. Its stdin can lack EOF
        /// after the payload, so this profile reads bounded chunks and runs the helper with what
        /// arrived instead of exiting without it — the event must never be the price of a slow pipe.
        /// </summary>
        [Fact]
        public void Codex_profile_aims_the_launcher_at_the_codex_root_and_forwards_partial_input()
        {
            const string path = @"C:\Users\O'Brien\AppData\Local\Logi\LogiPluginService\Plugins\VizhiCodex\bin\claude-console-hook.exe";
            var source = new Agents.CodexStateBridge { HookExe = path }.HookCommand("PermissionRequest", windows: true);
            Assert.StartsWith("# " + BridgeWiring.WindowsMarker + "\n", source);
            Assert.DoesNotContain("powershell.exe", source);
            Assert.Contains("'codex-console', 'hook-health', '" + WindowsHookHealth.HealthDirectoryName(path) + "'", source);
            Assert.DoesNotContain("'claude-console'", source);
            Assert.Contains(" 'codex' 'PermissionRequest'; ", source);
            Assert.Contains("Write-HookFailure 'missing' 'helper'", source);
            Assert.Contains("Write-HookFailure 'launch-failed' 'helper'", source);
            Assert.Contains("Write-HookFailure 'nonzero-exit' 'helper'", source);
            Assert.Contains("ReadAsync($buf, 0, $buf.Length)", source);
            Assert.Contains("$t.Wait(1500)", source);
            Assert.Contains("Write-HookFailure 'input-timeout' 'delivery' }; $payload = ", source);
            Assert.DoesNotContain("'input-timeout' 'delivery'; exit 0", source);
            Assert.Contains("$payload | & $helper", source);
            Assert.InRange(source.Length, 1, 1900);
            var sessionEnd = new Agents.CodexStateBridge { HookExe = path }.HookCommand("SessionEnd", windows: true);
            Assert.Contains("$t.Wait(500)", sessionEnd);
            Assert.DoesNotContain("powershell.exe", sessionEnd);
        }

        [Fact]
        public void The_claude_profile_is_the_default_and_its_text_did_not_move()
        {
            // UpgradeOwnedCommands rewrites every user's settings.json the moment this differs
            // from what is installed, so the 2.3.2 text is the contract.
            const string path = @"C:\Users\Ravi Shankar\AppData\Local\Logi\LogiPluginService\Plugins\ClaudeConsole\bin\claude-console-hook.exe";
            var command = BridgeWiring.WindowsCommand(path, "statusline");
            Assert.Equal(command, BridgeWiring.WindowsCommand(path, BridgeWiring.LauncherProfile.Claude, "statusline"));
            var source = WindowsHookTests.DecodeLauncher(command);
            Assert.Contains("'claude-console', 'hook-health', '", source);
            Assert.Contains("try { $read = [Console]::In.ReadToEndAsync(); if (-not $read.Wait(5000)) { Write-HookFailure 'input-timeout' 'delivery'; exit 0 }; " +
                "$ErrorActionPreference = 'Stop'; $LASTEXITCODE = 0; $read.Result | & $helper 'statusline'; $code = $LASTEXITCODE; ", source);
        }

        [Fact]
        public void Failure_reporting_keeps_path_and_arguments_literal_across_shells()
        {
            const string path = @"C:\Users\O'Brien\café & $ tools\claude-console-hook.exe";
            var command = BridgeWiring.WindowsCommand(path, "activity", "permission");
            var source = WindowsHookTests.DecodeLauncher(command);
            Assert.StartsWith("powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand ", command);
            Assert.Contains(@"'C:\Users\O''Brien\café & $ tools\claude-console-hook.exe'", source);
            Assert.Contains("'activity' 'permission'", source);
            Assert.True(BridgeWiring.IsOurs(command));
            Assert.True(BridgeWiring.IsOurHook(command));
            Assert.Contains("ReadToEndAsync()", source);
            Assert.Contains("$read.Wait(5000)", source);
            Assert.Contains("UTF8Encoding", source);
        }

        [Fact]
        public void Launcher_carries_the_health_directory_as_a_literal_and_no_hashing_code()
        {
            // The plugin, the launcher and the exe must agree on one directory name. The launcher
            // gets it precomputed: no SHA-256, no JSON cmdlets, no Get-ChildItem in six copies of
            // the script inside settings.json.
            const string path = @"C:\Users\O'Brien\AppData\Local\Logi\LogiPluginService\Plugins\ClaudeConsole\bin\claude-console-hook.exe";
            var source = WindowsHookTests.DecodeLauncher(BridgeWiring.WindowsCommand(path, "statusline"));
            Assert.Contains("'claude-console', 'hook-health', '" + WindowsHookHealth.HealthDirectoryName(path) + "'", source);
            Assert.DoesNotContain("SHA256", source);
            Assert.DoesNotContain("ConvertTo-Json", source);
            Assert.DoesNotContain("Get-ChildItem", source);
            // Every failure the launcher can record names its scope; the plugin blocks on "helper" only.
            Assert.Contains("Write-HookFailure 'missing' 'helper'", source);
            Assert.Contains("Write-HookFailure 'launch-failed' 'helper'", source);
            Assert.Contains("Write-HookFailure 'nonzero-exit' 'helper'", source);
            Assert.Contains("Write-HookFailure 'input-timeout' 'delivery'", source);
            Assert.Contains("\"scope\":\"' + $scope + '\"", source);
        }

        [Fact]
        public void Encoded_launcher_stays_small_enough_for_six_copies_in_settings_json()
        {
            // The original launcher was ~1,700 characters; 27fd842's failure reporting took it to
            // ~4,900 (six entries ≈ 29 KB of base64 in the user's settings.json). With the health
            // directory baked in and the cleanup moved to the plugin it is 3,874 for this path.
            // Hold that line: anything added here is added six times to a file users keep in git.
            const string path = @"C:\Users\Ravi Shankar\AppData\Local\Logi\LogiPluginService\Plugins\ClaudeConsole\bin\claude-console-hook.exe";
            Assert.InRange(BridgeWiring.ActivityCommand(true, path, "permission").Length, 1, 4100);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(260)]
        public void Encoded_launcher_leaves_room_under_the_cmd_command_line_limit(int pathLength)
        {
            const string file = @"\claude-console-hook.exe";
            var path = @"C:\" + new string('x', pathLength - 3 - file.Length) + file;
            var command = BridgeWiring.ActivityCommand(true, path, "permission");
            // cmd.exe's 8191-character limit includes surrounding shell syntax such as stdin
            // redirection. Leave headroom; long Windows profiles must not lose all hooks simply
            // because failure reporting made the encoded launcher too large.
            Assert.InRange(command.Length, 1, 7600);
            var source = WindowsHookTests.DecodeLauncher(command);
            Assert.Equal(1, source.Split(path).Length - 1);
        }
    }
}
