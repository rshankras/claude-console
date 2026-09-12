namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    /// <summary>Rewrite changed JSON values while retaining the source of untouched values.</summary>
    internal static class SettingsText
    {
        private sealed class Node
        {
            internal Int32 Start, End, MemberStart;
            internal String Name;
            internal JsonNode Value, Identity;
            internal readonly List<Node> Children = new();
        }

        internal static String Rewrite(String source, JsonNode value, SettingsLayout layout) =>
            Capture(source, null, layout)(value);

        // Capture identities BEFORE mutation. Removing one array entry and editing a later one
        // must retain the later entry's own source, not pair it with the deleted entry by position.
        internal static Func<JsonNode, String> Capture(String source, JsonNode original, SettingsLayout layout)
        {
            if (String.IsNullOrWhiteSpace(source)) { return value => layout.Render(value); }
            var bytes = Encoding.UTF8.GetBytes(source);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
            { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            reader.Read();
            var node = Read(ref reader, bytes, source);
            Bind(node, original);
            return value => source[..node.Start] + Render(source, node, value, layout, 0) + source[node.End..];
        }

        private static void Bind(Node node, JsonNode original)
        {
            node.Identity = original;
            for (var i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                Bind(child, original is JsonObject obj ? obj[child.Name] : original is JsonArray array ? array[i] : null);
            }
        }

        private static Node Read(ref Utf8JsonReader reader, Byte[] bytes, String source)
        {
            Int32 Position(Int64 offset) => Encoding.UTF8.GetCharCount(bytes, 0, checked((Int32)offset));
            var node = new Node { Start = Position(reader.TokenStartIndex) };
            node.MemberStart = node.Start;
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                while (reader.Read() && reader.TokenType is not (JsonTokenType.EndObject or JsonTokenType.EndArray))
                {
                    String name = null;
                    var start = Position(reader.TokenStartIndex);
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        name = reader.GetString();
                        reader.Read();
                    }
                    var child = Read(ref reader, bytes, source);
                    child.Name = name;
                    child.MemberStart = start;
                    node.Children.Add(child);
                }
            }
            node.End = Position(reader.BytesConsumed);
            node.Value = JsonNode.Parse(source[node.Start..node.End], documentOptions: new JsonDocumentOptions
            { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return node;
        }

        private static String Render(String source, Node node, JsonNode value, SettingsLayout layout, Int32 depth)
        {
            if (JsonNode.DeepEquals(node.Value, value)) { return source[node.Start..node.End]; }
            if ((node.Value is not JsonObject || value is not JsonObject) &&
                (node.Value is not JsonArray || value is not JsonArray))
            {
                return Fresh(value, layout, depth);
            }

            var items = new List<(Node Old, String Name, JsonNode Value)>();
            if (value is JsonObject obj)
            {
                foreach (var pair in obj)
                {
                    items.Add((node.Children.FirstOrDefault(c => c.Name == pair.Key), pair.Key, pair.Value));
                }
            }
            else
            {
                var used = new HashSet<Node>();
                foreach (var item in (JsonArray)value)
                {
                    var old = node.Children.FirstOrDefault(c => !used.Contains(c) && item != null && ReferenceEquals(c.Identity, item))
                        ?? node.Children.FirstOrDefault(c => !used.Contains(c) && JsonNode.DeepEquals(c.Value, item));
                    if (old != null) { used.Add(old); }
                    items.Add((old, null, item));
                }
            }

            var multiline = source[node.Start..node.End].Contains('\n');
            var indent = new String(layout.IndentCharacter, layout.IndentSize * (depth + 1));
            var newLeading = multiline || node.Children.Count == 0 ? layout.NewLine + indent : " ";
            var output = new StringBuilder(source[node.Start].ToString());
            foreach (var item in items)
            {
                if (output.Length > 1) { output.Append(','); }
                if (item.Old != null)
                {
                    var index = node.Children.IndexOf(item.Old);
                    var previousEnd = index == 0 ? node.Start + 1 : node.Children[index - 1].End;
                    var leading = source[previousEnd..item.Old.MemberStart];
                    if (index > 0) { leading = RemoveComma(leading); }
                    output.Append(leading);
                    output.Append(source[item.Old.MemberStart..item.Old.Start]);
                    output.Append(Render(source, item.Old, item.Value, layout, depth + 1));
                }
                else
                {
                    output.Append(newLeading);
                    if (item.Name != null)
                    { output.Append(JsonSerializer.Serialize(item.Name)); output.Append(": "); }
                    output.Append(Fresh(item.Value, layout, depth + 1));
                }
            }
            var end = node.Children.Count == 0 ? node.Start + 1 : node.Children[^1].End;
            var suffix = source[end..(node.End - 1)];
            // A pre-existing trailing comma is legal for this reader; keep it only with members.
            if (items.Count == 0) { suffix = RemoveComma(suffix); }
            if (node.Children.Count == 0 && items.Count > 0 && String.IsNullOrWhiteSpace(suffix))
            { suffix = layout.NewLine + new String(layout.IndentCharacter, layout.IndentSize * depth); }
            output.Append(suffix);
            output.Append(source[node.End - 1]);
            return output.ToString();
        }

        // Skip comments so a comma inside one is never mistaken for a JSON separator.
        private static String RemoveComma(String trivia)
        {
            for (var i = 0; i < trivia.Length; i++)
            {
                if (trivia[i] == ',') { return trivia.Remove(i, 1); }
                if (i + 1 < trivia.Length && trivia[i] == '/' && trivia[i + 1] == '/')
                { var end = trivia.IndexOf('\n', i + 2); if (end < 0) { break; } i = end; }
                else if (i + 1 < trivia.Length && trivia[i] == '/' && trivia[i + 1] == '*')
                { var end = trivia.IndexOf("*/", i + 2, StringComparison.Ordinal); if (end < 0) { break; } i = end + 1; }
            }
            return trivia;
        }

        private static String Fresh(JsonNode value, SettingsLayout layout, Int32 depth)
        {
            if (value == null) { return "null"; }
            var text = (layout with { TrailingNewline = false }).Render(value);
            return text.Replace(layout.NewLine, layout.NewLine + new String(layout.IndentCharacter, layout.IndentSize * depth));
        }
    }
}
