namespace Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration
{
    using System;
    using System.IO;
    using System.Globalization;
    using System.Text.Json.Nodes;

    /// <summary>
    /// What an Options+ uninstall removes, and what survives the minutes an upgrade takes.
    ///
    /// Options+ runs the SAME Uninstall() callback for a real uninstall and for a package
    /// replacement (upgrade), and nothing in the callback can tell them apart. The runtime —
    /// helpers, speech model, captures, IPC — is removed outright; what a user would miss across
    /// an upgrade is small and is parked beside a timestamped receipt that the next load within
    /// ten minutes honours: an unsent dictation, and the registration with its customised or
    /// deliberately deleted profiles (#138). An expired receipt is discarded on the next load.
    ///
    /// The speech model is deliberately NOT parked. Keeping 148 MB alive past an uninstall needs
    /// a timer process the plugin cannot own once it is gone, and a lingering process after an
    /// uninstall is its own finding; the next voice press after an upgrade copies the model from a
    /// sibling product when one has it, and downloads it otherwise, saying so on the key.
    ///
    /// User settings (workflows, labels, voice shortcut) are kept in a small sibling backup on
    /// purpose, upgrade or not; that is the one deliberate leftover and the README says so.
    /// </summary>
    internal static class DesktopUninstall
    {
        internal const String PendingDraftFile = "pending-draft.json";
        internal static readonly TimeSpan ReplacementWindow = TimeSpan.FromMinutes(10);

        internal static String SettingsDirectory(String runtime) => runtime + " Settings";
        internal static String RegistrationReceipt(String runtime) => Path.Combine(SettingsDirectory(runtime), "registration-reinstall");
        internal static String RuntimeCache(String runtime) => Path.Combine(SettingsDirectory(runtime), "reinstall-cache");

        /// <summary>Remove the product's runtime, IPC roots and owned registration, parking what an upgrade must keep.</summary>
        internal static Boolean Clean(String runtime, String ipc, String appsRoot, DateTime? utcNow = null)
        {
            var now = utcNow ?? DateTime.UtcNow;
            try
            {
                if (Directory.Exists(runtime))
                {
                    var settings = SettingsDirectory(runtime);
                    foreach (var name in ProductRuntime.DesktopSettings)
                    {
                        var source = Path.Combine(runtime, name);
                        if (!File.Exists(source)) continue;
                        PrivateFiles.EnsurePrivateDirectory(settings);
                        File.Copy(source, Path.Combine(settings, name), true);
                        PrivateFiles.EnsurePrivateFile(Path.Combine(settings, name));
                    }
                    ParkDraft(runtime, now);
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
                            // Keep the registration (custom AND deliberately deleted profiles, #138)
                            // for the replacement window, so an upgrade does not reset the layout.
                            var receipt = RegistrationReceipt(runtime);
                            PrivateFiles.EnsurePrivateDirectory(SettingsDirectory(runtime));
                            var backup = Path.Combine(receipt, Path.GetFileName(device), "@_vizhidesktop");
                            if (Directory.Exists(backup)) Directory.Delete(backup, true);
                            CopyTree(app, backup);
                            WriteStamp(receipt, now);
                            Directory.Delete(app, true);
                        }
                    }
                return true;
            }
            catch (Exception ex) { PluginLog.Warning(ex, "Desktop uninstall cleanup failed"); return false; }
        }

        // An unsent dictation is a few hundred bytes of the user's own words: parked, never deleted
        // by an upgrade.
        private static void ParkDraft(String runtime, DateTime now)
        {
            var draft = Path.Combine(runtime, PendingDraftFile);
            if (!File.Exists(draft)) return;
            var cache = RuntimeCache(runtime);
            if (Directory.Exists(cache)) Directory.Delete(cache, true);
            PrivateFiles.EnsurePrivateDirectory(cache);
            File.Move(draft, Path.Combine(cache, PendingDraftFile));
            PrivateFiles.EnsurePrivateFile(Path.Combine(cache, PendingDraftFile));
            WriteStamp(cache, now);
        }

