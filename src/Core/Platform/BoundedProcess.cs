namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading.Tasks;

    /// <summary>
    /// Running a subprocess without leaking a thread — shared by every backend, because the failure
    /// it prevents is not platform-specific.
    ///
    /// The history: a synchronous ReadToEnd() blocks with NO timeout, which makes the WaitForExit(ms)
    /// after it unreachable. Poll callbacks then piled up until LogiPluginService hit its 4096-thread
    /// limit and aborted. So: drain both pipes asynchronously (a full buffer can't deadlock us),
    /// bound the wait, and KILL the process if it overruns — a hung osascript (locked screen, busy
    /// window server, an Accessibility prompt) or a wedged `ps`/PowerShell can never wedge a poll.
    ///
    /// The overrun message used to name those three causes as suspects. Measured on hardware for #46
    /// (spikes/subproc-46) they are not what is happening in the common case, and a log line that
    /// guesses wrong is worse than one that says less: the frontmost probe costs ~140ms against a
    /// 2000ms budget, ~215ms with every core saturated, and ~600ms with sixteen Apple Events in
    /// flight at once. Nothing reachable by load gets near the budget. So an overrun is a stalled
    /// machine, not a tight budget — and the thing worth recording is the DURATION of the calls that
    /// come close, because that is what distinguishes a cliff from a creep on the next read.
    /// </summary>
    internal static class BoundedProcess
    {
        // A poll-path call that has eaten half its budget is the early warning that overruns are
        // coming. Log those with their real duration; the pattern across a few of them is the
        // evidence #46 asked for and could not get from "exceeded 2000ms".
        private const Double SlowFraction = 0.5;

        // ...but only for the short, repeated, poll-path budgets. `screencapture -i` (120s) waits on
        // a human dragging a selection and the whisper helper (130s) transcribes audio: both are
        // MEANT to take a while, and warning about them would bury the calls that matter.
        private const Int32 SlowLogMaxBudgetMs = 10_000;

        // How many times each executable has overrun, so the log carries the rate rather than a
        // series of identical lines. Rate is the question #46 actually asks.
        private static readonly ConcurrentDictionary<String, Int32> Overruns = new ConcurrentDictionary<String, Int32>();

        /// <summary>Run <paramref name="file"/> and return trimmed stdout when <paramref name="wantOutput"/>, else null.</summary>
        internal static String Run(String file, List<String> args, Int32 timeoutMs, Boolean wantOutput) =>
            Run(file, args, timeoutMs, wantOutput, out _);

        /// <summary>
        /// As <see cref="Run(String,List{String},Int32,Boolean)"/>, but also reports whether the child
        /// was killed for overrunning its budget.
        ///
        /// Callers need that separated from "ran fine and produced nothing", because for the frontmost
        /// probe the two mean opposite things: an empty result is "Terminal isn't frontmost", which is
        /// routine and must not change any behaviour, while an overrun is "this machine is not
        /// answering Apple Events", which is the one case worth backing off from (#46).
        /// </summary>
        internal static String Run(String file, List<String> args, Int32 timeoutMs, Boolean wantOutput, out Boolean timedOut)
        {
            timedOut = false;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                foreach (var a in args)
                {
                    psi.ArgumentList.Add(a);
                }

                var sw = Stopwatch.StartNew();

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return null;
                }

                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();

                if (!proc.WaitForExit(timeoutMs))
                {
                    timedOut = true;
                    PluginLog.Warning($"{file} exceeded {timeoutMs}ms — killing ({NoteOverrun(file)})");
                    try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    return null;
                }

                var slow = SlowNote(file, sw.ElapsedMilliseconds, timeoutMs);
                if (slow != null)
                {
                    PluginLog.Warning(slow);
                }

                var outp = SafeAwait(outTask);
                // Output can contain a transcript, clipboard text, or a script with user content.
                // Drain both streams and retain stdout for the caller. A nonzero exit alone is
                // often an expected status (e.g. app closed); do not log it on every poll.
                var err = SafeAwait(errTask);
                if (proc.ExitCode != 0 && !String.IsNullOrWhiteSpace(err))
                {
                    PluginLog.Warning($"{file} exited {proc.ExitCode}");
                }
                return wantOutput ? outp?.Trim() : null;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"BoundedProcess.Run({file}) failed (is the required permission granted?)");
                return null;
            }
        }

        /// <summary>
        /// Run a helper for its EXIT CODE rather than its output, under the same kill-on-timeout
        /// discipline. Returns null when the process couldn't be started or overran its budget —
        /// which callers must not treat as success.
        /// </summary>
        internal static Int32? RunForExitCode(String file, List<String> args, Int32 timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                foreach (var a in args)
                {
                    psi.ArgumentList.Add(a);
                }

                var sw = Stopwatch.StartNew();

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return null;
                }

                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();

                if (!proc.WaitForExit(timeoutMs))
                {
                    PluginLog.Warning($"{file} exceeded {timeoutMs}ms — killing ({NoteOverrun(file)})");
                    try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    return null;
                }

                var slow = SlowNote(file, sw.ElapsedMilliseconds, timeoutMs);
                if (slow != null)
                {
                    PluginLog.Warning(slow);
                }

                var err = SafeAwait(errTask);
                if (proc.ExitCode != 0 && !String.IsNullOrWhiteSpace(err))
                {
                    PluginLog.Warning($"{file} exited {proc.ExitCode}");
                }
                _ = SafeAwait(outTask);

                return proc.ExitCode;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"BoundedProcess.RunForExitCode({file}) failed");
                return null;
            }
        }

        /// <summary>
        /// Count this overrun and describe it as a rate. Internal because the tests pin the wording:
        /// the wording IS half the fix for #46 — the message it replaces named three causes that the
        /// hardware measurements went on to rule out.
        /// </summary>
        internal static String NoteOverrun(String file)
        {
            var n = Overruns.AddOrUpdate(file, 1, (_, prev) => prev + 1);
            return $"{Ordinal(n)} overrun since load";
        }

        /// <summary>
        /// The creep detector: describe a call that survived but spent most of its budget, or null
        /// when it was comfortably inside. Returns the message rather than logging it so the
        /// threshold is assertable — PluginLog is a no-op with no log file attached, so a test
        /// could otherwise only prove this never throws.
        /// </summary>
        internal static String SlowNote(String file, Int64 elapsedMs, Int32 timeoutMs) =>
            timeoutMs > SlowLogMaxBudgetMs || elapsedMs < timeoutMs * SlowFraction
                ? null
                : $"{file} took {elapsedMs}ms of its {timeoutMs}ms budget";

        /// <summary>Reset the overrun tally. Tests only — in production it counts per plugin load.</summary>
        internal static void ResetOverrunsForTest() => Overruns.Clear();

        private static String Ordinal(Int32 n)
        {
            var suffix = (n % 100) is >= 11 and <= 13
                ? "th"
                : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            return $"{n}{suffix}";
        }

        // Await a pipe-read task that has already reached EOF (the process exited). Never throws.
        private static String SafeAwait(Task<String> t)
        {
            try { return t.GetAwaiter().GetResult(); }
            catch { return null; }
        }
    }
}
