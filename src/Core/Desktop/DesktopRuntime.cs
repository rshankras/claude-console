namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// First-run install of the AX helper from the plugin package — the voice-runtime pattern
    /// (BridgeManager.EnsureVoiceRuntimeInstalled) applied to a single binary: package-only
    /// installs get a working helper copied into the shared runtime home; dev builds (where
    /// tools/desktop/build.sh already installed it) are a no-op. Files unpacked from a
    /// downloaded .lplug4 carry com.apple.quarantine, so strip it after copying — a quarantined
    /// helper dies on first spawn with no visible error.
    /// </summary>
    internal static class DesktopRuntime
    {
        /// <summary>Idempotent; safe to call on every Load. Never throws.</summary>
        public static void EnsureInstalled(String pluginAssemblyFilePath)
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            try
            {
                var pluginDir = Path.GetDirectoryName(pluginAssemblyFilePath);
                if (String.IsNullOrEmpty(pluginDir))
                {
                    return;
                }

                var packaged = Path.Combine(pluginDir, "desktop", "VizhiAxBridge");
                if (!File.Exists(packaged) || File.Exists(MacDesktopAutomation.HelperPath))
                {
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(MacDesktopAutomation.HelperPath));

                // ditto preserves the code signature and exec bit; File.Copy would break the
                // signature the same way it broke the voice helper's.
                BoundedProcess.RunForExitCode("/usr/bin/ditto",
                    new List<String> { packaged, MacDesktopAutomation.HelperPath }, 10000);
                BoundedProcess.RunForExitCode("/usr/bin/xattr",
                    new List<String> { "-d", "com.apple.quarantine", MacDesktopAutomation.HelperPath }, 5000);

                PluginLog.Info($"DesktopRuntime: installed AX helper to {MacDesktopAutomation.HelperPath}");
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "DesktopRuntime.EnsureInstalled failed — desktop keys will report unavailable");
            }
        }
    }
}
