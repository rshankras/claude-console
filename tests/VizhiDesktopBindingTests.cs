namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    using Xunit;

    /// <summary>
    /// The desktop product's application binding — same silent-failure surface as the terminal
    /// pair's (ApplicationBindingTests), different app, plus the two things that are new here:
    /// the bundle is one NOTHING ELSE in the family claims (that non-collision is the product's
    /// coexistence story), and the Windows identity is deliberately empty until the W0 recon
    /// names the real app — an empty name registers nothing, a guessed name registers the wrong
    /// thing invisibly (the 1.8.0 lesson).
    /// </summary>
    public class VizhiDesktopBindingTests
    {
        private static String Invoke(String method)
        {
            var app = new VizhiDesktopApplication();
            var m = typeof(VizhiDesktopApplication)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(m);
            return (String)m.Invoke(app, null);
        }

        [Fact]
        public void The_plugin_declares_its_foreground_application()
        {
            // The production constructor selects the desktop IPC namespace globally. Bypass it:
            // these overrides are constants, and this test must not leak product state into the
            // rest of the parallel suite.
            var plugin = (VizhiDesktopPlugin)RuntimeHelpers.GetUninitializedObject(
                typeof(VizhiDesktopPlugin));

            Assert.True(plugin.UsesApplicationApiOnly);
            Assert.False(plugin.HasNoApplication);
        }

        [Fact]
        public void The_bundle_is_the_real_desktop_app()
        {
            Assert.Equal("com.openai.codex", Invoke("GetBundleName"));
        }

        [Fact]
        public void The_bundle_collides_with_no_other_product()
        {
            // Activation is exclusive per bundle. The terminal pair collide over
            // com.apple.Terminal; this product must never join that fight.
            Assert.NotEqual("com.apple.Terminal", Invoke("GetBundleName"));
        }

        [Fact]
        public void Both_platforms_name_the_real_process()
        {
            // The Mac bundle's process and the Windows Store package's ChatGPT.exe were both
            // read off the installed app, and agree with the adapter the helper is aimed at.
            var name = Invoke("GetProcessName");
            Assert.Equal("ChatGPT", name);
            Assert.DoesNotContain(".exe", name, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(name, Assert.Single(new Desktop.OpenAiDesktopAdapter().WindowsProcessNames));
        }

        [Fact]
        public void The_install_probe_answers_honestly_from_the_machine()
        {
            var app = new VizhiDesktopApplication();
            var status = app.GetApplicationStatus();

            if ((OperatingSystem.IsMacOS() && Directory.Exists("/Applications/ChatGPT.app"))
                || (OperatingSystem.IsWindows() && VizhiDesktopApplication.WindowsPackageInstalled()))
            {
                Assert.Equal(ClientApplicationStatus.Installed, status);
            }
            else
            {
                // Unlike Terminal, this app may genuinely be absent — and unlike the terminal
                // products, "Unknown" is not an honest answer for an installable app.
                Assert.Equal(ClientApplicationStatus.NotInstalled, status);
            }
        }

        [Fact]
        public void The_windows_probe_looks_for_the_store_package_family()
        {
            // Package folders sit under WindowsApps, which a plain process may not list; the
            // per-user package repository is the readable record. The family name is the
            // identity, not the version in between.
            var family = Desktop.OpenAiDesktopAdapter.WindowsPackageFamily;
            Assert.Equal("OpenAI.Codex_2p2nqsd0c76g0", family);
            if (!OperatingSystem.IsWindows())
            {
                Assert.False(VizhiDesktopApplication.WindowsPackageInstalled());
            }
        }
    }
}
