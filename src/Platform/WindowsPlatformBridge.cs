namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    /// <summary>
    /// The Windows backend. PHASE 1 — session discovery only: it can see which Claude sessions are
    /// running and mint their keys, so the grid, slots and pinning work. Injection (Phase 2),
    /// navigation (Phase 3) and voice (Phase 5) still report Unsupported, which surfaces as a
    /// logged no-op rather than a wrong keystroke.
    ///
    /// Sessions are keyed by the Claude process (PID + start time), never by window title: the
    /// 2026-08-07 spike found every Claude tab reports the identical title "✳ Claude Code", and
    /// that Claude REWRITES the title to a conversation summary once chatting starts. See
    /// docs/windows-port-2.0-plan.md §1.
    /// </summary>
    internal sealed class WindowsPlatformBridge : IPlatformBridge
    {
        public String Name => "Windows";

        // Phase 1: discovery works, injection does not. Flipped to true in Phase 2.
        public Boolean IsSupported => false;

        /// <summary>
        /// Enumerates the process table. Injectable so the discovery logic is testable on any OS —
        /// the default implementation is the only Windows-only code in this class.
        /// </summary>
        internal Func<IEnumerable<WindowsProcessInfo>> ProcessEnumerator { get; set; }

        /// <summary>
        /// Resolves a PID's full command line, or null if it can't be read. Only consulted for
        /// interpreter processes (an npm/bun install), and cached per PID — the native
        /// `claude.exe` install needs no command line at all, so the common case costs nothing.
        /// </summary>
        internal Func<Int32, String> CommandLineResolver { get; set; }

        // Session key → command line. Pruned every scan to the processes still alive, so a
        // long-running plugin that sees thousands of short-lived node processes doesn't grow.
        private readonly Dictionary<String, String> _cmdCache = new Dictionary<String, String>(StringComparer.Ordinal);

        /// <summary>Test seam: proves the cache is pruned rather than accumulating.</summary>
        internal Int32 CommandLineCacheCount => _cmdCache.Count;

        public HashSet<String> DiscoverSessions()
        {
            var enumerator = this.ProcessEnumerator ?? this.EnumerateProcesses;

            List<WindowsProcessInfo> rows;
            try
            {
                rows = enumerator()?.ToList();
            }
            catch (Exception ex)
            {
                // "I don't know", NOT "none" — an empty set would tell the registry to reap every
                // live session (see IPlatformBridge.DiscoverSessions).
                PluginLog.Verbose(ex, "WindowsPlatformBridge: process scan failed");
                return null;
            }

            if (rows == null)
            {
                return null;
            }

            this.FillCommandLines(rows);
            return WindowsProcessWatcher.SessionsFrom(rows);
        }

        // Command lines are only needed to tell `node.exe running the Claude CLI` from any other
        // node process. Resolve them lazily, once per process, and never for the native install.
        private void FillCommandLines(List<WindowsProcessInfo> rows)
        {
            var resolver = this.CommandLineResolver ?? ResolveCommandLine;

            // Drop cache entries for processes that are gone, so a long-running plugin doesn't grow.
            var liveKeys = new HashSet<String>(rows.Select(WindowsProcessWatcher.SessionKeyFor), StringComparer.Ordinal);
            foreach (var stale in _cmdCache.Keys.Where(k => !liveKeys.Contains(k)).ToList())
            {
                _cmdCache.Remove(stale);
            }

            foreach (var row in rows)
            {
                if (row.CommandLine != null || !NeedsCommandLine(row))
                {
                    continue;
                }

                var key = WindowsProcessWatcher.SessionKeyFor(row);
                if (!_cmdCache.TryGetValue(key, out var cmd))
                {
                    try
                    {
                        cmd = resolver(row.Pid);
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Verbose(ex, $"WindowsPlatformBridge: could not read command line for pid {row.Pid}");
                        cmd = null;
                    }
                    _cmdCache[key] = cmd;
                }

                row.CommandLine = cmd;
            }
        }

        // Only interpreters are ambiguous. claude.exe is a session on its name alone.
        internal static Boolean NeedsCommandLine(WindowsProcessInfo p) =>
            p?.Name != null &&
            !p.Name.Equals("claude.exe", StringComparison.OrdinalIgnoreCase) &&
            !p.Name.Equals("claude", StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------------------------------
        // Windows-only plumbing below. Compiles everywhere; only ever CALLED on Windows.
        // ------------------------------------------------------------------------------------------

        // Names worth enumerating at all — everything else in the process table is irrelevant, and
        // touching fewer Process objects keeps the ~2s scan cheap.
        private static readonly String[] InterestingNames =
        {
            "claude", "node", "bun", "deno", "npx",
        };

        private IEnumerable<WindowsProcessInfo> EnumerateProcesses()
        {
            var rows = new List<WindowsProcessInfo>();

            foreach (var name in InterestingNames)
            {
                Process[] found;
                try
                {
                    found = Process.GetProcessesByName(name);   // takes the name WITHOUT .exe
                }
                catch (Exception ex)
                {
                    PluginLog.Verbose(ex, $"WindowsPlatformBridge: GetProcessesByName({name}) failed");
                    continue;
                }

                foreach (var proc in found)
                {
                    try
                    {
                        rows.Add(new WindowsProcessInfo
                        {
                            Pid = proc.Id,
                            ParentPid = 0,               // filled by the resolver path when available
                            Name = name + ".exe",
                            StartTime = proc.StartTime,
                            CommandLine = null,          // resolved lazily, only when ambiguous
                        });
                    }
                    catch (Exception ex)
                    {
                        // A process can exit between enumeration and property access; also access
                        // denied for higher-integrity processes. Skip it rather than fail the scan.
                        PluginLog.Verbose(ex, $"WindowsPlatformBridge: skipping pid {SafePid(proc)}");
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }

            return rows;
        }

        private static String SafePid(Process p)
        {
            try { return p.Id.ToString(); } catch { return "?"; }
        }

        /// <summary>
        /// Default command-line resolver.
        ///
        /// UNVERIFIED ON HARDWARE — written against the documented API, but this class has never
        /// run on Windows (the plugin is built on macOS). Confirm before Phase 2 lands, and prefer
        /// swapping this one method over reshaping the class: everything above it is covered by
        /// tests that pass on any OS.
        /// </summary>
        private static String ResolveCommandLine(Int32 pid)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            // Deliberately NOT a System.Management dependency: that would add a NuGet assembly to
            // the shipped package for one string lookup. Shell out under the same hard timeout
            // discipline every backend uses instead (BoundedProcess).
            var output = BoundedProcess.Run(
                "powershell.exe",
                new List<String>
                {
                    "-NoProfile", "-NonInteractive", "-Command",
                    $"(Get-CimInstance Win32_Process -Filter \"ProcessId={pid}\").CommandLine",
                },
                5000,
                wantOutput: true);

            return String.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }

        // ------------------------------------------------------------------------------------------
        // Not yet implemented — Phases 2 (injection), 3 (navigation).
        // These return/act exactly as UnsupportedPlatformBridge does, so a key press on a Phase 1
        // build is a logged no-op, never a keystroke sent somewhere unintended.
        // ------------------------------------------------------------------------------------------

        public String QueryFrontmostSession() => null;   // Phase 3

        public InjectionOutcome InjectText(String sessionKey, String text, Boolean pressEnter) => NotYet(nameof(InjectText));

        public InjectionOutcome InjectKey(String sessionKey, KeyStroke key) => NotYet(nameof(InjectKey));

        public InjectionOutcome InjectTabThenEnter(String sessionKey) => NotYet(nameof(InjectTabThenEnter));

        public void FocusSession(String sessionKey) => NotYet(nameof(FocusSession));

        public void Navigate(TerminalAction action) => NotYet(nameof(Navigate));

        public void LaunchClaudeInProject(String projectDir) => NotYet(nameof(LaunchClaudeInProject));

        public void Alert() { /* Phase 3 */ }

        private static InjectionOutcome NotYet(String what)
        {
            PluginLog.Info($"WindowsPlatformBridge: {what} is not implemented yet (Phase 2/3)");
            return InjectionOutcome.Unsupported;
        }
    }
}
