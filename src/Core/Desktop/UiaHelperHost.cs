namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>A persistent helper per lane. Once a request is written it is never replayed.
    /// All children, including compatibility/contended one-shots, belong to this lifetime.</summary>
    internal sealed class UiaHelperHost
    {
        internal const Int32 ColdStartAllowanceMs = 5000;
        private readonly String _serveVerb;
        private Int32 _startupMs = -1;
        private readonly String _lane;
        private readonly Func<String> _helper;
        private readonly Func<String, List<String>, Int32, String> _oneShot;
        private readonly Func<String, ProcessStartInfo> _startInfo;
        private readonly Object _gate = new(), _lifecycle = new();
        private readonly HashSet<Process> _children = new();
        private Process _process;
        private Boolean _unavailable, _enabled = true;
        private Int64 _generation;

        internal UiaHelperHost(String lane, Func<String> helper,
            Func<String, List<String>, Int32, String> oneShot = null,
            Func<String, ProcessStartInfo> startInfo = null, String serveVerb = "serve")
        { _lane = lane; _helper = helper; _oneShot = oneShot; _startInfo = startInfo; _serveVerb = serveVerb; }

        internal void WarmUp()
        {
            Int64 generation;
            lock (_lifecycle) { if (!_enabled) return; generation = _generation; }
            _ = Task.Run(() => { if (Current(generation)) Run(new List<String> { "ping" }, 1000); });
        }
        internal Int32 StartupMs => Volatile.Read(ref _startupMs);

        internal Int32? ServingPid
        { get { lock (_lifecycle) { try { return _process != null && !_process.HasExited ? _process.Id : null; } catch { return null; } } } }

        internal void Start() { lock (_lifecycle) _enabled = true; }

        internal void Shutdown()
        {
            Process[] children;
            lock (_lifecycle)
            {
                _enabled = false; _generation++; _process = null; _unavailable = false;
                children = new List<Process>(_children).ToArray();
            }
            // Kill before closing pipes: closing stdin can itself block behind a stuck write.
            // The request owner disposes active pipes after its pending I/O completes.
            foreach (var child in children) Kill(child);
            if (Monitor.TryEnter(_gate))
            {
                try { foreach (var child in children) Discard(child); }
                finally { Monitor.Exit(_gate); }
            }
        }

        internal String Run(List<String> args, Int32 timeoutMs)
        {
            Int64 generation;
            lock (_lifecycle) { if (!_enabled) return null; generation = _generation; }
            var file = _helper();
            if (file == null) return null;
            if (!Monitor.TryEnter(_gate)) return OneShot(file, args, timeoutMs, generation);
            try
            {
                if (!Current(generation)) return null;
                return _unavailable ? OneShot(file, args, timeoutMs, generation) : Serve(file, args, timeoutMs, generation);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException or AggregateException)
            {
                // A failed write/flush can have delivered all of a request. Only a later user
                // action may try again, even when the outcome of this request is unknown.
                PluginLog.Warning($"UiaHelperHost({_lane}): helper communication failed; request not repeated");
                return null;
            }
            finally { Monitor.Exit(_gate); }
        }

        private Boolean Current(Int64 generation)
        { lock (_lifecycle) return _enabled && generation == _generation; }

        private String Serve(String file, List<String> args, Int32 timeoutMs, Int64 generation)
        {
            var sw = Stopwatch.StartNew();
            var process = _process;
            var cold = process == null || HasExited(process);
            if (cold)
            {
                Discard(process);
                process = Launch(file, new[] { _serveVerb }, generation);
                if (process == null) return null;
                lock (_lifecycle)
                { if (_enabled && generation == _generation) _process = process; }
            }
            try
            {
                if (cold)
                {
                    var ready = process.StandardOutput.ReadLineAsync();
                    if (!Wait(ready, sw, ColdStartAllowanceMs)) return Overrun(process, file, ColdStartAllowanceMs);
                    Volatile.Write(ref _startupMs, (Int32)sw.ElapsedMilliseconds);
                    if (ready.Result != "{\"ready\":1}")
                    {
                        // No action was sent. Only an explicit old-helper response permits
                        // compatibility fallback; a crash or random output does not.
                        var legacy = IsLegacy(ready.Result);
                        Discard(process);
                        if (!legacy) return null;
                        lock (_lifecycle)
                        {
                            if (!_enabled || generation != _generation) return null;
                            _unavailable = true;
                        }
                        PluginLog.Warning($"UiaHelperHost({_lane}): old helper; using one process per call");
                        return OneShot(file, args, timeoutMs, generation);
                    }
                    PluginLog.Info($"UiaHelperHost({_lane}): ready in {StartupMs}ms; requests retain their own budgets");
                }
                if (!Current(generation)) return null;
                sw.Restart();
                var exchange = Exchange(process, args);
                if (!Wait(exchange, sw, timeoutMs)) return Overrun(process, file, timeoutMs);
                var response = ParseResponse(exchange.Result);
                if (response == null) { Discard(process); return null; }
                if (!Current(generation)) return null;
                if (BoundedProcess.SlowNote(file, sw.ElapsedMilliseconds, timeoutMs) is String slow)
                    PluginLog.Warning($"UiaHelperHost({_lane}, {(args.Count > 0 ? args[0] : "empty")}): {slow}");
                return response.Value.Output.Trim();
            }
            catch { Discard(process); throw; }
            finally { if (!Current(generation)) Discard(process); }
        }

        private static async Task<String> Exchange(Process process, List<String> args)
        {
            await process.StandardInput.WriteLineAsync(Request(args)).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
            return await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
        }

        private static Boolean Wait(Task task, Stopwatch elapsed, Int32 budget)
        {
            _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            return task.Wait(Math.Max(0, budget - (Int32)elapsed.ElapsedMilliseconds));
        }

        private String OneShot(String file, List<String> args, Int32 timeoutMs, Int64 generation)
        {
            if (!Current(generation)) return null;
            if (_oneShot != null) return _oneShot(file, args, timeoutMs);
            var sw = Stopwatch.StartNew();
            var process = Launch(file, args, generation);
            if (process == null) return null;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var exit = process.WaitForExitAsync();
                var budget = timeoutMs + (StartupMs >= 0 ? StartupMs + 250 : ColdStartAllowanceMs);
                if (!Wait(Task.WhenAll(output, exit), sw, budget))
                    return Overrun(process, file, budget);
                return Current(generation) ? output.Result.Trim() : null;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException or AggregateException)
            { return null; }
            finally { Discard(process); }
        }

        private Process Launch(String file, IEnumerable<String> args, Int64 generation)
        {
            lock (_lifecycle)
            {
                if (!_enabled || generation != _generation) return null;
                Process process = null;
                try
                {
                    var psi = _startInfo?.Invoke(file) ?? new ProcessStartInfo(file);
                    psi.UseShellExecute = false; psi.CreateNoWindow = true;
                    psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                    psi.StandardInputEncoding = new UTF8Encoding(false); psi.StandardOutputEncoding = new UTF8Encoding(false);
                    foreach (var arg in args) psi.ArgumentList.Add(arg);
                    process = Process.Start(psi);
                    if (process == null) return null;
                    _children.Add(process);
                    process.ErrorDataReceived += (_, _) => { };
                    process.BeginErrorReadLine();
                    return process;
                }
                catch (Exception ex)
                {
                    if (process != null) Discard(process);
                    PluginLog.Warning(ex, $"UiaHelperHost({_lane}): could not start helper");
                    return null;
                }
            }
        }

        private String Overrun(Process process, String file, Int32 budget)
        {
            PluginLog.Warning($"{file} exceeded {budget}ms — killing ({BoundedProcess.NoteOverrun(file)})");
            Discard(process);
            return null;
        }
        private void Discard(Process process)
        {
            if (process == null) return;
            lock (_lifecycle)
            {
                if (ReferenceEquals(_process, process)) _process = null;
                if (!_children.Remove(process)) return;
            }
            Kill(process);
            try { process.Dispose(); } catch { }
        }
        private static void Kill(Process process)
        { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } }
        private static Boolean HasExited(Process process)
        { try { return process.HasExited; } catch { return true; } }
        private Boolean IsLegacy(String line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line ?? "null");
                return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.String && error.GetString() == "unknown-verb " + _serveVerb;
            }
            catch (JsonException) { return false; }
        }

        internal static String Request(IEnumerable<String> args) => JsonSerializer.Serialize(args);
        internal static (Int32 Exit, String Output)? ParseResponse(String line)
        {
            if (String.IsNullOrWhiteSpace(line)) return null;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("exit", out var exit) || exit.ValueKind != JsonValueKind.Number || !exit.TryGetInt32(out var code)
                    || !root.TryGetProperty("out", out var output) || output.ValueKind != JsonValueKind.String) return null;
                return (code, output.GetString());
            }
            catch (JsonException) { return null; }
        }
    }
}
