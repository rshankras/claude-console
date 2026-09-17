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
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    /// <summary>
    /// Creates the @_claudeconsole application registration when it is missing entirely.
    ///
    /// A sideloaded .lplug4 install NEVER creates the registration — proven 2026-08-08 on a
    /// clean macOS account, and matching the Windows clean install exactly. Only Marketplace
    /// installs write registrations at install time; every machine that "worked" here got its
    /// entry from dev-era service activity that predates packaging (@_claudeconsole born during
    /// phase-0 dev, @_vizhi born a month before its first package install). Nothing recreates a
    /// missing entry either: with it moved aside, 90 seconds of service restart, plugin load,
    /// target-app activation and Options+ produced nothing. Without the entry there is no
    /// application icon in Options+ and no keypad layout — a first install looks dead on arrival.
    ///
    /// So the plugin performs the registration itself. The packaged DefaultProfile70.lp5 already
    /// carries the complete registration document — an ApplicationInfo.json whose
    /// defaultProfileName names the profile it wraps — and the payload carries the icon. The
    /// service adopts hand-written registration dirs at startup (validated end-to-end by the
    /// Windows manual recovery, 2026-08-08), so writing the files and restarting the service IS
    /// the install step the package system never performs. This also turns the Windows reinstall
    /// story around: its uninstall deletes the registration outright, which previously meant a
    /// manual re-import — now the next load rebuilds the default layout unaided.
    ///
    /// Loop-safety is structural: the trigger is "no registration directory exists anywhere",
    /// and the first thing a successful pass does is create one.
    /// </summary>
    internal static class SelfRegistration
    {
        /// <summary>
        /// Register if missing and schedule the adopting service restart. Safe to call on every
        /// load; never throws. Returns true when it registered — the caller should then skip
        /// RegistrationHeal (this load's restart already covers it).
        /// </summary>
        internal static Boolean RegisterIfMissing(String windowsProcessName = null)
        {
            try
            {
                if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
                {
                    return false;
                }

                var pluginDir = PluginPaths.PluginDirectory;         // .../Plugins/ClaudeConsole/bin
                var payloadRoot = String.IsNullOrEmpty(pluginDir) ? null : Path.GetDirectoryName(pluginDir);
                if (String.IsNullOrEmpty(payloadRoot))
                {
                    return false;
                }

                var lp5 = Path.Combine(payloadRoot, "profiles", "DefaultProfile70.lp5");
                if (!File.Exists(lp5))
                {
                    return false;                                    // dev tree without the package payload
                }

                // The package names itself; nothing here hardcodes a product. Two consoles built
                // from this repo therefore register under their own entries instead of overwriting
                // each other's — the identity travels with the profile, not with the code.
                var appName = ReadApplicationName(lp5);
                if (appName == null)
                {
                    return false;
                }

                var appsRoot = RegistrationHeal.ApplicationsRoot();
                var icon = Path.Combine(payloadRoot, "metadata", "Icon256x256.png");
                if (RegistrationExists(appsRoot, appName))
                {
                    if (UpdateOwnedDefaultProfileIfNeeded(
                        lp5, File.Exists(icon) ? icon : null, appsRoot,
                        OperatingSystem.IsWindows(), windowsProcessName))
                    {
                        PluginLog.Info(
                            "SelfRegistration: installed a new packaged default profile without " +
                            "overwriting the previous profile; restarting Logi Plugin Service in 10s");
                        Process.Start(OperatingSystem.IsWindows()
                            ? RegistrationHeal.WindowsRestart()
                            : RegistrationHeal.MacRestart());
                        return true;
                    }
                    return false;
                }
                CreateRegistration(
                    lp5, File.Exists(icon) ? icon : null, appsRoot, OperatingSystem.IsWindows(), windowsProcessName);

                PluginLog.Info(
                    "SelfRegistration: no application registration on disk (sideloaded installs never create one) — " +
                    "wrote it from the packaged profile; restarting Logi Plugin Service in 10s so it adopts the entry");

                Process.Start(OperatingSystem.IsWindows()
                    ? RegistrationHeal.WindowsRestart()
                    : RegistrationHeal.MacRestart());
                return true;
            }
            catch (Exception ex)
            {
                try { PluginLog.Warning($"SelfRegistration: skipped ({ex.Message})"); } catch { }
                return false;
            }
        }

        /// <summary>
        /// The application name the packaged profile declares (e.g. "@_claudeconsole"). This is the
        /// registration's identity, and reading it rather than assuming it is what keeps two
        /// products from claiming the same entry. Null when the package can't be read.
        /// </summary>
        internal static String ReadApplicationName(String lp5Path)
        {
            try
            {
                using var zip = ZipFile.OpenRead(lp5Path);
                var entry = zip.GetEntry("ApplicationInfo.json");
                if (entry == null)
                {
                    return null;
                }

                using var stream = entry.Open();
                var name = (String)JsonNode.Parse(stream)?["name"];
                return String.IsNullOrWhiteSpace(name) ? null : name;
            }
            catch (Exception ex)
            {
                try { PluginLog.Warning($"SelfRegistration: cannot read application name ({ex.Message})"); } catch { }
                return null;
            }
        }

        /// <summary>True when any device type already has a registration for this application.</summary>
        internal static Boolean RegistrationExists(String appsRoot, String appName)
        {
            if (String.IsNullOrEmpty(appsRoot) || !Directory.Exists(appsRoot))
            {
                return false;
            }

            foreach (var deviceDir in Directory.GetDirectories(appsRoot))
            {
                if (File.Exists(Path.Combine(deviceDir, appName, "ApplicationInfo.json")))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Adopt a newly versioned packaged profile into an existing self-registered application.
        /// The old profile directory is deliberately retained: it may contain user customization
        /// or icon snapshots. Only registrations stamped as ours or naming this plugin as their
        /// native owner are eligible, and the application document is replaced atomically after
        /// the new profile is complete. Options+ may remove the custom ownership stamp when it
        /// rewrites an application, but it preserves nativePluginName.
        /// </summary>
        internal static Boolean UpdateOwnedDefaultProfileIfNeeded(
            String lp5Path, String iconPath, String appsRoot, Boolean windows,
            String windowsProcessName = null)
        {
            using var zip = ZipFile.OpenRead(lp5Path);
            var packaged = ReadApplicationInfo(zip, windows, windowsProcessName);
            var deviceType = (String)packaged["deviceType"] ?? "Loupedeck70";
            var appName = (String)packaged["name"];
            var nextProfile = (String)packaged["defaultProfileName"];
            var pluginName = (String)packaged["nativePluginName"];
            if (String.IsNullOrWhiteSpace(appName) || String.IsNullOrWhiteSpace(nextProfile)
                || String.IsNullOrWhiteSpace(pluginName))
            {
                return false;
            }

            var appDir = Path.Combine(appsRoot, deviceType, appName);
            var infoPath = Path.Combine(appDir, "ApplicationInfo.json");
            if (!File.Exists(infoPath))
            {
                return false;
            }

            var installed = JsonNode.Parse(File.ReadAllText(infoPath));
            var stampedOwner = (String)installed?[RegistrationCleanup.OwnerKey];
            var nativeOwner = (String)installed?["nativePluginName"];
            var ours = String.Equals(stampedOwner, pluginName, StringComparison.Ordinal)
                || String.Equals(nativeOwner, pluginName, StringComparison.Ordinal);
            if (!ours
                || String.Equals((String)installed?["defaultProfileName"], nextProfile,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var profilesDir = Path.Combine(appDir, "Profiles");
            var profileDir = Path.Combine(profilesDir, nextProfile);
            String staging = null;
            try
            {
                if (!Directory.Exists(profileDir))
                {
                    Directory.CreateDirectory(profilesDir);
                    staging = profileDir + ".staging-" + Guid.NewGuid().ToString("N");
                    Directory.CreateDirectory(staging);
                    ExtractProfile(zip, staging);
                    Directory.Move(staging, profileDir);
                    staging = null;
                }

                // Keep service/user settings from the installed document, but refresh the
                // package-owned identity and binding fields alongside the new default pointer.
                foreach (var field in new[]
                {
                    "name", "displayName", "description", "deviceType", "nativePluginName",
                    "hasNativePlugin", "processOrBundleName", "modes", "defaultProfileName",
                })
                {
                    installed[field] = packaged[field]?.DeepClone();
                }
                installed[RegistrationCleanup.OwnerKey] = pluginName;

                var tempInfo = infoPath + ".tmp-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(tempInfo,
                    installed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tempInfo, infoPath, overwrite: true);

                if (iconPath != null)
                {
                    File.Copy(iconPath, Path.Combine(appDir, "ApplicationIcon.png"), overwrite: true);
                }
                return true;
            }
            catch
            {
                if (staging != null)
                {
                    try { Directory.Delete(staging, recursive: true); } catch { }
                }
                throw;
            }
        }

        /// <summary>
        /// Write the registration directory from the packaged profile: the lp5's own
        /// ApplicationInfo.json at the top (patched for Windows), the icon beside it, and the
        /// profile content under Profiles/&lt;defaultProfileName&gt;/ — the exact layout of a
        /// working registration. Throws on any failure after removing the partial directory, so
        /// a later load retries from scratch rather than the service adopting half an entry.
        /// </summary>
        internal static void CreateRegistration(
            String lp5Path, String iconPath, String appsRoot, Boolean windows, String windowsProcessName = null)
        {
            using var zip = ZipFile.OpenRead(lp5Path);

            var appInfo = ReadApplicationInfo(zip, windows, windowsProcessName);

            var deviceType = (String)appInfo["deviceType"] ?? "Loupedeck70";
            var profileName = (String)appInfo["defaultProfileName"]
                ?? throw new InvalidDataException("packaged ApplicationInfo has no defaultProfileName");

            var appName = (String)appInfo["name"]
                ?? throw new InvalidDataException("packaged ApplicationInfo has no name");

            // Stamp ownership. Uninstalling through Options+ removes the plugin and leaves this
            // entry behind, where it still claims the terminal with keys that cannot resolve — so a
            // surviving plugin sweeps it up later (RegistrationCleanup), and this is what tells it
            // the entry is ours to remove rather than another vendor's.
            appInfo[RegistrationCleanup.OwnerKey] = (String)appInfo["nativePluginName"];

            var appDir = Path.Combine(appsRoot, deviceType, appName);
            var profileDir = Path.Combine(appDir, "Profiles", profileName);
            try
            {
                Directory.CreateDirectory(profileDir);
                File.WriteAllText(
                    Path.Combine(appDir, "ApplicationInfo.json"),
                    appInfo.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                if (iconPath != null)
                {
                    File.Copy(iconPath, Path.Combine(appDir, "ApplicationIcon.png"), overwrite: true);
                }

                ExtractProfile(zip, profileDir);
            }
            catch
            {
                try { Directory.Delete(appDir, recursive: true); } catch { }
                throw;
            }
        }

        private static JsonNode ReadApplicationInfo(
            ZipArchive zip, Boolean windows, String windowsProcessName)
        {
            var appInfoEntry = zip.GetEntry("ApplicationInfo.json")
                ?? throw new InvalidDataException("packaged profile has no ApplicationInfo.json");
            JsonNode appInfo;
            using (var stream = appInfoEntry.Open())
            {
                appInfo = JsonNode.Parse(stream);
            }

            if (windows && (String)appInfo["processOrBundleName"] == "com.apple.Terminal")
            {
                appInfo["processOrBundleName"] = "WindowsTerminal";
                var description = (String)appInfo["description"];
                appInfo["description"] = String.IsNullOrEmpty(description)
                    ? "Controls for Windows Terminal."
                    : description.Replace("Terminal.app", "Windows Terminal");
            }
            else if (windows && !String.IsNullOrWhiteSpace(windowsProcessName))
            {
                appInfo["processOrBundleName"] = windowsProcessName;
            }

            return appInfo;
        }

        private static void ExtractProfile(ZipArchive zip, String profileDir)
        {
            var profileRoot = Path.GetFullPath(profileDir) + Path.DirectorySeparatorChar;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName == "ApplicationInfo.json" || entry.Name.Length == 0)
                {
                    continue;
                }

                var target = Path.GetFullPath(Path.Combine(profileDir, entry.FullName));
                if (!target.StartsWith(profileRoot, StringComparison.Ordinal))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                entry.ExtractToFile(target, overwrite: true);
            }
        }
    }
}
