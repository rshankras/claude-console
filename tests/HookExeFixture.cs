namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// Publishes claude-console-hook.exe ONCE per test run, exactly the way the package does
    /// (tools/windows/build-windows-payload.sh: single file, self-contained, trimmed). The artifact
    /// under test has to be the published one: trimming is the one thing in that exe that can
    /// break its hand-built JSON, and a Debug build would never show it.
    ///
    /// Set CC_HOOK_EXE to an already-published exe to skip the publish. Do not point it at a stale
    /// one — the contract tests would then pass against code that is not in the tree.
    /// </summary>
    public sealed class HookExeFixture : IDisposable
    {
        /// <summary>The published exe, or null on a platform that cannot run it.</summary>
        public String ExePath { get; }

        public HookExeFixture()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;   // every fact on this fixture is reported skipped; build nothing
            }

            var preset = Environment.GetEnvironmentVariable("CC_HOOK_EXE");
            if (!String.IsNullOrEmpty(preset) && File.Exists(preset))
            {
                this.ExePath = Path.GetFullPath(preset);
                return;
            }

            var repo = RepoRoot();
            var project = Path.Combine(repo, "tools", "windows", "ClaudeConsoleHook", "ClaudeConsoleHook.csproj");
            var outDir = Path.Combine(repo, "tests", "bin", "hook-under-test", "win-x64");

            // The SDK that is running this suite, when it says which one it is; otherwise whatever
            // `dotnet` is on PATH. Either can publish a net8.0 project.
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (String.IsNullOrEmpty(dotnet) || !File.Exists(dotnet))
            {
                dotnet = "dotnet";
            }

            var psi = new ProcessStartInfo(dotnet)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = repo,
            };
            foreach (var arg in new[]
            {
                "publish", project,
                "-c", "Release", "-r", "win-x64",
                "-p:PublishSingleFile=true",
                "-p:EnableWindowsTargeting=true",
                "-o", outDir,
                "--nologo", "-nodeReuse:false",
            })
            {
                psi.ArgumentList.Add(arg);
            }

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start " + dotnet);
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(600_000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* gone */ }
                throw new InvalidOperationException("publishing the hook exe took longer than ten minutes");
            }

            var exe = Path.Combine(outDir, "claude-console-hook.exe");
            if (p.ExitCode != 0 || !File.Exists(exe))
            {
                throw new InvalidOperationException(
                    $"could not publish the hook exe (dotnet exited {p.ExitCode}):\n{stdout.Result}\n{stderr.Result}");
            }

            this.ExePath = exe;
        }

        public void Dispose()
        {
            // The publish output stays in tests/bin so the next run is incremental.
        }

        internal static String RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "src", "Products")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }
            throw new InvalidOperationException("repo root not found above " + AppContext.BaseDirectory);
        }
    }
}
