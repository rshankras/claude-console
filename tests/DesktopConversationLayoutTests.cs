namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using SkiaSharp;
    using Xunit;

    public class DesktopConversationLayoutTests
    {
        [Theory]
        [InlineData("Organize recent ChatGPT discussions")]
        [InlineData("Make plate with duration")]
        [InlineData("Find planned next work")]
        [InlineData("WWW WWW WWW WWW WWW WWW")]
        [InlineData("iiii iiiii iiiii iiiiii iiiii")]
        [InlineData("A_very_long_unbroken_conversation_identifier_123456789")]
        [InlineData("தமிழ் உரையாடல் தலைப்பு")]
        [InlineData("日本語の会話タイトルの折り返し")]
        [InlineData("Review 👨‍👩‍👧‍👦 family plans and cafe\u0301 notes for September")]
        public void Every_line_fits_the_actual_font_width_and_only_the_last_can_be_truncated(String title)
        {
            var lines = Layout(title);
            Assert.InRange(lines.Length, 1, 3);
            Assert.All(lines, line => Assert.InRange(Width(line), 0, 80));
            Assert.All(lines.SkipLast(1), line => Assert.DoesNotContain("…", line));
            foreach (var line in lines)
            {
                Assert.DoesNotContain(Rune.ReplacementChar, line.EnumerateRunes());
                Assert.False(line.EndsWith("\u200D", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void Short_titles_fit_in_two_lines_without_reducing_the_font_or_losing_words()
        {
            foreach (var title in new[] { "Organize chats", "Plate duration", "Next work plan", "Q3 report" })
            {
                var lines = Layout(title);
                Assert.InRange(lines.Length, 1, 2);
                Assert.Equal(title, String.Join(" ", lines));
            }
        }

        [Fact]
        public void Final_line_prefers_a_complete_word_over_a_dangling_fragment()
        {
            var lines = DesktopConversationLayout.FitLines("alpha bravo charlie delta longer", 10, s => s.Length);
            Assert.Equal(new[] { "alpha", "bravo", "charlie…" }, lines);
        }

        [Fact]
        public void Combining_sequences_are_not_split_even_when_a_word_is_too_long()
        {
            var title = String.Concat(Enumerable.Repeat("e\u0301", 12));
            var lines = DesktopConversationLayout.FitLines(title, 4,
                s => StringInfo.ParseCombiningCharacters(s).Length);
            Assert.Equal(new[] { "e\u0301e\u0301e\u0301e\u0301", "e\u0301e\u0301e\u0301e\u0301", "e\u0301e\u0301e\u0301e\u0301" }, lines);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(0.1f)]
        public void Tiny_canvases_terminate_without_drawing_overwide_text(Single width)
        {
            Assert.All(DesktopConversationLayout.FitLines("Long title", width, s => s.Length),
                line => Assert.True(line.Length <= width));
        }

        [Fact]
        public void Photo_regression_renders_three_separate_lines_with_no_fourth_line_or_footer_overlap()
        {
            var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            try
            {
                using var face = DesktopConversationRenderer.Render(PluginImageSize.Width90,
                    "Organize recent ChatGPT discussions", "Ready", KeyImage.Gray, false);
                face.SaveToFile(file);
                using var image = SKBitmap.Decode(file);
                var rows = Enumerable.Range(0, 74).Where(y => Enumerable.Range(0, image.Width)
                    .Any(x => { var p = image.GetPixel(x, y); return p.Red > 180 && p.Green > 180 && p.Blue > 180; })).ToArray();
                var bands = rows.Where((y, i) => i == 0 || y - rows[i - 1] > 2).ToArray();
                Assert.Equal(3, bands.Length);
                Assert.True(rows[0] > 3);
                Assert.True(rows[^1] < 70);
                Assert.Equal(new SKColor(0x28, 0x28, 0x2E), image.GetPixel(0, image.Height - 1));
            }
            finally { File.Delete(file); }
        }

        private static Single Width(String text) => DesktopConversationRenderer.Measure(text, DesktopConversationRenderer.TitleFontAt90);
        private static String[] Layout(String title) => DesktopConversationLayout.FitLines(title, 80, Width);
    }
}
