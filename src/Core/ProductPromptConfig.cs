namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Collections.Generic;

    internal static class ProductPromptConfig
    {
        internal static String PathFor(String home, String product) => product == "codex-console"
            ? Path.Combine(home, ".codex", "vizhi", "prompts.json")
            : Path.Combine(home, ".claude", "claude-console", "prompts.json");

        // Null asks the caller to use in-memory defaults, leaving migration retryable next load.
        internal static String Resolve(String home, String product, Action<String> warn)
        {
            var destination = PathFor(home, product);
            if (product != "codex-console" || File.Exists(destination)) { return destination; }
            var source = PathFor(home, "claude-console");
            if (!File.Exists(source)) { return destination; }
            try
            {
                var raw = File.ReadAllBytes(source);
                using var reader = new StreamReader(new MemoryStream(raw), detectEncodingFromByteOrderMarks: true);
                var entries = JsonSerializer.Deserialize<List<PromptDef>>(reader.ReadToEnd(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (entries == null || entries.Any(p => p == null || String.IsNullOrWhiteSpace(p.Id))
                    || entries.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != entries.Count)
                {
                    throw new JsonException("Expected a prompt array with unique, nonempty IDs.");
                }
                Publish(destination, raw, overwrite: false);
                return destination;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                // A concurrent first load may have installed a valid destination while we read.
                if (File.Exists(destination)) { return destination; }
                warn?.Invoke($"Could not copy prompts from {source} to {destination}. " +
                    "The original is unchanged; using defaults for this load. Check the source file and folder permissions, then reload.");
                return null;
            }
        }

        internal static void Publish(String destination, String content, Boolean overwrite)
            => Publish(destination, System.Text.Encoding.UTF8.GetBytes(content), overwrite);

        internal static void Publish(String destination, Byte[] content, Boolean overwrite)
        {
            var parent = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(parent);
            var temporary = Path.Combine(parent, ".prompts-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(temporary, content);
                try { File.Move(temporary, destination, overwrite); }
                catch (IOException) when (!overwrite && File.Exists(destination))
                {
                    // Destination won the create race. Never replace the user's new file.
                }
            }
            finally
            {
                if (File.Exists(temporary)) { File.Delete(temporary); }
            }
        }
    }
}
