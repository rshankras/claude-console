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
        [InlineData("--search")]
        [InlineData("--changes")]
        [InlineData("--projects")]
        [InlineData("--plugins")]
        [InlineData("--attach-files")]
        [InlineData("--permissions")]
        [InlineData("--scheduled")]
        [InlineData("--pull-requests")]
        [InlineData("--explore")]
        [InlineData("--quick-chat")]
        [InlineData("--label")]
        [InlineData("--text")]
        [InlineData("--send-label")]
        [InlineData("--expect-near")]
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
        [InlineData("selected")]
        [InlineData("searchPresent")]
        [InlineData("changesPresent")]
        [InlineData("projectsPresent")]
        [InlineData("pluginsPresent")]
        [InlineData("attachFilesPresent")]
        [InlineData("permissionsPresent")]
        [InlineData("scheduledPresent")]
        [InlineData("pullRequestsPresent")]
        [InlineData("explorePresent")]
        [InlineData("quickChatPresent")]
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
        public void The_expected_card_guard_refuses_with_its_own_error()
        {
            // "card-changed" is what makes the user LOOK — the whole point of the guard. The
            // C# side logs it verbatim; both sides of that string live here.
            Assert.Contains("\"card-changed\"", Source());
        }

        [Fact]
        public void The_surface_unavailable_report_exists()
        {
            // The screen-lock rule starts here: no web area → an explicit surface:false report,
            // never a healthy-looking empty one.
            Assert.Contains("\"surface\": false", Source());
        }

        [Fact]
        public void Running_uses_the_focused_modes_observed_sidebar_baseline()
        {
            // ChatGPT idle rows expose one image; Codex exposes two. Deriving the minimum makes
            // the additional spinner readable in either mode without mislabelling all Codex rows.
            Assert.Contains("let baselineImages = readings.map", Source());
            Assert.Contains("reading.images > baselineImages", Source());
        }

        [Fact]
        public void Every_operation_is_scoped_to_the_focused_window()
        {
            var source = Source();

            Assert.Contains("kAXFocusedWindowAttribute", source);
            Assert.Contains("kAXMainWindowAttribute", source);
            Assert.Contains("for w in targetWindows()", source);
            Assert.Contains("windows.count == 1 ? windows : []", source);
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
