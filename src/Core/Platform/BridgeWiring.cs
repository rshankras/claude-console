namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Linq;
    using System.Text.Json.Nodes;

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

        /// <summary>Substring that identifies OUR activity hook command on macOS.</summary>
        internal const String MacActivityMarker = "activity-hook.sh";

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

        /// <summary>
        /// Is this hook entry's command ours? The idempotence check for the activity hooks —
        /// it must recognise BOTH platforms' commands, or re-wiring on the unrecognised platform
        /// appends a duplicate hook entry on every plugin load. (The Windows command contains no
        /// "activity-hook.sh": the shim is one exe dispatched by verb.)
        /// </summary>
        internal static Boolean IsOurHook(String command) =>
            command != null &&
            (command.Contains(MacActivityMarker, StringComparison.OrdinalIgnoreCase) ||
             command.Contains(WindowsMarker, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Take our wiring back OUT of a settings document, and nothing else (#31).
        ///
        /// "Nothing else" is the whole point. The previous undo story was "restore the backup" — a
        /// snapshot taken once on first load and never again, so on QA's machine it was a month
        /// stale and restoring it would have rolled back every unrelated setting since. This is
        /// surgical instead: it removes only hook entries whose command is ours, collapses only the
        /// containers it emptied, and puts the status line back to what it chained (or removes it,
        /// if there was nothing to chain). A user's own hooks in the same event survive.
        /// </summary>
        /// <param name="root">The parsed settings.json; mutated in place.</param>
        /// <param name="chainedCommand">The status line command we chained, if any — recorded in the
        /// chain file at wiring time.</param>
        /// <returns>True when anything was removed, so the caller knows whether to write.</returns>
        internal static Boolean Unwire(JsonObject root, String chainedCommand)
        {
            var changed = false;

            if (root["hooks"] is JsonObject hooks)
            {
                foreach (var eventName in hooks.Select(kv => kv.Key).ToList())
                {
                    if (hooks[eventName] is not JsonArray entries)
                    {
                        continue;
                    }

                    var emptiedAnEntry = false;
                    for (var i = entries.Count - 1; i >= 0; i--)
                    {
                        if (entries[i]?["hooks"] is not JsonArray inner)
                        {
                            continue;
                        }

                        var removedHere = false;
                        for (var j = inner.Count - 1; j >= 0; j--)
                        {
                            if (IsOurHook(Str(inner[j]?["command"])))
                            {
                                inner.RemoveAt(j);
                                removedHere = true;
                                changed = true;
                            }
                        }

                        // Collapse an entry only when WE emptied it; an entry that was already
                        // empty is the user's oddity, not our leftover.
                        if (removedHere && inner.Count == 0)
                        {
                            entries.RemoveAt(i);
                            emptiedAnEntry = true;
                        }
                    }

                    if (emptiedAnEntry && entries.Count == 0)
                    {
                        hooks.Remove(eventName);
                    }
                }

                if (changed && hooks.Count == 0)
                {
                    root.Remove("hooks");
                }
            }

            if (root["statusLine"] is JsonObject sl && IsOurs(Str(sl["command"])))
            {
                if (!String.IsNullOrWhiteSpace(chainedCommand))
                {
                    sl["command"] = chainedCommand;
                    sl["type"] = "command";
                }
                else
                {
                    root.Remove("statusLine");
                }
                changed = true;
            }

            return changed;
        }

        // A command node that is not a string (a user's malformed entry) reads as "not ours".
        private static String Str(JsonNode node) =>
            node is JsonValue v && v.TryGetValue<String>(out var s) ? s : null;

        // Windows paths routinely contain spaces (the plugin lives under %LOCALAPPDATA%), and
        // Claude Code hands hook commands to a shell. Unquoted, "C:\Program Files\..." would run
        // "C:\Program" with the rest as arguments.
        private static String Quote(String path) =>
            String.IsNullOrEmpty(path) || path.StartsWith("\"", StringComparison.Ordinal)
                ? path
                : "\"" + path + "\"";
    }
}
