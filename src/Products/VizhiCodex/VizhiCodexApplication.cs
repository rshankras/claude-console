namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    /// <summary>
    /// Associates this plugin with the terminal Codex runs in, making it an APPLICATION plugin
    /// (yaml capability HasApplication) so the package can ship an auto-imported default profile.
    ///
    /// The names must be REAL and must name THIS platform's terminal. Claude Console shipped
    /// 1.8.0-1.8.4 with a hardcoded "WindowsTerminal", which on macOS named a process that does not
    /// exist: the service then registered no application at all, so a fresh install created no
    /// entry and never imported the layout — and nothing failed loudly. The plugin loaded, its
    /// actions appeared, and only the application row was empty. Choose at runtime, always.
    ///
    /// Same binding as Claude Console, because it is the same terminal — which is precisely why
    /// the coexistence question (which profile activates when Terminal comes forward with both
    /// installed) has to be answered on hardware before both listings go live.
    /// </summary>
    public class VizhiCodexApplication : ClientApplication
    {
        public VizhiCodexApplication()
        {
        }

        // The host terminal's process name ON THIS PLATFORM. Never a constant.
        protected override String GetProcessName() =>
            OperatingSystem.IsWindows() ? "WindowsTerminal" : "Terminal";

        // macOS bundle id of the associated application.
        protected override String GetBundleName() => "com.apple.Terminal";

        // Terminal ships with macOS, so "not installed" isn't a state worth probing for.
        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}
