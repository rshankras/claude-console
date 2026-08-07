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

        // Open a new tab, then run `claude` in it via `do script` (reliable command send — no
        // per-character keystroke timing). delay lets the new tab become front first.
        private const String NewClaudeScript =
            "tell application \"Terminal\"\n" +
            "  activate\n" +
            "  tell application \"System Events\" to keystroke \"t\" using command down\n" +
            "  delay 0.5\n" +
            "  do script \"claude\" in front window\n" +
            "end tell";

        // New WINDOW running `claude`: `do script` with no "in" target opens a fresh window.
        private const String NewClaudeWindowScript =
            "tell application \"Terminal\"\n" +
            "  activate\n" +
            "  do script \"claude\"\n" + // no target → new window
            "end tell";

        // Test seam: when set, replaces the real osascript invocation so the unit tests can assert
        // on the exact script + arguments a key press would send without driving the window server.
        // Null in production — nothing but the tests ever assigns it.
        internal Func<List<String>, Int32, Boolean, String> OsascriptRunner { get; set; }

        // Test seam for the process scan: lets the tests feed captured `ps` output.
        internal Func<String> PsRunner { get; set; }

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
            return output == null ? null : ClaudeProcessWatcher.TtysFrom(output);
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
            return NormalizeTty(this.RunOsascriptCapture(new List<String> { "-e", script }));
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
            var body = "tell application \"System Events\" to keystroke (item 2 of argv)\n";
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
                TerminalAction.NewTab => ActivateThen + "keystroke \"t\" using {command down}",           // Cmd+T
                TerminalAction.NewClaudeTab => NewClaudeScript,
                TerminalAction.NextTab => ActivateThen + "key code 48 using {control down}",              // Ctrl+Tab
                TerminalAction.PreviousTab => ActivateThen + "key code 48 using {control down, shift down}",
                TerminalAction.NewClaudeWindow => NewClaudeWindowScript,
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
            var cmd = "cd '" + projectDir + "' && claude";
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
                "      tell application \"System Events\" to keystroke \"t\" using command down\n" +
                "      delay 0.5\n" +
                "      do script \"" + cmd + "\" in front window\n" +
                "    end if\n" +
                "  end if\n" +
                "end tell";
            this.RunAppleScript(script);
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

        private String RunOsascriptCore(List<String> args, Int32 timeoutMs, Boolean wantOutput)
        {
            var runner = this.OsascriptRunner;
            if (runner != null)
            {
                return runner(args, timeoutMs, wantOutput);
            }

            return BoundedProcess.Run("osascript", args, timeoutMs, wantOutput);
        }

        // Like a fire-and-forget osascript but returns stdout (trimmed) — for querying state (e.g.
        // the frontmost Terminal tab's TTY) on the poll timer, so it uses a short, snappy timeout.
        private String RunOsascriptCapture(List<String> args) => this.RunOsascriptCore(args, 2000, wantOutput: true);

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
