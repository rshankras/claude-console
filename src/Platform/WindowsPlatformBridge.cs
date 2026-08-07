namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    /// <summary>
    /// The Windows backend: session discovery (Phase 1), console injection via a short-lived
    /// helper (Phase 2), and terminal control through wt.exe (Phase 3). Voice is not implemented
    /// (Phase 5) — those keys log and no-op rather than pretending.
    ///
    /// NOTHING HERE HAS RUN ON WINDOWS HARDWARE. The decision logic is unit-tested on macOS and
    /// the helpers cross-compile clean, but the Win32 calls themselves are unverified — see the
    /// ordered checklist in docs/windows-port-2.0-plan.md, starting with
    /// `claude-console-inject selftest`.
    ///
    /// Sessions are keyed by the Claude process (PID + start time), never by window title: the
    /// 2026-08-07 spike found every Claude tab reports the identical title "✳ Claude Code", and
    /// that Claude REWRITES the title to a conversation summary once chatting starts. See
    /// docs/windows-port-2.0-plan.md §1.
    /// </summary>
    internal sealed class WindowsPlatformBridge : IPlatformBridge
    {
        public String Name => "Windows";

        // Discovery, injection and terminal control are all implemented (Phases 1-3). Voice is
        // not (Phase 5) — those keys log and no-op. Nothing gates on this today; it is the
        // backend's own statement of whether it has a working implementation, and Windows now does.
        public Boolean IsSupported => OperatingSystem.IsWindows();

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

        // Every candidate needs its command line (see NeedsCommandLine), so the cost matters: a
        // machine with Claude Desktop open has a dozen claude.exe processes. Results are cached per
        // session key and only UNSEEN pids are looked up, so a steady state costs nothing; when
        // there are new pids they are fetched in ONE batched query rather than one process each.
        private void FillCommandLines(List<WindowsProcessInfo> rows)
        {
            // Drop cache entries for processes that are gone, so a long-running plugin doesn't grow.
            var liveKeys = new HashSet<String>(rows.Select(WindowsProcessWatcher.SessionKeyFor), StringComparer.Ordinal);
            foreach (var stale in _cmdCache.Keys.Where(k => !liveKeys.Contains(k)).ToList())
            {
                _cmdCache.Remove(stale);
            }

            var pending = rows
                .Where(r => r.CommandLine == null && NeedsCommandLine(r) &&
                            !_cmdCache.ContainsKey(WindowsProcessWatcher.SessionKeyFor(r)))
                .ToList();

            if (pending.Count > 0)
            {
                var perPid = this.CommandLineResolver != null
                    ? pending.ToDictionary(r => r.Pid, r => SafeResolve(this.CommandLineResolver, r.Pid))
                    : ResolveCommandLines(pending.Select(r => r.Pid));

                foreach (var row in pending)
                {
                    perPid.TryGetValue(row.Pid, out var cmd);
                    _cmdCache[WindowsProcessWatcher.SessionKeyFor(row)] = cmd;
                }
            }

            foreach (var row in rows)
            {
                if (row.CommandLine == null &&
                    _cmdCache.TryGetValue(WindowsProcessWatcher.SessionKeyFor(row), out var cached))
                {
                    row.CommandLine = cached;
                }
            }
        }

        private static String SafeResolve(Func<Int32, String> resolver, Int32 pid)
        {
            try
            {
                return resolver(pid);
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, $"WindowsPlatformBridge: could not read command line for pid {pid}");
                return null;
            }
        }

        /// <summary>
        /// EVERY candidate needs its command line — including claude.exe.
        ///
        /// This used to skip claude.exe as an "optimisation", on the theory that the native CLI is
        /// identifiable by name alone. It isn't: Claude DESKTOP's executable is also called
        /// claude.exe, and the only thing separating them is the install path in the command line.
        /// Skipping the lookup meant the Desktop markers had nothing to match, so all ten of
        /// Desktop's processes were counted as Claude Code sessions (seen on real hardware
        /// 2026-08-07 — slots pinned to a crashpad handler and a GPU process).
        /// </summary>
        internal static Boolean NeedsCommandLine(WindowsProcessInfo p) => p?.Name != null;

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
                            // KNOWN GAP: not populated. Process.GetProcessesByName gives no parent,
                            // and fetching one per process would add a CIM query to every scan for a
                            // case that barely occurs. Consequence: SessionsFrom's "drop a candidate
                            // whose parent is also a candidate" rule never fires on Windows, so a
                            // session that spawns a NESTED claude would take two keys instead of one.
                            // The real-hardware capture (2026-08-07, 4 sessions) showed no nesting.
                            // Revisit if it turns up; a cheap parent lookup exists via NtQuery.
                            ParentPid = 0,
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
        /// Fetch command lines for many pids in ONE query.
        ///
        /// Batched deliberately: every claude.exe needs its command line to tell the CLI from
        /// Claude Desktop, and a machine with Desktop open has a dozen of them. One PowerShell
        /// spawn per scan-with-new-pids is fine; a dozen would not be.
        /// </summary>
        private static Dictionary<Int32, String> ResolveCommandLines(IEnumerable<Int32> pids)
        {
            var result = new Dictionary<Int32, String>();
            var list = pids?.Distinct().ToList() ?? new List<Int32>();
            if (list.Count == 0 || !OperatingSystem.IsWindows())
            {
                return result;
            }

            // Deliberately NOT a System.Management dependency: that would add a NuGet assembly to
            // the shipped package. Shell out under the same hard timeout discipline every backend
            // uses (BoundedProcess). Tab-separated so a command line containing commas is safe.
            var filter = String.Join(" or ", list.Select(p => $"ProcessId={p}"));
            var output = BoundedProcess.Run(
                "powershell.exe",
                new List<String>
                {
                    "-NoProfile", "-NonInteractive", "-Command",
                    $"Get-CimInstance Win32_Process -Filter \"{filter}\" | " +
                    "ForEach-Object {{ \"$($_.ProcessId)`t$($_.CommandLine)\" }}",
                },
                8000,
                wantOutput: true);

            if (String.IsNullOrWhiteSpace(output))
            {
                return result;
            }

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                var tab = trimmed.IndexOf('\t');
                if (tab <= 0)
                {
                    continue;
                }
                if (Int32.TryParse(trimmed.Substring(0, tab), out var pid))
                {
                    result[pid] = trimmed.Substring(tab + 1);
                }
            }

            return result;
        }

        // ------------------------------------------------------------------------------------------
        // Injection (Phase 2) — one short-lived helper process per keypress.
        //
        // The guarantee IPlatformBridge demands (focus the target and type, or type nothing) holds
        // differently here than on macOS, and more strongly: WriteConsoleInput addresses a CONSOLE
        // HANDLE, not the foreground window. There is no "focus" step that another app could win a
        // race against — the keystrokes are delivered to the attached console or not at all, which
        // is why the 2026-08-07 spike could type into a hidden tab while Notepad held the
        // foreground and see nothing leak.
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Runs claude-console-inject.exe. Injectable so the argument construction and outcome
        /// mapping are testable without Windows; returns the helper's exit code (null if it could
        /// not be run at all).
        /// </summary>
        internal Func<List<String>, Int32?> InjectRunner { get; set; }

        public InjectionOutcome InjectText(String sessionKey, String text, Boolean pressEnter)
        {
            if (String.IsNullOrEmpty(text))
            {
                return InjectionOutcome.Skipped;
            }

            return this.RunInject(WindowsInjection.TextArgs(sessionKey, text, pressEnter));
        }

        public InjectionOutcome InjectKey(String sessionKey, KeyStroke key) =>
            this.RunInject(WindowsInjection.KeyArgs(sessionKey, key));

        public InjectionOutcome InjectTabThenEnter(String sessionKey) =>
            this.RunInject(WindowsInjection.TabThenEnterArgs(sessionKey));

        private InjectionOutcome RunInject(List<String> args)
        {
            // Null args means the session key didn't name a process we can reach — treat it as a
            // missing session rather than launching the helper with nothing to aim at.
            if (args == null)
            {
                PluginLog.Warning("WindowsPlatformBridge: no usable target session — nothing typed");
                return InjectionOutcome.SessionMissing;
            }

            var runner = this.InjectRunner ?? this.RunHelper;
            var outcome = WindowsInjection.OutcomeFor(runner(args));

            if (outcome != InjectionOutcome.Ok)
            {
                PluginLog.Warning($"WindowsPlatformBridge: injection skipped — {WindowsInjection.Explain(outcome)}");
            }
            return outcome;
        }

        private Int32? RunHelper(List<String> args)
        {
            var exe = this.HelperPath;
            if (exe == null)
            {
                PluginLog.Warning("WindowsPlatformBridge: claude-console-inject.exe not found in the plugin package");
                return null;
            }

            return BoundedProcess.RunForExitCode(exe, args, 15000);
        }

        // Resolved LAZILY on every use, not cached at construction: the SDK hands us the plugin
        // path in Plugin.AssemblyFilePath AFTER the bridge is created, so anything captured in a
        // field initialiser is null forever. Settable for tests.
        private String _helperPath;

        /// <summary>Where the inject helper lives — beside the plugin DLL. See PluginPaths.</summary>
        internal String HelperPath
        {
            get => _helperPath ?? PluginPaths.PackagedFile("claude-console-inject.exe");
            set => _helperPath = value;
        }

        // ------------------------------------------------------------------------------------------
        // Terminal control (Phase 3) — via wt.exe, the only supported way to drive Windows Terminal.
        //
        // TWO HONEST GAPS, both consequences of Windows Terminal having no automation API:
        //
        // 1. QueryFrontmostSession returns null — "I don't know". Nothing supported maps a terminal
        //    TAB to the process running in it, so we cannot say which session the user is looking
        //    at. This is NOT a silent failure: BridgeManager.TargetTty degrades through its other
        //    rules, so one session still works, and "exactly one session waiting on you" still
        //    works. With several idle sessions the user presses a session key first — which is the
        //    explicit, already-supported way to aim the keypad. Pinning is exact on Windows even
        //    though frontmost-tracking is not.
        //
        // 2. FocusSession raises the terminal WINDOW but cannot select the tab. Same root cause.
        //    Injection is unaffected — it addresses the console directly — so pressing a session
        //    key still aims every subsequent key at that session correctly. Only the "and show it
        //    to me" half is approximate.
        // ------------------------------------------------------------------------------------------

        /// <summary>Runs a terminal command. Injectable so navigation is testable without Windows.</summary>
        internal Func<String, List<String>, Boolean> TerminalRunner { get; set; }

        public String QueryFrontmostSession() => null;   // see gap 1 above

        public void Navigate(TerminalAction action)
        {
            var args = WindowsTerminalCli.ArgsFor(action);
            if (args == null)
            {
                // Cycling windows is an OS gesture wt.exe can't express. Better to say so than to
                // send something that does the wrong thing.
                PluginLog.Info($"WindowsPlatformBridge: {action} is not available on Windows Terminal");
                return;
            }

            this.RunTerminal(args);
        }

        public void LaunchClaudeInProject(String projectDir)
        {
            var args = WindowsTerminalCli.LaunchClaudeArgs(projectDir);
            if (args == null)
            {
                return;
            }

            this.RunTerminal(args);
        }

        public void FocusSession(String sessionKey)
        {
            // Best effort: bring the terminal window forward. The tab is not selectable (gap 2).
            if (WindowsInjection.TryParseSessionKey(sessionKey, out _, out _))
            {
                this.RunTerminal(WindowsTerminalCli.ArgsFor(TerminalAction.Activate));
            }
        }

        public void Alert()
        {
            var exe = this.HelperPath;
            if (exe != null)
            {
                BoundedProcess.RunForExitCode(exe, new List<String> { "beep" }, 5000);
            }
        }

        private void RunTerminal(List<String> args)
        {
            var runner = this.TerminalRunner;
            if (runner != null)
            {
                runner(WindowsTerminalCli.Exe, args);
                return;
            }

            // wt.exe is absent on stock Windows 10 (R9). A failure here is a logged no-op — the
            // keypad's typing keys are unaffected, only navigation is.
            if (BoundedProcess.RunForExitCode(WindowsTerminalCli.Exe, args, 10000) == null)
            {
                PluginLog.Warning("WindowsPlatformBridge: wt.exe unavailable — Windows Terminal is required for the navigation keys");
            }
        }
    }
}
