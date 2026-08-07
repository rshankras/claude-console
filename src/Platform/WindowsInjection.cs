namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    /// <summary>
    /// The plugin's half of Windows injection: turn a request into an argument list for
    /// claude-console-inject.exe, and turn its exit code back into an outcome. Pure — no Win32,
    /// no process launch — so all of it is unit-tested on any OS. The Win32 work lives in the
    /// helper (tools/windows/ClaudeConsoleInject), which is the only piece needing hardware.
    ///
    /// Why a separate short-lived process at all: AttachConsole/FreeConsole mutate GLOBAL state
    /// for the calling process. LogiPluginService is a long-lived, multi-threaded GUI host, and
    /// having it repeatedly detach from and re-attach to arbitrary consoles on every keypress is
    /// asking for trouble. One process per injection — attach, write, exit — isolates that,
    /// exactly as the macOS backend isolates each osascript run.
    /// </summary>
    internal static class WindowsInjection
    {
        /// <summary>Helper exit codes. Mirrors InjectionOutcome; 5 deliberately echoes ERROR_ACCESS_DENIED.</summary>
        internal const Int32 ExitOk = 0;
        internal const Int32 ExitSessionMissing = 2;
        internal const Int32 ExitFailed = 3;
        internal const Int32 ExitSessionElevated = 5;

        /// <summary>
        /// Split a Windows session key back into the process it names.
        ///
        /// Session keys are opaque ABOVE the platform seam; this is inside the backend that minted
        /// them (see WindowsProcessWatcher.SessionKeyFor), which is the one place allowed to read
        /// their shape.
        /// </summary>
        internal static Boolean TryParseSessionKey(String sessionKey, out Int32 pid, out Int64 startTicks)
        {
            pid = 0;
            startTicks = 0;

            if (String.IsNullOrWhiteSpace(sessionKey))
            {
                return false;
            }

            // pid-<pid>-<utcTicks>
            var parts = sessionKey.Split('-');
            return parts.Length == 3
                && parts[0] == "pid"
                && Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out pid)
                && Int64.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out startTicks)
                && pid > 0;
        }

        /// <summary>
        /// Arguments for typing text. The text travels as its own argv element — never concatenated
        /// into a command string — so quotes, backslashes and ampersands in a voice transcript
        /// cannot break out of it. Same discipline as the macOS backend passing text as argv item 2.
        /// </summary>
        internal static List<String> TextArgs(String sessionKey, String text, Boolean pressEnter)
        {
            var args = BaseArgs("text", sessionKey);
            if (args == null)
            {
                return null;
            }

            args.Add("--submit");
            args.Add(pressEnter ? "true" : "false");
            args.Add("--text");
            args.Add(Flatten(text));
            return args;
        }

        /// <summary>Arguments for one key chord.</summary>
        internal static List<String> KeyArgs(String sessionKey, KeyStroke key)
        {
            var args = BaseArgs("key", sessionKey);
            if (args == null)
            {
                return null;
            }

            args.Add("--key");
            args.Add(key.Key.ToString());
            if (key.Modifiers != KeyModifiers.None)
            {
                args.Add("--mods");
                args.Add(String.Join(",", ModifierNames(key.Modifiers)));
            }
            return args;
        }

        /// <summary>Arguments for Tab-then-Return as ONE helper run (never two key runs).</summary>
        internal static List<String> TabThenEnterArgs(String sessionKey) => BaseArgs("tabenter", sessionKey);

        // Every command carries the process AND its start time. The start time is not decoration:
        // Windows recycles PIDs, so without it a stale session key could attach to an unrelated
        // process that happens to have inherited the number — and type into it. The helper
        // re-verifies before attaching; this is the guard's other half.
        private static List<String> BaseArgs(String verb, String sessionKey)
        {
            if (!TryParseSessionKey(sessionKey, out var pid, out var startTicks))
            {
                return null;
            }

            return new List<String>
            {
                verb,
                "--pid", pid.ToString(CultureInfo.InvariantCulture),
                "--start-ticks", startTicks.ToString(CultureInfo.InvariantCulture),
            };
        }

        // Newlines become spaces so a multi-line voice transcript can't submit its first line early
        // (the macOS backend flattens for the same reason).
        internal static String Flatten(String text) =>
            text?.Replace("\r", " ").Replace("\n", " ");

        internal static IEnumerable<String> ModifierNames(KeyModifiers mods)
        {
            if ((mods & KeyModifiers.Control) != 0) { yield return "Control"; }
            if ((mods & KeyModifiers.Shift) != 0) { yield return "Shift"; }
            if ((mods & KeyModifiers.Alt) != 0) { yield return "Alt"; }
            if ((mods & KeyModifiers.Command) != 0) { yield return "Command"; }
        }

        /// <summary>
        /// Turn the helper's exit code into an outcome. An unknown code is Failed, never Ok —
        /// "we don't know what happened" must not be reported as a successful keystroke.
        /// </summary>
        internal static InjectionOutcome OutcomeFor(Int32? exitCode) => exitCode switch
        {
            ExitOk => InjectionOutcome.Ok,
            ExitSessionMissing => InjectionOutcome.SessionMissing,
            ExitSessionElevated => InjectionOutcome.SessionElevated,
            _ => InjectionOutcome.Failed,
        };

        /// <summary>Human-readable note for the log when an injection didn't happen.</summary>
        internal static String Explain(InjectionOutcome outcome) => outcome switch
        {
            InjectionOutcome.SessionMissing => "that Claude session is gone (or its PID was recycled)",
            InjectionOutcome.SessionElevated =>
                "that session runs elevated — Logi Options+ is not, and Windows blocks the attach across integrity levels",
            InjectionOutcome.Failed => "the inject helper failed",
            InjectionOutcome.Unsupported => "no injection backend on this platform",
            _ => null,
        };
    }
}
