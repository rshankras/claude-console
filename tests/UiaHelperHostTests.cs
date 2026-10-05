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
    /// the host against a Python stand-in that speaks the same line protocol, so the lifecycle
    /// (reuse, overrun, death, busy fallback, an old helper) is pinned on any platform.
    /// </summary>
    public sealed class UiaHelperHostTests : IDisposable
    {
        private readonly String _dir = Path.Combine(Path.GetTempPath(), "uia-host-" + Guid.NewGuid().ToString("N"));
        private readonly List<UiaHelperHost> _hosts = new();

        // Answers ["pid"] with its pid, ["sleep", ms] after a pause, ["die"] by exiting,
        // anything else by echoing the argv back as the "out" JSON.
        private const String ServingHelper = """
            #!/usr/bin/env python3
            import json, os, sys, time
            if sys.argv[1:] != ["serve"]:
                print(json.dumps({"ok": True, "oneshot": sys.argv[1:]})); sys.exit(0)
            for line in sys.stdin:
                args = json.loads(line)
                if args[0] == "die": sys.exit(0)
                if args[0] == "sleep": time.sleep(int(args[1]) / 1000)
                out = str(os.getpid()) if args[0] == "pid" else json.dumps(args)
                sys.stdout.write(json.dumps({"exit": 0, "out": out}) + "\n"); sys.stdout.flush()
            """;

        // What a helper from before #155 does with `serve`: an unknown verb, then exit.
        private const String OldHelper = """
            #!/usr/bin/env python3
            import json, sys
            print(json.dumps({"ok": False, "error": "unknown-verb " + sys.argv[1]}))
            """;

        public UiaHelperHostTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            foreach (var host in _hosts) host.Shutdown();
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private String Script(String name, String body)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, body.Replace("\r\n", "\n"));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }

        private UiaHelperHost Host(String script, Func<String, List<String>, Int32, String> oneShot = null)
        {
            var host = new UiaHelperHost("test", () => script, oneShot);
            _hosts.Add(host);
            return host;
        }

        private static List<String> Args(params String[] args) => new(args);

        [ScriptHelperFact]
        public void One_process_serves_every_call_and_returns_the_verb_output()
        {
            var host = this.Host(this.Script("helper", ServingHelper));
            var first = host.Run(Args("pid"), 2000);
            Assert.Equal(first, host.Run(Args("pid"), 2000));
            Assert.Equal(first, host.ServingPid?.ToString());
            var echoed = JsonSerializer.Deserialize<String[]>(host.Run(Args("status", "--text", "தமிழ் \"quoted\"\nline"), 2000));
            Assert.Equal(new[] { "status", "--text", "தமிழ் \"quoted\"\nline" }, echoed);
        }

        [ScriptHelperFact]
        public void An_overrun_is_killed_returns_nothing_and_the_next_call_starts_afresh()
        {
            var host = this.Host(this.Script("helper", ServingHelper));
            var before = host.Run(Args("pid"), 2000);
            Assert.Null(host.Run(Args("sleep", "1500"), 300));
            Assert.Null(host.ServingPid);
            var after = host.Run(Args("pid"), 2000);
            Assert.NotNull(after);
            Assert.NotEqual(before, after);
        }

        [ScriptHelperFact]
        public void A_helper_that_died_between_calls_is_replaced_without_losing_the_call()
        {
            var host = this.Host(this.Script("helper", ServingHelper));
            var before = host.Run(Args("pid"), 2000);
            // Dies while answering: the call that killed it reports nothing (never repeated)...
            Assert.Null(host.Run(Args("die"), 2000));
            // ...and the next call is served by a new process.
            var after = host.Run(Args("pid"), 2000);
            Assert.NotNull(after);
            Assert.NotEqual(before, after);
        }

        [ScriptHelperFact]
        public void A_busy_lane_runs_the_call_as_a_one_shot_instead_of_waiting()
        {
            var oneShots = 0;
            var host = this.Host(this.Script("helper", ServingHelper), (_, _, _) => { Interlocked.Increment(ref oneShots); return "one-shot"; });
            host.Run(Args("pid"), 2000);
            var slow = Task.Run(() => host.Run(Args("sleep", "800"), 3000));
            Thread.Sleep(200);
            var sw = Stopwatch.StartNew();
            Assert.Equal("one-shot", host.Run(Args("pid"), 2000));
            Assert.True(sw.ElapsedMilliseconds < 500, $"waited {sw.ElapsedMilliseconds}ms behind the busy lane");
            Assert.NotNull(slow.Result);
            Assert.Equal(1, oneShots);
        }

        [ScriptHelperFact]
        public void A_helper_without_serve_falls_back_to_one_process_per_call_for_the_load()
        {
            var oneShots = new List<List<String>>();
            var host = this.Host(this.Script("old-helper", OldHelper), (_, args, _) => { oneShots.Add(args); return "one-shot"; });
            Assert.Equal("one-shot", host.Run(Args("status"), 2000));
            Assert.Equal("one-shot", host.Run(Args("press"), 2000));
            Assert.Equal(2, oneShots.Count);
            Assert.Null(host.ServingPid);
        }

        [ScriptHelperFact]
        public void Shutdown_ends_the_serving_process()
        {
            var host = this.Host(this.Script("helper", ServingHelper));
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
        }

        [Fact]
        public void Polls_and_keys_use_separate_lanes()
        {
            Assert.True(WindowsDesktopAutomation.IsPollVerb(Args("frontmost")));
            Assert.True(WindowsDesktopAutomation.IsPollVerb(Args("status", "--process", "ChatGPT.exe")));
            Assert.False(WindowsDesktopAutomation.IsPollVerb(Args("press")));
            Assert.False(WindowsDesktopAutomation.IsPollVerb(Args("focus")));
            Assert.False(WindowsDesktopAutomation.IsPollVerb(Args()));
        }

        private static Boolean Alive(Int32 pid)
        {
            try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
            catch (ArgumentException) { return false; }
        }
    }

    /// <summary>
    /// A fact that runs a Python stand-in for the helper as an executable script, so it needs a
    /// Unix shebang. On Windows it is REPORTED as skipped; the real helper is covered there by
    /// the device pass (#155).
    /// </summary>
    public sealed class ScriptHelperFactAttribute : FactAttribute
    {
        public ScriptHelperFactAttribute()
        {
            if (OperatingSystem.IsWindows()) this.Skip = "the helper stand-in is a Python script run through its shebang";
        }
    }
}
