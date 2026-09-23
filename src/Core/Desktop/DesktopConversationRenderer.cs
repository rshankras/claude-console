namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Text;
    using SkiaSharp;

    /// <summary>
    /// Full-key desktop conversation cards. Measure and draw with the same font; draw each line
    /// exactly once. BitmapBuilder.DrawText re-wraps text and cannot enforce our line limit.
    /// SkiaSharp is already supplied by the SDK host; it must not be bundled in the plugin.
    /// </summary>
    internal static class DesktopConversationRenderer
    {
        internal const Single TitleFontAt90 = 13.5f;
        internal const Single LineHeightAt90 = 18f;

        public static BitmapImage Render(PluginImageSize imageSize, String title, String stateWord,
            BitmapColor barColor, Boolean darkText)
        {
            var width = imageSize.GetButtonWidth();
            var height = imageSize.GetButtonHeight();
            if (width <= 0 || height <= 0)
            {
                using var fallback = new BitmapBuilder(imageSize);
                width = fallback.Width;
                height = fallback.Height;
            }
            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Black);
            if (!String.IsNullOrWhiteSpace(title) && !String.IsNullOrWhiteSpace(stateWord))
            {
                var scale = Math.Min(width, height) / 90f;
                var pad = Math.Max(2f, 4f * scale);
                var footerHeight = Math.Max(1, (Int32)Math.Round(16f * scale));
                var titleHeight = height - footerHeight;
                var fontSize = TitleFontAt90 * scale;
                var lineHeight = LineHeightAt90 * scale;
                var lines = DesktopConversationLayout.FitLines(title, width - 2 * pad - 2 * scale,
                    text => Measure(text, fontSize));
                var top = (titleHeight - lines.Length * lineHeight) / 2;

                canvas.Save();
                canvas.ClipRect(new SKRect(pad, 0, width - pad, titleHeight));
                for (var i = 0; i < lines.Length; i++)
                {
                    DrawLine(canvas, lines[i], width / 2f, top + i * lineHeight, lineHeight, fontSize, SKColors.White);
                }
                canvas.Restore();

                var quiet = String.Equals(stateWord, "Ready", StringComparison.Ordinal);
                using var bar = new SKPaint { Color = quiet ? new SKColor(0x28, 0x28, 0x2E) :
                    new SKColor(barColor.R, barColor.G, barColor.B, barColor.A) };
                canvas.DrawRect(0, titleHeight, width, footerHeight, bar);
                DrawLine(canvas, stateWord, width / 2f, titleHeight, footerHeight, 11f * scale,
                    quiet ? new SKColor(0xB8, 0xB8, 0xC0) : darkText ? new SKColor(0x0D, 0x11, 0x17) : SKColors.White);
            }
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return BitmapImage.FromArray(encoded.ToArray());
        }

        internal static Single Measure(String text, Single fontSize)
        {
            using var fallback = FallbackTypeface(text);
            using var paint = TextPaint(fontSize, fallback);
            var bounds = new SKRect();
            var advance = paint.MeasureText(text, ref bounds);
            return Math.Max(advance, bounds.Right) - Math.Min(0, bounds.Left);
        }

        private static void DrawLine(SKCanvas canvas, String text, Single centerX, Single top,
            Single height, Single fontSize, SKColor color)
        {
            using var fallback = FallbackTypeface(text);
            using var paint = TextPaint(fontSize, fallback);
            paint.Color = color;
            paint.TextAlign = SKTextAlign.Center;
            var metrics = paint.FontMetrics;
            var baseline = top + (height - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent;
            canvas.DrawText(text, centerX, baseline, paint); // SKCanvas draws one line; never word-wraps.
        }

        private static SKPaint TextPaint(Single size, SKTypeface fallback) => new SKPaint
        {
            Typeface = fallback ?? BitmapFonts.GetDefaultTypeface(),
            TextSize = size,
            IsAntialias = true,
            FakeBoldText = true, // same SDK font weight as the other keypad labels
        };

        private static SKTypeface FallbackTypeface(String text)
        {
            // Preserve font fallback for titles in other scripts; never dispose the SDK's
            // shared default face. This owned fallback is disposed after measurement/drawing.
            using var font = new SKFont(BitmapFonts.GetDefaultTypeface());
            foreach (var rune in (text ?? "").EnumerateRunes())
            {
                if (!font.ContainsGlyph(rune.Value)) { return SKFontManager.Default.MatchCharacter(rune.Value); }
            }
            return null;
        }
    }
}
