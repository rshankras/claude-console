namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;

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
        public void One_live_session_claims_the_rollout()
        {
            var path = this.Rollout("a", Today, TaskStarted);
            var bridge = this.New();
            bridge.LiveSessions = new List<(String, DateTime)> { ("pid-100-abc", Today) };

            Assert.Equal("pid-100-abc", bridge.KeyFor(path));
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
            var bridge = this.New();
            bridge.LiveSessions = new List<(String, DateTime)> { ("pid-100-abc", Today) };
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
            var bridge = this.New();
            bridge.LiveSessions = new List<(String, DateTime)> { ("pid-100-abc", Today) };
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
