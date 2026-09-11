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
    /// Finds the Claude Code sessions running on Windows — the counterpart to AgentProcessWatcher,
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

        // The agent's names and script hints come from the AgentProcessMatcher the product was
        // constructed with — the same seam macOS discovery has used since it existed. This class
        // carried hardcoded Claude names long after that seam landed, so the Codex product's
        // Windows build scanned for claude processes and found nothing: profile visible, every
        // key refusing to type. Sixth bug of the built-but-never-wired shape.

        /// <summary>Exe basenames to match, case-insensitively, with and without ".exe".</summary>
        private static IEnumerable<String> CliNames(AgentProcessMatcher matcher)
        {
            foreach (var name in matcher?.ExeNames ?? Array.Empty<String>())
            {
                yield return name;
                yield return name + ".exe";
            }
        }

        /// <summary>
        /// Fragments that identify the agent's CLI on an interpreter's command line, in BOTH slash
        /// flavours: matcher hints are written mac-style ("/@openai/codex/"), Windows paths mostly
        /// arrive backslashed, and npm-generated shims mix the two freely. Plus one derived
        /// fragment per exe name ("\claude", "/codex"), which is what recognises
        /// `node …\.local\bin\claude` and script names like claude.js.
        /// </summary>
        private static IEnumerable<String> CliMarkers(AgentProcessMatcher matcher)
        {
            foreach (var hint in matcher?.ScriptHints ?? Array.Empty<String>())
            {
                yield return hint;
                yield return hint.Replace('/', '\\');
            }

            foreach (var name in matcher?.ExeNames ?? Array.Empty<String>())
            {
                yield return "/" + name;
                yield return "\\" + name;
            }
        }

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
        internal static HashSet<String> SessionsFrom(IEnumerable<WindowsProcessInfo> processes, AgentProcessMatcher matcher)
        {
            var rows = processes?.Where(p => p != null).ToList() ?? new List<WindowsProcessInfo>();

            var candidates = rows.Where(p => IsAgentSession(p, matcher)).ToList();

            // Drop a candidate whose parent is also a candidate — one key per session, even when a
            // session shells out to another claude.
            var candidatePids = new HashSet<Int32>(candidates.Select(c => c.Pid));
            var keep = candidates.Where(c => !candidatePids.Contains(c.ParentPid));

            return new HashSet<String>(keep.Select(SessionKeyFor), StringComparer.Ordinal);
        }

        /// <summary>
        /// The executable a command line runs — as a rooted path to claude.exe, or null.
        ///
        /// Used to learn where Claude is actually installed from a LIVE session, so the launch
        /// keys follow any install directory on any drive instead of assuming the native
        /// installer's default. Only rooted paths qualify: a bare `claude --resume …` command
        /// line names no directory, and guessing one would defeat the point.
        /// </summary>
        internal static String ExeFromCommandLine(String cmd, AgentProcessMatcher matcher)
        {
            if (String.IsNullOrWhiteSpace(cmd))
            {
                return null;
            }

            var t = cmd.TrimStart();
            String exe;
            if (t[0] == '"')
            {
                var end = t.IndexOf('"', 1);
                if (end <= 1)
                {
                    return null;
                }
                exe = t.Substring(1, end - 1);
            }
            else
            {
                var end = t.IndexOf(' ');
                exe = end < 0 ? t : t.Substring(0, end);
            }

            var isAgentExe = CliNames(matcher).Any(n =>
                exe.EndsWith("\\" + n, StringComparison.OrdinalIgnoreCase) ||
                exe.EndsWith("/" + n, StringComparison.OrdinalIgnoreCase));

            return isAgentExe && IsWindowsRooted(exe) ? exe : null;
        }

        /// <summary>
        /// Is this a rooted WINDOWS path — "C:\…" or a "\\server\share" UNC?
        ///
        /// Deliberately not Path.IsPathRooted: that applies the rules of whatever OS the code is
        /// RUNNING on, and this function parses a Windows command line as DATA. On macOS
        /// Path.IsPathRooted(@"C:\Users\…") is false, so the whole install-location lookup returned
        /// null and four tests failed there while passing on Windows.
        /// </summary>
        internal static Boolean IsWindowsRooted(String path) =>
            !String.IsNullOrEmpty(path) &&
            ((path.Length >= 3 &&
              Char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')) ||
             path.StartsWith(@"\\", StringComparison.Ordinal));

        internal static Boolean IsAgentSession(WindowsProcessInfo p, AgentProcessMatcher matcher)
        {
            if (p == null || String.IsNullOrWhiteSpace(p.Name))
            {
                return false;
            }

            var cmd = p.CommandLine ?? String.Empty;

            // Desktop GUI apps and their Electron helpers are never sessions, whatever the agent.
            if (DesktopMarkers.Any(m => cmd.Contains(m, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // The native CLI: <agent>.exe. Case-insensitive on purpose — Windows filesystems are —
            // and the desktop-app collision that forces case-sensitivity on macOS is handled here
            // by the command-line exclusions above instead.
            var name = RunningImageName(p.Name);
            if (CliNames(matcher).Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            // An npm/bun install: an interpreter running the agent's CLI script.
            if (Interpreters.Contains(name))
            {
                return CliMarkers(matcher).Any(m => cmd.Contains(m, StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }

        /// <summary>
        /// A process's name as it was launched, even after an in-place update renamed the running
        /// image. Claude Code updates itself while sessions run; Windows cannot overwrite a running
        /// executable, so the updater renames it — claude.exe becomes claude.exe.old.1789090133131
        /// and any enumeration that reads the CURRENT image name reports that. WMI reports the
        /// creation-time name, which is why this scan kept finding the session on 2026-09-11 while
        /// the hook exe, which asks .NET, lost it — see claude-console-hook's IsExe. Same rule on
        /// both sides, so they can never disagree about what a Claude process is.
        /// </summary>
        internal static String RunningImageName(String name)
        {
            var at = name.IndexOf(".exe.", StringComparison.OrdinalIgnoreCase);
            return at > 0 ? name.Substring(0, at + 4) : name;
        }
    }
}
