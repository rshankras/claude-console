// claude-console-hook — the Windows counterpart of scripts/statusline-handler.sh and
// scripts/activity-hook.sh, in one arg-dispatched executable.
//
//   claude-console-hook statusline          <- reads Claude's JSON on stdin, writes it verbatim
//   claude-console-hook activity <state>    <- busy | waiting | done | permission
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
        // A hook must never break the user's session. Any failure is silent and non-zero at worst;
        // Claude Code keeps going either way.
        try
        {
            return args.Length switch
            {
                > 0 when args[0] == "statusline" => Statusline(),
                > 1 when args[0] == "activity" => Activity(args[1]),
                > 0 when args[0] == "selftest" => SelfTest(),
                _ => Usage(),
            };
        }
        catch
        {
            return 1;
        }
    }

    private static Int32 Usage()
    {
        Console.Error.WriteLine("usage: claude-console-hook statusline | activity <busy|waiting|done|permission> | selftest");
        return 2;
    }

    // ---- IPC layout (must mirror IpcPaths) ---------------------------------

    private static String Root => Path.Combine(Path.GetTempPath(), "claude-console");
    private static String SessionsDir => Path.Combine(Root, "sessions");
    private static String ActivityDir => Path.Combine(Root, "activity");
    private const String SharedName = "shared";

    // ---- commands ----------------------------------------------------------

    private static Int32 Statusline()
    {
        var json = Console.In.ReadToEnd();
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
        // tell a routine approval from `git push --force` (RiskClassifier). Any other state means
        // the moment has passed, so the pending file is cleared.
        var pending = key != null ? Path.Combine(ActivityDir, "pending-" + key + ".json") : null;
        if (pending != null)
        {
            if (state == "permission")
            {
                var stdin = Console.IsInputRedirected ? Console.In.ReadToEnd() : "";
                if (!String.IsNullOrWhiteSpace(stdin))
                {
                    WriteAtomic(pending, stdin);
                }
            }
            else
            {
                try { File.Delete(pending); } catch { /* best effort */ }
            }
        }

        return 0;
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
    private static String? SessionKey()
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
                if (IsClaude(proc))
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

    [SupportedOSPlatform("windows")]
    private static Int32 ParentOf(Int32 pid) =>
        Int32.TryParse(Wmic($"ParentProcessId from Win32_Process where ProcessId={pid}"), out var ppid) ? ppid : 0;

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

            var outp = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(4000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* gone */ }
                return null;
            }
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
