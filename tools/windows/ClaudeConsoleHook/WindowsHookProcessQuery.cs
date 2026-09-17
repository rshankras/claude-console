#nullable enable
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
internal static class WindowsHookProcessQuery
{
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(UInt32 access, Boolean inherit, Int32 pid);
    [DllImport("kernel32.dll")] private static extern Boolean CloseHandle(IntPtr handle);
    [DllImport("ntdll.dll", EntryPoint = "NtQueryInformationProcess")]
    private static extern Int32 QueryProcessBuffer(IntPtr process, Int32 informationClass,
        IntPtr buffer, Int32 size, out Int32 needed);

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public UInt16 Length;
        public UInt16 MaximumLength;
        public IntPtr Buffer;
    }

    // ProcessCommandLineInformation (60) returns a UNICODE_STRING in our own buffer.
    // See https://github.com/winsiderss/phnt/blob/master/ntpsapi.h . Unlike WMI this
    // does not spawn a shell per interpreter ancestor. Unsupported/denied queries
    // return no match, never a guessed session identity.
    [SupportedOSPlatform("windows")]
    internal static String? CommandLineViaNtQuery(Int32 pid)
    {
        var handle = IntPtr.Zero;
        var buffer = IntPtr.Zero;
        try
        {
            handle = OpenProcess(0x1000, false, pid);
            if (handle == IntPtr.Zero) { return null; }
            QueryProcessBuffer(handle, 60, IntPtr.Zero, 0, out var needed);
            if (needed < Marshal.SizeOf<UnicodeString>() || needed > 128 * 1024) { return null; }
            buffer = Marshal.AllocHGlobal(needed);
            if (QueryProcessBuffer(handle, 60, buffer, needed, out _) != 0) { return null; }
            var value = Marshal.PtrToStructure<UnicodeString>(buffer);
            var offset = value.Buffer.ToInt64() - buffer.ToInt64();
            if (value.Length == 0 || value.Length % 2 != 0 || value.Length > value.MaximumLength ||
                offset < Marshal.SizeOf<UnicodeString>() || offset > needed - value.Length) { return null; }
            return Marshal.PtrToStringUni(value.Buffer, value.Length / 2);
        }
        catch { return null; }
        finally
        {
            if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); }
            if (handle != IntPtr.Zero) { CloseHandle(handle); }
        }
    }

}
