// claude-console-inject — types into one specific Claude Code session's console on Windows.
//
// The plugin never does this itself. AttachConsole/FreeConsole mutate GLOBAL state for the calling
// process, and LogiPluginService is a long-lived multi-threaded GUI host; one short-lived process
// per keypress (attach, write, exit) isolates that, exactly as the macOS backend isolates each
// osascript run.
//
// Mechanism proven by the 2026-08-07 spike (spikes/windows-injection): writes go to a CONSOLE
// HANDLE, not the foreground window, so a keystroke reaches the intended session or nothing at
// all — it cannot leak into whatever app happens to be focused.
//
// Exit codes are the contract with the plugin (see WindowsInjection):
//   0 ok · 2 session missing/not verifiable · 3 failed · 5 session elevated (access denied)

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

internal static class Program
{
    private const Int32 ExitOk = 0;
    private const Int32 ExitSessionMissing = 2;
    private const Int32 ExitFailed = 3;
    private const Int32 ExitSessionElevated = 5;

    private const Int32 ErrorAccessDenied = 5;

    private static Int32 Main(String[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("claude-console-inject runs on Windows only.");
            return ExitFailed;
        }

        try
        {
            var verb = args.Length > 0 ? args[0] : "help";
            var opts = ParseOptions(args);

            return verb switch
            {
                "text" => InjectText(opts),
                "key" => InjectKey(opts),
                "tabenter" => InjectTabThenEnter(opts),
                "selftest" => SelfTest(),
                "beep" => Beep(),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"claude-console-inject: {ex.Message}");
            return ExitFailed;
        }
    }

    private static Int32 Usage()
    {
        Console.WriteLine("""
            claude-console-inject — type into one Claude Code session's console

              text     --pid N --start-ticks T --submit true|false --text "..."
              key      --pid N --start-ticks T --key <Name> [--mods Control,Shift]
              tabenter --pid N --start-ticks T
              beep
              selftest

            Key names: Escape Return Tab ArrowUp ArrowDown ArrowLeft ArrowRight PageUp PageDown
            Exit codes: 0 ok · 2 session missing · 3 failed · 5 session elevated
            """);
        return ExitFailed;
    }

