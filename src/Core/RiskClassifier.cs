namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.IO;
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

            // --- Windows: PowerShell and cmd ------------------------------------------------
            // On Windows, Claude Code proposes PowerShell, not sh — a delete arrives as
            // `Remove-Item -Recurse -Force`, a shutdown as `Stop-Computer`. The Unix patterns
            // above never see these, so a genuinely destructive Windows command read as routine
            // (a real 2.2.1 finding: a recursive force-delete showed an amber Yes, not red).
            // PowerShell accepts any unambiguous parameter prefix, so -Force is -Fo/-For/-Forc
            // (never a lone -f — -Filter shares the F) and -Recurse is -R/-Rec/…; matched that way.
            // The "lone generic flag is not enough" rule from the Unix side still holds.
            new Regex(@"\b(Remove-Item|rmdir|rd|del|erase|ri)\b[^|;\r\n]*\s-(R|Re|Rec|Recu|Recur|Recurs|Recurse|Fo|For|Forc|Force)\b", Opts), // recursive/forced delete
            new Regex(@"\b(rmdir|rd)\b[^|;\r\n]*\s/s\b", Opts),           // cmd recursive rmdir
            new Regex(@"\b(del|erase)\b[^|;\r\n]*\s/s\b", Opts),          // cmd recursive delete
            new Regex(@"\bFormat-Volume\b|\bClear-Disk\b", Opts),         // formats / wipes a volume or disk
            new Regex(@"\bformat\s+[A-Za-z]:", Opts),                     // cmd `format C:` (not `dotnet format`)
            new Regex(@"\b(Stop|Restart)-Computer\b", Opts),             // shutdown / reboot
            new Regex(@"\bSet-ExecutionPolicy\b[^|;\r\n]*\b(Unrestricted|Bypass)\b", Opts), // disables script safety
            new Regex(@"\b(iwr|irm|Invoke-WebRequest|Invoke-RestMethod)\b[^|;]*\|\s*(iex|Invoke-Expression)\b", Opts), // download-and-run
        };

        // A patch is not a command. Codex's apply_patch tool delivers its body in the SAME field
        // that Bash uses for a shell string, so the payload alone cannot tell you which you have.
        // Both a name check and a content sniff are used: the name is how it is documented, the
        // sniff is what survives the tool being renamed.
        private const String PatchPreamble = "*** Begin Patch";

        // Paths inside a patch header. Move-to is included because a patch can relocate a file out
        // of the workspace as easily as it can create one there.
        private static readonly Regex PatchPaths =
            new Regex(@"^\*\*\* (?:Add File|Update File|Delete File|Move to):\s*(?<path>.+?)\s*$",
                      RegexOptions.Multiline | Opts);

        // Files whose contents are worth a second look wherever they live: credentials, and the
        // things that run on their own later (CI workflows, git hooks, shell profiles).
        private static readonly Regex[] SensitivePaths =
        {
            new Regex(@"(^|/)\.ssh/", Opts),
            new Regex(@"(^|/)\.aws/", Opts),
            new Regex(@"(^|/)\.env(\.|$)", Opts),
            new Regex(@"(^|/)id_(rsa|ed25519)\b", Opts),
            new Regex(@"(^|/)\.npmrc$", Opts),
            new Regex(@"(^|/)\.git/(config|hooks/)", Opts),
            new Regex(@"(^|/)\.github/workflows/", Opts),
            new Regex(@"(^|/)\.(z|ba)shrc$|(^|/)\.zprofile$", Opts),
        };

        /// <summary>
        /// Classify a pending approval. <paramref name="toolName"/> is the agent's tool
        /// (Claude Code: "Bash"; Codex: "Bash" or "apply_patch"), <paramref name="command"/> the
        /// payload that tool carries, and <paramref name="workspaceRoot"/> the session's working
        /// directory when it is known — without it, "outside the workspace" cannot be judged and
        /// only the path-content rules apply. A request with no tool at all is
        /// <see cref="ApprovalRisk.None"/>.
        /// </summary>
        public static ApprovalRisk Classify(String toolName, String command, String workspaceRoot = null)
        {
            if (String.IsNullOrWhiteSpace(toolName) && String.IsNullOrWhiteSpace(command))
            {
                return ApprovalRisk.None;
            }

            if (IsPatch(toolName, command))
            {
                return IsHighRiskPatch(command, workspaceRoot) ? ApprovalRisk.High : ApprovalRisk.Normal;
            }

            return IsHighRisk(command) ? ApprovalRisk.High : ApprovalRisk.Normal;
        }

        /// <summary>True when this approval carries a patch rather than a shell command.</summary>
        public static Boolean IsPatch(String toolName, String command) =>
            String.Equals(toolName, "apply_patch", StringComparison.OrdinalIgnoreCase)
            || (command?.TrimStart().StartsWith(PatchPreamble, StringComparison.Ordinal) ?? false);

        /// <summary>
        /// Grade a patch by WHERE it writes, not by what its lines say. Running the shell patterns
        /// over a diff is wrong twice over: it misses the real risk, and ordinary diff content is
        /// full of text that looks like a dangerous command — a patch adding a line that documents
        /// `git push --force` is not a force push.
        ///
        /// High when the patch escapes the workspace (the captured example wrote to /tmp), or
        /// touches a credential or something that will run on its own later. Everything else is a
        /// routine edit, because most patches are, and a badge that lights on every edit is noise.
        /// </summary>
        public static Boolean IsHighRiskPatch(String patch, String workspaceRoot)
        {
            if (String.IsNullOrWhiteSpace(patch))
            {
                return false;
            }

            foreach (Match m in PatchPaths.Matches(patch))
            {
                var path = m.Groups["path"].Value.Trim().Trim('"');
                if (path.Length == 0)
                {
                    continue;
                }

                foreach (var sensitive in SensitivePaths)
                {
                    if (sensitive.IsMatch(path))
                    {
                        return true;
                    }
                }

                if (EscapesWorkspace(path, workspaceRoot))
                {
                    return true;
                }
            }

            return false;
        }

        // Absolute paths are judged against the workspace; relative ones only need checking when
        // they climb out of it with "..". Comparison is textual on purpose — resolving symlinks
        // would touch the filesystem on a hot path, and this is a hint, not a sandbox.
        private static Boolean EscapesWorkspace(String path, String workspaceRoot)
        {
            if (path.Contains(".." + Path.DirectorySeparatorChar) || path.Contains("../"))
            {
                return true;
            }

            if (!Path.IsPathRooted(path))
            {
                return false;
            }

            if (String.IsNullOrWhiteSpace(workspaceRoot))
            {
                return false;
            }

            var root = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, '/');
            return !path.StartsWith(root + "/", StringComparison.Ordinal)
                && !String.Equals(path, root, StringComparison.Ordinal);
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
