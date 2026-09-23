namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Xunit;

    public sealed class DesktopSearchAudioTests : IDisposable
    {
        private readonly String _path = Path.Combine(Path.GetTempPath(), "vizhi-audio-test-" + Guid.NewGuid().ToString("N") + ".wav");
        public void Dispose() => File.Delete(_path);
        private void Write(Int16 amplitude, Boolean extraChunk = false, UInt16 bits = 16)
        {
            using var s = File.Create(_path); using var w = new BinaryWriter(s, Encoding.ASCII);
            void Tag(String t) => w.Write(Encoding.ASCII.GetBytes(t));
            Tag("RIFF"); w.Write(36 + 32000 + (extraChunk ? 12 : 0)); Tag("WAVE");
            Tag("fmt "); w.Write(16); w.Write((UInt16)1); w.Write((UInt16)1); w.Write(16000); w.Write(32000); w.Write((UInt16)2); w.Write(bits);
            if (extraChunk) { Tag("JUNK"); w.Write(3); w.Write(new Byte[4]); }
            Tag("data"); w.Write(32000);
            for (var i = 0; i < 16000; i++) w.Write((Int16)(amplitude * Math.Sin(i * Math.PI / 20)));
        }
        [Theory]
        [InlineData(0, false)][InlineData(8, false)][InlineData(300, true)][InlineData(4000, true)]
        public void Silence_is_rejected_but_quiet_signal_is_preserved(Int16 amplitude, Boolean expected)
        { Write(amplitude); Assert.Equal(expected, DesktopSearchAudio.HasSignal(_path)); }
        [Fact]
        public void Extra_odd_sized_metadata_chunks_do_not_hide_audio()
        { Write(300, extraChunk: true); Assert.True(DesktopSearchAudio.HasSignal(_path)); }
        [Fact]
        public void Truncated_audio_never_becomes_a_query()
        { Write(300); using (var f = File.OpenWrite(_path)) f.SetLength(40); Assert.Throws<InvalidDataException>(() => DesktopSearchAudio.HasSignal(_path)); }
        [Fact]
        public void Unexpected_sample_format_is_not_interpreted_as_speech()
        { Write(300, bits: 8); Assert.Throws<InvalidDataException>(() => DesktopSearchAudio.HasSignal(_path)); }
    }
}
