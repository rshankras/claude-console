namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>How much attention an approval request deserves.</summary>
    public enum ApprovalRisk
    {
        /// <summary>Nothing is waiting for an answer.</summary>
        None = 0,

        /// <summary>Waiting on you, but routine — reading a file, running a test.</summary>
        Normal = 1,

        /// <summary>Waiting on something destructive or outward-facing. Look before you press Yes.</summary>
        High = 2,
    }

    /// <summary>
    /// Decides whether a pending permission request is routine or worth a second look, so the keypad
    /// can colour it from across the room. This is a HINT, not a security control — Claude Code's own
    /// prompt remains the thing that actually gates the command.
    ///
    /// Ported from Vizhi's classifier, with its substring-matching bug fixed: matching "force" as a
    /// plain substring flags "workforce", and matching "rm " flags "confirm ". Every pattern here is
    /// anchored on word boundaries.
    ///
    /// Tuned to prefer a false alarm over a missed one — an unnecessary red badge costs a glance, a
    /// missed one costs a force-push. But bare generic flags (a lone "-f") are deliberately NOT
    /// flagged: a badge that lights on every other command teaches you to ignore it.
    /// </summary>
    internal static class RiskClassifier
    {
        private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Compiled;

        // Each entry is "why it's risky" — kept as named fields so a future reader can tell whether a
        // pattern is about destruction, escalation, or reaching the outside world.
        private static readonly Regex[] HighRiskCommands =
        {
            new Regex(@"\bsudo\b", Opts),                                  // privilege escalation
            new Regex(@"\bdoas\b", Opts),
            new Regex(@"\brm\s+-\w*[rRf]", Opts),                          // recursive/forced delete
            new Regex(@"\bgit\s+push\b", Opts),                            // outward-facing, hard to undo
            new Regex(@"\bgit\s+reset\s+--hard\b", Opts),                  // discards work
            new Regex(@"\bgit\s+clean\s+-\w*[fdx]", Opts),                 // deletes untracked files
            new Regex(@"--force\b|--force-with-lease\b", Opts),
            new Regex(@"\bmkfs(\.\w+)?\b", Opts),                          // formats a filesystem
            new Regex(@"\bdd\b[^|;]*\bof=", Opts),                         // raw device write
            new Regex(@"\bchmod\s+(-\w+\s+)*777\b", Opts),                 // world-writable
            new Regex(@"\bchown\s+-R\b", Opts),
            new Regex(@"\bdrop\s+(table|database|schema)\b", Opts),        // destructive SQL
            new Regex(@"\bdelete\s+from\b", Opts),
            new Regex(@"\btruncate\s+table\b", Opts),
            new Regex(@"(curl|wget)\b[^|;]*\|\s*(sudo\s+)?\w*sh\b", Opts), // pipe-from-internet to shell
            new Regex(@">\s*/dev/(disk|sd|nvme)", Opts),                   // writing over a device
            new Regex(@"\bkillall\b", Opts),
            new Regex(@"\b(shutdown|reboot|halt)\b", Opts),
            new Regex(@"\bnpm\s+publish\b|\byarn\s+publish\b|\bpnpm\s+publish\b", Opts),
            new Regex(@"\bgh\s+release\s+create\b", Opts),
            new Regex(@"\bterraform\s+(apply|destroy)\b", Opts),
            new Regex(@"\bkubectl\s+delete\b", Opts),
        };

        /// <summary>
        /// Classify a pending approval. <paramref name="toolName"/> is Claude Code's tool
        /// (e.g. "Bash"); <paramref name="command"/> is the shell string when there is one.
        /// A request with no tool at all is <see cref="ApprovalRisk.None"/>.
        /// </summary>
        public static ApprovalRisk Classify(String toolName, String command)
        {
            if (String.IsNullOrWhiteSpace(toolName) && String.IsNullOrWhiteSpace(command))
            {
                return ApprovalRisk.None;
            }

            return IsHighRisk(command) ? ApprovalRisk.High : ApprovalRisk.Normal;
        }

        /// <summary>True when the shell command matches one of the destructive patterns.</summary>
        public static Boolean IsHighRisk(String command)
        {
            if (String.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            foreach (var pattern in HighRiskCommands)
            {
                if (pattern.IsMatch(command))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
