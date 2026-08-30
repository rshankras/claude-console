namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Reflection;

    using Xunit;

    /// <summary>
    /// The shape of a universal plugin, pinned (#23).
    ///
    /// Two halves have to agree, and neither compiler checks the other:
    ///
    ///   1. The package yaml says HasNoApplication and carries no profiles/ — the plugin binds no
    ///      application and imports no layout. Users import a download onto Terminal's own entry.
    ///   2. The assembly STILL contains a ClientApplication subclass, and it overrides nothing.
    ///
    /// The second half cost an afternoon on 2026-08-28. The universal change deleted the class
    /// along with the Terminal binding it carried, and the Logi Plugin Service then refused the
    /// assembly — "Cannot load plugin", "added to disabled plugins list" — with no reason in any
    /// log, while the same DLL loaded fine in a plain host. Rebuilding the previous commit loaded;
    /// probing Spotify, the universal plugin QA named as the model, showed an empty
    /// SpotifyApplication : ClientApplication. The service requires the class to exist; the yaml
    /// decides whether it binds anything. And "binds nothing" must stay literally true: under
    /// HasApplication this same empty shape was the 1.5-era crash.
    /// </summary>
    public class UniversalPluginTests
    {
        private static String RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "src", "Products")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate the repo root");
        }

        public static TheoryData<Type, String> Products => new TheoryData<Type, String>
        {
            { typeof(ClaudeConsoleApplication), "ClaudeConsole" },
            { typeof(VizhiCodexApplication), "VizhiCodex" },
        };

        [Theory]
        [MemberData(nameof(Products))]
        public void The_application_class_exists_and_binds_nothing(Type app, String product)
        {
            // Exists: the service will not load an assembly without one.
            Assert.True(typeof(ClientApplication).IsAssignableFrom(app), $"{product}: not a ClientApplication");
            Assert.NotNull(Activator.CreateInstance(app));

            // Binds nothing: none of the identity accessors may be overridden. Overriding one with a
            // real name recreates the app-bound plugin; overriding with "" under HasApplication was
            // the crash. Leaving them alone is the only shape that is safe in every combination.
            foreach (var name in new[] { "GetProcessName", "GetBundleName", "GetApplicationStatus" })
            {
                var m = app.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Assert.NotNull(m);
                Assert.Equal(typeof(ClientApplication), m.DeclaringType);
            }
        }

        [Theory]
        [MemberData(nameof(Products))]
        public void The_package_declares_no_application_and_ships_no_profile(Type app, String product)
        {
            _ = app;
            var package = Path.Combine(RepoRoot(), "src", "Products", product, "package");
            var yaml = File.ReadAllText(Path.Combine(package, "metadata", "LoupedeckPackage.yaml"));

            Assert.Contains("- HasNoApplication", yaml);
            Assert.DoesNotContain("- HasApplication", yaml);

            // A packaged DefaultProfile70.lp5 is exactly what an application plugin auto-imported;
            // a universal plugin cannot import one, so shipping it would only mislead a reviewer.
            Assert.False(Directory.Exists(Path.Combine(package, "profiles")), $"{product} still ships package/profiles");
        }

        [Fact]
        public void The_downloads_bind_the_terminal_not_the_plugin()
        {
            // Every profile in profiles/ is a Terminal profile that USES a plugin — never one a
            // plugin owns. hasNativePlugin=true with our name would be the old app-bound shape,
            // which a universal plugin can no longer satisfy.
            var profiles = Path.Combine(RepoRoot(), "profiles");
            foreach (var lp5 in Directory.GetFiles(profiles, "*.lp5"))
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(lp5);
                using var reader = new StreamReader(zip.GetEntry("ApplicationInfo.json").Open());
                var appInfo = reader.ReadToEnd();

                Assert.Contains("\"hasNativePlugin\": false", appInfo);
                Assert.DoesNotContain("@_claudeconsole", appInfo);
                Assert.DoesNotContain("@_codexconsole", appInfo);
            }
        }
    }
}
