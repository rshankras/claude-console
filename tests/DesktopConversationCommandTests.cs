namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;

    using Xunit;

    public sealed class DesktopConversationCommandTests
    {
        [Theory]
        [InlineData((int)ConversationState.Awaiting, "Allow?", true)]
        [InlineData((int)ConversationState.Running, "Thinking", false)]
        [InlineData((int)ConversationState.Unread, "Complete", false)]
        [InlineData((int)ConversationState.Idle, "Ready", false)]
        public void Conversation_state_maps_to_an_explicit_bar(
            int state, string word, bool darkText)
        {
            var face = DesktopConversationCommand.FaceFor((ConversationState)state);

            Assert.Equal(word, face.Word);
            Assert.Equal(darkText, face.DarkText);
        }

        [Theory]
        [InlineData("Short title", "Short title")]
        [InlineData("Find planned next work", "Find planned|next work")]
        [InlineData("Check Vizhi for Codex Desktop", "Check Vizhi|for Codex|Desktop")]
        [InlineData("Assess serious fall symptoms immediately", "Assess|serious fall|symptoms im…")]
        public void Conversation_titles_use_up_to_three_readable_lines(string title, string expected)
        {
            var lines = KeyImage.WrapConversationTitle(title, maxLength: 12, maxLines: 3);

            Assert.Equal(expected, string.Join("|", lines));
        }
    }
}
