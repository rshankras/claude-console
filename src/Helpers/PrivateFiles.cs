namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.IO;

    /// <summary>
    /// Owner-only file hygiene for the IPC root (/tmp/claude-console). Session state, activity,
    /// and voice transcripts carry the user's prompts and dictation, so nothing here may be
    /// world-readable, and we refuse to follow a symlink another local user could have planted.
    /// Ported from Vizhi's VizhiPrivateFiles (0700 dirs / 0600 files, symlink refusal).
    /// </summary>
    internal static class PrivateFiles
    {
        private const UnixFileMode PrivateDirectoryMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        /// <summary>Create (or adopt) a directory, refuse symlinks, and force mode 0700.</summary>
        public static void EnsurePrivateDirectory(String path)
        {
            var directory = Directory.CreateDirectory(path);
            if (directory.LinkTarget != null)
            {
                throw new IOException($"Claude Console refuses to use symlinked directory {path}.");
            }
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(path, PrivateDirectoryMode);
            }
        }

        /// <summary>Refuse symlinks and force mode 0600 on an existing file.</summary>
        public static void EnsurePrivateFile(String path)
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                throw new FileNotFoundException("Claude Console private file is missing.", path);
            }
            if (file.LinkTarget != null)
            {
                throw new IOException($"Claude Console refuses to use symlinked file {path}.");
            }
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(path, PrivateFileMode);
            }
        }
    }
}
