namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.IO;
    using System.Text.Json.Nodes;

    /// <summary>
    /// Removes application registrations we created whose plugin is no longer installed.
    ///
    /// WHY THIS HAS TO EXIST. A sideloaded install never creates its own application entry, so the
    /// plugin writes one itself (SelfRegistration). Uninstalling through Options+ removes the
    /// PLUGIN and leaves that entry behind — and an orphaned entry is not inert. It still claims
    /// the terminal, it still wins activation against a plugin that IS installed, and every key on
    /// its profile points at actions that no longer exist. The visible result is a keypad of
    /// exclamation marks and a working plugin that appears broken, with the uninstalled product
    /// nowhere in the plugin list to explain it. Observed on hardware, 2026-08-18.
    ///
    /// The plugin cannot clean up after its own uninstall — it is gone by then. So each surviving
    /// plugin sweeps on load. That is only safe because ownership is explicit: SelfRegistration
    /// stamps <see cref="OwnerKey"/> into the document it writes, and nothing without that stamp is
    /// ever touched. A registration created by Logitech, by another vendor, or by hand is not ours
    /// to delete however orphaned it looks.
    /// </summary>
    internal static class RegistrationCleanup
    {
        /// <summary>Marks a registration as written by one of our products, and by which.</summary>
        internal const String OwnerKey = "selfRegisteredBy";

        /// <summary>
        /// Delete our orphans. <paramref name="runningPlugin"/> is never removed — a transient
        /// failure to see our own plugin directory must not delete the entry we are using.
        /// Never throws: this runs during plugin load.
        /// </summary>
        internal static Int32 RemoveOrphans(String appsRoot, String pluginsRoot, String runningPlugin)
        {
            var removed = 0;

            try
            {
                if (!Directory.Exists(appsRoot) || !Directory.Exists(pluginsRoot))
                {
                    return 0;
                }

                foreach (var deviceDir in Directory.GetDirectories(appsRoot))
                {
                    foreach (var appDir in Directory.GetDirectories(deviceDir))
                    {
                        if (IsOurOrphan(appDir, pluginsRoot, runningPlugin))
                        {
                            Directory.Delete(appDir, recursive: true);
                            removed++;
                            PluginLog.Info(
                                $"RegistrationCleanup: removed orphaned registration {Path.GetFileName(appDir)} " +
                                "— its plugin is no longer installed, and leaving it would hold the terminal " +
                                "with keys that cannot resolve");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { PluginLog.Warning($"RegistrationCleanup: skipped ({ex.Message})"); } catch { }
            }

            return removed;
        }

        internal static Boolean IsOurOrphan(String appDir, String pluginsRoot, String runningPlugin)
        {
            try
            {
                var infoPath = Path.Combine(appDir, "ApplicationInfo.json");
                if (!File.Exists(infoPath))
                {
                    return false;
                }

                var info = JsonNode.Parse(File.ReadAllText(infoPath));

                // Ours, and only ours. An unstamped entry belongs to someone else.
                var owner = (String)info?[OwnerKey];
                if (String.IsNullOrWhiteSpace(owner))
                {
                    return false;
                }

                var plugin = (String)info?["nativePluginName"];
                if (String.IsNullOrWhiteSpace(plugin))
                {
                    return false;
                }

                // Never remove the entry the running plugin depends on.
                if (String.Equals(plugin, runningPlugin, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Orphaned = the plugin it names is not installed.
                return !Directory.Exists(Path.Combine(pluginsRoot, plugin));
            }
            catch (Exception)
            {
                // Unreadable is not "ours" — leave it alone.
                return false;
            }
        }
    }
}
