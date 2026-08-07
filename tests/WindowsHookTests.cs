namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// The Windows bridge wiring (Phase 4): what gets written into settings.json, and the two
    /// things the hook shim MUST agree with the plugin about — the IPC root and the session key.
    ///
    /// That agreement is the whole ballgame. If either drifts, nothing crashes: the live keys just
    /// quietly show defaults forever, which is the hardest class of bug to notice. On macOS the
    /// bash scripts and the C# had to be hand-matched; here both sides are checked against each
    /// other by reading the shim's source.
    /// </summary>
    public class WindowsHookTests
    {
        private const String Exe = @"C:\Users\me\AppData\Local\Logi\Plugins\ClaudeConsole\bin\claude-console-hook.exe";

        // ---------------------------------------------------------------------------------------
        // Command construction
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Windows_wires_the_shim_with_a_verb()
        {
            Assert.Equal($"\"{Exe}\" statusline", BridgeWiring.StatuslineCommand(true, Exe));
            Assert.Equal($"\"{Exe}\" activity busy", BridgeWiring.ActivityCommand(true, Exe, "busy"));
        }

        [Fact]
        public void The_shim_path_is_quoted()
        {
            // The plugin lives under %LOCALAPPDATA%, and Claude Code hands hook commands to a
            // shell. Unquoted, a path with spaces runs the wrong program with the rest as args.
            var spacey = @"C:\Program Files\Logi\ClaudeConsole\claude-console-hook.exe";

            Assert.StartsWith($"\"{spacey}\"", BridgeWiring.StatuslineCommand(true, spacey));
        }

        [Fact]
        public void A_path_that_is_already_quoted_is_not_double_quoted()
        {
            var quoted = "\"" + Exe + "\"";

            Assert.Equal($"{quoted} statusline", BridgeWiring.StatuslineCommand(true, quoted));
        }

        [Fact]
        public void MacOS_wiring_is_unchanged()
        {
            // Phase 4 must not disturb the working macOS wiring.
            Assert.Equal("bash /home/me/.claude/claude-console/scripts/statusline-handler.sh",
                BridgeWiring.StatuslineCommand(false, "/home/me/.claude/claude-console/scripts/statusline-handler.sh"));
            Assert.Equal("bash /x/activity-hook.sh waiting",
                BridgeWiring.ActivityCommand(false, "/x/activity-hook.sh", "waiting"));
        }

        [Theory]
        [InlineData("bash /home/me/.claude/claude-console/scripts/statusline-handler.sh", true)]
        [InlineData("\"C:\\x\\claude-console-hook.exe\" statusline", true)]
        [InlineData("starship prompt", false)]
        [InlineData("powershell -c oh-my-posh", false)]
        [InlineData(null, false)]
        public void Our_own_command_is_recognised_on_both_platforms(String command, Boolean expected) =>
            Assert.Equal(expected, BridgeWiring.IsOurs(command));

        [Fact]
        public void A_users_existing_statusline_is_not_mistaken_for_ours()
        {
            // If this returned true for someone else's command, re-wiring would overwrite their
            // status bar instead of chaining it.
            Assert.False(BridgeWiring.IsOurs("~/.local/bin/my-status.sh"));
        }

        // ---------------------------------------------------------------------------------------
        // Contract with the shim (it is a separate executable; the compiler can't check this)
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void The_shim_uses_the_same_ipc_root_as_the_plugin()
        {
            // IpcPaths picks Path.GetTempPath() on Windows; the shim must do exactly the same, or
            // it writes state the plugin never reads.
            var source = ReadShimSource();

            Assert.Contains("Path.GetTempPath()", source);
            Assert.Contains("\"claude-console\"", source);
            Assert.Contains("\"sessions\"", source);
            Assert.Contains("\"activity\"", source);
        }

        [Fact]
        public void The_shim_mints_the_same_session_key_format_as_the_plugin()
        {
            // Both sides must produce "pid-<pid>-<utcStartTicks>".
            var expected = WindowsProcessWatcher.SessionKeyFor(new WindowsProcessInfo
            {
                Pid = 1234,
                StartTime = new DateTime(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc),
            });
            Assert.StartsWith("pid-1234-", expected);

            var source = ReadShimSource();
            Assert.Contains("$\"pid-{proc.Id}-{proc.StartTime.ToUniversalTime().Ticks}\"", source);
        }

        [Fact]
        public void The_shim_climbs_the_parent_chain_to_find_claude()
        {
            // A hook is spawned below Claude (Claude -> shell -> hook), so keying on its OWN pid
            // would produce a key matching no session. The bash scripts climb for the same reason.
            var source = ReadShimSource();

            Assert.Contains("ParentOf", source);
            Assert.Contains("IsClaude", source);
        }

        [Fact]
        public void The_shim_writes_the_shared_fallback_as_well_as_the_per_session_file()
        {
            // The plugin falls back to shared.json when it has no key match yet — without it, a
            // brand-new session shows defaults until the grid catches up.
            var source = ReadShimSource();

            Assert.Contains("SharedName", source);
            Assert.Contains("shared", source);
        }

        [Fact]
        public void The_shim_writes_atomically()
        {
            // The plugin polls every 500 ms; a non-atomic write would let it read a truncated file.
            var source = ReadShimSource();

            Assert.Contains("WriteAtomic", source);
            Assert.Contains("File.Move(tmp, path, overwrite: true)", source);
        }

        [Fact]
        public void The_shim_passes_claude_json_through_verbatim()
        {
            // All field parsing lives in ClaudeState. A shim that parsed would be a second place
            // to update whenever Claude Code's payload changes.
            var source = ReadShimSource();

            Assert.Contains("ReadToEnd", source);
            Assert.DoesNotContain("JsonSerializer.Deserialize", source);
        }

        [Fact]
        public void A_hook_failure_can_never_break_the_users_session()
        {
            var source = ReadShimSource();

            Assert.Contains("catch", source);
            Assert.Contains("return 1;", source);   // fails quietly rather than throwing
        }

        private static String ReadShimSource()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir, "tools", "windows", "ClaudeConsoleHook", "Program.cs");
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate the hook shim source");
        }
    }
}
