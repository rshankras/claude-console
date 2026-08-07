namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;

    /// <summary>
    /// How Claude Code is told to talk to the plugin — the `statusLine` handler and the activity
    /// hook commands merged into settings.json. Pure string construction, so the whole wiring
    /// contract is unit-tested; BridgeManager owns the merge policy (backup, chaining, idempotence).
    ///
    /// macOS runs two bash scripts extracted to the runtime home. Windows runs ONE compiled shim
    /// (claude-console-hook.exe) shipped in the plugin package, arg-dispatched between the two
    /// jobs. The shim is compiled rather than PowerShell for two reasons: it has to derive the same
    /// session key the plugin does (trivial in C#, awkward in PowerShell), and a script would drag
    /// execution policy into the critical path of every status line render.
    /// </summary>
    internal static class BridgeWiring
    {
        /// <summary>Substring that identifies OUR statusline command, so re-wiring is idempotent.</summary>
        internal const String MacMarker = "statusline-handler.sh";
        internal const String WindowsMarker = "claude-console-hook";

        /// <summary>
        /// The command Claude Code should run to render the status line.
        /// </summary>
        /// <param name="handlerPath">bash script path (macOS) or hook exe path (Windows).</param>
        internal static String StatuslineCommand(Boolean isWindows, String handlerPath) =>
            isWindows ? $"{Quote(handlerPath)} statusline" : $"bash {handlerPath}";

        /// <summary>
        /// The command Claude Code should run for an activity transition. <paramref name="state"/>
        /// is one of busy / waiting / done / permission.
        /// </summary>
        internal static String ActivityCommand(Boolean isWindows, String handlerPath, String state) =>
            isWindows ? $"{Quote(handlerPath)} activity {state}" : $"bash {handlerPath} {state}";

        /// <summary>
        /// Is this settings.json command already ours? Checked before rewriting, so a second
        /// plugin load doesn't chain our own handler to itself.
        /// </summary>
        internal static Boolean IsOurs(String command) =>
            command != null &&
            (command.Contains(MacMarker, StringComparison.OrdinalIgnoreCase) ||
             command.Contains(WindowsMarker, StringComparison.OrdinalIgnoreCase));

        // Windows paths routinely contain spaces (the plugin lives under %LOCALAPPDATA%), and
        // Claude Code hands hook commands to a shell. Unquoted, "C:\Program Files\..." would run
        // "C:\Program" with the rest as arguments.
        private static String Quote(String path) =>
            String.IsNullOrEmpty(path) || path.StartsWith("\"", StringComparison.Ordinal)
                ? path
                : "\"" + path + "\"";
    }
}
