namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Diagnostics;
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

        // Unix file modes don't exist on Windows, BY DESIGN in the code under test too:
        // PrivateFiles skips chmod there because %LOCALAPPDATA%\Temp is already per-user via
        // ACLs. The symlink refusal has no such platform split — it runs everywhere — but Windows
        // lets only administrators and Developer Mode CREATE a symlink, so the two link tests
        // below plant what the account can and say so when it can plant nothing.
        private static Boolean HasUnixModes => !OperatingSystem.IsWindows();

        // ERROR_PRIVILEGE_NOT_HELD (1314) as an HRESULT — SeCreateSymbolicLinkPrivilege missing.
        private const Int32 PrivilegeNotHeld = unchecked((Int32)0x80070522);

        private static Boolean TryCreateSymlink(String link, String target, Boolean directory)
        {
            try
            {
                if (directory) { Directory.CreateSymbolicLink(link, target); }
                else { File.CreateSymbolicLink(link, target); }
                return true;
            }
            catch (IOException ex) when (OperatingSystem.IsWindows() && ex.HResult == PrivilegeNotHeld)
            {
                return false;
            }
        }

        // A directory junction is the redirect a plain Windows account CAN plant, no privilege
        // needed; .NET reports it through LinkTarget like a symlink, so the refusal catches it too.
        private static Boolean TryCreateJunction(String link, String target)
        {
            if (!OperatingSystem.IsWindows()) { return false; }
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{target}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 && Directory.Exists(link);
        }

        [Fact]
        public void EnsurePrivateDirectory_creates_the_directory_owner_only()
        {
            if (!HasUnixModes) { return; }
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
            if (!HasUnixModes) { return; }
            var dir = Path_("loose");
            Directory.CreateDirectory(dir);
            File.SetUnixFileMode(dir, (UnixFileMode)Convert.ToInt32("755", 8));

            PrivateFiles.EnsurePrivateDirectory(dir);

            Assert.Equal((UnixFileMode)Convert.ToInt32("700", 8), File.GetUnixFileMode(dir));
        }

        [Fact]
        public void EnsurePrivateDirectory_is_idempotent()
        {
            if (!HasUnixModes) { return; }
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
            if (!TryCreateSymlink(link, real, directory: true) && !TryCreateJunction(link, real))
            {
                return;   // this account can plant no directory link at all — nothing to refuse
            }

            var ex = Assert.Throws<IOException>(() => PrivateFiles.EnsurePrivateDirectory(link));
            Assert.Contains("symlinked", ex.Message);
        }

        [Fact]
        public void EnsurePrivateFile_forces_owner_only_permissions()
        {
            if (!HasUnixModes) { return; }
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
            if (!TryCreateSymlink(link, real, directory: false))
            {
                return;   // a plain Windows account cannot plant a file symlink — nothing to refuse
            }

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
