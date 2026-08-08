namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    using Xunit;

    /// <summary>
    /// Windows injection (Phase 2), plugin side. The Win32 work is in the helper executable and
    /// needs hardware; everything here — what the helper is ASKED to do, and how its answer is
    /// interpreted — is pure and runs on macOS.
    ///
    /// The safety property under test is the same one InjectionGuardTests pins for macOS, restated
    /// for a different mechanism: a keypress reaches the intended Claude session or nothing at all.
    /// </summary>
    public class WindowsInjectionTests
    {
        private const String Key = "pid-1234-638900000000000000";

        private static List<String> Capture(Action<WindowsPlatformBridge> act, Int32 exitCode = 0)
        {
            List<String> seen = null;
            var bridge = new WindowsPlatformBridge
            {
                InjectRunner = args => { seen = args; return exitCode; },
            };
            act(bridge);
            return seen;
        }

        private static String ValueAfter(List<String> args, String flag)
        {
            var i = args.IndexOf(flag);
            return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        }

        // ---------------------------------------------------------------------------------------
        // Session keys → a verified target
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void A_session_key_round_trips_to_its_process()
        {
            var minted = WindowsProcessWatcher.SessionKeyFor(new WindowsProcessInfo
            {
                Pid = 4321,
                StartTime = new DateTime(2026, 8, 7, 10, 0, 0, DateTimeKind.Utc),
            });

            Assert.True(WindowsInjection.TryParseSessionKey(minted, out var pid, out var ticks));
            Assert.Equal(4321, pid);
            Assert.Equal(new DateTime(2026, 8, 7, 10, 0, 0, DateTimeKind.Utc).Ticks, ticks);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("ttys003")]          // a macOS key — wrong platform, must not parse
        [InlineData("pid-abc-123")]
        [InlineData("pid-1234")]         // no start time
        [InlineData("pid-0-123")]        // pid 0 is not a target
        [InlineData("garbage")]
        public void An_unusable_session_key_is_rejected(String key) =>
            Assert.False(WindowsInjection.TryParseSessionKey(key, out _, out _));

        [Fact]
        public void Every_injection_carries_the_start_time_not_just_the_pid()
        {
            // Windows recycles PIDs. If the helper were given only a PID, a stale session key could
            // attach to whatever unrelated process inherited that number — and type into it. The
            // start time is what makes the target verifiable, so it must be on EVERY command.
            foreach (var args in new[]
            {
                Capture(b => b.InjectText(Key, "hi", false)),
                Capture(b => b.InjectKey(Key, KeyStroke.Escape)),
                Capture(b => b.InjectTabThenEnter(Key)),
            })
            {
                Assert.Equal("1234", ValueAfter(args, "--pid"));
                Assert.Equal("638900000000000000", ValueAfter(args, "--start-ticks"));
            }
        }

        [Fact]
        public void An_unusable_target_types_nothing_and_reports_a_missing_session()
        {
            var ran = false;
            var bridge = new WindowsPlatformBridge { InjectRunner = _ => { ran = true; return 0; } };

            var outcome = bridge.InjectText("ttys003", "hello", pressEnter: true);

            Assert.Equal(InjectionOutcome.SessionMissing, outcome);
            Assert.False(ran);   // the helper is never launched without a target to aim at
        }

        // ---------------------------------------------------------------------------------------
        // What the helper is asked to do
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Text_travels_as_its_own_argument()
        {
            // Concatenating text into a command string would let quotes and ampersands in a voice
            // transcript break out of it. Same rule the macOS backend follows with argv item 2.
            var nasty = "say \"hi\" & del /q * | echo 'pwned'";

            var args = Capture(b => b.InjectText(Key, nasty, pressEnter: true));

            Assert.Equal(nasty, ValueAfter(args, "--text"));
            Assert.Single(args, a => a == nasty);
        }

        [Fact]
        public void Multiline_text_is_flattened_so_it_cannot_submit_early()
        {
            var args = Capture(b => b.InjectText(Key, "first\nsecond\r\nthird", pressEnter: true));

            Assert.Equal("first second  third", ValueAfter(args, "--text"));
        }

        [Fact]
        public void Empty_text_never_reaches_the_helper()
        {
            var ran = false;
            var bridge = new WindowsPlatformBridge { InjectRunner = _ => { ran = true; return 0; } };

            Assert.Equal(InjectionOutcome.Skipped, bridge.InjectText(Key, "", true));
            Assert.Equal(InjectionOutcome.Skipped, bridge.InjectText(Key, null, true));
            Assert.False(ran);
        }

        [Theory]
        [InlineData(true, "true")]
        [InlineData(false, "false")]
        public void Submit_intent_is_explicit(Boolean pressEnter, String expected)
        {
            var args = Capture(b => b.InjectText(Key, "draft", pressEnter));

            Assert.Equal(expected, ValueAfter(args, "--submit"));
        }

        [Fact]
        public void Keys_are_named_not_encoded_as_windows_scan_codes()
        {
            // The plugin stays platform-neutral right up to the helper boundary; VK translation is
            // the helper's job, so a wrong code can be fixed without touching the plugin.
            var args = Capture(b => b.InjectKey(Key, KeyStroke.ArrowDown));

            Assert.Equal("ArrowDown", ValueAfter(args, "--key"));
        }

        [Fact]
        public void Modifiers_are_passed_when_present_and_omitted_when_not()
        {
            var plain = Capture(b => b.InjectKey(Key, KeyStroke.Escape));
            var chord = Capture(b => b.InjectKey(Key, KeyStroke.ShiftTab));

            Assert.DoesNotContain("--mods", plain);
            Assert.Equal("Tab", ValueAfter(chord, "--key"));
            Assert.Equal("Shift", ValueAfter(chord, "--mods"));
        }

        [Fact]
        public void Multiple_modifiers_are_listed_in_a_fixed_order()
        {
            var args = Capture(b => b.InjectKey(Key, new KeyStroke(TerminalKey.Tab, KeyModifiers.Shift | KeyModifiers.Control)));

            Assert.Equal("Control,Shift", ValueAfter(args, "--mods"));
        }

        [Fact]
        public void TabThenEnter_is_one_helper_run()
        {
            // Two runs would mean two attach/detach cycles with a gap between them, and the
            // completion could register late. One command, one atomic write.
            var runs = 0;
            var bridge = new WindowsPlatformBridge { InjectRunner = _ => { runs++; return 0; } };

            bridge.InjectTabThenEnter(Key);

            Assert.Equal(1, runs);
        }

        [Fact]
        public void Each_command_names_its_verb_first()
        {
            Assert.Equal("text", Capture(b => b.InjectText(Key, "x", false))[0]);
            Assert.Equal("key", Capture(b => b.InjectKey(Key, KeyStroke.Return))[0]);
            Assert.Equal("tabenter", Capture(b => b.InjectTabThenEnter(Key))[0]);
        }

        // ---------------------------------------------------------------------------------------
        // How the helper's answer is read
        // ---------------------------------------------------------------------------------------

        [Theory]
        [InlineData(0, InjectionOutcome.Ok)]
        [InlineData(2, InjectionOutcome.SessionMissing)]
        [InlineData(5, InjectionOutcome.SessionElevated)]   // ERROR_ACCESS_DENIED across integrity levels
        [InlineData(3, InjectionOutcome.Failed)]
        [InlineData(99, InjectionOutcome.Failed)]           // unknown code is never success
        [InlineData(null, InjectionOutcome.Failed)]         // helper couldn't run / timed out
        public void Exit_codes_map_to_outcomes(Int32? exitCode, InjectionOutcome expected) =>
            Assert.Equal(expected, WindowsInjection.OutcomeFor(exitCode));

        [Fact]
        public void An_elevated_session_is_reported_distinctly_from_a_failure()
        {
            // Options+ runs unelevated, so an elevated Claude session is simply unreachable. The
            // user needs to be told that specifically — it looks like a broken plugin otherwise.
            var bridge = new WindowsPlatformBridge { InjectRunner = _ => WindowsInjection.ExitSessionElevated };

            Assert.Equal(InjectionOutcome.SessionElevated, bridge.InjectKey(Key, KeyStroke.Return));
            Assert.Contains("elevated", WindowsInjection.Explain(InjectionOutcome.SessionElevated));
        }

        // ---------------------------------------------------------------------------------------
        // The plugin ↔ helper contract
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Every_key_the_plugin_can_send_is_one_the_helper_understands()
        {
            // The helper is a separate executable, so the compiler cannot check this seam: renaming
            // a TerminalKey member would keep building and then silently do nothing on that key.
            // Read the helper's mapping table and compare it to the enum instead.
            var source = ReadHelperSource();
            var mapped = System.Text.RegularExpressions.Regex
                .Matches(source, @"""(\w+)"" => (?:Vk\w+|0x[0-9A-Fa-f]+)")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var key in Enum.GetNames<TerminalKey>())
            {
                Assert.True(mapped.Contains(key), $"claude-console-inject has no mapping for TerminalKey.{key}");
            }
        }

        [Fact]
        public void The_exit_codes_the_plugin_reads_are_the_ones_the_helper_returns()
        {
            var source = ReadHelperSource();

            Assert.Contains($"ExitOk = {WindowsInjection.ExitOk}", source);
            Assert.Contains($"ExitSessionMissing = {WindowsInjection.ExitSessionMissing}", source);
            Assert.Contains($"ExitFailed = {WindowsInjection.ExitFailed}", source);
            Assert.Contains($"ExitSessionElevated = {WindowsInjection.ExitSessionElevated}", source);
        }

        [Fact]
        public void The_helper_verifies_the_start_time_before_attaching()
        {
            // The plugin sends --start-ticks on every command; that only buys safety if the helper
            // actually checks it, and checks it BEFORE AttachConsole.
            var source = ReadHelperSource();

            var verify = source.IndexOf("VerifyStartTime", StringComparison.Ordinal);
            var attach = source.IndexOf("AttachConsole((UInt32)pid)", StringComparison.Ordinal);

            Assert.True(verify >= 0, "helper does not verify the start time");
            Assert.InRange(verify, 0, attach - 1);
        }

        private static String ReadHelperSource()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = System.IO.Path.Combine(
                    dir, "tools", "windows", "ClaudeConsoleInject", "Program.cs");
                if (System.IO.File.Exists(candidate))
                {
                    return System.IO.File.ReadAllText(candidate);
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate the inject helper source");
        }

        [Fact]
        public void A_missing_helper_is_a_failure_not_a_silent_success()
        {
            var bridge = new WindowsPlatformBridge { HelperPath = null };

            Assert.Equal(InjectionOutcome.Failed, bridge.InjectKey(Key, KeyStroke.Return));
        }

        [Fact]
        public void The_helper_is_located_relative_to_the_plugin_dll_not_the_host_process()
        {
            // THE BUG THAT SHIPPED: AppContext.BaseDirectory points at the SERVICE's directory when
            // the SDK loads a plugin, so the lookup logged "claude-console-inject.exe not found in
            // the plugin package" while the exe sat right beside the DLL. The SDK-provided
            // Plugin.AssemblyFilePath is the only reliable anchor — see PluginPaths.
            var dir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "cc-helper-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            var exe = System.IO.Path.Combine(dir, "claude-console-inject.exe");
            System.IO.File.WriteAllText(exe, "");

            var previous = PluginPaths.PluginAssemblyFilePath;
            try
            {
                PluginPaths.PluginAssemblyFilePath = System.IO.Path.Combine(dir, "ClaudeConsolePlugin.dll");

                Assert.Equal(exe, new WindowsPlatformBridge().HelperPath);
            }
            finally
            {
                PluginPaths.PluginAssemblyFilePath = previous;
                try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void The_helper_path_is_resolved_lazily_not_captured_at_construction()
        {
            // The SDK hands over Plugin.AssemblyFilePath AFTER the bridge is constructed, so a
            // path captured in a field initialiser is null forever.
            var previous = PluginPaths.PluginAssemblyFilePath;
            try
            {
                PluginPaths.PluginAssemblyFilePath = null;
                var bridge = new WindowsPlatformBridge();
                Assert.Null(bridge.HelperPath);

                var dir = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "cc-late-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "claude-console-inject.exe"), "");
                PluginPaths.PluginAssemblyFilePath = System.IO.Path.Combine(dir, "ClaudeConsolePlugin.dll");

                Assert.NotNull(bridge.HelperPath);   // same instance, now resolvable
                try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
            }
            finally
            {
                PluginPaths.PluginAssemblyFilePath = previous;
            }
        }
    }
}