    private static Dictionary<String, String> ParseOptions(String[] args)
    {
        var opts = new Dictionary<String, String>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                opts[args[i]] = args[i + 1];
                i++;
            }
        }
        return opts;
    }

    // ---- commands ----------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static Int32 InjectText(Dictionary<String, String> opts)
    {
        var text = opts.GetValueOrDefault("--text") ?? "";
        if (text.Length == 0)
        {
            return ExitOk;   // nothing to type is not a failure
        }

        var records = new List<INPUT_RECORD>();
        foreach (var ch in text)
        {
            AppendChar(records, ch);
        }

        if (String.Equals(opts.GetValueOrDefault("--submit"), "true", StringComparison.OrdinalIgnoreCase))
        {
            AppendKey(records, VkReturn, '\r', 0);
        }

        return Deliver(opts, records);
    }

    [SupportedOSPlatform("windows")]
    private static Int32 InjectKey(Dictionary<String, String> opts)
    {
        var name = opts.GetValueOrDefault("--key");
        if (name == null || !TryMapKey(name, out var vk))
        {
            Console.Error.WriteLine($"unknown key: {name}");
            return ExitFailed;
        }

        var records = new List<INPUT_RECORD>();
        AppendKey(records, vk, '\0', ModifiersFrom(opts.GetValueOrDefault("--mods")));
        return Deliver(opts, records);
    }

    [SupportedOSPlatform("windows")]
    private static Int32 InjectTabThenEnter(Dictionary<String, String> opts)
    {
        // One attach, one write: Tab to accept the completion, then Return to submit. Two separate
        // runs would leave a gap in which the completion might not have registered.
        var records = new List<INPUT_RECORD>();
        AppendKey(records, VkTab, '\t', 0);
        AppendKey(records, VkReturn, '\r', 0);
        return Deliver(opts, records);
    }

    /// <summary>
    /// Verifies Phase 1's assumptions on real hardware: that Claude sessions are discoverable,
    /// that StartTime is readable (it is half the session key), and that command lines resolve.
    /// Run this by hand before trusting discovery — see docs/windows-port-2.0-plan.md.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static Int32 SelfTest()
    {
        Console.WriteLine("claude-console-inject selftest\n");

        var names = new[] { "claude", "node", "bun", "deno", "npx" };
        var any = false;

        foreach (var name in names)
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                using (proc)
                {
                    String start, key, cmd;
                    try
                    {
                        start = proc.StartTime.ToUniversalTime().ToString("o");
                        key = $"pid-{proc.Id}-{proc.StartTime.ToUniversalTime().Ticks}";
                    }
                    catch (Exception ex)
                    {
                        // If this fails broadly, the session key design needs revisiting.
                        Console.WriteLine($"  [{name}.exe pid {proc.Id}] StartTime UNREADABLE: {ex.GetType().Name}");
                        continue;
                    }

                    try
                    {
                        cmd = CommandLineOf(proc.Id) ?? "(unreadable)";
                    }
                    catch (Exception ex)
                    {
                        cmd = $"(error: {ex.GetType().Name})";
                    }

                    any = true;
                    Console.WriteLine($"  {name}.exe pid={proc.Id}");
                    Console.WriteLine($"    start   {start}");
                    Console.WriteLine($"    key     {key}");
                    Console.WriteLine($"    cmdline {Trim(cmd, 120)}");
                }
            }
        }

        if (!any)
        {
            Console.WriteLine("  no candidate processes found — is Claude Code running natively (not WSL)?");
            return ExitFailed;
        }

        Console.WriteLine("\nCheck: every real Claude session above has a readable start time,");
        Console.WriteLine("and Claude Desktop's processes are distinguishable by their command line.");
        return ExitOk;
    }

    // The plugin's only out-of-band signal (e.g. voice heard nothing). LogiPluginService is a
    // background host with no console, so Console.Beep is unreliable; MessageBeep is not.
    [SupportedOSPlatform("windows")]
    private static Int32 Beep()
    {
        MessageBeep(0xFFFFFFFF);   // MB_OK — the default system sound
        return ExitOk;
    }

    [DllImport("user32.dll")]
    private static extern Boolean MessageBeep(UInt32 type);

    private static String Trim(String s, Int32 max) => s.Length <= max ? s : s[..max] + "…";

    [SupportedOSPlatform("windows")]
    private static String? CommandLineOf(Int32 pid)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add($"(Get-CimInstance Win32_Process -Filter \"ProcessId={pid}\").CommandLine");

        using var p = Process.Start(psi);
        if (p == null)
        {
            return null;
        }
        var outp = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return String.IsNullOrWhiteSpace(outp) ? null : outp.Trim();
    }

    // ---- delivery ----------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static Int32 Deliver(Dictionary<String, String> opts, List<INPUT_RECORD> records)
    {
        if (!Int32.TryParse(opts.GetValueOrDefault("--pid"), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
        {
            Console.Error.WriteLine("missing or invalid --pid");
            return ExitFailed;
        }

        // Verify the target IS the session we were told to type into, before attaching to anything.
        // Windows recycles PIDs: without this, a stale session key could attach to an unrelated
        // process that inherited the number and type into it. This is the guard.
        if (Int64.TryParse(opts.GetValueOrDefault("--start-ticks"), NumberStyles.None, CultureInfo.InvariantCulture, out var expectedTicks)
            && expectedTicks > 0)
        {
            if (!VerifyStartTime(pid, expectedTicks, out var why))
            {
                Console.Error.WriteLine($"target not verified: {why}");
                return ExitSessionMissing;
            }
        }

        if (records.Count == 0)
        {
            return ExitOk;
        }

        // Detach from our own console (if any) before claiming the target's.
        FreeConsole();

        if (!AttachConsole((UInt32)pid))
        {
            var err = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"AttachConsole({pid}) failed: {err}");
            return err == ErrorAccessDenied ? ExitSessionElevated : ExitSessionMissing;
        }

        try
        {
            var handle = CreateFile("CONIN$", GenericRead | GenericWrite,
                FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle == InvalidHandle)
            {
                return ExitFailed;
            }

            try
            {
                // ONE write for the whole burst — the console's input buffer receives it as a unit,
                // so a user typing concurrently cannot interleave into the middle of a prompt.
                var arr = records.ToArray();
                if (!WriteConsoleInputW(handle, arr, (UInt32)arr.Length, out var written) || written != arr.Length)
                {
                    return ExitFailed;
                }
                return ExitOk;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        finally
        {
            FreeConsole();
        }
    }

    [SupportedOSPlatform("windows")]
    private static Boolean VerifyStartTime(Int32 pid, Int64 expectedTicks, out String why)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            var actual = proc.StartTime.ToUniversalTime().Ticks;
            if (actual != expectedTicks)
            {
                why = $"pid {pid} started at {actual}, expected {expectedTicks} (PID recycled — session is gone)";
                return false;
            }
            why = "";
            return true;
        }
        catch (ArgumentException)
        {
            why = $"pid {pid} is not running";
            return false;
        }
        catch (Exception ex)
        {
            why = $"could not verify pid {pid}: {ex.GetType().Name}";
            return false;
        }
    }

    // ---- input records -----------------------------------------------------

    private const UInt16 VkReturn = 0x0D;
    private const UInt16 VkTab = 0x09;
    private const UInt16 VkEscape = 0x1B;

    private static Boolean TryMapKey(String name, out UInt16 vk)
    {
        // Names come from the plugin's TerminalKey enum — keep in sync with
        // src/Platform/TerminalKey.cs (MacPlatformBridge.AppleScriptFor is the macOS counterpart).
        vk = name switch
        {
            "Escape" => VkEscape,
            "Return" => VkReturn,
            "Tab" => VkTab,
            "ArrowUp" => 0x26,
            "ArrowDown" => 0x28,
            "ArrowLeft" => 0x25,
            "ArrowRight" => 0x27,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            _ => 0,
        };
        return vk != 0;
    }

    private static UInt32 ModifiersFrom(String? mods)
    {
        if (String.IsNullOrEmpty(mods))
        {
            return 0;
        }

        UInt32 state = 0;
        foreach (var m in mods.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            state |= m.Trim() switch
            {
                "Shift" => ShiftPressed,
                "Control" => LeftCtrlPressed,
                "Alt" => LeftAltPressed,
                _ => 0u,   // Command is macOS-only; ignore rather than fail the press
            };
        }
        return state;
    }

    private static void AppendChar(List<INPUT_RECORD> records, Char ch)
    {
        var scan = VkKeyScanW(ch);
        if (scan == -1)
        {
            AppendKey(records, 0, ch, 0);   // no key for this char on the current layout
            return;
        }

        var vk = (UInt16)(scan & 0xFF);
        var mods = (scan & 0x0100) != 0 ? ShiftPressed : 0u;
        AppendKey(records, vk, ch, mods);
    }

    private static void AppendKey(List<INPUT_RECORD> records, UInt16 vk, Char ch, UInt32 controlState)
    {
        var rec = new INPUT_RECORD
        {
            EventType = KeyEventType,
            KeyEvent = new KEY_EVENT_RECORD
            {
                bKeyDown = 1,
                wRepeatCount = 1,
                wVirtualKeyCode = vk,
                wVirtualScanCode = (UInt16)MapVirtualKeyW(vk, 0),
                UnicodeChar = ch,
                dwControlKeyState = controlState,
            },
        };
        records.Add(rec);
        rec.KeyEvent.bKeyDown = 0;
        records.Add(rec);   // every press needs its release, or the console sees a stuck key
    }

    // ---- Win32 -------------------------------------------------------------

    private const UInt16 KeyEventType = 0x0001;
    private const UInt32 ShiftPressed = 0x0010;
    private const UInt32 LeftCtrlPressed = 0x0008;
    private const UInt32 LeftAltPressed = 0x0002;
    private const UInt32 GenericRead = 0x80000000;
    private const UInt32 GenericWrite = 0x40000000;
    private const UInt32 FileShareRead = 0x1;
    private const UInt32 FileShareWrite = 0x2;
    private const UInt32 OpenExisting = 3;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)] public UInt16 EventType;
        [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct KEY_EVENT_RECORD
    {
        public Int32 bKeyDown;
        public UInt16 wRepeatCount;
        public UInt16 wVirtualKeyCode;
        public UInt16 wVirtualScanCode;
        public Char UnicodeChar;
        public UInt32 dwControlKeyState;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern Boolean FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern Boolean AttachConsole(UInt32 pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(String name, UInt32 access, UInt32 share,
        IntPtr security, UInt32 disposition, UInt32 flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern Boolean CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern Boolean WriteConsoleInputW(IntPtr handle, INPUT_RECORD[] records,
        UInt32 count, out UInt32 written);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern Int16 VkKeyScanW(Char ch);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern UInt32 MapVirtualKeyW(UInt32 code, UInt32 mapType);
}
