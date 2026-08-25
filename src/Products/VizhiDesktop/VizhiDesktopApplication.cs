namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.IO;

    /// <summary>
    /// Associates this plugin with the OpenAI desktop app, making it an APPLICATION plugin
    /// (yaml capability HasApplication) so the package ships an auto-imported default profile.
    ///
    /// Unlike the terminal products this binds a bundle NOTHING ELSE CLAIMS — no activation
    /// collision with Claude Console or Vizhi for Codex; all three can coexist on one keypad.
    ///
    /// The names must be REAL on this platform (the 1.8.0-1.8.4 lesson: one wrong process name
    /// and the service silently registers no application at all). Windows identity is unknown
    /// until the W0 recon — this product is macOS-only until then, and the Windows name below
    /// says so honestly rather than guessing one.
    /// </summary>
    public class VizhiDesktopApplication : ClientApplication
    {
        public VizhiDesktopApplication()
        {
        }

        protected override String GetProcessName() =>
            OperatingSystem.IsWindows() ? "" : "ChatGPT";   // Windows: unknown (W0) — empty, never a guess

        protected override String GetBundleName() => "com.openai.codex";

        // Terminal ships with the OS; this app does not. Probe honestly — "Unknown" over an
        // absent app is exactly the kind of lie the engine forbids elsewhere.
        public override ClientApplicationStatus GetApplicationStatus() =>
            OperatingSystem.IsMacOS() && Directory.Exists("/Applications/ChatGPT.app")
                ? ClientApplicationStatus.Installed
                : ClientApplicationStatus.NotInstalled;
    }
}
