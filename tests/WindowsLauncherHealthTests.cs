namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using Loupedeck.ClaudeConsolePlugin.Platform;
    using Xunit;

    public class WindowsLauncherHealthTests
    {
        [Fact]
        public void Failure_reporting_keeps_path_and_arguments_literal_across_shells()
        {
            const string path = @"C:\Users\O'Brien\café & $ tools\claude-console-hook.exe";
            var command = BridgeWiring.WindowsCommand(path, "activity", "permission");
            var source = WindowsHookTests.DecodeLauncher(command);
            Assert.StartsWith("powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand ", command);
            Assert.Contains(@"'C:\Users\O''Brien\café & $ tools\claude-console-hook.exe'", source);
            Assert.Contains("'activity' 'permission'", source);
            Assert.True(BridgeWiring.IsOurs(command));
            Assert.True(BridgeWiring.IsOurHook(command));
            Assert.Contains("ReadToEndAsync()", source);
            Assert.Contains("$read.Wait(5000)", source);
            Assert.Contains("UTF8Encoding", source);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(260)]
        public void Encoded_launcher_leaves_room_under_the_cmd_command_line_limit(int pathLength)
        {
            const string file = @"\claude-console-hook.exe";
            var path = @"C:\" + new string('x', pathLength - 3 - file.Length) + file;
            var command = BridgeWiring.ActivityCommand(true, path, "permission");
            // cmd.exe's 8191-character limit includes surrounding shell syntax such as stdin
            // redirection. Leave headroom; long Windows profiles must not lose all hooks simply
            // because failure reporting made the encoded launcher too large.
            Assert.InRange(command.Length, 1, 7600);
            var source = WindowsHookTests.DecodeLauncher(command);
            Assert.Equal(1, source.Split(path).Length - 1);
        }
    }
}
