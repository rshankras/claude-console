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
        public void View_changes_outlasts_the_helpers_own_wait_for_the_panel()
        {
            // The helper checks the target twice, presses, then waits up to 3 s for the panel;
            // the old 5 s budget could kill it inside that wait.
            var auto = new WindowsDesktopAutomation(new OpenAiDesktopAdapter());
            var budget = 0;
            auto.Runner = (_, timeout) => { budget = timeout; return "{\"ok\":true,\"opened\":true}"; };
            Assert.True(auto.OpenChanges(out _));
            Assert.True(budget >= 7000);
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
            // Windows only so far: the replies' per-turn review buttons.
            Assert.Equal("View changes", args[args.IndexOf("--changes-turn") + 1]);
            Assert.True(snapshot.SurfaceAvailable);
            Assert.True(snapshot.ApprovalPresent);
            Assert.Equal("Codex", snapshot.Mode);
        }

        [Fact]
        public void A_press_that_activated_the_app_hands_the_foreground_back_from_a_fresh_process()
        {
            var (auto, calls) = Build(
                "{\"ok\":true,\"matched\":\"Allow once\",\"frontMoved\":true,\"frontBeforeHwnd\":7014446}",
                "{\"ok\":true,\"restored\":true}");
            Assert.True(auto.PressGuarded(new[] { "Allow once" }, "npm install", out var matched, out _));
            Assert.Equal("Allow once", matched);
            Assert.Equal(2, calls.Count);
            Assert.Equal("restore-front", calls[1][0]);
            Assert.Equal("7014446", calls[1][calls[1].IndexOf("--hwnd") + 1]);
            Assert.Contains("ChatGPT", calls[1]);

            // Already in front, or the app had the foreground before: nothing to hand back.
            var (still, stillCalls) = Build("{\"ok\":true,\"matched\":\"Stop\",\"frontMoved\":false,\"frontBeforeHwnd\":1}");
            Assert.True(still.PressExact(new[] { "Stop" }));
            Assert.Single(stillCalls);

            // A refused restore is logged, never a failed press: the press already landed.
            var (refused, refusedCalls) = Build(
                "{\"ok\":true,\"matched\":\"Deny\",\"frontMoved\":true,\"frontBeforeHwnd\":5}",
                "{\"ok\":false,\"error\":\"restore-refused\"}");
            Assert.True(refused.Press(new[] { "Deny" }, out _));
            Assert.Equal(2, refusedCalls.Count);

            // The conversation key brings the app forward on purpose: no hand-back.
            var (open, openCalls) = Build("{\"ok\":true,\"matched\":\"Plan\",\"frontMoved\":true,\"frontBeforeHwnd\":5}");
            Assert.True(open.PressConversation("Plan"));
            Assert.Single(openCalls);
        }

        [Fact]
        public void The_remaining_verbs_send_the_macOS_argument_shapes()
        {
            var (auto, calls) = Build(
                "{\"ok\":true,\"target\":\"tok\",\"origin\":\"o\",\"query\":\"\",\"results\":[]}",
                "{\"ok\":true,\"opened\":true,\"alreadyOpen\":false}",
                "{\"ok\":true,\"requested\":\"start\"}",
                "{\"ok\":true,\"target\":\"tok\",\"fingerprint\":\"f\",\"hasContent\":true}",
                "{\"ok\":true,\"method\":\"value\",\"sent\":false}",
                "{\"ok\":true,\"attached\":true}",
                "{\"ok\":false,\"error\":\"files-changed\"}",
                "{\"ok\":true,\"text\":\"the answer\"}",
                "{\"ok\":true,\"image\":\"C:\\\\shot.png\",\"appName\":\"Mail\"}",
                "{\"ok\":true,\"matched\":\"Changes\"}");

            var search = auto.Search("open");
            Assert.True(search.Available);
            Assert.Equal("search", calls[0][0]);
            Assert.Equal("ChatGPT", calls[0][calls[0].IndexOf("--expect-mode") + 1]);
            Assert.Contains("--search-field", calls[0]);
            Assert.Contains("--result-host", calls[0]);
            Assert.Equal("Chats", calls[0][calls[0].IndexOf("--result-group") + 1]);

            Assert.True(auto.OpenChanges(out _));
            Assert.Equal("open-panel", calls[1][0]);
            Assert.Equal("Codex", calls[1][calls[1].IndexOf("--expect-mode") + 1]);
            Assert.Contains("--panel-open", calls[1]);
            Assert.Equal("View changes", calls[1][calls[1].IndexOf("--panel-open-turn") + 1]);
            Assert.Contains("--panel-visible", calls[1]);

            Assert.True(auto.SetVoiceChat(true, out _));
            Assert.Equal("voice", calls[2][0]);
            Assert.Equal("start", calls[2][calls[2].IndexOf("--action") + 1]);

            Assert.True(auto.SupportsAppend);
            var target = auto.PrepareAppend("ChatGPT", out _);
            Assert.Equal("append-target", calls[3][0]);
            Assert.True(target.HasContent);
            Assert.True(auto.AppendPreparedDraft("more", "ChatGPT", target, retry: false, out _));
            Assert.Equal("append", calls[4][0]);
            Assert.Equal("f", calls[4][calls[4].IndexOf("--expect-draft") + 1]);
            Assert.DoesNotContain("--accept-existing", calls[4]);

            Assert.True(auto.AttachPreparedImage("C:\\a.png", "ChatGPT", "tok", out _));
            Assert.Equal("attach-image", calls[5][0]);
            Assert.False(auto.AttachPreparedFiles(new[] { new DesktopFile("C:\\a.txt", 3, 4) }, "ChatGPT", "tok", out var error));
            Assert.Equal("files-changed", error);
            Assert.Equal("attach-files", calls[6][0]);
            Assert.Contains("--files", calls[6]);

            var copied = auto.Context("copy");
            Assert.Equal("the answer", copied.Text);
            Assert.Equal("copy-reply", calls[7][0]);
            Assert.Contains("--assistant-heading", calls[7]);

            var shot = auto.Context("screenshot");
            Assert.Equal("C:\\shot.png", shot.Image);
            Assert.Equal("context-screenshot", calls[8][0]);

            Assert.True(auto.PressInMode(new[] { "Changes" }, "Codex", out var matched));
            Assert.Equal("Changes", matched);
            Assert.Equal("Codex", calls[9][calls[9].IndexOf("--expect-mode") + 1]);
        }

        [Fact]
        public void An_unconfirmed_attachment_is_reported_as_such_when_the_helper_says_nothing()
        {
            var (auto, _) = Build("");
            Assert.False(auto.AttachPreparedImage("C:\\a.png", "ChatGPT", "tok", out var error));
            Assert.Equal("attachment-unconfirmed", error);
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
            Assert.Contains("--voice-end", args);
            Assert.Contains("--draft-placeholder", args);
            Assert.Contains("--composer-send-label", args);
            Assert.DoesNotContain("--expect-target", args);

            var (draft, draftCalls) = Build("{\"ok\":true,\"method\":\"value\",\"sent\":false}");
            Assert.True(draft.WriteComposer("hello", send: false, out _));
            Assert.DoesNotContain("--send-label", Assert.Single(draftCalls));
        }

        [Fact]
        public void Prepared_drafts_carry_the_target_through_write_and_send_like_macOS()
        {
            var (auto, calls) = Build(
                "{\"ok\":true,\"target\":\"tok\"}",
                "{\"ok\":true,\"method\":\"value\",\"sent\":false}",
                "{\"ok\":true,\"sent\":true}",
                "{\"ok\":false,\"error\":\"draft-changed\"}");

            Assert.Equal("tok", auto.PrepareDraft("Codex", requireEmpty: true, out var error));
            Assert.Null(error);
            Assert.Equal("draft-target", calls[0][0]);
            Assert.Equal("Codex", calls[0][calls[0].IndexOf("--expect-mode") + 1]);
            Assert.DoesNotContain("--allow-existing", calls[0]);

            Assert.True(auto.WritePreparedDraft("hello", "Codex", "tok", retry: true, out _));
            Assert.Equal("tok", calls[1][calls[1].IndexOf("--expect-target") + 1]);
            Assert.Contains("--accept-existing", calls[1]);

            Assert.True(auto.SendPreparedPrompt("hello", "Codex", "tok", out _));
            Assert.Equal("send", calls[2][0]);
            Assert.Equal("hello", calls[2][calls[2].IndexOf("--expect-text") + 1]);
            Assert.Equal("tok", calls[2][calls[2].IndexOf("--expect-target") + 1]);

            Assert.False(auto.SendComposer(out error));
            Assert.Equal("draft-changed", error);
            Assert.DoesNotContain("--expect-target", calls[3]);

            Assert.False(auto.SendPreparedPrompt("", "Codex", "tok", out error));
            Assert.Equal("empty-text", error);
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

            var helperDir = Path.GetDirectoryName(RepoFile("tools", "windows", "VizhiDesktopUia", "Program.cs"));
            var allSource = String.Join("\n", Directory.GetFiles(helperDir, "*.cs").Select(File.ReadAllText));
            foreach (var verb in new[]
            {
                "inspect", "frontmost", "status", "press", "press-exact", "restore-front", "voice", "open-panel",
                "draft-target", "append-target", "write", "append", "send", "attach-image", "attach-files",
                "copy-reply", "search", "focus",
            })
            {
                Assert.Contains($"\"{verb}\"", source);
            }
            foreach (var action in new[] { "return", "paste", "window", "screenshot", "clipboard", "selection" })
            {
                Assert.Contains($"case \"{action}\"", allSource);
            }
            foreach (var argument in new[]
            {
                "--window", "--approve", "--deny", "--stop", "--attention", "--mode-prefix",
                "--process", "--require-process",
                "--conv-marker", "--state-awaiting", "--state-unread", "--state-running", "--idle-images",
                "--voice-start", "--voice-end", "--panel-visible", "--panel-mode",
                "--label", "--text", "--send-label", "--expect-near", "--expect-mode", "--conversation",
                "--expect-target", "--expect-draft", "--accept-existing", "--draft-placeholder", "--image", "--files",
                "--copy-response", "--assistant-heading", "--search-field", "--result-host", "--source",
            })
            {
                Assert.Contains($"\"{argument}\"", allSource);
            }

            foreach (var refusal in new[]
            {
                "ambiguous-app", "ambiguous-composer", "ambiguous-conversation", "card-changed", "mode-changed",
                "surface-unavailable", "draft-exists", "draft-changed", "composer-target-changed", "attachment-unconfirmed",
                "clipboard-busy", "answer-changed", "search-target-changed", "query-changed",
            })
            {
                Assert.Contains($"\"{refusal}\"", allSource);
            }
            Assert.Contains("SwitchToThisWindow", allSource);
            Assert.Contains("ForegroundPid() == target.Pid", source);
            // The WPF wrapper is what made the helper framework-dependent (#83); COM only.
            Assert.DoesNotContain("using System.Windows.Automation", allSource);
            Assert.DoesNotContain("Allow once", allSource); // app knowledge stays in the adapter
            Assert.DoesNotContain("ChatGPT", allSource);
            // The only synthesised inputs: a chord posted after the focus was proven, and a
            // zero-distance mouse move that satisfies the foreground lock. Never a key on its own.
            Assert.DoesNotContain("keybd_event", allSource);
            Assert.Contains("MOUSEEVENTF_MOVE", allSource);

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
