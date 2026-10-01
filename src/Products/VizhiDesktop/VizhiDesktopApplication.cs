namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.IO;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// Associates this plugin with the OpenAI desktop app, making it an APPLICATION plugin
    /// (yaml capability HasApplication) so the package ships an auto-imported default profile.
    ///
    /// Unlike the terminal products this binds a bundle NOTHING ELSE claims — no activation
    /// collision with Claude Console or Vizhi for Codex; all three can coexist on one keypad.
    ///
    /// The names must be REAL on this platform (the 1.8.0-1.8.4 lesson: one wrong process name
    /// and the service silently registers no application at all). Both were read off the
    /// installed app, never guessed: the Mac bundle in the 2026-08-24 spike, the Windows Store
    /// package and its ChatGPT.exe on 2026-09-30.
    /// </summary>
    public class VizhiDesktopApplication : ClientApplication
    {
        public VizhiDesktopApplication()
        {
        }

        protected override String GetProcessName() => "ChatGPT";

        protected override String GetBundleName() => "com.openai.codex";

        // Terminal ships with the OS; this app does not. Probe honestly — "Unknown" over an
        // absent app is exactly the kind of lie the engine forbids elsewhere.
        public override ClientApplicationStatus GetApplicationStatus() =>
            (OperatingSystem.IsMacOS() && Directory.Exists("/Applications/ChatGPT.app"))
            || (OperatingSystem.IsWindows() && WindowsPackageInstalled())
                ? ClientApplicationStatus.Installed
                : ClientApplicationStatus.NotInstalled;

        /// <summary>
        /// A Store package is recorded per user under the AppModel package repository; the
        /// package folder itself lives under WindowsApps, which a plain process may not list.
        /// </summary>
        internal static Boolean WindowsPackageInstalled()
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var packages = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
                if (packages == null) return false;
                var family = OpenAiDesktopAdapter.WindowsPackageFamily;
                var name = family[..family.IndexOf('_')] + "_";
                var publisher = family[family.IndexOf('_')..];
                foreach (var package in packages.GetSubKeyNames())
                {
                    if (package.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                        && package.EndsWith(publisher, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Registry access denied or missing hive: not installed as far as we can tell.
            }
            return false;
        }
    }
}
