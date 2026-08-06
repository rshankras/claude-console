namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    using Loupedeck.ClaudeConsolePlugin.Models;

    using Xunit;

    /// <summary>
    /// The session grid: which sessions exist and which key each one owns.
    ///
    /// The property that matters most on real hardware is SLOT STABILITY — a session must keep its
    /// key for as long as it lives. If slots reshuffled when a session exited, muscle memory would
    /// send an approval to the wrong session, which is precisely the accident this feature exists
    /// to prevent.
    ///
    /// These tests drive the real files the bash writers produce, in a temp IPC root.
    /// </summary>
    public class SessionRegistryTests : IDisposable
    {
        // A throwaway root per test. SessionRegistry DELETES files it judges dead, so it must never
        // be pointed at the live /tmp/claude-console — a test run would wipe a running session's
        // state. Hence the injected directories rather than the IpcPaths defaults.
        private readonly String _root =
            Path.Combine(Path.GetTempPath(), "cc-grid-" + Guid.NewGuid().ToString("N"));

        private readonly String _sessionsDir;
        private readonly String _activityDir;
        private readonly String _registryFile;

        public SessionRegistryTests()
        {
            _sessionsDir = Path.Combine(_root, "sessions");
            _activityDir = Path.Combine(_root, "activity");
            _registryFile = Path.Combine(_root, "registry.json");
            Directory.CreateDirectory(_sessionsDir);
            Directory.CreateDirectory(_activityDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        private SessionRegistry NewRegistry() => new SessionRegistry(_sessionsDir, _activityDir, _registryFile);

        private String StateFor(String tty) => Path.Combine(_sessionsDir, tty + ".json");
        private String ActivityFor(String tty) => Path.Combine(_activityDir, tty + ".json");

        // --- fixtures -------------------------------------------------------------------------

        private void WriteSession(String tty, String projectDir, Int32 ctxPercent, DateTime? updatedAt = null)
        {
            var state = new
            {
                session_id = "sid-" + tty,
                session_name = "name-" + tty,
                workspace = new { project_dir = projectDir, current_dir = projectDir },
                context_window = new { used_percentage = ctxPercent, context_window_size = 1000000, total_input_tokens = 0 },
            };
            var path = this.StateFor(tty);
            File.WriteAllText(path, JsonSerializer.Serialize(state));
            if (updatedAt.HasValue)
            {
                File.SetLastWriteTimeUtc(path, updatedAt.Value);
            }
        }

        private void WriteActivity(String tty, String state, Int64? ts = null)
        {
            File.WriteAllText(
                this.ActivityFor(tty),
                JsonSerializer.Serialize(new { state, ts = ts ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds() }));
        }

        private SessionRegistry RefreshedWith(params String[] liveTtys)
        {
            var registry = this.NewRegistry();
            registry.Refresh(new HashSet<String>(liveTtys, StringComparer.Ordinal));
            return registry;
        }

        // --- reading sessions -----------------------------------------------------------------

        [Fact]
        public void Reads_project_name_and_context_from_the_statusline_file()
        {
            WriteSession("ttys001", "/Users/x/Work/MyApps/claude-console", 35);

            var session = RefreshedWith("ttys001").SlotSession(1);

            Assert.Equal("claude-console", session.Project);
            Assert.Equal(35, session.CtxPercent);
            Assert.Equal("sid-ttys001", session.SessionId);
            Assert.False(session.IsProvisional);
        }

        [Fact]
        public void Ignores_the_shared_fallback_file()
        {
            // sessions/shared.json duplicates whichever tab wrote last; treating it as a tab of its
            // own would put a phantom session on a key.
            WriteSession("ttys001", "/Users/x/proj", 10);
            File.Copy(this.StateFor("ttys001"), Path.Combine(_sessionsDir, "shared.json"));

            var registry = RefreshedWith("ttys001");

            Assert.Single(registry.LiveSessions());
        }

        [Fact]
        public void A_live_tab_with_no_state_file_still_gets_a_key()
        {
            // A session that has just started hasn't rendered a status line yet. It should light a
            // key immediately rather than appearing seconds later.
            var session = RefreshedWith("ttys005").SlotSession(1);

            Assert.NotNull(session);
            Assert.True(session.IsProvisional);
            Assert.Equal("Claude", session.Project);
            Assert.Null(session.CtxPercent);
        }

        [Fact]
        public void A_tab_with_no_live_process_is_reaped()
        {
            WriteSession("ttys002", "/Users/x/old-project", 50);

            var registry = RefreshedWith();   // ps reports nothing alive

            Assert.Empty(registry.LiveSessions());
            Assert.False(File.Exists(this.StateFor("ttys002")), "stale state file should be deleted");
        }

        [Fact]
        public void Activity_state_comes_from_the_hooks()
        {
            WriteSession("ttys001", "/Users/x/proj", 10);
            WriteActivity("ttys001", "waiting");

            Assert.Equal("waiting", RefreshedWith("ttys001").SlotSession(1).State);
        }

        [Fact]
        public void A_session_stuck_on_busy_settles_back_to_ready()
        {
            // The Stop hook can be missed if a session is killed. A key stuck on "Working" forever
            // is worse than one that settles early.
            WriteSession("ttys001", "/Users/x/proj", 10);
            WriteActivity("ttys001", "busy", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600);

            Assert.Equal("ready", RefreshedWith("ttys001").SlotSession(1).State);
        }

        [Fact]
        public void A_recently_busy_session_stays_busy()
        {
            WriteSession("ttys001", "/Users/x/proj", 10);
            WriteActivity("ttys001", "busy", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 2);

            Assert.Equal("busy", RefreshedWith("ttys001").SlotSession(1).State);
        }

        // --- slot assignment ------------------------------------------------------------------

        [Fact]
        public void Sessions_take_slots_oldest_first()
        {
            var now = DateTime.UtcNow;
            WriteSession("ttys003", "/Users/x/third", 1, now);
            WriteSession("ttys001", "/Users/x/first", 1, now.AddMinutes(-10));
            WriteSession("ttys002", "/Users/x/second", 1, now.AddMinutes(-5));

            var registry = RefreshedWith("ttys001", "ttys002", "ttys003");

            Assert.Equal("first", registry.SlotSession(1).Project);
            Assert.Equal("second", registry.SlotSession(2).Project);
            Assert.Equal("third", registry.SlotSession(3).Project);
        }

        [Fact]
        public void Surviving_sessions_keep_their_keys_when_one_exits()
        {
            // THE important one. Close the middle session and the other two must not move.
            var now = DateTime.UtcNow;
            WriteSession("ttys001", "/Users/x/alpha", 1, now.AddMinutes(-10));
            WriteSession("ttys002", "/Users/x/beta", 1, now.AddMinutes(-5));
            WriteSession("ttys003", "/Users/x/gamma", 1, now);

            var registry = this.NewRegistry();
            registry.Refresh(new HashSet<String> { "ttys001", "ttys002", "ttys003" });
            Assert.Equal("gamma", registry.SlotSession(3).Project);

            registry.Refresh(new HashSet<String> { "ttys001", "ttys003" });   // beta exits

            Assert.Equal("alpha", registry.SlotSession(1).Project);
            Assert.Null(registry.SlotSession(2));                 // freed key stays empty
            Assert.Equal("gamma", registry.SlotSession(3).Project); // gamma did NOT slide down
        }

        [Fact]
        public void A_new_session_reuses_the_freed_key()
        {
            var now = DateTime.UtcNow;
            WriteSession("ttys001", "/Users/x/alpha", 1, now.AddMinutes(-10));
            WriteSession("ttys002", "/Users/x/beta", 1, now.AddMinutes(-5));

            var registry = this.NewRegistry();
            registry.Refresh(new HashSet<String> { "ttys001", "ttys002" });
            registry.Refresh(new HashSet<String> { "ttys002" });    // alpha exits, freeing slot 1

            WriteSession("ttys009", "/Users/x/delta", 1, now);
            registry.Refresh(new HashSet<String> { "ttys002", "ttys009" });

            Assert.Equal("delta", registry.SlotSession(1).Project);
            Assert.Equal("beta", registry.SlotSession(2).Project);
        }

        [Fact]
        public void Only_six_sessions_get_keys()
        {
            var live = new List<String>();
            for (var i = 1; i <= 8; i++)
            {
                var tty = $"ttys00{i}";
                live.Add(tty);
                WriteSession(tty, $"/Users/x/p{i}", 1, DateTime.UtcNow.AddMinutes(-20 + i));
            }

            var registry = RefreshedWith(live.ToArray());

            Assert.Equal(SessionRegistry.SlotCount, registry.LiveSessions().Count);
            Assert.NotNull(registry.SlotSession(6));
        }

        [Fact]
        public void Slot_assignments_survive_a_plugin_reload()
        {
            var now = DateTime.UtcNow;
            WriteSession("ttys001", "/Users/x/alpha", 1, now.AddMinutes(-10));
            WriteSession("ttys002", "/Users/x/beta", 1, now.AddMinutes(-5));
            this.NewRegistry().Refresh(new HashSet<String> { "ttys001", "ttys002" });

            var reloaded = this.NewRegistry();   // simulates the plugin restarting
            reloaded.LoadPersisted();
            reloaded.Refresh(new HashSet<String> { "ttys001", "ttys002" });

            Assert.Equal("alpha", reloaded.SlotSession(1).Project);
            Assert.Equal("beta", reloaded.SlotSession(2).Project);
        }

        [Fact]
        public void Registry_file_is_written_owner_only()
        {
            WriteSession("ttys001", "/Users/x/alpha", 1);
            RefreshedWith("ttys001");

            Assert.True(File.Exists(_registryFile));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(_registryFile));
        }

        [Fact]
        public void Registry_is_not_rewritten_when_nothing_changed()
        {
            // The poll runs twice a second; rewriting the registry every time would be pointless disk
            // churn (and defeats the only-on-change contract).
            WriteSession("ttys001", "/Users/x/alpha", 1);
            var registry = this.NewRegistry();
            registry.Refresh(new HashSet<String> { "ttys001" });
            var firstWrite = File.GetLastWriteTimeUtc(_registryFile);

            File.SetLastWriteTimeUtc(_registryFile, firstWrite.AddDays(-1));
            var marker = File.GetLastWriteTimeUtc(_registryFile);
            registry.Refresh(new HashSet<String> { "ttys001" });

            Assert.Equal(marker, File.GetLastWriteTimeUtc(_registryFile));
        }

        [Fact]
        public void Sessions_survive_the_polls_that_do_not_rescan_processes()
        {
            // The `ps` scan runs every 4th poll; the others pass null. Treating null as "nothing is
            // alive" made provisional sessions vanish and reappear twice a second — the keys
            // flickered and a press could land on a momentarily empty slot ("slot N is empty").
            var registry = this.NewRegistry();
            registry.Refresh(new HashSet<String> { "ttys004" });   // scan tick: provisional appears
            Assert.NotNull(registry.SlotSession(1));

            registry.Refresh(null);                                // in-between tick
            registry.Refresh(null);

            Assert.NotNull(registry.SlotSession(1));
            Assert.Equal("ttys004", registry.SlotSession(1).Tty);
        }

        [Fact]
        public void A_scan_that_reports_nothing_still_reaps()
        {
            // An explicit empty scan means "nothing is running" and must be honoured — only a null
            // (no scan this tick) is the one we carry over.
            WriteSession("ttys001", "/Users/x/alpha", 1);
            var registry = this.NewRegistry();
            registry.Refresh(new HashSet<String> { "ttys001" });
            Assert.NotNull(registry.SlotSession(1));

            registry.Refresh(new HashSet<String>());

            Assert.Null(registry.SlotSession(1));
        }

        // --- change notification ---------------------------------------------------------------

        [Fact]
        public void Signals_a_repaint_when_a_session_changes_state()
        {
            WriteSession("ttys001", "/Users/x/alpha", 1);
            var registry = this.NewRegistry();
            registry.Refresh(new HashSet<String> { "ttys001" });

            var repaints = 0;
            registry.OnGridChanged += () => repaints++;
            WriteActivity("ttys001", "busy");
            registry.Refresh(new HashSet<String> { "ttys001" });

            Assert.Equal(1, repaints);
        }

        [Fact]
        public void Does_not_repaint_when_only_the_timestamp_moved()
        {
            // A quiet session's file gets rewritten with identical content; the LCD must not churn.
            WriteSession("ttys001", "/Users/x/alpha", 1);
            var registry = this.NewRegistry();
            registry.Refresh(new HashSet<String> { "ttys001" });

            var repaints = 0;
            registry.OnGridChanged += () => repaints++;
            WriteSession("ttys001", "/Users/x/alpha", 1);   // same values, new mtime
            registry.Refresh(new HashSet<String> { "ttys001" });

            Assert.Equal(0, repaints);
        }

        // --- helpers ---------------------------------------------------------------------------

        [Theory]
        [InlineData("/Users/x/Work/MyApps/claude-console", "claude-console")]
        [InlineData("/Users/x/Work/MyApps/claude-console/", "claude-console")]
        [InlineData("/", "Claude")]
        [InlineData("", "Claude")]
        [InlineData(null, "Claude")]
        public void Project_name_is_the_directory_basename(String dir, String expected)
        {
            Assert.Equal(expected, SessionRegistry.ProjectName(dir));
        }

        [Fact]
        public void Context_percent_falls_back_to_the_token_ratio()
        {
            // used_percentage is what the status line normally provides; the ratio is the fallback.
            var state = new ClaudeState
            {
                ContextWindow = new ContextInfo { TotalInputTokens = 250_000, MaxTokens = 1_000_000 },
            };

            Assert.Equal(25, SessionRegistry.ContextPercent(state));
        }
    }
}
