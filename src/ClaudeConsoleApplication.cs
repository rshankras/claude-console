namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    /// <summary>
    /// Associates the plugin with Terminal.app, making this an APPLICATION plugin (yaml capability
    /// HasApplication). That association is what lets the package ship an auto-imported default
    /// profile (package/profiles/DefaultProfile70.lp5) — the mechanism Vizhi uses, so a fresh
    /// install gets a working keypad layout with no manual .lp5 import.
    ///
    /// These names must be REAL: the 1.5-era crash that disabled the plugin happened when
    /// HasApplication was enabled while this class still returned empty strings — an application
    /// with no identity. Terminal.app is the right binding on macOS: every typing key targets
    /// Claude's Terminal tab by TTY, and the hand-imported profile was always Terminal-bound.
    ///
    /// On Windows the equivalent host is Windows Terminal, and GetProcessName is what binds the
    /// profile there — but this class runs on BOTH platforms, so the name must be chosen at
    /// runtime. 1.8.0-1.8.4 hardcoded "WindowsTerminal", which named a process that does not
    /// exist on macOS: the service then registered no application at all, so a fresh macOS
    /// install created no "Claude Console" entry and never imported the layout. Nothing failed
    /// loudly — the plugin loaded, its actions appeared, and only the application row was empty.
    /// Existing installs were unaffected (their registration was already on disk), which is why
    /// it took a clean install to surface.
    /// </summary>
    public class ClaudeConsoleApplication : ClientApplication
    {
        public ClaudeConsoleApplication()
        {
        }

        // The host terminal's process name ON THIS PLATFORM. Never a constant: a name belonging to
        // the other OS reads as an application that isn't installed, and the registration is
        // silently skipped. (No ".exe" — the SDK matches on the bare process name.)
        protected override String GetProcessName() =>
            OperatingSystem.IsWindows() ? "WindowsTerminal" : "Terminal";

        // macOS bundle id of the associated application.
        protected override String GetBundleName() => "com.apple.Terminal";

        // Terminal ships with macOS, so "not installed" isn't a state worth probing for.
        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}
