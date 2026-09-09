namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    using Xunit;

    /// <summary>
    /// The hooks outlive the plugin (#73): Options+ deletes the package folder and the wiring in
    /// settings.json keeps running. The plugin's answer is to tell the hooks, on every load, which
    /// place on disk means "still installed" — and that place differs by how it was installed. A
    /// package is keyed on its folder under the service's Plugins root, the one Options+ removes; a
    /// dev build on its <c>.link</c> file there, since the build output outlives many links; and
    /// anything else on the DLL's own directory. These pin that resolution so a wrong key can never
    /// make a live plugin look uninstalled — which would unwire live status out from under it.
    /// </summary>
    public class PluginHomeTests : IDisposable
    {
        private readonly String _root = Path.Combine(Path.GetTempPath(), "cc-plugins-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        [Fact]
        public void A_package_is_keyed_on_its_folder_under_the_plugins_root()
        {
            var asm = Path.Combine(_root, "ClaudeConsole", "bin", "mac", "ClaudeConsolePlugin.dll");

            Assert.Equal(Path.Combine(_root, "ClaudeConsole"), BridgeManager.InstalledPluginHome(asm, _root));
        }

        [Fact]
        public void A_dev_build_is_keyed_on_its_link_file()
        {
            var build = Path.Combine(_root, "..", "cc-build-" + Guid.NewGuid().ToString("N"), "Debug");
            Directory.CreateDirectory(_root);
            var link = Path.Combine(_root, "ClaudeConsolePlugin.link");
            // Exactly what the csproj's PostBuild `echo` leaves behind: the path, a separator, a newline.
            File.WriteAllText(link, build + "/ \n");
            var asm = Path.Combine(build, "ClaudeConsolePlugin.dll");

            Assert.Equal(link, BridgeManager.InstalledPluginHome(asm, _root));
        }

        [Fact]
        public void Anything_else_is_keyed_on_the_dll_directory()
        {
            var elsewhere = Path.Combine(Path.GetTempPath(), "cc-elsewhere-" + Guid.NewGuid().ToString("N"));
            var asm = Path.Combine(elsewhere, "ClaudeConsolePlugin.dll");

            Assert.Equal(elsewhere, BridgeManager.InstalledPluginHome(asm, _root));
            Assert.Equal(elsewhere, BridgeManager.InstalledPluginHome(asm, null));
        }

        [Fact]
        public void No_path_means_no_key()
        {
            Assert.Null(BridgeManager.InstalledPluginHome(null, _root));
            Assert.Null(BridgeManager.InstalledPluginHome("", _root));
        }
    }
}
