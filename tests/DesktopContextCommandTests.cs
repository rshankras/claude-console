namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;

    using Xunit;

    public class DesktopContextCommandTests
    {
        [Fact]
        public void Primary_key_is_search_in_chatgpt_and_changes_in_codex()
        {
            var chat = DesktopContextCommand.FaceFor(
                DesktopContextCommand.Primary, "ChatGPT", DesktopControl.Search);
            var codex = DesktopContextCommand.FaceFor(
                DesktopContextCommand.Primary, "Codex", DesktopControl.Changes);

            Assert.True(chat.Enabled);
            Assert.Equal("Search", chat.Label);
            Assert.Equal(DesktopControl.Search, chat.Control);
            Assert.True(codex.Enabled);
            Assert.Equal("View Changes", codex.Label);
            Assert.Equal(DesktopControl.Changes, codex.Control);
        }

        [Fact]
        public void Codex_without_a_review_control_says_not_available_and_cannot_fire()
        {
            var face = DesktopContextCommand.FaceFor(
                DesktopContextCommand.Primary, "Codex", DesktopControl.Permissions);

            Assert.False(face.Enabled);
            Assert.Equal("View Changes", face.Label);
            Assert.Equal("Not available", face.Status);
            Assert.Equal("diff", face.Icon);
        }

        [Fact]
        public void Unknown_mode_never_guesses_an_action()
        {
            var face = DesktopContextCommand.FaceFor(
                DesktopContextCommand.Secondary1, "", (DesktopControl)(-1));

            Assert.False(face.Enabled);
            Assert.Equal(DesktopControl.None, face.Control);
            Assert.Equal("Mode?", face.Label);
        }

        [Fact]
        public void Secondary_positions_keep_semantic_pairs()
        {
            Assert.Equal("Projects", DesktopContextCommand.FaceFor(
                DesktopContextCommand.Secondary1, "ChatGPT", DesktopControl.Projects).Label);
            Assert.Equal("Permissions", DesktopContextCommand.FaceFor(
                DesktopContextCommand.Secondary1, "Codex", DesktopControl.Permissions).Label);
            Assert.Equal("Plugins", DesktopContextCommand.FaceFor(
                DesktopContextCommand.Secondary2, "ChatGPT", DesktopControl.Plugins).Label);
            Assert.Equal("Attach Files", DesktopContextCommand.FaceFor(
                DesktopContextCommand.Secondary2, "Codex", DesktopControl.AttachFiles).Label);
        }

        [Theory]
        [InlineData("primary", "ChatGPT", "Search", "search")]
        [InlineData("secondary_1", "ChatGPT", "Projects", "project")]
        [InlineData("secondary_2", "Codex", "Attach Files", "attach")]
        [InlineData("secondary_3", "ChatGPT", "Scheduled", "scheduled")]
        [InlineData("secondary_4", "Codex", "Quick Chat", "quick_chat")]
        public void Unavailable_actions_keep_their_identity(string slot, string mode, string label, string icon)
        {
            var unavailable = DesktopContextCommand.FaceFor(slot, mode, DesktopControl.None);
            var available = DesktopContextCommand.FaceFor(slot, mode, (DesktopControl)(-1));
            Assert.Equal(label, unavailable.Label);
            Assert.Equal(icon, unavailable.Icon);
            Assert.False(unavailable.Enabled);
            Assert.Equal("Unavailable", unavailable.Status);
            Assert.Equal(unavailable.Label, available.Label);
            Assert.Equal(unavailable.Icon, available.Icon);
            Assert.True(available.Enabled);
            Assert.Null(available.Status);
        }
    }
}