        /// <summary>
        /// Put a parked dictation back if a reinstall followed within the window; discard it
        /// otherwise. Runs in the plugin CONSTRUCTOR, before the draft recovery reads its file.
        /// Never throws.
        /// </summary>
        internal static void RestoreRuntimeCache(String runtime, DateTime? utcNow = null)
        {
            var cache = RuntimeCache(runtime);
            try
            {
                if (!Directory.Exists(cache)) return;
                if (!StampIsFresh(cache, utcNow ?? DateTime.UtcNow)) { Directory.Delete(cache, true); return; }
                var draft = Path.Combine(cache, PendingDraftFile);
                if (File.Exists(draft) && !File.Exists(Path.Combine(runtime, PendingDraftFile)))
                {
                    PrivateFiles.EnsurePrivateDirectory(runtime);
                    File.Move(draft, Path.Combine(runtime, PendingDraftFile));
                    PluginLog.Info("DesktopUninstall: restored the pending dictation after a reinstall");
                }
                Directory.Delete(cache, true);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "DesktopUninstall: the parked dictation could not be restored");
                try { if (Directory.Exists(cache)) Directory.Delete(cache, true); } catch { }
            }
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

        /// <summary>
        /// Restore a parked registration if the reinstall came within the window. Best effort and
        /// never throws: a load must not fail because a layout could not be copied back (#138 review).
        /// A receipt that fails to restore is discarded, so the failure is not repeated on every load.
        /// </summary>
        internal static Boolean TryRestoreRegistration(String runtime, String appsRoot, DateTime? utcNow = null)
        {
            try
            {
                RestoreRegistration(runtime, appsRoot, utcNow);
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "DesktopUninstall: the keypad layout could not be restored after the reinstall; the packaged layout applies");
                try { var receipt = RegistrationReceipt(runtime); if (Directory.Exists(receipt)) Directory.Delete(receipt, true); } catch { }
                return false;
            }
        }

        internal static void RestoreRegistration(String runtime, String appsRoot, DateTime? utcNow = null)
        {
            var receipt = RegistrationReceipt(runtime);
            if (!Directory.Exists(receipt)) return;
            if (!StampIsFresh(receipt, utcNow ?? DateTime.UtcNow)) { Directory.Delete(receipt, true); return; }
            foreach (var device in Directory.GetDirectories(receipt))
            {
                var source = Path.Combine(device, "@_vizhidesktop");
                var target = Path.Combine(appsRoot, Path.GetFileName(device), "@_vizhidesktop");
                if (!Directory.Exists(source) || Directory.Exists(target)) continue;
                var temporary = target + ".restore-" + Guid.NewGuid().ToString("N");
                try
                {
                    CopyTree(source, temporary);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    Directory.Move(temporary, target);
                }
                finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
            }
            Directory.Delete(receipt, true);
        }

        internal static void RestoreSettings(String runtime)
        {
            var settings = SettingsDirectory(runtime);
            foreach (var name in ProductRuntime.DesktopSettings)
            {
                var source = Path.Combine(settings, name); var target = Path.Combine(runtime, name);
                if (!File.Exists(source) || File.Exists(target)) continue;
                PrivateFiles.EnsurePrivateDirectory(runtime); File.Copy(source, target);
                PrivateFiles.EnsurePrivateFile(target);
            }
        }

        private static void WriteStamp(String directory, DateTime now) =>
            File.WriteAllText(Path.Combine(directory, "written-at"), now.ToString("O", CultureInfo.InvariantCulture));

        private static Boolean StampIsFresh(String directory, DateTime now)
        {
            var stamp = Path.Combine(directory, "written-at");
            return File.Exists(stamp)
                && DateTime.TryParse(File.ReadAllText(stamp), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var written)
                && now - written <= ReplacementWindow
                && written - now <= TimeSpan.FromMinutes(1);
        }
    }
}
