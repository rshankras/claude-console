// Desktop-owned ON PURPOSE. Main deleted this family from Core when the terminal plugins went
// universal (#23): a universal plugin claims no application, so it has nothing to register,
// heal or sweep. Vizhi Desktop is deliberately app-bound — it declares HasNoApplication => false
// and binds the ChatGPT bundle — so it still needs all three, and it is the ONLY product that
// does. That is why the code lives under the product and not in src/Core: putting it back in
// Core would reintroduce registration machinery to two products that must never run it.
//
// Depends only on PluginPaths, IpcPaths and PluginLog, all still in Core.
namespace Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration
{
    using Loupedeck.ClaudeConsolePlugin.Platform;
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
                return !IsInstalled(pluginsRoot, plugin);
            }
            catch (Exception)
            {
                // Unreadable is not "ours" — leave it alone.
                return false;
            }
        }

        /// <summary>
        /// Is this plugin installed, in EITHER of the two shapes the service accepts? A packaged
        /// install is a directory; a development build is a `&lt;AssemblyName&gt;.link` file pointing at
        /// a build tree. Both are installations, and only the first was recognised here.
        ///
        /// The gap was not theoretical. With one product installed as a package and another
        /// dev-linked, the packaged one saw no directory for the dev-linked one, judged its
        /// registration an orphan and deleted it; the dev-linked product reloaded, found no
        /// registration, wrote one and restarted the service to adopt it — which handed the
        /// packaged product another boot in which to delete it again. The result was a service
        /// restart loop that thrashed Options+ every few seconds (observed on hardware,
        /// 2026-08-25). SelfRegistration's loop-safety rests on "the first thing a success does is
        /// create the registration"; that invariant only holds if nobody else deletes it.
        /// </summary>
        private static Boolean IsInstalled(String pluginsRoot, String plugin)
        {
            if (Directory.Exists(Path.Combine(pluginsRoot, plugin)))
            {
                return true;
            }

            try
            {
                // The link is named for the ASSEMBLY (VizhiDesktopPlugin.link), the registration
                // for the PLUGIN (VizhiDesktop) — hence the prefix match rather than an exact name.
                return Directory.GetFiles(pluginsRoot, plugin + "*.link").Length > 0;
            }
            catch (Exception)
            {
                // Cannot enumerate: assume installed. Deleting a live product's registration is
                // far worse than leaving a stale one for the next sweep.
                return true;
            }
        }
    }
}
