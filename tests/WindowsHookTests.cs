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
        internal static String DecodeLauncher(String command) => System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(command.Split(' ').Last()));
        private const String Exe = @"C:\Users\me\AppData\Local\Logi\Plugins\ClaudeConsole\bin\claude-console-hook.exe";

        // ---------------------------------------------------------------------------------------
        // Command construction
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Windows_wires_the_shim_with_a_verb()
        {
            Assert.Contains("'statusline'", DecodeLauncher(BridgeWiring.StatuslineCommand(true, Exe)));
            Assert.Contains("'activity' 'busy'", DecodeLauncher(BridgeWiring.ActivityCommand(true, Exe, "busy")));
        }

        [Fact]
        public void The_shim_path_is_quoted()
        {
            // The plugin lives under %LOCALAPPDATA%, and Claude Code hands hook commands to a
            // shell. Unquoted, a path with spaces runs the wrong program with the rest as args.
            var spacey = @"C:\Program Files\Logi\ClaudeConsole\claude-console-hook.exe";

            var command = BridgeWiring.StatuslineCommand(true, spacey);
            Assert.Contains($"$helper = '{spacey}'", DecodeLauncher(command));
            Assert.Contains("Test-Path -LiteralPath $helper", DecodeLauncher(command));
            Assert.Contains("& $helper 'statusline'", DecodeLauncher(command));
        }

        [Fact]
        public void A_path_that_is_already_quoted_is_not_double_quoted()
        {
            var quoted = "\"" + Exe + "\"";

            Assert.Equal(BridgeWiring.StatuslineCommand(true, Exe), BridgeWiring.StatuslineCommand(true, quoted));
        }

        [Fact]
        public void MacOS_wiring_is_unchanged()
        {
            // Phase 4 must not disturb the working macOS wiring. The macOS form itself changed once
            // since, deliberately, for #55 (the command checks that its script still exists) — the
            // guard here is that the Windows flag never leaks into it. TolerantWiringTests owns the
            // macOS form's meaning.
            Assert.Equal("[ ! -f \"/home/me/.claude/claude-console/scripts/statusline-handler.sh\" ] || bash \"/home/me/.claude/claude-console/scripts/statusline-handler.sh\"",
                BridgeWiring.StatuslineCommand(false, "/home/me/.claude/claude-console/scripts/statusline-handler.sh"));
            Assert.Equal("[ ! -f \"/x/activity-hook.sh\" ] || bash \"/x/activity-hook.sh\" waiting",
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

        // ---------------------------------------------------------------------------------------
        // #57 — a hook must not be able to outlive its usefulness. Logitech QA's 2.2.0 retest
        // found ~15 claude-console-hook processes left behind after one session following a
        // reboot; the machine froze until the plugin service was shut down. Cannot be run here
        // (win-x64 exe); the shape of the guarantee is pinned at the source instead.
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void The_shim_arms_a_watchdog_before_it_does_anything_that_could_block()
        {
            var source = ReadShimSource();
            var main = source.Substring(source.IndexOf("private static Int32 Main(", StringComparison.Ordinal));

            var watchdog = main.IndexOf("StartWatchdog(", StringComparison.Ordinal);
            var breadcrumb = main.IndexOf("EntryBreadcrumb(args);", StringComparison.Ordinal);
            var dispatch = main.IndexOf("args[0] == \"statusline\"", StringComparison.Ordinal);
            Assert.True(watchdog >= 0, "Main no longer arms the watchdog");
            Assert.True(watchdog < breadcrumb, "the watchdog must be armed before even diagnostic file I/O");
            Assert.True(watchdog < dispatch, "the watchdog must be armed before the verb dispatch");

            // Background, so it can never be the thing keeping the process alive; exit 0, so a
            // timed-out hook is not a hook error in the user's session. Nothing may log before the
            // exit on this thread: a blocked log write would defeat the watchdog itself.
            Assert.Contains("IsBackground = true", source);
            Assert.Contains("Environment.Exit(0)", source);
            Assert.Matches(@"WatchdogSeconds\s*=\s*\d+;", source);
            var watchdogBody = source.Substring(source.IndexOf("private static Boolean StartWatchdog(", StringComparison.Ordinal));
            watchdogBody = watchdogBody.Substring(0, watchdogBody.IndexOf("private static", 10, StringComparison.Ordinal));
            Assert.DoesNotContain("Breadcrumb(", watchdogBody);
            Assert.Contains("return false;", watchdogBody);
        }

        [Fact]
        public void The_shim_never_reads_stdin_without_a_time_limit()
        {
            // One unbounded ReadToEnd on Console.In is one way to live forever. The bounded reader
            // is the only place allowed to call it.
            var source = ReadShimSource();
            var bounded = source.IndexOf("private static String ReadStdinBounded(", StringComparison.Ordinal);
            var body = source.Substring(bounded);
            var outside = source.Substring(0, bounded);

            Assert.DoesNotContain("Console.In.ReadToEnd()", outside);
            Assert.Contains("Console.In.ReadToEnd()", body.Substring(0, body.IndexOf("private static", 10, StringComparison.Ordinal)));
            Assert.Contains("ReadStdinBounded(1500)", source.Substring(source.IndexOf("private static Int32 Statusline()", StringComparison.Ordinal), 400));
            Assert.Contains("ReadStdinBounded(1500)", source.Substring(source.IndexOf("if (state == \"permission\")", StringComparison.Ordinal), 200));
        }

        [Fact]
        public void The_powershell_fallback_waits_with_its_limit_before_reading()
        {
            // ReadToEnd() returns when PowerShell exits, so "read, then WaitForExit(4000)" waited
            // for a PowerShell cold start however long it took — per hop, per hook. The read must
            // be in the background and the wait must come first.
            var source = ReadShimSource();
            var wmic = source.Substring(source.IndexOf("private static String? Wmic(", StringComparison.Ordinal));

            var read = wmic.IndexOf("ReadToEndAsync()", StringComparison.Ordinal);
            var wait = wmic.IndexOf("WaitForExit(4000)", StringComparison.Ordinal);
            Assert.True(read >= 0 && wait > read, "Wmic must start the read in the background and then wait with the limit");
            Assert.DoesNotContain("StandardOutput.ReadToEnd()", wmic.Substring(0, wmic.IndexOf("private static", 10, StringComparison.Ordinal)));
        }

        [Fact]
        public void The_shim_refuses_to_join_a_pile_up()
        {
            var source = ReadShimSource();

            Assert.Matches(@"MaxConcurrentHooks\s*=\s*\d+;", source);
            Assert.Contains("Process.GetProcessesByName(name).Length", source);
            Assert.Contains("if (TooManyOfUs())", source);
            Assert.Contains("MaxConcurrentHooks = 8", source);   // below QA's ~15-process freeze
        }

        [Fact]
        public void A_timed_out_chained_status_line_is_killed_with_its_children()
        {
            var source = ReadShimSource();
            var chained = source.Substring(source.IndexOf("private static void RunChained(", StringComparison.Ordinal));

            Assert.Contains("p.StandardInput.WriteAsync(stdin)", chained);
            Assert.Contains("if (!write.Wait(2000))", chained);
            Assert.Contains("exited = p.WaitForExit(2000)", chained);
            Assert.Contains("if (!exited)", chained);
            Assert.Contains("KillTree(p)", chained);
            Assert.Contains("process.Kill(entireProcessTree: true)", chained);
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

        // ---------------------------------------------------------------------------------------
        // Auto-wiring reaches Windows at all (the 1.8.x bug: a macOS-only early return meant
        // settings.json was never touched, so the live keys showed defaults with nothing logged)
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Auto_wiring_is_supported_on_this_platform()
        {
            // Runs on macOS AND Windows. On Windows this is the test that was missing: the gate
            // read `if (!OperatingSystem.IsMacOS()) return;` while every wiring test below it
            // exercised only the pure string helpers that gate never let run.
            Assert.True(BridgeManager.AutoWireSupported);
        }

        [Fact]
        public void The_hook_shim_is_located_relative_to_the_plugin_dll_not_the_host_process()
        {
            // Same bug as the inject helper, second copy: FindHookExe still used
            // AppContext.BaseDirectory — the SERVICE's directory under the SDK's load context —
            // and it ran in a field initialiser, before Plugin.Load supplies the real path.
            var dir = Path.Combine(Path.GetTempPath(), "cc-hook-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, "claude-console-hook.exe");
            File.WriteAllText(exe, "");

            var previous = PluginPaths.PluginAssemblyFilePath;
            try
            {
                // Constructed BEFORE the SDK path lands, exactly like the real load order.
                var manager = new BridgeManager();
                PluginPaths.PluginAssemblyFilePath = Path.Combine(dir, "ClaudeConsolePlugin.dll");

                Assert.Equal(exe, manager.HookExePath);
            }
            finally
            {
                PluginPaths.PluginAssemblyFilePath = previous;
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        // ---------------------------------------------------------------------------------------
        // Re-wiring is idempotent on BOTH platforms. The check used to match "activity-hook.sh"
        // only — the macOS script name, which no Windows command contains — so every plugin load
        // on Windows would have appended five more hook entries to settings.json.
        // ---------------------------------------------------------------------------------------

        [Theory]
        [InlineData("bash /home/me/.claude/claude-console/scripts/activity-hook.sh busy", true)]
        [InlineData("\"C:\\x\\claude-console-hook.exe\" activity busy", true)]
        [InlineData("starship prompt", false)]
        [InlineData(null, false)]
        public void Our_own_activity_hook_is_recognised_on_both_platforms(String command, Boolean expected) =>
            Assert.Equal(expected, BridgeWiring.IsOurHook(command));

        [Fact]
        public void Rewiring_on_windows_does_not_duplicate_hooks()
        {
            var command = BridgeWiring.ActivityCommand(true, Exe, "busy");
            var hooks = new System.Text.Json.Nodes.JsonObject();

            Assert.True(BridgeManager.EnsureHook(hooks, "Stop", null, command));    // first wire: added
            Assert.False(BridgeManager.EnsureHook(hooks, "Stop", null, command));   // reload: no-op

            Assert.Single((System.Text.Json.Nodes.JsonArray)hooks["Stop"]);
        }

        [Fact]
        public void A_users_own_hook_is_kept_and_ours_is_appended_beside_it()
        {
            var hooks = new System.Text.Json.Nodes.JsonObject
            {
                ["Stop"] = new System.Text.Json.Nodes.JsonArray
                {
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["hooks"] = new System.Text.Json.Nodes.JsonArray
                        {
                            new System.Text.Json.Nodes.JsonObject
                            {
                                ["type"] = "command",
                                ["command"] = "notify-send done",
                            },
                        },
                    },
                },
            };

            Assert.True(BridgeManager.EnsureHook(hooks, "Stop", null, BridgeWiring.ActivityCommand(true, Exe, "done")));
            Assert.Equal(2, ((System.Text.Json.Nodes.JsonArray)hooks["Stop"]).Count);
        }

        // ---------------------------------------------------------------------------------------
        // The CODEX verb — the Windows body of scripts/codex-hook.sh. Found missing on hardware
        // 2026-08-20: the bridge wrote "/bin/sh …" into hooks.json on Windows, a command that can
        // never run there, so no state ever arrived. These pin both sides of the fix.
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void The_codex_hook_command_is_the_exe_verb_on_windows_and_the_script_on_macos()
        {
            var bridge = new Agents.CodexStateBridge(codexHome: @"C:\Users\me\.codex") { HookExe = Exe };

            Assert.Equal($"& '{Exe}' codex SessionStart", bridge.HookCommand("SessionStart", windows: true));
            Assert.Contains("/bin/sh '", bridge.HookCommand("SessionStart", windows: false));
            Assert.Contains("codex-hook.sh' SessionStart", bridge.HookCommand("SessionStart", windows: false));
        }

        /// <summary>
        /// Codex executes commandWindows as PowerShell source. Without the call operator, a
        /// quoted executable path is parsed as a string followed by an unexpected token: Codex
        /// reports exit code 1 and the helper never reaches its first breadcrumb instruction.
        /// Exercise the real shell boundary, not only the generated string.
        /// </summary>
        [Fact]
        public void The_codex_hook_command_is_valid_powershell_source()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var bridge = new Agents.CodexStateBridge(codexHome: @"C:\Users\me\.codex")
            {
                HookExe = "Write-Output",
            };
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(bridge.HookCommand("SessionStart", windows: true));

            using var process = System.Diagnostics.Process.Start(psi);
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            Assert.True(process.WaitForExit(10_000), "PowerShell did not finish the hook command");
            Assert.True(process.ExitCode == 0, $"PowerShell rejected the hook command: {stderr}");
            Assert.Equal(new[] { "codex", "SessionStart" },
                stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        }

        [Fact]
        public void The_codex_hook_command_quotes_powershell_literal_paths()
        {
            const String tricky = @"C:\Users\O'Brien\$hooks\claude-console-hook.exe";
            var bridge = new Agents.CodexStateBridge(codexHome: @"C:\Users\me\.codex") { HookExe = tricky };

            Assert.Equal("& 'C:\\Users\\O''Brien\\$hooks\\claude-console-hook.exe' codex Stop",
                bridge.HookCommand("Stop", windows: true));
        }

        [Fact]
        public void The_shim_dispatches_the_codex_verb()
        {
            var src = ReadShimSource();

            Assert.Contains("args[0] == \"codex\" => Codex(args[1])", src);
        }

        [Fact]
        public void The_codex_verb_writes_to_the_codex_products_ipc_root()
        {
            // Two consoles must never share an IPC root — each reaps sessions it can't see.
            var src = ReadShimSource();

            Assert.Contains("Path.Combine(Path.GetTempPath(), \"codex-console\")", src);
            Assert.Contains("Path.Combine(CodexRoot, \"sessions\")", src);
        }

        [Fact]
        public void The_codex_verb_writes_the_scripts_envelope()
        {
            // Field-for-field the envelope scripts/codex-hook.sh writes and CodexStateReader parses.
            var src = ReadShimSource();

            Assert.Contains("\\\"schema\\\":1,\\\"agent\\\":\\\"codex-cli\\\",\\\"event\\\":", src);
            Assert.Contains("\\\"ts\\\":{ts},\\\"payload\\\":{body}", src);

            // No transport field: the rollout fallback is the only writer that stamps one, so the
            // reader infers "hook" from its absence. Keeping it out holds the launcher's bytes
            // stable and keeps envelopes written before the field was invented readable.
            Assert.DoesNotContain("\\\"transport\\\"", src);
        }

        /// <summary>
        /// Codex surfaces a nonzero hook exit to the USER ("hook exited with code 1" appeared in
        /// the TUI on Windows hardware, 2026-08-20), so the codex verb must be structurally unable
        /// to produce one: every statement, including the final {} write, is inside a guard, and
        /// failures leave a breadcrumb file instead of an exit code.
        /// </summary>
        [Fact]
        public void The_codex_verb_cannot_exit_nonzero_and_never_fails_silently()
        {
            var src = ReadShimSource();

            Assert.Contains("try { Console.Write(\"{}\"); } catch (Exception ex) { Breadcrumb(ex, eventName); }", src);
            Assert.Contains("hook-error.log", src);
        }

        /// <summary>
        /// Codex TERMINATES a hook that outlives its timeout — exit code 1, no exception, no
        /// breadcrumb — which is exactly what hardware showed while parent lookups spawned
        /// PowerShell (seconds of cold start per hop). Two defenses, both pinned: the shared
        /// state file is written BEFORE any process walking, and parent lookups go through the
        /// kernel before ever considering a PowerShell spawn.
        /// </summary>
        [Fact]
        public void The_codex_verb_writes_evidence_before_walking_and_walks_without_powershell()
        {
            var src = ReadShimSource();

            var sharedWrite = src.IndexOf(
                "WriteAtomic(Path.Combine(CodexSessionsDir, SharedName + \".json\"), envelope)",
                StringComparison.Ordinal);
            var climb = src.IndexOf("SessionKeyTopmost(IsCodex)", StringComparison.Ordinal);

            Assert.True(sharedWrite >= 0, "the codex verb must write the shared file");
            Assert.True(climb > sharedWrite, "the shared write must come BEFORE the ancestor climb");
            Assert.Contains("NtQueryInformationProcess", src);
        }

        /// <summary>
        /// The codex verb must never read stdin unbounded. On Windows the hook's stdin can fail
        /// to deliver EOF even after the payload is fully written (inherited pipe write handles),
        /// so a bare ReadToEnd hangs until codex kills the hook at its timeout — kill code 1,
        /// nothing written, no exception to breadcrumb. The bounded read forfeits the payload on
        /// timeout but always records the event.
        /// </summary>
        [Fact]
        public void The_codex_verb_reads_stdin_with_a_time_bound()
        {
            var src = ReadShimSource();

            Assert.Contains("ReadStdinBounded(", src);
            Assert.Contains("read.Wait(ms) ? read.Result : \"\"", src);
        }

        [Fact]
        public void The_codex_verb_resolves_the_codex_process_not_claude()
        {
            var src = ReadShimSource();

            Assert.Contains("SessionKeyTopmost(IsCodex)", src);
            // Through IsExe, like IsClaude: a codex renamed by an in-place update must still match.
            Assert.Contains("IsExe(name, \"codex\")", src);
            Assert.Contains("@openai\\codex", src);
        }

        [Fact]
        public void The_shim_recognises_a_claude_renamed_by_an_in_place_update()
        {
            // Claude Code's updater renames the RUNNING claude.exe to claude.exe.old.<epoch-ms>
            // and .NET reports that as the process name. The 2026-09-11 finding: from the 06:58
            // auto-update on, every hook in a day-old session climbed past its own Claude.
            var src = ReadShimSource();

            Assert.Contains("IsExe(name, \"claude\")", src);
            Assert.Contains("processName.StartsWith(exe + \".exe.\", StringComparison.OrdinalIgnoreCase)", src);
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
