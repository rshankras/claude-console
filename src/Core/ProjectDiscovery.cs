namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Where "Go to Project" looks for projects.
    ///
    /// Until 2.0.1 the answer was two hardcoded paths — <c>~/Work/MyApps</c> and <c>~/Work</c> — which
    /// are the author's folders and nobody else's. A tester whose code lives in <c>~/Projects</c> got
    /// "no match" for every project they own, with no setting to change it and nothing in the log to
    /// say why (#26). The key was not broken for them; it could not see them.
    ///
    /// Three sources, best first:
    ///
    /// 1. <b>Projects already in use.</b> Every session reports its own <c>project_dir</c>, so these
    ///    are known-good paths wherever they live. Free, exact, and previously unmatchable.
    /// 2. <b>An explicit list</b>, if <c>~/.claude/claude-console/project-roots</c> exists: one root
    ///    per line, children become candidates. This is the old behaviour, made configurable, and it
    ///    is the escape hatch for projects that are not git repositories.
    /// 3. <b>Discovery</b>, otherwise: directories containing <c>.git</c>, within three levels of
    ///    home. Finds the author's layout and the tester's without either being declared.
    /// </summary>
    internal static class ProjectDiscovery
    {
        internal const Int32 MaxDepth = 3;   // ~/Work/MyApps/claude-console is exactly three deep

        /// <summary>
        /// Directories never worth descending: large, and never where source lives. Everything
        /// starting with a dot is skipped separately, which also stops us walking into .git itself.
        /// </summary>
        private static readonly String[] SkipNames =
        {
            "Library", "Applications", "AppData", "Music", "Movies", "Pictures",
            "node_modules", "Trash", "OneDrive", "Dropbox",
        };

        /// <summary>The candidate set, with a one-line description of where it came from (for the log).</summary>
        internal sealed class Result
        {
            public IReadOnlyList<String> Paths { get; init; } = Array.Empty<String>();
            public String Source { get; init; } = "";
        }

        internal static String DefaultRootsFile(String home) =>
            Path.Combine(home ?? "", ".claude", "claude-console", "project-roots");

        /// <summary>
        /// Parse a roots file: one path per line, blank lines and #-comments ignored, a leading ~
        /// expanded. Unreadable or absent file yields nothing — never an exception, because this
        /// runs on the voice path and a typo in a config file must not cost the user their dictation.
        /// </summary>
        internal static IReadOnlyList<String> ReadRootsFile(String path, String home)
        {
            var roots = new List<String>();
            if (String.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return roots;
            }

            String[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { return roots; }

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }
                if (line == "~")
                {
                    line = home ?? "";
                }
                else if (line.StartsWith("~/", StringComparison.Ordinal) || line.StartsWith("~\\", StringComparison.Ordinal))
                {
                    line = Path.Combine(home ?? "", line.Substring(2));
                }
                if (line.Length > 0)
                {
                    roots.Add(line);
                }
            }

            return roots;
        }

        /// <summary>Immediate children of each root — the pre-2.2.0 notion of a candidate.</summary>
        internal static IReadOnlyList<String> ChildrenOf(IEnumerable<String> roots)
        {
            var found = new List<String>();
            foreach (var root in roots ?? Enumerable.Empty<String>())
            {
                if (String.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    continue;
                }
                try { found.AddRange(Directory.GetDirectories(root)); }
                catch { /* unreadable root — skip it rather than fail the whole lookup */ }
            }

            return found;
        }

        /// <summary>
        /// Directories containing a <c>.git</c> entry, within <paramref name="maxDepth"/> levels of
        /// <paramref name="home"/>. A repository is not descended into: its submodules and vendored
        /// checkouts are not projects you would ask for by name.
        /// </summary>
        internal static IReadOnlyList<String> DiscoverGitProjects(String home, Int32 maxDepth = MaxDepth)
        {
            var found = new List<String>();
            if (String.IsNullOrEmpty(home) || !Directory.Exists(home))
            {
                return found;
            }

            Walk(home, 0);
            return found;

            void Walk(String dir, Int32 depth)
            {
                if (depth >= maxDepth)
                {
                    return;
                }

                String[] children;
                try { children = Directory.GetDirectories(dir); }
                catch { return; }   // permission denied, or it vanished mid-walk

                foreach (var child in children)
                {
                    var name = Path.GetFileName(child);
                    if (name.Length == 0 || name[0] == '.')
                    {
                        continue;   // hidden, and this is what keeps us out of .git
                    }
                    if (SkipNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (Directory.Exists(Path.Combine(child, ".git")) || File.Exists(Path.Combine(child, ".git")))
                    {
                        found.Add(child);   // a worktree's .git is a FILE, not a directory
                        continue;
                    }

                    Walk(child, depth + 1);
                }
            }
        }

        /// <summary>
        /// Infer the directories that hold projects, from where the discovered repositories sit.
        ///
        /// Requiring <c>.git</c> on every candidate would have been a regression: 38 folders under
        /// the author's own roots are not repositories — checkouts in progress, app folders, notes —
        /// and every one of them was matchable before. A directory containing
        /// <paramref name="minRepos"/> or more repositories is evidently where projects are kept, so
        /// all of its children count, git or not. That reconstructs exactly the roots a user would
        /// have written by hand, without asking them to.
        ///
        /// Home itself is never inferred, however many repositories sit loose in it — that would make
        /// Documents, Desktop and Downloads candidates and invite a mis-launch.
        /// </summary>
        internal static IReadOnlyList<String> InferProjectRoots(IEnumerable<String> repos, String home, Int32 minRepos = 2)
        {
            var homeKey = TrimSlash(home ?? "");
            var counts = new Dictionary<String, Int32>(StringComparer.OrdinalIgnoreCase);

            foreach (var repo in repos ?? Enumerable.Empty<String>())
            {
                String parent;
                try { parent = Path.GetDirectoryName(TrimSlash(repo)); }
                catch { continue; }

                if (String.IsNullOrEmpty(parent)
                    || String.Equals(TrimSlash(parent), homeKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                counts[parent] = counts.TryGetValue(parent, out var n) ? n + 1 : 1;
            }

            var roots = new List<String>();
            var seen = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in counts.Where(kv => kv.Value >= minRepos))
            {
                if (seen.Add(TrimSlash(kv.Key)))
                {
                    roots.Add(kv.Key);
                }
            }

            // ...and one level above each, because the folder that HOLDS your project folders is
            // itself where you keep work. Without this, ~/Work/MyApps is inferred but ~/Work is not
            // (its repositories sit a level deeper), and ~/Work/LifeOS — matchable in 2.0.1 — would
            // quietly stop matching. Only one level, and never home, so this stays bounded.
            foreach (var root in roots.ToList())
            {
                String parent;
                try { parent = Path.GetDirectoryName(TrimSlash(root)); }
                catch { continue; }

                if (!String.IsNullOrEmpty(parent)
                    && !String.Equals(TrimSlash(parent), homeKey, StringComparison.OrdinalIgnoreCase)
                    && seen.Add(TrimSlash(parent)))
                {
                    roots.Add(parent);
                }
            }

            return roots;
        }

        /// <summary>
        /// The full candidate set. <paramref name="knownProjectDirs"/> comes first and always counts;
        /// the roots file, when present, replaces discovery rather than supplementing it, so someone
        /// who has declared their layout does not also pay for a walk of their home directory.
        /// </summary>
        internal static Result Candidates(String home, String rootsFile, IEnumerable<String> knownProjectDirs)
        {
            var seen = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            var paths = new List<String>();
            var inUse = 0;

            foreach (var dir in knownProjectDirs ?? Enumerable.Empty<String>())
            {
                if (!String.IsNullOrEmpty(dir) && Directory.Exists(dir) && seen.Add(TrimSlash(dir)))
                {
                    paths.Add(dir);
                    inUse++;
                }
            }

            var roots = ReadRootsFile(rootsFile, home);
            String origin;
            IReadOnlyList<String> rest;
            if (roots.Count > 0)
            {
                rest = ChildrenOf(roots);
                origin = $"{rest.Count} under {roots.Count} configured root(s)";
            }
            else
            {
                var repos = DiscoverGitProjects(home);
                var inferred = InferProjectRoots(repos, home);
                var siblings = ChildrenOf(inferred);
                rest = repos.Concat(siblings).ToList();
                origin = $"{repos.Count} repo(s) + their siblings under {inferred.Count} inferred root(s), within {MaxDepth} levels of home";
            }

            foreach (var dir in rest)
            {
                if (seen.Add(TrimSlash(dir)))
                {
                    paths.Add(dir);
                }
            }

            return new Result
            {
                Paths = paths,
                Source = $"{inUse} in use + {origin}",
            };
        }

        private static String TrimSlash(String p) => p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
