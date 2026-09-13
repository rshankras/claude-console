namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    /// <summary>
    /// How <c>~/.claude/settings.json</c> was laid out when it was read, so a rewrite hands it back
    /// the same way (#72).
    ///
    /// SettingsText retains the source of unchanged values. This layout applies to NEW or
    /// replaced values only: relaxed JSON escaping, the user's indentation and line endings,
    /// and the original trailing newline and byte-order mark.
    /// </summary>
    internal readonly record struct SettingsLayout(
        Boolean TrailingNewline,
        String NewLine,
        Char IndentCharacter,
        Int32 IndentSize,
        Boolean ByteOrderMark)
    {
        internal static SettingsLayout Default => new(true, "\n", ' ', 2, false);

        /// <summary>Read the layout off the bytes that were parsed. Never throws.</summary>
        internal static SettingsLayout Detect(Byte[] bytes, String text)
        {
            var bom = bytes != null && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            if (String.IsNullOrEmpty(text))
            {
                return Default with { ByteOrderMark = bom };
            }

            var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var trailing = text.EndsWith('\n');
            var (indentChar, indentSize) = DetectIndent(text);
            return new SettingsLayout(trailing, newLine, indentChar, indentSize, bom);
        }

        // The first line that starts with whitespace sets the unit: tabs, or a run of spaces. A file
        // with no indented line (single-line, minified) gets the default — Claude Code never writes
        // one, and there is nothing to preserve.
        private static (Char, Int32) DetectIndent(String text)
        {
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || !Char.IsWhiteSpace(line[0]))
                {
                    continue;
                }
                if (line[0] == '\t')
                {
                    return ('\t', 1);
                }

                var spaces = 0;
                while (spaces < line.Length && line[spaces] == ' ')
                {
                    spaces++;
                }
                if (spaces == line.Length)
                {
                    continue; // whitespace-only line — says nothing about the unit
                }
                return (' ', Math.Clamp(spaces, 1, 8));
            }

            return (' ', 2);
        }

        /// <summary>The document as text, in this layout.</summary>
        internal String Render(JsonNode root)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                NewLine = this.NewLine,
                IndentCharacter = this.IndentCharacter,
                IndentSize = this.IndentSize,
            };
            var json = root.ToJsonString(options);
            return this.TrailingNewline ? json + this.NewLine : json;
        }

        /// <summary>UTF-8, with the byte-order mark only if the file already carried one.</summary>
        internal Encoding Encoding => new UTF8Encoding(encoderShouldEmitUTF8Identifier: this.ByteOrderMark);
    }
}
