namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using VizhiDesktopUia;
    using Xunit;

    public class UiaQueryCacheTests
    {
        [Fact]
        public void Root_reuse_still_reads_fresh_values_and_changes_window_or_process_immediately()
        {
            var cache = new UiaRootCache<Object>(); var acquisitions = 0; var reads = 0; var value = "old";
            Object Acquire() { acquisitions++; return new Object(); }
            String Read(Object _) { reads++; return value; }
            Assert.Equal("old", cache.Read(10, 1, Acquire, Read));
            value = "new"; Assert.Equal("new", cache.Read(10, 1, Acquire, Read));
            Assert.Equal(1, acquisitions); Assert.Equal(2, reads);
            cache.Read(11, 1, Acquire, Read); Assert.Equal(2, acquisitions);
            cache.Read(11, 2, Acquire, Read); Assert.Equal(3, acquisitions);
            cache.Invalidate(); cache.Read(11, 2, Acquire, Read); Assert.Equal(4, acquisitions);
        }

        [Fact]
        public void A_stale_provider_is_reacquired_once_and_persistent_failure_drops_the_root()
        {
            var cache = new UiaRootCache<Object>(); var acquisitions = 0; var reads = 0;
            Object Acquire() { acquisitions++; return new Object(); }
            Int32 StaleOnce(Object _) { if (++reads == 1) throw new COMException(); return reads; }
            Assert.Equal(2, cache.Read(1, 1, Acquire, StaleOnce)); Assert.Equal(2, acquisitions);
            Assert.Throws<COMException>(() => cache.Read<Int32>(1, 1, Acquire, _ => throw new COMException()));
            var before = acquisitions;
            cache.Read(1, 1, Acquire, _ => 1); Assert.Equal(before + 1, acquisitions);
        }

        private static IReadOnlyList<UiaNode> Row(String id, String title = "Chosen", Boolean enabled = true) => new[]
        {
            new UiaNode { Role = "Button", Text = title, Depth = 0, Pressable = true, Enabled = enabled, RuntimeId = id },
            new UiaNode { Role = "Button", Text = "Pin chat", Depth = 1, Pressable = true },
        };

        [Fact]
        public void Overlapping_query_roots_are_one_physical_row_but_two_real_rows_remain_ambiguous()
        {
            var first = Row("same-row"); var duplicate = Row("same-row");
            Assert.Same(first[0], UiaMatching.UniqueQueriedConversation(new[] { first, duplicate }, "Chosen", "Pin chat"));
            Assert.Null(UiaMatching.UniqueQueriedConversation(new[] { first, Row("other-row") }, "Chosen", "Pin chat"));
            Assert.Null(UiaMatching.UniqueQueriedConversation(new[] { Row(""), Row("") }, "Chosen", "Pin chat"));
            Assert.Null(UiaMatching.UniqueQueriedConversation(new[] { first, Row("same-row", enabled: false) }, "Chosen", "Pin chat"));
        }

        [Fact]
        public void A_title_without_a_sidebar_marker_or_with_a_different_title_never_becomes_a_match()
        {
            IReadOnlyList<UiaNode> text = new[] { new UiaNode { Role = "Button", Text = "Chosen", Pressable = true } };
            Assert.Null(UiaMatching.UniqueQueriedConversation(new[] { text }, "Chosen", "Pin chat"));
            Assert.Null(UiaMatching.UniqueQueriedConversation(new[] { Row("id", "Other") }, "Chosen", "Pin chat"));
            Assert.Null(UiaMatching.UniqueQueriedConversation(new[] { Row("id", enabled: false) }, "Chosen", "Pin chat"));
        }
    }
}
