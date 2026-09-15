using System.Diagnostics;
using System.Runtime.InteropServices;

if (!OperatingSystem.IsWindows()) return 4;
Native.FreeConsole();
if (!Native.AllocConsole()) return Fail("AllocConsole", 5);
try
{
    // ReadConsole needs read access; changing the input mode also needs write access on
    // this handle (a read-only handle made SetConsoleMode fail with ERROR_ACCESS_DENIED).
    var input = Native.CreateFileW("CONIN$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    if (input == new IntPtr(-1)) return Fail("CreateFileW(CONIN$)", 6);
    try
    {
        if (!Native.SetConsoleMode(input, 2)) return Fail("SetConsoleMode", 7); // line input, no echo/processed shortcuts
        File.WriteAllText(args[0], Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString());
        for (var n = 0; n < int.Parse(args[2]); n++)
        {
            var text = new char[4096];
            if (!Native.ReadConsoleW(input, text, (uint)text.Length, out var read, IntPtr.Zero)) return Fail("ReadConsoleW", 8);
            // ReadConsoleW reports a length; it does not promise a NUL-terminated string.
            File.AppendAllText(args[1], new string(text, 0, checked((int)read)).TrimEnd('\r', '\n') + "\n");
        }
    }
    finally { Native.CloseHandle(input); }
}
finally { Native.FreeConsole(); }
return 0;

int Fail(string operation, int code)
{
    // AllocConsole replaces the process standard handles; diagnostics must survive detachment.
    File.WriteAllText(args[0] + ".error", $"{operation} failed: Win32 error {Marshal.GetLastWin32Error()}");
    return code;
}

internal static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool AllocConsole();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool SetConsoleMode(IntPtr handle, uint mode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool ReadConsoleW(IntPtr handle, [Out] char[] buffer, uint count, out uint read, IntPtr control);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
}
