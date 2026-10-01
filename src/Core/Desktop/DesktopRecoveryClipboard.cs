namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Text;

    internal static class DesktopRecoveryClipboard
    {
        internal static Boolean Copy(String text)
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    using var process = Process.Start(new ProcessStartInfo("/usr/bin/pbcopy") {
                        UseShellExecute = false, RedirectStandardInput = true });
                    process.StandardInput.Write(text); process.StandardInput.Close();
                    if (!process.WaitForExit(2000)) { process.Kill(); return false; }
                    return process.ExitCode == 0;
                }
                if (!OperatingSystem.IsWindows()) return false;
                // SetClipboardData requires an owner after EmptyClipboard (Win32 contract).
                var owner = CreateWindowExW(0, "STATIC", "Vizhi draft recovery", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (owner == IntPtr.Zero) return false;
                if (!OpenClipboard(owner)) { DestroyWindow(owner); return false; }
                IntPtr memory = IntPtr.Zero;
                try
                {
                    var bytes = Encoding.Unicode.GetBytes(text + "\0");
                    memory = GlobalAlloc(2, (UIntPtr)bytes.Length);
                    if (memory == IntPtr.Zero) return false;
                    var pointer = GlobalLock(memory);
                    if (pointer == IntPtr.Zero) return false;
                    Marshal.Copy(bytes, 0, pointer, bytes.Length); GlobalUnlock(memory);
                    if (!EmptyClipboard() || SetClipboardData(13, memory) == IntPtr.Zero) return false;
                    memory = IntPtr.Zero; return true;
                }
                finally { if (memory != IntPtr.Zero) GlobalFree(memory); CloseClipboard(); DestroyWindow(owner); }
            }
            catch { return false; }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(UInt32 style, String cls, String name, UInt32 windowStyle, Int32 x, Int32 y, Int32 width, Int32 height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] private static extern Boolean DestroyWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern Boolean OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern Boolean CloseClipboard();
        [DllImport("user32.dll")] private static extern Boolean EmptyClipboard();
        [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(UInt32 format, IntPtr data);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(UInt32 flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern Boolean GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
    }
}
