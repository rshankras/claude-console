namespace Loupedeck.ClaudeConsolePlugin.Platform
{
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
        internal static Boolean RegisterIfMissing()
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

                var appsRoot = RegistrationHeal.ApplicationsRoot();
                if (RegistrationExists(appsRoot))
                {
                    return false;
                }

                var icon = Path.Combine(payloadRoot, "metadata", "Icon256x256.png");
                CreateRegistration(lp5, File.Exists(icon) ? icon : null, appsRoot, OperatingSystem.IsWindows());

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

        /// <summary>True when any device type already has an @_claudeconsole registration.</summary>
        internal static Boolean RegistrationExists(String appsRoot)
        {
            if (String.IsNullOrEmpty(appsRoot) || !Directory.Exists(appsRoot))
            {
                return false;
            }

            foreach (var deviceDir in Directory.GetDirectories(appsRoot))
            {
                if (File.Exists(Path.Combine(deviceDir, "@_claudeconsole", "ApplicationInfo.json")))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Write the registration directory from the packaged profile: the lp5's own
        /// ApplicationInfo.json at the top (patched for Windows), the icon beside it, and the
        /// profile content under Profiles/&lt;defaultProfileName&gt;/ — the exact layout of a
        /// working registration. Throws on any failure after removing the partial directory, so
        /// a later load retries from scratch rather than the service adopting half an entry.
        /// </summary>
        internal static void CreateRegistration(String lp5Path, String iconPath, String appsRoot, Boolean windows)
        {
            using var zip = ZipFile.OpenRead(lp5Path);

            var appInfoEntry = zip.GetEntry("ApplicationInfo.json")
                ?? throw new InvalidDataException("packaged profile has no ApplicationInfo.json");
            JsonNode appInfo;
            using (var stream = appInfoEntry.Open())
            {
                appInfo = JsonNode.Parse(stream);
            }

            var deviceType = (String)appInfo["deviceType"] ?? "Loupedeck70";
            var profileName = (String)appInfo["defaultProfileName"]
                ?? throw new InvalidDataException("packaged ApplicationInfo has no defaultProfileName");

            if (windows)
            {
                // The document in the package is authored for macOS; Windows binds the same
                // layout to Windows Terminal (the shipped platform default).
                appInfo["processOrBundleName"] = "WindowsTerminal";
                appInfo["description"] = "Claude Code controls for Windows Terminal.";
            }

            var appDir = Path.Combine(appsRoot, deviceType, "@_claudeconsole");
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

                var profileRoot = Path.GetFullPath(profileDir) + Path.DirectorySeparatorChar;
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName == "ApplicationInfo.json" || entry.Name.Length == 0)
                    {
                        continue;                                    // app-level document / directory entry
                    }

                    var target = Path.GetFullPath(Path.Combine(profileDir, entry.FullName));
                    if (!target.StartsWith(profileRoot, StringComparison.Ordinal))
                    {
                        continue;                                    // zip-slip guard
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
            catch
            {
                try { Directory.Delete(appDir, recursive: true); } catch { }
                throw;
            }
        }
    }
}
