namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;

    internal static class ProjectVocabulary
    {
        // Whisper accepts a vocabulary prompt, not instructions. Bound it so large homes don't
        // crowd out the recorded speech. This improves decoding; it never authorizes a launch.
        internal static String For(IEnumerable<String> paths)
        {
            var result = new StringBuilder();
            foreach (var name in (paths ?? Array.Empty<String>())
                .Where(p => !String.IsNullOrWhiteSpace(p))
                .Select(p => Path.GetFileName(p.TrimEnd('/', '\\')))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(32))
            {
                var words = new String(name.Select(c => Char.IsLetterOrDigit(c) ? c : ' ').ToArray());
                words = String.Join(" ", words.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                if (words.Length == 0 || words.Length > 120) continue;
                if (result.Length + words.Length + 2 > 1000) break;
                if (result.Length > 0) result.Append(", ");
                result.Append(words);
            }
            return result.ToString();
        }
    }
}
