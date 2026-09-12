namespace Loupedeck.ClaudeConsolePlugin.Tests;

using System;
using System.Collections.Generic;
using Loupedeck.ClaudeConsolePlugin.Platform;
using Xunit;

public class WindowsToolsTests
{
    [Theory]
    [InlineData("inject")]
    [InlineData("focus")]
    [InlineData("voice")]
    [InlineData("shot")]
    public void Shared_toolkit_wins_over_stale_standalone_helpers(string tool)
    {
        Assert.Equal("/package/claude-console-tools.exe", WindowsTools.PathFor(tool, name => "/package/" + name));
    }

    [Fact]
    public void Standalone_dev_builds_remain_usable_and_missing_helpers_stay_missing()
    {
        Assert.Equal("/package/voice.exe", WindowsTools.PathFor("voice", name => name == "claude-console-voice.exe" ? "/package/voice.exe" : null));
        Assert.Null(WindowsTools.PathFor("voice", _ => null));
    }

    [Theory]
    [InlineData("inject", "--text", "a café & 'quoted' prompt")]
    [InlineData("focus", "--pid", "1234")]
    [InlineData("voice", "--ready", "a path with spaces.ready")]
    [InlineData("shot", "--clipboard", "a path with spaces.png")]
    public void Dispatch_preserves_argument_boundaries_and_does_not_mutate_callers(string tool, string flag, string value)
    {
        var original = new List<string> { flag, value };
        Assert.Equal(new[] { tool, flag, value }, WindowsTools.Arguments("/package/claude-console-tools.exe", tool, original));
        Assert.Equal(new[] { flag, value }, WindowsTools.Arguments("/package/claude-console-" + tool + ".exe", tool, original));
        Assert.Equal(new[] { flag, value }, original);
    }
}
