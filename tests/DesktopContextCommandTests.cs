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
            Assert.Equal("Changes", codex.Label);
            Assert.Equal(DesktopControl.Changes, codex.Control);
        }

        [Fact]
        public void Codex_without_a_diff_says_no_changes_and_cannot_fire()
        {
            var face = DesktopContextCommand.FaceFor(
                DesktopContextCommand.Primary, "Codex", DesktopControl.Permissions);

            Assert.False(face.Enabled);
            Assert.Equal("No Changes", face.Label);
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
    }
}
