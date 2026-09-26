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

        /// <summary>One activity hook the plugin wires: the Claude Code event, its matcher, and the
        /// state word the handler is called with.</summary>
        internal readonly record struct HookSpec(String Event, String Matcher, String State);

        /// <summary>
        /// The five hooks, in wiring order — the ONE table both the wirer (BridgeManager) and the
        /// detector (Inspect) read, so "wired" and "fully wired" can never disagree about what that
        /// means. PermissionRequest fires the moment a tool needs approval and carries the tool name
        /// and its input, which is what tells a routine approval from `git push --force`;
        /// Notification can't (no tool name, ~6 s late for permission prompts). Older Claude Code
        /// builds ignore events they don't know, so the badge there simply stays amber.
        /// </summary>
        internal static readonly HookSpec[] HookSpecs =
        {
            new HookSpec("UserPromptSubmit", null, "busy"),
            new HookSpec("PostToolUse", "*", "busy"),
            new HookSpec("Notification", null, "waiting"),
            new HookSpec("Stop", null, "done"),
            new HookSpec("PermissionRequest", null, "permission"),
        };

        /// <summary>
        /// What settings.json says about our wiring. Pure: a parsed document in, a verdict out.
        /// Enabled means every hook in <see cref="HookSpecs"/> is present AND the status line is ours;
        /// Disabled means no trace of ours anywhere; NeedsRepair is everything in between — a hand
        /// edit, an older layout, a partial write. (Precedent: CodexBridgeStatus.)
        /// </summary>
        internal static LiveStatusWiring Inspect(JsonObject root)
        {
            if (root == null)
            {
                return LiveStatusWiring.Disabled;   // unreadable: the caller cannot repair it either
            }

            var hooks = root["hooks"] as JsonObject;
            var statusOurs = root["statusLine"] is JsonObject sl && IsOurs(Str(sl["command"]));

            var specsPresent = HookSpecs.Count(spec => EventCarriesOurs(hooks, spec.Event));
            if (specsPresent == HookSpecs.Length && statusOurs)
            {
                return LiveStatusWiring.Enabled;
            }

            var anyOurs = statusOurs || (hooks != null && hooks.Any(kv => EventCarriesOurs(hooks, kv.Key)));
            return anyOurs ? LiveStatusWiring.NeedsRepair : LiveStatusWiring.Disabled;
        }

        private static Boolean EventCarriesOurs(JsonObject hooks, String eventName)
        {
            if (hooks?[eventName] is not JsonArray entries)
            {
                return false;
            }
            foreach (var entry in entries)
            {
                if (entry?["hooks"] is not JsonArray inner)
                {
                    continue;
                }
                foreach (var h in inner)
                {
                    if (IsOurHook(Str(h?["command"])))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// What the live keys show. The file's verdict plus two facts only the engine knows: whether
        /// the user chose Off (the marker file), and whether Enable just wrote and no session has
        /// reported since (so "Restart Claude" is the honest face, not a dash).
        /// </summary>
        internal static LiveStatusState DisplayState(LiveStatusWiring wiring, Boolean optOutMarker, Boolean justEnabled) =>
            wiring switch
            {
                LiveStatusWiring.Enabled => justEnabled ? LiveStatusState.JustEnabled : LiveStatusState.Enabled,
                LiveStatusWiring.NeedsRepair => LiveStatusState.NeedsRepair,
                _ => optOutMarker ? LiveStatusState.Off : LiveStatusState.NotEnabled,
            };

        /// <summary>
        /// The command Claude Code should run to render the status line.
        ///
        /// On both platforms the command checks that the handler still exists before running it. An Options+
        /// uninstall now removes owned Windows wiring, but manual deletion or quarantine can still
        /// remove a handler while its command remains. The Windows guard records that failure for
        /// the plugin without printing into the user's status line; the handler's own exit code
        /// still propagates when it is there. Keep the encoded PowerShell launcher until a simpler
        /// launcher has passed the Windows/Git Bash compatibility tests (#122).
        /// </summary>
        /// <param name="handlerPath">bash script path (macOS) or hook exe path (Windows).</param>
        internal static String StatuslineCommand(Boolean isWindows, String handlerPath) =>
            isWindows
                ? WindowsCommand(handlerPath, "statusline")
                : $"[ ! -f {Quote(handlerPath)} ] || bash {Quote(handlerPath)}";

        /// <summary>
        /// The command Claude Code should run for an activity transition. <paramref name="state"/>
        /// is one of busy / waiting / done / permission. Same missing-script guard as the status
        /// line on both platforms (#55).
        /// </summary>
        internal static String ActivityCommand(Boolean isWindows, String handlerPath, String state) =>
            isWindows
                ? WindowsCommand(handlerPath, "activity", state)
                : $"[ ! -f {Quote(handlerPath)} ] || bash {Quote(handlerPath)} {state}";

        /// <summary>
        /// Upgrade commands that are already recognisably ours to the current guarded form, without
        /// adding any wiring or touching a user's entries. This is intentionally narrower than an
        /// enable/repair: existing 2.2.0 installs need the #55 missing-handler guard without making
        /// installation itself an opt-in settings write. Returns whether the document changed.
        /// </summary>
        internal static Boolean UpgradeOwnedCommands(
            JsonObject root,
            Boolean isWindows,
            String statusHandler,
            String activityHandler)
        {
            var changed = false;

            if (root?["statusLine"] is JsonObject statusLine &&
                IsOurs(Str(statusLine["command"])))
            {
                var desired = StatuslineCommand(isWindows, statusHandler);
                if (!String.Equals(Str(statusLine["command"]), desired, StringComparison.Ordinal))
                {
                    statusLine["command"] = desired;
                    changed = true;
                }
            }

            if (root?["hooks"] is not JsonObject hooks)
            {
                return changed;
            }

            foreach (var spec in HookSpecs)
            {
                if (hooks[spec.Event] is not JsonArray entries)
                {
                    continue;
                }

                var desired = ActivityCommand(isWindows, activityHandler, spec.State);
                foreach (var entry in entries)
                {
                    if (entry?["hooks"] is not JsonArray inner)
                    {
                        continue;
                    }

                    foreach (var node in inner)
                    {
                        if (node is JsonObject hook && IsOurHook(Str(hook["command"])) &&
                            !String.Equals(Str(hook["command"]), desired, StringComparison.Ordinal))
                        {
                            hook["command"] = desired;
                            changed = true;
                        }
                    }
                }
            }

            return changed;
        }

        /// <summary>
        /// Is this settings.json command already ours? Checked before rewriting, so a second
        /// plugin load doesn't chain our own handler to itself.
        /// </summary>
        internal static Boolean IsOurs(String command) =>
            command != null &&
            (command.Contains(MacMarker, StringComparison.OrdinalIgnoreCase) ||
             command.Contains(WindowsMarker, StringComparison.OrdinalIgnoreCase) || IsWindowsLauncher(command));

        /// <summary>
        /// Is this hook entry's command ours? The idempotence check for the activity hooks —
        /// it must recognise BOTH platforms' commands, or re-wiring on the unrecognised platform
        /// appends a duplicate hook entry on every plugin load. (The Windows command contains no
        /// "activity-hook.sh": the shim is one exe dispatched by verb.)
        /// </summary>
        internal static Boolean IsOurHook(String command) =>
            command != null &&
            (command.Contains(MacActivityMarker, StringComparison.OrdinalIgnoreCase) ||
             command.Contains(WindowsMarker, StringComparison.OrdinalIgnoreCase) || IsWindowsLauncher(command));

        // Git Bash rewrites cmd.exe /d and /c as paths, leaving cmd interactive and making
        // it execute the JSON payload. EncodedCommand passes literal PowerShell source through
        // Bash, PowerShell and cmd without another round of path or quote interpretation.
        internal static String WindowsCommand(String handlerPath, params String[] arguments)
        {
            var path = handlerPath.Trim('"');
            String Literal(String value) => "'" + value.Replace("'", "''") + "'";
            // The health directory is one hash of the helper path. It is computed HERE, once, and
            // baked into the launcher as a literal: the six hook entries in settings.json are
            // already six copies of this script, and every line of PowerShell in them is a line a
            // security product gets to dislike. No hashing, no JSON module, no file enumeration
            // cmdlets in the hot path — the exe computes the same name from its own path
            // (WindowsHookHealth.HealthDirectoryName), and the plugin from the package path.
            var health = WindowsHookHealth.HealthDirectoryName(path);
            var script = "# " + WindowsMarker + "\n" +
                "$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); " +
                "[Console]::InputEncoding = [Text.UTF8Encoding]::new($false); " +
                $"$helper = {Literal(path)}; " +
                // A marker is immutable so concurrent hooks cannot replace a newer failure with
                // an older one. The launcher only writes; the plugin (which reads this directory
                // on every poll) and the exe keep it trimmed to the newest 16. Scope "helper"
                // moves the plugin's recovery barrier (the exe may not have run); "delivery" is
                // diagnostic only. Reasons are fixed literals, so the hand-built JSON is safe.
                "function Write-HookFailure([string]$reason, [string]$scope) { try { " +
                $"$dir = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'claude-console', 'hook-health', '{health}'); " +
                "[IO.Directory]::CreateDirectory($dir) | Out-Null; " +
                "$ticks = [DateTime]::UtcNow.Ticks; " +
                "$file = [IO.Path]::Combine($dir, 'failure-' + $ticks + '-' + [Guid]::NewGuid().ToString('N') + '.json'); " +
                "[IO.File]::WriteAllText($file + '.tmp', '{\"schema\":1,\"observedUtcTicks\":' + $ticks + ',\"reason\":\"' + $reason + '\",\"scope\":\"' + $scope + '\"}', [Text.UTF8Encoding]::new($false)); " +
                "[IO.File]::Move($file + '.tmp', $file) " +
                "} catch {} }; " +
                "if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) { Write-HookFailure 'missing' 'helper'; exit 0 }; " +
                "try { $read = [Console]::In.ReadToEndAsync(); if (-not $read.Wait(5000)) { Write-HookFailure 'input-timeout' 'delivery'; exit 0 }; " +
                // ErrorActionPreference makes native start failures catchable; native nonzero
                // exits still flow through LASTEXITCODE and retain their original exit code.
                "$ErrorActionPreference = 'Stop'; $LASTEXITCODE = 0; " +
                "$read.Result | & $helper " +
                String.Join(" ", arguments.Select(Literal)) + "; $code = $LASTEXITCODE; " +
                "if ($code -ne 0) { Write-HookFailure 'nonzero-exit' 'helper' }; exit $code " +
                "} catch { Write-HookFailure 'launch-failed' 'helper'; exit 1 }";
            return "powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand " +
                Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        }

        private static Boolean IsWindowsLauncher(String command)
        {
            const String prefix = "powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand ";
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return false; }
            try
            {
                var source = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(command[prefix.Length..]));
                return source.StartsWith("# " + WindowsMarker + "\n", StringComparison.Ordinal);
            }
            catch (FormatException) { return false; }
        }

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
        internal static String Str(JsonNode node) =>
            node is JsonValue v && v.TryGetValue<String>(out var s) ? s : null;

        // Windows paths routinely contain spaces (the plugin lives under %LOCALAPPDATA%), and
        // Claude Code hands hook commands to a shell. Unquoted, "C:\Program Files\..." would run
        // "C:\Program" with the rest as arguments.
        private static String Quote(String path) =>
            String.IsNullOrEmpty(path) || path.StartsWith("\"", StringComparison.Ordinal)
                ? path
                : "\"" + path + "\"";
    }

    /// <summary>The file's verdict on our wiring — see BridgeWiring.Inspect.</summary>
    public enum LiveStatusWiring
    {
        Disabled = 0,
        NeedsRepair = 1,
        Enabled = 2,
    }

    /// <summary>What the live keys show — see BridgeWiring.DisplayState and LiveStatusFace.</summary>
    public enum LiveStatusState
    {
        NotEnabled,     // never wired — "Set up"
        Off,            // the user turned it off — "Off"
        NeedsRepair,    // partly wired — "Set up" (Enable completes the set)
        JustEnabled,    // wired this session, no data yet — "Restart Claude"
        Enabled,        // the keys show their live values
    }
}
