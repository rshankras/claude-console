namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The macOS backend: drives Terminal.app through osascript, discovers sessions with `ps`,
    /// and keys sessions by TTY (e.g. "ttys003") — the same key the bash statusline/hook scripts
    /// derive via `ps -o tty`, which is what lets the two sides agree on a filename.
    ///
    /// This is the original 1.x behavior, moved behind IPlatformBridge unchanged. The AppleScript
    /// here is verbatim: the focus guard, the settling delays, and the key codes are all
    /// load-bearing and pinned by tests.
    /// </summary>
    internal sealed class MacPlatformBridge : IPlatformBridge
    {
        public String Name => "macOS";

        public Boolean IsSupported => OperatingSystem.IsMacOS();

        // WHAT counts as a session, supplied at construction. The bridge never learns which agent
        // this describes — that is the whole point of keeping the two seams orthogonal. Defaults to
        // matching NOTHING: a bridge built before its product declares an agent must find no
        // sessions rather than another agent's.
        private readonly AgentProcessMatcher _matcher;

        // The CLI a new session runs. Data, not knowledge of an agent: the bridge opens a terminal
        // and types this, exactly as it would any other command.
        private readonly String _cliCommand;

        public MacPlatformBridge(AgentProcessMatcher matcher = null, String cliCommand = null)
        {
            this._matcher = matcher ?? AgentProcessMatcher.None;
            this._cliCommand = String.IsNullOrWhiteSpace(cliCommand) ? "claude" : cliCommand;
        }

        // ------------------------------------------------------------------------------------------
        // Guarded keystroke injection — every injection FIRST focuses the tracked Claude tab in
        // Terminal.app (verified by TTY), then types, all in ONE osascript run so no app switch can
        // slip in between. If Terminal isn't running or the tracked tab is gone, the script beeps
        // and types NOTHING — a key press can never land in Slack or a browser. Strict Terminal.app
        // by design (the profile is Terminal-bound). Ported from Vizhi's focusTerminal.
        // ------------------------------------------------------------------------------------------
        // argv item 1 = target tty ("/dev/ttysNNN"), or "" to use Terminal's front tab.
        internal const String FocusGuardScript =
            "on run argv\n" +
            "set targetTty to item 1 of argv\n" +
            "if application \"Terminal\" is not running then\n" +  // never auto-launch Terminal
            "  beep\n" +
            "  return \"no-terminal\"\n" +
            "end if\n" +
            "tell application \"Terminal\"\n" +
            "  activate\n" +
            "  if targetTty is not \"\" then\n" +
            "    set found to false\n" +
            "    repeat with terminalWindow in windows\n" +
            // Not every Terminal window has tabs — a settings/inspector window raises
            // "Can't get every tab of item N of every window" (-1728), which aborted the whole
            // script and left the plugin unable to focus OR type. Skip such windows instead.
            "      try\n" +
            "        repeat with terminalTab in tabs of terminalWindow\n" +
            "          if (tty of terminalTab as text) is targetTty then\n" +
            "            set selected tab of terminalWindow to terminalTab\n" +
            "            set index of terminalWindow to 1\n" +
            "            set found to true\n" +
            "            exit repeat\n" +
            "          end if\n" +
            "        end repeat\n" +
            "      end try\n" +
            "      if found then exit repeat\n" +
            "    end repeat\n" +
            "    if not found then\n" +
            "      beep\n" +
            "      return \"tab-missing\"\n" +
            "    end if\n" +
            "  end if\n" +
            "end tell\n" +
            "delay 0.05\n";  // let activation settle before System Events types

        // Bring Terminal to the front, then send a key chord to it. (Was NavCommand.ActivateThen.)
        private const String ActivateThen =
            "tell application \"Terminal\" to activate\n" +
            "delay 0.05\n" +
            "tell application \"System Events\" to ";

        // Open a new tab, then run the AGENT's cli in it via `do script` (reliable command send —
        // no per-character keystroke timing). delay lets the new tab become front first. Built
        // from _cliCommand, never a literal: these two were consts hardcoding "claude" long after
        // the rest of the bridge had learned the agent's name, so the Codex keypad's New key
        // opened a claude session — found only when a user read the key label.
        private String NewAgentTabScript() =>
            "tell application \"Terminal\"\n" +
            "  activate\n" +
            "  tell application \"System Events\" to key code 17 using command down\n" +
            "  delay 0.5\n" +
            "  do script \"" + this._cliCommand + "\" in front window\n" +
            "end tell";

        // New WINDOW running the agent: `do script` with no "in" target opens a fresh window.
        private String NewAgentWindowScript() =>
            "tell application \"Terminal\"\n" +
            "  activate\n" +
            "  do script \"" + this._cliCommand + "\"\n" + // no target → new window
            "end tell";

        // Test seam: when set, replaces the real osascript invocation so the unit tests can assert
        // on the exact script + arguments a key press would send without driving the window server.
        // Null in production — nothing but the tests ever assigns it.
        internal Func<List<String>, Int32, Boolean, String> OsascriptRunner { get; set; }

        // Test seam for the process scan: lets the tests feed captured `ps` output.
        internal Func<String> PsRunner { get; set; }

        // Skip-window for the frontmost probe after it overruns (#46). Instance state, and the poll
        // loop is non-overlapping, so it needs no locking.
        private readonly ProbeBackoff _frontmostBackoff = new ProbeBackoff();

        // ------------------------------------------------------------------------------------------
        // Discovery
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Which Terminal tabs are running Claude right now. Bounded and killed on hang, exactly like
        /// the osascript calls — a wedged `ps` must never be able to pile up poll threads.
        /// </summary>
        public HashSet<String> DiscoverSessions()
        {
            if (!OperatingSystem.IsMacOS() && this.PsRunner == null)
            {
                return null;
            }

            var output = this.RunCapture("/bin/ps", new List<String> { "-axo", "pid=,ppid=,tty=,command=" }, 5000);
            return output == null ? null : AgentProcessWatcher.TtysFrom(output, this._matcher);
        }

        /// <summary>
        /// The TTY (e.g. "ttys003") of the frontmost Terminal tab, or null if Terminal isn't the
        /// frontmost app / isn't running. Matches the key the bash scripts derive from `ps -o tty`.
        /// </summary>
        public String QueryFrontmostSession()
        {
            if (!OperatingSystem.IsMacOS() && this.OsascriptRunner == null)
            {
                return null;
            }

            // "... is running" avoids auto-LAUNCHING Terminal just to query it.
            var script =
                "if application \"Terminal\" is running then\n" +
                "  tell application \"Terminal\"\n" +
                "    try\n" +
                "      if frontmost then return tty of selected tab of front window\n" +
                "    end try\n" +
                "  end tell\n" +
                "end if\n" +
                "return \"\"";
            // #46: an overrun means the machine is not answering Apple Events, and the next probe
            // would ask the same stalled machine the same question — blocking the non-overlapping
            // poll loop for another full 2000ms to do it. Skip a few opportunities instead. An EMPTY
            // answer is not a failure and must not back off: it just means Terminal isn't frontmost.
            if (this._frontmostBackoff.ShouldSkip())
            {
                return null;
            }

            var raw = this.RunOsascriptCapture(new List<String> { "-e", script }, out var timedOut);
            if (timedOut)
            {
                this._frontmostBackoff.RecordTimeout();
                return null;
            }

            this._frontmostBackoff.RecordSuccess();
            return NormalizeTty(raw);
        }

        /// <summary>"/dev/ttys003" (osascript) -> "ttys003"; "ttys003" (ps) stays "ttys003".</summary>
        internal static String NormalizeTty(String raw)
        {
            if (String.IsNullOrWhiteSpace(raw))
            {
                return null;
            }
            var s = raw.Trim();
            var slash = s.LastIndexOf('/');
            if (slash >= 0)
            {
                s = s.Substring(slash + 1);
            }
            return s.Length > 0 && s != "??" ? s : null;
        }

        // ------------------------------------------------------------------------------------------
        // Injection
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Type text into the target tab and optionally press Return. The text travels as an
        /// osascript ARGUMENT — no AppleScript string escaping, so quotes/backslashes in a voice
        /// transcript can't break (or extend) the script. Newlines are flattened to spaces so a
        /// multi-line transcript doesn't submit early.
        /// </summary>
        /// <summary>
        /// Deliver the text (argv item 2) through the clipboard rather than System Events'
        /// `keystroke`.
        ///
        /// WHY. `keystroke` does not send characters — it asks macOS to press the keys that WOULD
        /// PRODUCE those characters under the CURRENT input source. On a non-US layout the terminal
        /// therefore receives something else entirely, silently: reproduced on hardware 2026-08-27
        /// with the Russian layout selected, where "the quick brown fox 123" arrived as
        /// "ффф ффффф ффффф ффф 123" — every letter collapsed to ф, digits and spaces intact. Every
        /// text-carrying action was affected: prompts, git, slash commands, voice transcripts and
        /// the Yes/No badges (QA retest of 2.0.1, finding 4; #22).
        ///
        /// A clipboard paste carries the characters themselves, so it is layout-independent and
        /// Unicode-safe — the same path a human uses. The text still travels as an osascript
        /// ARGUMENT, never interpolated into the script source, so quotes and backslashes in a
        /// transcript still cannot break out.
        ///
        /// The clipboard is saved and restored as a RECORD, which preserves every flavour the user
        /// had (an image, styled text) rather than flattening it to a string. Both halves are
        /// wrapped in try blocks: failing to save must not stop the injection, and failing to
        /// restore must not fail an injection that has already landed.
        ///
        /// The delay before restoring is not optional — Terminal reads the pasteboard when Cmd+V is
        /// handled, so restoring too early pastes the OLD clipboard.
        /// </summary>
        private const String PasteTextBody =
            "set savedClipboard to missing value\n" +
            "try\n" +
            "  set savedClipboard to (the clipboard as record)\n" +
            "end try\n" +
            "set the clipboard to (item 2 of argv)\n" +
            "delay 0.05\n" +
            "tell application \"System Events\" to key code 9 using command down\n" +   // Cmd+V
            "delay 0.2\n" +
            "if savedClipboard is not missing value then\n" +
            "  try\n" +
            "    set the clipboard to savedClipboard\n" +
            "  end try\n" +
            "end if\n";

        public InjectionOutcome InjectText(String sessionKey, String text, Boolean pressEnter)
        {
            if (String.IsNullOrEmpty(text))
            {
                return InjectionOutcome.Skipped;
            }

            if (!this.CanRun())
            {
                return InjectionOutcome.Unsupported;
            }

            var flattened = text.Replace("\r", " ").Replace("\n", " ");
            var body = PasteTextBody;
            if (pressEnter)
            {
                // A leading "/" opens Claude Code's slash-command autocomplete. Pressing Return
                // before it finishes filtering to the typed command selects whatever is highlighted
                // (often a recent command like /copy), so pause to let the menu settle first. Harmless
                // for plain text (Git/Prompts/voice) where no menu is shown.
                body += "delay 0.35\n" +
                        "tell application \"System Events\" to key code 36\n"; // Return
            }

            return this.RunGuardedInjection(sessionKey, body, flattened);
        }

        public InjectionOutcome InjectKey(String sessionKey, KeyStroke key)
        {
            if (!this.CanRun())
            {
                return InjectionOutcome.Unsupported;
            }

            return this.RunGuardedInjection(
                sessionKey, $"tell application \"System Events\" to {AppleScriptFor(key)}\n");
        }

        public InjectionOutcome InjectTabThenEnter(String sessionKey)
        {
            if (!this.CanRun())
            {
                return InjectionOutcome.Unsupported;
            }

            return this.RunGuardedInjection(
                sessionKey,
                "tell application \"System Events\" to key code 48\n" + // Tab — accept the suggestion
                "delay 0.3\n" +                                          // let the completion register
                "tell application \"System Events\" to key code 36\n"); // Return — submit
        }

        /// <summary>
        /// The neutral key vocabulary rendered as a System Events statement. These key codes are
        /// macOS virtual key codes and are load-bearing — pinned by KeyStrokeMappingTests.
        /// </summary>
        internal static String AppleScriptFor(KeyStroke stroke)
        {
            var code = stroke.Key switch
            {
                TerminalKey.Escape => 53,
                TerminalKey.Return => 36,
                TerminalKey.Tab => 48,
                TerminalKey.ArrowUp => 126,
                TerminalKey.ArrowDown => 125,
                TerminalKey.ArrowLeft => 123,
                TerminalKey.ArrowRight => 124,
                TerminalKey.PageUp => 116,
                TerminalKey.PageDown => 121,
                _ => throw new ArgumentOutOfRangeException(nameof(stroke), stroke.Key, "unmapped key"),
            };

            var spec = $"key code {code}";
            var mods = ModifierList(stroke.Modifiers);
            return mods == null ? spec : $"{spec} using {mods}";
        }

        // AppleScript modifier syntax: "using {shift down}" / "using {control down, shift down}".
        // Order is fixed (control, shift, alt, command) so the emitted script is deterministic.
        private static String ModifierList(KeyModifiers mods)
        {
            if (mods == KeyModifiers.None)
            {
                return null;
            }

            var parts = new List<String>();
            if ((mods & KeyModifiers.Control) != 0) { parts.Add("control down"); }
            if ((mods & KeyModifiers.Shift) != 0) { parts.Add("shift down"); }
            if ((mods & KeyModifiers.Alt) != 0) { parts.Add("option down"); }
            if ((mods & KeyModifiers.Command) != 0) { parts.Add("command down"); }
            return "{" + String.Join(", ", parts) + "}";
        }

        // Compose focus guard + injection body into one script and run it. textArg (when non-null)
        // becomes argv item 2 — passing text as an argument sidesteps AppleScript escaping entirely.
        private InjectionOutcome RunGuardedInjection(String sessionKey, String injectionBody, String textArg = null)
        {
            var script = FocusGuardScript + injectionBody + "return \"ok\"\n" + "end run";
            var args = new List<String>
            {
                "-e", script,
                String.IsNullOrEmpty(sessionKey) ? "" : "/dev/" + sessionKey,
            };
            if (textArg != null)
            {
                args.Add(textArg);
            }

            var result = this.RunOsascriptCore(args, 15000, wantOutput: true);
            var outcome = result switch
            {
                "ok" => InjectionOutcome.Ok,
                "no-terminal" => InjectionOutcome.NoTerminal,
                "tab-missing" => InjectionOutcome.SessionMissing,
                _ => InjectionOutcome.Failed,
            };

            if (outcome != InjectionOutcome.Ok)
            {
                PluginLog.Warning($"MacPlatformBridge: injection skipped ({result ?? "osascript failed"}) — Claude's Terminal tab is unavailable");
            }
            return outcome;
        }

        // ------------------------------------------------------------------------------------------
        // Focus and navigation
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Bring a specific Terminal tab to the front. Same tab-by-TTY script the injection guard
        /// uses, minus the typing.
        /// </summary>
        public void FocusSession(String sessionKey)
        {
            if (!this.CanRun())
            {
                return;
            }

            this.RunOsascriptCore(
                new List<String> { "-e", FocusGuardScript + "return \"ok\"\n" + "end run", "/dev/" + sessionKey },
                15000,
                wantOutput: true);
        }

        public void Navigate(TerminalAction action)
        {
            var script = action switch
            {
                TerminalAction.Activate => "tell application \"Terminal\" to activate",
                TerminalAction.NewTab => ActivateThen + "key code 17 using {command down}",                // Cmd+T (key code, not "t" — see KeyCodeT)
                TerminalAction.NewClaudeTab => this.NewAgentTabScript(),
                TerminalAction.NextTab => ActivateThen + "key code 48 using {control down}",              // Ctrl+Tab
                TerminalAction.PreviousTab => ActivateThen + "key code 48 using {control down, shift down}",
                TerminalAction.NewClaudeWindow => this.NewAgentWindowScript(),
                TerminalAction.NextWindow => ActivateThen + "key code 50 using {command down}",           // Cmd+`
                TerminalAction.PreviousWindow => ActivateThen + "key code 50 using {command down, shift down}",
                _ => null,
            };

            if (script != null)
            {
                this.RunAppleScript(script);
            }
        }

        /// <summary>
        /// cd into the project and run claude. Smart about where:
        ///   • no Terminal window open      → open one and run there
        ///   • front tab is an IDLE shell   → reuse it (this is the "empty terminal" case)
        ///   • front tab is BUSY (claude/cmd running) → open a NEW tab, so we never type into a
        ///     live session
        /// Terminal's `busy` is false only at an idle shell prompt, which is exactly the signal we want.
        /// </summary>
        public void LaunchClaudeInProject(String projectDir)
        {
            if (!this.CanRun())
            {
                return;
            }

            // Single-quote the path for the shell so spaces are safe (project paths have no quotes).
            this.LaunchShellCommand("cd '" + projectDir + "' && " + this._cliCommand);
        }

        // The busy-aware launch shared by LaunchClaudeInProject and LaunchAgentSession: reuse an
        // idle front tab, open a new one when it is busy, never type into a live session.
        private void LaunchShellCommand(String cmd)
        {
            if (!this.CanRun())
            {
                return;
            }

            var script =
                "tell application \"Terminal\"\n" +
                "  activate\n" +
                "  if (count of windows) is 0 then\n" +
                "    do script \"" + cmd + "\"\n" +
                "  else\n" +
                "    set isIdle to false\n" +
                "    try\n" +
                "      set isIdle to (busy of selected tab of front window is false)\n" +
                "    end try\n" +
                "    if isIdle then\n" +
                "      do script \"" + cmd + "\" in front window\n" +   // reuses the idle tab (NOT 'selected tab of' — that form no-ops)
                "    else\n" +
                "      tell application \"System Events\" to key code 17 using command down\n" +
                "      delay 0.5\n" +
                "      do script \"" + cmd + "\" in front window\n" +
                "    end if\n" +
                "  end if\n" +
                "end tell";
            this.RunAppleScript(script);
        }

        /// <summary>
        /// `screencapture -i`: the system's own region/window picker (the Shift+Cmd+4 gesture).
        /// Cancelling with Esc exits without writing a file, so file-exists IS the outcome — the
        /// tool's exit code does not distinguish the cases. The generous timeout is thinking time:
        /// the user is aiming a crosshair, and killing the picker under them takes the shot anyway.
        /// </summary>
        public Boolean CaptureScreenshotInteractive(String outputPath)
        {
            if (!OperatingSystem.IsMacOS())
            {
                return false;
            }

            BoundedProcess.Run("/usr/sbin/screencapture", new List<String> { "-i", outputPath }, 120000, wantOutput: false);

            if (!File.Exists(outputPath))
            {
                PluginLog.Info("MacPlatformBridge.CaptureScreenshotInteractive: no file — cancelled, or the service lacks a Screen Recording grant (System Settings → Privacy & Security)");
                return false;
            }

            return true;
        }

        public void LaunchAgentSession(String[] extraArgs)
        {
            var quoted = new List<String>();
            foreach (var arg in extraArgs ?? Array.Empty<String>())
            {
                // Single-quote for the shell, the same discipline LaunchClaudeInProject applies to
                // its path; embedded quotes get the standard '\'' splice.
                quoted.Add("'" + arg.Replace("'", "'\''") + "'");
            }

            this.LaunchShellCommand(this._cliCommand + " " + String.Join(" ", quoted));
        }

        public void Alert() => this.RunAppleScript("beep");

        /// <summary>
        /// Run an arbitrary multi-line AppleScript via osascript — for automation richer than a
        /// single keystroke. Needs Accessibility. Internal: nothing outside this backend should
        /// be composing AppleScript.
        /// </summary>
        internal void RunAppleScript(String script)
        {
            if (!this.CanRun())
            {
                PluginLog.Info("MacPlatformBridge.RunAppleScript: non-macOS — skipped");
                return;
            }

            this.RunOsascriptCore(new List<String> { "-e", script }, 15000, wantOutput: false);
        }

        // In production this is "are we on macOS"; in tests, a stubbed runner stands in for the OS.
        private Boolean CanRun() => OperatingSystem.IsMacOS() || this.OsascriptRunner != null;

        // ------------------------------------------------------------------------------------------
        // Bounded subprocess plumbing
        // ------------------------------------------------------------------------------------------

        private String RunOsascriptCore(List<String> args, Int32 timeoutMs, Boolean wantOutput) =>
            this.RunOsascriptCore(args, timeoutMs, wantOutput, out _);

        private String RunOsascriptCore(List<String> args, Int32 timeoutMs, Boolean wantOutput, out Boolean timedOut)
        {
            var runner = this.OsascriptRunner;
            if (runner != null)
            {
                // A stubbed runner stands in for the OS and cannot overrun a budget it never waits on.
                // The backoff POLICY is covered directly by ProbeBackoffTests; this line is the seam,
                // not the behaviour.
                timedOut = false;
                return runner(args, timeoutMs, wantOutput);
            }

            return BoundedProcess.Run("osascript", args, timeoutMs, wantOutput, out timedOut);
        }

        // Like a fire-and-forget osascript but returns stdout (trimmed) — for querying state (e.g.
        // the frontmost Terminal tab's TTY) on the poll timer, so it uses a short, snappy timeout.
        private String RunOsascriptCapture(List<String> args, out Boolean timedOut) =>
            this.RunOsascriptCore(args, 2000, wantOutput: true, out timedOut);

        // Run a plain capture-only subprocess (the `ps` session scan) under the same hard-timeout
        // discipline as osascript.
        private String RunCapture(String file, List<String> args, Int32 timeoutMs)
        {
            var runner = this.PsRunner;
            if (runner != null)
            {
                return runner();
            }

            return BoundedProcess.Run(file, args, timeoutMs, wantOutput: true);
        }
    }
}
