namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;

    internal static class MacProcessDirectory
    {
        // Darwin proc_vnodepathinfo: two vnode_info_path records (152 byte vnode info + MAXPATHLEN).
        // Layout checked against sys/proc_info.h; supported macOS targets are 64-bit.
        [DllImport("/usr/lib/libproc.dylib")]
        private static extern Int32 proc_pidinfo(Int32 pid, Int32 flavor, UInt64 arg, Byte[] buffer, Int32 size);

        internal static String Read(Int32 pid)
        {
            if (!OperatingSystem.IsMacOS()) { return null; }
            var buffer = new Byte[2352];
            try
            {
                if (proc_pidinfo(pid, 9, 0, buffer, buffer.Length) != buffer.Length) { return null; }
                var end = Array.IndexOf(buffer, (Byte)0, 152, 1024);
                return end > 152 ? Encoding.UTF8.GetString(buffer, 152, end - 152) : null;
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
        }

        // proc_bsdinfo (flavor PROC_PIDTBSDINFO = 3, 136 bytes): pbi_start_tvsec at offset 120
        // and pbi_start_tvusec at 128, both uint64. Offsets checked against `ps -o lstart` for
        // two live processes on macOS 26 (2026-09-28); the seconds bound rejects a misread.
        internal static DateTime? StartTimeUtc(Int32 pid)
        {
            if (!OperatingSystem.IsMacOS()) { return null; }
            var buffer = new Byte[136];
            try
            {
                if (proc_pidinfo(pid, 3, 0, buffer, buffer.Length) != buffer.Length) { return null; }
                var seconds = BitConverter.ToUInt64(buffer, 120);
                var micros = BitConverter.ToUInt64(buffer, 128);
                if (seconds == 0 || seconds > 4102444800UL || micros >= 1_000_000UL) { return null; }
                return DateTime.UnixEpoch.AddSeconds(seconds).AddTicks((Int64)micros * 10);
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
        }
    }
}
