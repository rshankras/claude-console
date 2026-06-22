namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;

    using Loupedeck.ClaudeConsolePlugin.Models;

    /// <summary>
    /// Bridge Manager — Connects the Logitech Actions SDK plugin to Claude Code
    /// via file-based IPC in the system temp directory.
    ///
    /// Reads: state.json (statusline data)
    /// Writes: cmd-queue.jsonl (commands)
    /// </summary>
    public class BridgeManager
    {
        // The bash hook/statusline scripts hardcode /tmp, so the plugin MUST read/write there too.
        // On macOS Path.GetTempPath() returns /var/folders/.../T/ — a DIFFERENT dir — which is why
        // the live displays showed defaults. Match the scripts: /tmp on macOS/Linux, temp on Windows.
        private static readonly String TempDir =
            Environment.OSVersion.Platform == PlatformID.Win32NT ? Path.GetTempPath() : "/tmp";
        private static readonly String StateFile = Path.Combine(TempDir, "claude-console-state.json");
        private static readonly String CommandQueueFile = Path.Combine(TempDir, "claude-console-cmd-queue.jsonl");
        private static readonly String ActivityFile = Path.Combine(TempDir, "claude-console-activity.json");

        // Voice capture IPC (must match ClaudeVoiceHelper's defaults in tools/voice/).
        private static readonly String VoiceStopFile = Path.Combine(TempDir, "claude-console-voice.stop");
        private static readonly String VoiceTranscriptFile = Path.Combine(TempDir, "claude-console-voice-transcript.txt");
        private static readonly String VoiceHelperApp = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "claude-console", "ClaudeVoiceHelper.app");

        private Timer _pollTimer;
        private ClaudeState _currentState;
        private ActivityState _activity;

        public event Action<ClaudeState> OnStateChanged;
        public event Action<ActivityState> OnActivityChanged;

        public ClaudeState CurrentState => _currentState;
        public ActivityState CurrentActivity => _activity;

        // ------------------------------------------------------------------------------------------
        // Singleton — the SDK auto-discovers PluginDynamicCommand/Adjustment subclasses and
        // instantiates them with their parameterless constructors, so they cannot receive the
        // bridge by constructor injection. They pull the shared instance from here instead.
        // Lazy + locked so it is safe regardless of whether a command ctor or Plugin.Load()
        // touches it first.
        // ------------------------------------------------------------------------------------------
        private static readonly Object _instanceLock = new Object();
        private static BridgeManager _instance;

        public static BridgeManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        _instance ??= new BridgeManager();
                    }
                }
                return _instance;
            }
        }

        public void StartPolling()
        {
            _pollTimer = new Timer(PollState, null, 0, 500);
            PluginLog.Info("BridgeManager: Started polling state file every 500ms");
        }

        public void StopPolling()
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
            PluginLog.Info("BridgeManager: Stopped polling");
        }

        private void PollState(Object timerState)
        {
            try
            {
                var newState = ReadJsonWithRetry<ClaudeState>(StateFile);

                if (newState != null)
                {
                    _currentState = newState;
                    OnStateChanged?.Invoke(_currentState);
                }

                // Activity is pushed by the Claude Code hooks into a separate file; surface changes
                // so the Status key can flip between working / waiting / idle.
                var act = ReadActivity();
                if (act?.State != _activity?.State)
                {
                    _activity = act;
                    OnActivityChanged?.Invoke(_activity);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Verbose(ex, "BridgeManager: PollState error");
            }
        }

        // Read the hook-written activity flag (busy/waiting/done). A "busy" with no Stop for a long
        // while is treated as done, so a missed Stop hook can't leave the key stuck on "Working".
        private ActivityState ReadActivity()
        {
            if (!File.Exists(ActivityFile))
            {
                return null;
            }

            var a = ReadJsonWithRetry<ActivityState>(ActivityFile);
            if (a != null && a.State == "busy" &&
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() - a.Ts > 300)
            {
                a.State = "done";
            }
            return a;
        }

        /// <summary>
        /// Read a JSON file with retry logic to handle race conditions from concurrent writes.
        /// </summary>
        private T ReadJsonWithRetry<T>(String filePath, Int32 maxAttempts = 3, Int32 backoffMs = 10) where T : class
        {
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        return null;
                    }

                    var json = File.ReadAllText(filePath);
                    if (String.IsNullOrWhiteSpace(json))
                    {
                        continue;
                    }

                    return JsonSerializer.Deserialize<T>(json);
                }
                catch
                {
                    if (attempt < maxAttempts - 1)
                    {
                        Thread.Sleep(backoffMs * (attempt + 1));
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Write a command to the queue file (append-based to avoid overwrite races).
        /// </summary>
        public void SendCommand(String action, Dictionary<String, String> args = null)
        {
            var cmd = new Dictionary<String, Object>
            {
                { "action", action },
                { "args", args ?? new Dictionary<String, String>() },
                { "timestamp", DateTime.UtcNow.ToString("o") },
                { "session_id", "default" }
            };

            var line = JsonSerializer.Serialize(cmd);

            try
            {
                File.AppendAllText(CommandQueueFile, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "BridgeManager: Failed to append command");
            }
        }

        /// <summary>
        /// Send a keystroke command to the active terminal.
        /// </summary>
        public void SendKeystroke(String key)
        {
            SendCommand("keystroke", new Dictionary<String, String> { { "key", key } });
        }

        /// <summary>
        /// Send a prompt to Claude Code: log it to the queue (history / simulator parity) AND
        /// type it into the focused terminal so it actually runs on real hardware.
        /// </summary>
        public void SendPrompt(String prompt)
        {
            SendCommand("prompt", new Dictionary<String, String> { { "text", prompt } });
            InjectText(prompt, pressEnter: true);
        }

        /// <summary>
        /// Type text into the frontmost application (the user's terminal) and optionally press
        /// Return. macOS-only for now, via osascript + System Events. Requires the Logi Plugin
        /// Service to have Accessibility permission (granted once on first use).
        /// Newlines are flattened to spaces so a multi-line voice transcript doesn't submit early.
        /// </summary>
        public void InjectText(String text, Boolean pressEnter)
        {
            if (String.IsNullOrEmpty(text))
            {
                return;
            }

            if (!OperatingSystem.IsMacOS())
            {
                PluginLog.Info("BridgeManager.InjectText: non-macOS — text injection not implemented yet");
                return;
            }

            var flattened = text.Replace("\r", " ").Replace("\n", " ");
            var escaped = EscapeForAppleScript(flattened);

            var args = new List<String>
            {
                "-e", $"tell application \"System Events\" to keystroke \"{escaped}\""
            };
            if (pressEnter)
            {
                // A leading "/" opens Claude Code's slash-command autocomplete. Pressing Return
                // before it finishes filtering to the typed command selects whatever is highlighted
                // (often a recent command like /copy), so pause to let the menu settle first. Harmless
                // for plain text (Git/Prompts/voice) where no menu is shown.
                args.Add("-e");
                args.Add("delay 0.35");
                args.Add("-e");
                args.Add("tell application \"System Events\" to key code 36"); // Return
            }

            RunOsascript(args);
        }

        /// <summary>
        /// Send a single key chord to the focused terminal, e.g. Shift+Tab to toggle plan mode.
        /// <paramref name="appleScriptKeySpec"/> is the body of a System Events key statement,
        /// for example: <c>key code 48 using {shift down}</c> (key code 48 = Tab).
        /// </summary>
        public void InjectKeystroke(String appleScriptKeySpec)
        {
            if (!OperatingSystem.IsMacOS())
            {
                PluginLog.Info("BridgeManager.InjectKeystroke: non-macOS — keystroke injection not implemented yet");
                return;
            }

            RunOsascript(new List<String>
            {
                "-e", $"tell application \"System Events\" to {appleScriptKeySpec}"
            });
        }

        /// <summary>
        /// Accept the highlighted autocomplete AND submit it in one press: Tab, then Return after a
        /// short delay so the completion registers before Enter fires. macOS only.
        /// </summary>
        public void InjectTabThenEnter()
        {
            if (!OperatingSystem.IsMacOS())
            {
                PluginLog.Info("BridgeManager.InjectTabThenEnter: non-macOS — not implemented");
                return;
            }

            RunOsascript(new List<String>
            {
                "-e", "tell application \"System Events\" to key code 48", // Tab — accept the suggestion
                "-e", "delay 0.3",                                         // let the completion register
                "-e", "tell application \"System Events\" to key code 36", // Return — submit
            });
        }

        /// <summary>
        /// Run an arbitrary multi-line AppleScript via osascript (macOS only) — for automation
        /// richer than a single keystroke, e.g. clicking a menu item. Needs Accessibility.
        /// </summary>
        public void RunAppleScript(String script)
        {
            if (!OperatingSystem.IsMacOS())
            {
                PluginLog.Info("BridgeManager.RunAppleScript: non-macOS — skipped");
                return;
            }

            RunOsascript(new List<String> { "-e", script });
        }

        private static String EscapeForAppleScript(String s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private void RunOsascript(List<String> args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "osascript",
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
                if (proc != null)
                {
                    var outp = proc.StandardOutput.ReadToEnd();
                    var err = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(3000);
                    if (!String.IsNullOrWhiteSpace(outp))
                    {
                        PluginLog.Warning($"osascript out: {outp.Trim()}");
                    }
                    if (!String.IsNullOrWhiteSpace(err))
                    {
                        PluginLog.Warning($"osascript error: {err.Trim()}");
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "BridgeManager.RunOsascript: failed (is Accessibility permission granted?)");
            }
        }

        // ------------------------------------------------------------------------------------------
        // Voice capture — offline dictation via the bundled ClaudeVoiceHelper.app (whisper.cpp).
        // The helper is a separate signed app bundle so it can hold its OWN Microphone TCC grant;
        // LogiPluginService (a background daemon) cannot get mic access itself. Flow:
        //   press 1 -> StartVoiceCapture launches the helper (records 16kHz WAV, polls the stop flag)
        //   press 2 -> StopVoiceCapture writes the stop flag; the helper transcribes -> writes the
        //              transcript; a background thread reads it and types it into the focused terminal.
        // ------------------------------------------------------------------------------------------
        public void StartVoiceCapture()
        {
            if (!OperatingSystem.IsMacOS())
            {
                PluginLog.Info("BridgeManager.StartVoiceCapture: non-macOS — not implemented");
                return;
            }

            // Clear any stale transcript/flag so we never type a previous result.
            TryDelete(VoiceTranscriptFile);
            TryDelete(VoiceStopFile);

            if (!Directory.Exists(VoiceHelperApp))
            {
                PluginLog.Warning($"BridgeManager.StartVoiceCapture: helper missing at {VoiceHelperApp} (run tools/voice/build.sh)");
                return;
            }

            // Launch via LaunchServices (open) so the helper is its own TCC subject. Detached.
            RunDetached("open", new List<String>
            {
                VoiceHelperApp, "--args",
                "--maxsec", "60",
                "--stopflag", VoiceStopFile,
                "--transcript", VoiceTranscriptFile,
            });
            PluginLog.Info("BridgeManager.StartVoiceCapture: helper launched");
        }

        // Stop voice capture and TYPE the transcript into the focused terminal (dictation).
        public void StopVoiceCapture() => StopVoiceCaptureThen(text => InjectText(text, pressEnter: true));

        // Stop voice capture and use the transcript to OPEN a project (new tab + cd + claude).
        public void StopVoiceCaptureForProject() => StopVoiceCaptureThen(NavigateToProjectByVoice);

        // Shared: signal the helper to stop, then wait for the transcript off the UI thread and run
        // <paramref name="handler"/> with it. An empty transcript (silence / mic denied) is ignored.
        private void StopVoiceCaptureThen(Action<String> handler)
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            try
            {
                File.WriteAllText(VoiceStopFile, "");
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "BridgeManager.StopVoiceCapture: failed to write stop flag");
                return;
            }

            new Thread(() =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(150);
                    if (!File.Exists(VoiceTranscriptFile))
                    {
                        continue;
                    }

                    String text;
                    try
                    {
                        text = File.ReadAllText(VoiceTranscriptFile).Trim();
                    }
                    catch
                    {
                        continue; // still being written — retry
                    }

                    TryDelete(VoiceTranscriptFile);
                    if (!String.IsNullOrWhiteSpace(text))
                    {
                        PluginLog.Info($"BridgeManager: transcript ({text.Length} chars): {text}");
                        try { handler(text); }
                        catch (Exception ex) { PluginLog.Warning(ex, "BridgeManager: transcript handler failed"); }
                    }
                    else
                    {
                        PluginLog.Info("BridgeManager: empty transcript (silence or mic denied)");
                    }
                    return;
                }
                PluginLog.Warning("BridgeManager: transcript not produced within 20s");
            })
            { IsBackground = true, Name = "claude-voice-transcript" }.Start();
        }

        // Roots scanned (live) for voice project navigation — newly-added folders work with no code change.
        private static readonly String[] ProjectRoots =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Work", "MyApps"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Work"),
        };

        // Match a spoken phrase to a project folder, then open it (new Terminal tab + cd + claude).
        private void NavigateToProjectByVoice(String transcript)
        {
            var match = MatchProject(transcript);
            if (match == null)
            {
                PluginLog.Warning($"NavigateToProjectByVoice: no project matched \"{transcript}\"");
                RunAppleScript("beep"); // audible "didn't catch a project" feedback
                return;
            }
            PluginLog.Info($"NavigateToProjectByVoice: \"{transcript}\" -> {match}");
            LaunchClaudeInProject(match);
        }

        private static String MatchProject(String transcript)
        {
            var t = NormalizeForMatch(transcript);
            if (t.Length < 2)
            {
                return null;
            }

            String best = null;
            var bestScore = 0;
            foreach (var root in ProjectRoots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }
                foreach (var dir in Directory.GetDirectories(root))
                {
                    var f = NormalizeForMatch(Path.GetFileName(dir));
                    if (f.Length == 0)
                    {
                        continue;
                    }
                    var score = MatchScore(t, f);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = dir;
                    }
                }
            }

            // Require a real match (exact / prefix / substring / strong overlap) to avoid mis-launches.
            return bestScore >= 300 ? best : null;
        }

        // Lowercase, drop common filler/command words ("open the X project"), keep letters+digits only.
        private static String NormalizeForMatch(String s)
        {
            s = (s ?? "").ToLowerInvariant();
            foreach (var w in new[] { "go to", "switch to", "open", "launch", "the", "project", "folder", "claude" })
            {
                s = s.Replace(w, " ");
            }
            return new String(s.Where(Char.IsLetterOrDigit).ToArray());
        }

        private static Int32 MatchScore(String t, String f)
        {
            if (t == f) return 1000;
            if (f.StartsWith(t) || t.StartsWith(f)) return 700 + Math.Min(t.Length, f.Length);
            if (f.Contains(t) || t.Contains(f)) return 500 + Math.Min(t.Length, f.Length);

            // Fuzzy fallback: longest contiguous overlap, ≥4 chars and ≥50% of the shorter name.
            var lcs = LongestCommonSubstringLength(t, f);
            var shorter = Math.Min(t.Length, f.Length);
            if (lcs >= 4 && shorter > 0 && lcs * 2 >= shorter)
            {
                return 300 + lcs;
            }
            return 0;
        }

        private static Int32 LongestCommonSubstringLength(String a, String b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            var prev = new Int32[b.Length + 1];
            var best = 0;
            for (var i = 1; i <= a.Length; i++)
            {
                var cur = new Int32[b.Length + 1];
                for (var j = 1; j <= b.Length; j++)
                {
                    if (a[i - 1] == b[j - 1])
                    {
                        cur[j] = prev[j - 1] + 1;
                        if (cur[j] > best) best = cur[j];
                    }
                }
                prev = cur;
            }
            return best;
        }

        // cd into the project and run claude. Smart about where:
        //   • no Terminal window open      → open one and run there
        //   • front tab is an IDLE shell   → reuse it (this is the "empty terminal" case)
        //   • front tab is BUSY (claude/cmd running) → open a NEW tab, so we never type into a
        //     live session
        // Terminal's `busy` is false only at an idle shell prompt, which is exactly the signal we want.
        private void LaunchClaudeInProject(String path)
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }
            // Single-quote the path for the shell so spaces are safe (project paths have no quotes).
            var cmd = "cd '" + path + "' && claude";
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
            RunAppleScript(script);
        }

        private static void TryDelete(String path)
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }

        private void RunDetached(String file, List<String> args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                foreach (var a in args)
                {
                    psi.ArgumentList.Add(a);
                }
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"BridgeManager.RunDetached: failed to launch {file}");
            }
        }

    }
}
