#nullable enable
namespace Loupedeck.ClaudeConsolePlugin.Tests;

using System;
using System.Diagnostics;
using Xunit;

public class WindowsHookNativeQueryTests
{
    [WindowsFact]
    public void Command_line_lookup_preserves_unicode_and_quotes_without_starting_a_shell()
    {
        var info = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        info.ArgumentList.Add("/d");
        info.ArgumentList.Add("/q");
        info.ArgumentList.Add("/k");
        info.ArgumentList.Add("rem C:\\@openai\\codex\\café 日本語");
        using var process = Process.Start(info)!;
        try
        {
            var command = WindowsHookProcessQuery.CommandLineViaNtQuery(process.Id);
            Assert.NotNull(command);
            Assert.Contains("C:\\@openai\\codex\\café 日本語", command);
            Assert.Null(WindowsHookProcessQuery.CommandLineViaNtQuery(Int32.MaxValue));
        }
        finally
        {
            process.StandardInput.WriteLine("exit");
            process.StandardInput.Close();
            if (!process.WaitForExit(2000)) { process.Kill(entireProcessTree: true); }
        }
        Assert.Null(WindowsHookProcessQuery.CommandLineViaNtQuery(process.Id));
    }
}
