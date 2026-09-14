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
    }
}
