namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.IO;

    // Product-owned storage. No migration ever changes the source product's files.
    internal static class ProductRuntime
    {
        internal static String Home => BridgeManager.HomeOverride == null && OperatingSystem.IsWindows() && IpcPaths.ProductSlug == "vizhi-desktop"
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vizhi Desktop") : For(BridgeManager.HomeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), IpcPaths.ProductSlug, OperatingSystem.IsWindows());
        internal static String For(String home, String product, Boolean windows) => product switch
        {
            "vizhi-desktop" => Path.Combine(home, windows ? "AppData/Local" : "Library/Application Support", "Vizhi Desktop"),
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
