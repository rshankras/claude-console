namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Finds the Terminal tabs currently running a Claude Code session, by scanning `ps`.
    ///
    /// This is what makes a closed session's key clear promptly and correctly. Before this, a tab's
    /// state file could only be aged out on a timer, so a finished session lingered and a quiet one
    /// risked vanishing; `ps` answers "is it still there?" exactly.
    ///
    /// Parsing rules, derived from real `ps -axo pid=,ppid=,tty=,command=` output on macOS:
    ///   • Rows with tty "??" are dropped. This is what excludes the Claude DESKTOP app, which runs
    ///     a dozen Electron helper processes with no controlling terminal.
    ///   • The executable is matched CASE-SENSITIVELY against "claude". The desktop app's binary is
    ///     "…/Contents/MacOS/Claude" (capital C), so were it ever launched from a terminal it would
    ///     still not be mistaken for a CLI session.
    ///   • Claude Code also appears as `node …/claude` or `bun …/claude` depending on how it was
    ///     installed, so those forms are matched on the script argument.
    ///   • A candidate whose parent is also a candidate is dropped, keeping one row per tab even
    ///     when a session spawns a nested claude process.
    /// </summary>
    internal static class ClaudeProcessWatcher
    {
        // pid, ppid, tty, command — the four columns requested from `ps`, whitespace separated.
        private static readonly Regex Row = new Regex(@"^(\d+)\s+(\d+)\s+(\S+)\s+(.+)$", RegexOptions.Compiled);

        private static readonly HashSet<String> Interpreters =
            new HashSet<String>(StringComparer.Ordinal) { "node", "bun", "npx", "deno" };

        internal sealed class Row4
        {
            public String Pid;
            public String ParentPid;
            public String Tty;      // bare, e.g. "ttys003"
        }

        /// <summary>
        /// Parse `ps -axo pid=,ppid=,tty=,command=` output into the TTYs running Claude Code.
        /// Pure and allocation-light so it can be unit-tested against captured output.
        /// </summary>
        internal static IReadOnlyList<Row4> Parse(String psOutput)
        {
            var candidates = new List<Row4>();
            if (String.IsNullOrEmpty(psOutput))
            {
                return candidates;
            }

            foreach (var line in psOutput.Split('\n'))
            {
                var m = Row.Match(line.Trim());
                if (!m.Success)
                {
                    continue;
                }

                var tty = m.Groups[3].Value;
                if (tty == "??" || tty == "?" || String.IsNullOrEmpty(tty))
                {
                    continue;   // no controlling terminal — not a session we can focus or type into
                }

                if (!IsClaudeCommand(m.Groups[4].Value))
                {
                    continue;
                }

                candidates.Add(new Row4
                {
                    Pid = m.Groups[1].Value,
                    ParentPid = m.Groups[2].Value,
                    Tty = tty.StartsWith("/") ? tty.Substring(tty.LastIndexOf('/') + 1) : tty,
                });
            }

            // Keep only the top-level session per tab: drop any candidate parented by another.
            var pids = new HashSet<String>(candidates.Select(c => c.Pid), StringComparer.Ordinal);
            return candidates.Where(c => !pids.Contains(c.ParentPid)).ToList();
        }

        /// <summary>The distinct TTYs running Claude Code, e.g. { "ttys000", "ttys003" }.</summary>
        internal static HashSet<String> TtysFrom(String psOutput) =>
            new HashSet<String>(Parse(psOutput).Select(r => r.Tty), StringComparer.Ordinal);

        // True for `claude …`, `/usr/local/bin/claude …`, `node …/claude …`, `bun …/claude …`.
        internal static Boolean IsClaudeCommand(String command)
        {
            var argv = SplitArgs(command);
            if (argv.Count == 0)
            {
                return false;
            }

            var exe = BaseName(argv[0]);
            if (exe == "claude")
            {
                return true;
            }

            // `node /opt/homebrew/lib/node_modules/@anthropic-ai/claude-code/cli.js` and friends:
            // check the script argument, skipping interpreter flags like `--enable-source-maps`.
            if (Interpreters.Contains(exe))
            {
                foreach (var arg in argv.Skip(1))
                {
                    if (arg.StartsWith("-"))
                    {
                        continue;
                    }
                    var name = BaseName(arg);
                    return name == "claude"
                        || arg.Contains("/claude-code/")
                        || arg.Contains("/.claude/");
                }
            }

            return false;
        }

        private static String BaseName(String path)
        {
            var slash = path.LastIndexOf('/');
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }

        private static List<String> SplitArgs(String command) =>
            command.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
