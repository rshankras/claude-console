namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using VizhiDesktopUia;

    using Xunit;

    /// <summary>
    /// #155: the Windows helper is served from one long-lived process per lane. These tests drive
    /// the host against a .NET stand-in that speaks the same line protocol, so the lifecycle
    /// (reuse, overrun, death, busy fallback, an old helper) is pinned on any platform.
    /// </summary>
    public sealed class UiaHelperHostTests : IDisposable
    {
        private readonly String _dir = Path.Combine(Path.GetTempPath(), "uia-host-" + Guid.NewGuid().ToString("N"));
        private readonly List<UiaHelperHost> _hosts = new();

        public UiaHelperHostTests() => Directory.CreateDirectory(_dir);
        public void Dispose()
        {
            foreach (var host in _hosts) host.Shutdown();
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }
        private UiaHelperHost Host(String mode = "normal", Func<String, List<String>, Int32, String> oneShot = null)
        {
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (String.IsNullOrEmpty(dotnet)) dotnet = Path.GetFullPath(Path.Combine(
                System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
            var host = new UiaHelperHost("test", () => dotnet, oneShot, file =>
            {
                var info = new ProcessStartInfo(file);
                info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "uia-fixture", "UiaHostFixture.dll"));
                info.ArgumentList.Add(mode);
                return info;
            });
            _hosts.Add(host);
            return host;
        }
        private static List<String> Args(params String[] args) => new(args);

        [Fact]
        public void One_process_serves_every_call_and_returns_the_verb_output()
        {
            var host = this.Host("normal");
            var first = host.Run(Args("pid"), 2000);
            Assert.Equal(first, host.Run(Args("pid"), 2000));
            Assert.Equal(first, host.ServingPid?.ToString());
            var echoed = JsonSerializer.Deserialize<String[]>(host.Run(Args("status", "--text", "தமிழ் \"quoted\"\nline"), 2000));
            Assert.Equal(new[] { "status", "--text", "தமிழ் \"quoted\"\nline" }, echoed);
        }

        [Fact]
        public void An_overrun_is_killed_returns_nothing_and_the_next_call_starts_afresh()
        {
            var host = this.Host("normal");
            var before = host.Run(Args("pid"), 2000);
            Assert.Null(host.Run(Args("sleep", "1500"), 300));
            Assert.Null(host.ServingPid);
            var after = host.Run(Args("pid"), 2000);
            Assert.NotNull(after);
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void A_helper_that_died_between_calls_is_replaced_without_losing_the_call()
        {
            var host = this.Host("normal");
            var before = host.Run(Args("pid"), 2000);
            // Dies while answering: the call that killed it reports nothing (never repeated)...
            Assert.Null(host.Run(Args("die"), 2000));
            // ...and the next call is served by a new process.
            var after = host.Run(Args("pid"), 2000);
            Assert.NotNull(after);
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void A_busy_lane_runs_the_call_as_a_one_shot_instead_of_waiting()
        {
            var oneShots = 0;
            var host = this.Host("normal", (_, _, _) => { Interlocked.Increment(ref oneShots); return "one-shot"; });
            host.Run(Args("pid"), 2000);
            var slow = Task.Run(() => host.Run(Args("sleep", "800"), 3000));
            Thread.Sleep(200);
            var sw = Stopwatch.StartNew();
            Assert.Equal("one-shot", host.Run(Args("pid"), 2000));
            Assert.True(sw.ElapsedMilliseconds < 500, $"waited {sw.ElapsedMilliseconds}ms behind the busy lane");
            Assert.NotNull(slow.Result);
            Assert.Equal(1, oneShots);
        }

        [Fact]
        public void A_helper_without_serve_falls_back_to_one_process_per_call_for_the_load()
        {
            var oneShots = new List<List<String>>();
            var host = this.Host("old", (_, args, _) => { oneShots.Add(args); return "one-shot"; });
            Assert.Equal("one-shot", host.Run(Args("status"), 2000));
            Assert.Equal("one-shot", host.Run(Args("press"), 2000));
            Assert.Equal(2, oneShots.Count);
            Assert.Null(host.ServingPid);
        }

        [Fact]
        public void Shutdown_ends_the_serving_process()
        {
            var host = this.Host("normal");
            var pid = Int32.Parse(host.Run(Args("pid"), 2000));
            host.Shutdown();
            Assert.Null(host.ServingPid);
            var deadline = Stopwatch.StartNew();
            while (deadline.ElapsedMilliseconds < 2000 && Alive(pid)) Thread.Sleep(50);
            Assert.False(Alive(pid), "the helper outlived Shutdown");
        }

        [Fact]
        public void No_packaged_helper_means_no_call()
        {
            var host = new UiaHelperHost("test", () => null, (_, _, _) => throw new Exception("ran without a helper"));
            Assert.Null(host.Run(Args("status"), 100));
        }

        [Fact]
        public void The_plugin_and_the_helper_agree_on_the_wire()
        {
            var args = new[] { "write", "--text", "தமிழ் \"quoted\" \\ back\nslash", "--expect-target", "" };
            Assert.Equal(args, ServeProtocol.ParseRequest(UiaHelperHost.Request(args)));
            Assert.Equal(args, ServeProtocol.ParseRequest(ServeProtocol.Request(args)));
            var output = "{\"ok\":true,\"title\":\"தமிழ்\"}\n";
            Assert.Equal((5, output), UiaHelperHost.ParseResponse(ServeProtocol.Response(5, output)));
            Assert.Null(UiaHelperHost.ParseResponse("{\"ok\":false,\"error\":\"unknown-verb serve\"}"));
            Assert.Null(ServeProtocol.ParseRequest("{\"verb\":\"status\"}"));
            Assert.Null(ServeProtocol.ParseRequest("[]"));
            Assert.Null(ServeProtocol.ParseRequest("[\"status\",1]"));
            Assert.Null(UiaHelperHost.ParseResponse("{\"exit\":9999999999999999,\"out\":\"\"}"));
            Assert.Null(ServeProtocol.ParseResponse("{\"exit\":9999999999999999,\"out\":\"\"}"));
        }

        [Fact]
        public void Polls_and_keys_use_separate_lanes()
        {
            Assert.True(WindowsDesktopAutomation.IsPollVerb(Args("frontmost")));
            Assert.True(WindowsDesktopAutomation.IsPollVerb(Args("status", "--process", "ChatGPT.exe")));
            Assert.False(WindowsDesktopAutomation.IsPollVerb(Args("press")));
            Assert.False(WindowsDesktopAutomation.IsPollVerb(Args("focus")));
            Assert.False(WindowsDesktopAutomation.IsPollVerb(Args()));
            Assert.True(WindowsDesktopAutomation.UsePollLane(Args("status")));
            var runner = new DesktopActionRunner { Schedule = action => action() };
            var statusUsesPoll = true; var frontmostUsesPoll = true;
            Assert.True(runner.TryRun(() =>
            {
                statusUsesPoll = WindowsDesktopAutomation.UsePollLane(Args("status"));
                frontmostUsesPoll = WindowsDesktopAutomation.UsePollLane(Args("frontmost"));
            }));
            Assert.False(statusUsesPoll); Assert.False(frontmostUsesPoll);
            Assert.True(WindowsDesktopAutomation.UsePollLane(Args("status")));
        }

        private static Boolean Alive(Int32 pid)
        {
            try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
            catch (ArgumentException) { return false; }
        }

        [Fact]
        public void A_corrupt_response_after_an_action_never_replays_that_action()
        {
            var fallback = 0;
            var host = Host("normal", (_, _, _) => { fallback++; return "replayed"; });
            var evidence = Path.Combine(_dir, "acted");
            Assert.Null(host.Run(Args("corrupt", evidence), 1000));
            Assert.Single(File.ReadAllLines(evidence)); Assert.Equal(0, fallback);
            Assert.NotNull(host.Run(Args("pid"), 1000));
        }

        [Fact]
        public void Startup_has_an_allowance_but_warm_requests_keep_their_own_budget()
        {
            var host = Host("cold");
            Assert.NotNull(host.Run(Args("pid"), 200));
            Assert.Null(host.Run(Args("sleep", "1000"), 200));
        }

        [Fact]
        public async Task Shutdown_cancels_a_request_and_cannot_restart_until_explicitly_started()
        {
            var host = Host();
            var pid = Int32.Parse(host.Run(Args("pid"), 2000));
            var entered = Path.Combine(_dir, "entered");
            var running = Task.Run(() => host.Run(Args("sleep", "30000", entered), 35000));
            await WaitForFile(entered);
            host.Shutdown();
            Assert.Null(await running.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Null(host.Run(Args("pid"), 1000));
            Assert.True(SpinWait.SpinUntil(() => !Alive(pid), 3000));
            host.Start(); Assert.NotNull(host.Run(Args("pid"), 2000));
        }

        [Fact]
        public async Task Shutdown_also_terminates_a_contended_one_shot()
        {
            var host = Host(); host.Run(Args("pid"), 2000);
            var entered = Path.Combine(_dir, "first"); var second = Path.Combine(_dir, "second");
            var first = Task.Run(() => host.Run(Args("sleep", "30000", entered), 35000));
            await WaitForFile(entered);
            var fallback = Task.Run(() => host.Run(Args("sleep", "30000", second), 35000));
            await WaitForFile(second);
            var pid = Int32.Parse(File.ReadAllText(second));
            host.Shutdown();
            Assert.Null(await first.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Null(await fallback.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.True(SpinWait.SpinUntil(() => !Alive(pid), 3000));
        }

        [Fact]
        public void A_helper_that_stops_reading_cannot_block_a_large_write_forever()
        {
            var host = Host("no-read"); var watch = Stopwatch.StartNew();
            Assert.Null(host.Run(Args("write", new String('x', 2_000_000)), 100));
            Assert.True(watch.ElapsedMilliseconds < 10000);
            Assert.Null(host.ServingPid);
        }

        private static async Task WaitForFile(String path)
        {
            var deadline = Stopwatch.StartNew();
            while ((!File.Exists(path) || new FileInfo(path).Length == 0) && deadline.ElapsedMilliseconds < 5000) await Task.Delay(20);
            Assert.True(File.Exists(path));
        }
    }

}
