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

        [Fact]
        public void Folder_entries_route_to_full_key_widgets_with_no_host_caption()
        {
            var conversation = new DesktopConversation { Title = "தமிழ் / release___candidate" };
            var action = ActionString.FromString(Assert.Single(AllChatsDynamicFolder.ConversationActionNames(
                "VizhiDesktop", new[] { conversation })));
            Assert.Equal("VizhiDesktop", action.PluginName);
            Assert.Equal(typeof(DesktopConversationCommand).FullName, action.ActionName);
            Assert.Same(conversation, DesktopConversationCommand.FolderConversation(action.ActionParameter, new[] { conversation }));

            var widget = new DesktopConversationCommand();
            Assert.True(widget.IsWidget);
            Assert.True(widget.TryGetCommandDisplayName(action.ActionParameter, PluginImageSize.Width90, out var label));
            Assert.Equal("\u200B", label); // host must not repeat the bitmap's title below it
            Assert.Equal(PluginDynamicFolderNavigation.ButtonArea, new AllChatsDynamicFolder().GetNavigationArea(default));
        }

        [Fact]
        public void A_folder_widget_resolves_exact_identity_after_a_sidebar_reorder()
        {
            var a = new DesktopConversation { Title = "A" };
            var b = new DesktopConversation { Title = "B" };
            var binding = AllChatsDynamicFolder.ConversationActionNames("VizhiDesktop", new[] { a, b })[1];
            var parameter = ActionString.FromString(binding).ActionParameter;
            Assert.Same(b, DesktopConversationCommand.FolderConversation(parameter, new[] { b, a }));
            Assert.Null(DesktopConversationCommand.FolderConversation(parameter, new[] { a }));
            Assert.Null(DesktopConversationCommand.FolderConversation("chat:%%%", new[] { b }));
            Assert.Null(DesktopConversationCommand.FolderConversation("1", new[] { b }));
        }
    }
}
