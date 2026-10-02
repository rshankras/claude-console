namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.IO;

    // Product-owned storage. No migration ever changes the source product's files.
    //
    // Vizhi Desktop keeps its files where the Logi Actions SDK says a plugin's data goes:
    // Plugin.GetPluginDataDirectory(), i.e. <Logi Plugin Service data>/PluginData/VizhiDesktop —
    // the same drawer Logitech's own Spotify and Zoom plugins use. The product hands the SDK's
    // answer to SdkHome at construction; For() reproduces the conventional path for tests and
    // for the sibling-product scan that reuses an already-downloaded speech model. The terminal
    // products stay beside their agent's own configuration on purpose: the agent's hooks and
    // scripts run from there without the plugin.
    internal static class ProductRuntime
    {
        /// <summary>The directory the SDK reported for this plugin's data; null outside a running plugin.</summary>
        internal static String SdkHome { get; set; }

        internal static String Home
        {
            get
            {
                if (SdkHome != null && BridgeManager.HomeOverride == null) return SdkHome;
                if (BridgeManager.HomeOverride == null && OperatingSystem.IsWindows() && IpcPaths.ProductSlug == "vizhi-desktop")
                {
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Logi", "LogiPluginService", "PluginData", "VizhiDesktop");
                }
                return For(BridgeManager.HomeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    IpcPaths.ProductSlug, OperatingSystem.IsWindows());
            }
        }

        internal static String For(String home, String product, Boolean windows) => product switch
        {
            "vizhi-desktop" => Path.Combine(home, windows ? "AppData/Local" : "Library/Application Support",
                "Logi", "LogiPluginService", "PluginData", "VizhiDesktop"),
            "codex-console" => Path.Combine(home, ".codex", "vizhi-runtime"),
            _ => Path.Combine(home, ".claude", "claude-console"),
        };
        internal static readonly String[] DesktopSettings = {
            "desktop-workflows.json", "desktop-chatgpt-workflows.json", "desktop-voice-shortcut.json",
            "desktop-conversation-labels.json"
        };
        internal static void MigrateDesktop(String home, String target)
        {
            var marker = Path.Combine(target, ".desktop-migrated");
            if (File.Exists(marker)) return;
            PrivateFiles.EnsurePrivateDirectory(target);
            var source = Path.Combine(home, ".claude", "claude-console");
            foreach (var name in DesktopSettings)
            {
                CopyMissing(Path.Combine(source, name), Path.Combine(target, name));
                if (Directory.Exists(source))
                    foreach (var backup in Directory.GetFiles(source, name + ".before-*"))
                        CopyMissing(backup, Path.Combine(target, Path.GetFileName(backup)));
            }
            File.WriteAllText(marker, "1");
        }
        private static void CopyMissing(String source, String target)
        {
            if (!File.Exists(source) || File.Exists(target)) return;
            var temporary = target + ".migrate-" + Guid.NewGuid().ToString("N");
            try { File.Copy(source, temporary); File.Move(temporary, target, false); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
