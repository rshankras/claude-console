namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>Recover a refused draft without focusing an app or synthesizing a paste.</summary>
    internal static class DesktopDraftClipboard
    {
        internal static Boolean Copy(String text) => OperatingSystem.IsMacOS() &&
            Copy(text, IpcPaths.VoiceDir, BoundedProcess.RunForExitCode);

        internal static Boolean Copy(String text, String directory,
            Func<String, List<String>, Int32, Int32?> run)
        {
            if (String.IsNullOrWhiteSpace(text)) { return false; }
            String path = null;
            try
            {
                PrivateFiles.EnsurePrivateDirectory(directory);
                path = Path.Combine(directory, "clipboard-" + Guid.NewGuid().ToString("N") + ".txt");
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows())
                {
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }
                using (var stream = new FileStream(path, options))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) { writer.Write(text); }

                // Fixed script; the path is a separate argv value and words are only stdin data.
                // pbcopy never sends a key or accesses the destination app. RunForExitCode bounds
                // the entire operation and drains both output streams while waiting.
                return run("/bin/sh", new List<String>
                    { "-c", "exec /usr/bin/pbcopy < \"$1\"", "vizhi-draft-copy", path }, 2000) == 0;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "DesktopDraftClipboard: draft could not be copied");
                return false;
            }
            finally
            {
                if (path != null) { try { File.Delete(path); } catch { } }
            }
        }
    }
}
