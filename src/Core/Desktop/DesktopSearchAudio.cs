namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Reject silent input before delivering Whisper's output: even digital silence can produce
    /// a plausible word. The recorder's contract is 16-bit mono PCM at 16 kHz. This conservative
    /// energy gate is not a speech/noise classifier; ordinary background noise may still pass.
    /// </summary>
    internal static class DesktopSearchAudio
    {
        // One 20 ms window above -60 dBFS keeps quiet speech and rejects near-digital silence.
        internal static Boolean HasSignal(String path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.ASCII);
            String Tag() => Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (Tag() != "RIFF") throw new InvalidDataException("Search audio is not a WAV file");
            var end = (Int64)reader.ReadUInt32() + 8;
            if (end > stream.Length || Tag() != "WAVE") throw new InvalidDataException("Search WAV is incomplete");
            var formatValid = false;
            while (stream.Position + 8 <= end)
            {
                var tag = Tag(); var length = reader.ReadUInt32(); var next = stream.Position + length;
                if (next > end) throw new InvalidDataException("Search WAV chunk is incomplete");
                if (tag == "fmt ")
                {
                    if (length < 16) throw new InvalidDataException("Search WAV format is incomplete");
                    var pcm = reader.ReadUInt16(); var channels = reader.ReadUInt16(); var rate = reader.ReadUInt32();
                    reader.ReadUInt32(); var align = reader.ReadUInt16(); var bits = reader.ReadUInt16();
                    formatValid = pcm == 1 && channels == 1 && rate == 16000 && align == 2 && bits == 16;
                }
                else if (tag == "data")
                {
                    if (!formatValid || length % 2 != 0) throw new InvalidDataException("Unexpected search WAV format");
                    Double energy = 0; Int32 samples = 0;
                    while (stream.Position < next)
                    {
                        var value = (Double)reader.ReadInt16(); energy += value * value; samples++;
                        if (samples == 320)
                        {
                            if (energy / samples > 1073.741824) return true; // (32768 * 0.001)^2
                            energy = 0; samples = 0;
                        }
                    }
                    return samples > 0 && energy / samples > 1073.741824;
                }
                stream.Position = next + (length & 1);
            }
            throw new InvalidDataException("Search WAV has no PCM data");
        }
    }
}
