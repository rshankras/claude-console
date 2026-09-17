namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Xunit;

    public class DesktopConversationLabelsTests
    {
        [Fact]
        public void Aliases_are_exact_mode_scoped_and_do_not_change_the_target()
        {
            var labels = DesktopConversationLabels.Parse("""
                {"Codex":{"Ship the September release":"Release"},"ChatGPT":{"Ship the September release":"Writing"}}
                """);
            var conversation = new DesktopConversation { Title = "Ship the September release" };
            Assert.Equal("Release", labels.Resolve("Codex", conversation.Title));
            Assert.Equal("Writing", labels.Resolve("ChatGPT", conversation.Title));
            Assert.Equal("Ship the September release", conversation.Title);
            Assert.Equal(conversation.Title, labels.Resolve("", conversation.Title));
            Assert.Equal("Ship the September release notes", labels.Resolve("Codex", "Ship the September release notes"));
        }

        [Theory]
        [InlineData("garbage")]
        [InlineData("[]")]
        [InlineData("null")]
        [InlineData("{\"Codex\":null}")]
        [InlineData("{\"Codex\":{\"Original\":\" \"}}")]
        [InlineData("{\"Codex\":{\"Original\":\"Release\",\"Other\":\" release \"}}")]
        public void Invalid_empty_or_ambiguous_aliases_keep_the_original(String json)
        {
            Assert.Equal("Original", DesktopConversationLabels.Parse(json).Resolve("Codex", "Original"));
        }
    }
}
