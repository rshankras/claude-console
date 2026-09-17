namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Xunit;

    public sealed class DesktopRuntimeTests : IDisposable
    {
        private readonly String _root = Path.Combine(Path.GetTempPath(), "desktop-runtime-" + Guid.NewGuid().ToString("N"));
        public DesktopRuntimeTests() => Directory.CreateDirectory(_root);
        public void Dispose() => Directory.Delete(_root, true);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Install_and_upgrade_replace_with_verified_package_then_become_noops(Boolean existing)
        {
            var package = Path.Combine(_root, "package");
            var target = Path.Combine(_root, "runtime", "helper");
            File.WriteAllText(package, "new helper");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (existing) File.WriteAllText(target, "old helper");
            var calls = 0;
            Int32? Run(String executable, List<String> args, Int32 timeout)
            {
                calls++;
                if (executable.EndsWith("ditto")) File.Copy(args[0], args[1]);
                if (existing) Assert.Equal("old helper", File.ReadAllText(target));
                else Assert.False(File.Exists(target));
                return 0;
            }
            Assert.True(DesktopRuntime.Refresh(package, target, Run));
            Assert.Equal("new helper", File.ReadAllText(target));
            Assert.Equal(3, calls);
            Assert.False(DesktopRuntime.Refresh(package, target, (_, _, _) => throw new Exception("unexpected process")));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target), "*.staging-*"));
        }

        [Theory]
        [InlineData("copy")]
        [InlineData("corrupt")]
        [InlineData("signature")]
        public void Failed_refresh_keeps_old_helper_and_cleans_staging(String failure)
        {
            var package = Path.Combine(_root, "package");
            var target = Path.Combine(_root, "helper");
            File.WriteAllText(package, "new helper");
            File.WriteAllText(target, "old helper");
            Int32? Run(String executable, List<String> args, Int32 timeout)
            {
                if (executable.EndsWith("ditto"))
                {
                    File.WriteAllText(args[1], failure == "corrupt" ? "bad" : "new helper");
                    return failure == "copy" ? null : 0;
                }
                return failure == "signature" ? 1 : 0;
            }
            Assert.Throws<IOException>(() => DesktopRuntime.Refresh(package, target, Run));
            Assert.Equal("old helper", File.ReadAllText(target));
            Assert.Empty(Directory.GetFiles(_root, "*.staging-*"));
        }

        [Fact]
        public void Mac_copy_preserves_a_real_signed_executable()
        {
            if (!OperatingSystem.IsMacOS()) return;
            var target = Path.Combine(_root, "helper");
            var packaged = Path.Combine(_root, "signed-helper");
            // Keep the embedded signature, without copying the system file's SIP flags.
            File.WriteAllBytes(packaged, File.ReadAllBytes("/usr/bin/true"));
            File.SetUnixFileMode(packaged, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(target, "old helper");
            Assert.True(DesktopRuntime.Refresh(packaged, target,
                Platform.BoundedProcess.RunForExitCode));
            Assert.Equal(File.ReadAllBytes("/usr/bin/true"), File.ReadAllBytes(target));
            Assert.True((File.GetUnixFileMode(target) & UnixFileMode.UserExecute) != 0);
            Assert.False(DesktopRuntime.Refresh(packaged, target,
                (_, _, _) => throw new Exception("unchanged binary was recopied")));
        }

        [Fact]
        public void Missing_package_leaves_dev_runtime_alone()
        {
            var target = Path.Combine(_root, "helper");
            File.WriteAllText(target, "dev helper");
            Assert.False(DesktopRuntime.Refresh(Path.Combine(_root, "missing"), target,
                (_, _, _) => throw new Exception("unexpected process")));
            Assert.Equal("dev helper", File.ReadAllText(target));
        }
    }
}
