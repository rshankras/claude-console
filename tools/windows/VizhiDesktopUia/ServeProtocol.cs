// The wire format of `vizhi-desktop-uia serve` (#155). One request per line, one response per
// line, both JSON. Kept free of UI Automation so the engine's test suite compiles it on any
// platform and pins the format the plugin's UiaHelperHost speaks.
//
//   request   ["status","--process","app.exe",...]       the same argv a one-shot run takes
//   response  {"exit":0,"out":"{\"ok\":true,...}"}        the one-shot exit code and stdout
// The server first writes {"ready":1}; the client sends no action before that handshake.
//
// Both sides escape non-ASCII, so the console code page never touches dictated text.

using System.Text;
using System.Text.Json;

namespace VizhiDesktopUia;

internal static class ServeProtocol
{
    /// <summary>The argv in one request line, or null when the line is not an array of strings.</summary>
    public static String[]? ParseRequest(String? line)
    {
        if (String.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            var args = new List<String>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) return null;
                args.Add(item.GetString()!);
            }
            return args.Count == 0 ? null : args.ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static String Request(IEnumerable<String> args)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var arg in args) writer.WriteStringValue(arg);
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static String Response(Int32 exit, String output)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("exit", exit);
            writer.WriteString("out", output);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The exit code and stdout in one response line, or null when it is not one.</summary>
    public static (Int32 Exit, String Output)? ParseResponse(String? line)
    {
        if (String.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("exit", out var exit) || exit.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty("out", out var output) || output.ValueKind != JsonValueKind.String)
                return null;
            return exit.TryGetInt32(out var code) ? (code, output.GetString()!) : null;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }
}
