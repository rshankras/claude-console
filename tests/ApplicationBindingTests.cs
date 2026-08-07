namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Reflection;

    using Xunit;

    /// <summary>
    /// The application binding — what makes this an APPLICATION plugin, and therefore what lets the
    /// package auto-import its keypad layout.
    ///
    /// This is a silent-failure surface. If the binding names something that doesn't exist on the
    /// running OS, the service registers no application: the plugin still loads, its actions still
    /// appear in Options+, and only the application row is quietly empty with nothing in any log.
    /// It also only shows up on a CLEAN install, because an existing registration stays on disk
    /// across upgrades — which is exactly how 1.8.0-1.8.4 shipped a macOS regression unnoticed.
    /// </summary>
    public class ApplicationBindingTests
    {
        // The accessors are protected; the SDK calls them. Reflection is the honest way to assert
        // on them without changing production visibility for a test's convenience.
        private static String Invoke(String method)
        {
            var app = new ClaudeConsoleApplication();
            var m = typeof(ClaudeConsoleApplication)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(m);
            return (String)m.Invoke(app, null);
        }

        [Fact]
        public void The_process_name_matches_the_platform_we_are_running_on()
        {
            // A name from the OTHER platform reads as "that application isn't installed", and the
            // registration is skipped — no Claude Console entry, no auto-imported profile.
            var expected = OperatingSystem.IsWindows() ? "WindowsTerminal" : "Terminal";

            Assert.Equal(expected, Invoke("GetProcessName"));
        }

        [Fact]
        public void The_process_name_is_not_hardcoded_to_one_platform()
        {
            // The regression in one line: this class runs on both operating systems, so a constant
            // is wrong on one of them by construction.
            var name = Invoke("GetProcessName");

            if (!OperatingSystem.IsWindows())
            {
                Assert.NotEqual("WindowsTerminal", name);
            }
            else
            {
                Assert.NotEqual("Terminal", name);
            }
        }

        [Fact]
        public void The_bundle_id_is_the_real_terminal_app()
        {
            // macOS binds by bundle id. Empty or wrong names here caused the 1.5-era crash that
            // disabled the plugin outright — an application with no identity.
            Assert.Equal("com.apple.Terminal", Invoke("GetBundleName"));
        }

        [Fact]
        public void Neither_binding_is_empty()
        {
            foreach (var m in new[] { "GetProcessName", "GetBundleName" })
            {
                Assert.False(String.IsNullOrWhiteSpace(Invoke(m)), $"{m} returned nothing");
            }
        }

        [Fact]
        public void The_process_name_carries_no_exe_suffix()
        {
            // The SDK matches on the bare process name; "WindowsTerminal.exe" would match nothing.
            Assert.DoesNotContain(".exe", Invoke("GetProcessName"), StringComparison.OrdinalIgnoreCase);
        }
    }
}
