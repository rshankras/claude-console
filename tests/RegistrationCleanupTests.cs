namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    using Loupedeck.ClaudeConsolePlugin.Platform;
    using Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration;

    using Xunit;

    /// <summary>
    /// Sweeping up registrations whose plugin is gone.
    ///
    /// Observed on hardware: uninstalling a plugin through Options+ removes the plugin and leaves
    /// its application entry behind. That entry is not inert — it keeps claiming the terminal, wins
    /// activation against a plugin that IS installed, and shows a keypad of unresolvable keys. The
    /// user sees the SURVIVING product apparently broken, with nothing in the plugin list to
    /// explain it.
    ///
    /// Deleting directories under the user's Logi install is the dangerous half, so most of these
    /// assert what must NOT be removed.
    /// </summary>
    public class RegistrationCleanupTests : IDisposable
    {
        private readonly String _root =
            Path.Combine(Path.GetTempPath(), "cc-cleanup-" + Guid.NewGuid().ToString("N"));

        private String Apps => Path.Combine(this._root, "Applications");
        private String Plugins => Path.Combine(this._root, "Plugins");

        public RegistrationCleanupTests()
        {
            Directory.CreateDirectory(Path.Combine(this.Apps, "Loupedeck70"));
            Directory.CreateDirectory(this.Plugins);
        }

        public void Dispose()
        {
            try { Directory.Delete(this._root, recursive: true); } catch { /* best effort */ }
        }

        private String Register(String appName, String plugin, Boolean ours = true, Boolean installed = false)
        {
            var dir = Path.Combine(this.Apps, "Loupedeck70", appName);
            Directory.CreateDirectory(dir);

            var owner = ours ? $"\"{RegistrationCleanup.OwnerKey}\":\"{plugin}\"," : "";
            File.WriteAllText(Path.Combine(dir, "ApplicationInfo.json"),
                $"{{{owner}\"name\":\"{appName}\",\"nativePluginName\":\"{plugin}\"}}");

            if (installed)
            {
                Directory.CreateDirectory(Path.Combine(this.Plugins, plugin));
            }

            return dir;
        }

        [Fact]
        public void An_entry_we_wrote_whose_plugin_is_gone_is_removed()
        {
            var orphan = this.Register("@_codexconsole", "VizhiCodex");

            var removed = RegistrationCleanup.RemoveOrphans(this.Apps, this.Plugins, "ClaudeConsole");

            Assert.Equal(1, removed);
            Assert.False(Directory.Exists(orphan));
        }

        [Fact]
        public void A_dev_linked_plugin_is_installed_too()
        {
            // A development build installs as "<AssemblyName>.link", not a directory. Reading only
            // directories, a packaged product judged a dev-linked product's registration an orphan
            // and deleted it — the dev-linked product then rewrote it and restarted the service to
            // adopt it, which handed the packaged one another boot to delete it again. That loop
            // thrashed Options+ every few seconds on hardware (2026-08-25).
            var live = this.Register("@_vizhidesktop", "VizhiDesktop");
            File.WriteAllText(Path.Combine(this.Plugins, "VizhiDesktopPlugin.link"), "/some/build/tree");

            Assert.Equal(0, RegistrationCleanup.RemoveOrphans(this.Apps, this.Plugins, "VizhiCodex"));
            Assert.True(Directory.Exists(live));
        }

        [Fact]
        public void A_link_for_a_different_plugin_does_not_rescue_an_orphan()
        {
            // The prefix match must not become "any .link file will do" — an orphan next to some
            // other product's dev link is still an orphan.
            var orphan = this.Register("@_vizhidesktop", "VizhiDesktop");
            File.WriteAllText(Path.Combine(this.Plugins, "ClaudeConsolePlugin.link"), "/some/build/tree");

            Assert.Equal(1, RegistrationCleanup.RemoveOrphans(this.Apps, this.Plugins, "VizhiCodex"));
            Assert.False(Directory.Exists(orphan));
        }

        [Fact]
        public void An_entry_whose_plugin_is_still_installed_stays()
        {
            var live = this.Register("@_claudeconsole", "ClaudeConsole", installed: true);

            Assert.Equal(0, RegistrationCleanup.RemoveOrphans(this.Apps, this.Plugins, "VizhiCodex"));
            Assert.True(Directory.Exists(live));
        }

        /// <summary>
        /// The dangerous case. Another vendor's registration can look exactly as orphaned as ours —
        /// their plugin may be installed somewhere we don't know to look, or simply not be our
        /// business. Without our stamp, we do not touch it.
        /// </summary>
        [Fact]
        public void An_entry_we_did_not_write_is_never_removed()
        {
            var theirs = this.Register("@_figma-ebc79978bc7445fc", "Figma", ours: false);

            Assert.Equal(0, RegistrationCleanup.RemoveOrphans(this.Apps, this.Plugins, "ClaudeConsole"));
            Assert.True(Directory.Exists(theirs));
        }

        /// <summary>
        /// A transient failure to see our own plugin directory must never delete the entry we are
        /// running on — that would uninstall the working product mid-session.
        /// </summary>
        [Fact]
        public void The_running_plugins_own_entry_is_never_removed()
        {
            var mine = this.Register("@_claudeconsole", "ClaudeConsole");   // deliberately not installed

            Assert.Equal(0, RegistrationCleanup.RemoveOrphans(this.Apps, this.Plugins, "ClaudeConsole"));
            Assert.True(Directory.Exists(mine));
        }

        [Fact]
        public void An_unreadable_entry_is_left_alone()
        {
            var dir = Path.Combine(this.Apps, "Loupedeck70", "@_broken");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "ApplicationInfo.json"), "{ not json");

            Assert.Equal(0, RegistrationCleanup.RemoveOrphans(this.Apps, this.Plugins, "ClaudeConsole"));
            Assert.True(Directory.Exists(dir));
        }

        /// <summary>Runs during plugin load; a missing tree must not take the plugin down.</summary>
        [Fact]
        public void Missing_directories_are_not_an_error()
        {
            var ex = Record.Exception(() =>
                RegistrationCleanup.RemoveOrphans(
                    Path.Combine(this._root, "nope"), this.Plugins, "ClaudeConsole"));

            Assert.Null(ex);
        }

        /// <summary>The exact hardware scenario: Codex uninstalled, Claude Console still installed.</summary>
        [Fact]
        public void The_surviving_product_reclaims_the_terminal()
        {
            var claude = this.Register("@_claudeconsole", "ClaudeConsole", installed: true);
            var codex = this.Register("@_codexconsole", "VizhiCodex");   // plugin uninstalled

            RegistrationCleanup.RemoveOrphans(this.Apps, this.Plugins, "ClaudeConsole");

            Assert.True(Directory.Exists(claude));
            Assert.False(Directory.Exists(codex));
        }
    }
}
