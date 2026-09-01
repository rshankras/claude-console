namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// Overflow for the three stable conversation keys on the home page. The AX helper reports
    /// up to eight sidebar conversations; the three most useful stay one press away on page 1,
    /// while this folder makes the rest reachable without shrinking every title on the hardware.
    ///
    /// Folder action parameters carry the conversation TITLE, base64url-encoded. An index would
    /// be unsafe: the app orders its sidebar by recency, so a reorder between render and press
    /// could open a different conversation. At press time the title is decoded and re-validated
    /// against the latest monitor state. Missing means no-op, never "whatever is now in slot 4".
    /// </summary>
    public sealed class AllChatsDynamicFolder : PluginDynamicFolder
    {
        private Boolean _active;

        public AllChatsDynamicFolder()
        {
            // SDK rule: constructors set metadata only. Monitor subscriptions begin in Activate.
            this.DisplayName = "All Chats";
            this.Description = "Open any visible ChatGPT or Codex conversation";
            this.GroupName = "Conversations";
        }

        public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType _) =>
            PluginDynamicFolderNavigation.ButtonArea;

        public override Boolean Activate()
        {
            if (!DesktopServices.Declared)
            {
                return false;
            }

            if (!_active)
            {
                DesktopServices.Monitor.OnChanged += this.OnDesktopChanged;
                _active = true;
            }

            this.ButtonActionNamesChanged();
            return true;
        }

        public override Boolean Deactivate()
        {
            if (_active && DesktopServices.Declared)
            {
                DesktopServices.Monitor.OnChanged -= this.OnDesktopChanged;
            }

            _active = false;
            return true;
        }

        public override IEnumerable<String> GetButtonPressActionNames(DeviceType _) =>
            VisibleConversations(CurrentConversations())
                .Select(c => this.CreateCommandName(EncodeTitle(c.Title)))
                .ToArray();

        public override String GetButtonDisplayName(PluginImageSize imageSize) => "All Chats";

        public override BitmapImage GetButtonImage(PluginImageSize imageSize) =>
            KeyImage.RenderDesktop(imageSize, "All Chats", "all_chats");

        public override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            TryDecodeTitle(actionParameter, out var title) ? Trim(title) : "Unavailable";

        public override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (!TryDecodeTitle(actionParameter, out var title))
            {
                return KeyImage.RenderSessionSlot(imageSize, null, null, selected: false);
            }

            var conversation = VisibleConversations(CurrentConversations())
                .FirstOrDefault(c => String.Equals(c.Title, title, StringComparison.Ordinal));
            if (conversation == null)
            {
                return KeyImage.RenderSessionSlot(imageSize, null, null, selected: false);
            }

            var (icon, risk) = conversation.State switch
            {
                ConversationState.Awaiting => ("waiting", ApprovalRisk.Normal),
                ConversationState.Unread => ("done", ApprovalRisk.None),
                ConversationState.Running => ("busy0", ApprovalRisk.None),
                _ => ((String)null, ApprovalRisk.None),
            };

            return KeyImage.RenderSessionSlot(imageSize, icon, null, selected: false, risk);
        }

        public override void RunCommand(String actionParameter)
        {
            if (!DesktopServices.Declared || !TryDecodeTitle(actionParameter, out var title))
            {
                return;
            }

            // Re-validate identity against the latest snapshot. A stale folder button must not
            // turn into a press on whichever conversation inherited its old position.
            var conversation = VisibleConversations(CurrentConversations())
                .FirstOrDefault(c => String.Equals(c.Title, title, StringComparison.Ordinal));
            if (conversation == null)
            {
                PluginLog.Info($"AllChatsDynamicFolder: '{title}' is no longer visible — ignored");
                this.ButtonActionNamesChanged();
                return;
            }

            if (!DesktopServices.Automation.Press(new[] { conversation.Title }, out _))
            {
                PluginLog.Warning($"AllChatsDynamicFolder: '{conversation.Title}' no longer matches — sidebar changed?");
                return;
            }

            DesktopServices.Automation.FocusApp();
            this.Close();
            PluginLog.Info($"AllChatsDynamicFolder: jumped to '{conversation.Title}'");
        }

        internal static IReadOnlyList<DesktopConversation> VisibleConversations(
            IReadOnlyList<DesktopConversation> conversations)
        {
            var result = new List<DesktopConversation>();
            var seen = new HashSet<String>(StringComparer.Ordinal);
            foreach (var conversation in conversations ?? Array.Empty<DesktopConversation>())
            {
                if (conversation != null
                    && !String.IsNullOrWhiteSpace(conversation.Title)
                    && seen.Add(conversation.Title))
                {
                    result.Add(conversation);
                }
            }

            return result;
        }

        internal static String EncodeTitle(String title) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(title ?? ""))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        internal static Boolean TryDecodeTitle(String token, out String title)
        {
            title = null;
            if (String.IsNullOrEmpty(token))
            {
                return false;
            }

            try
            {
                var base64 = token.Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
                title = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                return !String.IsNullOrEmpty(title);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static IReadOnlyList<DesktopConversation> CurrentConversations() =>
            DesktopServices.Declared
                ? DesktopServices.Monitor.Current.Conversations
                : Array.Empty<DesktopConversation>();

        private static String Trim(String title) =>
            title.Length <= 24 ? title : title.Substring(0, 23) + "…";

        private void OnDesktopChanged(DesktopState _)
        {
            // The list, state glyphs, or both may have changed. Dynamic folders auto-page the
            // returned actions. Explicit image invalidation keeps state changes live even when
            // membership is unchanged (Running -> Awaiting -> Unread on the same title).
            foreach (var conversation in VisibleConversations(CurrentConversations()))
            {
                this.CommandImageChanged(EncodeTitle(conversation.Title));
            }
            this.ButtonActionNamesChanged();
        }
    }
}
