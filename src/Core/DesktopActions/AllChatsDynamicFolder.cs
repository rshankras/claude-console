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
        private Boolean _active, _cleanupRegistered;

        public AllChatsDynamicFolder()
        {
            // SDK rule: constructors set metadata only. Monitor subscriptions begin in Activate.
            this.DisplayName = "Chats";
            this.Description = "Recent conversations visible in the app sidebar; use Find Chat for older conversations";
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

            if (!_cleanupRegistered)
            {
                DesktopServices.Lifetime.OnStop(() => Deactivate());
                _cleanupRegistered = true;
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
            ConversationActionNames(this.Plugin.Name, CurrentConversations());

        // Folder-generated commands are not widgets: the host shrinks their image and adds a
        // caption. Return our existing full-key widget instead, with an exact-title parameter.
        internal static String[] ConversationActionNames(String pluginName, IReadOnlyList<DesktopConversation> conversations) =>
            VisibleConversations(conversations).Select(c => ActionString.ToString(pluginName,
                typeof(DesktopConversationCommand).FullName,
                DesktopConversationCommand.FolderParameterPrefix + EncodeTitle(c.Title))).ToArray();

        public override String GetButtonDisplayName(PluginImageSize imageSize) => "Chats";

        public override BitmapImage GetButtonImage(PluginImageSize imageSize) =>
            KeyImage.Render(imageSize, "Chats", KeyImage.Blue, "all_chats");

        public override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            "\u200B"; // suppress a duplicate caption even for an already-open legacy folder

        public override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (!TryDecodeTitle(actionParameter, out var title))
            {
                return DesktopConversationRenderer.Render(imageSize, null, null, KeyImage.Gray, darkText: false);
            }

            var conversation = VisibleConversations(CurrentConversations())
                .FirstOrDefault(c => String.Equals(c.Title, title, StringComparison.Ordinal));
            if (conversation == null)
            {
                return DesktopConversationRenderer.Render(imageSize, null, null, KeyImage.Gray, darkText: false);
            }

            // The same card the page-1 conversation keys draw, so a chat looks the same on both
            // surfaces: measured title lines with a compact state footer. FaceFor already yields
            // exactly those inputs; the old icon-plus-badge face was the pre-redesign session look.
            var (word, color, darkText) = DesktopConversationCommand.FaceFor(conversation.State);
            return DesktopConversationRenderer.Render(imageSize, DisplayTitle(title), word, color, darkText);
        }

        public override void RunCommand(String actionParameter)
        {
            if (!DesktopServices.Declared) { return; }
            Execute(actionParameter, CurrentConversations(), DesktopServices.Automation,
                () => this.Close(), () => this.ButtonActionNamesChanged());
        }

        internal static void Execute(String actionParameter, IReadOnlyList<DesktopConversation> conversations,
            IDesktopAutomation automation, Action close, Action invalidate)
        {
            if (!TryDecodeTitle(actionParameter, out var title)) { return; }
            var conversation = VisibleConversations(conversations)
                .FirstOrDefault(c => String.Equals(c.Title, title, StringComparison.Ordinal));
            if (conversation == null) { invalidate(); return; }
            if (DesktopConversationCommand.Execute(conversation, automation)) { close(); }
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

        private static String DisplayTitle(String title) => DesktopConversationLabels.Display(
            DesktopServices.Declared ? DesktopServices.Monitor.Current.Mode : "", title);

        private void OnDesktopChanged(DesktopState _)
        {
            // The list, state glyphs, or both may have changed. Dynamic folders auto-page the
            // returned actions. Explicit image invalidation keeps state changes live even when
            // membership is unchanged (Running -> Awaiting -> Unread on the same title).
            // DesktopConversationCommand invalidates its widgets on the same monitor event.
            this.ButtonActionNamesChanged();
        }
    }
}
