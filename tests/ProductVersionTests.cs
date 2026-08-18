namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;

    using Xunit;

    /// <summary>
    /// Each product versions itself independently — and consistently.
    ///
    /// One repo, several packages, and no reason for a Codex release to drag Claude Console's
    /// number along. But every product states its version TWICE: in the csproj, which becomes the
    /// assembly version, and in LoupedeckPackage.yaml, which is what the Marketplace and Options+
    /// show. Nothing enforces that they agree, and disagreement is silent in both directions:
    ///
    ///   • the yaml is what a reviewer and a user see, so a stale one ships the wrong number
    ///   • the ASSEMBLY version is what the Logi Plugin Service keys its crash-disable marker on
    ///     (Logs/plugin_crashes/&lt;Plugin&gt;.dll). A build that fails to bump it inherits a marker
    ///     from a crash it already fixed, and the plugin stays disabled with no visible cause.
    ///
    /// Products are discovered from disk, so a third one is covered the day it appears.
    /// </summary>
    public class ProductVersionTests
    {
        private static String ProductsRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir, "src", "Products");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate src/Products");
        }

        private sealed record Product(String Name, String CsprojVersion, String YamlVersion, String PluginFileName, String AssemblyName);

        private static List<Product> Discover()
        {
            var products = new List<Product>();

            foreach (var dir in Directory.GetDirectories(ProductsRoot()))
            {
                var csproj = Directory.GetFiles(dir, "*.csproj").SingleOrDefault();
                var yaml = Path.Combine(dir, "package", "metadata", "LoupedeckPackage.yaml");
                if (csproj == null || !File.Exists(yaml))
                {
                    continue;
                }

                var csprojText = File.ReadAllText(csproj);
                var yamlText = File.ReadAllText(yaml);

                products.Add(new Product(
                    Path.GetFileName(dir),
                    Match(csprojText, @"<Version>\s*([^<\s]+)\s*</Version>"),
                    Match(yamlText, @"(?m)^version:\s*(\S+)"),
                    Match(yamlText, @"(?m)^pluginFileName:\s*(\S+)"),
                    Match(csprojText, @"<AssemblyName>\s*([^<\s]+)\s*</AssemblyName>")
                        ?? Path.GetFileNameWithoutExtension(csproj)));
            }

            Assert.True(products.Count >= 2, "expected at least two products under src/Products");
            return products;
        }

        private static String Match(String text, String pattern)
        {
            var m = Regex.Match(text, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }

        [Fact]
        public void Every_product_states_the_same_version_in_both_places()
        {
            foreach (var p in Discover())
            {
                Assert.False(String.IsNullOrWhiteSpace(p.CsprojVersion), $"{p.Name}: no <Version> in the csproj");
                Assert.False(String.IsNullOrWhiteSpace(p.YamlVersion), $"{p.Name}: no version in LoupedeckPackage.yaml");
                Assert.True(p.CsprojVersion == p.YamlVersion,
                    $"{p.Name}: assembly version {p.CsprojVersion} but package version {p.YamlVersion} — " +
                    "the package number is what users see, the assembly number is what the crash-disable marker keys on");
            }
        }

        /// <summary>
        /// The yaml names the DLL the service loads. A mismatch means the service looks for an
        /// assembly the package doesn't contain, and the plugin simply never appears.
        /// </summary>
        [Fact]
        public void Every_product_points_the_service_at_the_assembly_it_actually_builds()
        {
            foreach (var p in Discover())
            {
                Assert.Equal(p.AssemblyName + ".dll", p.PluginFileName);
            }
        }

        /// <summary>
        /// Independence is the point: products share an engine, not a release cadence. Identical
        /// numbers today would be coincidence rather than error, so this only asserts that nothing
        /// forces them to move together.
        /// </summary>
        [Fact]
        public void Products_version_independently()
        {
            var products = Discover();

            Assert.Equal(products.Count, products.Select(p => p.Name).Distinct().Count());
            Assert.Equal(products.Count, products.Select(p => p.AssemblyName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }
}
