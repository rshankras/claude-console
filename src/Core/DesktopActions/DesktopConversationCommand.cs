namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    /// <summary>
    /// The home page's top two rows: six keys, each ONE conversation by name — title plus what
    /// it wants from you. This is the argument for an LCD keypad over a glowing one, made
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
            if (!DesktopServices.Automation.Press(new[] { conv.Title }, out _))
            {
                PluginLog.Warning($"DesktopConversationCommand: “{conv.Title}” no longer matches — sidebar changed?");
                return;
            }

            DesktopServices.Automation.FocusApp();
            PluginLog.Info($"DesktopConversationCommand: jumped to “{conv.Title}”");
        }

        // THE LAYOUT LAW, learned by the terminal session grid over three hardware photo
        // iterations (see KeyImage.RenderSessionSlot's history): the service reserves the
        // key's bottom strip for the label, and that strip's single-line font is the largest,
        // crispest text a key can carry — while in-bitmap text at comparable size clips. The
        // first cut of these keys ignored that and drew the title INSIDE the bitmap too, so
        // every key showed its title twice, small and mangled above, truncated below.
        //
        // So identity goes where the platform is strongest: the TITLE is the service label,
        // once; the bitmap carries only STATE — amber-badged waiting glyph, green check for
        // unread, hourglass while running, plain dark for idle. Same design language as the
        // terminal grid, which is the family promise.
        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            var conv = Slot(actionParameter);
            return conv == null ? "—" : Trim(conv.Title);
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var conv = Slot(actionParameter);
            if (conv == null)
            {
                return KeyImage.RenderSessionSlot(imageSize, null, null, selected: false);
            }

            var (icon, risk) = conv.State switch
            {
                ConversationState.Awaiting => ("waiting", ApprovalRisk.Normal),   // the amber badge
                ConversationState.Unread => ("done", ApprovalRisk.None),          // green check: done, unseen
                ConversationState.Running => ("busy0", ApprovalRisk.None),
                _ => ((String)null, ApprovalRisk.None),
            };

            return KeyImage.RenderSessionSlot(imageSize, icon, null, selected: false, risk);
        }

        // The label strip is single-line; past ~24 characters the service shrinks it to mush.
        private static String Trim(String title) =>
            title.Length <= 24 ? title : title.Substring(0, 23) + "…";
    }
}
