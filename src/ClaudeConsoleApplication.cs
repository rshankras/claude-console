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
    /// with no identity. Terminal.app is the right binding: every typing key targets Claude's
    /// Terminal tab by TTY, and the hand-imported profile was always Terminal-bound anyway.
    /// </summary>
    public class ClaudeConsoleApplication : ClientApplication
    {
        public ClaudeConsoleApplication()
        {
        }

        // Windows process name (unused today — the plugin is macOS-only, but the SDK requires it).
        protected override String GetProcessName() => "Terminal";

        // macOS bundle id of the associated application.
        protected override String GetBundleName() => "com.apple.Terminal";

        // Terminal ships with macOS, so "not installed" isn't a state worth probing for.
        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}
