namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    using Loupedeck.ClaudeConsolePlugin.Agents;

    using Xunit;

    /// <summary>
    /// The Windows state transport: codex's rollout transcript read directly, because its hook
    /// runner spawns nothing there (docs/spike-windows-codex-hooks.md).
    ///
    /// Two properties carry the design. First, it writes the SAME envelope the hook writes — the
    /// transport changed, the contract did not, so every reader above stays untouched. Second, an
    /// unstable format read defensively means a surprise produces NO update, never a guessed one:
    /// a keypad that stops updating disappoints, a keypad that lies is a bug.
    ///
    /// These run on any OS — the bridge is file IO; only its activation is platform-gated.
    /// </summary>
    public class CodexRolloutBridgeTests : IDisposable
    {
        private readonly String _root =
            Path.Combine(Path.GetTempPath(), "cx-rollout-" + Guid.NewGuid().ToString("N"));

        private readonly String _ipc =
            Path.Combine(Path.GetTempPath(), "cx-rollout-ipc-" + Guid.NewGuid().ToString("N"));

        private static readonly DateTime Today = new DateTime(2026, 8, 20, 14, 0, 0, DateTimeKind.Local);

        public void Dispose()
        {
            try { Directory.Delete(this._root, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(this._ipc, recursive: true); } catch { /* best effort */ }
        }

        private CodexRolloutBridge New() =>
            new CodexRolloutBridge(this._root, this._ipc) { Now = () => Today };

        /// <summary>Writes a rollout file in codex's own layout: sessions/yyyy/MM/dd/rollout-*.jsonl.</summary>
        private String Rollout(String name, DateTime day, params String[] lines)
        {
            var dir = Path.Combine(this._root, day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"rollout-{name}.jsonl");
            File.WriteAllText(path, String.Join("\n", lines) + "\n");
            return path;
        }

        private void Append(String path, String line) => File.AppendAllText(path, line + "\n");

        private String SharedState() =>
            File.ReadAllText(Path.Combine(this._ipc, "shared.json"));

        // The shapes observed in codex 0.148 rollouts (spike hardware).
        private const String TaskStarted =
            "{\"timestamp\":\"2026-08-20T14:00:01.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"model_context_window\":272000}}";

        private const String TaskComplete =
            "{\"timestamp\":\"2026-08-20T14:00:09.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"last_agent_message\":\"done\"}}";

        private const String TurnAborted =
            "{\"timestamp\":\"2026-08-20T14:00:05.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_aborted\"}}";

        private static String CodeModeExec(String callId, String command, String permission = "require_escalated",
            String timestamp = "2026-08-20T14:00:02.000Z")
        {
            var input = "const r = await tools.exec_command({cmd:"
                + JsonSerializer.Serialize(command)
                + ",workdir:\"C:\\\\Users\\\\me\\\\proj\",sandbox_permissions:"
                + JsonSerializer.Serialize(permission)
                + "});\ntext(r.output);";
            return JsonSerializer.Serialize(new
            {
                timestamp,
                type = "response_item",
                payload = new { type = "custom_tool_call", name = "exec", call_id = callId, input },
            });
        }

        private static String CodeModeOutput(String callId) => JsonSerializer.Serialize(new
        {
            timestamp = "2026-08-20T14:00:03.000Z",
            type = "response_item",
            payload = new { type = "custom_tool_call_output", call_id = callId, output = Array.Empty<Object>() },
        });

        // ------------------------------------------------------------------------------------
        // The translation: rollout events become the hook's envelope
        // ------------------------------------------------------------------------------------

        [Fact]
        public void A_started_task_becomes_the_busy_envelope()
        {
            Assert.Equal(CodexStateBridge.BusyEvent, CodexRolloutBridge.ActivityFor(TaskStarted));
            Assert.Equal("busy", CodexStateReader.ActivityFor(CodexRolloutBridge.ActivityFor(TaskStarted)));
        }

        [Fact]
        public void A_completed_task_becomes_the_idle_envelope()
        {
            Assert.Equal(CodexStateBridge.IdleEvent, CodexRolloutBridge.ActivityFor(TaskComplete));
            Assert.Equal("done", CodexStateReader.ActivityFor(CodexRolloutBridge.ActivityFor(TaskComplete)));
        }

        [Fact]
        public void An_interrupted_task_becomes_the_idle_envelope()
        {
            Assert.Equal(CodexStateBridge.IdleEvent, CodexRolloutBridge.ActivityFor(TurnAborted));
            Assert.Equal("done", CodexStateReader.ActivityFor(CodexRolloutBridge.ActivityFor(TurnAborted)));
        }

        /// <summary>
        /// Everything else is not information. Including events that exist but we have never
        /// mapped — inventing a state for an unknown event is exactly the guess this reader's
        /// contract forbids.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{ this is not json")]
        [InlineData("{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\"}}")]
        [InlineData("{\"type\":\"response_item\",\"payload\":{\"type\":\"message\"}}")]
        [InlineData("{\"payload\":{\"type\":\"task_someday\"}}")]
        public void Anything_unrecognised_produces_no_activity(String line) =>
            Assert.Null(CodexRolloutBridge.ActivityFor(line));

        /// <summary>The type may sit at the root or under payload; neither nesting is assumed.</summary>
        [Fact]
        public void The_type_is_found_at_either_nesting()
        {
            Assert.Equal(CodexStateBridge.BusyEvent,
                CodexRolloutBridge.ActivityFor("{\"type\":\"task_started\"}"));
            Assert.Equal(CodexStateBridge.BusyEvent,
                CodexRolloutBridge.ActivityFor("{\"payload\":{\"type\":\"task_started\"}}"));
        }

        // ------------------------------------------------------------------------------------
        // The cwd: what lets a key show the project's folder name instead of "Codex"
        // ------------------------------------------------------------------------------------

        [Fact]
        public void The_cwd_is_read_from_a_turn_context_record()
        {
            Assert.Equal(@"C:\Users\me\proj",
                CodexRolloutBridge.CwdFrom(TurnContext));
        }

        [Theory]
        [InlineData("")]
        [InlineData("{ not json \"cwd\"")]
        [InlineData("{\"type\":\"turn_context\",\"payload\":{\"cwd\":\"\"}}")]
        [InlineData("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}")]
        public void An_unreadable_or_absent_cwd_is_null(String line) =>
            Assert.Null(CodexRolloutBridge.CwdFrom(line));

        /// <summary>
        /// The envelope carries the last-reported cwd, backslashes intact through JSON escaping,
        /// so CodexStateReader's payload.cwd → ProjectDir path lights the label. Before this, the
        /// Windows transport wrote payload:null forever and every key could only say "Codex" —
        /// seen on hardware 2026-08-21.
        /// </summary>
        [Fact]
        public void The_envelope_carries_the_cwd_once_seen()
        {
            var path = this.Rollout("a", Today, TurnContext);
            var bridge = this.New();
            bridge.Poll();

            this.Append(path, TaskComplete);
            Assert.Equal(1, bridge.Poll());

            var snap = CodexStateReader.Parse(this.SharedState());
            Assert.Equal(@"C:\Users\me\proj", snap.ProjectDir);
        }

        /// <summary>
        /// Even with no cwd yet, the payload still carries transcript_path — it is the file being
        /// tailed, always known — so the context key can size the window from the first state
        /// write. The cwd is simply absent until a record reports one, never guessed.
        /// </summary>
        [Fact]
        public void Without_a_cwd_the_payload_still_carries_the_transcript_but_no_cwd()
        {
            var path = this.Rollout("a", Today, TaskStarted);
            var bridge = this.New();
            bridge.Poll();

            this.Append(path, TaskComplete);
            bridge.Poll();

            var state = this.SharedState();
            Assert.Contains("\"transcript_path\":", state);
            Assert.DoesNotContain("\"cwd\":", state);

            // And the reader turns it into the transcript the context percent is read from.
            Assert.Equal(path, CodexStateReader.Parse(state).TranscriptPath);
        }

        [Fact]
        public void Rollout_growth_refreshes_a_busy_session_even_without_another_lifecycle_edge()
        {
            var path = this.Rollout("busy-growth", Today, TaskStarted);
            var bridge = this.New();
            Assert.Equal(1, bridge.Poll());

            this.Append(path, "{\"timestamp\":\"2026-08-20T14:00:02.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\"}}");
            Assert.Equal(1, bridge.Poll());

            var snap = CodexStateReader.Parse(this.SharedState());
            Assert.Equal("busy", snap.Activity);
            Assert.NotNull(snap.TranscriptActivityTs);
        }

        [Fact]
        public void Rollout_heartbeat_cannot_overwrite_a_permission_hook()
        {
            var path = this.Rollout("approval", Today, TaskStarted);
            var bridge = this.New();
            Assert.Equal(1, bridge.Poll());

            Directory.CreateDirectory(this._ipc);
            File.WriteAllText(
                Path.Combine(this._ipc, "shared.json"),
                "{\"schema\":1,\"agent\":\"codex-cli\",\"transport\":\"hook\",\"event\":\"PermissionRequest\",\"ts\":1,\"payload\":{\"tool_name\":\"Bash\"}}");

            this.Append(path, "{\"timestamp\":\"2026-08-20T14:00:02.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\"}}");
            Assert.Equal(0, bridge.Poll());
            Assert.Equal("waiting", CodexStateReader.Parse(this.SharedState()).Activity);

            // A real terminal edge is stronger than the old approval and must clear it even if a
            // PostToolUse hook was missed.
            this.Append(path, TaskComplete);
            Assert.Equal(1, bridge.Poll());
            Assert.Equal("done", CodexStateReader.Parse(this.SharedState()).Activity);
        }

        /// <summary>
        /// The hook owns PermissionRequest and can write it between two of our polls. A lifecycle
        /// edge read afterwards says nothing about approvals, so it must not bury one: that turned
        /// the amber key grey with nothing left to re-emit it. A plugin reload hit this every time,
        /// because the wide first-sighting tail always finds a task_started to replay.
        /// </summary>
        [Fact]
        public void A_task_started_edge_cannot_overwrite_a_permission_hook()
        {
            var path = this.Rollout("edge-approval", Today, SessionMeta);
            var bridge = this.New();
            bridge.Poll();

            Directory.CreateDirectory(this._ipc);
            File.WriteAllText(
                Path.Combine(this._ipc, "shared.json"),
                "{\"schema\":1,\"agent\":\"codex-cli\",\"event\":\"PermissionRequest\",\"ts\":1,\"payload\":{\"tool_name\":\"Bash\"}}");

            this.Append(path, TaskStarted);
            bridge.Poll();
            Assert.Equal("waiting", CodexStateReader.Parse(this.SharedState()).Activity);

            // A terminal edge is still stronger than the approval, even if PostToolUse was missed.
            this.Append(path, TaskComplete);
            Assert.Equal(1, bridge.Poll());
            Assert.Equal("done", CodexStateReader.Parse(this.SharedState()).Activity);
        }

        /// <summary>Naming a session is never grounds for discarding a live approval on its key.</summary>
        [Fact]
        public void A_first_sighting_cannot_clobber_a_waiting_keyed_file()
        {
            var path = this.Rollout("first-sighting", Today, SessionMeta);
            Directory.CreateDirectory(this._ipc);
            File.WriteAllText(
                Path.Combine(this._ipc, "pid-100-cli.json"),
                "{\"schema\":1,\"agent\":\"codex-cli\",\"event\":\"PermissionRequest\",\"ts\":1,\"payload\":{\"tool_name\":\"Bash\"}}");

            var bridge = this.New();
            bridge.LiveSessions = new List<(String, DateTime)>
            {
                ("pid-100-cli", new DateTime(2026, 8, 20, 14, 0, 0, DateTimeKind.Utc)),
            };
            bridge.Poll();

            var keyed = CodexStateReader.Parse(
                File.ReadAllText(Path.Combine(this._ipc, "pid-100-cli.json")));
            Assert.Equal("waiting", keyed.Activity);
        }

        /// <summary>
        /// Preserving a waiting envelope must not freeze it: a SECOND approval is a new question,
        /// with its own command, and has to replace the first even though both read "waiting".
        /// </summary>
        [Fact]
        public void A_newer_code_mode_approval_still_replaces_an_older_one()
        {
            var path = this.Rollout(
                "two-approvals", Today, SessionMeta, TaskStarted,
                CodeModeExec("call-1", "git push origin feature"));
            var bridge = this.New();
            bridge.Poll();
            Assert.Equal("git push origin feature", CodexStateReader.Parse(this.SharedState()).PendingCommand);

            this.Append(path, CodeModeExec("call-2", "rm -rf build"));
            Assert.Equal(1, bridge.Poll());

            var snap = CodexStateReader.Parse(this.SharedState());
            Assert.Equal("waiting", snap.Activity);
            Assert.Equal("rm -rf build", snap.PendingCommand);
        }

        [Fact]
        public void A_code_mode_escalation_becomes_a_keyed_cli_approval()
        {
            var path = this.Rollout(
                "code-approval", Today, SessionMeta, TaskStarted,
                CodeModeExec("call-approval", "git push origin feature"));
            var bridge = this.New();
            bridge.LiveSessions = new List<(String, DateTime)>
            {
                ("pid-100-cli", new DateTime(2026, 8, 20, 14, 0, 0, DateTimeKind.Utc)),
            };

            Assert.Equal(1, bridge.Poll());

            var shared = CodexStateReader.Parse(this.SharedState());
            Assert.Equal("waiting", shared.Activity);
            Assert.Equal("Bash", shared.PendingTool);
            Assert.Equal("git push origin feature", shared.PendingCommand);
            Assert.Contains("\"transport\":\"rollout-code-mode\"", this.SharedState());

            var keyed = CodexStateReader.Parse(File.ReadAllText(Path.Combine(this._ipc, "pid-100-cli.json")));
            Assert.Equal("waiting", keyed.Activity);
            Assert.Equal("git push origin feature", keyed.PendingCommand);
        }

        [Fact]
        public void Reading_an_old_approval_now_preserves_its_original_event_timestamp()
        {
            this.Rollout("old-approval-proof", Today, TaskStarted, CodeModeExec("call-old", "git push"));
            var observed = new DateTime(2026, 8, 20, 14, 0, 10, DateTimeKind.Utc);
            var bridge = this.New();
            bridge.UtcNow = () => observed;
            Assert.Equal(1, bridge.Poll());

            using var state = JsonDocument.Parse(this.SharedState());
            var original = new DateTime(2026, 8, 20, 14, 0, 2, DateTimeKind.Utc);
            Assert.Equal(original.Ticks, state.RootElement.GetProperty("rolloutEventUtcTicks").GetInt64());
            Assert.Equal(observed.Ticks, state.RootElement.GetProperty("observationStartedUtcTicks").GetInt64());
            var failedAt = original.AddSeconds(1);
            Assert.True(state.RootElement.GetProperty("rolloutEventUtcTicks").GetInt64() < failedAt.Ticks);
        }

        [Fact]
        public void A_new_approval_after_recovery_carries_both_fresh_event_and_read_evidence()
        {
            var path = this.Rollout("new-approval-proof", Today, TaskStarted);
            var observed = new DateTime(2026, 8, 20, 14, 0, 10, DateTimeKind.Utc);
            var bridge = this.New();
            bridge.UtcNow = () => observed;
            bridge.Poll();
            var failedAt = observed;
            observed = observed.AddSeconds(2);
            this.Append(path, CodeModeExec("call-new", "git push", timestamp: "2026-08-20T14:00:11.000Z"));
            Assert.Equal(1, bridge.Poll());

            using var state = JsonDocument.Parse(this.SharedState());
            Assert.True(state.RootElement.GetProperty("rolloutEventUtcTicks").GetInt64() > failedAt.Ticks);
            Assert.Equal(observed.Ticks, state.RootElement.GetProperty("observationStartedUtcTicks").GetInt64());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("not-a-timestamp")]
        [InlineData("2099-08-20T14:00:02.000Z")]
        public void A_missing_invalid_or_future_event_timestamp_cannot_authorize_an_approval(String timestamp)
        {
            this.Rollout("invalid-approval-proof", Today, TaskStarted, CodeModeExec("call-unknown", "git push", timestamp: timestamp));
            var bridge = this.New();
            bridge.UtcNow = () => new DateTime(2026, 8, 20, 14, 0, 10, DateTimeKind.Utc);
            Assert.Equal(1, bridge.Poll());
            using var state = JsonDocument.Parse(this.SharedState());
            Assert.Equal("PermissionRequest", state.RootElement.GetProperty("event").GetString());
            Assert.False(state.RootElement.TryGetProperty("rolloutEventUtcTicks", out _));
            Assert.True(state.RootElement.TryGetProperty("observationStartedUtcTicks", out _));
        }

        [Fact]
        public void A_bounded_batch_does_not_authorize_a_request_before_its_unread_matching_output()
        {
            var path = this.Rollout("approval-before-unread-output", Today, TaskStarted);
            var bridge = this.New();
            bridge.Poll(); // Subsequent reads have the 64 KiB bound.
            this.Append(path, CodeModeExec("call-caught-up", "git push"));
            for (var index = 0; index < 1500; ++index)
            {
                this.Append(path, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"padding\":\"" + new String('x', 60) + "\"}}");
            }
            this.Append(path, CodeModeOutput("call-caught-up"));

            Assert.Equal(1, bridge.Poll());
            using (var incomplete = JsonDocument.Parse(this.SharedState()))
            {
                Assert.Equal("PermissionRequest", incomplete.RootElement.GetProperty("event").GetString());
                Assert.False(incomplete.RootElement.TryGetProperty("rolloutEventUtcTicks", out _));
            }
            for (var index = 0; index < 10; ++index)
            {
                bridge.Poll();
                using var state = JsonDocument.Parse(this.SharedState());
                Assert.False(state.RootElement.TryGetProperty("rolloutEventUtcTicks", out _));
            }
            Assert.Equal("busy", CodexStateReader.Parse(this.SharedState()).Activity);
        }

        [Fact]
        public void Catching_up_confirms_a_still_unresolved_request_without_changing_its_event_time()
        {
            var path = this.Rollout("approval-needs-complete-tail", Today, TaskStarted);
            var bridge = this.New();
            bridge.Poll();
            this.Append(path, CodeModeExec("call-waiting", "git push"));
            for (var index = 0; index < 1500; ++index)
            {
                this.Append(path, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"padding\":\"" + new String('x', 60) + "\"}}");
            }
            bridge.Poll();
            using (var incomplete = JsonDocument.Parse(this.SharedState()))
            {
                Assert.False(incomplete.RootElement.TryGetProperty("rolloutEventUtcTicks", out _));
            }
            for (var index = 0; index < 10; ++index) { bridge.Poll(); }
            using var complete = JsonDocument.Parse(this.SharedState());
            Assert.Equal("PermissionRequest", complete.RootElement.GetProperty("event").GetString());
            Assert.Equal(new DateTime(2026, 8, 20, 14, 0, 2, DateTimeKind.Utc).Ticks,
                complete.RootElement.GetProperty("rolloutEventUtcTicks").GetInt64());
        }

        [Fact]
        public void A_previously_confirmed_request_loses_authority_while_a_new_batch_is_incomplete()
        {
            var path = this.Rollout("approval-later-unread-output", Today, TaskStarted, CodeModeExec("call-1", "git push"));
            var bridge = this.New();
            bridge.Poll();
            using (var initial = JsonDocument.Parse(this.SharedState()))
            {
                Assert.True(initial.RootElement.TryGetProperty("rolloutEventUtcTicks", out _));
            }
            for (var index = 0; index < 1500; ++index)
            {
                this.Append(path, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"padding\":\"" + new String('x', 60) + "\"}}");
            }
            this.Append(path, CodeModeOutput("call-1"));
            Assert.Equal(1, bridge.Poll());
            using var incomplete = JsonDocument.Parse(this.SharedState());
            Assert.False(incomplete.RootElement.TryGetProperty("rolloutEventUtcTicks", out _));
        }

        [Fact]
        public void An_unterminated_new_record_revokes_earlier_code_mode_authority_until_complete()
        {
            var path = this.Rollout("approval-partial-output", Today, TaskStarted, CodeModeExec("call-1", "git push"));
            var bridge = this.New();
            bridge.Poll();
            var output = CodeModeOutput("call-1");
            File.AppendAllText(path, output.Substring(0, output.Length / 2));
            Assert.Equal(1, bridge.Poll());
            using (var incomplete = JsonDocument.Parse(this.SharedState()))
            {
                Assert.False(incomplete.RootElement.TryGetProperty("rolloutEventUtcTicks", out _));
            }
            File.AppendAllText(path, output.Substring(output.Length / 2) + "\n");
            Assert.Equal(1, bridge.Poll());
            Assert.Equal("busy", CodexStateReader.Parse(this.SharedState()).Activity);
        }

        [Fact]
        public void Ordinary_rollout_state_uses_the_scan_start_not_the_later_publication_time()
        {
            this.Rollout("scan-start-proof", Today, TaskStarted);
            var began = new DateTime(2026, 8, 20, 14, 0, 10, DateTimeKind.Utc);
            var calls = 0;
            var bridge = this.New();
            bridge.UtcNow = () => ++calls == 1 ? began : began.AddSeconds(10);
            Assert.Equal(1, bridge.Poll());
            using var state = JsonDocument.Parse(this.SharedState());
            Assert.Equal(began.Ticks, state.RootElement.GetProperty("observationStartedUtcTicks").GetInt64());
            Assert.False(state.RootElement.TryGetProperty("rolloutEventUtcTicks", out _));
        }

        [Fact]
        public void Truncating_the_rollout_cannot_confirm_an_old_cached_approval_against_its_new_end()
        {
            var path = this.Rollout("approval-truncated", Today, TaskStarted, CodeModeExec("call-old", "git push"));
            var bridge = this.New();
            bridge.Poll();
            File.WriteAllText(path, TaskStarted + "\n");
            bridge.Poll();
            using var state = JsonDocument.Parse(this.SharedState());
            Assert.False(state.RootElement.TryGetProperty("rolloutEventUtcTicks", out _));
        }

        [Fact]
        public void Matching_code_mode_output_clears_the_cli_approval()
        {
            var path = this.Rollout("code-output", Today, TaskStarted, CodeModeExec("call-1", "git push"));
            var bridge = this.New();
            Assert.Equal(1, bridge.Poll());
            Assert.Equal("waiting", CodexStateReader.Parse(this.SharedState()).Activity);

            this.Append(path, CodeModeOutput("some-other-call"));
            Assert.Equal(0, bridge.Poll());
            Assert.Equal("waiting", CodexStateReader.Parse(this.SharedState()).Activity);

            this.Append(path, CodeModeOutput("call-1"));
            Assert.Equal(1, bridge.Poll());
            Assert.Equal("busy", CodexStateReader.Parse(this.SharedState()).Activity);
            Assert.Null(CodexStateReader.Parse(this.SharedState()).PendingCommand);
        }

        [Fact]
        public void A_non_escalated_exec_never_claims_an_approval()
        {
            var diagnostic = "rg 'sandbox_permissions:\"require_escalated\"' src";
            var path = this.Rollout(
                "ordinary-exec", Today, TaskStarted,
                CodeModeExec("call-ordinary", diagnostic, permission: "use_default"));
            var bridge = this.New();

            Assert.Equal(1, bridge.Poll());
            var snap = CodexStateReader.Parse(this.SharedState());
            Assert.Equal("busy", snap.Activity);
            Assert.Null(snap.PendingTool);
        }

        [Fact]
        public void A_rollout_heartbeat_cannot_overwrite_a_synthetic_cli_approval()
        {
            var path = this.Rollout("approval-heartbeat", Today, TaskStarted, CodeModeExec("call-1", "git push"));
            var bridge = this.New();
            Assert.Equal(1, bridge.Poll());

            this.Append(path, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\"}}");
            Assert.Equal(0, bridge.Poll());
            Assert.Equal("waiting", CodexStateReader.Parse(this.SharedState()).Activity);
        }

        [Fact]
        public void A_reload_mid_turn_recovers_task_started_beyond_the_normal_poll_tail()
        {
            var toolOutput = "{\"ignored\":\"" + new String('x', 2 * 1024 * 1024) + "\"}";
            this.Rollout("reload-busy", Today, TaskStarted, toolOutput);

            var bridge = this.New();
            Assert.Equal(1, bridge.Poll());

            Assert.Equal("busy", CodexStateReader.Parse(this.SharedState()).Activity);
        }

        private const String TurnContext =
            "{\"timestamp\":\"2026-08-20T14:00:00.500Z\",\"type\":\"turn_context\",\"payload\":{\"turn_id\":\"t1\",\"cwd\":\"C:\\\\Users\\\\me\\\\proj\",\"workspace_roots\":[]}}";

        private const String SessionMeta =
            "{\"timestamp\":\"2026-08-20T14:00:05.000Z\",\"type\":\"session_meta\",\"payload\":{\"timestamp\":\"2026-08-20T14:00:00.000Z\",\"cwd\":\"C:\\\\Users\\\\me\\\\metadata-project\"}}";

        [Fact]
        public void Session_metadata_supplies_cwd_even_when_it_is_outside_the_catch_up_tail()
        {
            var filler = "{\"ignored\":\"" + new String('x', 70 * 1024) + "\"}";
            this.Rollout("metadata", Today, SessionMeta, filler, TaskStarted);
            var bridge = this.New();

            Assert.Equal(1, bridge.Poll());
            Assert.Equal(@"C:\Users\me\metadata-project", CodexStateReader.Parse(this.SharedState()).ProjectDir);
        }

        [Fact]
        public void Session_metadata_alone_replaces_missing_or_stale_per_session_state()
        {
            var path = this.Rollout("metadata-only", Today, SessionMeta);
            var bridge = this.New();
            var started = new DateTime(2026, 8, 20, 14, 0, 0, DateTimeKind.Utc);
            bridge.LiveSessions = new List<(String, DateTime)> { ("pid-100-real", started) };

            Assert.Equal(1, bridge.Poll());

            var state = File.ReadAllText(Path.Combine(this._ipc, "pid-100-real.json"));
            var snap = CodexStateReader.Parse(state);
            Assert.Equal(@"C:\Users\me\metadata-project", snap.ProjectDir);
            Assert.Equal("done", snap.Activity);
            Assert.False(File.Exists(Path.Combine(this._ipc, "shared.json")));
        }

        // ------------------------------------------------------------------------------------
        // Polling: only what is new, only what is complete
        // ------------------------------------------------------------------------------------

        [Fact]
        public void New_events_are_written_as_state()
        {
            var path = this.Rollout("a", Today, TaskStarted);
            var bridge = this.New();

            // First poll starts at the tail, so seed the offset, then append what we assert on.
            bridge.Poll();
            this.Append(path, TaskComplete);

            Assert.Equal(1, bridge.Poll());
            Assert.Contains("\"event\":\"" + CodexStateBridge.IdleEvent + "\"", this.SharedState());
        }

        [Fact]
        public void A_second_poll_with_nothing_new_writes_nothing()
        {
            var path = this.Rollout("a", Today, TaskStarted);
            var bridge = this.New();
            bridge.Poll();
            this.Append(path, TaskComplete);
            bridge.Poll();

            Assert.Equal(0, bridge.Poll());
        }

        /// <summary>
        /// The keys show a state, not a history: when a whole turn lands between two polls, the
        /// last event wins rather than the keypad flashing through busy on its way to done.
        /// </summary>
        [Fact]
        public void A_batch_containing_a_whole_turn_ends_in_the_last_state()
        {
            var path = this.Rollout("a", Today, TaskComplete);
            var bridge = this.New();
            bridge.Poll();

            this.Append(path, TaskStarted);
            this.Append(path, TaskComplete);
            bridge.Poll();

            Assert.Contains("\"event\":\"" + CodexStateBridge.IdleEvent + "\"", this.SharedState());
        }

        /// <summary>
        /// A line still being written must never be parsed in half — it is re-read whole on the
        /// next poll, when the writer has finished it.
        /// </summary>
        [Fact]
        public void A_half_written_line_is_not_parsed_until_it_is_complete()
        {
            var path = this.Rollout("a", Today, TaskStarted);
            var bridge = this.New();
            bridge.Poll();

            // No trailing newline: the writer is mid-line.
            File.AppendAllText(path, TaskComplete.Substring(0, 40));
            Assert.Equal(0, bridge.Poll());

            File.AppendAllText(path, TaskComplete.Substring(40) + "\n");
            Assert.Equal(1, bridge.Poll());
            Assert.Contains("\"event\":\"" + CodexStateBridge.IdleEvent + "\"", this.SharedState());
        }

        [Fact]
        public void A_missing_sessions_tree_is_survivable()
        {
            var bridge = new CodexRolloutBridge(
                Path.Combine(this._root, "nope"), this._ipc) { Now = () => Today };

            Assert.Equal(0, bridge.Poll());
        }

        /// <summary>Sessions span midnight, so yesterday's directory is still live.</summary>
        [Fact]
        public void Yesterdays_rollouts_are_still_read()
        {
            var path = this.Rollout("y", Today.AddDays(-1), TaskStarted);
            var bridge = this.New();
            bridge.Poll();
            this.Append(path, TaskComplete);

            Assert.Equal(1, bridge.Poll());
        }

        [Fact]
        public void Older_rollouts_are_ignored()
        {
            var path = this.Rollout("old", Today.AddDays(-5), TaskStarted);
            var bridge = this.New();
            bridge.Poll();
            this.Append(path, TaskComplete);

            Assert.Equal(0, bridge.Poll());
        }

        // ------------------------------------------------------------------------------------
        // Correlation: which key does this rollout belong to?
        // ------------------------------------------------------------------------------------

        [Fact]
        public void Production_poll_waits_for_independent_directory_discovery_before_consuming_a_rollout()
        {
            var start = DateTime.UtcNow;
            var path = this.Rollout("presskit", Today, Metadata(start, @"C:\demo\presskit"), TaskComplete);
            var bridge = new CodexRolloutBridge(this._root, this._ipc)
            {
                Now = () => Today, RequireSessionDirectories = true,
                LiveSessions = new[] { ("stage", start), ("presskit", start.AddSeconds(-5)) },
            };
            Assert.Equal(0, bridge.Poll());
            Assert.False(File.Exists(Path.Combine(this._ipc, "stage.json")));
            bridge.LiveSessionDirectories = new Dictionary<String, String>
            {
                ["stage"] = @"C:\demo\stage", ["presskit"] = @"C:\demo\presskit",
            };
            Assert.Equal(1, bridge.Poll());
            Assert.False(File.Exists(Path.Combine(this._ipc, "stage.json")));
            Assert.Equal(path, CodexStateReader.Parse(File.ReadAllText(Path.Combine(this._ipc, "presskit.json"))).TranscriptPath);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Production_retries_metadata_seen_before_the_first_record_is_complete(Boolean partial)
        {
            var start = DateTime.UtcNow;
            var path = this.Rollout("presskit-incomplete", Today);
            var metadata = Metadata(start.AddSeconds(2), @"C:\demo\presskit");
            File.WriteAllText(path, partial ? metadata.Substring(0, metadata.Length / 2) : "");
            var bridge = new CodexRolloutBridge(this._root, this._ipc)
            {
                Now = () => Today, RequireSessionDirectories = true,
                LiveSessions = new[] { ("presskit", start), ("stage", start.AddSeconds(2)) },
                LiveSessionDirectories = new Dictionary<String, String>
                {
                    ["presskit"] = @"C:\demo\presskit", ["stage"] = @"C:\demo\stage",
                },
            };
            Assert.Equal(0, bridge.Poll());
            Assert.Null(bridge.KeyFor(path));
            File.WriteAllText(path, metadata + "\n" + TaskComplete + "\n");
            Assert.Equal(1, bridge.Poll());
            Assert.False(File.Exists(Path.Combine(this._ipc, "stage.json")));
            Assert.Equal(@"C:\demo\presskit", CodexStateReader.Parse(
                File.ReadAllText(Path.Combine(this._ipc, "presskit.json"))).ProjectDir);
        }

        [Fact]
        public void Production_waits_for_the_target_directory_and_replays_an_unchanged_finished_rollout()
        {
            var start = DateTime.UtcNow;
            var path = this.Rollout("unknown-directory", Today,
                Metadata(start.AddSeconds(2), @"C:\demo\presskit"), TaskComplete);
            var directories = new Dictionary<String, String>();
            var bridge = new CodexRolloutBridge(this._root, this._ipc)
            {
                Now = () => Today, RequireSessionDirectories = true,
                LiveSessions = new[] { ("presskit", start) }, LiveSessionDirectories = directories,
            };
            Assert.Equal(0, bridge.Poll());
            Assert.Null(bridge.KeyFor(path));
            Assert.False(File.Exists(Path.Combine(this._ipc, "presskit.json")));
            directories["presskit"] = @"C:\demo\presskit";
            Assert.Equal(1, bridge.Poll());
            Assert.Equal(path, CodexStateReader.Parse(
                File.ReadAllText(Path.Combine(this._ipc, "presskit.json"))).TranscriptPath);
        }

        [Fact]
        public void Production_rejects_a_recent_transcript_from_before_the_process_started()
        {
            var start = DateTime.UtcNow;
            var old = this.Rollout("older-stage", Today,
                Metadata(start.AddSeconds(-30), @"C:\demo\stage"), TaskComplete);
            var bridge = new CodexRolloutBridge(this._root, this._ipc)
            {
                Now = () => Today, RequireSessionDirectories = true,
                LiveSessions = new[] { ("stage", start) },
                LiveSessionDirectories = new Dictionary<String, String> { ["stage"] = @"C:\demo\stage" },
            };
            Assert.Null(bridge.KeyFor(old));
            Assert.Equal(0, bridge.Poll());
            var current = this.Rollout("current-stage", Today,
                Metadata(start.AddSeconds(2), @"C:\demo\stage"), TaskComplete);
            Assert.Equal(1, bridge.Poll());
            Assert.Equal(current, CodexStateReader.Parse(
                File.ReadAllText(Path.Combine(this._ipc, "stage.json"))).TranscriptPath);
        }

        private static String Metadata(DateTime started, String cwd) => JsonSerializer.Serialize(new
        {
            type = "session_meta",
            payload = new { timestamp = started.ToString("O"), cwd },
        });

        [Fact]
        public void Closely_launched_projects_keep_their_own_labels_and_transcripts()
        {
            var start = new DateTime(2026, 8, 20, 14, 0, 0, DateTimeKind.Utc);
            var projects = new[] { "presskit", "faq", "stage" };
            var bridge = this.New();
            var directories = new Dictionary<String, String>();
            var live = new List<(String, DateTime)>();
            var paths = new Dictionary<String, String>();
            for (var i = 0; i < projects.Length; i++)
            {
                var project = projects[i];
                var cwd = @"C:\demo\repos\" + project;
                live.Add((project, start.AddSeconds(i * 2.5)));
                directories[project] = cwd;
                // Each transcript appears after the next CLI has started, as on the demo laptop.
                paths[project] = this.Rollout(project, Today,
                    Metadata(start.AddSeconds(5 + i * 4), cwd), TaskComplete);
            }
            bridge.LiveSessions = live;
            bridge.LiveSessionDirectories = directories;

            bridge.Poll();

            var grid = new SessionRegistry(this._ipc, Path.Combine(this._root, "activity"),
                Path.Combine(this._root, "registry.json")) { Agent = new CodexCliAdapter() };
            grid.Refresh(new HashSet<String>(projects));
            foreach (var project in projects)
            {
                Assert.Equal(project, grid.Sessions[project].Project);
                Assert.Equal(paths[project], grid.Sessions[project].TranscriptPath);
            }
        }

        [Fact]
        public void A_nearby_process_in_a_different_project_cannot_claim_a_rollout()
        {
            var start = DateTime.UtcNow;
            var path = this.Rollout("stage", Today, Metadata(start, @"C:\demo\stage"), TaskComplete);
            var bridge = this.New();
            bridge.LiveSessions = new[] { ("presskit", start) };
            bridge.LiveSessionDirectories = new Dictionary<String, String> { ["presskit"] = @"C:\demo\presskit" };

            Assert.Null(bridge.KeyFor(path));
        }

        [Theory]
        [InlineData(@"C:\demo\presskit")]
        [InlineData("c:/DEMO/presskit/")]
        public void A_known_project_match_beats_an_unknown_but_closer_process(String directory)
        {
            var start = DateTime.UtcNow;
            var path = this.Rollout("presskit", Today, Metadata(start, @"C:\demo\presskit"), TaskComplete);
            var bridge = this.New();
            bridge.LiveSessions = new[] { ("unknown", start), ("presskit", start.AddSeconds(-5)) };
            bridge.LiveSessionDirectories = new Dictionary<String, String> { ["presskit"] = directory };

            Assert.Equal("presskit", bridge.KeyFor(path));
        }

        [Fact]
        public void Newly_observed_directory_rejects_an_incorrect_cached_claim()
        {
            var start = DateTime.UtcNow;
            var path = this.Rollout("stage", Today, Metadata(start, @"C:\demo\stage"), TaskComplete);
            var bridge = this.New();
            bridge.LiveSessions = new[] { ("presskit", start), ("stage", start.AddSeconds(-5)) };
            Assert.Equal("presskit", bridge.KeyFor(path));
            bridge.LiveSessionDirectories = new Dictionary<String, String>
            {
                ["presskit"] = @"C:\demo\presskit", ["stage"] = @"C:\demo\stage",
            };

            Assert.Equal("stage", bridge.KeyFor(path));
        }

        [Fact]
        public void One_live_session_claims_the_rollout()
        {
            var path = this.Rollout("a", Today, TaskStarted);
            var created = new FileInfo(path).CreationTimeUtc;
            var bridge = this.New();
            bridge.LiveSessions = new List<(String, DateTime)> { ("pid-100-abc", created.AddSeconds(-1)) };

            Assert.Equal("pid-100-abc", bridge.KeyFor(path));
        }

        [Fact]
        public void An_old_rollout_does_not_claim_a_new_live_session()
        {
            var path = this.Rollout("old", Today, TaskStarted);
            var created = new FileInfo(path).CreationTimeUtc;
            var bridge = this.New();
            bridge.LiveSessions = new List<(String, DateTime)> { ("pid-100-new", created.AddHours(3)) };

            Assert.Null(bridge.KeyFor(path));
        }

        [Fact]
        public void Session_metadata_timestamp_correlates_the_rollout_instead_of_ntfs_creation_time()
        {
            var path = this.Rollout("metadata", Today, SessionMeta, TaskStarted);
            var bridge = this.New();
            bridge.LiveSessions = new List<(String, DateTime)>
            {
                ("pid-100-real", new DateTime(2026, 8, 20, 13, 59, 57, DateTimeKind.Utc)),
            };

            Assert.Equal("pid-100-real", bridge.KeyFor(path));
        }

        [Fact]
        public void With_no_live_session_nothing_is_claimed()
        {
            var path = this.Rollout("a", Today, TaskStarted);

            Assert.Null(this.New().KeyFor(path));
        }

        /// <summary>
        /// Two sessions that started within a second of each other cannot be told apart by start
        /// time. Claiming either would put one session's state on the other's key — the shared
        /// file still carries the state, so waiting costs nothing and guessing costs correctness.
        /// </summary>
        [Fact]
        public void An_ambiguous_match_claims_nothing()
        {
            var path = this.Rollout("a", Today, TaskStarted);
            var created = new FileInfo(path).CreationTimeUtc.ToLocalTime();

            var bridge = this.New();
            bridge.LiveSessions = new List<(String, DateTime)>
            {
                ("pid-100-abc", created.AddMilliseconds(-200)),
                ("pid-200-def", created.AddMilliseconds(200)),
            };

            Assert.Null(bridge.KeyFor(path));
        }

        [Fact]
        public void The_nearest_start_time_wins_when_it_is_clearly_nearest()
        {
            var path = this.Rollout("a", Today, TaskStarted);
            var created = new FileInfo(path).CreationTimeUtc.ToLocalTime();

            var bridge = this.New();
            bridge.LiveSessions = new List<(String, DateTime)>
            {
                ("pid-100-old", created.AddHours(-3)),
                ("pid-200-now", created.AddSeconds(-1)),
            };

            Assert.Equal("pid-200-now", bridge.KeyFor(path));
        }

        /// <summary>
        /// A claim sticks: re-deciding every poll could hand a session's key to a different
        /// rollout as processes come and go.
        /// </summary>
        [Fact]
        public void A_claim_is_kept_across_polls()
        {
            var first = this.Rollout("a", Today, TaskStarted);
            var created = new FileInfo(first).CreationTimeUtc;
            var bridge = this.New();
            bridge.LiveSessions = new List<(String, DateTime)> { ("pid-100-abc", created) };
            Assert.Equal("pid-100-abc", bridge.KeyFor(first));

            // A second rollout appears; the first session is already spoken for.
            var second = this.Rollout("b", Today, TaskStarted);
            Assert.Null(bridge.KeyFor(second));
            Assert.Equal("pid-100-abc", bridge.KeyFor(first));
        }

        [Fact]
        public void A_claim_lapses_when_its_session_dies()
        {
            var path = this.Rollout("a", Today, TaskStarted);
            var created = new FileInfo(path).CreationTimeUtc;
            var bridge = this.New();
            bridge.LiveSessions = new List<(String, DateTime)> { ("pid-100-abc", created) };
            bridge.KeyFor(path);

            bridge.LiveSessions = Array.Empty<(String, DateTime)>();

            Assert.Null(bridge.KeyFor(path));
        }

        /// <summary>
        /// Two sessions, two keys, and neither may ever carry the other's state — the acceptance
        /// check the design doc leads with.
        /// </summary>
        [Fact]
        public void Two_sessions_write_two_separate_state_files()
        {
            var a = this.Rollout("a", Today, TaskStarted);
            var b = this.Rollout("b", Today, TaskStarted);
            var createdA = new FileInfo(a).CreationTimeUtc.ToLocalTime();

            var bridge = this.New();
            bridge.LiveSessions = new List<(String, DateTime)>
            {
                ("pid-100-abc", createdA.AddSeconds(-1)),
                ("pid-200-def", createdA.AddHours(-2)),
            };

            bridge.Poll();
            this.Append(a, TaskComplete);
            this.Append(b, TaskComplete);
            bridge.Poll();

            Assert.True(File.Exists(Path.Combine(this._ipc, "pid-100-abc.json")));
            Assert.True(File.Exists(Path.Combine(this._ipc, "shared.json")));
        }

        /// <summary>
        /// The envelope must be what CodexStateReader already parses — that is the whole point of
        /// reusing it rather than inventing a Windows dialect.
        /// </summary>
        [Fact]
        public void The_envelope_is_the_one_the_reader_understands()
        {
            var path = this.Rollout("a", Today, TaskStarted);
            var bridge = this.New();
            bridge.Poll();
            this.Append(path, TaskComplete);
            bridge.Poll();

            var snapshot = CodexStateReader.Parse(this.SharedState());

            Assert.NotNull(snapshot);
            Assert.Equal("done", snapshot.Activity);
        }
    }
}
