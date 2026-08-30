namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;

    using Xunit;

    /// <summary>
    /// A quiet session is not a closed one (#49).
    ///
    /// The prune existed to clear up closed tabs, and deleted any state file older than ten minutes
    /// with no check for whether that session was still alive. So a session you had not typed in for
    /// ten minutes lost its file, and two things followed: the display fell back to shared.json — the
    /// LAST WRITER — and put another session's cost on the key; and the grid recreated the session as
    /// provisional with no project, so its key showed the agent's name ("Claude Code") instead.
    ///
    /// Reported from the device by the plugin's own author, who spotted both symptoms and correctly
    /// identified them as one cause.
    /// </summary>
    public class IdleSessionStateTests : IDisposable
    {
        private readonly String _root =
            Path.Combine(Path.GetTempPath(), "cc-idle-" + Guid.NewGuid().ToString("N"));

        public IdleSessionStateTests() => Directory.CreateDirectory(this._root);

        public void Dispose()
        {
            try { Directory.Delete(this._root, recursive: true); } catch { /* best effort */ }
        }

        private String WriteAged(String name, TimeSpan age)
        {
            var path = Path.Combine(this._root, name);
            File.WriteAllText(path, "{}");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
            return path;
        }

        /// <summary>The defect: an idle-but-live session losing the file that IS its memory.</summary>
        [Fact]
        public void A_live_sessions_file_survives_however_long_it_has_been_idle()
        {
            var idleButLive = this.WriteAged("ttys001.json", TimeSpan.FromHours(6));

            BridgeManager.PruneStaleFiles(
                new[] { this._root }, DateTime.UtcNow - TimeSpan.FromMinutes(10), new[] { "ttys001" });

            Assert.True(File.Exists(idleButLive), "a live session's state file was pruned");
        }

        /// <summary>...but a closed tab's file must still be cleaned up, or the directory grows forever.</summary>
        [Fact]
        public void A_dead_sessions_file_is_still_pruned()
        {
            var closed = this.WriteAged("ttys002.json", TimeSpan.FromHours(6));

            BridgeManager.PruneStaleFiles(
                new[] { this._root }, DateTime.UtcNow - TimeSpan.FromMinutes(10), new[] { "ttys001" });

            Assert.False(File.Exists(closed), "a closed session's file was left behind");
        }

        [Fact]
        public void A_recent_file_survives_whether_or_not_its_session_is_live()
        {
            var recentDead = this.WriteAged("ttys003.json", TimeSpan.FromMinutes(1));

            BridgeManager.PruneStaleFiles(
                new[] { this._root }, DateTime.UtcNow - TimeSpan.FromMinutes(10), Array.Empty<String>());

            Assert.True(File.Exists(recentDead));
        }

        /// <summary>The shared fallback file belongs to no session, so age alone governs it.</summary>
        [Fact]
        public void The_shared_file_is_pruned_on_age_alone()
        {
            var shared = this.WriteAged("shared.json", TimeSpan.FromHours(6));

            BridgeManager.PruneStaleFiles(
                new[] { this._root }, DateTime.UtcNow - TimeSpan.FromMinutes(10), new[] { "ttys001" });

            Assert.False(File.Exists(shared));
        }

        /// <summary>
        /// Every live session is exempt, not merely the first — with several quiet tabs open, one
        /// surviving and the rest vanishing would look like random data loss.
        /// </summary>
        [Fact]
        public void Every_live_session_is_exempt()
        {
            var a = this.WriteAged("ttys001.json", TimeSpan.FromHours(6));
            var b = this.WriteAged("ttys002.json", TimeSpan.FromHours(6));
            var c = this.WriteAged("ttys003.json", TimeSpan.FromHours(6));

            BridgeManager.PruneStaleFiles(
                new[] { this._root }, DateTime.UtcNow - TimeSpan.FromMinutes(10), new[] { "ttys001", "ttys002" });

            Assert.True(File.Exists(a));
            Assert.True(File.Exists(b));
            Assert.False(File.Exists(c), "the one session that is gone should still be pruned");
        }

        /// <summary>Called with no keep-list at all, the old behaviour stands — nothing is exempt.</summary>
        [Fact]
        public void With_no_live_sessions_everything_stale_goes()
        {
            var f = this.WriteAged("ttys001.json", TimeSpan.FromHours(6));

            BridgeManager.PruneStaleFiles(new[] { this._root }, DateTime.UtcNow - TimeSpan.FromMinutes(10));

            Assert.False(File.Exists(f));
        }

        // -----------------------------------------------------------------------------------------
        // Part two: never present another session's numbers as this session's.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// CLAUDE.md's law — "a key must never show a value the agent did not report" — applied to
        /// the display keys. These are source checks because the keys need the plugin host to build,
        /// so no test in this suite can construct one.
        /// </summary>
        [Fact]
        public void A_known_session_with_no_file_yields_no_state_rather_than_the_shared_file()
        {
            var bridge = File.ReadAllText(RepoFile("src", "Core", "BridgeManager.cs"));

            // PerTty returns null for a known session with no file; shared is only for "unknown".
            Assert.Contains("return File.Exists(p) ? p : null;", bridge);
            Assert.Contains("OnStateUnavailable", bridge);
        }

        [Theory]
        [InlineData("CostDisplayCommand")]
        [InlineData("ContextCommand")]
        [InlineData("ModelCycleCommand")]
        public void Every_display_key_falls_back_to_no_value(String key)
        {
            var source = File.ReadAllText(RepoFile("src", "Core", "Actions", key + ".cs"));

            Assert.Contains("OnStateUnavailable", source);
            Assert.Contains("_hasData", source);
        }

        private static String RepoFile(params String[] relative)
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(new[] { dir }.Concat(relative).ToArray());
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("could not locate " + Path.Combine(relative));
        }
    }
}
