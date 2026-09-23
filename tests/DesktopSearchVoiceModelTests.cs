namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Xunit;

    public sealed class DesktopSearchVoiceModelTests : IDisposable
    {
        private readonly String _dir = Path.Combine(Path.GetTempPath(), "vizhi-model-test-" + Guid.NewGuid().ToString("N"));
        private static readonly Byte[] Bytes = Enumerable.Range(0, 400000).Select(i => (Byte)(i % 251)).ToArray();
        private String ModelPath => Path.Combine(_dir, "model.bin");
        public DesktopSearchVoiceModelTests() => Directory.CreateDirectory(_dir);
        public void Dispose() => Directory.Delete(_dir, true);
        private DesktopSearchVoiceModel Model(HttpClient http) => new(ModelPath, http, "https://example.invalid/model", Bytes.Length, Convert.ToHexString(SHA256.HashData(Bytes)));
        private sealed class Handler : HttpMessageHandler
        {
            internal Int32 Calls;
            internal Func<CancellationToken, Task<HttpResponseMessage>> Reply = _ => Task.FromResult(Response(Bytes));
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            { Interlocked.Increment(ref Calls); return Reply(token); }
        }
        private static HttpResponseMessage Response(Byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

        [Fact]
        public async Task Unload_cancels_a_download_and_reload_can_prepare_again()
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new Handler { Reply = async token =>
            {
                entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return Response(Bytes);
            } };
            using var http = new HttpClient(handler); using var model = Model(http);
            Assert.False(model.EnsureReady()); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            model.Suspend(); await model.Pending.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(SpeechModelPhase.Cancelled, model.Status.Phase);
            Assert.Empty(Directory.GetFiles(_dir, "*.part"));
            handler.Reply = _ => Task.FromResult(Response(Bytes));
            Assert.False(model.EnsureReady()); await model.Pending;
            Assert.True(model.EnsureReady()); Assert.Equal(2, handler.Calls);
            model.Suspend(); Assert.True(model.EnsureReady()); Assert.Equal(2, handler.Calls);
        }

        [Fact]
        public async Task Downloads_once_with_progress_and_reuses_verified_model_offline()
        {
            var handler = new Handler(); using var http = new HttpClient(handler); using var model = Model(http);
            var states = new System.Collections.Generic.List<SpeechModelStatus>();
            model.Changed += () => states.Add(model.Status);
            Assert.False(model.EnsureReady()); await model.Pending;
            Assert.True(model.EnsureReady()); Assert.True(model.EnsureReady()); Assert.Equal(1, handler.Calls);
            Assert.Equal(Bytes, File.ReadAllBytes(ModelPath)); Assert.Empty(Directory.GetFiles(_dir, "*.part"));
            Assert.Contains(states, s => s.Phase == SpeechModelPhase.Downloading && s.Percent > 0 && s.Percent < 100);
            Assert.Equal(SpeechModelPhase.Verifying, states[^2].Phase); Assert.Equal(SpeechModelPhase.Ready, states[^1].Phase);
            using var reload = Model(http); Assert.False(reload.EnsureReady()); await reload.Pending;
            Assert.True(reload.EnsureReady()); Assert.Equal(1, handler.Calls);
        }

        [Theory]
        [InlineData("truncated")][InlineData("checksum")][InlineData("too-large")][InlineData("http")]
        public async Task Bad_download_never_replaces_existing_file_and_can_retry(String failure)
        {
            var old = new Byte[] { 1, 2, 3 }; File.WriteAllBytes(ModelPath, old);
            var bad = failure switch { "truncated" => Bytes.Take(10).ToArray(), "too-large" => Bytes.Concat(new Byte[] { 0 }).ToArray(), _ => Bytes.Reverse().ToArray() };
            var handler = new Handler { Reply = _ => Task.FromResult(failure == "http" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Response(bad)) };
            using var http = new HttpClient(handler); using var model = Model(http);
            Assert.False(model.EnsureReady()); await model.Pending;
            Assert.Equal(SpeechModelPhase.Failed, model.Status.Phase); Assert.Equal("Tap to retry", model.Status.Footer);
            Assert.Equal(old, File.ReadAllBytes(ModelPath)); Assert.Empty(Directory.GetFiles(_dir, "*.part"));
            handler.Reply = _ => Task.FromResult(Response(Bytes));
            Assert.False(model.EnsureReady()); await model.Pending;
            Assert.True(model.EnsureReady()); Assert.Equal(2, handler.Calls);
        }

        [Fact]
        public async Task Same_size_corruption_is_detected_on_load_and_repaired()
        {
            File.WriteAllBytes(ModelPath, Bytes.Reverse().ToArray());
            var handler = new Handler(); using var http = new HttpClient(handler); using var model = Model(http);
            Assert.False(model.EnsureReady()); await model.Pending;
            Assert.Equal(1, handler.Calls); Assert.Equal(Bytes, File.ReadAllBytes(ModelPath));
        }

        [Fact]
        public async Task Repeated_presses_share_one_transfer_and_unload_cancels_it()
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new Handler { Reply = async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return Response(Bytes); } };
            using var http = new HttpClient(handler); using var model = Model(http);
            Assert.False(model.EnsureReady()); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var pending = model.Pending;
            for (var i = 0; i < 30; i++) Assert.False(model.EnsureReady());
            Assert.Same(pending, model.Pending); Assert.Equal(1, handler.Calls);
            model.Dispose(); await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(SpeechModelPhase.Cancelled, model.Status.Phase); Assert.False(model.EnsureReady());
            Assert.False(File.Exists(ModelPath)); Assert.Empty(Directory.GetFiles(_dir, "*.part"));
        }

        [Fact]
        public async Task Deleting_a_verified_model_requires_preparation_again()
        {
            var handler = new Handler(); using var http = new HttpClient(handler); using var model = Model(http);
            model.EnsureReady(); await model.Pending; File.Delete(ModelPath);
            Assert.False(model.EnsureReady()); await model.Pending; Assert.True(model.EnsureReady()); Assert.Equal(2, handler.Calls);
        }

        [Fact]
        public async Task Rendering_failure_does_not_fail_download()
        {
            using var http = new HttpClient(new Handler()); using var model = Model(http);
            model.Changed += () => throw new Exception("renderer unavailable");
            model.EnsureReady(); await model.Pending; Assert.True(model.EnsureReady());
        }

        [Fact]
        public void New_model_is_only_for_macOS_search()
        {
            foreach (var intent in Enum.GetValues<VoiceIntent>())
            {
                Assert.False(DesktopSearchVoiceModel.AppliesTo(intent, isMacOS: false));
                Assert.Equal(intent == VoiceIntent.DesktopSearch, DesktopSearchVoiceModel.AppliesTo(intent, isMacOS: true));
            }
        }
    }
}
