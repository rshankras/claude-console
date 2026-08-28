namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;

    using Loupedeck.ClaudeConsolePlugin.Actions;

    using Xunit;

    /// <summary>
    /// A Prompt key asked about with no parameter must answer, not throw (#32).
    ///
    /// The SDK calls a parameterised command's display-name and image hooks with a NULL parameter
    /// in some Options+ contexts. Dictionary.TryGetValue(null) throws ArgumentNullException, so
    /// every such call produced an error trace — dozens per load on QA's machine, and it is the
    /// one defect on the retest list QA saw unchanged from 2.0.0. A PluginDynamicCommand cannot be
    /// constructed outside the host, so the lookup is a static the SDK path goes through.
    /// </summary>
    public class PromptNullParameterTests
    {
        private static readonly IReadOnlyDictionary<String, PromptDef> Prompts = new Dictionary<String, PromptDef>
        {
            ["fix_bug"] = new PromptDef { Id = "fix_bug", Label = "Fix Bug", Prompt = "Fix this bug" },
            ["unlabelled"] = new PromptDef { Id = "unlabelled", Prompt = "…" },
        };

        [Fact]
        public void A_null_parameter_finds_nothing_instead_of_throwing()
        {
            // THE important one. This is the exact call that produced the traces.
            var p = PromptCommand.Find(Prompts, null);

            Assert.Null(p);
        }

        [Fact]
        public void A_null_parameter_still_names_the_key()
        {
            // The SDK wants a string back; "Prompt" is the only honest answer with nothing to go on.
            Assert.Equal("Prompt", PromptCommand.NameFor(PromptCommand.Find(Prompts, null), null));
        }

        [Fact]
        public void A_known_parameter_is_unchanged()
        {
            var p = PromptCommand.Find(Prompts, "fix_bug");

            Assert.NotNull(p);
            Assert.Equal("Fix Bug", PromptCommand.NameFor(p, "fix_bug"));
        }

        [Fact]
        public void An_unknown_parameter_shows_its_own_id()
        {
            // A prompts.json edit can remove an id a profile still binds. The key then shows the
            // id rather than a lie — and, crucially, rather than an exception.
            Assert.Null(PromptCommand.Find(Prompts, "gone"));
            Assert.Equal("gone", PromptCommand.NameFor(null, "gone"));
        }

        [Fact]
        public void A_prompt_without_a_label_falls_back_to_its_id()
        {
            Assert.Equal("unlabelled", PromptCommand.NameFor(PromptCommand.Find(Prompts, "unlabelled"), "unlabelled"));
        }
    }
}
