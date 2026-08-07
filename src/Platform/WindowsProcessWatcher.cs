namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>One row of the Windows process table, as much of it as session discovery needs.</summary>
    internal sealed class WindowsProcessInfo
    {
        public Int32 Pid { get; set; }
        public Int32 ParentPid { get; set; }

        /// <summary>Executable name including extension, e.g. "claude.exe".</summary>
        public String Name { get; set; }

        /// <summary>Full command line, or null when it couldn't be read (access denied).</summary>
        public String CommandLine { get; set; }

        /// <summary>
        /// Process start time. Half of the session key — Windows recycles PIDs aggressively, and a
        /// recycled PID must never inherit the dead session's slot and pin.
        /// </summary>
        public DateTime StartTime { get; set; }
    }

    /// <summary>
    /// Finds the Claude Code sessions running on Windows — the counterpart to ClaudeProcessWatcher,
    /// and deliberately the same shape: a PURE decision function over process rows, so it can be
    /// unit-tested against captured tables on any OS, with the actual enumeration kept thin and
    /// injectable (see WindowsPlatformBridge).
    ///
    /// Discovery rules, and why:
    ///   • The native installer produces "claude.exe"; an npm install produces
    ///     `node.exe "…\@anthropic-ai\claude-code\cli.js"`. Both are sessions, so we match the
    ///     executable name OR an interpreter whose command line names the Claude CLI.
    ///   • Claude DESKTOP must never be mistaken for a session. On macOS the "no controlling
    ///     terminal" rule excluded it; Windows has no equivalent signal here, so we exclude it
    ///     explicitly: it is an Electron app, so its helper processes carry `--type=` switches, and
    ///     its binary lives under an AnthropicClaude install directory. Both are checked, because
    ///     the main Electron process has no --type switch of its own.
    ///   • A candidate whose PARENT is also a candidate is dropped, so one session that spawns a
    ///     nested claude still occupies exactly one key (same rule as macOS).
    ///
    /// NOTE ON CONSOLES: discovery deliberately does NOT group by console. There is no passive API
    /// that maps a process to its console — GetConsoleProcessList only answers for the console you
    /// are already attached to — so grouping would mean attach-probing every candidate on a timer.
    /// The 2026-08-07 spike showed it is unnecessary: attaching directly to Claude's own PID reaches
    /// the right console. See docs/windows-port-2.0-plan.md §1.
    /// </summary>
    internal static class WindowsProcessWatcher
    {
        private static readonly HashSet<String> Interpreters = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            "node.exe", "bun.exe", "deno.exe", "npx.exe", "node", "bun", "deno", "npx",
        };

        private static readonly HashSet<String> ClaudeCliNames = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            "claude.exe", "claude",
        };

        // Fragments that identify the Claude Code CLI on an interpreter's command line. Checked
        // case-insensitively: Windows paths vary in case by installer and by user.
        private static readonly String[] CliMarkers =
        {
            "claude-code",      // npm package dir: @anthropic-ai\claude-code\cli.js
            "claude.js",
            @"\claude",         // …\.local\bin\claude
            "/claude",          // forward slashes appear in npm-generated shims
        };

        // Claude Desktop is Electron AND its executable is also called claude.exe, so the name alone
        // cannot tell the two apart. Its renderer/GPU/utility children carry --type=, but the MAIN
        // process carries no switch at all — only its install location distinguishes it. Both known
        // install shapes are matched:
        //
        //   Microsoft Store (MSIX):  C:\Program Files\WindowsApps\Claude_1.26832.0.0_x64__…\app\Claude.exe
        //   Direct download:         %LOCALAPPDATA%\AnthropicClaude\app-x.y.z\claude.exe
        //
        // The Store form was found on real hardware 2026-08-07 — the original list only had the
        // direct-download path, so the Desktop main process was being taken for a CLI session and
        // would have put a phantom key on the grid that no keystroke could reach.
        private static readonly String[] DesktopMarkers =
        {
            "--type=",             // any Electron child (renderer, gpu, utility, crashpad)
            "windowsapps",         // Store install — the CLI is never installed here
            "anthropicclaude",     // direct-download install dir
            "claude desktop",
            "squirrel",            // the Electron updater that shares the install dir
        };

        /// <summary>
        /// The session key for a process: opaque above the platform seam, filename-safe, and
        /// unique across PID reuse because it carries the start time.
        /// </summary>
        internal static String SessionKeyFor(WindowsProcessInfo p) =>
            $"pid-{p.Pid}-{p.StartTime.ToUniversalTime().Ticks}";

        /// <summary>
        /// Decide which rows are live Claude Code sessions and mint a session key for each.
        /// Pure — no OS calls — so it is unit-tested against captured process tables.
        /// </summary>
        internal static HashSet<String> SessionsFrom(IEnumerable<WindowsProcessInfo> processes)
        {
            var rows = processes?.Where(p => p != null).ToList() ?? new List<WindowsProcessInfo>();

            var candidates = rows.Where(IsClaudeSession).ToList();

            // Drop a candidate whose parent is also a candidate — one key per session, even when a
            // session shells out to another claude.
            var candidatePids = new HashSet<Int32>(candidates.Select(c => c.Pid));
            var keep = candidates.Where(c => !candidatePids.Contains(c.ParentPid));

            return new HashSet<String>(keep.Select(SessionKeyFor), StringComparer.Ordinal);
        }

        internal static Boolean IsClaudeSession(WindowsProcessInfo p)
        {
            if (p == null || String.IsNullOrWhiteSpace(p.Name))
            {
                return false;
            }

            var cmd = p.CommandLine ?? String.Empty;

            // Claude Desktop and its Electron helpers are never sessions.
            if (DesktopMarkers.Any(m => cmd.Contains(m, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // The native CLI: claude.exe. Guard against the desktop app's binary, which can also be
            // named claude.exe but lives under its own install directory (already excluded above by
            // command line; this catches the case where the command line couldn't be read).
            if (ClaudeCliNames.Contains(p.Name))
            {
                return true;
            }

            // An npm/bun install: an interpreter running the Claude CLI script.
            if (Interpreters.Contains(p.Name))
            {
                return CliMarkers.Any(m => cmd.Contains(m, StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }
    }
}
