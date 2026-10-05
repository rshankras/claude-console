namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;

    public class DesktopBusyFeedbackTests
    {
        [Fact]
        public void Feedback_expires_per_key_and_another_press_extends_only_that_key()
        {
            Int64 now = 0; var changes = 0;
            using var feedback = new DesktopBusyFeedback(() => changes++, () => now);
            feedback.Show("approve"); feedback.Show("deny");
            now = 1000; feedback.Show("approve");
            now = DesktopBusyFeedback.HoldMs; feedback.Expire();
            Assert.True(feedback.Contains("approve")); Assert.False(feedback.Contains("deny"));
            now = 2800; feedback.Expire(); Assert.False(feedback.Contains("approve"));
            Assert.True(changes >= 5);
            feedback.Show("send"); feedback.Dispose(); Assert.False(feedback.Contains("send"));
        }

        [Fact]
        public void Every_action_reports_rejection_without_queuing_or_touching_the_app()
        {
            using var home = new TempHome();
            var app = new DesktopCommandRig.Automation();
            DesktopServices.Declare(new OpenAiDesktopAdapter(), app, new DesktopMonitor(app));
            var queue = new Queue<Action>(); DesktopServices.Actions.Schedule = queue.Enqueue;
            try
            {
                Assert.True(DesktopServices.Run(() => { }));
                var commands = new (DesktopCommandBase Command, String Parameter)[]
                {
                    (new DesktopApprovalCommand(), "approve"), (new DesktopApprovalCommand(), "deny"),
                    (new DesktopComposerCommand(), "send"), (new DesktopControlCommand(), "new_chat"),
                    (new DesktopConversationCommand(), "1"), (new DesktopContextCommand(), "clipboard"),
                    (new DesktopFileCommand(), "attach"), (new DesktopSearchCommand(), "speak"),
                    (new DesktopVoiceChatCommand(), "voice"), (new DesktopVoiceCommand(), "voice"),
                    (new DesktopVoiceDraftCommand(), "draft"), (new DesktopWorkflowCommand(), "review"),
                    (new DesktopCaptureCommand(), "clipboard"), (new DesktopToolsCommand(), "copy"),
                    (new DesktopNavigateCommand(), "find")
                };
                foreach (var (command, parameter) in commands)
                {
                    if (command is DesktopToolsCommand)
                    {
                        var shown = (System.Collections.Concurrent.ConcurrentDictionary<String, DesktopState>)
                            command.GetType().GetField("_shown", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(command);
                        shown[parameter] = new DesktopState { Mode = "ChatGPT" };
                    }
                    if (command is DesktopNavigateCommand)
                        command.GetType().GetField("_shownMode", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(command, "ChatGPT");
                    for (var i = 0; i < 4; i++) Press(command, parameter);
                    Assert.True(command.ShowsBusy(parameter), command.GetType().Name);
                    Assert.False(command.ShowsBusy("unrelated"));
                }
                Assert.Single(queue); Assert.Empty(app.Calls);
                queue.Dequeue()(); Assert.Empty(queue); Assert.Empty(app.Calls);
                // No rejected approval/send becomes a deferred action. A fresh tap is required.
                Press(commands[2].Command, "send");
                Assert.Single(queue); Assert.False(commands[2].Command.ShowsBusy("send"));
                DesktopServices.Actions.Stop(); queue.Dequeue()(); Assert.Empty(app.Calls);
                DesktopServices.Lifetime.Stop();
                foreach (var (command, parameter) in commands) Assert.False(command.ShowsBusy(parameter));
            }
            finally { DesktopServices.Actions.Stop(); DesktopServices.Lifetime.Dispose(); }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Busy_folders_stay_on_a_retry_page_until_an_explicit_retry(Boolean search)
        {
            using var home = new TempHome();
            var app = new DesktopCommandRig.Automation();
            DesktopServices.Declare(new OpenAiDesktopAdapter(), app, new DesktopMonitor(app));
            var queue = new Queue<Action>(); DesktopServices.Actions.Schedule = queue.Enqueue;
            DesktopActionFolder folder = search ? new FindChatDynamicFolder() : new DesktopFilesDynamicFolder();
            try
            {
                Assert.True(DesktopServices.Run(() => { }));
                Assert.True(folder.Activate()); Assert.True(folder.RetryPending);
                folder.RunCommand("retry"); Assert.True(folder.RetryPending); Assert.Single(queue);
                queue.Dequeue()(); Assert.True(folder.RetryPending); Assert.Empty(app.Calls);
                folder.RunCommand("retry"); Assert.False(folder.RetryPending); Assert.Single(queue);
                folder.Deactivate(); queue.Dequeue()(); Assert.Empty(app.Calls);
            }
            finally { folder.Deactivate(); DesktopServices.Actions.Stop(); DesktopServices.Lifetime.Dispose(); }
        }

        private static void Press(DesktopCommandBase command, String parameter) =>
            command.GetType().GetMethod("RunCommand", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(command, new Object[] { parameter });

        private sealed class Probe : DesktopCommandBase
        {
            internal void Tap() => RunDesktopAction("key", () => { });
            protected override BitmapImage GetDesktopCommandImage(String parameter, PluginImageSize size) => null;
        }

        [Fact]
        public void Accepted_press_does_not_clear_feedback_from_a_later_rejected_press()
        {
            using var home = new TempHome(); var app = new DesktopCommandRig.Automation();
            DesktopServices.Declare(new OpenAiDesktopAdapter(), app, new DesktopMonitor(app));
            var command = new Probe(); Action pending = null;
            DesktopServices.Actions.Schedule = work => { pending = work; command.Tap(); };
            try
            {
                command.Tap(); Assert.True(command.ShowsBusy("key"));
                pending(); Assert.True(command.ShowsBusy("key"));
            }
            finally { DesktopServices.Actions.Stop(); DesktopServices.Lifetime.Dispose(); }
        }
    }
}
