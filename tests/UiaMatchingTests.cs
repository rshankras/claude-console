namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using VizhiDesktopUia;

    using Xunit;

    /// <summary>
    /// The Windows desktop helper's matching rules, held to the same answers the macOS helper
    /// gives (tools/desktop/VizhiAxBridge.swift). The trees below are shaped like the live
    /// ChatGPT app's UIA tree as captured on 2026-09-30: a sidebar row is a Button whose
    /// subtree carries the "Pin chat" control, the composer is an Edit with a value, and a
    /// popup button exposes ExpandCollapse rather than Invoke.
    /// </summary>
    public sealed class UiaMatchingTests
    {
        private static UiaNode N(Int32 depth, String role, String text, Boolean pressable = false,
            Boolean enabled = true, String aria = "", String props = "", String value = null, Boolean selected = false,
            String[] labels = null) => new UiaNode
        {
            Depth = depth,
            Role = role,
            Text = text,
            Labels = labels ?? (text.Length > 0 ? new[] { text } : Array.Empty<String>()),
            Pressable = pressable,
            Enabled = enabled,
            AriaRole = aria,
            AriaProperties = props,
            Value = value ?? "",
            HasValue = value != null,
            Selected = selected,
        };

        private static List<UiaNode> Row(Int32 depth, String title, String state = null, Boolean selected = false, String props = "")
        {
            var row = new List<UiaNode>
            {
                N(depth, "Button", title, pressable: true, selected: selected, props: props),
                N(depth + 1, "Group", ""),
                N(depth + 2, "Button", "Chat actions", pressable: true),
                N(depth + 2, "Button", "Pin chat", pressable: true),
                N(depth + 1, "Text", title, aria: "description"),
            };
            if (state != null)
            {
                row.Add(N(depth + 1, "StatusBar", state, aria: "status"));
            }
            return row;
        }

        private static List<UiaNode> Window(params IEnumerable<UiaNode>[] parts)
        {
            var nodes = new List<UiaNode> { N(0, "Window", "ChatGPT"), N(1, "Pane", ""), N(2, "Document", "ChatGPT") };
            foreach (var part in parts) nodes.AddRange(part);
            return nodes.Select((n, i) => new UiaNode
            {
                Index = i, Depth = n.Depth, Role = n.Role, Text = n.Text, Labels = n.Labels, Pressable = n.Pressable,
                Enabled = n.Enabled, AriaRole = n.AriaRole, AriaProperties = n.AriaProperties, Value = n.Value,
                HasValue = n.HasValue, ReadOnly = n.ReadOnly, Selected = n.Selected, Bounds = n.Bounds,
                RuntimeId = n.RuntimeId.Length > 0 ? n.RuntimeId : "r" + i,
            }).ToList();
        }

        [Fact]
        public void A_document_without_content_is_not_a_surface()
        {
            // Chromium keeps the document element while the screen is locked or the window is
            // covered; the emptiness beneath it must read as "cannot see", never "resolved".
            var hidden = Window();
            Assert.False(UiaMatching.HasSurface(hidden, complete: true));

            var shown = Window(new[] { N(3, "Group", "") });
            Assert.True(UiaMatching.HasSurface(shown, complete: true));
            Assert.False(UiaMatching.HasSurface(shown, complete: false));
        }

        [Fact]
        public void First_pressable_matches_by_substring_and_ignores_case_and_unpressable_text()
        {
            var nodes = Window(new[]
            {
                N(3, "Text", "Allow once to continue"),
                N(3, "Button", "Deny", pressable: true),
                N(3, "Button", "Allow once", pressable: true),
            });
            Assert.Equal("Allow once", UiaMatching.FirstPressable(nodes, new[] { "allow ONCE" }).Text);
            Assert.Null(UiaMatching.FirstPressable(nodes, new[] { "Always allow" }));
            Assert.Null(UiaMatching.FirstPressable(nodes, new[] { "" }));
        }

        [Fact]
        public void Exact_buttons_need_the_whole_label_and_no_nested_control()
        {
            var nodes = Window(
                Row(3, "Stop"),                                     // a chat titled Stop is not the Stop button
                new[] { N(3, "Button", "Stop generating", pressable: true) },
                new[] { N(3, "Button", " stop ", pressable: true, labels: new[] { " stop " }) });

            var stops = UiaMatching.ExactButtons(nodes, new[] { "Stop" });
            Assert.Single(stops);
            Assert.Equal(" stop ", stops[0].Text);
            Assert.NotNull(UiaMatching.UniqueEnabledButton(nodes, new[] { "Stop" }));
        }

        [Fact]
        public void Unique_enabled_button_refuses_duplicates_and_disabled_controls()
        {
            var twice = Window(new[]
            {
                N(3, "Button", "Send", pressable: true),
                N(3, "Button", "Send", pressable: true),
            });
            Assert.Null(UiaMatching.UniqueEnabledButton(twice, new[] { "Send" }));

            var disabled = Window(new[] { N(3, "Button", "Send", pressable: true, enabled: false) });
            Assert.Null(UiaMatching.UniqueEnabledButton(disabled, new[] { "Send" }));
        }

        [Fact]
        public void Card_text_is_the_last_three_texts_before_the_anchor_collapsed_and_capped()
        {
            var nodes = Window(new[]
            {
                N(3, "Text", "older"),
                N(3, "Text", "Codex wants to run"),
                N(3, "Text", "  npm   install\n--save "),
                N(3, "Text", new String('x', 500)),
                N(3, "Button", "Allow once", pressable: true),
            });
            var approve = UiaMatching.FirstPressable(nodes, new[] { "Allow once" });
            var card = UiaMatching.CardText(approve, nodes);
            Assert.StartsWith("Codex wants to run npm install --save xxx", card);
            Assert.Equal(UiaMatching.CardTextCap, card.Length);
            Assert.DoesNotContain("older", card);
        }

        [Fact]
        public void Conversations_come_from_marked_rows_in_sidebar_order_with_their_state()
        {
            var nodes = Window(
                new[] { N(3, "Button", "New chat", pressable: true) },
                Row(3, "Fix the build", "Awaiting approval"),
                Row(3, "Thinking about travel", "Working", selected: true),
                Row(3, "Quiet one", "Complete"),
                Row(3, "Idle one"));

            var list = UiaMatching.Conversations(nodes, "Pin chat",
                new[] { "Awaiting approval" }, new[] { "Unread", "Complete" }, new[] { "Thinking", "Working" }, baseline: null);

            Assert.Equal(new[] { "Fix the build", "Thinking about travel", "Quiet one", "Idle one" }, list.Select(c => c["title"]));
            Assert.Equal(new[] { "awaiting", "running", "unread", "idle" }, list.Select(c => c["state"]));
            Assert.Equal(new[] { "false", "true", "false", "false" }, list.Select(c => c["selected"]));
        }

        [Fact]
        public void A_row_title_never_reads_as_its_own_state()
        {
            // "Working" as a chat title must not make the chat busy; only a status descendant may.
            var nodes = Window(Row(3, "Working"));
            var list = UiaMatching.Conversations(nodes, "Pin chat", new[] { "Awaiting approval" }, new[] { "Unread" }, new[] { "Working" }, null);
            Assert.Equal("idle", Assert.Single(list)["state"]);
        }

        [Fact]
        public void Aria_current_decides_the_open_conversation_before_the_selection_pattern()
        {
            var current = N(3, "Button", "A", pressable: true, props: "readonly=true;current=page;expanded=false");
            var explicitlyNot = N(3, "Button", "B", pressable: true, props: "current=false", selected: true);
            var legacy = N(3, "Button", "C", pressable: true, selected: true);
            Assert.True(UiaMatching.IsCurrent(current));
            Assert.False(UiaMatching.IsCurrent(explicitlyNot));
            Assert.True(UiaMatching.IsCurrent(legacy));
        }

        [Fact]
        public void Conversation_matches_use_exact_titles_and_report_duplicates()
        {
            var nodes = Window(Row(3, "Plan"), Row(3, "Plan"), Row(3, "Plan B"));
            Assert.Equal(2, UiaMatching.ConversationMatches(nodes, "Plan", "Pin chat").Count);
            Assert.Single(UiaMatching.ConversationMatches(nodes, "Plan B", "Pin chat"));
            Assert.Empty(UiaMatching.ConversationMatches(nodes, "plan", "Pin chat"));
            Assert.Empty(UiaMatching.ConversationMatches(nodes, "Plan", ""));
        }

        [Fact]
        public void A_project_chat_listed_under_its_project_and_in_recents_is_one_conversation()
        {
            // Seen live (2026-10-01): the open chat sat under project "tmp" and in Recents, both
            // rows aria-current, so Copy Reply refused with reply-selection-multiple and the
            // chat keys would have found two rows for one title.
            var current = "current=page";
            var nodes = Window(
                new[] { N(3, "List", ""), N(4, "ListItem", "tmp Plan Old"), N(5, "Group", "tmp"), N(6, "List", "Chats in tmp"), N(7, "ListItem", "Plan") },
                Row(8, "Plan", props: current),
                new[] { N(7, "ListItem", "Old") }, Row(8, "Old"),
                new[] { N(3, "List", ""), N(4, "ListItem", "Plan") }, Row(5, "Plan", props: current),
                new[] { N(4, "ListItem", "Other") }, Row(5, "Other"));
            var rows = UiaMatching.Conversations(nodes, "Pin chat", Array.Empty<String>(), Array.Empty<String>(), Array.Empty<String>(), null);
            Assert.Equal(new[] { "Old", "Plan", "Other" }, rows.Select(r => r["title"]));   // Old is only under its project
            Assert.Equal(new[] { "false", "true", "false" }, rows.Select(r => r["selected"]));
            Assert.Single(UiaMatching.ConversationMatches(nodes, "Plan", "Pin chat"));
            Assert.StartsWith("Plan:", UiaMatching.SelectedConversation(nodes, "Pin chat"));

            // Two different chats with one title in Recents are still two rows.
            var twins = Window(new[] { N(3, "List", ""), N(4, "ListItem", "Plan") }, Row(5, "Plan"), new[] { N(4, "ListItem", "Plan") }, Row(5, "Plan"));
            Assert.Equal(2, UiaMatching.ConversationMatches(twins, "Plan", "Pin chat").Count);
        }

        [Fact]
        public void Mode_is_read_from_the_prefixed_switcher_label()
        {
            var nodes = Window(new[] { N(3, "Button", "Switch mode, current mode: Codex", pressable: true) });
            Assert.Equal("Codex", UiaMatching.Mode(nodes, "Switch mode, current mode: "));
            Assert.Equal("", UiaMatching.Mode(nodes, ""));
            Assert.Equal("", UiaMatching.Mode(nodes, "Other prefix: "));
        }

        [Fact]
        public void Attention_is_a_substring_anywhere_in_the_tree()
        {
            var nodes = Window(new[] { N(3, "Button", "View activity, needs attention", pressable: true) });
            Assert.True(UiaMatching.Attention(nodes, "needs attention"));
            Assert.False(UiaMatching.Attention(nodes, ""));
            Assert.False(UiaMatching.Attention(nodes, "needs review"));
        }

        [Fact]
        public void Voice_state_needs_a_unique_enabled_start_and_treats_any_end_as_active()
        {
            var start = new[] { "Start voice chat", "Start new voice chat" };
            var end = new[] { "Stop voice chat" };

            var ready = Window(new[] { N(3, "Button", "Start new voice chat", pressable: true) });
            Assert.Equal("ready", UiaMatching.VoiceState(ready, start, end));

            var twoStarts = Window(new[]
            {
                N(3, "Button", "Start new voice chat", pressable: true),
                N(3, "Button", "Start new voice chat", pressable: true),
            });
            Assert.Equal("unavailable", UiaMatching.VoiceState(twoStarts, start, end));

            // The Windows app shows the composer's and the sidebar's start buttons together
            // (seen 2026-09-30): the adapter's first label wins, so voice stays available.
            var both = Window(new[]
            {
                N(3, "Button", "Start new voice chat", pressable: true),
                N(4, "Button", "Start voice chat", pressable: true),
            });
            Assert.Equal("ready", UiaMatching.VoiceState(both, start, end));
            Assert.Equal("Start voice chat", UiaMatching.VoiceTarget("start", both, start, end).Text);

            var active = Window(new[] { N(3, "Button", "Stop voice chat", pressable: true) });
            Assert.Equal("active", UiaMatching.VoiceState(active, start, end));
            Assert.Null(UiaMatching.VoiceTarget("start", active, start, end));
            Assert.NotNull(UiaMatching.VoiceTarget("end", active, start, end));

            // Even a disabled End button is evidence of a session: it blocks a second start,
            // and is not itself a target.
            var ending = Window(new[] { N(3, "Button", "Stop voice chat", pressable: true, enabled: false) });
            Assert.Equal("active", UiaMatching.VoiceState(ending, start, end));
            Assert.Null(UiaMatching.VoiceTarget("end", ending, start, end));

            Assert.Equal("unavailable", UiaMatching.VoiceState(ready, start, Array.Empty<String>()));
        }

        [Fact]
        public void Send_target_needs_one_composer_with_a_draft_and_a_local_enabled_send()
        {
            IEnumerable<UiaNode> Composer(String draft, Boolean sendEnabled = true) => new[]
            {
                N(3, "Group", ""),
                N(4, "Edit", "Work with ChatGPT", aria: "textbox", value: draft),
                N(4, "Button", "Send", pressable: true, enabled: sendEnabled),
            };
            var stop = new[] { "Stop" };
            var approve = new[] { "Allow once" };

            Assert.NotNull(UiaMatching.SendTarget(Window(Composer("hello")), "Send", stop, approve));
            Assert.Null(UiaMatching.SendTarget(Window(Composer("   ")), "Send", stop, approve));
            Assert.Null(UiaMatching.SendTarget(Window(Composer("hello", sendEnabled: false)), "Send", stop, approve));
            Assert.Null(UiaMatching.SendTarget(Window(Composer("hello")), "", stop, approve));

            // A running task or a waiting approval means the draft is not sendable now.
            var running = Window(Composer("hello"), new[] { N(3, "Button", "Stop", pressable: true) });
            Assert.Null(UiaMatching.SendTarget(running, "Send", stop, approve));

            // Send found only at the document level is too broad a relationship.
            var far = Window(new[] { N(3, "Group", ""), N(4, "Edit", "", aria: "textbox", value: "hello") },
                new[] { N(3, "Button", "Send", pressable: true) });
            Assert.Null(UiaMatching.SendTarget(far, "Send", stop, approve));
        }

        [Fact]
        public void Panel_route_needs_the_mode_owning_document_and_one_opener_or_one_visible_panel()
        {
            var modeLabel = "Switch mode, current mode: Codex";
            var open = new[] { "Changes", "This branch" };
            var visible = new[] { "Show files", "Hide files" };

            var withOpener = Window(new[]
            {
                N(3, "Button", modeLabel, pressable: true),
                N(3, "Button", "Changes +1,234 −56", pressable: true),
            });
            Assert.True(UiaMatching.PanelRouteAvailable(withOpener, modeLabel, open, visible));

            var settingsToo = Window(new[]
            {
                N(3, "Button", modeLabel, pressable: true),
                N(3, "Button", "Changes", pressable: true),
                N(3, "Button", "Changes settings", pressable: true),
            });
            Assert.True(UiaMatching.PanelRouteAvailable(settingsToo, modeLabel, open, visible));

            var obstructed = Window(new[]
            {
                N(3, "Button", modeLabel, pressable: true),
                N(3, "Button", "Changes", pressable: true),
                N(3, "Group", "Confirm", aria: "dialog"),
            });
            Assert.False(UiaMatching.PanelRouteAvailable(obstructed, modeLabel, open, visible));

            var preview = Window(new[]
            {
                N(3, "Button", modeLabel, pressable: true),
                N(3, "Document", "preview"),
                N(4, "Button", "Changes", pressable: true),
            });
            Assert.False(UiaMatching.PanelRouteAvailable(preview, modeLabel, open, visible));
            Assert.False(UiaMatching.PanelRouteAvailable(withOpener, "", open, visible));

            // Seen live on Windows (app 26.928): the reply's edit summary offers "View changes"
            // beside "View changed files"; only the first opens Review.
            var reply = Window(new[]
            {
                N(3, "Button", modeLabel, pressable: true),
                N(3, "Button", "View changed files", pressable: true),
                N(3, "Button", "View changes", pressable: true),
            });
            Assert.False(UiaMatching.PanelRouteAvailable(reply, modeLabel, open, visible));
            var withReply = new[] { "Changes", "This branch", "View changes" };
            Assert.True(UiaMatching.PanelRouteAvailable(reply, modeLabel, withReply, visible));
            Assert.Equal("View changes", Assert.Single(UiaMatching.PanelOpeners(reply, withReply)).Text);

            // Seen live after a second edit: every edited reply keeps its own "View changes".
            // Named as a turn label, the latest reply's is the opener; unnamed, two are refused.
            var turn = new[] { "View changes" };
            var twoTurns = Window(new[]
            {
                N(3, "Button", modeLabel, pressable: true),
                N(3, "Button", "View changed files", pressable: true),
                N(3, "Button", "View changes", pressable: true),
                N(3, "Button", "View changed files", pressable: true),
                N(3, "Button", "View changes", pressable: true),
            });
            Assert.False(UiaMatching.PanelRouteAvailable(twoTurns, modeLabel, withReply, visible));
            Assert.True(UiaMatching.PanelRouteAvailable(twoTurns, modeLabel, withReply, visible, turn));
            Assert.Equal(twoTurns.Count - 1, Assert.Single(UiaMatching.PanelOpeners(twoTurns, withReply, turn)).Index);

            // A summary row outranks the replies' buttons, and two summary rows stay ambiguous.
            var rowToo = Window(twoTurns.Skip(3).Append(N(3, "Button", "Changes +2 −0", pressable: true)));
            Assert.Equal("Changes +2 −0", Assert.Single(UiaMatching.PanelOpeners(rowToo, withReply, turn)).Text);
            var twoRows = Window(twoTurns.Skip(3).Concat(new[]
            {
                N(3, "Button", "Changes", pressable: true), N(3, "Button", "Changes", pressable: true),
            }));
            Assert.False(UiaMatching.PanelRouteAvailable(twoRows, modeLabel, withReply, visible, turn));
        }

        [Fact]
        public void An_empty_composer_renders_its_hint_into_its_value_and_is_still_empty()
        {
            // Seen live on Windows: clearing the composer leaves "\nWork with ChatGPT" as its value.
            var hints = new[] { "Work with ChatGPT", "Ask ChatGPT" };
            Assert.True(UiaMatching.IsPlaceholderDraft("\nWork with ChatGPT", hints, sendEnabled: false));
            // A literal draft spelling the hint has Send enabled beside it, and is a draft.
            Assert.False(UiaMatching.IsPlaceholderDraft("Work with ChatGPT", hints, sendEnabled: true));
            Assert.False(UiaMatching.IsPlaceholderDraft("Work with ChatGPT now", hints, sendEnabled: false));
            Assert.Equal(UiaMatching.Fingerprint(""), UiaMatching.Fingerprint("  \n"));
            Assert.NotEqual(UiaMatching.Fingerprint("a"), UiaMatching.Fingerprint("b"));
            Assert.Equal(64, UiaMatching.Fingerprint("x").Length);
        }

        [Fact]
        public void Reported_modes_come_from_controls_and_must_agree_with_the_expected_one()
        {
            var prefix = "Switch mode, current mode: ";
            var one = Window(new[]
            {
                N(3, "Button", prefix + "Codex", pressable: true),
                N(4, "Text", prefix + "Codex"),
                N(3, "Text", prefix + "ChatGPT"),               // plain text is not a control's report
            });
            var modes = UiaMatching.ReportedModes(one, prefix);
            Assert.Equal(new[] { "Codex" }, modes);
            Assert.Null(UiaMatching.ModeError(modes, "Codex", pinned: false));
            Assert.Equal("mode-changed", UiaMatching.ModeError(modes, "ChatGPT", pinned: false));
            Assert.Equal("mode-unavailable", UiaMatching.ModeError(modes, "", pinned: false));

            var none = new HashSet<String>();
            Assert.Equal("mode-unavailable", UiaMatching.ModeError(none, "Codex", pinned: false));
            Assert.Null(UiaMatching.ModeError(none, "Codex", pinned: true));

            var conflicting = new HashSet<String> { "Codex", "ChatGPT" };
            Assert.Equal("mode-changed", UiaMatching.ModeError(conflicting, "Codex", pinned: true));
            Assert.Empty(UiaMatching.ReportedModes(one, ""));
        }

        [Fact]
        public void The_selected_conversation_is_part_of_a_draft_target_and_two_are_a_refusal()
        {
            var none = Window(Row(3, "A"), Row(3, "B"));
            Assert.Equal("", UiaMatching.SelectedConversation(none, "Pin chat"));

            var one = Window(Row(3, "A", props: "current=page"), Row(3, "B"));
            Assert.StartsWith("A:", UiaMatching.SelectedConversation(one, "Pin chat"));

            var two = Window(Row(3, "A", selected: true), Row(3, "B", selected: true));
            Assert.Null(UiaMatching.SelectedConversation(two, "Pin chat"));
            Assert.Equal("", UiaMatching.SelectedConversation(two, ""));
        }

        [Fact]
        public void A_dialog_or_a_live_task_blocks_the_composer()
        {
            var blocking = new[] { "Stop", "Allow once", "Stop voice chat" };
            Assert.False(UiaMatching.ComposerBlocked(Window(new[] { N(3, "Button", "Send", pressable: true) }), blocking));
            Assert.True(UiaMatching.ComposerBlocked(Window(new[] { N(3, "Button", "Stop", pressable: true) }), blocking));
            Assert.False(UiaMatching.ComposerBlocked(Window(new[] { N(3, "Button", "Stop", pressable: true, enabled: false) }), blocking));
            Assert.True(UiaMatching.ComposerBlocked(Window(new[] { N(3, "Group", "Save?", aria: "alertdialog") }), blocking));
            Assert.True(UiaMatching.ComposerBlocked(Window(new[] { N(3, "Window", "Open", pressable: false) }), blocking));
        }

        [Fact]
        public void An_append_is_confirmed_by_the_original_fingerprint_and_a_real_separator()
        {
            var before = UiaMatching.Fingerprint("first line");
            Assert.True(UiaMatching.AppendedDraftMatches("first line\n\nsecond", before, "second"));
            Assert.True(UiaMatching.AppendedDraftMatches("first line\nsecond\n", before, " second "));
            Assert.False(UiaMatching.AppendedDraftMatches("first linesecond", before, "second"));      // no separator
            Assert.False(UiaMatching.AppendedDraftMatches("other\n\nsecond", before, "second"));       // original changed
            Assert.False(UiaMatching.AppendedDraftMatches("first line\n\nsecond", before, ""));
            // Into an empty composer there is nothing to separate from.
            Assert.True(UiaMatching.AppendedDraftMatches("second", UiaMatching.Fingerprint(""), "second"));
        }

        private static readonly UiaMatching.ReplyRules Rules = new(
            new[] { "ChatGPT said:" }, new[] { "You said:" }, new[] { "Copy response" }, new[] { "Copy" }, new[] { "Copied" },
            new[] { "Fork chat from here", "Rate response", "More actions" }, new[] { "Stop" }, new[] { "Stop voice chat" },
            new[] { "Allow once" }, "Pin chat", new[] { "Awaiting approval" }, new[] { "Working" });

        private static UiaNode B(Int32 depth, String text, Double x, Double y = 100, Double w = 24, Double h = 24) => new UiaNode
        {
            Depth = depth, Role = "Button", Text = text, Labels = new[] { text }, Pressable = true, Enabled = true,
            AriaRole = "button", Bounds = new[] { x, y, w, h }, RuntimeId = "b" + text.GetHashCode(),
        };

        // The live Windows layout (2026-09-30): each footer button in its own pressable group,
        // headings as text with an aria heading role.
        private static IEnumerable<UiaNode> Turn(Boolean assistant, String body, Boolean footer = true)
        {
            yield return N(4, "Text", assistant ? "ChatGPT said:" : "You said:", aria: "heading");
            yield return N(4, "Group", "");
            yield return N(5, "Text", body, aria: "description");
            if (!footer) yield break;
            yield return N(4, "Group", "", pressable: true);
            yield return B(5, assistant ? "Copy" : "Copy message", 10);
            if (assistant)
            {
                yield return N(4, "Group", "", pressable: true);
                yield return B(5, "Rate response", 40);
                yield return N(4, "Group", "", pressable: true);
                yield return B(5, "Fork chat from here", 70);
            }
        }

        [Fact]
        public void Copy_reply_finds_the_latest_answer_through_the_wrapped_footer_row()
        {
            var nodes = Window(Turn(false, "question"), Turn(true, "answer one"), Turn(false, "follow-up"), Turn(true, "answer two"));
            var (node, error) = UiaMatching.ReplyTarget(nodes, Rules);
            Assert.Equal("", error);
            Assert.NotNull(node);
            // The last answer's Copy, not the first's: same label, different row.
            Assert.Equal("Copy", node.Text);
            Assert.Equal(nodes.Count - 5, UiaMatching.IndexOf(nodes, node));
        }

        [Fact]
        public void Copy_reply_refuses_when_the_latest_turn_is_the_user_or_a_task_is_live()
        {
            Assert.Equal("no-answer", UiaMatching.ReplyTarget(Window(Turn(true, "a"), Turn(false, "b")), Rules).Error);
            Assert.Equal("reply-unrecognized", UiaMatching.ReplyTarget(Window(new[] { N(3, "Group", "") }), Rules).Error);
            Assert.Equal("answer-not-ready", UiaMatching.ReplyTarget(Window(Turn(true, "a"), new[] { N(3, "Button", "Stop", pressable: true) }), Rules).Error);
            Assert.Equal("reply-dialog-open", UiaMatching.ReplyTarget(Window(Turn(true, "a"), new[] { N(3, "Group", "Confirm", aria: "dialog") }), Rules).Error);
            Assert.Equal("reply-copy-not-found", UiaMatching.ReplyTarget(Window(Turn(true, "a", footer: false)), Rules).Error);
            // A running open conversation is not a finished answer.
            var running = Window(Row(3, "A", "Working", props: "current=page"), Turn(true, "a"));
            Assert.Equal("answer-not-ready", UiaMatching.ReplyTarget(running, Rules).Error);
        }

        [Fact]
        public void A_generic_copy_needs_two_distinct_actions_beside_it_in_one_compact_row()
        {
            // Copy with only one action: not a footer.
            var one = Window(new[] { N(3, "Text", "ChatGPT said:", aria: "heading"), N(3, "Group", "", pressable: true), B(4, "Copy", 10),
                N(3, "Group", "", pressable: true), B(4, "Rate response", 40) });
            Assert.Equal("reply-action-row-unrecognized", UiaMatching.ReplyTarget(one, Rules).Error);
            // Two actions but on another line: not a row.
            var apart = Window(new[] { N(3, "Text", "ChatGPT said:", aria: "heading"), N(3, "Group", "", pressable: true), B(4, "Copy", 10),
                N(3, "Group", "", pressable: true), B(4, "Rate response", 40), N(3, "Group", "", pressable: true), B(4, "Fork chat from here", 70, y: 200) });
            Assert.Equal("reply-action-row-unrecognized", UiaMatching.ReplyTarget(apart, Rules).Error);
            Assert.False(UiaMatching.SameReplyControlRow(new[] { new[] { 0d, 0, 24, 24 }, new[] { 30d, 0, 24, 24 }, new[] { 300d, 0, 24, 24 } }));
            Assert.True(UiaMatching.SameReplyControlRow(new[] { new[] { 0d, 0, 24, 24 }, new[] { 30d, 2, 24, 24 }, new[] { 60d, 0, 24, 30 } }));
        }

        [Fact]
        public void Search_rules_recognise_the_command_menu_and_its_list_items()
        {
            var names = new[] { "Search", "Search chats" };
            var field = new UiaNode { Role = "ComboBox", Text = "Command menu", Labels = new[] { "Command menu", "Search chats" }, AriaRole = "combobox", HasValue = true, Pressable = true };
            Assert.True(UiaMatching.IsSearchField(field, names));
            Assert.False(UiaMatching.IsSearchField(N(3, "Edit", "Work with ChatGPT", aria: "textbox", value: ""), names));
            Assert.True(UiaMatching.IsSearchField(new UiaNode { Role = "Edit", AriaRole = "searchbox" }, names));

            var nodes = Window(new[]
            {
                N(3, "Window", "Command menu", aria: "dialog"),
                N(4, "ComboBox", "Command menu", aria: "combobox", value: "vizhi", labels: new[] { "Command menu", "Search chats" }),
                N(4, "List", "Suggestions"),
                N(5, "ListItem", "Read vizhi handoff claude-console Alt+1 snippet", pressable: true, aria: "option"),
                N(6, "Group", "", pressable: true),
                N(7, "Text", "Read vizhi handoff", aria: "description"),
                N(5, "ListItem", "Other chat Alt+2", pressable: true, aria: "option"),
                N(6, "Text", "Other chat", aria: "description"),
                N(5, "ListItem", "Other chat Alt+2", pressable: true, aria: "option"),   // duplicate: dropped
                N(5, "Hyperlink", "A link", pressable: true, value: "https://chatgpt.com/c/abc"),
                N(5, "Hyperlink", "Not a chat", pressable: true, value: "https://example.com/c/abc"),
            });
            var fieldIndex = nodes.FindIndex(n => n.Role == "ComboBox");
            Assert.Equal(nodes.FindIndex(n => n.AriaRole == "dialog"), UiaMatching.SearchContainer(fieldIndex, nodes));
            var results = UiaMatching.SearchResults(nodes, "vizhi", new[] { "chatgpt.com" }, new[] { "/c/" });
            Assert.Equal(new[] { "Read vizhi handoff", "A link" }, results.Select(r => r.Title));
            Assert.StartsWith("item:", results[0].Id);
            Assert.Equal("https://chatgpt.com/c/abc", results[1].Id);
            Assert.Empty(UiaMatching.SearchResults(nodes, "", new[] { "chatgpt.com" }, new[] { "/c/" }));

            // Seen live: a query that matches the title splits it around the matched words. The
            // key shows the whole title, not the part before the match, and stops at the project.
            var split = Window(new[]
            {
                N(3, "Window", "Command menu", aria: "dialog"),
                N(4, "ListItem", "Add third line to notes.txt tmp Alt+1 Add a third", pressable: true, aria: "option"),
                N(5, "Group", "", pressable: true),
                N(6, "Text", "Add third line to ", aria: "description"),
                N(6, "Text", "notes", aria: "description"),
                N(6, "Text", ".txt", aria: "description"),
                N(5, "Group", "", pressable: true),
                N(6, "Text", "tmp", aria: "description"),
                N(5, "Text", "Alt+1", aria: "description"),
            });
            Assert.Equal("Add third line to notes.txt",
                Assert.Single(UiaMatching.SearchResults(split, "notes", Array.Empty<String>(), Array.Empty<String>())).Title);

            // Seen live: "new" lists the app's own commands under other headings. Only items
            // under a named chat group are results; a command is never offered as a chat.
            var mixed = Window(new[]
            {
                N(3, "Window", "Command menu", aria: "dialog"),
                N(4, "List", "Suggestions"),
                N(5, "Group", ""),
                N(5, "Group", "Chats"),
                N(6, "ListItem", "Check the new branch claude-console Alt+1", pressable: true, aria: "option"),
                N(7, "Group", "", pressable: true),
                N(8, "Text", "Check the new branch", aria: "description"),
                N(5, "Group", "Chat"),
                N(6, "ListItem", "New chat Ctrl+N", pressable: true, aria: "option"),
                N(6, "ListItem", "Open in new window", pressable: true, aria: "option"),
                N(5, "Group", "Panels"),
                N(6, "ListItem", "Open browser tab Ctrl+T", pressable: true, aria: "option"),
                N(4, "ListItem", "New loose item", pressable: true, aria: "option"),
            });
            var none = Array.Empty<String>();
            Assert.Equal(5, UiaMatching.SearchResults(mixed, "new", none, none).Count);
            Assert.Equal("Check the new branch", Assert.Single(UiaMatching.SearchResults(mixed, "new", none, none, new[] { "Chats" })).Title);
        }

        [Fact]
        public void Normalisation_collapses_whitespace_and_case()
        {
            Assert.Equal("allow once", UiaMatching.NormalizeLabel("  Allow\n ONCE "));
            Assert.Equal("a b", UiaMatching.Collapse(" a \r\n  b "));
            Assert.Equal("", UiaMatching.NormalizeLabel(null));
        }
    }
}
