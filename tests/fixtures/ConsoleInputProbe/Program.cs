using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

if (!OperatingSystem.IsWindows()) return 4;
Native.FreeConsole();
if (!Native.AllocConsole()) return 5;
try
{
    var input = Native.CreateFileW("CONIN$", 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    if (input == new IntPtr(-1)) return 6;
    try
    {
        if (!Native.SetConsoleMode(input, 2)) return 7; // line input, no echo/processed shortcuts
        File.WriteAllText(args[0], Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString());
        for (var n = 0; n < int.Parse(args[2]); n++)
        {
            var text = new StringBuilder(4096);
            if (!Native.ReadConsoleW(input, text, 4096, out _, IntPtr.Zero)) return 8;
            File.AppendAllText(args[1], text.ToString().TrimEnd('\r', '\n') + "\n");
        }
    }
    finally { Native.CloseHandle(input); }
}
finally { Native.FreeConsole(); }
return 0;

internal static class Native
{
    [DllImport("kernel32.dll")] internal static extern bool FreeConsole();
    [DllImport("kernel32.dll")] internal static extern bool AllocConsole();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] internal static extern bool SetConsoleMode(IntPtr handle, uint mode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern bool ReadConsoleW(IntPtr handle, StringBuilder buffer, uint count, out uint read, IntPtr control);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
}
