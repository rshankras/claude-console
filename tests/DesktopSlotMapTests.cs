namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Desktop;

    using Xunit;

    /// <summary>
    /// Stable slots — the review round's number-one finding made law: a conversation claims a
    /// key and KEEPS it through every sidebar reorder, because a physical control that remaps
    /// under the fingers breaks the muscle memory the keypad exists for. The failure these
    /// tests prevent is the original implementation: slots mirroring recency order, every key
    /// moving every time anything happened.
    /// </summary>
    public class DesktopSlotMapTests
    {
        private static DesktopConversation C(String title, ConversationState state = ConversationState.Idle) =>
            new DesktopConversation { Title = title, State = state };

        [Fact]
        public void A_fresh_map_matches_the_sidebar_top_to_bottom()
        {
            var map = new DesktopSlotMap();

            var slots = map.Apply(new[] { C("A"), C("B"), C("C") });

            Assert.Equal(new[] { "A", "B", "C" }, slots.Take(3).Select(s => s.Title));
            Assert.All(slots.Skip(3), Assert.Null);
        }

        [Fact]
        public void A_reorder_moves_no_keys()
        {
            // The defining case: B becomes most recent (user sent it a message). In the sidebar
            // B is now first; on the KEYPAD everything stays exactly where it was.
            var map = new DesktopSlotMap();
            map.Apply(new[] { C("A"), C("B"), C("C") });

            var slots = map.Apply(new[] { C("B"), C("A"), C("C") });

            Assert.Equal(new[] { "A", "B", "C" }, slots.Take(3).Select(s => s.Title));
        }

        [Fact]
        public void State_updates_flow_through_without_moving_anything()
        {
            var map = new DesktopSlotMap();
            map.Apply(new[] { C("A"), C("B") });

            var slots = map.Apply(new[] { C("B", ConversationState.Awaiting), C("A") });

            Assert.Equal("B", slots[1].Title);
            Assert.Equal(ConversationState.Awaiting, slots[1].State);   // the badge moves to the key
            Assert.Equal("A", slots[0].Title);                          // the key does not move
        }

        [Fact]
        public void A_departed_conversation_frees_its_slot_and_a_new_one_takes_the_lowest_empty()
        {
            var map = new DesktopSlotMap();
            map.Apply(new[] { C("A"), C("B"), C("C") });

            // B left the sidebar; D is new. D takes B's freed slot — the lowest empty one —
            // and A and C stay put.
            var slots = map.Apply(new[] { C("D"), C("A"), C("C") });

            Assert.Equal("A", slots[0].Title);
            Assert.Equal("D", slots[1].Title);
            Assert.Equal("C", slots[2].Title);
        }

        [Fact]
        public void A_new_chat_appears_at_once_taking_only_the_stalest_slot()
        {
            // The hardware-feedback refinement: membership is recency, positions are sticky.
            // NEW arrives at the sidebar top; T6 falls out of the top six. NEW takes T6's slot;
            // T1-T5 do not move. The user sees their new chat immediately and nothing shuffles.
            var map = new DesktopSlotMap();
            map.Apply(Enumerable.Range(1, DesktopSlotMap.SlotCount).Select(i => C($"T{i}")).ToArray());

            // NEW at the sidebar top pushes the last conversation out of the top-N; NEW takes
            // exactly that slot and every survivor stays put.
            var slots = map.Apply(
                new[] { C("NEW") }.Concat(Enumerable.Range(1, DesktopSlotMap.SlotCount - 1).Select(i => C($"T{i}")))
                    .Concat(new[] { C($"T{DesktopSlotMap.SlotCount}") }).ToArray());

            Assert.Equal(new[] { "T1", "T2", "NEW" }, slots.Select(s => s.Title));
        }

        [Fact]
        public void Duplicate_titles_collapse_to_the_first()
        {
            // Title IS identity (the only one AX offers); two rows named "what is this" are one
            // key, deliberately — better than two keys whose presses are indistinguishable.
            var map = new DesktopSlotMap();

            var slots = map.Apply(new[] { C("what is this"), C("what is this"), C("B") });

            Assert.Equal("what is this", slots[0].Title);
            Assert.Equal("B", slots[1].Title);
            Assert.Null(slots[2]);
        }

        [Fact]
        public void An_empty_reading_frees_everything()
        {
            var map = new DesktopSlotMap();
            map.Apply(new[] { C("A") });

            var slots = map.Apply(Array.Empty<DesktopConversation>());

            Assert.All(slots, Assert.Null);
        }
    }
}
