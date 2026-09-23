namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.IO;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;

    internal enum SpeechModelPhase { NotStarted, Checking, Downloading, Verifying, Ready, Failed, Cancelled }
    internal sealed record SpeechModelStatus(SpeechModelPhase Phase, Int32 Percent = 0)
    {
        public String Footer => this.Phase switch
        {
            SpeechModelPhase.Checking => "Checking",
            SpeechModelPhase.Downloading => $"Download {this.Percent}%",
            SpeechModelPhase.Verifying => "Verifying",
            SpeechModelPhase.Ready => "SEARCH",
            SpeechModelPhase.Failed => "Tap to retry",
            _ => "Preparing"
        };
    }

    /// <summary>
    /// One default for Speak Query. Weights stay outside the plugin; the existing signed recorder
    /// already accepts a model path. Other voice intents keep their existing model and helper.
    /// </summary>
    internal sealed class DesktopSearchVoiceModel : IDisposable
    {
        internal const String FileName = "ggml-large-v3-turbo-q5_0.bin";
        internal const Int64 Size = 574041195;
        internal const String Sha256 = "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2";
        internal const String Url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-large-v3-turbo-q5_0.bin";
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        private readonly Object _gate = new Object();
        private readonly HttpClient _http;
        private readonly String _url, _hash;
        private readonly Int64 _size;
        private CancellationTokenSource _cancel = new CancellationTokenSource();
        private SpeechModelStatus _status = new SpeechModelStatus(SpeechModelPhase.NotStarted);
        private Task _pending = Task.CompletedTask;
        private Boolean _disposed;
        private DateTime _verifiedWrite;
        public String ModelPath { get; }
        public event Action Changed;
        public SpeechModelStatus Status { get { lock (_gate) return _status; } }
        internal Task Pending { get { lock (_gate) return _pending; } }

        public DesktopSearchVoiceModel(String directory)
            : this(Path.Combine(directory, FileName), Http, Url, Size, Sha256) { }

        // Real streams/files in tests, a tiny model and an injected HTTP handler; never live paths.
        internal DesktopSearchVoiceModel(String path, HttpClient http, String url, Int64 size, String hash)
        { this.ModelPath = path; _http = http; _url = url; _size = size; _hash = hash; }

        internal static Boolean AppliesTo(VoiceIntent intent, Boolean isMacOS) =>
            isMacOS && intent == VoiceIntent.DesktopSearch;

        /// <summary>Nonblocking. A press during preparation never opens the microphone.</summary>
        public Boolean EnsureReady()
        {
            lock (_gate)
            {
                if (_disposed) return false;
                if (_status.Phase == SpeechModelPhase.Ready && this.FileUnchanged()) return true;
                if (!_pending.IsCompleted) return false;
                if (_cancel.IsCancellationRequested) { _cancel.Dispose(); _cancel = new(); }
                _status = new SpeechModelStatus(SpeechModelPhase.Checking);
                var token = _cancel.Token;
                _pending = Task.Run(() => this.Prepare(token));
                return false;
            }
        }

        private Boolean FileUnchanged()
        {
            try { var f = new FileInfo(this.ModelPath); return f.Exists && f.Length == _size && f.LastWriteTimeUtc == _verifiedWrite; }
            catch { return false; }
        }

        private void Publish(SpeechModelPhase phase, Int32 percent = 0)
        {
            lock (_gate)
            {
                if (_disposed) return;
                var next = new SpeechModelStatus(phase, percent);
                if (_status == next) return;
                _status = next;
            }
            // Rendering/status subscribers must not turn a successful transfer into a failure.
            foreach (Action callback in this.Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                try { callback(); } catch (Exception ex) { PluginLog.Warning(ex, "Speak Query model: status callback failed"); }
            }
        }

        private async Task<Boolean> Valid(String path, CancellationToken token)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != _size) return false;
            using var stream = File.OpenRead(path);
            var actual = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
            return Convert.ToHexString(actual).Equals(_hash, StringComparison.OrdinalIgnoreCase);
        }

        private async Task Prepare(CancellationToken token)
        {
            // Unique partial files let separate service instances finish without deleting each
            // other's transfer. Promotion is atomic and only follows size + SHA-256 verification.
            var part = this.ModelPath + "." + Guid.NewGuid().ToString("N") + ".part";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            var lifetime = token;
            token = timeout.Token;
            try
            {
                if (!await this.Valid(this.ModelPath, token).ConfigureAwait(false))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(this.ModelPath));
                    this.Publish(SpeechModelPhase.Downloading);
                    using var response = await _http.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is Int64 length && length != _size)
                        throw new InvalidDataException("Speech model size mismatch");
                    using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                    using (var output = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                    {
                        var buffer = new Byte[131072];
                        Int64 received = 0;
                        Int32 count;
                        while ((count = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
                        {
                            received += count;
                            if (received > _size) throw new InvalidDataException("Speech model exceeds expected size");
                            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                            this.Publish(SpeechModelPhase.Downloading, (Int32)(received * 100 / _size));
                        }
                    }
                    this.Publish(SpeechModelPhase.Verifying);
                    if (!await this.Valid(part, token).ConfigureAwait(false)) throw new InvalidDataException("Speech model checksum mismatch");
                    token.ThrowIfCancellationRequested();
                    File.Move(part, this.ModelPath, overwrite: true);
                }
                _verifiedWrite = File.GetLastWriteTimeUtc(this.ModelPath);
                this.Publish(SpeechModelPhase.Ready);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { this.Publish(SpeechModelPhase.Cancelled); }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Speak Query model: preparation failed; tap Speak Query to retry");
                this.Publish(SpeechModelPhase.Failed);
            }
            finally { try { File.Delete(part); } catch { /* best effort after a disk failure */ } }
        }

        internal void Suspend()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _cancel.Cancel();
                if (_status.Phase != SpeechModelPhase.Ready) _status = new(SpeechModelPhase.Cancelled);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _status = new SpeechModelStatus(SpeechModelPhase.Cancelled);
                _cancel.Cancel();
                this.Changed = null;
                var cancel = _cancel;
                _pending.ContinueWith(_ => cancel.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }
}
