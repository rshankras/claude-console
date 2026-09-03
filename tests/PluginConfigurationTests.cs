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
    /// Logitech's own @Generic do not and warn the same way). Every action here is dynamic, so the
    /// file declares nothing — but the parser is strict about its SHAPE, and each missing piece was
    /// found on 2026-09-03 by a load that failed with the keys dead ("'actions' tag not found",
    /// then "'layout' tags not found", each refusing every dynamic action with a
    /// NullReferenceException). These tests pin that shape so the next edit cannot repeat it.
    /// </summary>
    public class PluginConfigurationTests
    {
        private static readonly String[] Products = { "ClaudeConsole", "VizhiCodex" };

        [Theory]
        [InlineData("ClaudeConsole")]
        [InlineData("VizhiCodex")]
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
        public void The_declaration_has_every_tag_the_parser_insists_on_and_declares_no_actions(String product)
        {
            var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "Products", product, "PluginConfiguration.xml"));
            var root = doc.Root;

            Assert.Equal("configuration", root.Name.LocalName);
            Assert.NotNull(root.Element("plugin"));
            Assert.NotNull(root.Element("aliases"));
            Assert.NotEmpty(root.Element("modes").Elements("mode"));            // a mode
            Assert.NotNull(root.Element("actions"));                            // "'actions' tag not found"
            Assert.NotEmpty(root.Element("layouts").Elements("layout"));        // "'layout' tags not found"

            // Nothing static: a declared action here would sit beside the dynamic one of the same
            // name, and the two can disagree. The layout stays empty for the same reason.
            Assert.Empty(root.Element("actions").Elements());
            Assert.All(root.Element("layouts").Elements("layout"), layout => Assert.Empty(layout.Elements()));
        }

        [Theory]
        [InlineData("ClaudeConsole")]
        [InlineData("VizhiCodex")]
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
