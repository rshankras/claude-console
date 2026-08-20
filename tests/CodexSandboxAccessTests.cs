namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// The grants that let codex's Windows sandbox reach this plugin — laid down before they can
    /// be used, so the day OpenAI fixes their hook runner nothing else has to change.
    ///
    /// Both grants come from hardware findings (docs/spike-windows-codex-hooks.md): the Logi
    /// installer disables ACL inheritance, so codex's own %LOCALAPPDATA% grant never arrives and
    /// launching the hook fails with access denied; and the sandbox user cannot write our IPC
    /// root, so even a hook that DID spawn would silently drop every state file — a failure
    /// indistinguishable from one that never ran.
    /// </summary>
    public class CodexSandboxAccessTests
    {
        /// <summary>
        /// On any non-Windows host this is a no-op that reports so — the sandbox, the group and
        /// icacls are all Windows-only, and pretending otherwise would mean shelling out to a
        /// program that isn't there on every macOS load.
        /// </summary>
        [Fact]
        public void It_does_nothing_off_windows()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            Assert.False(CodexSandboxAccess.EnsureGranted());
        }

        /// <summary>
        /// A machine where codex's sandbox setup never ran has no such group, and icacls says so.
        /// That must be an ordinary "nothing to do", never an exception into the plugin's load
        /// path: the feature it enables is already unavailable, and the plugin still has keys to
        /// draw.
        /// </summary>
        [Fact]
        public void A_missing_group_or_path_is_survivable()
        {
            var missing = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "cx-acl-" + Guid.NewGuid().ToString("N"));

            var ex = Record.Exception(() => CodexSandboxAccess.Grant(missing, "RX"));

            Assert.Null(ex);

            try { System.IO.Directory.Delete(missing, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public void An_empty_path_grants_nothing()
        {
            Assert.False(CodexSandboxAccess.Grant(null, "RX"));
            Assert.False(CodexSandboxAccess.Grant("", "RX"));
        }
    }
}
