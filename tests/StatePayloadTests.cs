namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    using Loupedeck.ClaudeConsolePlugin.Models;

    using Xunit;

    /// <summary>
    /// Parsing the status-line payload.
    ///
    /// Regression tests for the 1.6.0 field bug: Claude Code sends `null` — not 0 — for figures it
    /// doesn't have yet, which is the normal state of a session that hasn't done any work. With
    /// those properties declared as non-nullable numbers, System.Text.Json threw, the ENTIRE state
    /// object failed to parse, the session was dropped from the grid, and the process scan re-added
    /// it as an unnamed "Claude". Every live key was affected, not just the grid.
    ///
    /// The payloads below are real, captured from this machine.
    /// </summary>
    public class StatePayloadTests
    {
        // A session that has just started: context and cost figures are all null.
        private const String FreshSessionPayload = @"{
          ""session_id"": ""3151050b-c228-4600-baae-5f702d550345"",
          ""cwd"": ""/Users/x/Work/MyApps/ChantFlow"",
          ""model"": { ""id"": ""claude-opus-5[1m]"", ""display_name"": ""Opus 5 (1M context)"" },
          ""workspace"": {
            ""current_dir"": ""/Users/x/Work/MyApps/ChantFlow"",
            ""project_dir"": ""/Users/x/Work/MyApps/ChantFlow"",
            ""added_dirs"": [],
            ""repo"": { ""host"": ""github.com"", ""owner"": ""x"", ""name"": ""ChantFlow"" }
          },
          ""version"": ""2.1.223"",
          ""cost"": { ""total_cost_usd"": 0, ""total_duration_ms"": 1380, ""total_lines_added"": 0 },
          ""context_window"": {
            ""total_input_tokens"": 0,
            ""total_output_tokens"": 0,
            ""context_window_size"": 1000000,
            ""current_usage"": null,
            ""used_percentage"": null,
            ""remaining_percentage"": null
          },
          ""exceeds_200k_tokens"": false
        }";

        // A session in flight: the same fields carry real numbers.
        private const String WorkingSessionPayload = @"{
          ""session_id"": ""15151a90-996d-4729-a44d-c28147a0f926"",
          ""session_name"": ""port-vizhi-features-claude-console"",
          ""model"": { ""display_name"": ""Opus 5 (1M context)"" },
          ""workspace"": { ""project_dir"": ""/Users/x/Work/MyApps/claude-console"" },
          ""cost"": { ""total_cost_usd"": 51.27918824999999, ""total_lines_added"": 1357 },
          ""context_window"": {
            ""total_input_tokens"": 353678,
            ""context_window_size"": 1000000,
            ""used_percentage"": 54
          }
        }";

        [Fact]
        public void A_fresh_session_with_null_figures_still_parses()
        {
            // This is THE regression: one null used to throw and take the whole object with it.
            var state = JsonSerializer.Deserialize<ClaudeState>(FreshSessionPayload);

            Assert.NotNull(state);
            Assert.Equal("/Users/x/Work/MyApps/ChantFlow", state.Workspace.ProjectDir);
            Assert.Null(state.ContextWindow.UsedPercentage);
        }

        [Fact]
        public void A_fresh_session_reports_unknown_context_not_zero()
        {
            // "0%" would be a lie — we simply don't know yet.
            var state = JsonSerializer.Deserialize<ClaudeState>(FreshSessionPayload);

            Assert.Null(SessionRegistry.ContextPercent(state));
        }

        [Fact]
        public void A_working_session_reports_its_context()
        {
            var state = JsonSerializer.Deserialize<ClaudeState>(WorkingSessionPayload);

            Assert.Equal(54, SessionRegistry.ContextPercent(state));
            Assert.Equal("port-vizhi-features-claude-console", state.SessionName);
            Assert.Equal(51.27918824999999m, state.Cost.TotalCostUsd);
        }

        [Theory]
        [InlineData(@"{""context_window"":{""used_percentage"":null,""context_window_size"":null,""total_input_tokens"":null}}")]
        [InlineData(@"{""cost"":{""total_cost_usd"":null,""total_lines_added"":null,""total_lines_removed"":null}}")]
        [InlineData(@"{""context_window"":null,""cost"":null,""model"":null,""workspace"":null}")]
        [InlineData(@"{""session_id"":null,""session_name"":null}")]
        public void Any_field_may_be_null(String payload)
        {
            // Claude Code has changed these payloads between versions before. Nothing here should be
            // able to throw — a surprising null must never blank the whole keypad again.
            var state = JsonSerializer.Deserialize<ClaudeState>(payload);

            Assert.NotNull(state);
            Assert.Null(SessionRegistry.ContextPercent(state));
        }

        // --- end to end through the grid ------------------------------------------------------

        [Fact]
        public void A_fresh_session_shows_its_project_name_not_a_placeholder()
        {
            // The user-visible bug: every key read "Claude" instead of the project.
            var root = Path.Combine(Path.GetTempPath(), "cc-payload-" + Guid.NewGuid().ToString("N"));
            var sessions = Path.Combine(root, "sessions");
            var activity = Path.Combine(root, "activity");
            Directory.CreateDirectory(sessions);
            Directory.CreateDirectory(activity);
            try
            {
                File.WriteAllText(Path.Combine(sessions, "ttys002.json"), FreshSessionPayload);

                var grid = new SessionRegistry(sessions, activity, Path.Combine(root, "registry.json"));
                grid.Refresh(new HashSet<String> { "ttys002" });
                var session = grid.SlotSession(1);

                Assert.Equal("ChantFlow", session.Project);
                Assert.False(session.IsProvisional, "a session with a state file must not be provisional");
                Assert.Null(session.CtxPercent);
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
