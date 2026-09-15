namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>Best-effort, read-only CWD lookup before an agent has emitted its first hook.</summary>
    internal static class WindowsProcessDirectory
    {
        // Native PEB/RTL_USER_PROCESS_PARAMETERS layout (phnt ntpebteb.h/ntrtl.h).
        // Reject cross-bitness and malformed/changing data rather than inventing a project.
        internal static String Read(Int32 pid, DateTime expectedStart)
        {
            if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return null;
            var handle = OpenProcess(0x0410, false, pid); // QUERY_INFORMATION | VM_READ
            if (handle == IntPtr.Zero) return null;
            try
            {
                if (!GetProcessTimes(handle, out var created, out _, out _, out _)
                    || DateTime.FromFileTimeUtc(created) != expectedStart.ToUniversalTime()
                    || !IsWow64Process(handle, out var wow64) || wow64) return null;
                var info = new BasicInformation();
                if (NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf<BasicInformation>(), out _) != 0) return null;
                var pointer = Bytes(handle, info.Peb + 0x20, 8);
                if (pointer == null) return null;
                var parameters = new IntPtr(BitConverter.ToInt64(pointer, 0));
                var descriptor = Bytes(handle, parameters + 0x38, 16);
                if (descriptor == null) return null;
                var length = BitConverter.ToUInt16(descriptor, 0);
                if (length == 0 || length > 32766 || length % 2 != 0
                    || length > BitConverter.ToUInt16(descriptor, 2)) return null;
                var value = Bytes(handle, new IntPtr(BitConverter.ToInt64(descriptor, 8)), length);
                var after = Bytes(handle, parameters + 0x38, 16);
                if (value == null || after == null || !descriptor.SequenceEqual(after)) return null;
                var path = Encoding.Unicode.GetString(value);
                return path.IndexOf('\0') < 0 && WindowsProcessWatcher.IsWindowsRooted(path) ? path : null;
            }
            catch { return null; }
            finally { CloseHandle(handle); }
        }

        private static Byte[] Bytes(IntPtr handle, IntPtr address, Int32 length)
        {
            if (address == IntPtr.Zero) return null;
            var data = new Byte[length];
            return ReadProcessMemory(handle, address, data, (UIntPtr)length, out var read)
                && read.ToUInt64() == (UInt64)length ? data : null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicInformation { public IntPtr ExitStatus, Peb, Affinity, Priority, Pid, ParentPid; }
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(UInt32 access, Boolean inherit, Int32 pid);
        [DllImport("kernel32.dll")] private static extern Boolean CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern Boolean IsWow64Process(IntPtr handle, out Boolean wow64);
        [DllImport("kernel32.dll")] private static extern Boolean GetProcessTimes(IntPtr handle, out Int64 created, out Int64 exited, out Int64 kernel, out Int64 user);
        [DllImport("kernel32.dll")] private static extern Boolean ReadProcessMemory(IntPtr handle, IntPtr address, Byte[] data, UIntPtr length, out UIntPtr read);
        [DllImport("ntdll.dll")] private static extern Int32 NtQueryInformationProcess(IntPtr handle, Int32 kind, ref BasicInformation info, Int32 length, out Int32 returned);
    }
}
