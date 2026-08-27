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
                if (RegistrationExists(appsRoot, appName))
                {
                    return false;
                }

                var icon = Path.Combine(payloadRoot, "metadata", "Icon256x256.png");
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

            if (windows && (String)appInfo["processOrBundleName"] == "com.apple.Terminal")
            {
                // The document in the package is authored for macOS; Windows binds the same
                // layout to Windows Terminal (the shipped platform default). The description is
                // rewritten rather than replaced so it keeps whatever the product called itself.
                //
                // TERMINAL products only — hence the guard on the packaged value. A product bound
                // to a desktop app's bundle (Vizhi Desktop: com.openai.codex) must pass through
                // untouched: rewriting it to WindowsTerminal here would silently rebind the whole
                // registration to an app the product does not drive, the same class of quiet
                // wrong-name failure as the 1.8.0 hardcoded "WindowsTerminal". Such products ship
                // their real Windows identity in the package once it is known (recon W0).
                appInfo["processOrBundleName"] = "WindowsTerminal";
                var description = (String)appInfo["description"];
                appInfo["description"] = String.IsNullOrEmpty(description)
                    ? "Controls for Windows Terminal."
                    : description.Replace("Terminal.app", "Windows Terminal");
            }
            else if (windows && !String.IsNullOrWhiteSpace(windowsProcessName))
            {
                // Desktop products package their macOS bundle id in the shared profile. Once W0
                // has proven the real Windows executable identity, write that identity into the
                // Windows registration instead of silently binding com.openai.codex as a process.
                appInfo["processOrBundleName"] = windowsProcessName;
            }

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
