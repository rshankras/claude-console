namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;

    public sealed class DesktopSearchTests
    {
        internal sealed class Fake : IDesktopAutomation
        {
            internal record Call(String Action, String Target, String Query, String Value, String Title, String Origin = null);
            internal List<Call> Calls = new();
            internal DesktopSearchSnapshot Next = Ready();
            internal DesktopSnapshot State = new() { SurfaceAvailable = true, Mode = "ChatGPT" };
            internal List<String> OtherCalls = new();
            public DesktopSearchSnapshot Search(String action, String target = null, String query = null, String value = null, String title = null, String origin = null)
            { Calls.Add(new(action, target, query, value, title, origin)); return Next; }
            public DesktopSnapshot Status() => State;
            public Boolean Press(String[] labels, out String matched) { matched = null; throw new Exception("unexpected generic press"); }
            public Boolean PressGuarded(String[] labels, String card, out String matched, out String error) { matched = error = null; throw new Exception("unexpected approval"); }
            public Boolean WriteComposer(String text, Boolean send, out String error) { error = null; throw new Exception("search reached composer"); }
            public Boolean FocusApp() { throw new Exception("search focused a different window"); }
            public Boolean SwitchMode(String mode) { throw new Exception("search switched modes"); }
            public Boolean PressInMode(String[] labels, String mode, out String matched)
            { matched = null; OtherCalls.Add(mode + ":" + String.Join("|", labels)); return true; }
        }
        internal static DesktopSearchSnapshot Ready(String query = "plate") => new()
        {
            Available = true, Target = "fixture-window-field", Query = query, Error = null,
            Results = new[] { new DesktopSearchResult("Plate duration", "/c/one"), new DesktopSearchResult("Plate design", "/c/two") },
        };

        [Fact]
        public void Native_search_arguments_are_separate_from_composer_and_preserve_text()
        {
            var automation = new MacDesktopAutomation(new OpenAiDesktopAdapter());
            List<String> actual = null;
            automation.Runner = (args, timeout) => { actual = args; Assert.Equal(5000, timeout); return "{\"ok\":true,\"target\":\"x\",\"query\":\"topic\",\"results\":[]}"; };
            Assert.True(automation.Search("write", "token", "old\nquery", "தமிழ் --send-label Send").Available);
            Assert.Equal("search", actual[0]);
            Assert.Equal("old\nquery", actual[actual.IndexOf("--query") + 1]);
            Assert.Equal("தமிழ் --send-label Send", actual[actual.IndexOf("--value") + 1]);
            Assert.DoesNotContain("--send-label", actual);
            Assert.Equal("ChatGPT", actual[actual.IndexOf("--expect-mode") + 1]);
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void Preparing_voice_never_starts_capture_and_can_retry_without_app_search(Boolean available)
        {
            var fake = new Fake { Next = available ? Ready() : new() { Error = "mode-unavailable" } };
            var search = new DesktopSearch(fake); search.Begin(); var capture = new VoiceCaptureState();
            var prepares = 0; var calls = fake.Calls.Count;
            for (var i = 0; i < 2; i++)
                DesktopSearchCommand.Execute("speak", search, new DesktopVoiceActions(fake), capture,
                    (_, _) => throw new Exception("Microphone must not start during preparation"),
                    () => throw new Exception("Must not navigate"), () => { prepares++; return false; });
            Assert.Equal(2, prepares); Assert.Equal(calls, fake.Calls.Count); Assert.Equal(VoicePhase.Idle, capture.Phase);
            DesktopSearchCommand.Execute("type", search, new DesktopVoiceActions(fake), capture,
                (_, _) => throw new Exception("Type query must not capture"), () => { },
                () => throw new Exception("Type query must not wait for voice model"));
            Assert.Equal(calls + 1, fake.Calls.Count);
        }

        [Theory]
        [InlineData(null)] [InlineData("oops")] [InlineData("[]")] [InlineData("null")]
        [InlineData("{\"ok\":true}")] [InlineData("{\"ok\":false,\"target\":\"x\"}")]
        public void Bad_helper_responses_never_enable_search(String json) => Assert.False(DesktopSearchSnapshot.Parse(json).Available);

        [Fact]
        public void Ambiguous_result_ids_are_removed_not_arbitrarily_selected()
        {
            var state = DesktopSearchSnapshot.Parse("{\"ok\":true,\"target\":\"x\",\"results\":[{\"title\":\"A\",\"id\":\"/c/a\"},{\"title\":\"B\",\"id\":\"/c/a\"},{\"title\":\"C\",\"id\":\"/c/c\"},42]}");
            Assert.Equal("C", Assert.Single(state.Results).Title);
        }

        [Fact]
        public void Polling_unchanged_results_does_not_reset_sdk_pagination()
        {
            var fake = new Fake(); var search = new DesktopSearch(fake); var updates = 0;
            search.Changed += () => updates++;
            search.Begin(); fake.Next = Ready(); search.Refresh(); search.Refresh();
            Assert.Equal(1, updates);
            fake.Next = Ready("new topic"); search.Refresh(); Assert.Equal(2, updates);
        }

        [Fact]
        public void Voice_uses_capture_start_query_even_after_user_types()
        {
            var fake = new Fake(); var search = new DesktopSearch(fake); search.Begin();
            var sink = search.CaptureSink();
            fake.Next = Ready("typed later"); search.Refresh();
            fake.Next = new() { Error = "query-changed" };
            Assert.Equal("Query changed", sink("spoken topic"));
            var write = fake.Calls.Last();
            Assert.Equal(new Fake.Call("write", "fixture-window-field", "plate", "spoken topic", null), write);
            Assert.Equal("Query changed", search.Feedback);
        }

        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void Back_or_reopening_cancels_late_transcript(Boolean reopen)
        {
            var fake = new Fake(); var search = new DesktopSearch(fake); search.Begin(); var sink = search.CaptureSink();
            search.End(); if (reopen) search.Begin();
            var count = fake.Calls.Count;
            Assert.Equal("Cancelled", sink("late topic")); Assert.Equal(count, fake.Calls.Count);
        }

        [Fact]
        public void Search_surface_loss_does_not_attach_to_a_new_search_on_timer()
        {
            var fake = new Fake(); var search = new DesktopSearch(fake); search.Begin();
            fake.Next = new() { Error = "search-target-changed" }; search.Refresh();
            var count = fake.Calls.Count; fake.Next = Ready(); search.Refresh(); Assert.Equal(count, fake.Calls.Count);
            Assert.Null(search.CaptureSink());
            search.TypeQuery(); Assert.Equal("open", fake.Calls.Last().Action); Assert.True(search.Current.Available);
        }

        [Fact]
        public void Delayed_field_is_observed_without_repressing_the_search_button()
        {
            var fake = new Fake { Next = new() { Origin = "window-1", Error = "search-field-missing" } };
            var search = new DesktopSearch(fake); Assert.False(search.Begin());
            search.Refresh();
            Assert.Equal(new Fake.Call("probe", null, null, null, null, "window-1"), fake.Calls.Last());
            fake.Next = Ready(); search.Refresh(); Assert.True(search.Current.Available);
            search.Refresh(); Assert.Equal("read", fake.Calls.Last().Action);
            Assert.Single(fake.Calls, c => c.Action == "open");
        }

        [Fact]
        public void Delayed_field_probe_stops_on_window_change_or_back()
        {
            var fake = new Fake { Next = new() { Origin = "window-1", Error = "search-field-missing" } };
            var search = new DesktopSearch(fake); search.Begin();
            fake.Next = new() { Origin = "window-2", Error = "search-target-changed" }; search.Refresh();
            var count = fake.Calls.Count; search.Refresh(); Assert.Equal(count, fake.Calls.Count);
            fake.Next = new() { Origin = "window-1", Error = "search-field-missing" }; search.Begin(); search.End();
            count = fake.Calls.Count; search.Refresh(); Assert.Equal(count, fake.Calls.Count);
        }

        [Fact]
        public void Explicit_retry_revokes_old_capture_even_if_the_app_reuses_its_search_field()
        {
            var fake = new Fake(); var search = new DesktopSearch(fake); search.Begin(); var sink = search.CaptureSink();
            fake.Next = new() { Error = "search-target-changed" }; search.Refresh();
            fake.Next = Ready(); search.TypeQuery(); var count = fake.Calls.Count;
            Assert.Equal("Cancelled", sink("old speech")); Assert.Equal(count, fake.Calls.Count);
        }

        [Theory]
        [InlineData("search-field-missing", "Search not ready", "Open in app")]
        [InlineData("search-container-missing", "Search layout", "Unsupported")]
        [InlineData("search-button-missing", "Open app search", "Then wait")]
        [InlineData("mode-unavailable", "Mode unreadable", "Retry")]
        [InlineData("search-focus-failed", "Click search", "In app")]
        [InlineData("app-not-frontmost", "Open ChatGPT", "Then retry")]
        public void Failure_faces_distinguish_the_next_action(String error, String label, String hint) =>
            Assert.Equal((label, hint), DesktopSearch.ProblemFor(error));

        [Fact]
        public void Failed_open_retains_only_its_window_identity_for_passive_recovery()
        {
            var state = DesktopSearchSnapshot.Parse("{\"ok\":false,\"origin\":\"window\",\"error\":\"search-field-missing\"}");
            Assert.False(state.Available); Assert.Equal("window", state.Origin); Assert.True(DesktopSearch.CanAwaitField(state));
            Assert.Null(new DesktopSearch(new Fake { Next = state }).CaptureSink());
        }

        [Fact]
        public void Result_selection_uses_exact_id_and_title_after_reorder()
        {
            var fake = new Fake(); var search = new DesktopSearch(fake); search.Begin();
            var parameter = search.ResultParameter(search.Current.Results[1]);
            fake.Next = Ready() with { Results = Ready().Results.Reverse().ToArray() }; search.Refresh();
            Assert.True(search.Select(parameter));
            Assert.Equal(new Fake.Call("select", "fixture-window-field", "plate", "/c/two", "Plate design"), fake.Calls.Last());
            Assert.False(search.Select(parameter));
        }

        [Theory]
        [InlineData("query")] [InlineData("session")] [InlineData("missing")]
        public void Old_result_cards_never_select_new_results(String change)
        {
            var fake = new Fake(); var search = new DesktopSearch(fake); search.Begin();
            var parameter = search.ResultParameter(search.Current.Results[0]);
            if (change == "session") search.Begin();
            else { fake.Next = change == "query" ? Ready("different") : Ready() with { Results = Array.Empty<DesktopSearchResult>() }; search.Refresh(); }
            var count = fake.Calls.Count;
            Assert.False(search.Select(parameter)); Assert.Equal(count, fake.Calls.Count);
            Assert.False(search.Select("result:garbage"));
        }

        [Fact]
        public void Native_voice_blocks_query_capture_and_other_dictation_is_not_stopped()
        {
            var fake = new Fake(); var search = new DesktopSearch(fake); search.Begin();
            fake.State = new() { VoiceChat = DesktopVoiceState.Active };
            var capture = new VoiceCaptureState(); var toggles = 0;
            DesktopSearchCommand.Execute("speak", search, new(fake), capture, (_, _) => toggles++, () => Assert.Fail("closed"));
            Assert.Equal(0, toggles); Assert.Equal("End Voice", search.Feedback);
            capture.Press(VoiceIntent.DesktopDraft, DateTime.UtcNow);
            DesktopSearchCommand.Execute("speak", search, new(fake), capture, (_, _) => toggles++, () => Assert.Fail("closed"));
            Assert.Equal(0, toggles); Assert.Equal("Dictating", search.Feedback);
        }

        [Fact]
        public void Search_intent_survives_stopping_from_a_different_voice_key()
        {
            var capture = new VoiceCaptureState(); capture.Press(VoiceIntent.DesktopSearch, DateTime.UtcNow);
            var stop = capture.Press(VoiceIntent.DesktopDraft, DateTime.UtcNow);
            Assert.Equal(VoiceAction.Stop, stop.Action); Assert.Equal(VoiceIntent.DesktopSearch, stop.Intent);
        }

        [Theory]
        [InlineData(null)] [InlineData("Query changed")] [InlineData("Cancelled")]
        public void Search_delivery_never_uses_composer_or_clipboard_recovery(String outcome)
        {
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
            bridge.TranscriptSink = (_, _) => throw new Exception("composer called");
            bridge.DraftRecoverySink = _ => throw new Exception("clipboard called");
            var failures = new List<(VoiceIntent, String)>();
            bridge.OnVoiceFailed += (intent, text) => failures.Add((intent, text));
            var received = "";
            bridge.DeliverSearchTranscript("plate duration", text => { received = text; return outcome; });
            Assert.Equal("plate duration", received);
            if (outcome == "Query changed") Assert.Equal((VoiceIntent.DesktopSearch, outcome), Assert.Single(failures));
            else Assert.Empty(failures);
        }

        [Fact]
        public void Result_actions_use_full_key_widgets_with_exact_session_parameters()
        {
            var search = new DesktopSearch(new Fake()); search.Begin();
            var actions = FindChatDynamicFolder.Actions("VizhiDesktop", search).Select(ActionString.FromString).ToArray();
            Assert.Equal(4, actions.Length);
            Assert.All(actions, a => Assert.Equal(typeof(DesktopSearchCommand).FullName, a.ActionName));
            Assert.Equal("speak", actions[0].ActionParameter);
            Assert.Equal("status", actions[1].ActionParameter);
            Assert.Equal("Plate duration", search.Resolve(actions[2].ActionParameter).Title);
        }
    }
}
