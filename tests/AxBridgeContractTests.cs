namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;

    using Xunit;

    /// <summary>
    /// Pins the AX helper's command-line and JSON contract by reading its SOURCE — the
    /// WindowsVoiceTests pattern, applied to Swift for the first time. MacDesktopAutomation
    /// builds these argument names and DesktopSnapshot parses these JSON keys; the helper is a
    /// separate binary, so nothing but this test fails at build time when one side drifts.
    /// </summary>
    public class AxBridgeContractTests
    {
        private static String Source() =>
            File.ReadAllText(RepoFile("tools", "desktop", "VizhiAxBridge.swift"));

        [Theory]
        [InlineData("\"status\"")]
        [InlineData("\"press\"")]
        [InlineData("\"write\"")]
        [InlineData("\"focus\"")]
        public void The_verbs_exist(String verb)
        {
            Assert.Contains($"case {verb}", Source());
        }

        [Theory]
        [InlineData("--app")]
        [InlineData("--approve")]
        [InlineData("--deny")]
        [InlineData("--stop")]
        [InlineData("--attention")]
        [InlineData("--mode-prefix")]
        [InlineData("--conv-marker")]
        [InlineData("--state-awaiting")]
        [InlineData("--state-unread")]
        [InlineData("--label")]
        [InlineData("--text")]
        [InlineData("--send-label")]
        public void The_argument_names_match_what_the_client_sends(String arg)
        {
            Assert.Contains($"\"{arg}\"", Source());
        }

        [Theory]
        [InlineData("surface")]
        [InlineData("attention")]
        [InlineData("approvalPresent")]
        [InlineData("denyPresent")]
        [InlineData("stopPresent")]
        [InlineData("cardText")]
        [InlineData("mode")]
        [InlineData("conversations")]
        [InlineData("title")]
        [InlineData("state")]
        public void The_status_json_keys_match_what_the_snapshot_parses(String key)
        {
            Assert.Contains($"\"{key}\"", Source());
        }

        [Fact]
        public void The_helper_stays_app_agnostic()
        {
            // App knowledge lives in the C# adapter ONLY. The single permitted appearance of the
            // bundle id is the --app default; no control label may be hardcoded.
            var source = Source();

            Assert.DoesNotContain("Allow once", source);
            Assert.DoesNotContain("needs attention", source);
            Assert.DoesNotContain("Switch mode", source);
        }

        [Fact]
        public void The_surface_unavailable_report_exists()
        {
            // The screen-lock rule starts here: no web area → an explicit surface:false report,
            // never a healthy-looking empty one.
            Assert.Contains("\"surface\": false", Source());
        }

        /// <summary>Walks up from the test binary to a repo-relative file.</summary>
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

            throw new FileNotFoundException($"not found walking up from {AppContext.BaseDirectory}: {String.Join("/", parts)}");
        }
    }
}
