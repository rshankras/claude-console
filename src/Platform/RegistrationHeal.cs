namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// Heals the reinstall registration desync (macOS and Windows — confirmed identical on both,
    /// 2026-08-08).
    ///
    /// Uninstalling removes the application registration from the running service's MEMORY but
    /// keeps the @_claudeconsole directory on disk; ANY reinstall — explicit uninstall first or
    /// installing straight over — runs that uninstall step internally, and the install step then
    /// sees the on-disk directory and silently skips re-registering. Result: the Claude Console
    /// icon vanishes from Options+ and the keypad drops to the default profile, while every file
    /// on disk is correct and the plugin loads fine. Nothing in the package can prevent it: the
    /// service consults neither the packaged profile nor ApplicationInfo on a reinstall (proven
    /// 2026-08-08 by four package-shape variations, none of which the installer ever read).
    ///
    /// The service rebuilds its application list from the disk scan at startup, so the fix is a
    /// one-shot service restart right after an install-event load. Detection is three signals:
    ///   1. install-event load — the service has been up for minutes, the plugin payload was
    ///      written seconds ago (a cold start fails this: the service is seconds old);
    ///   2. stale registration — ApplicationInfo.json predates the payload. A CLEAN install
    ///      writes the registration fresh, after extracting the payload, so it never triggers;
    ///   3. a marker keyed to the payload write time, so one payload heals at most once.
    /// The cold-start gate alone makes a restart loop impossible; the marker is belt and braces.
    /// </summary>
    internal static class RegistrationHeal
    {
        /// <summary>
        /// The decision, kept pure so tests exercise the real logic on values — fixtures that
        /// hand the wiring pre-cooked data would prove nothing (see the Windows fixture trap).
        ///
        /// The install-event signal is "the payload was written AFTER this service process
        /// started": a real (re)install always happens inside a running service session, while
        /// the reload that follows our own restart always sees a payload OLDER than its fresh
        /// service start — so a loop is impossible by construction, and back-to-back reinstalls
        /// each heal (an earlier uptime-based gate suppressed a reinstall that came within
        /// minutes of the previous heal's restart). Dev builds are covered the same way: the
        /// PostBuild link reload restarts the service, putting the build before the start.
        /// </summary>
        internal static Boolean ShouldHeal(
            DateTime serviceStartUtc,
            DateTime payloadWrittenUtc,
            DateTime? registrationWrittenUtc,
            Boolean alreadyHealedThisPayload)
        {
            if (alreadyHealedThisPayload)
            {
                return false;
            }

            // Payload predates this service session: not an install-event load. This is every
            // cold start (including the one our own killall causes) and every plugin re-enable.
            if (payloadWrittenUtc <= serviceStartUtc)
            {
                return false;
            }

            // No registration on disk: a genuinely clean install, mid-registration — the service
            // is creating the entry right now and the live list will have it. Nothing to heal.
            if (registrationWrittenUtc == null)
            {
                return false;
            }

            // Registration written at-or-after the payload means THIS install refreshed it (the
            // clean-install path); older means the install skipped it — the desync.
            return registrationWrittenUtc.Value < payloadWrittenUtc;
        }

        /// <summary>
        /// Detect the desync and schedule the one-shot service restart. Safe to call on every
        /// load; never throws. macOS only — the Windows service's reinstall behaviour is
        /// unverified, and the restart command is platform-specific.
        /// </summary>
        internal static void HealIfNeeded()
        {
            try
            {
                if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
                {
                    return;
                }

                var pluginDir = PluginPaths.PluginDirectory;         // .../Plugins/ClaudeConsole/bin
                var payloadRoot = String.IsNullOrEmpty(pluginDir) ? null : Path.GetDirectoryName(pluginDir);
                if (String.IsNullOrEmpty(payloadRoot) || !Directory.Exists(payloadRoot))
                {
                    return;
                }

                var payloadWritten = Directory.GetLastWriteTimeUtc(payloadRoot);
                var registrationWritten = NewestRegistrationWriteUtc();
                var marker = MarkerPath();
                var markerValue = payloadWritten.Ticks.ToString();
                var alreadyHealed = File.Exists(marker) && File.ReadAllText(marker).Trim() == markerValue;

                var serviceStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
                if (!ShouldHeal(serviceStart, payloadWritten, registrationWritten, alreadyHealed))
                {
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(marker));
                File.WriteAllText(marker, markerValue);

                PluginLog.Info(
                    "RegistrationHeal: reinstall detected (registration predates the installed payload) — " +
                    "restarting Logi Plugin Service in 10s so it re-adopts the application registration");

                // Detached: children survive the kill that takes down our host process (neither
                // killall nor taskkill-without-/T touches child processes). The sequence mirrors
                // scripts/repair-registration.sh, proven by hand repeatedly. 10s before the kill
                // lets Options+ finish its install flow and settle, so the restart reads as a
                // blink rather than an install error.
                Process.Start(OperatingSystem.IsWindows() ? WindowsRestart() : MacRestart());
            }
            catch (Exception ex)
            {
                try { PluginLog.Warning($"RegistrationHeal: skipped ({ex.Message})"); } catch { }
            }
        }

        /// <summary>
        /// macOS restart: launchd respawns both the service and the Options+ agent, but the agent
        /// comes back WINDOWLESS (--launchd), so the final `open` brings the window back for the
        /// user, who was in it installing when we took it down.
        /// </summary>
        internal static ProcessStartInfo MacRestart() => new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = "-c \"sleep 10; /usr/bin/killall LogiPluginService; sleep 6; " +
                        "/usr/bin/killall logioptionsplus_agent 2>/dev/null; sleep 5; " +
                        "/usr/bin/open '/Library/Application Support/Logitech.localized/LogiOptionsPlus/logioptionsplus_agent.app' 2>/dev/null; exit 0\"",
            UseShellExecute = false,
        };

        /// <summary>
        /// Windows restart: LogiPluginService.exe is a PLAIN PROCESS, not an SCM service (verified
        /// 2026-08-08 — only the LampArray service and the Options+ updater are SCM-managed), and
        /// NOTHING respawns it after a kill, so the script must relaunch it explicitly. Our own
        /// process IS the service, so its exe path comes from MainModule rather than a hardcoded
        /// install location. The Options+ window is then bounced the same way as on macOS.
        /// Written to a .cmd file because nesting `start "" "path"` quoting inside a cmd.exe /c
        /// argument string is an error factory.
        /// </summary>
        internal static ProcessStartInfo WindowsRestart()
        {
            var serviceExe = Process.GetCurrentProcess().MainModule?.FileName
                             ?? @"C:\Program Files\Logi\LogiPluginService\LogiPluginService.exe";
            var script = Path.Combine(IpcPaths.Root, "registration-heal.cmd");
            Directory.CreateDirectory(Path.GetDirectoryName(script));
            File.WriteAllText(script,
                "@echo off\r\n" +
                "timeout /t 10 /nobreak >nul\r\n" +
                "taskkill /F /IM LogiPluginService.exe >nul 2>&1\r\n" +
                "timeout /t 3 /nobreak >nul\r\n" +
                $"start \"\" \"{serviceExe}\"\r\n" +
                "timeout /t 8 /nobreak >nul\r\n" +
                "taskkill /F /IM logioptionsplus.exe >nul 2>&1\r\n" +
                "timeout /t 3 /nobreak >nul\r\n" +
                "if exist \"%ProgramFiles%\\LogiOptionsPlus\\logioptionsplus.exe\" start \"\" \"%ProgramFiles%\\LogiOptionsPlus\\logioptionsplus.exe\"\r\n");
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }

        /// <summary>The service's per-user Applications directory (registration dirs live under it).</summary>
        internal static String ApplicationsRoot() =>
            OperatingSystem.IsWindows()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Logi", "LogiPluginService", "Applications")
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "Logi", "LogiPluginService", "Applications");

        /// <summary>Newest ApplicationInfo.json across device types, or null when unregistered.</summary>
        private static DateTime? NewestRegistrationWriteUtc()
        {
            var appsRoot = ApplicationsRoot();
            if (!Directory.Exists(appsRoot))
            {
                return null;
            }

            DateTime? newest = null;
            foreach (var deviceDir in Directory.GetDirectories(appsRoot))
            {
                var info = Path.Combine(deviceDir, "@_claudeconsole", "ApplicationInfo.json");
                if (File.Exists(info))
                {
                    var t = File.GetLastWriteTimeUtc(info);
                    if (newest == null || t > newest.Value)
                    {
                        newest = t;
                    }
                }
            }

            return newest;
        }

        private static String MarkerPath() =>
            Path.Combine(IpcPaths.Root, "registration-heal-marker");
    }
}
