namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// The 1.4.0 safety property: a key press may only ever type into Claude's Terminal.app tab.
    /// Every injection must therefore (a) verify Terminal is running, (b) select the tab whose TTY
    /// we are tracking, (c) bail without typing if either fails — and it must all happen in ONE
    /// osascript run, so no app switch can slip between the focus and the keystrokes.
    ///
    /// These tests capture the osascript invocation instead of performing it (BridgeManager
    /// .OsascriptRunner), so they assert on exactly what a key press would send.
    /// </summary>
    public class InjectionGuardTests
    {
        // Captures the last osascript invocation; returns "ok" so the bridge sees a success.
        private sealed class Capture
        {
            public List<String> Args { get; private set; }
            public Int32 Calls { get; private set; }

            public String Script => this.Args != null && this.Args.Count > 1 ? this.Args[1] : null;

            // osascript arguments after "-e <script>" — what AppleScript sees as argv.
            public List<String> ScriptArgv =>
                this.Args != null && this.Args.Count > 2 ? this.Args.GetRange(2, this.Args.Count - 2) : new List<String>();

            public BridgeManager Bridge(String activeTty = null)
            {
                var bridge = new BridgeManager { ActiveTty = activeTty };
                bridge.OsascriptRunner = (args, timeout, wantOutput) =>
                {
                    this.Args = args;
                    this.Calls++;
                    return "ok";
                };
                return bridge;
            }
        }

        private static Int32 IndexOfBody(String script) =>
            script.IndexOf("tell application \"System Events\"", StringComparison.Ordinal);

        // ---------------------------------------------------------------------------------------
        // The guard itself
        // ---------------------------------------------------------------------------------------

        [Theory]
        [InlineData("text")]
        [InlineData("keystroke")]
        [InlineData("tabenter")]
        public void Every_injection_path_focuses_terminal_before_typing(String kind)
        {
            var capture = new Capture();
            var bridge = capture.Bridge("ttys003");

            switch (kind)
            {
                case "text": bridge.InjectText("hello", pressEnter: false); break;
                case "keystroke": bridge.InjectKey(KeyStroke.Escape); break;
                case "tabenter": bridge.InjectTabThenEnter(); break;
            }

            Assert.Equal(1, capture.Calls); // one osascript run — focus and typing are atomic
            var script = capture.Script;

            Assert.Contains("if application \"Terminal\" is not running then", script);
            Assert.Contains("beep", script);
            Assert.Contains("set selected tab of terminalWindow to terminalTab", script);

            // The focus guard must come BEFORE any System Events typing.
            var guardAt = script.IndexOf("tell application \"Terminal\"", StringComparison.Ordinal);
            Assert.InRange(guardAt, 0, IndexOfBody(script) - 1);
        }

        [Fact]
        public void Injection_targets_the_tracked_tab_by_device_path()
        {
            var capture = new Capture();

            capture.Bridge("ttys007").InjectKey(KeyStroke.Return);

            Assert.Equal("/dev/ttys007", capture.ScriptArgv[0]);
        }

        [Fact]
        public void Injection_passes_an_empty_target_when_no_tab_is_known()
        {
            // Unknown tab: still Terminal-only (the script activates Terminal and uses its front
            // tab), never "whatever app happens to be frontmost".
            var capture = new Capture();

            capture.Bridge(activeTty: null).InjectKey(KeyStroke.Return);

            Assert.Equal("", capture.ScriptArgv[0]);
            Assert.Contains("tell application \"Terminal\"", capture.Script);
        }

        // ---------------------------------------------------------------------------------------
        // Text handling
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Prompt_text_travels_as_an_argument_not_as_script_source()
        {
            // Injecting text into the script body would let quotes/backslashes break the script
            // (or worse). It must arrive as argv item 2.
            var capture = new Capture();
            var nasty = "say \"hi\" \\ && osascript -e 'beep'";

            capture.Bridge("ttys001").InjectText(nasty, pressEnter: true);

            Assert.Equal(nasty, capture.ScriptArgv[1]);
            Assert.DoesNotContain(nasty, capture.Script);
            Assert.Contains("keystroke (item 2 of argv)", capture.Script);
        }

        [Fact]
        public void Multiline_text_is_flattened_so_it_cannot_submit_early()
        {
            // A voice transcript with newlines would otherwise send the first line on its own.
            var capture = new Capture();

            capture.Bridge("ttys001").InjectText("first\nsecond\r\nthird", pressEnter: true);

            Assert.Equal("first second  third", capture.ScriptArgv[1]);
        }

        [Fact]
        public void Empty_text_sends_nothing_at_all()
        {
            var capture = new Capture();

            capture.Bridge("ttys001").InjectText("", pressEnter: true);
            capture.Bridge("ttys001").InjectText(null, pressEnter: true);

            Assert.Equal(0, capture.Calls);
        }

        [Fact]
        public void PressEnter_appends_Return_after_a_settling_delay()
        {
            // The delay lets Claude Code's slash-command menu filter down before Return picks an
            // entry — without it, "/compact" could fire whatever was highlighted.
            var capture = new Capture();

            capture.Bridge("ttys001").InjectText("/compact", pressEnter: true);

            var script = capture.Script;
            Assert.Contains("delay 0.35", script);
            Assert.Contains("key code 36", script);
            Assert.InRange(
                script.IndexOf("delay 0.35", StringComparison.Ordinal),
                0,
                script.IndexOf("key code 36", StringComparison.Ordinal) - 1);
        }

        [Fact]
        public void PressEnter_false_does_not_submit()
        {
            var capture = new Capture();

            capture.Bridge("ttys001").InjectText("draft", pressEnter: false);

            Assert.DoesNotContain("key code 36", capture.Script);
        }

        // ---------------------------------------------------------------------------------------
        // Key chords used by the action classes
        // ---------------------------------------------------------------------------------------

        // The neutral key vocabulary the action classes use, and the macOS key code each must
        // produce. These codes are load-bearing: a wrong one silently sends the wrong key.
        [Theory]
        [InlineData(TerminalKey.Escape, KeyModifiers.None, "key code 53")]              // ControlCommand
        [InlineData(TerminalKey.Tab, KeyModifiers.Shift, "key code 48 using {shift down}")] // Mode
        [InlineData(TerminalKey.ArrowUp, KeyModifiers.None, "key code 126")]            // AnswerCommand
        [InlineData(TerminalKey.ArrowDown, KeyModifiers.None, "key code 125")]          // AnswerCommand
        [InlineData(TerminalKey.Return, KeyModifiers.None, "key code 36")]              // AnswerCommand
        [InlineData(TerminalKey.PageUp, KeyModifiers.None, "key code 116")]             // ScrollCommand
        [InlineData(TerminalKey.PageDown, KeyModifiers.None, "key code 121")]           // ScrollCommand
        public void Neutral_keys_map_to_the_expected_macOS_key_codes(
            TerminalKey key, KeyModifiers mods, String expectedSpec)
        {
            var capture = new Capture();

            capture.Bridge("ttys002").InjectKey(new KeyStroke(key, mods));

            Assert.Contains($"tell application \"System Events\" to {expectedSpec}", capture.Script);
        }

        [Fact]
        public void TabThenEnter_accepts_the_completion_before_submitting()
        {
            var capture = new Capture();

            capture.Bridge("ttys002").InjectTabThenEnter();

            var script = capture.Script;
            var tab = script.IndexOf("key code 48", StringComparison.Ordinal);
            var enter = script.IndexOf("key code 36", StringComparison.Ordinal);
            Assert.InRange(tab, 0, enter - 1);
            Assert.Contains("delay 0.3", script);
        }

        [Fact]
        public void SendPrompt_types_the_prompt_and_submits_it()
        {
            var capture = new Capture();

            capture.Bridge("ttys004").SendPrompt("/context");

            Assert.Equal("/context", capture.ScriptArgv[1]);
            Assert.Contains("key code 36", capture.Script);
        }
    }
}
