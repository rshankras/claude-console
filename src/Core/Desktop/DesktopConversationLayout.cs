namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>Measured, display-only wrapping. Never use these shortened strings for navigation.</summary>
    internal static class DesktopConversationLayout
    {
        internal static String[] FitLines(String title, Single width, Func<String, Single> measure)
        {
            if (String.IsNullOrWhiteSpace(title) || width <= 0) { return Array.Empty<String>(); }
            var remaining = String.Join(" ", title.Split((Char[])null, StringSplitOptions.RemoveEmptyEntries));
            var lines = new List<String>();
            while (remaining.Length > 0 && lines.Count < 3)
            {
                if (measure(remaining) <= width)
                {
                    lines.Add(remaining);
                    break;
                }
                if (lines.Count == 2)
                {
                    lines.Add(Ellipsize(remaining, width, measure));
                    break;
                }

                var cut = FittingPrefix(remaining, width, measure);
                if (cut == 0)
                {
                    // A single grapheme cannot fit (e.g. a very small library thumbnail).
                    lines.Add(measure("…") <= width ? "…" : "");
                    break;
                }
                var wordEnd = remaining.LastIndexOf(' ', Math.Min(cut, remaining.Length - 1));
                if (wordEnd > 0) { cut = wordEnd; }
                lines.Add(remaining.Substring(0, cut).TrimEnd());
                remaining = remaining.Substring(cut).TrimStart();
            }
            return lines.ToArray();
        }

        private static String Ellipsize(String text, Single width, Func<String, Single> measure)
        {
            if (measure("…") > width) { return ""; }
            var cut = FittingPrefix(text, width, value => measure(value.TrimEnd() + "…"));
            if (cut == 0) { return "…"; }
            var wordEnd = text.LastIndexOf(' ', Math.Min(cut, text.Length - 1));
            // Prefer a complete word; only split a word when that word alone is too wide.
            if (wordEnd > 0) { cut = wordEnd; }
            return text.Substring(0, cut).TrimEnd() + "…";
        }

        private static Int32 FittingPrefix(String text, Single width, Func<String, Single> measure)
        {
            // A UTF-16 character limit can split an emoji, combining mark or Indic grapheme.
            var starts = StringInfo.ParseCombiningCharacters(text);
            var low = 0;
            var high = starts.Length;
            while (low < high)
            {
                var count = (low + high + 1) / 2;
                var end = count == starts.Length ? text.Length : starts[count];
                if (measure(text.Substring(0, end)) <= width) { low = count; }
                else { high = count - 1; }
            }
            return low == starts.Length ? text.Length : starts[low];
        }
    }
}
