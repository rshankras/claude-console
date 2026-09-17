namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Xml.Linq;

    using Xunit;

    /// <summary>
    /// Each product embeds the SDK's static plugin declaration, PluginConfiguration.xml (#63).
    ///
    /// Logitech QA's 2.2.0 retest noted two WARN lines on every load, "'PluginConfiguration.xml'
    /// file not found for 'ClaudeConsole' plugin", where the 2.2.0 response had claimed zero. The
    /// file is an SDK mechanism (Logitech's Spotify and DefaultMac plugins embed it; Zoom and
    /// Logitech's own @Generic do not and warn the same way). The parser is strict about its SHAPE,
    /// and an empty actions collection still emits "Action tags not found". Because every real
    /// action here is dynamic, the declaration contains one uniquely named command restricted to
    /// deviceType 0 (None): it satisfies the legacy parser without appearing on a real device or
    /// colliding with a runtime action. These tests pin that deliberately odd compatibility shim.
    /// </summary>
    public class PluginConfigurationTests
    {
        private static readonly String[] Products = { "ClaudeConsole", "VizhiCodex", "VizhiDesktop" };

        [Theory]
        [InlineData("ClaudeConsole")]
        [InlineData("VizhiCodex")]
        [InlineData("VizhiDesktop")]
        public void The_file_is_embedded_under_the_name_the_SDK_looks_up(String product)
        {
            // The SDK resolves "<plugin class namespace>.PluginConfiguration.xml". Both products
            // live in Loupedeck.ClaudeConsolePlugin, so both use the same logical name.
            var csproj = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Products", product, product + "Plugin.csproj"));

            Assert.Contains("<EmbeddedResource Include=\"PluginConfiguration.xml\">", csproj);
            Assert.Contains("<LogicalName>Loupedeck.ClaudeConsolePlugin.PluginConfiguration.xml</LogicalName>", csproj);
        }

        [Theory]
        [InlineData("ClaudeConsole")]
        [InlineData("VizhiCodex")]
        [InlineData("VizhiDesktop")]
        public void The_declaration_satisfies_the_legacy_parser_without_exposing_a_static_action(String product)
        {
            var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "Products", product, "PluginConfiguration.xml"));
            var root = doc.Root;

            Assert.Equal("configuration", root.Name.LocalName);
            Assert.NotNull(root.Element("plugin"));
            Assert.NotNull(root.Element("aliases"));
            Assert.NotEmpty(root.Element("modes").Elements("mode"));            // a mode
            Assert.NotNull(root.Element("actions"));                            // "'actions' tag not found"
            Assert.NotEmpty(root.Element("layouts").Elements("layout"));        // "'layout' tags not found"

            // An empty collection produces a second SDK warning, "Action tags not found". One
            // command must therefore parse, but deviceType None ensures it is filtered from every
            // real keypad. A unique name prevents AddDynamicAction from hitting a duplicate key.
            var groups = root.Element("actions").Elements("group").ToArray();
            var commands = groups.SelectMany(group => group.Elements("command")).ToArray();
            var sentinel = Assert.Single(commands);

            Assert.Single(groups);
            Assert.Equal("Compatibility", (String)groups[0].Attribute("name"));
            Assert.Equal("LegacyParserSentinel", (String)sentinel.Attribute("name"));
            Assert.Equal("0", (String)sentinel.Attribute("deviceType"));
            Assert.DoesNotContain(
                Directory.GetFiles(Path.Combine(RepoRoot(), "src", "Core", "Actions"), "*.cs"),
                file => Path.GetFileNameWithoutExtension(file) == (String)sentinel.Attribute("name"));

            // The sentinel must not be assigned to a layout either.
            Assert.All(root.Element("layouts").Elements("layout"), layout => Assert.Empty(layout.Elements()));
        }

        [Theory]
        [InlineData("ClaudeConsole")]
        [InlineData("VizhiCodex")]
        [InlineData("VizhiDesktop")]
        public void The_display_name_is_the_packages_display_name(String product)
        {
            var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "Products", product, "PluginConfiguration.xml"));
            var yaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Products", product, "package", "metadata", "LoupedeckPackage.yaml"));
            var packaged = Regex.Match(yaml, @"^displayName:\s*(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim();

            Assert.Equal(packaged, (String)doc.Root.Element("plugin").Attribute("displayName"));
        }

        [Fact]
        public void Every_product_has_one()
        {
            foreach (var product in Products)
            {
                Assert.True(File.Exists(Path.Combine(RepoRoot(), "src", "Products", product, "PluginConfiguration.xml")), product);
            }
        }

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
    }
}
