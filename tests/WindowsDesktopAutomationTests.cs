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
        public void Status_uses_windows_titles_and_the_same_snapshot_contract_as_macOS()
        {
            var (auto, calls) = Build(
                "{\"ok\":true,\"surface\":true,\"approvalPresent\":true,\"stopPresent\":false,\"mode\":\"Codex\"}");

            var snapshot = auto.Status();

            var args = Assert.Single(calls);
            Assert.Equal("status", args[0]);
            Assert.Contains("--require-process", args);
            Assert.Contains("--window", args);
            Assert.Contains("ChatGPT", args);
            Assert.Contains("--approve", args);
            Assert.Contains("Allow once", args);
            Assert.True(snapshot.SurfaceAvailable);
            Assert.True(snapshot.ApprovalPresent);
            Assert.Equal("Codex", snapshot.Mode);
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

            foreach (var verb in new[] { "inspect", "status", "press", "write", "focus" })
            {
                Assert.Contains($"\"{verb}\"", source);
            }
            foreach (var argument in new[]
            {
                "--window", "--approve", "--deny", "--stop", "--attention", "--mode-prefix",
                "--process", "--require-process",
                "--conv-marker", "--state-awaiting", "--state-unread", "--label", "--text",
                "--send-label", "--expect-near",
            })
            {
                Assert.Contains($"\"{argument}\"", source);
            }

            Assert.Contains("ambiguous-app", source);
            Assert.Contains("ambiguous-composer", source);
            Assert.Contains("card-changed", source);
            Assert.Contains("SwitchToThisWindow", source);
            Assert.Contains("foregroundPid == (UInt32)targetPid", source);
            Assert.DoesNotContain("Allow once", source); // app knowledge stays in the adapter
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
