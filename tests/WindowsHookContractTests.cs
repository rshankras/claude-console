namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck.ClaudeConsolePlugin.Agents;
    using Loupedeck.ClaudeConsolePlugin.Models;
    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// The Windows hook exe and the plugin's reader, both ends running for real: the exe published
    /// the way the package publishes it, the reader being SessionRegistry over the files the exe
    /// wrote. WindowsHookTests pins the same contract by reading the exe's SOURCE; this runs it.
    ///
    /// Why both exist: the source checks were green while the exe wrote state "permission" and the
    /// reader wanted "waiting", so every Yes/No press on Windows was discarded (#74, 2.2.1 retest
    /// item 2). Each half had tests. Nothing ran the two against each other.
    ///
    /// Isolation, since the live plugin's hooks run on this machine during the suite:
    ///  - the exe takes its IPC root from %TEMP% (Path.GetTempPath()), so every test hands it a
    ///    private TEMP;
    ///  - it keys the session by climbing its ancestry to a process named "claude", so the tests
    ///    launch it under a renamed cmd.exe standing in for Claude — one stand-in, many hooks, the
    ///    way one session fires many events;
    ///  - it counts copies of ITSELF by process name (#57), so the copy under test carries a unique
    ///    name and neither counts nor is counted by the real hooks.
    /// </summary>
    public class WindowsHookContractTests : IClassFixture<HookExeFixture>, IDisposable
    {
        private readonly String _dir;      // scratch for this one test
        private readonly String _name;     // unique base name of the exe copy under test
        private readonly String _bin;      // the copy, the payload files, and hook-invoked.log
        private readonly String _hook;
        private readonly String _claude;   // the stand-in claude.exe
        private readonly String _updated;  // the same stand-in as Claude Code's updater leaves a RUNNING one
        private readonly String _temp;     // the TEMP the hook is given

        private String Root => Path.Combine(_temp, "claude-console");
        private String SessionsDir => Path.Combine(Root, "sessions");
        private String ActivityDir => Path.Combine(Root, "activity");
        private String Log => Path.Combine(_bin, "hook-invoked.log");

        public WindowsHookContractTests(HookExeFixture exe)
        {
            _dir = Path.Combine(Path.GetTempPath(), "cc-hook-contract-" + Guid.NewGuid().ToString("N"));
            _name = "cc-hook-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _bin = Path.Combine(_dir, "bin");
            _hook = Path.Combine(_bin, _name + ".exe");
            _claude = Path.Combine(_dir, "claude", "claude.exe");
            _updated = Path.Combine(_dir, "claude", "claude.exe.old.1789090133131");
            _temp = Path.Combine(_dir, "temp");

            if (exe.ExePath == null)
            {
                return;   // not Windows: every fact below is reported skipped
            }

            Directory.CreateDirectory(_bin);
            Directory.CreateDirectory(Path.GetDirectoryName(_claude));
            Directory.CreateDirectory(_temp);
            File.Copy(exe.ExePath, _hook);
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), _claude);
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), _updated);
            File.Copy(Payload("Status.json"), Path.Combine(_bin, "status.json"));
            File.Copy(Payload("PermissionRequest.json"), Path.Combine(_bin, "permission.json"));
        }

        public void Dispose()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try { Directory.Delete(_dir, recursive: true); return; }
                catch (IOException) { Thread.Sleep(100); }
                catch (UnauthorizedAccessException) { Thread.Sleep(100); }
                catch { return; }
            }
        }

        [WindowsFact]
        public void Generated_launcher_preserves_unicode_payload_and_session_identity_through_powershell()
        {
            using var claude = this.StartClaude();
            var specialDir = Path.Combine(_bin, "quoted ' & $ folder");
            Directory.CreateDirectory(specialDir);
            var specialHook = Path.Combine(specialDir, _name + ".exe");
            File.Copy(_hook, specialHook);
            var command = BridgeWiring.StatuslineCommand(true, specialHook);
            var value = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Payload("Status.json")));
            value["encoding_probe"] = "café ₹ 日本語";
            var payload = value.ToJsonString(new JsonSerializerOptions
            { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
            File.WriteAllText(Path.Combine(_bin, "status.json"), payload, new System.Text.UTF8Encoding(false));
            Assert.Equal(0, claude.Run(command + " < status.json", 30000));
            var output = File.ReadAllText(Path.Combine(SessionsDir, claude.Key + ".json"));
            Assert.Equal(payload.TrimEnd(), output.TrimEnd());
            Assert.Equal(0, claude.Run(BridgeWiring.ActivityCommand(true, _hook, "permission") + " < permission.json", 30000));
            Assert.Equal("waiting", ActivityWord(claude.Key));
        }

        [WindowsGitBashFact]
        public void Generated_launcher_reaches_the_helper_through_git_bash_instead_of_executing_json_as_commands()
        {
            using var claude = this.StartClaude();
            var command = BridgeWiring.StatuslineCommand(true, _hook);
            Assert.Equal(0, claude.Run($"\"{WindowsGitBashFactAttribute.BashPath}\" -c \"{command} < status.json\"", 30000));
            Assert.True(File.Exists(Path.Combine(SessionsDir, claude.Key + ".json")));
            Assert.Equal(0, claude.Run($"\"{WindowsGitBashFactAttribute.BashPath}\" -c \"{BridgeWiring.ActivityCommand(true, _hook, "permission")} < permission.json\"", 30000));
            Assert.Equal("waiting", ActivityWord(claude.Key));
        }

        [WindowsFact]
        public void Generated_launcher_is_silent_when_the_package_was_uninstalled()
        {
            using var claude = this.StartClaude();
            Assert.Equal(0, claude.Run(BridgeWiring.StatuslineCommand(true, Path.Combine(_bin, "missing.exe")) + " < status.json", 30000));
            Assert.False(File.Exists(Path.Combine(SessionsDir, claude.Key + ".json")));
        }

        // ---------------------------------------------------------------------------------------
        // #74 — the contract that broke
        // ---------------------------------------------------------------------------------------

        [WindowsFact]
        public void A_permission_request_lands_as_waiting_with_its_payload_beside_it()
        {
            using var claude = this.StartClaude();
            Assert.Equal(0, claude.Run($@".\{_name}.exe statusline < status.json"));
            Assert.Equal(0, claude.Run($@".\{_name}.exe activity permission < permission.json"));

            // The files, under the key the plugin would mint for that very process.
            var key = claude.Key;
            Assert.True(File.Exists(Path.Combine(SessionsDir, key + ".json")), "no session file under the plugin's key: " + key);
            Assert.Equal("waiting", ActivityWord(key));
            Assert.Equal(File.ReadAllText(Payload("PermissionRequest.json")), File.ReadAllText(Path.Combine(ActivityDir, "pending-" + key + ".json")));

            // The reader — the exact path the Yes/No keys and the badge go through.
            var session = this.ReadSession(key);
            Assert.NotNull(session);
            Assert.Equal("waiting", session.State);
            Assert.Equal("PowerShell", session.PendingTool);
            Assert.Equal(ExpectedCommand(), session.PendingCommand);
            Assert.Equal(ApprovalRisk.High, session.Risk);   // the QA case: a recursive force-delete must show red
        }

        [WindowsFact]
        public void A_session_whose_claude_was_renamed_by_an_update_still_gets_its_own_key()
        {
            // Claude Code updates itself in place while sessions run. Windows cannot overwrite a
            // running image, so the updater RENAMES it (claude.exe.old.<epoch-ms>) and drops the
            // new claude.exe beside it; .NET then reports the renamed image as the process name.
            // Found 2026-09-11: a session up since the previous evening lost every per-session
            // write at the 06:58 auto-update — hooks climbed past their own Claude, Yes/No said
            // "no pending approval" on a session the plugin had pinned, and the plugin's WMI-based
            // discovery (creation-time name) never noticed.
            using var claude = this.StartClaude(_updated);
            Assert.Equal(0, claude.Run($@".\{_name}.exe statusline < status.json"));
            Assert.Equal(0, claude.Run($@".\{_name}.exe activity permission < permission.json"));

            var key = claude.Key;
            Assert.True(File.Exists(Path.Combine(SessionsDir, key + ".json")), "the renamed Claude minted no per-session file");
            Assert.Equal("waiting", ActivityWord(key));
            Assert.Equal("PowerShell", this.ReadSession(key).PendingTool);
        }

        [WindowsFact]
        public void The_status_line_is_written_verbatim_to_the_session_file_and_the_shared_fallback()
        {
            using var claude = this.StartClaude();
            var payload = File.ReadAllText(Payload("Status.json"));

            Assert.Equal(0, claude.Run($@".\{_name}.exe statusline < status.json"));

            // Verbatim on both files: all parsing belongs to the plugin (ClaudeState), so a shim
            // that reshaped the JSON would be a second place to update per Claude Code release.
            Assert.Equal(payload, File.ReadAllText(Path.Combine(SessionsDir, claude.Key + ".json")));
            Assert.Equal(payload, File.ReadAllText(Path.Combine(SessionsDir, "shared.json")));

            var state = JsonSerializer.Deserialize<ClaudeState>(File.ReadAllText(Path.Combine(SessionsDir, claude.Key + ".json")));
            Assert.Equal(1.25m, state.Cost.TotalCostUsd);
            Assert.Equal("Opus 5 (1M context)", state.Model.DisplayName);
            Assert.Equal(54, SessionRegistry.ContextPercent(state));
        }

        [WindowsFact]
        public void The_pending_payload_survives_the_late_notification_and_clears_on_busy_and_done()
        {
            // A permission menu fires PermissionRequest (with the payload) and then, ~6 s later, a
            // plain Notification ("waiting", no payload) while the same menu is still up. Clearing
            // the payload on that second event blinded the Yes/No keys to a menu they were about to
            // answer (#21/#51). Only busy and done may clear it.
            using var claude = this.StartClaude();
            var key = claude.Key;
            var pending = Path.Combine(ActivityDir, "pending-" + key + ".json");
            Assert.Equal(0, claude.Run($@".\{_name}.exe statusline < status.json"));

            Assert.Equal(0, claude.Run($@".\{_name}.exe activity permission < permission.json"));
            Assert.True(File.Exists(pending));

            Assert.Equal(0, claude.Run($@".\{_name}.exe activity waiting < nul"));
            Assert.True(File.Exists(pending), "the late Notification must not clear a pending approval");
            Assert.Equal("waiting", ActivityWord(key));
            Assert.Equal("PowerShell", this.ReadSession(key).PendingTool);

            Assert.Equal(0, claude.Run($@".\{_name}.exe activity busy < nul"));
            Assert.False(File.Exists(pending), "busy means the approval was answered");
            Assert.Equal("busy", ActivityWord(key));

            Assert.Equal(0, claude.Run($@".\{_name}.exe activity permission < permission.json"));
            Assert.True(File.Exists(pending));
            Assert.Equal(0, claude.Run($@".\{_name}.exe activity done < nul"));
            Assert.False(File.Exists(pending), "done means the turn is over");
            Assert.Equal("done", ActivityWord(key));
            Assert.Equal("ready", this.ReadSession(key).State);   // the grid's word for it
        }

        [WindowsFact]
        public void An_empty_permission_payload_records_the_state_but_no_pending_file()
        {
            // Law 5: a hook never breaks the session. Whatever stdin holds, the event still records,
            // the exit code is still 0, and nothing unparseable is left for the reader.
            using var claude = this.StartClaude();
            Assert.Equal(0, claude.Run($@".\{_name}.exe statusline < status.json"));

            Assert.Equal(0, claude.Run($@".\{_name}.exe activity permission < nul"));

            Assert.Equal("waiting", ActivityWord(claude.Key));
            Assert.False(File.Exists(Path.Combine(ActivityDir, "pending-" + claude.Key + ".json")));
            Assert.Null(this.ReadSession(claude.Key).PendingTool);
        }

        [WindowsFact]
        public void Without_a_claude_ancestor_only_the_shared_fallback_is_written()
        {
            // With no Claude up the chain the key is null, the shared last-writer-wins file is all
            // the plugin gets, and nothing else appears. "No Claude up the chain" has to be made
            // true, not assumed: this suite is often run from INSIDE a Claude Code session, whose
            // process sits above the test host — the hook found it the moment the renamed-image
            // fix landed, and this test had only ever passed because of that bug. So the hook is
            // launched with explorer.exe as its parent (PROC_THREAD_ATTRIBUTE_PARENT_PROCESS),
            // an ancestry that has no Claude in it wherever the suite runs from.
            var explorer = Process.GetProcessesByName("explorer").FirstOrDefault();
            if (explorer == null)
            {
                throw new Xunit.Sdk.XunitException("no explorer.exe to reparent under — this test needs an interactive session");
            }

            var run = LaunchReparented(explorer.Id, _hook, "activity busy", _bin, _temp);

            Assert.Equal(0, run.ExitCode);
            Assert.Equal(new[] { "shared.json" }, Directory.GetFiles(ActivityDir).Select(Path.GetFileName).ToArray());
            Assert.Equal("busy", ActivityWord("shared"));
            Assert.False(Directory.Exists(SessionsDir));
        }

        // ---------------------------------------------------------------------------------------
        // #57 — a hook must not be able to outlive its usefulness
        // ---------------------------------------------------------------------------------------

        [WindowsFact]
        public void Codex_session_end_with_an_open_input_pipe_finishes_before_its_three_second_deadline()
        {
            // Warm the copied binary before timing; this isolates the helper from antivirus startup.
            this.LaunchDirect("{}", false, "codex", "SessionStart");
            var run = this.LaunchDirect(null, true, "codex", "SessionEnd");
            Assert.Equal(0, run.ExitCode);
            Assert.True(run.Elapsed < TimeSpan.FromSeconds(3), $"hook took {run.Elapsed.TotalSeconds:F2}s");
            using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(_temp, "codex-console", "sessions", "shared.json")));
            Assert.Equal("SessionEnd", state.RootElement.GetProperty("event").GetString());
        }

        [WindowsFact]
        public void A_stdin_that_never_closes_cannot_keep_the_hook_alive()
        {
            // The pile-up QA saw after a reboot: ~15 hooks stuck, machine frozen. A stdin that never
            // reaches EOF is the simplest way to get there. The bounded read gives up at 1.5 s; the
            // event still records with what it has.
            // Measured against a normal run of the same copy: the first launch of a freshly copied
            // exe pays an antivirus scan, and the climb from the test host's ancestry costs what it
            // costs. Only the difference is the price of the open pipe.
            var baseline = this.LaunchDirect(stdin: "", holdStdin: false, "activity", "busy");
            var run = this.LaunchDirect(stdin: null, holdStdin: true, "activity", "permission");

            Assert.Equal(0, run.ExitCode);
            var extra = run.Elapsed - baseline.Elapsed;
            Assert.True(extra < TimeSpan.FromSeconds(4),
                $"stdin held open cost {extra.TotalSeconds:F1} s over a normal run of {baseline.Elapsed.TotalSeconds:F1} s; the bounded read gives up at 1.5 s");
            Assert.Equal("waiting", ActivityWord("shared"));
        }

        [WindowsFact]
        public void A_ninth_copy_refuses_to_join_a_pile_up_and_an_eighth_runs()
        {
            // Eight long-lived processes wearing the exe's name — cmd.exe copies pinging localhost —
            // make the copy under test the ninth. It must write nothing and say why in its log.
            var dummyDir = Path.Combine(_dir, "dummies");
            Directory.CreateDirectory(dummyDir);
            var dummyExe = Path.Combine(dummyDir, _name + ".exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), dummyExe);

            var dummies = new List<Process>();
            try
            {
                for (var i = 0; i < 8; i++)
                {
                    dummies.Add(StartDummy(dummyExe));
                }
                WaitUntil(() => Process.GetProcessesByName(_name).Length >= 8, "eight dummies to be visible");

                var refused = this.LaunchDirect(stdin: "", holdStdin: false, "activity", "done");
                Assert.Equal(0, refused.ExitCode);   // refusing is not a hook error
                Assert.False(File.Exists(Path.Combine(ActivityDir, "shared.json")), "a ninth copy must not write");
                Assert.Contains("(cap 8)", File.ReadAllText(Log));

                dummies[0].Kill(entireProcessTree: true);
                dummies[0].WaitForExit();
                dummies.RemoveAt(0);
                WaitUntil(() => Process.GetProcessesByName(_name).Length <= 7, "a dummy to disappear");

                var ran = this.LaunchDirect(stdin: "", holdStdin: false, "activity", "done");
                Assert.Equal(0, ran.ExitCode);
                Assert.Equal("done", ActivityWord("shared"));
            }
            finally
            {
                foreach (var d in dummies)
                {
                    try { d.Kill(entireProcessTree: true); d.WaitForExit(2000); } catch { /* gone */ }
                    d.Dispose();
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        // Rig
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// A renamed cmd.exe reading commands from stdin: one process named "claude" whose hooks
        /// are its direct children, so every hook it runs mints the same session key — the key the
        /// plugin would mint for it too. Each command's exit code comes back on a marker line.
        /// Commands name the exe as `.\name.exe`: a renamed cmd does not search its current
        /// directory for a bare name (9009, "not recognized").
        /// </summary>
        private sealed class ClaudeStandIn : IDisposable
        {
            private readonly Process _p;
            private readonly List<String> _lines = new();
            private Int32 _n;

            public String Key { get; }

            public ClaudeStandIn(String claudeExe, String workDir, String temp)
            {
                var psi = new ProcessStartInfo(claudeExe)
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workDir,
                };
                psi.ArgumentList.Add("/d");   // no AutoRun
                psi.ArgumentList.Add("/q");   // echo off: no prompts in the output
                psi.Environment["TEMP"] = temp;
                psi.Environment["TMP"] = temp;

                _p = Process.Start(psi) ?? throw new InvalidOperationException("could not start the stand-in");
                this.Key = WindowsProcessWatcher.SessionKeyFor(new WindowsProcessInfo { Pid = _p.Id, StartTime = _p.StartTime });
                _p.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (_lines) { _lines.Add(e.Data); } } };
                _p.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (_lines) { _lines.Add("stderr: " + e.Data); } } };
                _p.BeginOutputReadLine();
                _p.BeginErrorReadLine();
            }

            /// <summary>Run one command line as a child of the stand-in and return its exit code.</summary>
            public Int32 Run(String commandLine, Int32 timeoutMs = 15_000)
            {
                var marker = $"__cc_done_{++_n}_";
                _p.StandardInput.WriteLine(commandLine);
                _p.StandardInput.WriteLine($"echo {marker}%ERRORLEVEL%__");   // expanded after the command ran
                _p.StandardInput.Flush();

                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    lock (_lines)
                    {
                        var hit = _lines.FirstOrDefault(l => l.Contains(marker, StringComparison.Ordinal));
                        if (hit != null)
                        {
                            var start = hit.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
                            var end = hit.IndexOf("__", start, StringComparison.Ordinal);
                            return Int32.Parse(hit.Substring(start, end - start));
                        }
                    }
                    Thread.Sleep(20);
                }

                String transcript;
                lock (_lines) { transcript = String.Join("\n", _lines); }
                throw new Xunit.Sdk.XunitException($"'{commandLine}' did not finish within {timeoutMs} ms; output so far:\n{transcript}");
            }

            public void Dispose()
            {
                try { _p.StandardInput.WriteLine("exit"); _p.StandardInput.Close(); } catch { /* already gone */ }
                if (!_p.WaitForExit(3000))
                {
                    try { _p.Kill(entireProcessTree: true); } catch { /* gone */ }
                }
                _p.Dispose();
            }
        }

        private ClaudeStandIn StartClaude(String exe = null) => new ClaudeStandIn(exe ?? _claude, _bin, _temp);

        private sealed record DirectRun(Int32 ExitCode, TimeSpan Elapsed);

        /// <summary>
        /// Run the copy under test straight from the test host — no Claude ancestor, so no session
        /// key. <paramref name="stdin"/> null with <paramref name="holdStdin"/> leaves the pipe open
        /// until the hook exits on its own; otherwise the text (or nothing) is written and the pipe
        /// closed at once.
        /// </summary>
        private DirectRun LaunchDirect(String stdin, Boolean holdStdin, params String[] args)
        {
            var psi = new ProcessStartInfo(_hook)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = _bin,
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }
            psi.Environment["TEMP"] = _temp;
            psi.Environment["TMP"] = _temp;

            var clock = Stopwatch.StartNew();
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start " + _hook);
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!holdStdin)
            {
                if (stdin != null)
                {
                    p.StandardInput.Write(stdin);
                }
                p.StandardInput.Close();
            }

            var exited = p.WaitForExit(12_000);
            clock.Stop();
            if (holdStdin)
            {
                try { p.StandardInput.Close(); } catch { /* the hook is gone; the pipe may be too */ }
            }
            if (!exited)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* gone */ }
                throw new Xunit.Sdk.XunitException($"the hook did not exit within 12 s ({String.Join(" ", args)})");
            }
            Task.WaitAll(new Task[] { stdout, stderr }, 2000);
            return new DirectRun(p.ExitCode, clock.Elapsed);
        }

        /// <summary>
        /// Start <paramref name="exe"/> as a child of <paramref name="parentPid"/> rather than of
        /// this process, so its ancestry is that process's. No pipes: with a foreign parent the
        /// inheritable handles are the parent's, not ours, so stdin is simply not redirected —
        /// the hook then reads nothing, which is what the verb under test expects.
        /// </summary>
        private static DirectRun LaunchReparented(Int32 parentPid, String exe, String args, String workDir, String temp)
        {
            var parent = OpenProcess(PROCESS_CREATE_PROCESS, false, parentPid);
            if (parent == IntPtr.Zero)
            {
                throw new Xunit.Sdk.XunitException($"cannot open pid {parentPid} for reparenting: error {Marshal.GetLastWin32Error()}");
            }

            var attributeList = IntPtr.Zero;
            var parentHandleCell = Marshal.AllocHGlobal(IntPtr.Size);
            var environment = IntPtr.Zero;
            try
            {
                var size = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                attributeList = Marshal.AllocHGlobal(size);
                if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
                {
                    throw new Xunit.Sdk.XunitException("InitializeProcThreadAttributeList failed: " + Marshal.GetLastWin32Error());
                }
                Marshal.WriteIntPtr(parentHandleCell, parent);
                if (!UpdateProcThreadAttribute(attributeList, 0, PROC_THREAD_ATTRIBUTE_PARENT_PROCESS, parentHandleCell, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                {
                    throw new Xunit.Sdk.XunitException("UpdateProcThreadAttribute failed: " + Marshal.GetLastWin32Error());
                }

                // The hook takes its IPC root from TEMP; hand it the private one in a fresh block.
                var block = new System.Text.StringBuilder();
                foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
                {
                    var name = (String)kv.Key;
                    if (name.Equals("TEMP", StringComparison.OrdinalIgnoreCase) || name.Equals("TMP", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    block.Append(name).Append('=').Append(kv.Value).Append('\0');
                }
                block.Append("TEMP=").Append(temp).Append('\0').Append("TMP=").Append(temp).Append('\0').Append('\0');
                environment = Marshal.StringToHGlobalUni(block.ToString());

                var startup = new STARTUPINFOEX();
                startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
                startup.lpAttributeList = attributeList;
                var commandLine = new System.Text.StringBuilder($"\"{exe}\" {args}");

                var clock = Stopwatch.StartNew();
                if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                        EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW,
                        environment, workDir, ref startup, out var info))
                {
                    throw new Xunit.Sdk.XunitException("CreateProcess (reparented) failed: " + Marshal.GetLastWin32Error());
                }

                try
                {
                    if (WaitForSingleObject(info.hProcess, 12_000) != 0)
                    {
                        TerminateProcess(info.hProcess, 1);
                        throw new Xunit.Sdk.XunitException("the reparented hook did not exit within 12 s");
                    }
                    clock.Stop();
                    GetExitCodeProcess(info.hProcess, out var exitCode);
                    return new DirectRun((Int32)exitCode, clock.Elapsed);
                }
                finally
                {
                    CloseHandle(info.hThread);
                    CloseHandle(info.hProcess);
                }
            }
            finally
            {
                if (attributeList != IntPtr.Zero) { DeleteProcThreadAttributeList(attributeList); Marshal.FreeHGlobal(attributeList); }
                Marshal.FreeHGlobal(parentHandleCell);
                if (environment != IntPtr.Zero) { Marshal.FreeHGlobal(environment); }
                CloseHandle(parent);
            }
        }

        private const UInt32 PROCESS_CREATE_PROCESS = 0x0080;
        private const UInt32 EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        private const UInt32 CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const UInt32 CREATE_NO_WINDOW = 0x08000000;
        private static readonly IntPtr PROC_THREAD_ATTRIBUTE_PARENT_PROCESS = (IntPtr)0x00020000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public Int32 cb;
            public String lpReserved;
            public String lpDesktop;
            public String lpTitle;
            public Int32 dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public Int16 wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFOEX
        {
            public STARTUPINFO StartupInfo;
            public IntPtr lpAttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public Int32 dwProcessId, dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(UInt32 access, Boolean inherit, Int32 pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Boolean InitializeProcThreadAttributeList(IntPtr list, Int32 count, Int32 flags, ref IntPtr size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Boolean UpdateProcThreadAttribute(IntPtr list, UInt32 flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);

        [DllImport("kernel32.dll")]
        private static extern void DeleteProcThreadAttributeList(IntPtr list);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern Boolean CreateProcessW(String applicationName, System.Text.StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, Boolean inheritHandles, UInt32 creationFlags, IntPtr environment, String currentDirectory, ref STARTUPINFOEX startupInfo, out PROCESS_INFORMATION processInformation);

        [DllImport("kernel32.dll")]
        private static extern UInt32 WaitForSingleObject(IntPtr handle, UInt32 milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Boolean GetExitCodeProcess(IntPtr process, out UInt32 exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Boolean TerminateProcess(IntPtr process, UInt32 exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Boolean CloseHandle(IntPtr handle);

        private static Process StartDummy(String exe)
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "/d", "/c", "ping", "-n", "90", "127.0.0.1" })
            {
                psi.ArgumentList.Add(a);
            }
            var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start a dummy");
            p.StandardOutput.ReadToEndAsync();   // drained in the background so ping never blocks on a full pipe
            p.StandardError.ReadToEndAsync();
            return p;
        }

        private static void WaitUntil(Func<Boolean> condition, String what)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) { return; }
                Thread.Sleep(50);
            }
            throw new Xunit.Sdk.XunitException("timed out waiting for " + what);
        }

        private String ActivityWord(String key)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ActivityDir, key + ".json")));
            return doc.RootElement.GetProperty("state").GetString();
        }

        private GridSession ReadSession(String key)
        {
            var grid = new SessionRegistry(SessionsDir, ActivityDir, Path.Combine(Root, "registry.json"))
            {
                Agent = new ClaudeCodeAdapter(),
            };
            grid.Refresh(new HashSet<String> { key });
            return grid.SlotSession(1);
        }

        private static String Payload(String name) =>
            Path.Combine(HookExeFixture.RepoRoot(), "tests", "payloads", "claude-code", name);

        private static String ExpectedCommand()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Payload("PermissionRequest.json")));
            return doc.RootElement.GetProperty("tool_input").GetProperty("command").GetString();
        }
    }
}
