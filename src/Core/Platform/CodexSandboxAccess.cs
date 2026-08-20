namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Grants codex's Windows sandbox users the access they need to reach this plugin — laid down
    /// now so that the day OpenAI fixes their hook runner, hooks work without a plugin update.
    ///
    /// WHY THIS IS NEEDED AT ALL. Codex on Windows runs hooks as dedicated low-privilege sandbox
    /// users (the CodexSandboxUsers group its setup creates). Its setup stamps an inheritable
    /// read+execute ACE across %LOCALAPPDATA% — but the Logi installer DISABLES INHERITANCE on its
    /// own directory, so that grant never reaches the plugin. Spawning our hook executable inside
    /// the sandbox then fails with CreateProcessAsUserW: 5 (Access is denied). Verified on
    /// hardware 2026-08-20 (docs/spike-windows-codex-hooks.md, Layer 2), where granting it by hand
    /// made `codex sandbox -- <hook exe>` run correctly.
    ///
    /// The second grant is the one nobody would have predicted: even a hook that spawns cannot
    /// WRITE its state, because the sandbox user has no access to our IPC root under %TEMP%. A
    /// hook that runs and silently drops every state file looks exactly like a hook that never
    /// ran — the kind of thing that costs another week to diagnose.
    ///
    /// BEST EFFORT BY DESIGN. The group exists only after codex's sandbox setup has run, which may
    /// be never. A failure here is logged and forgotten: it costs a feature that is already
    /// unavailable, and must never cost the plugin its load.
    /// </summary>
    internal static class CodexSandboxAccess
    {
        private const String SandboxGroup = "CodexSandboxUsers";

        /// <summary>
        /// Grant read+execute on the plugin's install directory and write on its IPC root. Returns
        /// true when every grant that was attempted succeeded — for tests and logging only.
        /// </summary>
        internal static Boolean EnsureGranted()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            var ok = true;

            // Read+execute so the sandbox user can LAUNCH the hook exe. (OI)(CI) makes it
            // inheritable, which is the whole point — the Logi installer's broken inheritance is
            // what blocks codex's own grant from arriving.
            var logiRoot = LogiRoot();
            if (logiRoot != null)
            {
                ok &= Grant(logiRoot, "RX");
            }

            // Write so a spawned hook can actually deposit state. Without this a working hook
            // would light nothing, which is indistinguishable from a hook that never ran.
            ok &= Grant(IpcPaths.Root, "M");

            return ok;
        }

        /// <summary>The Logi plugins directory this plugin was installed into.</summary>
        private static String LogiRoot()
        {
            try
            {
                var plugins = PluginPaths.PluginsRoot;
                return String.IsNullOrEmpty(plugins) ? null : Directory.GetParent(plugins)?.FullName;
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, "CodexSandboxAccess: cannot resolve the Logi root");
                return null;
            }
        }

        internal static Boolean Grant(String path, String rights)
        {
            if (String.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(path);

                // icacls rather than a managed ACL edit: the plugin runs inside the Logi service,
                // and a bad DirectorySecurity write there is far more dangerous than a subprocess
                // that fails. Bounded like every other subprocess in this codebase.
                //
                // NO /T. An (OI)(CI) ACE set on the root propagates to the whole subtree through
                // NTFS auto-inheritance; /T additionally REWRITES every descendant's ACL, which
                // took ~10s across the Logi tree on hardware — the 1.5.0 install failure: Load()
                // has a 10s budget and this call sat on it. The no-/T form was what the spike ran
                // by hand, and it made the hook exe launchable several levels down (Layer 2).
                var exit = BoundedProcess.RunForExitCode(
                    "icacls",
                    new List<String> { path, "/grant", $"{SandboxGroup}:(OI)(CI){rights}", "/C", "/Q" },
                    15000);

                if (exit == 0)
                {
                    PluginLog.Info($"CodexSandboxAccess: granted {SandboxGroup} {rights} on {path}");
                    return true;
                }

                // Exit 1332 (no such account) is the ordinary case on a machine where codex's
                // sandbox setup has never run — not a problem, just nothing to grant to.
                PluginLog.Verbose($"CodexSandboxAccess: icacls exit {exit} for {path} — {SandboxGroup} may not exist yet");
                return false;
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, $"CodexSandboxAccess: cannot grant on {path}");
                return false;
            }
        }
    }
}
