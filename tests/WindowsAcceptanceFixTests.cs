namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Loupedeck.ClaudeConsolePlugin.Platform;
    using Xunit;

    public class WindowsAcceptanceFixTests
    {
        [Fact]
        public async Task Cancellation_consumes_escape_rejects_overlapping_capture_and_suppresses_racing_output()
        {
            if (!OperatingSystem.IsWindows()) return;
            var output = Path.Combine(Path.GetTempPath(), "capture-test-" + Guid.NewGuid() + ".png");
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var bridge = new WindowsPlatformBridge { ShotHelperPath = Environment.ProcessPath };
            bridge.CaptureRunner = (_, args, _) =>
            {
                var name = args[args.IndexOf("--cancel-event") + 1];
                using var cancel = EventWaitHandle.OpenExisting(name);
                started.Set();
                Assert.True(cancel.WaitOne(5000));
                // Even if a snip races the cancellation and the helper reports success,
                // the cancelled capture must never be handed to the conversation.
                File.WriteAllText(output, "racing capture");
                Assert.True(release.Wait(5000));
                return 0;
            };
            Assert.False(bridge.TryCancelScreenshot());
            var capture = Task.Run(() => bridge.CaptureScreenshotInteractive(output));
            try
            {
                Assert.True(started.Wait(5000));
                Assert.False(bridge.CaptureScreenshotInteractive(output + ".second"));
                Assert.True(bridge.TryCancelScreenshot());
                Assert.True(bridge.TryCancelScreenshot());
                release.Set();
                Assert.False(await capture);
                Assert.False(bridge.TryCancelScreenshot());
            }
            finally { release.Set(); File.Delete(output); }
        }

        [Fact]
        public void Startup_directory_is_available_without_any_hook_and_pruned_after_exit()
        {
            var start = DateTime.UtcNow;
            var rows = new[] { new WindowsProcessInfo { Pid = 123, Name = "codex.exe", CommandLine = "codex.exe", StartTime = start } };
            var bridge = new WindowsPlatformBridge(AgentProcessMatcher.CodexCli)
            {
                ProcessEnumerator = () => rows,
                DirectoryResolver = (_, _) => @"C:\Projects\new-project",
            };
            var key = Assert.Single(bridge.DiscoverSessions());
            Assert.Equal(@"C:\Projects\new-project", bridge.SessionDirectories[key]);
            rows = Array.Empty<WindowsProcessInfo>();
            Assert.Empty(bridge.DiscoverSessions());
            Assert.Empty(bridge.SessionDirectories);
        }

        [Fact]
        public void Native_directory_read_matches_process_and_rejects_wrong_generation()
        {
            if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return;
            using var self = Process.GetCurrentProcess();
            Assert.Equal(Path.GetFullPath(Environment.CurrentDirectory).TrimEnd('\\'),
                WindowsProcessDirectory.Read(self.Id, self.StartTime)?.TrimEnd('\\'), ignoreCase: true);
            Assert.Null(WindowsProcessDirectory.Read(self.Id, self.StartTime.AddTicks(1)));
            Assert.Null(WindowsProcessDirectory.Read(Int32.MaxValue, self.StartTime));
        }

        [Fact]
        public void Project_vocabulary_is_bounded_deduplicated_and_contains_spoken_names()
        {
            var names = new[] { Path.Combine("root", "mx-keypad-launch-demo"), Path.Combine("root", "gh-portable"), Path.Combine("other", "gh-portable") };
            Assert.Equal("mx keypad launch demo, gh portable", ProjectVocabulary.For(names));
            Assert.True(ProjectVocabulary.For(Enumerable.Range(0, 100).Select(n => new String('a', 100) + n)).Length <= 1000);
            Assert.Equal("", ProjectVocabulary.For(null));
        }
    }
}
