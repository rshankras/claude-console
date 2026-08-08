namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
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
    /// </summary>
    internal static class BoundedProcess
    {
        /// <summary>Run <paramref name="file"/> and return trimmed stdout when <paramref name="wantOutput"/>, else null.</summary>
        internal static String Run(String file, List<String> args, Int32 timeoutMs, Boolean wantOutput)
        {
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

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return null;
                }

                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();

                if (!proc.WaitForExit(timeoutMs))
                {
                    PluginLog.Warning($"{file} exceeded {timeoutMs}ms — killing (hung window server / locked screen / a11y prompt?)");
                    try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    return null;
                }

                var outp = SafeAwait(outTask);
                var err = SafeAwait(errTask);
                if (!wantOutput && !String.IsNullOrWhiteSpace(outp))
                {
                    PluginLog.Warning($"{file} out: {outp.Trim()}");
                }
                if (!String.IsNullOrWhiteSpace(err))
                {
                    PluginLog.Warning($"{file} error: {err.Trim()}");
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

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return null;
                }

                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();

                if (!proc.WaitForExit(timeoutMs))
                {
                    PluginLog.Warning($"{file} exceeded {timeoutMs}ms — killing");
                    try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    return null;
                }

                var err = SafeAwait(errTask);
                if (!String.IsNullOrWhiteSpace(err))
                {
                    PluginLog.Warning($"{file}: {err.Trim()}");
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

        // Await a pipe-read task that has already reached EOF (the process exited). Never throws.
        private static String SafeAwait(Task<String> t)
        {
            try { return t.GetAwaiter().GetResult(); }
            catch { return null; }
        }
    }
}
