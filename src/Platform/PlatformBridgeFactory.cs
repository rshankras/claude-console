namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Picks the backend for the machine we're running on. Selection is at RUNTIME, not compile
    /// time: one plugin assembly serves both operating systems, and only the helper executables
    /// shipped alongside it are per-platform.
    /// </summary>
    internal static class PlatformBridgeFactory
    {
        public static IPlatformBridge Create()
        {
            if (OperatingSystem.IsMacOS())
            {
                return new MacPlatformBridge();
            }

            if (OperatingSystem.IsWindows())
            {
                // Phase 1: discovery only — the grid populates, but injection is still a logged
                // no-op (IsSupported stays false until Phase 2). See docs/windows-port-2.0-plan.md.
                return new WindowsPlatformBridge();
            }

            // Deliberately a working object rather than null: every key press then degrades to a
            // logged no-op instead of throwing inside an SDK callback.
            return new UnsupportedPlatformBridge();
        }
    }

    /// <summary>
    /// The "this OS has no backend yet" bridge. Reports no sessions and performs no injection,
    /// which is exactly what the pre-seam code did on non-macOS — it just says so in one place
    /// now instead of at fifteen call sites.
    /// </summary>
    internal sealed class UnsupportedPlatformBridge : IPlatformBridge
    {
        public String Name => "unsupported";

        public Boolean IsSupported => false;

        // null, not an empty set: "I don't know" — an empty set would tell the registry to reap
        // every live session (see IPlatformBridge.DiscoverSessions).
        public HashSet<String> DiscoverSessions() => null;

        public String QueryFrontmostSession() => null;

        public InjectionOutcome InjectText(String sessionKey, String text, Boolean pressEnter) => this.Unsupported(nameof(this.InjectText));

        public InjectionOutcome InjectKey(String sessionKey, KeyStroke key) => this.Unsupported(nameof(this.InjectKey));

        public InjectionOutcome InjectTabThenEnter(String sessionKey) => this.Unsupported(nameof(this.InjectTabThenEnter));

        public void FocusSession(String sessionKey) => this.Unsupported(nameof(this.FocusSession));

        public void Navigate(TerminalAction action) => this.Unsupported(nameof(this.Navigate));

        public void LaunchClaudeInProject(String projectDir) => this.Unsupported(nameof(this.LaunchClaudeInProject));

        public void Alert() { /* no backend to beep with */ }

        private InjectionOutcome Unsupported(String what)
        {
            PluginLog.Info($"UnsupportedPlatformBridge: {what} — no backend for this OS yet");
            return InjectionOutcome.Unsupported;
        }
    }
}
