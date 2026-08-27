namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;

    using Xunit;

    public sealed class AllChatsDynamicFolderTests
    {
        [Theory]
        [InlineData("Q3 report")]
        [InlineData("日本語 / release___candidate")]
        [InlineData("emoji 🚀 and punctuation: #42?")]
        public void Title_tokens_round_trip_without_using_the_sdk_separator(String title)
        {
            var token = AllChatsDynamicFolder.EncodeTitle(title);

            Assert.DoesNotContain("___", token);
            Assert.True(AllChatsDynamicFolder.TryDecodeTitle(token, out var decoded));
            Assert.Equal(title, decoded);
        }

        [Fact]
        public void Invalid_tokens_fail_closed()
        {
            Assert.False(AllChatsDynamicFolder.TryDecodeTitle("%%%", out _));
            Assert.False(AllChatsDynamicFolder.TryDecodeTitle("", out _));
        }

        [Fact]
        public void Folder_membership_preserves_sidebar_order_and_collapses_duplicate_titles()
        {
            var conversations = new[]
            {
                new DesktopConversation { Title = "Newest", State = ConversationState.Running },
                new DesktopConversation { Title = "Duplicate", State = ConversationState.Idle },
                new DesktopConversation { Title = "Duplicate", State = ConversationState.Awaiting },
                new DesktopConversation { Title = "Older", State = ConversationState.Unread },
                new DesktopConversation { Title = "" },
            };

            var visible = AllChatsDynamicFolder.VisibleConversations(conversations);

            Assert.Equal(new[] { "Newest", "Duplicate", "Older" }, visible.Select(c => c.Title));
            Assert.Equal(ConversationState.Idle, visible[1].State); // first exact-title row wins
        }

        [Fact]
        public void It_is_a_real_sdk_dynamic_folder()
        {
            Assert.True(typeof(PluginDynamicFolder).IsAssignableFrom(typeof(AllChatsDynamicFolder)));
        }
    }
}
