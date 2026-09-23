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
        public Boolean CanSend { get; init; }
        public Boolean CanCopyAnswer { get; init; }
        public String CopyAnswerError { get; init; } = "";
        public DesktopVoiceState VoiceChat { get; init; }
        public String CardText { get; init; } = "";
        public String Mode { get; init; } = "";
        public DesktopControl AvailableControls { get; init; }

        /// <summary>Sidebar conversations in the app's own order (recency first).</summary>
        public IReadOnlyList<DesktopConversation> Conversations { get; init; } = Array.Empty<DesktopConversation>();

        /// <summary>Why the surface is unavailable — distinct truths deserve distinct faces
        /// (review round: fail closed, and say why). Empty when the surface is up.</summary>
        public String UnavailableReason { get; init; } = "";

        /// <summary>The reading when the helper failed, timed out, or returned junk.</summary>
        public static DesktopSnapshot Unavailable => new DesktopSnapshot { UnavailableReason = "no-signal" };

        private static DesktopSnapshot UnavailableBecause(String reason) =>
            new DesktopSnapshot { UnavailableReason = reason };

        /// <summary>
        /// Parse the helper's one-line JSON. Anything unparseable degrades to
        /// <see cref="Unavailable"/> — a surprise from the helper must never become a guessed
        /// state on a key.
        /// </summary>
        public static DesktopSnapshot Parse(String json)
        {
            if (String.IsNullOrWhiteSpace(json))
            {
                return Unavailable;   // helper never ran, timed out, or was killed
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!ReadBool(root, "ok"))
                {
                    // The helper said WHY (its fail() always names the error) — keep the truth.
                    return ReadString(root, "error") switch
                    {
                        "not-trusted" => UnavailableBecause("no-permission"),
                        "app-not-running" => UnavailableBecause("not-running"),
                        _ => Unavailable,
                    };
                }

                if (!ReadBool(root, "surface"))
                {
                    return UnavailableBecause("hidden");   // screen locked / window gone
                }

                return new DesktopSnapshot
                {
                    SurfaceAvailable = true,
                    Attention = ReadBool(root, "attention"),
                    ApprovalPresent = ReadBool(root, "approvalPresent"),
                    DenyPresent = ReadBool(root, "denyPresent"),
                    StopPresent = ReadBool(root, "stopPresent"),
                    CanSend = ReadBool(root, "canSend"),
                    CanCopyAnswer = ReadBool(root, "canCopyAnswer"),
                    CopyAnswerError = ReadString(root, "copyAnswerError"),
                    VoiceChat = ReadString(root, "voiceChat") switch
                    {
                        "ready" => DesktopVoiceState.Ready,
                        "active" => DesktopVoiceState.Active,
                        _ => DesktopVoiceState.Unavailable,
                    },
                    CardText = ReadString(root, "cardText"),
                    Mode = ReadString(root, "mode"),
                    AvailableControls = ReadControls(root),
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
                    Selected = String.Equals(ReadString(item, "selected"), "true", StringComparison.Ordinal),
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

        private static DesktopControl ReadControls(JsonElement root)
        {
            var controls = DesktopControl.None;
            if (ReadBool(root, "searchPresent")) controls |= DesktopControl.Search;
            if (ReadBool(root, "changesPresent")) controls |= DesktopControl.Changes;
            if (ReadBool(root, "projectsPresent")) controls |= DesktopControl.Projects;
            if (ReadBool(root, "pluginsPresent")) controls |= DesktopControl.Plugins;
            if (ReadBool(root, "attachFilesPresent")) controls |= DesktopControl.AttachFiles;
            if (ReadBool(root, "permissionsPresent")) controls |= DesktopControl.Permissions;
            if (ReadBool(root, "scheduledPresent")) controls |= DesktopControl.Scheduled;
            if (ReadBool(root, "pullRequestsPresent")) controls |= DesktopControl.PullRequests;
            if (ReadBool(root, "explorePresent")) controls |= DesktopControl.Explore;
            if (ReadBool(root, "quickChatPresent")) controls |= DesktopControl.QuickChat;
            return controls;
        }

        private static Boolean ReadBool(JsonElement root, String name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        private static String ReadString(JsonElement root, String name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? ""
                : "";
    }
}
