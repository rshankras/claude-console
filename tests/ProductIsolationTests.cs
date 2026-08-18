namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    using Xunit;

    /// <summary>
    /// Keeping two consoles out of each other's files.
    ///
    /// Claude Console and a Codex console can be installed on the same keypad, each with its own
    /// plugin process. They share an engine, so if they also shared an IPC root each would see the
    /// other's sessions — and worse, would reap them: the grid deletes state for sessions whose
    /// process it cannot find, and neither plugin can see the other agent's processes.
    ///
    /// The subtle failure this pins is initialisation ORDER. A product declares itself at load, so
    /// any path captured in a `static readonly` before that runs would freeze the default and point
    /// the second console straight at the first one's tree — with no error anywhere.
    /// </summary>
    [Collection("ipc-product-slug")]
    public class ProductIsolationTests : IDisposable
    {
        private readonly String _original = IpcPaths.ProductSlug;

        public void Dispose() => IpcPaths.UseProduct(this._original);

        [Fact]
        public void The_default_product_is_claude_console() =>
            Assert.Equal("claude-console", this._original);

        [Fact]
        public void Declaring_a_product_moves_the_whole_tree()
        {
            IpcPaths.UseProduct("codex-console");

            Assert.EndsWith("codex-console", IpcPaths.Root);
            Assert.Contains("codex-console", IpcPaths.SessionsDir);
            Assert.Contains("codex-console", IpcPaths.ActivityDir);
            Assert.Contains("codex-console", IpcPaths.VoiceDir);
            Assert.Contains("codex-console", IpcPaths.RegistryFile);
            Assert.Contains("codex-console", IpcPaths.StateFor("ttys003"));
            Assert.Contains("codex-console", IpcPaths.ActivityFor("ttys003"));
        }

        /// <summary>Not one shared path between the two products, anywhere in the tree.</summary>
        [Fact]
        public void Two_products_share_no_path_at_all()
        {
            IpcPaths.UseProduct("claude-console");
            var claude = Snapshot();

            IpcPaths.UseProduct("codex-console");
            var codex = Snapshot();

            for (var i = 0; i < claude.Length; i++)
            {
                Assert.NotEqual(claude[i], codex[i]);
            }
        }

        /// <summary>
        /// The order guard. Reading a path BEFORE the product is declared must not freeze it —
        /// that read happens for real, because the SDK constructs actions during plugin load.
        /// </summary>
        [Fact]
        public void A_path_read_before_the_product_is_declared_still_moves()
        {
            IpcPaths.UseProduct("claude-console");
            var early = IpcPaths.SessionsDir;

            IpcPaths.UseProduct("codex-console");
            var late = IpcPaths.SessionsDir;

            Assert.NotEqual(early, late);
            Assert.Contains("codex-console", late);
        }

        /// <summary>A blank slug is a bug in the caller, and must not silently produce /tmp itself.</summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void A_blank_product_is_ignored_rather_than_rooting_at_tmp(String slug)
        {
            IpcPaths.UseProduct("claude-console");

            IpcPaths.UseProduct(slug);

            Assert.Equal("claude-console", IpcPaths.ProductSlug);
            Assert.NotEqual(IpcPaths.TempDir, IpcPaths.Root);
        }

        /// <summary>Every path stays inside the product's own root — nothing escapes it.</summary>
        [Fact]
        public void Everything_lives_under_the_products_root()
        {
            IpcPaths.UseProduct("codex-console");
            var root = IpcPaths.Root + Path.DirectorySeparatorChar;

            foreach (var path in Snapshot())
            {
                Assert.StartsWith(root, path);
            }
        }

        private static String[] Snapshot() => new[]
        {
            IpcPaths.SessionsDir,
            IpcPaths.ActivityDir,
            IpcPaths.VoiceDir,
            IpcPaths.RegistryFile,
            IpcPaths.SharedStateFile,
            IpcPaths.SharedActivityFile,
            IpcPaths.VoiceStopFile,
            IpcPaths.VoiceTranscriptFile,
            IpcPaths.VoiceWavFile,
            IpcPaths.StateFor("ttys003"),
            IpcPaths.ActivityFor("ttys003"),
        };
    }
}
