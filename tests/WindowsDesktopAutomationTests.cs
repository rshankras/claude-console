namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    using Xunit;

    public sealed class WindowsDesktopAutomationTests
    {
        private static (WindowsDesktopAutomation auto, List<List<String>> calls) Build(params String[] replies)
        {
            var calls = new List<List<String>>();
            var queue = new Queue<String>(replies);
            var auto = new WindowsDesktopAutomation(new OpenAiDesktopAdapter())
            {
                Runner = (args, _) =>
                {
                    calls.Add(new List<String>(args));
                    return queue.Count > 0 ? queue.Dequeue() : null;
                },
            };
            return (auto, calls);
        }

        [Fact]
        public void Conversation_navigation_uses_exact_sidebar_selector()
        {
            var auto = new WindowsDesktopAutomation(new OpenAiDesktopAdapter());
            List<String> captured = null;
            auto.Runner = (args, _) =>
            {
                captured = args;
                return "{\"ok\":true}";
            };
            Assert.True(auto.PressConversation("Plan"));
            Assert.Contains("--conversation", captured);
            Assert.Contains("Pin chat", captured);
            Assert.Contains("Plan", captured);
            auto.Runner = (_, _) => "{\"ok\":false,\"error\":\"ambiguous-conversation\"}";
            Assert.False(auto.PressConversation("Plan"));
        }

        [Fact]
        public void Status_names_the_process_and_asks_the_same_questions_as_macOS()
        {
            var (auto, calls) = Build(
                "{\"ok\":true,\"surface\":true,\"approvalPresent\":true,\"stopPresent\":false,\"mode\":\"Codex\"}");

            var snapshot = auto.Status();

            var args = Assert.Single(calls);
            Assert.Equal("status", args[0]);
            Assert.Contains("--require-process", args);
            Assert.Equal("ChatGPT", args[args.IndexOf("--process") + 1]);
            Assert.Contains("--approve", args);
            Assert.Contains("Allow once", args);
            // The macOS client's status arguments, so one helper contract serves both.
            foreach (var flag in new[]
            {
                "--state-running", "--idle-images", "--voice-start", "--voice-end", "--send-label",
                "--panel-visible", "--panel-mode", "--search", "--changes", "--quick-chat",
            })
            {
                Assert.Contains(flag, args);
            }
            Assert.Contains("Codex=2", args);
            Assert.True(snapshot.SurfaceAvailable);
            Assert.True(snapshot.ApprovalPresent);
            Assert.Equal("Codex", snapshot.Mode);
        }

        [Fact]
        public void Frontmost_is_a_process_check_that_degrades_to_unknown()
        {
            var (auto, calls) = Build("{\"ok\":true,\"frontmost\":false}", "", "{\"ok\":false,\"error\":\"app-not-running\"}");
            Assert.False(auto.IsAppFrontmost());
            Assert.Equal("frontmost", calls[0][0]);
            Assert.Contains("--process", calls[0]);
            Assert.Null(auto.IsAppFrontmost());
            Assert.Null(auto.IsAppFrontmost());
        }

        [Fact]
        public void Send_after_write_carries_the_guards_that_make_send_refuse()
        {
            var (auto, calls) = Build("{\"ok\":true,\"method\":\"value\",\"sent\":true}");
            Assert.True(auto.WriteComposer("hello", send: true, out _));
            var args = Assert.Single(calls);
            Assert.Contains("--send-label", args);
            Assert.Contains("--stop", args);
            Assert.Contains("--approve", args);

            var (draft, draftCalls) = Build("{\"ok\":true,\"method\":\"value\",\"sent\":false}");
            Assert.True(draft.WriteComposer("hello", send: false, out _));
            Assert.DoesNotContain("--send-label", Assert.Single(draftCalls));
        }

        [Fact]
        public void Guarded_press_passes_the_rendered_card_and_surfaces_helper_errors()
        {
            var (auto, calls) = Build("{\"ok\":false,\"error\":\"ambiguous-app\"}");

            Assert.False(auto.PressGuarded(
                new[] { "Allow once" }, "Run npm install", out var matched, out var error));

            Assert.Null(matched);
            Assert.Equal("ambiguous-app", error);
            var args = Assert.Single(calls);
            Assert.Contains("--expect-near", args);
            Assert.Contains("Run npm install", args);
        }

        [Fact]
        public void Write_and_mode_switch_use_the_platform_neutral_adapter_labels()
        {
            var (write, writeCalls) = Build("{\"ok\":true,\"method\":\"value\",\"sent\":true}");
            Assert.True(write.WriteComposer("hello", send: true, out _));
            Assert.Contains("Send", Assert.Single(writeCalls));

            var (mode, modeCalls) = Build(
                "{\"ok\":true,\"matched\":\"Switch mode\"}",
                "{\"ok\":true,\"matched\":\"Codex Build, debug, and ship\"}");
            Assert.True(mode.SwitchMode("Codex"));
            Assert.Equal(2, modeCalls.Count);
            Assert.Contains("Codex Build, debug, and ship", modeCalls[1]);
        }

        [Fact]
        public void Windows_helper_source_keeps_the_fail_closed_wire_contract()
        {
            var source = File.ReadAllText(RepoFile(
                "tools", "windows", "VizhiDesktopUia", "Program.cs"));

            foreach (var verb in new[] { "inspect", "frontmost", "status", "press", "press-exact", "write", "focus" })
            {
                Assert.Contains($"\"{verb}\"", source);
            }
            foreach (var argument in new[]
            {
                "--window", "--approve", "--deny", "--stop", "--attention", "--mode-prefix",
                "--process", "--require-process",
                "--conv-marker", "--state-awaiting", "--state-unread", "--state-running", "--idle-images",
                "--voice-start", "--voice-end", "--panel-visible", "--panel-mode",
                "--label", "--text", "--send-label", "--expect-near", "--expect-mode", "--conversation",
            })
            {
                Assert.Contains($"\"{argument}\"", source);
            }

            Assert.Contains("ambiguous-app", source);
            Assert.Contains("ambiguous-composer", source);
            Assert.Contains("ambiguous-conversation", source);
            Assert.Contains("card-changed", source);
            Assert.Contains("mode-changed", source);
            Assert.Contains("surface-unavailable", source);
            Assert.Contains("SwitchToThisWindow", source);
            Assert.Contains("ForegroundPid() == target.Pid", source);
            // The WPF wrapper is what made the helper framework-dependent (#83); COM only.
            Assert.DoesNotContain("System.Windows.Automation", source);
            Assert.DoesNotContain("Allow once", source); // app knowledge stays in the adapter
            Assert.DoesNotContain("ChatGPT", source);

            var project = File.ReadAllText(RepoFile("tools", "windows", "VizhiDesktopUia", "VizhiDesktopUia.csproj"));
            Assert.Contains("<SelfContained>true</SelfContained>", project);
            Assert.Contains("<PublishTrimmed>true</PublishTrimmed>", project);
            Assert.Contains("<BuiltInComInteropSupport>true</BuiltInComInteropSupport>", project);
            Assert.DoesNotContain("<UseWPF>", project);
        }

        private static String RepoFile(params String[] parts)
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                dir = Path.GetDirectoryName(dir);
            }
            throw new FileNotFoundException(String.Join("/", parts));
        }
    }
}
