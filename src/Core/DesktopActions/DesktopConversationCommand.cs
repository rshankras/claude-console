namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;

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
    /// Unread (done, result unseen), Running (the app's spinner), idle. The first two are the
    /// app's own words; Running is inferred from an unnamed spinner image and says so in the
    /// adapter — a state we can't read honestly renders as idle, never as a guess.
    /// </summary>
    public class DesktopConversationCommand : PluginDynamicCommand
    {
        private const Int32 Slots = DesktopSlotMap.SlotCount;

        public DesktopConversationCommand()
            : base()
        {
            if (!DesktopServices.Declared || String.IsNullOrEmpty(DesktopServices.App.ConversationItemMarker))
            {
                return;   // an app with no readable sidebar never grows these keys
            }

            // Conversation cards own the whole LCD surface: title above, live state bar below.
            // A normal command would be inset as an icon and receive a second static label strip.
            this.SetWidget(true);

            for (var i = 1; i <= Slots; i++)
            {
                this.AddParameter(i.ToString(), $"Conversation {i}", "Conversations")
                    .SetDescription($"Sidebar conversation #{i} (most recent first) — press to open it in the app");
            }

            DesktopServices.Monitor.OnChanged += _ => this.ActionImageChanged();
        }

        private static DesktopConversation Slot(String actionParameter)
        {
            if (!DesktopServices.Declared || !Int32.TryParse(actionParameter, out var n))
            {
                return null;
            }

            var slots = DesktopServices.Monitor.Current.Slots;
            return n >= 1 && n <= slots.Count ? slots[n - 1] : null;   // null = empty slot
        }

        protected override void RunCommand(String actionParameter)
        {
            var conv = Slot(actionParameter);
            if (conv == null)
            {
                PluginLog.Info($"DesktopConversationCommand({actionParameter}): empty slot — ignored");
                return;
            }

            // Jump: open the conversation, then bring the app forward. Press first — the press
            // targets by title and needs the tree as-is; the focus is cosmetic and can't fail
            // the jump.
            if (!DesktopServices.Automation.PressConversation(conv.Title))
            {
                PluginLog.Warning($"DesktopConversationCommand: “{conv.Title}” no longer matches — sidebar changed?");
                return;
            }

            DesktopServices.Automation.FocusApp();
            PluginLog.Info($"DesktopConversationCommand: jumped to “{conv.Title}”");
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
                return KeyImage.RenderConversationSlot(
                    imageSize, null, null, KeyImage.Gray, darkText: false);
            }

            var (word, color, darkText) = FaceFor(conv.State);
            return KeyImage.RenderConversationSlot(imageSize, conv.Title, word, color, darkText);
        }

        // Desktop states are the app's own observable truths. Colour reinforces the two states
        // that matter across the room: amber wants the user; green means unseen completed work.
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
