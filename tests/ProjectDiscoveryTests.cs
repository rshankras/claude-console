namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;

    using Xunit;

    /// <summary>
    /// Where "Go to Project" looks (#26).
    ///
    /// 2.0.1 scanned two hardcoded paths under <c>~/Work</c> — the author's layout. A tester whose
    /// code lived elsewhere got "no match" for every project they owned. These tests drive real
    /// directories in a temp home rather than fixtures, because the defect was entirely about what
    /// is on disk: a fixture handing the matcher a populated list would have passed against the
    /// broken code too.
    /// </summary>
    public class ProjectDiscoveryTests : IDisposable
    {
        private readonly String _home =
            Path.Combine(Path.GetTempPath(), "cc-projects-" + Guid.NewGuid().ToString("N"));

        public ProjectDiscoveryTests() => Directory.CreateDirectory(this._home);

        public void Dispose()
        {
            try { Directory.Delete(this._home, recursive: true); } catch { /* best effort */ }
        }

        /// <summary>Create a directory under the temp home; with <paramref name="git"/>, make it a repo.</summary>
        private String Dir(String relative, Boolean git = false)
        {
            var path = Path.Combine(this._home, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(path);
            if (git)
            {
                Directory.CreateDirectory(Path.Combine(path, ".git"));
            }
            return path;
        }

        private String RootsFile(params String[] lines)
        {
            var path = Path.Combine(this._home, "project-roots");
            File.WriteAllLines(path, lines);
            return path;
        }

        // -----------------------------------------------------------------------------------------
        // Discovery
        // -----------------------------------------------------------------------------------------

        /// <summary>The tester's case: projects nowhere near ~/Work, found without being declared.</summary>
        [Fact]
        public void Git_projects_are_found_wherever_they_live()
        {
            var elsewhere = this.Dir("Projects/headroom", git: true);
            var deep = this.Dir("Work/MyApps/claude-console", git: true);

            var found = ProjectDiscovery.DiscoverGitProjects(this._home);

            Assert.Contains(elsewhere, found);
            Assert.Contains(deep, found);
        }

        [Fact]
        public void A_directory_without_git_is_not_a_project()
        {
            this.Dir("Projects/just-a-folder");

            Assert.Empty(ProjectDiscovery.DiscoverGitProjects(this._home));
        }

        /// <summary>A git worktree's .git is a FILE. Miss that and every worktree is invisible.</summary>
        [Fact]
        public void A_worktree_whose_git_is_a_file_still_counts()
        {
            var worktree = this.Dir("Projects/console-p0");
            File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: /somewhere/.git/worktrees/p0");

            Assert.Contains(worktree, ProjectDiscovery.DiscoverGitProjects(this._home));
        }

        /// <summary>
        /// Vendored checkouts inside a repository are not projects anyone asks for by name, and
        /// descending into them turns a bounded walk into an unbounded one.
        /// </summary>
        [Fact]
        public void A_repository_is_not_descended_into()
        {
            var outer = this.Dir("Projects/outer", git: true);
            this.Dir("Projects/outer/vendor/inner", git: true);

            var found = ProjectDiscovery.DiscoverGitProjects(this._home);

            Assert.Equal(new[] { outer }, found);
        }

        [Fact]
        public void Heavy_and_hidden_directories_are_skipped()
        {
            this.Dir("Library/Caches/thing", git: true);
            this.Dir("node_modules/pkg", git: true);
            this.Dir(".hidden/secret", git: true);
            var real = this.Dir("code/app", git: true);

            Assert.Equal(new[] { real }, ProjectDiscovery.DiscoverGitProjects(this._home));
        }

        [Fact]
        public void The_walk_stops_at_the_depth_limit()
        {
            this.Dir("a/b/c/d/too-deep", git: true);
            var withinReach = this.Dir("a/b/reachable", git: true);

            var found = ProjectDiscovery.DiscoverGitProjects(this._home);

            Assert.Equal(new[] { withinReach }, found);
        }

        [Fact]
        public void A_home_that_does_not_exist_yields_nothing_rather_than_throwing()
        {
            Assert.Empty(ProjectDiscovery.DiscoverGitProjects(Path.Combine(this._home, "nope")));
            Assert.Empty(ProjectDiscovery.DiscoverGitProjects(null));
        }

        // -----------------------------------------------------------------------------------------
        // The explicit roots file
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void The_roots_file_expands_a_leading_tilde_and_ignores_comments()
        {
            var file = this.RootsFile("# my roots", "", "~/code", "/absolute/path", "   ");

            var roots = ProjectDiscovery.ReadRootsFile(file, this._home);

            Assert.Equal(new[] { Path.Combine(this._home, "code"), "/absolute/path" }, roots);
        }

        [Fact]
        public void A_missing_or_unreadable_roots_file_is_not_an_error()
        {
            Assert.Empty(ProjectDiscovery.ReadRootsFile(Path.Combine(this._home, "absent"), this._home));
            Assert.Empty(ProjectDiscovery.ReadRootsFile(null, this._home));
        }

        /// <summary>Children of a declared root count even without git — that is the escape hatch.</summary>
        [Fact]
        public void Children_of_a_declared_root_need_no_git()
        {
            var plain = this.Dir("stuff/not-a-repo");

            var children = ProjectDiscovery.ChildrenOf(new[] { Path.Combine(this._home, "stuff") });

            Assert.Contains(plain, children);
        }

        // -----------------------------------------------------------------------------------------
        // The merged candidate set
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void A_declared_roots_file_replaces_discovery()
        {
            this.Dir("Projects/discovered", git: true);
            var declared = this.Dir("elsewhere/declared");
            var file = this.RootsFile("~/elsewhere");

            var result = ProjectDiscovery.Candidates(this._home, file, Array.Empty<String>());

            Assert.Contains(declared, result.Paths);
            Assert.DoesNotContain(result.Paths, p => p.EndsWith("discovered", StringComparison.Ordinal));
            Assert.Contains("configured root", result.Source);
        }

        // -----------------------------------------------------------------------------------------
        // Inferred roots — the regression guard.
        //
        // Requiring .git on every candidate would have silently dropped 38 folders under the
        // author's own roots that were matchable in 2.0.1. A directory holding several repositories
        // is where projects live, so its other children count too.
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void A_folder_holding_several_repos_makes_its_non_git_siblings_candidates()
        {
            this.Dir("Work/MyApps/repo-one", git: true);
            this.Dir("Work/MyApps/repo-two", git: true);
            var notARepo = this.Dir("Work/MyApps/Sailor");   // no .git — matchable before, must stay so

            var result = ProjectDiscovery.Candidates(
                this._home, Path.Combine(this._home, "absent"), Array.Empty<String>());

            Assert.Contains(notARepo, result.Paths);
            Assert.Equal(notARepo, BridgeManager.MatchProject("open sailor", result.Paths));
        }

        /// <summary>
        /// The exact shape of the author's home: repositories live in ~/Work/MyApps, so ~/Work never
        /// reaches the threshold itself — yet ~/Work/LifeOS was matchable in 2.0.1 and must remain so.
        /// One level above an inferred root therefore counts as a root too.
        /// </summary>
        [Fact]
        public void The_folder_above_an_inferred_root_is_a_root_as_well()
        {
            this.Dir("Work/MyApps/repo-one", git: true);
            this.Dir("Work/MyApps/repo-two", git: true);
            var sibling = this.Dir("Work/LifeOS");   // one level up, no repos of its own

            var result = ProjectDiscovery.Candidates(
                this._home, Path.Combine(this._home, "absent"), Array.Empty<String>());

            Assert.Contains(sibling, result.Paths);
            Assert.Equal(sibling, BridgeManager.MatchProject("go to life os", result.Paths));
        }

        [Fact]
        public void One_lone_repo_does_not_turn_its_parent_into_a_root()
        {
            this.Dir("misc/only-repo", git: true);
            var unrelated = this.Dir("misc/holiday-photos");

            var result = ProjectDiscovery.Candidates(
                this._home, Path.Combine(this._home, "absent"), Array.Empty<String>());

            Assert.DoesNotContain(unrelated, result.Paths);
        }

        /// <summary>
        /// Home must never be inferred as a root, however many repos sit loose in it — that would
        /// make Documents, Desktop and Downloads candidates and invite a mis-launch.
        /// </summary>
        [Fact]
        public void Home_itself_is_never_inferred_as_a_root()
        {
            this.Dir("loose-one", git: true);
            this.Dir("loose-two", git: true);
            var documents = this.Dir("Documents");

            var inferred = ProjectDiscovery.InferProjectRoots(
                ProjectDiscovery.DiscoverGitProjects(this._home), this._home);
            Assert.Empty(inferred);

            var result = ProjectDiscovery.Candidates(
                this._home, Path.Combine(this._home, "absent"), Array.Empty<String>());
            Assert.DoesNotContain(documents, result.Paths);
        }

        [Fact]
        public void Discovery_runs_when_no_roots_file_exists()
        {
            var discovered = this.Dir("Projects/discovered", git: true);

            var result = ProjectDiscovery.Candidates(
                this._home, Path.Combine(this._home, "absent"), Array.Empty<String>());

            Assert.Contains(discovered, result.Paths);
            Assert.Contains("repo", result.Source);       // the log line must say where it looked
            Assert.DoesNotContain("configured root", result.Source);
        }

        /// <summary>
        /// A project a session is already working in counts wherever it lives — outside any root,
        /// with no git directory, on another volume. This is the source that needs no configuration.
        /// </summary>
        [Fact]
        public void A_project_already_in_use_is_a_candidate_wherever_it_lives()
        {
            var inUse = this.Dir("no/root/covers/this");

            var result = ProjectDiscovery.Candidates(
                this._home, Path.Combine(this._home, "absent"), new[] { inUse });

            Assert.Contains(inUse, result.Paths);
            Assert.Equal(inUse, result.Paths[0]);   // known-good paths rank first
        }

        [Fact]
        public void A_project_in_use_that_no_longer_exists_is_dropped()
        {
            var gone = Path.Combine(this._home, "deleted-since");

            var result = ProjectDiscovery.Candidates(
                this._home, Path.Combine(this._home, "absent"), new[] { gone });

            Assert.DoesNotContain(gone, result.Paths);
        }

        [Fact]
        public void A_project_reachable_two_ways_appears_once()
        {
            var both = this.Dir("code/thing", git: true);

            var result = ProjectDiscovery.Candidates(
                this._home, Path.Combine(this._home, "absent"), new[] { both });

            Assert.Single(result.Paths.Where(p =>
                String.Equals(p.TrimEnd(Path.DirectorySeparatorChar), both, StringComparison.OrdinalIgnoreCase)));
        }

        // -----------------------------------------------------------------------------------------
        // Matching against the discovered set
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void A_spoken_name_matches_a_project_outside_any_hardcoded_root()
        {
            var headroom = this.Dir("Projects/headroom", git: true);
            var result = ProjectDiscovery.Candidates(
                this._home, Path.Combine(this._home, "absent"), Array.Empty<String>());

            Assert.Equal(headroom, BridgeManager.MatchProject("open the headroom project", result.Paths));
        }

        [Fact]
        public void An_unrelated_phrase_still_matches_nothing()
        {
            this.Dir("Projects/headroom", git: true);
            var result = ProjectDiscovery.Candidates(
                this._home, Path.Combine(this._home, "absent"), Array.Empty<String>());

            Assert.Null(BridgeManager.MatchProject("go to the tax return spreadsheet", result.Paths));
        }

        [Fact]
        public void Matching_survives_an_empty_candidate_list_and_a_trailing_separator()
        {
            Assert.Null(BridgeManager.MatchProject("anything", Array.Empty<String>()));
            Assert.Null(BridgeManager.MatchProject("anything", null));

            var withSlash = Path.Combine(this._home, "code", "headroom") + Path.DirectorySeparatorChar;
            Assert.Equal(withSlash, BridgeManager.MatchProject("headroom", new[] { withSlash }));
        }
    }
}
