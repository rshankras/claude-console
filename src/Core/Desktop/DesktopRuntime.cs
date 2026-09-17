namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>Install or refresh the packaged AX helper without exposing a partial binary.</summary>
    internal static class DesktopRuntime
    {
        /// <summary>Idempotent; safe to call on every Load. Never throws.</summary>
        public static void EnsureInstalled(String pluginAssemblyFilePath)
        {
            if (!OperatingSystem.IsMacOS()) return;
            try
            {
                var pluginDir = Path.GetDirectoryName(pluginAssemblyFilePath);
                if (String.IsNullOrEmpty(pluginDir)) return;
                if (Refresh(Path.Combine(pluginDir, "desktop", "VizhiAxBridge"),
                    MacDesktopAutomation.HelperPath, BoundedProcess.RunForExitCode))
                {
                    PluginLog.Info($"DesktopRuntime: refreshed AX helper at {MacDesktopAutomation.HelperPath}");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "DesktopRuntime: refresh failed; previous helper retained");
            }
        }

        // Paths and process runner are injected so upgrade and failure tests never touch the
        // user's runtime. Stage beside the destination so the final rename is atomic.
        internal static Boolean Refresh(String packaged, String target,
            Func<String, List<String>, Int32, Int32?> run)
        {
            if (!File.Exists(packaged) || SameContent(packaged, target)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            var staging = target + ".staging-" + Guid.NewGuid().ToString("N");
            try
            {
                if (run("/usr/bin/ditto", new List<String> { packaged, staging }, 10000) != 0
                    || !SameContent(packaged, staging))
                {
                    throw new IOException("AX helper copy failed or did not match the package");
                }
                // ditto preserves executable permissions and signing metadata. Verify before
                // replacing the old helper; a failed copy/signature must be retryable next load.
                if (run("/usr/bin/codesign", new List<String> { "--verify", "--strict", staging }, 5000) != 0)
                {
                    throw new IOException("AX helper signature verification failed");
                }
                // No quarantine attribute is a normal case (xattr then returns nonzero).
                run("/usr/bin/xattr", new List<String> { "-d", "com.apple.quarantine", staging }, 5000);
                File.Move(staging, target, overwrite: true);
                return true;
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
        }

        private static Boolean SameContent(String a, String b) => File.Exists(b)
            && new FileInfo(a).Length == new FileInfo(b).Length
            && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a)))
                == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(b)));
    }
}
