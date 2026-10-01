namespace Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration
{
    using System;
    using System.IO;
    using System.Globalization;
    using System.Text.Json.Nodes;

    internal static class DesktopUninstall
    {
        // User settings survive reinstall in a small, explicit settings backup. Runtime binaries,
        // models, recordings, pending dictation and registration are removed on every uninstall.
        internal static Boolean Clean(String runtime, String ipc, String appsRoot, DateTime? utcNow = null)
        {
            try
            {
                if (Directory.Exists(runtime))
                {
                    var settings = runtime + " Settings";
                    foreach (var name in ProductRuntime.DesktopSettings)
                    {
                        var source = Path.Combine(runtime, name);
                        if (!File.Exists(source)) continue;
                        PrivateFiles.EnsurePrivateDirectory(settings);
                        File.Copy(source, Path.Combine(settings, name), true);
                        PrivateFiles.EnsurePrivateFile(Path.Combine(settings, name));
                    }
                    Directory.Delete(runtime, true);
                }
                if (Directory.Exists(ipc)) Directory.Delete(ipc, true);
                if (Directory.Exists(appsRoot))
                    foreach (var device in Directory.GetDirectories(appsRoot))
                    {
                        var app = Path.Combine(device, "@_vizhidesktop");
                        var info = Path.Combine(app, "ApplicationInfo.json");
                        if (!File.Exists(info)) continue;
                        var node = JsonNode.Parse(File.ReadAllText(info));
                        if ((String)node?[RegistrationCleanup.OwnerKey] == "VizhiDesktop")
                        {
                            // Options+ invokes Uninstall during replacement. Keep a short-lived
                            // receipt so custom/deleted profiles survive that callback pair.
                            var receipt = Path.Combine(runtime + " Settings", "registration-reinstall");
                            PrivateFiles.EnsurePrivateDirectory(runtime + " Settings");
                            var backup = Path.Combine(receipt, Path.GetFileName(device), "@_vizhidesktop");
                            if (Directory.Exists(backup)) Directory.Delete(backup, true);
                            CopyTree(app, backup);
                            File.WriteAllText(Path.Combine(receipt, "written-at"), (utcNow ?? DateTime.UtcNow).ToString("O", CultureInfo.InvariantCulture));
                            Directory.Delete(app, true);
                        }
                    }
                return true;
            }
            catch (Exception ex) { PluginLog.Warning(ex, "Desktop uninstall cleanup failed"); return false; }
        }
        private static void CopyTree(String source, String target)
        {
            if (new DirectoryInfo(source).LinkTarget != null) throw new IOException("Refusing linked registration");
            PrivateFiles.EnsurePrivateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                if (new FileInfo(file).LinkTarget != null) throw new IOException("Refusing linked registration file");
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false);
            }
            foreach (var directory in Directory.GetDirectories(source)) CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
        internal static void RestoreRegistration(String runtime, String appsRoot, DateTime? utcNow = null)
        {
            var receipt = Path.Combine(runtime + " Settings", "registration-reinstall");
            if (!Directory.Exists(receipt)) return;
            var stamp = Path.Combine(receipt, "written-at");
            if (!File.Exists(stamp) || !DateTime.TryParse(File.ReadAllText(stamp), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var written)
                || (utcNow ?? DateTime.UtcNow) - written > TimeSpan.FromMinutes(10)
                || written - (utcNow ?? DateTime.UtcNow) > TimeSpan.FromMinutes(1))
            { Directory.Delete(receipt, true); return; }
            foreach (var device in Directory.GetDirectories(receipt))
            {
                var source = Path.Combine(device, "@_vizhidesktop");
                var target = Path.Combine(appsRoot, Path.GetFileName(device), "@_vizhidesktop");
                if (!Directory.Exists(source) || Directory.Exists(target)) continue;
                var temporary = target + ".restore-" + Guid.NewGuid().ToString("N");
                try { CopyTree(source, temporary); Directory.Move(temporary, target); }
                finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
            }
            Directory.Delete(receipt, true);
        }

        internal static void RestoreSettings(String runtime)
        {
            var settings = runtime + " Settings";
            foreach (var name in ProductRuntime.DesktopSettings)
            {
                var source = Path.Combine(settings, name); var target = Path.Combine(runtime, name);
                if (!File.Exists(source) || File.Exists(target)) continue;
                PrivateFiles.EnsurePrivateDirectory(runtime); File.Copy(source, target);
                PrivateFiles.EnsurePrivateFile(target);
            }
        }
    }
}
