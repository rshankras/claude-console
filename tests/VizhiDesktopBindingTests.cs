namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Reflection;

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
        public void MacOS_names_the_real_process_and_windows_deliberately_names_none()
        {
            var name = Invoke("GetProcessName");

            if (OperatingSystem.IsWindows())
            {
                // Not a guess, not a placeholder: empty until recon W0 finds the real identity.
                Assert.Equal("", name);
            }
            else
            {
                Assert.Equal("ChatGPT", name);
                Assert.DoesNotContain(".exe", name, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void The_install_probe_answers_honestly_from_disk()
        {
            var app = new VizhiDesktopApplication();
            var status = app.GetApplicationStatus();

            if (OperatingSystem.IsMacOS() && Directory.Exists("/Applications/ChatGPT.app"))
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
    }
}
