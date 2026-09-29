namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;

    internal sealed record DesktopFile(String Path, Int64 Size, Int64 Modified)
    {
        internal const Int32 MaximumCount = 8;
        internal const Int64 MaximumSize = 50L * 1024 * 1024;
        internal const Int64 MaximumTotalSize = 100L * 1024 * 1024;
        internal String Name => System.IO.Path.GetFileName(Path);
        internal String Id => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Path}\n{Size}\n{Modified}")));
        internal Boolean IsCurrent => this == Read(Path);
        internal static DesktopFile Read(String path)
        {
            try
            {
                var info = new FileInfo(System.IO.Path.GetFullPath(path));
                if (!info.Exists || (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
                    || info.LinkTarget != null || info.Length <= 0 || info.Length > MaximumSize) return null;
                return new(info.FullName, info.Length, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { return null; }
        }
    }
}
