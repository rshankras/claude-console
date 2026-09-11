namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json.Nodes;

    using Xunit;

    /// <summary>
    /// A settings.json rewrite must hand the file back the way it was found (#72, Logitech QA's
    /// 2.2.1 retest, finding B). The plugin parses the whole document and serialises it again, and
    /// the default writer made that round trip visible: every quote, ampersand, apostrophe, angle
    /// bracket and non-ASCII character came back as a \uXXXX escape and the trailing newline was
    /// dropped — on every write, in entries the plugin does not own. These tests push a file through
    /// the real door (BridgeManager.RewriteSettings) with a mutate that changes nothing, so any byte
    /// that differs afterwards is the writer's doing.
    /// </summary>
    public class SettingsLayoutTests
    {
        // What QA's file looked like: another tool's hook carrying quotes, an ampersand and an
        // apostrophe; a description with an em dash; two-space indentation; a trailing newline.
        private const String QaShapedFile =
            "{\n" +
            "  \"model\": \"opus\",\n" +
            "  \"permissions\": {\n" +
            "    \"allow\": [\n" +
            "      \"**Cloud provider(s)**: Firebase — used by this repo <Specimen>\"\n" +
            "    ]\n" +
            "  },\n" +
            "  \"hooks\": {\n" +
            "    \"Stop\": [\n" +
            "      {\n" +
            "        \"hooks\": [\n" +
            "          {\n" +
            "            \"type\": \"command\",\n" +
            "            \"command\": \"printf '%s' \\\"QA's foreign & \\\\\\\"quoted\\\\\\\" hook\\\" >/dev/null\"\n" +
            "          }\n" +
            "        ]\n" +
            "      }\n" +
            "    ]\n" +
            "  }\n" +
            "}\n";

        private static void Touch(Boolean expectWrite)
        {
            var ok = BridgeManager.RewriteSettings(_ => true, out var changed);
            Assert.True(ok);
            Assert.Equal(expectWrite, changed);
        }

        [Fact]
        public void A_write_that_changes_nothing_reproduces_the_file_byte_for_byte()
        {
            using var home = new TempHome();
            home.WriteSettings(QaShapedFile);

            Touch(expectWrite: true);

            Assert.Equal(QaShapedFile, home.ReadSettings());
        }

        [Fact]
        public void Quotes_symbols_and_non_ascii_stay_literal_after_a_real_change()
        {
            using var home = new TempHome();
            home.WriteSettings(QaShapedFile);

            var ok = BridgeManager.RewriteSettings(root => { root["x"] = "y"; return true; }, out _);
            Assert.True(ok);

            var text = home.ReadSettings();
            Assert.DoesNotContain("\\u", text);
            // As it sits in the file: the shell's \" is JSON-escaped to \\\" and stays that way.
            Assert.Contains("printf '%s' \\\"QA's foreign & \\\\\\\"quoted\\\\\\\" hook\\\" >/dev/null", text);
            Assert.Contains("Firebase — used by this repo <Specimen>", text);
            Assert.EndsWith("}\n", text);
            Assert.Equal("y", JsonNode.Parse(text)["x"].GetValue<String>());
        }

        [Fact]
        public void A_file_without_a_trailing_newline_stays_without_one()
        {
            using var home = new TempHome();
            var original = "{\n  \"model\": \"opus\"\n}";
            home.WriteSettings(original);

            Touch(expectWrite: true);

            Assert.Equal(original, home.ReadSettings());
        }

        [Fact]
        public void Crlf_line_endings_and_four_space_indentation_are_kept()
        {
            using var home = new TempHome();
            var original = "{\r\n    \"model\": \"opus\",\r\n    \"hooks\": {\r\n        \"Stop\": []\r\n    }\r\n}\r\n";
            home.WriteSettings(original);

            Touch(expectWrite: true);

            Assert.Equal(original, home.ReadSettings());
        }

        [Fact]
        public void Tab_indentation_is_kept()
        {
            using var home = new TempHome();
            var original = "{\n\t\"model\": \"opus\",\n\t\"hooks\": {\n\t\t\"Stop\": []\n\t}\n}\n";
            home.WriteSettings(original);

            Touch(expectWrite: true);

            Assert.Equal(original, home.ReadSettings());
        }

        [Fact]
        public void A_byte_order_mark_is_kept_and_never_introduced()
        {
            using var home = new TempHome();
            Directory.CreateDirectory(home.ClaudeDir);
            var body = "{\n  \"model\": \"opus\"\n}\n";
            File.WriteAllBytes(home.Settings, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(body)));

            Touch(expectWrite: true);

            var bytes = File.ReadAllBytes(home.Settings);
            Assert.Equal(new Byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.Equal(body, Encoding.UTF8.GetString(bytes[3..]));

            home.WriteSettings(body);
            Touch(expectWrite: true);
            Assert.NotEqual(0xEF, File.ReadAllBytes(home.Settings)[0]);
        }

        [Fact]
        public void A_fresh_file_gets_claude_codes_own_shape()
        {
            using var home = new TempHome();

            var ok = BridgeManager.RewriteSettings(root => { root["x"] = "y"; return true; }, out _);
            Assert.True(ok);

            Assert.Equal("{\n  \"x\": \"y\"\n}\n", home.ReadSettings());
        }

        [Fact]
        public void Detect_reads_the_layout_off_the_text()
        {
            var lf = SettingsLayout.Detect(Array.Empty<Byte>(), "{\n  \"a\": 1\n}\n");
            Assert.Equal(new SettingsLayout(true, "\n", ' ', 2, false), lf);

            var crlf = SettingsLayout.Detect(Array.Empty<Byte>(), "{\r\n    \"a\": 1\r\n}");
            Assert.Equal(new SettingsLayout(false, "\r\n", ' ', 4, false), crlf);

            var minified = SettingsLayout.Detect(Array.Empty<Byte>(), "{\"a\":1}");
            Assert.Equal(SettingsLayout.Default with { TrailingNewline = false }, minified);
        }
    }

    internal static class ByteArrayExtensions
    {
        public static Byte[] Concat(this Byte[] head, Byte[] tail)
        {
            var all = new Byte[head.Length + tail.Length];
            head.CopyTo(all, 0);
            tail.CopyTo(all, head.Length);
            return all;
        }
    }
}
