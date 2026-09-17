namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Xunit;

    public class DesktopVoiceShortcutTests
    {
        [Fact]
        public void User_confirmed_Control_Shift_V_is_one_targeted_shortcut_request()
        {
            var calls = new List<List<String>>();
            var auto = new MacDesktopAutomation(new OpenAiDesktopAdapter(), DesktopVoiceShortcut.Parse("Control+Shift+V"))
            {
                Runner = (args, timeout) =>
                {
                    calls.Add(args);
                    Assert.Equal(2500, timeout);
                    return "{\"ok\":true,\"requested\":\"shortcut\"}";
                },
            };
            Assert.True(auto.HasVoiceShortcut);
            Assert.True(auto.ToggleVoiceChat(out var error));
            Assert.Null(error);
            Assert.Equal(new[] { "shortcut", "--app", "com.openai.codex", "--key-code", "9",
                "--modifiers", "control,shift" }, Assert.Single(calls));
        }

        [Fact]
        public void No_configuration_means_no_assumed_hotkey()
        {
            var auto = new MacDesktopAutomation(new OpenAiDesktopAdapter())
                { Runner = (_, _) => throw new Exception("must not post an unconfigured shortcut") };
            Assert.False(auto.HasVoiceShortcut);
            Assert.False(auto.ToggleVoiceChat(out var error));
            Assert.Equal("shortcut-unconfigured", error);
        }

        [Theory]
        [InlineData("{\"ok\":false,\"error\":\"app-not-frontmost\"}", "app-not-frontmost")]
        [InlineData("{\"ok\":false,\"error\":\"not-trusted\"}", "not-trusted")]
        [InlineData(null, "helper produced no output (timeout, missing binary, or spawn failure)")]
        public void Posting_failure_is_propagated_without_retrying(String reply, String expected)
        {
            var calls = 0;
            var auto = new MacDesktopAutomation(new OpenAiDesktopAdapter(), DesktopVoiceShortcut.Parse("Control+Shift+V"))
                { Runner = (_, _) => { calls++; return reply; } };
            Assert.False(auto.ToggleVoiceChat(out var error));
            Assert.Equal(expected, error);
            Assert.Equal(1, calls);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("V")]
        [InlineData("Shift+V")]
        [InlineData("Control+Control+V")]
        [InlineData("Control++V")]
        [InlineData("Control+Return")]
        [InlineData("Control+Unknown+V")]
        [InlineData("Control+Shift+V; command")]
        public void Invalid_or_unmodified_input_cannot_become_a_shortcut(String value) => Assert.Null(DesktopVoiceShortcut.Parse(value));

        [Fact]
        public void Configuration_is_explicit_and_accepts_case_and_spacing_variations()
        {
            var path = Path.Combine(Path.GetTempPath(), "vizhi-voice-config-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Assert.Null(DesktopVoiceShortcut.Load(path));
                File.WriteAllText(path, "{\"toggleVoiceChat\":\" shift + CONTROL + v \"}");
                var shortcut = DesktopVoiceShortcut.Load(path);
                Assert.NotNull(shortcut);
                Assert.Equal(9, shortcut.KeyCode);
                Assert.Equal("control,shift", shortcut.Modifiers);
                foreach (var invalid in new[] { "{}", "{\"toggleVoiceChat\":false}", "{\"toggleVoiceChat\":\"\"}", "bad json", "[]" })
                {
                    File.WriteAllText(path, invalid);
                    Assert.Null(DesktopVoiceShortcut.Load(path));
                }
            }
            finally { File.Delete(path); }
        }
    }
}
