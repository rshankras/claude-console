// claude-console-hook — the Windows counterpart of scripts/statusline-handler.sh and
// scripts/activity-hook.sh, in one arg-dispatched executable.
//
//   claude-console-hook statusline          <- reads Claude's JSON on stdin, writes it verbatim
//   claude-console-hook activity <state>    <- busy | waiting | done | permission
//   claude-console-hook codex <event>       <- the Windows body of scripts/codex-hook.sh: wraps
//                                              the event JSON in the state envelope and writes it
//                                              to the CODEX product's IPC root (%TEMP%\codex-console)
//
// TWO THINGS MUST MATCH THE PLUGIN EXACTLY, or the live keys silently show defaults:
//
//   1. The IPC root.  %TEMP%\claude-console\{sessions,activity}  — mirrors IpcPaths.TempDir,
//      which uses Path.GetTempPath() on Windows.
//   2. The SESSION KEY.  "pid-<pid>-<utcStartTicks>" of the Claude process — mirrors
//      WindowsProcessWatcher.SessionKeyFor. The bash scripts solve the same problem by climbing
//      the parent chain with `ps -o tty=` until a real tty appears; this climbs the parent chain
//      until it finds the Claude process, because a hook can be spawned several levels deep.
//
// Both are pinned by contract tests that read this file (tests/WindowsHookTests.cs).
//
// Like the bash handler, this stays DUMB about payload shape: Claude's JSON is written through
// verbatim and all parsing happens in the plugin (ClaudeState).

using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

internal static class Program
{
    private static Int32 Main(String[] args)
    {
        // FIRST, before anything that could hang, throw, or depend on the spawn environment:
        // prove we were launched at all. Windows hardware reported hooks "exited with code 1"
        // while the exe's every internal path was already guarded — the remaining question is
        // whether codex ever spawns the process. This line is the answer: if hook-invoked.log
        // is silent while codex reports failures, the exe was never the patient.
        EntryBreadcrumb(args);

        // SECOND: make it impossible for this process to outlive its usefulness. Logitech QA's
        // 2.2.0 retest found ~15 claude-console-hook processes left behind after one terminal
        // session had been opened and closed following a reboot, and the machine froze until the
        // plugin service was shut down (#57). Every path below that could block — a stdin that
        // never reaches EOF, a PowerShell cold start after boot, a chained status line — is now
        // bounded, but the watchdog is what makes the guarantee: after WatchdogSeconds this
        // process exits whatever it is doing. A hook that has not finished by then has nothing
        // left to write, and a dropped status update is survivable; an unbounded process is not.
        StartWatchdog(args);
        if (TooManyOfUs())
        {
            return 0;
        }

        // A hook must never break the user's session. Any failure is silent and non-zero at worst;
        // Claude Code keeps going either way.
        try
        {
            return args.Length switch
            {
                > 0 when args[0] == "statusline" => Statusline(),
                > 1 when args[0] == "activity" => Activity(args[1]),
                > 1 when args[0] == "codex" => Codex(args[1]),
                > 0 when args[0] == "selftest" => SelfTest(),
                _ => Usage(),
            };
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>
    /// One line per invocation, written beside the exe itself — the only location that needs no
    /// environment variables and no directory creation. Records what the spawn actually looked
    /// like (args, TEMP, cwd, whether stdin is a pipe), because a hook launched with a scrubbed
    /// environment writes its state somewhere nobody looks and this is how we'd know. Capped so
    /// it can never grow into a problem; every failure is swallowed.
    /// </summary>
    private static void EntryBreadcrumb(String[] args)
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            if (dir == null)
            {
                return;
            }

            var path = Path.Combine(dir, "hook-invoked.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
            {
                return;
            }

            Boolean redirected;
            try { redirected = Console.IsInputRedirected; } catch { redirected = false; }

            File.AppendAllText(path,
                $"{DateTime.UtcNow:o} args=[{String.Join(" ", args)}] temp={Environment.GetEnvironmentVariable("TEMP") ?? "(unset)"} cwd={Environment.CurrentDirectory} stdinRedirected={redirected}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never become the failure they exist to explain.
        }
    }

    /// <summary>Longer than any healthy hook run (tens of milliseconds) by two orders of magnitude.</summary>
    private const Int32 WatchdogSeconds = 8;

    /// <summary>
    /// More live copies of this exe than any healthy session produces. A hook is spawned per
    /// event and lives for milliseconds; a count above this means they are stuck, and one more
    /// stuck copy helps nobody. Checked once, after the watchdog is armed.
    /// </summary>
    private const Int32 MaxConcurrentHooks = 16;

    /// <summary>
    /// A background thread that ends the process after <see cref="WatchdogSeconds"/> regardless
    /// of what the main thread is blocked on (#57). Background so it never keeps the process
    /// alive itself; exit code 0 so a hook that timed out does not surface as a hook error in the
    /// user's session — the breadcrumb beside the exe is where the timeout is recorded.
    /// </summary>
    private static void StartWatchdog(String[] args)
    {
        try
        {
            var t = new Thread(() =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(WatchdogSeconds));
                Breadcrumb($"watchdog: still running after {WatchdogSeconds}s, args=[{String.Join(" ", args)}] — exiting");
                Environment.Exit(0);
            })
            { IsBackground = true, Name = "claude-console-hook watchdog" };
            t.Start();
        }
        catch
        {
            // No watchdog is worse than a late one, but it must never stop the hook from running.
        }
    }

