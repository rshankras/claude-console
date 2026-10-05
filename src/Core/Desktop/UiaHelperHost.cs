namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// One long-lived <c>vizhi-desktop-uia serve</c> process (#155). Starting the helper per call
    /// cost ~0.6 s on a managed Windows endpoint and a key press made two or three calls, so
    /// presses took 1–6 s where the Mac takes 0.3–0.6 s. The host keeps the process and sends each
    /// call as one line (the helper's ServeProtocol), under the same budget, slow-call warning and
    /// kill-on-overrun as <see cref="BoundedProcess"/>.
    ///
    /// One request at a time per host. A caller that finds the host busy runs the old one-shot
    /// process instead of waiting, so serving never makes a call slower than it was. A request is
    /// never repeated once written: a press that may have happened must not happen twice.
    /// </summary>
    internal sealed class UiaHelperHost
    {
        // The first request on a new process also pays the process start.
        internal const Int32 ColdStartAllowanceMs = 5000;

        private readonly String _lane;
        private readonly Func<String> _helper;
        private readonly Func<String, List<String>, Int32, String> _oneShot;
        private readonly Object _gate = new();
        private Process _process;
        private Boolean _unavailable;

        internal UiaHelperHost(String lane, Func<String> helper, Func<String, List<String>, Int32, String> oneShot = null)
        {
            _lane = lane;
            _helper = helper;
            _oneShot = oneShot ?? ((file, args, timeoutMs) => BoundedProcess.Run(file, args, timeoutMs, wantOutput: true));
        }

        /// <summary>Process id of the serving helper, or null. Tests and the log only.</summary>
        internal Int32? ServingPid { get { var p = _process; try { return p != null && !p.HasExited ? p.Id : null; } catch { return null; } } }

        internal String Run(List<String> args, Int32 timeoutMs)
        {
            var file = _helper();
            if (file == null) return null;
            if (Volatile.Read(ref _unavailable) || !Monitor.TryEnter(_gate)) return _oneShot(file, args, timeoutMs);
            try
            {
                return this.Serve(file, args, timeoutMs);
            }
            finally
            {
                Monitor.Exit(_gate);
            }
        }

        /// <summary>Ends the serving process. The next call starts a new one.</summary>
        internal void Shutdown()
        {
            var process = Interlocked.Exchange(ref _process, null);
            if (process != null) Kill(process);
        }

        private String Serve(String file, List<String> args, Int32 timeoutMs)
        {
            var cold = false;
            if (_process == null || HasExited(_process))
            {
                if (_process != null) { PluginLog.Warning($"UiaHelperHost({_lane}): the helper had exited — starting a new one"); Discard(); }
                if (!this.Start(file)) return _oneShot(file, args, timeoutMs);
                cold = true;
            }

            var process = _process;
            var sw = Stopwatch.StartNew();
            try
            {
                process.StandardInput.WriteLine(Request(args));
                process.StandardInput.Flush();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
            {
                // Nothing reached the helper, so starting again cannot repeat an action.
                this.Discard();
                if (cold || !this.Start(file)) return _oneShot(file, args, timeoutMs);
                return this.Serve(file, args, timeoutMs);
            }

            var read = process.StandardOutput.ReadLineAsync();
            if (!read.Wait(timeoutMs + (cold ? ColdStartAllowanceMs : 0)))
            {
                PluginLog.Warning($"{file} exceeded {timeoutMs}ms — killing ({BoundedProcess.NoteOverrun(file)})");
                this.Discard();
                return null;
            }

            String line;
            try { line = read.Result; }
            catch (AggregateException) { line = null; }
            if (line == null)
            {
                PluginLog.Warning($"UiaHelperHost({_lane}): the helper exited during a call");
                this.Discard();
                return null;
            }

            var response = ParseResponse(line);
            if (response == null)
            {
                // A helper without `serve` answers the verb as unknown and exits without reading
                // the request. Use one-shot processes for the rest of this load.
                PluginLog.Warning($"UiaHelperHost({_lane}): the helper does not serve — using one process per call");
                Volatile.Write(ref _unavailable, true);
                this.Discard();
                return _oneShot(file, args, timeoutMs);
            }

            // The first call includes the process start; that cost is the one-off this host exists
            // to pay, so it is recorded once and not mistaken for a slow call.
            if (cold) PluginLog.Info($"UiaHelperHost({_lane}): first call took {sw.ElapsedMilliseconds}ms including the start");
            else if (BoundedProcess.SlowNote(file, sw.ElapsedMilliseconds, timeoutMs) is String slow) PluginLog.Warning(slow);
            return response.Value.Output.Trim();
        }

        private Boolean Start(String file)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardInputEncoding = new UTF8Encoding(false),
                };
                psi.ArgumentList.Add("serve");
                var process = Process.Start(psi);
                if (process == null) return false;
                process.ErrorDataReceived += (_, _) => { };
                process.BeginErrorReadLine();
                _process = process;
                PluginLog.Info($"UiaHelperHost({_lane}): serving from pid {process.Id}");
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"UiaHelperHost({_lane}): could not start the helper — using one process per call");
                Volatile.Write(ref _unavailable, true);
                return false;
            }
        }

        private void Discard()
        {
            var process = _process;
            _process = null;
            if (process != null) Kill(process);
        }

        private static void Kill(Process process)
        {
            try { process.StandardInput.Close(); } catch { }
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { process.Dispose(); } catch { }
        }

        private static Boolean HasExited(Process process)
        {
            try { return process.HasExited; } catch { return true; }
        }

        // ---- wire: the helper's ServeProtocol, mirrored (UiaHelperHostTests pins both sides) ----

        internal static String Request(IEnumerable<String> args) => JsonSerializer.Serialize(args);

        internal static (Int32 Exit, String Output)? ParseResponse(String line)
        {
            if (String.IsNullOrWhiteSpace(line)) return null;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("exit", out var exit) || exit.ValueKind != JsonValueKind.Number
                    || !root.TryGetProperty("out", out var output) || output.ValueKind != JsonValueKind.String)
                    return null;
                return (exit.GetInt32(), output.GetString());
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
            {
                return null;
            }
        }
    }
}
