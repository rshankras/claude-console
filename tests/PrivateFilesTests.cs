namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;

    using Xunit;

    /// <summary>
    /// The IPC root holds session state and voice transcripts — the user's prompts, cwd and
    /// dictation. These tests pin the two properties that keep it private: owner-only modes, and
    /// a refusal to follow a symlink another local user could have planted at the path.
    /// </summary>
    public class PrivateFilesTests : IDisposable
    {
        private readonly String _root =
            Path.Combine(Path.GetTempPath(), "cc-privatefiles-" + Guid.NewGuid().ToString("N"));

        public PrivateFilesTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        private String Path_(String name) => Path.Combine(_root, name);

        [Fact]
        public void EnsurePrivateDirectory_creates_the_directory_owner_only()
        {
            var dir = Path_("sessions");

            PrivateFiles.EnsurePrivateDirectory(dir);

            Assert.True(Directory.Exists(dir));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(dir));
        }

        [Fact]
        public void EnsurePrivateDirectory_tightens_an_existing_world_readable_directory()
        {
            // A root left behind by an older build (or a lax umask) must get locked down, not adopted.
            var dir = Path_("loose");
            Directory.CreateDirectory(dir);
            File.SetUnixFileMode(dir, (UnixFileMode)Convert.ToInt32("755", 8));

            PrivateFiles.EnsurePrivateDirectory(dir);

            Assert.Equal((UnixFileMode)Convert.ToInt32("700", 8), File.GetUnixFileMode(dir));
        }

        [Fact]
        public void EnsurePrivateDirectory_is_idempotent()
        {
            var dir = Path_("twice");

            PrivateFiles.EnsurePrivateDirectory(dir);
            PrivateFiles.EnsurePrivateDirectory(dir);

            Assert.Equal((UnixFileMode)Convert.ToInt32("700", 8), File.GetUnixFileMode(dir));
        }

        [Fact]
        public void EnsurePrivateDirectory_refuses_a_symlinked_directory()
        {
            var real = Path_("elsewhere");
            var link = Path_("linked-dir");
            Directory.CreateDirectory(real);
            Directory.CreateSymbolicLink(link, real);

            var ex = Assert.Throws<IOException>(() => PrivateFiles.EnsurePrivateDirectory(link));
            Assert.Contains("symlinked", ex.Message);
        }

        [Fact]
        public void EnsurePrivateFile_forces_owner_only_permissions()
        {
            var file = Path_("transcript.txt");
            File.WriteAllText(file, "spoken words");
            File.SetUnixFileMode(file, (UnixFileMode)Convert.ToInt32("644", 8));

            PrivateFiles.EnsurePrivateFile(file);

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }

        [Fact]
        public void EnsurePrivateFile_refuses_a_symlinked_file()
        {
            var real = Path_("real.txt");
            var link = Path_("linked.txt");
            File.WriteAllText(real, "x");
            File.CreateSymbolicLink(link, real);

            var ex = Assert.Throws<IOException>(() => PrivateFiles.EnsurePrivateFile(link));
            Assert.Contains("symlinked", ex.Message);
        }

        [Fact]
        public void EnsurePrivateFile_throws_when_the_file_is_missing()
        {
            Assert.Throws<FileNotFoundException>(() => PrivateFiles.EnsurePrivateFile(Path_("nope.json")));
        }
    }
}
