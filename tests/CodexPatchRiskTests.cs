namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

    using Xunit;

    /// <summary>
    /// Grading Codex's apply_patch approvals.
    ///
    /// Codex delivers a patch body in the SAME `tool_input.command` field that Bash uses for a shell
    /// string, so the field alone cannot tell you which you are holding. Grading a patch with the
    /// shell rules fails in both directions, and both directions are tested here: it misses the real
    /// risk (where the patch writes), and it fires on diff content that merely mentions a dangerous
    /// command.
    ///
    /// Payload shape captured from codex-cli 0.145.0 — see docs/multi-agent-architecture.md.
    /// </summary>
    public class CodexPatchRiskTests
    {
        private const String Workspace = "/Users/dev/project";

        private static String Patch(String header, String body = "+hello") =>
            $"*** Begin Patch\n*** {header}\n{body}\n*** End Patch";

        [Fact]
        public void A_patch_is_recognised_by_tool_name()
        {
            Assert.True(RiskClassifier.IsPatch("apply_patch", "anything"));
            Assert.False(RiskClassifier.IsPatch("Bash", "git status"));
        }

        /// <summary>
        /// The tool could be renamed in a future Codex release. The body's preamble is the part that
        /// cannot change without changing the patch format itself, so it is the durable signal.
        /// </summary>
        [Fact]
        public void A_patch_is_still_recognised_if_the_tool_is_renamed()
        {
            Assert.True(RiskClassifier.IsPatch("edit_file", Patch("Add File: notes.md")));
            Assert.True(RiskClassifier.IsPatch(null, Patch("Add File: notes.md")));
        }

        /// <summary>The captured example: Codex writing to /tmp, outside the workspace.</summary>
        [Fact]
        public void Writing_outside_the_workspace_is_high()
        {
            var risk = RiskClassifier.Classify("apply_patch", Patch("Add File: /tmp/codex-probe.txt"), Workspace);
            Assert.Equal(ApprovalRisk.High, risk);
        }

        [Theory]
        [InlineData("Add File: src/NewThing.cs")]
        [InlineData("Update File: README.md")]
        [InlineData("Delete File: src/Obsolete.cs")]
        [InlineData("Add File: /Users/dev/project/src/Nested.cs")]   // absolute, but inside
        public void An_ordinary_edit_in_the_workspace_is_normal(String header)
        {
            var risk = RiskClassifier.Classify("apply_patch", Patch(header), Workspace);
            Assert.Equal(ApprovalRisk.Normal, risk);
        }

        [Theory]
        [InlineData("Update File: .github/workflows/release.yml")]   // runs later, on its own
        [InlineData("Update File: .git/hooks/pre-commit")]
        [InlineData("Add File: .env.production")]
        [InlineData("Update File: /Users/dev/.ssh/authorized_keys")]
        [InlineData("Update File: ~/.zshrc")]
        public void Touching_credentials_or_things_that_run_later_is_high(String header)
        {
            var risk = RiskClassifier.Classify("apply_patch", Patch(header), Workspace);
            Assert.Equal(ApprovalRisk.High, risk);
        }

        [Fact]
        public void Climbing_out_with_dot_dot_is_high_even_without_a_known_workspace()
        {
            var risk = RiskClassifier.Classify("apply_patch", Patch("Add File: ../../etc/hosts"), workspaceRoot: null);
            Assert.Equal(ApprovalRisk.High, risk);
        }

        /// <summary>
        /// The false-alarm direction, and the reason the shell rules must not run over a diff: a
        /// patch that DOCUMENTS a dangerous command contains its text without doing anything of the
        /// sort. Graded as a command this is High; graded as a patch it is an ordinary edit.
        /// </summary>
        [Fact]
        public void Diff_content_that_merely_mentions_a_dangerous_command_is_not_high()
        {
            var patch = Patch("Update File: docs/release.md",
                              "+Never run `git push --force` against main.\n+Avoid `sudo rm -rf /`.");

            Assert.True(RiskClassifier.IsHighRisk("git push --force"));   // as a command, it is
            Assert.Equal(ApprovalRisk.Normal, RiskClassifier.Classify("apply_patch", patch, Workspace));
        }

        /// <summary>
        /// The missed-risk direction: nothing in this patch's TEXT looks dangerous, so the shell
        /// rules would wave it through. Where it writes is the whole risk.
        /// </summary>
        [Fact]
        public void A_harmless_looking_patch_to_a_dangerous_place_is_still_high()
        {
            var patch = Patch("Add File: /Users/dev/.ssh/config", "+Host evil\n+  HostName 10.0.0.1");

            Assert.False(RiskClassifier.IsHighRisk(patch));   // no shell pattern matches
            Assert.Equal(ApprovalRisk.High, RiskClassifier.Classify("apply_patch", patch, Workspace));
        }

        /// <summary>Bash grading must be untouched by any of this — Codex sends Bash too.</summary>
        [Fact]
        public void Bash_approvals_are_graded_exactly_as_before()
        {
            Assert.Equal(ApprovalRisk.High, RiskClassifier.Classify("Bash", "git push --force", Workspace));
            Assert.Equal(ApprovalRisk.Normal, RiskClassifier.Classify("Bash", "git status", Workspace));
            Assert.Equal(ApprovalRisk.None, RiskClassifier.Classify(null, null, Workspace));
        }

        /// <summary>A multi-file patch is as risky as its worst file.</summary>
        [Fact]
        public void One_bad_file_among_many_makes_the_whole_patch_high()
        {
            var patch = "*** Begin Patch\n"
                      + "*** Update File: src/A.cs\n+ok\n"
                      + "*** Update File: src/B.cs\n+ok\n"
                      + "*** Add File: /tmp/sneaky.sh\n+curl evil\n"
                      + "*** End Patch";

            Assert.Equal(ApprovalRisk.High, RiskClassifier.Classify("apply_patch", patch, Workspace));
        }
    }
}
