namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>Resolve the shared toolkit, retaining compatibility with standalone dev builds.</summary>
    internal static class WindowsTools
    {
        internal const String FileName = "claude-console-tools.exe";

        internal static String PathFor(String tool, Func<String, String> resolve = null)
        {
            resolve ??= PluginPaths.PackagedFile;
            return resolve(FileName) ?? resolve($"claude-console-{tool}.exe");
        }

        internal static List<String> Arguments(String executable, String tool, IEnumerable<String> arguments)
        {
            var result = new List<String>();
            if (String.Equals(Path.GetFileName(executable), FileName, StringComparison.OrdinalIgnoreCase))
            { result.Add(tool); }
            result.AddRange(arguments);
            return result;
        }
    }
}
