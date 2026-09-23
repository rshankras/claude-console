namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json.Nodes;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// The macOS hook and status-line commands survive their script being deleted (#55).
    ///
    /// Older releases did not implement the SDK uninstall callback, so an Options+
    /// uninstall left wiring in ~/.claude/settings.json. Logitech QA's 2.2.0
    /// retest filed the leftovers on Windows and confirmed them on macOS. Reproduced 2026-09-03
    /// with a throwaway settings file: with the old commands and the folder gone, Claude Code
    /// raised "Stop hook error occurred" on every turn; with a command that checks for the script
    /// first, nothing — and the script still runs when it is there.
    /// </summary>
    public class TolerantWiringTests
    {
        private const String Script = "/Users/me/.claude/claude-console/scripts/activity-hook.sh";
        private const String Handler = "/Users/me/.claude/claude-console/scripts/statusline-handler.sh";

        [Fact]
        public void The_mac_commands_check_for_the_script_before_running_it()
        {
            Assert.Equal($"[ ! -f \"{Script}\" ] || bash \"{Script}\" busy", BridgeWiring.ActivityCommand(false, Script, "busy"));
            Assert.Equal($"[ ! -f \"{Handler}\" ] || bash \"{Handler}\"", BridgeWiring.StatuslineCommand(false, Handler));
        }

        [Fact]
        public void The_guarded_commands_are_still_recognised_as_ours_and_so_is_the_old_form()
        {
            // Inspect / Unwire find our entries by the script name inside the command. Both the new
            // form and the form 2.2.0 wrote must match, or an existing install would be judged
            // "not wired" and get a duplicate set appended on the next Turn on.
            Assert.True(BridgeWiring.IsOurHook(BridgeWiring.ActivityCommand(false, Script, "done")));
            Assert.True(BridgeWiring.IsOurs(BridgeWiring.StatuslineCommand(false, Handler)));
            Assert.True(BridgeWiring.IsOurHook($"bash {Script} done"));
            Assert.True(BridgeWiring.IsOurs($"bash {Handler}"));
        }

        [Fact]
        public void The_windows_commands_check_for_the_exe_before_running_it()
        {
            const String exe = @"C:\Users\me\.claude\claude-console\claude-console-hook.exe";

            Assert.Contains("Test-Path -LiteralPath", WindowsHookTests.DecodeLauncher(BridgeWiring.ActivityCommand(true, exe, "busy")));
            Assert.Contains("Test-Path -LiteralPath", WindowsHookTests.DecodeLauncher(BridgeWiring.StatuslineCommand(true, exe)));
        }

        [Fact]
        public void Existing_owned_commands_are_migrated_but_user_commands_are_not()
        {
            const String exe = @"C:\Users\me\.claude\claude-console\claude-console-hook.exe";
            var root = new JsonObject
            {
                ["statusLine"] = new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = $"\"{exe}\" statusline",
                },
                ["hooks"] = new JsonObject
                {
                    ["Stop"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["hooks"] = new JsonArray
                            {
                                new JsonObject { ["type"] = "command", ["command"] = $"\"{exe}\" activity done" },
                                new JsonObject { ["type"] = "command", ["command"] = "my-stop-handler" },
                            },
                        },
                    },
                },
            };

            Assert.True(BridgeWiring.UpgradeOwnedCommands(root, true, exe, exe));

            Assert.Equal(BridgeWiring.StatuslineCommand(true, exe), root["statusLine"]["command"].GetValue<String>());
            var commands = root["hooks"]["Stop"][0]["hooks"].AsArray();
            Assert.Equal(BridgeWiring.ActivityCommand(true, exe, "done"), commands[0]["command"].GetValue<String>());
            Assert.Equal("my-stop-handler", commands[1]["command"].GetValue<String>());
            Assert.False(BridgeWiring.UpgradeOwnedCommands(root, true, exe, exe));
        }

        [Fact]
        public void A_missing_script_is_a_silent_no_op_and_a_present_one_still_runs()
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            {
                return;   // the command is a POSIX shell line; Windows has its own form
            }

            var dir = Path.Combine(Path.GetTempPath(), "cc-tolerant-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var script = Path.Combine(dir, "activity-hook.sh");
                var marker = Path.Combine(dir, "ran.txt");

                // Deleted folder: exit 0, nothing on stderr — Claude Code has nothing to report.
                var missing = Run(BridgeWiring.ActivityCommand(false, script, "busy"));
                Assert.Equal(0, missing.ExitCode);
                Assert.Equal(String.Empty, missing.Stderr.Trim());

                // Present script: it runs with its argument, and its own exit code is what counts.
                File.WriteAllText(script, $"#!/usr/bin/env bash\necho \"$1\" > \"{marker}\"\nexit 7\n");
                var present = Run(BridgeWiring.ActivityCommand(false, script, "permission"));
                Assert.Equal(7, present.ExitCode);
                Assert.Equal("permission", File.ReadAllText(marker).Trim());
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        private static (Int32 ExitCode, String Stderr) Run(String command)
        {
            var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
            using var p = Process.Start(psi);
            var stderr = p.StandardError.ReadToEnd();
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, stderr);
        }
    }
}
