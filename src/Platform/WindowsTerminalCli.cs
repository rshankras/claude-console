namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Builds Windows Terminal command lines. Pure, so the whole navigation surface is unit-tested
    /// without Windows; running them is BoundedProcess's job.
    ///
    /// `wt.exe` is the only supported way to drive Windows Terminal from outside — there is no
    /// automation API. That shapes what the keypad can and cannot do here; see the note on focus
    /// in WindowsPlatformBridge.
    /// </summary>
    internal static class WindowsTerminalCli
    {
        internal const String Exe = "wt.exe";

        /// <summary>Filesystem probe — injectable so command resolution is testable anywhere.</summary>
        internal static Func<String, Boolean> FileExists { get; set; } = System.IO.File.Exists;

        /// <summary>
        /// What to tell Windows Terminal to run for a Claude tab.
        ///
        /// NOT the bare word "claude": wt resolves its command against the environment it
        /// inherited, and we spawn wt from LogiPluginService — whose PATH predates (or simply
        /// lacks) the installer's %USERPROFILE%\.local\bin entry. The tab then dies with
        /// `[error 2147942402 (0x80070002) when launching `claude']` (seen on real hardware
        /// 2026-08-07). The native install location is fixed, so resolve it ourselves and only
        /// fall back to PATH for exotic setups.
        /// </summary>
        internal static String ClaudeCommand()
        {
            var native = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "bin", "claude.exe");
            return FileExists(native) ? native : "claude";
        }

        /// <summary>
        /// Arguments for a navigation gesture, or null when Windows Terminal cannot express it.
        ///
        /// `wt` has no "previous tab" verb — only `focus-tab -n/-p` (next/previous) and
        /// `--window` targeting. `-w 0` means "the current window", which is what makes these
        /// act on the terminal the user is already looking at rather than spawning a new one.
        /// </summary>
        internal static List<String> ArgsFor(TerminalAction action) => action switch
        {
            // Focusing the existing window without changing anything: address the current window
            // and re-focus the tab it already has.
            TerminalAction.Activate => new List<String> { "-w", "0", "focus-tab" },

            TerminalAction.NewTab => new List<String> { "-w", "0", "new-tab" },
            TerminalAction.NewClaudeTab => new List<String> { "-w", "0", "new-tab", ClaudeCommand() },
            TerminalAction.NextTab => new List<String> { "-w", "0", "focus-tab", "-n" },
            TerminalAction.PreviousTab => new List<String> { "-w", "0", "focus-tab", "-p" },

            // A NEW window: "-w new" is the documented spelling for "don't reuse".
            TerminalAction.NewClaudeWindow => new List<String> { "-w", "new", "new-tab", ClaudeCommand() },

            // Cycling WINDOWS is an OS-level gesture, not a terminal one — wt.exe cannot do it.
            // Callers degrade rather than sending something that would do the wrong thing.
            TerminalAction.NextWindow => null,
            TerminalAction.PreviousWindow => null,

            _ => null,
        };

        /// <summary>
        /// Open a new tab at <paramref name="projectDir"/> running claude.
        ///
        /// The directory travels as its own argv element, so spaces and ampersands in a project
        /// path need no quoting dance — the same discipline the injection path uses for text.
        /// </summary>
        internal static List<String> LaunchClaudeArgs(String projectDir)
        {
            if (String.IsNullOrWhiteSpace(projectDir))
            {
                return null;
            }

            // Deliberately always a NEW tab, never reusing the current one. macOS can ask Terminal
            // whether the front tab is idle (`busy`) and reuse it; Windows Terminal exposes no such
            // signal, and guessing wrong would type `claude` into a live session. A spare tab is a
            // far cheaper mistake.
            return new List<String> { "-w", "0", "new-tab", "-d", projectDir, ClaudeCommand() };
        }
    }
}
