namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// One observation of the desktop app's UI, as reported by a single AX helper `status`
    /// invocation. This is a raw reading, not an interpretation — mapping it to activity and
    /// risk is <see cref="DesktopMonitor"/>'s job, so the mapping is testable without AX.
    ///
    /// <see cref="SurfaceAvailable"/> is load-bearing: when the screen locks or the window
    /// hides, Chromium drops the whole web AX tree and every control "disappears" at once.
    /// That mass-removal must read as "we cannot see", never as "the approval was resolved" —
    /// the difference between a grey key and a lying one.
    /// </summary>
    internal sealed class DesktopSnapshot
    {
        public Boolean SurfaceAvailable { get; init; }
        public Boolean Attention { get; init; }
        public Boolean ApprovalPresent { get; init; }
        public Boolean DenyPresent { get; init; }
        public Boolean StopPresent { get; init; }
        public String CardText { get; init; } = "";
        public String Mode { get; init; } = "";

        /// <summary>Sidebar conversations in the app's own order (recency first).</summary>
        public IReadOnlyList<DesktopConversation> Conversations { get; init; } = Array.Empty<DesktopConversation>();

        /// <summary>The reading when the helper failed, timed out, or returned junk.</summary>
        public static DesktopSnapshot Unavailable => new DesktopSnapshot();

        /// <summary>
        /// Parse the helper's one-line JSON. Anything unparseable degrades to
        /// <see cref="Unavailable"/> — a surprise from the helper must never become a guessed
        /// state on a key.
        /// </summary>
        public static DesktopSnapshot Parse(String json)
        {
            if (String.IsNullOrWhiteSpace(json))
            {
                return Unavailable;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!ReadBool(root, "ok") || !ReadBool(root, "surface"))
                {
                    return Unavailable;
                }

                return new DesktopSnapshot
                {
                    SurfaceAvailable = true,
                    Attention = ReadBool(root, "attention"),
                    ApprovalPresent = ReadBool(root, "approvalPresent"),
                    DenyPresent = ReadBool(root, "denyPresent"),
                    StopPresent = ReadBool(root, "stopPresent"),
                    CardText = ReadString(root, "cardText"),
                    Mode = ReadString(root, "mode"),
                    Conversations = ReadConversations(root),
                };
            }
            catch (JsonException)
            {
                return Unavailable;
            }
        }

        private static IReadOnlyList<DesktopConversation> ReadConversations(JsonElement root)
        {
            if (!root.TryGetProperty("conversations", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<DesktopConversation>();
            }

            var list = new List<DesktopConversation>();
            foreach (var item in arr.EnumerateArray())
            {
                var title = ReadString(item, "title");
                if (String.IsNullOrEmpty(title))
                {
                    continue;
                }

                list.Add(new DesktopConversation
                {
                    Title = title,
                    State = ReadString(item, "state") switch
                    {
                        "awaiting" => ConversationState.Awaiting,
                        "unread" => ConversationState.Unread,
                        "running" => ConversationState.Running,
                        _ => ConversationState.Idle,   // unknown state words degrade to idle, never to a guess
                    },
                });
            }

            return list;
        }

        private static Boolean ReadBool(JsonElement root, String name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        private static String ReadString(JsonElement root, String name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? ""
                : "";
    }
}
