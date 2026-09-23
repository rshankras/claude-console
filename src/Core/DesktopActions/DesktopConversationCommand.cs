namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// The home page's top row: three keys, each ONE conversation by name — title plus what
    /// it wants from you. All Chats carries the overflow without shrinking these identities.
    /// This is the argument for an LCD keypad over a glowing one, made
    /// physical: "Q3 report · needs input", not "amber means something, somewhere".
    ///
    /// Slots are STABLE (review round, 2026-08-25): a conversation claims a key on first sight
    /// and keeps it through reorders until it leaves the sidebar — DesktopSlotMap, the terminal
    /// grid's model. A hardware key that remaps under the fingers breaks the muscle memory a
    /// keypad exists to serve. Press = jump:
    /// open that conversation AND bring the app forward — the one action here that is SUPPOSED
    /// to focus, because "take me to it" is what a press on a named thing means. An empty slot
    /// renders dim and a press on it does nothing.
    ///
    /// States, in the order they out-rank each other: Awaiting (approval — the amber badge),
    /// Unread (Complete or Unread), Running (Thinking or a legacy spinner), idle. Explicit
    /// sidebar status labels take precedence over the older unnamed-image inference.
    /// </summary>
    public class DesktopConversationCommand : DesktopCommandBase
    {
        private const Int32 Slots = DesktopSlotMap.SlotCount;
        internal const String FolderParameterPrefix = "chat:";

        public DesktopConversationCommand()
            : base()
        {
            this.SetWidget(true);
            if (!DesktopServices.Declared || String.IsNullOrEmpty(DesktopServices.App.ConversationItemMarker))
            {
                return;   // an app with no readable sidebar never grows these keys
            }

            // Conversation cards own the whole LCD surface: title above, live state bar below.
            // A normal command would be inset as an icon and receive a second static label strip.
            for (var i = 1; i <= Slots; i++)
            {
                this.AddParameter(i.ToString(), $"Conversation {i}", "Conversations")
                    .SetDescription($"Sidebar conversation #{i} (most recent first) — press to open it in the app");
            }

            DesktopServices.OnMonitorChanged(_ => this.ActionImageChanged());
        }

        private static DesktopConversation Slot(String actionParameter)
        {
            if (!DesktopServices.Declared)
            {
                return null;
            }

            var slots = DesktopServices.Monitor.Current.Slots;
            if (IsFolderParameter(actionParameter))
            {
                return FolderConversation(actionParameter, DesktopServices.Monitor.Current.Conversations);
            }
            if (!Int32.TryParse(actionParameter, out var n)) { return null; }
            return n >= 1 && n <= slots.Count ? slots[n - 1] : null;   // null = empty slot
        }

        internal static Boolean IsFolderParameter(String parameter) =>
            parameter?.StartsWith(FolderParameterPrefix, StringComparison.Ordinal) == true;

        internal static DesktopConversation FolderConversation(String parameter, IReadOnlyList<DesktopConversation> conversations) =>
            IsFolderParameter(parameter) && AllChatsDynamicFolder.TryDecodeTitle(parameter.Substring(FolderParameterPrefix.Length), out var title)
                ? AllChatsDynamicFolder.VisibleConversations(conversations).FirstOrDefault(c => String.Equals(c.Title, title, StringComparison.Ordinal))
                : null;

        protected override void RunCommand(String actionParameter)
        {
            var shown = DesktopServices.Monitor?.Current;
            var slot = DesktopServices.Declared ? Slot(actionParameter) : null;
            DesktopServices.Run(() => this.RunDesktopCommand(actionParameter, shown, slot));
        }

        private void RunDesktopCommand(String actionParameter, DesktopState shown, DesktopConversation slot)
        {
            if (!DesktopServices.Declared) { return; }
            if (IsFolderParameter(actionParameter))
            {
                ExecuteFolder(actionParameter, shown.Conversations, DesktopServices.Automation,
                    () => this.Plugin.ExecuteGenericAction(ActionString.FromString(PluginDynamicFolder.NavigateUpActionName).ActionName, null, 0),
                    () => this.ActionImageChanged());
                return;
            }
            Execute(slot, DesktopServices.Automation);
        }

        internal static void ExecuteFolder(String parameter, IReadOnlyList<DesktopConversation> conversations,
            IDesktopAutomation automation, Action close, Action invalidate)
        {
            if (!IsFolderParameter(parameter)) { return; }
            AllChatsDynamicFolder.Execute(parameter.Substring(FolderParameterPrefix.Length), conversations, automation, close, invalidate);
        }

        internal static Boolean Execute(DesktopConversation conversation, IDesktopAutomation automation)
        {
            if (conversation == null || String.IsNullOrWhiteSpace(conversation.Title)) { return false; }
            if (!automation.PressConversation(conversation.Title)) { return false; }
            automation.FocusApp();
            return true;
        }

        // The full widget draws both title and state. A zero-width space suppresses the SDK's
        // registered-name fallback ("Conversation 1") without adding a duplicate bottom label.
        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            "\u200B";

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var conv = Slot(actionParameter);
            if (conv == null)
            {
                return DesktopConversationRenderer.Render(
                    imageSize, null, null, KeyImage.Gray, darkText: false);
            }

            var (word, color, darkText) = FaceFor(conv.State);
            var title = DesktopConversationLabels.Display(DesktopServices.Monitor.Current.Mode, conv.Title);
            return DesktopConversationRenderer.Render(imageSize, title, word, color, darkText);
        }

        // Desktop states are the app's own observable truths. Colour reinforces the two states
        // that matter across the room: amber wants the user; green means Complete or Unread.
        internal static (String Word, BitmapColor Color, Boolean DarkText) FaceFor(ConversationState state) =>
            state switch
            {
                ConversationState.Awaiting => ("Allow?", KeyImage.Amber, true),
                ConversationState.Running => ("Thinking", KeyImage.Gray, false),
                ConversationState.Unread => ("Complete", KeyImage.Green, false),
                _ => ("Ready", KeyImage.Gray, false),
            };
    }
}
