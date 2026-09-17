namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;

    using Xunit;

    /// <summary>
    /// Whisper's non-speech annotations must never be acted on.
    ///
    /// Found by testing #26 on real hardware: saying "Life" from across the room produced no
    /// intelligible speech, so whisper emitted its label for the noise instead — "(gunshot)", then
    /// "(static)". Both were treated as spoken project names, fuzzy-matched, and OPENED a project
    /// the user had not asked for. A failed dictation did not fail; it did the wrong thing, which
    /// is worse than doing nothing.
    ///
    /// The macOS helper only stripped three exact literals; the Windows helper stripped bracketed
    /// runs properly. Both now feed one rule in the engine.
    /// </summary>
    public class TranscriptCleaningTests
    {
        // The two transcripts that actually launched the wrong project, from the plugin log.
        [Theory]
        [InlineData("(gunshot)")]
        [InlineData("(static)")]
        [InlineData("[BLANK_AUDIO]")]
        [InlineData("(silence)")]
        [InlineData("[ Silence ]")]
        [InlineData("(wind blowing)")]
        [InlineData("(keyboard clacking)")]
        [InlineData("  (music)  ")]
        public void A_transcript_that_is_only_an_annotation_leaves_nothing(String annotation)
        {
            Assert.Equal("", BridgeManager.CleanTranscript(annotation));
        }

        [Fact]
        public void Real_speech_survives_untouched()
        {
            Assert.Equal("Life.", BridgeManager.CleanTranscript("Life."));
            Assert.Equal("open the headroom project", BridgeManager.CleanTranscript("open the headroom project"));
        }

        [Fact]
        public void An_annotation_beside_real_speech_is_removed_and_the_speech_kept()
        {
            Assert.Equal("go to headroom", BridgeManager.CleanTranscript("(cough) go to headroom"));
            Assert.Equal("go to headroom", BridgeManager.CleanTranscript("go to [BLANK_AUDIO] headroom"));
            Assert.Equal("fix the tests", BridgeManager.CleanTranscript("fix the tests (typing)"));
        }

        /// <summary>
        /// Unbalanced brackets resolve in the SAFE direction, deliberately.
        ///
        /// A stray ')' never opened an annotation, so it is ordinary text and the words survive
        /// (NormalizeForMatch strips the punctuation again before matching). An unclosed '(' means
        /// whisper began an annotation, so everything after it is discarded — dropping text can
        /// only cause a failed match, whereas keeping annotation text can launch a wrong project.
        /// </summary>
        [Fact]
        public void Unbalanced_brackets_resolve_towards_dropping_text()
        {
            Assert.Equal("headroom)", BridgeManager.CleanTranscript("headroom)"));
            Assert.Equal("go to", BridgeManager.CleanTranscript("go to (headroom"));
        }

        [Fact]
        public void Empty_and_null_are_handled()
        {
            Assert.Equal("", BridgeManager.CleanTranscript(""));
            Assert.Equal("", BridgeManager.CleanTranscript(null));
            Assert.Equal("", BridgeManager.CleanTranscript("   "));
        }

        // -----------------------------------------------------------------------------------------
        // The two launches that actually happened, end to end.
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void The_noise_that_opened_SafeShot_now_matches_nothing()
        {
            var projects = new[] { "/Users/x/Work/MyApps/SafeShot", "/Users/x/Life" };
            var spoken = BridgeManager.CleanTranscript("(gunshot)");

            Assert.Equal("", spoken);
            Assert.Null(BridgeManager.MatchProject(spoken, projects));
        }

        [Fact]
        public void The_noise_that_opened_StatementSense_now_matches_nothing()
        {
            var projects = new[] { "/Users/x/Work/MyApps/StatementSense", "/Users/x/Life" };
            var spoken = BridgeManager.CleanTranscript("(static)");

            Assert.Equal("", spoken);
            Assert.Null(BridgeManager.MatchProject(spoken, projects));
        }

        /// <summary>The attempt that worked must keep working.</summary>
        [Fact]
        public void The_transcript_that_correctly_opened_Life_still_does()
        {
            var projects = new[] { "/Users/x/Life", "/Users/x/Work/LifeOS", "/Users/x/Work/MyApps/SafeShot" };
            var spoken = BridgeManager.CleanTranscript("Life.");

            Assert.Equal("/Users/x/Life", BridgeManager.MatchProject(spoken, projects));
        }

        // -----------------------------------------------------------------------------------------
        // The tightened fuzzy threshold — the second line of defence.
        // -----------------------------------------------------------------------------------------

        /// <summary>Even unbracketed, a four-letter overlap is too thin to launch a project on.</summary>
        [Theory]
        [InlineData("gunshot", "safeshot")]
        [InlineData("static", "statementsense")]
        public void A_four_letter_overlap_is_no_longer_a_match(String spoken, String folder)
        {
            Assert.Equal(0, BridgeManager.MatchScore(spoken, folder));
        }

        /// <summary>A genuine mishearing still lands: "tailor"/"sailor" share five characters.</summary>
        [Fact]
        public void A_genuine_mishearing_still_matches()
        {
            Assert.True(BridgeManager.MatchScore("tailor", "sailor") >= 300);
        }
    }
}
