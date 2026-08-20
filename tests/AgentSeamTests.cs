namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Loupedeck.ClaudeConsolePlugin.Agents;

    using Xunit;

    /// <summary>
    /// The agent seam. Like PlatformSeamTests, these pin a DIVISION OF LABOUR rather than either
    /// agent: a third adapter added later must satisfy every contract here without editing them.
    ///
    /// The invariant worth the most is honesty. A key reads its adapter's capabilities to decide
    /// whether to render at all, so a capability that claims more than the agent reports doesn't
    /// produce a compile error or a crash — it produces a keypad that quietly shows a wrong
    /// number, which is the one failure mode the hardware cannot survive.
    /// </summary>
    public class AgentSeamTests
    {
        private static IAgentAdapter[] All => new IAgentAdapter[]
        {
            new ClaudeCodeAdapter(),
            new CodexCliAdapter(),
        };

        [Fact]
        public void EveryAdapterHasTheIdentityTheIpcLayerNeeds()
        {
            foreach (var a in All)
            {
                Assert.False(String.IsNullOrWhiteSpace(a.Id), $"{a.GetType().Name}: Id");
                Assert.False(String.IsNullOrWhiteSpace(a.DisplayName), $"{a.GetType().Name}: DisplayName");
                Assert.False(String.IsNullOrWhiteSpace(a.CliCommand), $"{a.GetType().Name}: CliCommand");
                Assert.False(String.IsNullOrWhiteSpace(a.ProductSlug), $"{a.GetType().Name}: ProductSlug");
                Assert.NotEmpty(a.ProcessNames);
            }
        }

        /// <summary>
        /// Two consoles can be installed on one keypad, so anything either agent owns on disk must
        /// be unique: the slug names the IPC root, the runtime home and the Options+ registration.
        /// A collision here would have two plugins reading each other's session state.
        /// </summary>
        [Fact]
        public void ProductSlugsAndIdsAreUniqueAcrossAdapters()
        {
            var slugs = All.Select(a => a.ProductSlug).ToList();
            Assert.Equal(slugs.Count, slugs.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            var ids = All.Select(a => a.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        /// <summary>
        /// The slug becomes a directory name and the process names are matched against ps output;
        /// neither may carry separators or whitespace that would change what they address.
        /// </summary>
        [Fact]
        public void SlugsAndProcessNamesArePathAndMatchSafe()
        {
            foreach (var a in All)
            {
                Assert.DoesNotContain(a.ProductSlug, c => c == '/' || c == '\\' || Char.IsWhiteSpace(c));
                foreach (var p in a.ProcessNames)
                {
                    Assert.False(String.IsNullOrWhiteSpace(p));
                    Assert.DoesNotContain(p, c => Char.IsWhiteSpace(c));
                    // The Windows watcher appends the extension itself.
                    Assert.DoesNotContain(".exe", p, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        /// <summary>
        /// A verb an agent has no word for must return null, never an empty or whitespace string:
        /// null hides the key, whereas "" would be typed into the terminal as a bare Return.
        /// </summary>
        [Fact]
        public void UnsupportedVerbsAreNullNotEmpty()
        {
            foreach (var a in All)
            {
                foreach (AgentVerb verb in Enum.GetValues<AgentVerb>())
                {
                    var cmd = a.SlashCommand(verb);
                    if (cmd != null)
                    {
                        Assert.StartsWith("/", cmd);
                        Assert.DoesNotContain(cmd, Char.IsWhiteSpace);
                    }
                }
            }
        }

        /// <summary>
        /// The honesty rule, in the one direction that can mislead: an adapter may not advertise a
        /// documented context percentage AND flag the same number as best-effort. Claiming both
        /// would let a key trust an explicitly unstable reader as though it were a stable one.
        /// </summary>
        [Fact]
        public void ContextCapabilitiesAreNotBothClaimed()
        {
            foreach (var a in All)
            {
                var c = a.Capabilities;
                Assert.False(c.ContextPercent && c.BestEffortContext,
                    $"{a.DisplayName}: a context source is either documented or best-effort, not both");
            }
        }

        /// <summary>
        /// Regression guard for the two facts that motivated the whole seam. Both agents are
        /// approval-signalling and model-reporting — that shared floor is what makes one grid and
        /// one set of approval keys serve both. Codex reports no cost, which is precisely the
        /// difference a capability flag exists to express, and the Cost key must never appear on
        /// a Codex profile showing a zero.
        /// </summary>
        [Fact]
        public void TheSharedFloorAndTheKnownDifferenceBothHold()
        {
            foreach (var a in All)
            {
                Assert.True(a.Capabilities.Model, $"{a.DisplayName}: model reporting");
            }

            // Approval signalling is the shared floor only where a transport delivers it: Claude
            // Code always, Codex wherever its hooks run (everywhere but Windows — see
            // ApprovalSignalFollowsTheTransport).
            Assert.True(new ClaudeCodeAdapter().Capabilities.ApprovalSignal);

            Assert.True(new ClaudeCodeAdapter().Capabilities.Cost);
            Assert.False(new CodexCliAdapter().Capabilities.Cost);
        }

        /// <summary>
        /// Codex trusts hook commands by hash and re-prompts when one changes, so its wiring can
        /// never be silent — where hooks are installed at all. On Windows none are, so no trust
        /// is ever due.
        /// </summary>
        [Fact]
        public void CodexIsMarkedAsNeedingAnInteractiveTrustGrant()
        {
            Assert.Equal(!OperatingSystem.IsWindows(), new CodexCliAdapter().Capabilities.HooksNeedTrust);
            Assert.False(new ClaudeCodeAdapter().Capabilities.HooksNeedTrust);
        }

        /// <summary>
        /// A capability describes what the agent can HONESTLY REPORT HERE, which for Codex means
        /// per-OS: its hook runner creates no process on Windows (hardware-proven, see
        /// docs/spike-windows-codex-hooks.md), so state comes from the rollout stream, which
        /// carries busy/idle edges but no approval event. Claiming ApprovalSignal there would
        /// light keys amber on evidence that does not exist — the same lie as a $0.00 cost.
        /// </summary>
        [Fact]
        public void ApprovalSignalFollowsTheTransport()
        {
            var codex = new CodexCliAdapter().Capabilities;

            Assert.Equal(!OperatingSystem.IsWindows(), codex.ApprovalSignal);

            // Claude Code's transport (settings.json hooks) works on both platforms.
            Assert.True(new ClaudeCodeAdapter().Capabilities.ApprovalSignal);
        }

        /// <summary>
        /// Both agents take a screenshot into the CURRENT conversation — that is the Screenshot
        /// key's whole promise, and it held only after a correction: Codex looked launch-only from
        /// its CLI (`-i` attaches to the initial prompt), but the model reads a file mid-session
        /// with its own image-viewing tool when told the path, proven on hardware by the July
        /// Vizhi plugin. The flag pins the WORKFLOW truth, not the flag-parser truth; if it ever
        /// flips false again, the key silently degrades to session-spawning — which is exactly the
        /// surprise a user reported.
        /// </summary>
        [Fact]
        public void BothAgentsTakeImagesIntoTheCurrentConversation()
        {
            var claude = new ClaudeCodeAdapter().Capabilities;
            var codex = new CodexCliAdapter().Capabilities;

            Assert.True(claude.ImageInConversation);
            Assert.True(codex.ImageInConversation);

            // Codex additionally seeds a session at launch; Claude Code's CLI has no image flag.
            Assert.True(codex.ImageAtLaunch);
            Assert.False(claude.ImageAtLaunch);
        }
    }
}