    /// <summary>Refuse to add to a pile-up: see <see cref="MaxConcurrentHooks"/>.</summary>
    private static Boolean TooManyOfUs()
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "claude-console-hook";
            var count = Process.GetProcessesByName(name).Length;
            if (count <= MaxConcurrentHooks)
            {
                return false;
            }

            Breadcrumb($"{count} copies of {name} are running (cap {MaxConcurrentHooks}) — not adding to the pile");
            return true;
        }
        catch
        {
            return false;   // if the count itself fails, run normally: the watchdog still bounds us
        }
    }

    /// <summary>A line in the breadcrumb log beside the exe (same file as EntryBreadcrumb).</summary>
    private static void Breadcrumb(String message)
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            if (dir == null)
            {
                return;
            }

            var path = Path.Combine(dir, "hook-invoked.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
            {
                return;
            }

            File.AppendAllText(path, $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never become the failure they exist to explain.
        }
    }

    private static Int32 Usage()
    {
        Console.Error.WriteLine("usage: claude-console-hook statusline | activity <state> | codex <event> | selftest");
        return 2;
    }

    // ---- IPC layout (must mirror IpcPaths) ---------------------------------

    private static String Root => Path.Combine(Path.GetTempPath(), "claude-console");
    private static String SessionsDir => Path.Combine(Root, "sessions");
    private static String ActivityDir => Path.Combine(Root, "activity");
    private const String SharedName = "shared";

    // The CODEX product's tree. Separate on purpose: two consoles must never share an IPC root,
    // or each would reap the other's sessions as dead (IpcPaths.ProductSlug, "codex-console").
    private static String CodexRoot => Path.Combine(Path.GetTempPath(), "codex-console");
    private static String CodexSessionsDir => Path.Combine(CodexRoot, "sessions");

    // ---- commands ----------------------------------------------------------

    private static Int32 Statusline()
    {
        // Bounded, like the codex path: a stdin that never reaches EOF is the simplest way for
        // this process to live forever (#57). Claude writes the JSON and closes the pipe at once,
        // so 1.5 s is generous; on timeout there is nothing to write and the exe exits clean.
        var json = ReadStdinBounded(1500);
        if (String.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        var key = SessionKey();
        Directory.CreateDirectory(SessionsDir);

        // Per-session file, plus the shared last-writer-wins fallback the plugin uses when it has
        // no key match yet. Written verbatim — the plugin owns all field parsing.
        if (key != null)
        {
            WriteAtomic(Path.Combine(SessionsDir, key + ".json"), json);
        }
        WriteAtomic(Path.Combine(SessionsDir, SharedName + ".json"), json);

        // Chain: if the user already had a status line, run it and pass its output through so
        // their status bar still renders. Mirrors the bash handler's chain block.
        var chain = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "claude-console", "statusline-chain");
        if (File.Exists(chain))
        {
            var cmd = File.ReadAllText(chain).Trim();
            if (cmd.Length > 0)
            {
                RunChained(cmd, json);
            }
        }

        return 0;
    }

    private static Int32 Activity(String state)
    {
        var key = SessionKey();
        Directory.CreateDirectory(ActivityDir);

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Built by hand, not JsonSerializer: reflection serialization is the one thing in this
        // exe that publish-trimming can break, and the payload is two fields. Escaping still
        // matters — state arrives via argv and lands in a file the plugin parses as JSON.
        var payload = $"{{\"state\":\"{JsonEscape(state)}\",\"ts\":{ts}}}";

        if (key != null)
        {
            WriteAtomic(Path.Combine(ActivityDir, key + ".json"), payload);
        }
        WriteAtomic(Path.Combine(ActivityDir, SharedName + ".json"), payload);

        // "permission" carries the tool name and its input — that payload is what lets the plugin
        // tell a routine approval from `git push --force` (RiskClassifier).
        //
        // Clear the pending file ONLY on busy/done, never on a bare "waiting". A permission menu
        // fires PermissionRequest (arrives here as "permission" WITH a payload) and then a plain
        // Notification the CLI delays ~6s ("waiting", no payload, same menu still up). Deleting the
        // payload on that second event darkened a live approval badge and blinded the Yes/No keys
        // to a menu they were about to answer (#21/#51). Mirror scripts/activity-hook.sh exactly.
        var pending = key != null ? Path.Combine(ActivityDir, "pending-" + key + ".json") : null;
        if (pending != null)
        {
            if (state == "permission")
            {
                var stdin = ReadStdinBounded(1500);   // bounded: see Statusline (#57)
                if (!String.IsNullOrWhiteSpace(stdin))
                {
                    WriteAtomic(pending, stdin);
                }
            }
            else if (state == "busy" || state == "done")
            {
                try { File.Delete(pending); } catch { /* best effort */ }
            }
            // "waiting" with no payload: leave any existing pending file intact — the menu is up.
        }

        return 0;
    }

    /// <summary>
    /// The Windows body of scripts/codex-hook.sh, envelope-for-envelope: wrap the event JSON as
    /// {"schema":1,"agent":"codex-cli","event":…,"ts":…,"payload":…} and atomically replace this
    /// session's state file. The session key is the CODEX process up the parent chain — Windows'
    /// answer to the script reading its own controlling terminal. Like the script, it never
    /// blocks Codex: every path prints {} and exits 0, because a hook that fails loudly would
    /// break the user's session to report a keypad problem.
    /// </summary>
    private static Int32 Codex(String eventName)
    {
        try
        {
            // BOUNDED stdin read, never ReadToEnd bare: on Windows the hook's stdin can fail to
            // deliver EOF even after codex has written the whole payload (inherited pipe write
            // handles), so an unbounded read hangs until codex kills the hook at its timeout —
            // kill code 1, nothing written, no exception to breadcrumb. Exactly the hardware
            // signature that survived three fixes aimed downstream of it. On timeout the payload
            // is forfeited but the EVENT still records — the keys light with less detail.
            var payload = ReadStdinBounded(1500);
            var body = payload.TrimStart().StartsWith('{') ? payload : "null";
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var envelope =
                $"{{\"schema\":1,\"agent\":\"codex-cli\",\"event\":\"{JsonEscape(eventName)}\",\"ts\":{ts},\"payload\":{body}}}\n";

            // The shared file goes FIRST, before any process walking: codex enforces the hook
            // timeout by TERMINATING the process (exit code 1 — seen as "hook exited with code 1"
            // on hardware, with no breadcrumb and no state file, because the old order died mid
            // climb). Whatever happens after this line, evidence exists and the plugin's shared
            // fallback lights up.
            Directory.CreateDirectory(CodexSessionsDir);
            WriteAtomic(Path.Combine(CodexSessionsDir, SharedName + ".json"), envelope);

            // TOPMOST codex, not nearest: codex runs as a TUI process plus a child codex (its
            // app server), hooks can be spawned by either, and the grid keys sessions on the
            // TUI — the outermost one. The climb is cheap now (kernel parent lookups, no
            // PowerShell spawns), which is what keeps this verb inside codex's timeout.
            var key = SessionKeyTopmost(IsCodex);
            if (key != null)
            {
                WriteAtomic(Path.Combine(CodexSessionsDir, key + ".json"), envelope);
            }
        }
        catch (Exception ex)
        {
            // Swallowed on purpose — same contract as the script — but never silently: the
            // breadcrumb is how "hook exited with code 1" stops being a guessing game.
            Breadcrumb(ex, eventName);
        }

        // Even the goodbye is guarded: codex reports a nonzero hook exit to the USER, so this
        // verb must be structurally unable to produce one. A closed stdout pipe on the final
        // write was the leading suspect for exactly that report from Windows hardware.
        try { Console.Write("{}"); } catch (Exception ex) { Breadcrumb(ex, eventName); }
        return 0;
    }

    /// <summary>
    /// Read all of stdin, but never wait longer than <paramref name="ms"/> for EOF. The reader
    /// task is a background thread, so an abandoned read cannot keep the process alive.
    /// </summary>
    private static String ReadStdinBounded(Int32 ms)
    {
        try
        {
            if (!Console.IsInputRedirected)
            {
                return "";
            }

            var read = System.Threading.Tasks.Task.Run(() => Console.In.ReadToEnd());
            return read.Wait(ms) ? read.Result : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Last-resort diagnostics for the codex verb: append the exception where a human will look
    /// (the codex IPC root), falling back to the exe's own directory if even that is unreachable.
    /// Failures here are swallowed — the breadcrumb must never become a new way to exit nonzero.
    /// </summary>
    private static void Breadcrumb(Exception ex, String eventName)
    {
        var line = $"{DateTime.UtcNow:o} {eventName}: {ex}{Environment.NewLine}";
        try
        {
            Directory.CreateDirectory(CodexRoot);
            File.AppendAllText(Path.Combine(CodexRoot, "hook-error.log"), line);
            return;
        }
        catch
        {
            // fall through to the exe-side location
        }

        try
        {
            var beside = Path.Combine(AppContext.BaseDirectory, "hook-error.log");
            File.AppendAllText(beside, line);
        }
        catch
        {
            // out of places to write — stay silent, stay exit 0
        }
    }

    private static Int32 SelfTest()
    {
        Console.WriteLine("claude-console-hook selftest");
        Console.WriteLine($"  ipc root     {Root}");
        Console.WriteLine($"  sessions     {SessionsDir}");
        Console.WriteLine($"  session key  {SessionKey() ?? "(no Claude process found in this process's ancestry)"}");
        Console.WriteLine();
        Console.WriteLine("Run this from INSIDE a Claude Code session — the key above must match the");
        Console.WriteLine("one `claude-console-inject selftest` prints for that same session.");
        return 0;
    }

    // ---- session key -------------------------------------------------------

    /// <summary>
    /// The Claude session this hook belongs to, as "pid-&lt;pid&gt;-&lt;utcStartTicks&gt;".
    ///
    /// MUST match WindowsProcessWatcher.SessionKeyFor. A hook can be spawned several levels below
    /// Claude (Claude -> shell -> us), so walk up the parent chain until we find it, the same way
    /// the bash scripts walk up until a real tty appears.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static String? SessionKey() => SessionKey(IsClaude);

    /// <summary>
    /// Like SessionKey, but keeps climbing and returns the OUTERMOST matching ancestor. The grid
    /// drops a session candidate whose parent is also a candidate, so it keys the topmost process
    /// of a nested pair — this must mint the same key or state never attaches to the session.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static String? SessionKeyTopmost(Func<Process, Boolean> isAgent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            String? best = null;
            var pid = Environment.ProcessId;
            for (var hop = 0; hop < 8; hop++)
            {
                using var proc = Process.GetProcessById(pid);
                if (isAgent(proc))
                {
                    best = $"pid-{proc.Id}-{proc.StartTime.ToUniversalTime().Ticks}";
                }

                var parent = ParentOf(pid);
                if (parent <= 0 || parent == pid)
                {
                    break;
                }
                pid = parent;
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static String? SessionKey(Func<Process, Boolean> isAgent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var pid = Environment.ProcessId;
            for (var hop = 0; hop < 8; hop++)
            {
                using var proc = Process.GetProcessById(pid);
                if (isAgent(proc))
                {
                    return $"pid-{proc.Id}-{proc.StartTime.ToUniversalTime().Ticks}";
                }

                var parent = ParentOf(pid);
                if (parent <= 0 || parent == pid)
                {
                    return null;
                }
                pid = parent;
            }
        }
        catch
        {
            // Access denied / process exited mid-walk — fall back to the shared file only.
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static Boolean IsClaude(Process proc)
    {
        String name;
        try
        {
            name = proc.ProcessName;
        }
        catch
        {
            return false;
        }

        // The native installer: claude.exe. Unambiguous by name, and the cheap common case.
        if (name.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // An npm/bun install runs the CLI under an interpreter — only then do we pay for a
        // command-line lookup.
        if (name is not ("node" or "bun" or "deno" or "npx"))
        {
            return false;
        }

        var cmd = CommandLineOf(proc.Id);
        return cmd != null &&
               (cmd.Contains("claude-code", StringComparison.OrdinalIgnoreCase) ||
                cmd.Contains("claude.js", StringComparison.OrdinalIgnoreCase) ||
                cmd.Contains(@"\claude", StringComparison.OrdinalIgnoreCase) ||
                cmd.Contains("/claude", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The codex twin of IsClaude — mirrors AgentProcessMatcher.CodexCli the way IsClaude mirrors
    /// ClaudeCode: native binary by name, npm install by interpreter + script path.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static Boolean IsCodex(Process proc)
    {
        String name;
        try
        {
            name = proc.ProcessName;
        }
        catch
        {
            return false;
        }

        if (name.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name is not ("node" or "bun" or "deno" or "npx"))
        {
            return false;
        }

        var cmd = CommandLineOf(proc.Id);
        return cmd != null &&
               (cmd.Contains(@"\@openai\codex", StringComparison.OrdinalIgnoreCase) ||
                cmd.Contains("/@openai/codex", StringComparison.OrdinalIgnoreCase) ||
                cmd.Contains(@"\codex", StringComparison.OrdinalIgnoreCase) ||
                cmd.Contains("/codex", StringComparison.OrdinalIgnoreCase));
    }

    [SupportedOSPlatform("windows")]
    private static Int32 ParentOf(Int32 pid)
    {
        // Kernel first: NtQueryInformationProcess answers in microseconds. The PowerShell path
        // survives only as a fallback — a PS cold start costs seconds, and codex TERMINATES a
        // hook that outlives its timeout (exit code 1), so a climb that spawned PowerShell per
        // hop was killed mid-walk on real hardware before it ever wrote state.
        var viaKernel = ParentViaNtQuery(pid);
        if (viaKernel > 0)
        {
            return viaKernel;
        }

        return Int32.TryParse(Wmic($"ParentProcessId from Win32_Process where ProcessId={pid}"), out var ppid) ? ppid : 0;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [System.Runtime.InteropServices.DllImport("ntdll.dll")]
    private static extern Int32 NtQueryInformationProcess(
        IntPtr processHandle, Int32 processInformationClass,
        ref ProcessBasicInformation processInformation, Int32 processInformationLength, out Int32 returnLength);

    [SupportedOSPlatform("windows")]
    private static Int32 ParentViaNtQuery(Int32 pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            var info = new ProcessBasicInformation();
            var status = NtQueryInformationProcess(
                proc.Handle, 0, ref info, System.Runtime.InteropServices.Marshal.SizeOf<ProcessBasicInformation>(), out _);

            return status == 0 ? (Int32)info.InheritedFromUniqueProcessId : 0;
        }
        catch
        {
            // Access denied or the process exited mid-walk — let the caller fall back.
            return 0;
        }
    }

    [SupportedOSPlatform("windows")]
    private static String? CommandLineOf(Int32 pid) => Wmic($"CommandLine from Win32_Process where ProcessId={pid}");

    // One PowerShell round trip per lookup. Kept off the hot path: the native-install case returns
    // from IsClaude on the process name alone and never lands here.
    [SupportedOSPlatform("windows")]
    private static String? Wmic(String selectClause)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add($"(Get-CimInstance -Query 'select {selectClause}')." +
                                 (selectClause.StartsWith("Parent", StringComparison.Ordinal) ? "ParentProcessId" : "CommandLine"));

            using var p = Process.Start(psi);
            if (p == null)
            {
                return null;
            }

            // The 4 s limit only means something if the read does not block first: ReadToEnd()
            // returns when PowerShell closes its stdout, i.e. when it exits — so the old order
            // (read, then wait) waited for a PowerShell cold start however long it took, per hop,
            // per hook. Right after a reboot that is the pile-up QA saw (#57). Read in the
            // background, wait with the limit, and kill what has not answered.
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(4000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* gone */ }
                return null;
            }
            var outp = output.Wait(500) ? output.Result : "";
            return String.IsNullOrWhiteSpace(outp) ? null : outp.Trim();
        }
        catch
        {
            return null;
        }
    }

    // Minimal JSON string escaping for the one hand-built payload above.
    private static String JsonEscape(String s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                default:
                    if (ch < 0x20) { sb.Append("\\u").Append(((Int32)ch).ToString("x4")); }
                    else { sb.Append(ch); }
                    break;
            }
        }
        return sb.ToString();
    }

    // ---- io ----------------------------------------------------------------

    /// <summary>
    /// Write via a temp file + move, so the plugin's 500 ms poll can never read a half-written
    /// file. Same guarantee the bash writers give with tmp+mv.
    /// </summary>
    private static void WriteAtomic(String path, String content)
    {
        try
        {
            var tmp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // A dropped status update is survivable; breaking the user's session is not.
        }
    }

    private static void RunChained(String command, String stdin)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(command);

            using var p = Process.Start(psi);
            if (p == null)
            {
                return;
            }
            p.StandardInput.Write(stdin);
            p.StandardInput.Close();
            p.WaitForExit(4000);
        }
        catch
        {
            // The user's own status line failing must not take ours down with it.
        }
    }
}
