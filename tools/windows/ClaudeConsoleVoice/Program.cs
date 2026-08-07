// claude-console-voice — the Windows counterpart of ClaudeVoiceHelper.app (Phase 5).
//
// Records the default microphone until the stop flag appears (or --maxsec elapses), writes the
// audio as 16 kHz mono 16-bit PCM — whisper's required input, produced directly by asking the
// WinMM wave mapper for that format so no resampler ships — then runs whisper-cli and writes the
// transcript. The plugin is already waiting on that file (BridgeManager.StopVoiceCaptureThen);
// the TRANSCRIPT FILE IS THE CONTRACT, so it is written atomically, and written ALWAYS — an
// empty file on failure or silence is what lets the plugin stop waiting instead of timing out.
//
// Argument names mirror what BridgeManager.StartVoiceCapture passes on macOS, verb-for-verb:
//   --maxsec 60 --out capture.wav --stopflag stop --transcript transcript.txt
//   --model ggml-base.en.bin --whisper whisper-cli.exe
//
// Mic permission on Windows is a Settings toggle (Privacy & security > Microphone > "Let desktop
// apps access your microphone"); when denied, waveInOpen fails or records silence — either way
// the transcript comes back empty and the plugin treats it as silence. `selftest` is the
// hand-run diagnostic for exactly that.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

internal static class Program
{
    private const Int32 SampleRate = 16000;      // whisper's required input rate
    private const Int16 BitsPerSample = 16;
    private const Int16 Channels = 1;

    private const Int32 BufferMs = 100;
    private const Int32 BufferBytes = SampleRate * (BitsPerSample / 8) * Channels * BufferMs / 1000;
    private const Int32 BufferCount = 8;

    private static Int32 Main(String[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("claude-console-voice runs on Windows only.");
            return 1;
        }

        if (args.Length > 0 && args[0] == "selftest")
        {
            return SelfTest();
        }

        var opts = ParseOptions(args);
        var wavPath = opts.GetValueOrDefault("--out");
        var stopFlag = opts.GetValueOrDefault("--stopflag");
        var transcriptPath = opts.GetValueOrDefault("--transcript");

        if (wavPath == null || stopFlag == null || transcriptPath == null)
        {
            Console.Error.WriteLine("""
                claude-console-voice — record the default mic and transcribe with whisper

                  --maxsec N --out capture.wav --stopflag stop --transcript transcript.txt
                  --model ggml-base.en.bin --whisper whisper-cli.exe
                  selftest

                Records until the stopflag file appears (or maxsec). The transcript file is
                ALWAYS written — empty on silence or failure — because the plugin waits on it.
                """);
            return 1;
        }

        var maxSec = 60;
        if (Int32.TryParse(opts.GetValueOrDefault("--maxsec"), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            maxSec = Math.Min(parsed, 300);
        }

        try
        {
            var pcm = Record(stopFlag, maxSec);
            WriteWav(wavPath, pcm);

            var transcript = pcm.Length > 0
                ? Transcribe(opts.GetValueOrDefault("--whisper"), opts.GetValueOrDefault("--model"), wavPath)
                : "";
            WriteAtomic(transcriptPath, transcript);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"claude-console-voice: {ex.Message}");
            // The contract: the plugin is waiting on this file. An empty transcript reads as
            // silence and ends the wait; no file would burn its whole 20 s timeout.
            try { WriteAtomic(transcriptPath, ""); } catch { /* nothing left to try */ }
            return 1;
        }
        finally
        {
            try { File.Delete(stopFlag); } catch { /* next capture clears it anyway */ }
        }
    }

    private static Dictionary<String, String> ParseOptions(String[] args)
    {
        var opts = new Dictionary<String, String>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                opts[args[i]] = args[i + 1];
                i++;
            }
        }
        return opts;
    }

    // ---- capture -----------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static Byte[] Record(String stopFlag, Int32 maxSec)
    {
        var fmt = new WAVEFORMATEX
        {
            wFormatTag = 1,   // WAVE_FORMAT_PCM
            nChannels = Channels,
            nSamplesPerSec = SampleRate,
            wBitsPerSample = BitsPerSample,
            nBlockAlign = (Int16)(Channels * BitsPerSample / 8),
            nAvgBytesPerSec = SampleRate * Channels * BitsPerSample / 8,
            cbSize = 0,
        };

        var rc = waveInOpen(out var handle, WaveMapper, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
        if (rc != 0)
        {
            // Denied mic access, no device, or format refused — all end here.
            throw new InvalidOperationException($"waveInOpen failed: {rc} (is a microphone present, and are desktop apps allowed to use it?)");
        }

        var audio = new MemoryStream();
        var buffers = new IntPtr[BufferCount];
        var headers = new IntPtr[BufferCount];

        try
        {
            for (var i = 0; i < BufferCount; i++)
            {
                buffers[i] = Marshal.AllocHGlobal(BufferBytes);
                headers[i] = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
                PrepareAndAdd(handle, headers[i], buffers[i]);
            }

            waveInStart(handle);

            var deadline = DateTime.UtcNow.AddSeconds(maxSec);
            while (DateTime.UtcNow < deadline && !File.Exists(stopFlag))
            {
                DrainDoneBuffers(handle, headers, buffers, audio, requeue: true);
                Thread.Sleep(30);
            }

            // Stop and collect what's still in flight. waveInReset returns every pending buffer
            // with WHDR_DONE set, so nothing recorded is lost.
            waveInStop(handle);
            waveInReset(handle);
            DrainDoneBuffers(handle, headers, buffers, audio, requeue: false);

            return audio.ToArray();
        }
        finally
        {
            for (var i = 0; i < BufferCount; i++)
            {
                if (headers[i] != IntPtr.Zero)
                {
                    waveInUnprepareHeader(handle, headers[i], Marshal.SizeOf<WAVEHDR>());
                    Marshal.FreeHGlobal(headers[i]);
                }
                if (buffers[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffers[i]);
                }
            }
            waveInClose(handle);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void PrepareAndAdd(IntPtr handle, IntPtr headerPtr, IntPtr buffer)
    {
        var hdr = new WAVEHDR
        {
            lpData = buffer,
            dwBufferLength = BufferBytes,
        };
        Marshal.StructureToPtr(hdr, headerPtr, fDeleteOld: false);
        waveInPrepareHeader(handle, headerPtr, Marshal.SizeOf<WAVEHDR>());
        waveInAddBuffer(handle, headerPtr, Marshal.SizeOf<WAVEHDR>());
    }

    [SupportedOSPlatform("windows")]
    private static void DrainDoneBuffers(IntPtr handle, IntPtr[] headers, IntPtr[] buffers, MemoryStream audio, Boolean requeue)
    {
        for (var i = 0; i < BufferCount; i++)
        {
            var hdr = Marshal.PtrToStructure<WAVEHDR>(headers[i]);
            if ((hdr.dwFlags & WHDR_DONE) == 0)
            {
                continue;
            }

            if (hdr.dwBytesRecorded > 0)
            {
                var chunk = new Byte[hdr.dwBytesRecorded];
                Marshal.Copy(hdr.lpData, chunk, 0, (Int32)hdr.dwBytesRecorded);
                audio.Write(chunk, 0, chunk.Length);
            }

            if (requeue)
            {
                waveInUnprepareHeader(handle, headers[i], Marshal.SizeOf<WAVEHDR>());
                PrepareAndAdd(handle, headers[i], buffers[i]);
            }
        }
    }

    // ---- wav ---------------------------------------------------------------

    private static void WriteWav(String path, Byte[] pcm)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var w = new BinaryWriter(fs);

        w.Write("RIFF"u8);
        w.Write(36 + pcm.Length);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((Int16)1);                                  // PCM
        w.Write(Channels);
        w.Write(SampleRate);
        w.Write(SampleRate * Channels * BitsPerSample / 8); // byte rate
        w.Write((Int16)(Channels * BitsPerSample / 8));     // block align
        w.Write(BitsPerSample);
        w.Write("data"u8);
        w.Write(pcm.Length);
        w.Write(pcm);
    }

    // ---- transcription -----------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static String Transcribe(String? whisperCli, String? model, String wavPath)
    {
        if (whisperCli == null || model == null || !File.Exists(whisperCli) || !File.Exists(model))
        {
            Console.Error.WriteLine("whisper-cli or model missing — transcript will be empty");
            return "";
        }

        var psi = new ProcessStartInfo
        {
            FileName = whisperCli,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // --no-timestamps: bare text; --no-prints: keep progress off stdout; language pinned to
        // the model (base.EN) rather than auto-detected.
        foreach (var a in new[] { "-m", model, "-f", wavPath, "--no-timestamps", "--no-prints", "-l", "en" })
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi);
        if (p == null)
        {
            return "";
        }

        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(120_000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            Console.Error.WriteLine("whisper-cli exceeded 120s — killed");
            return "";
        }

        _ = errTask.ContinueWith(_ => { });   // drained; content irrelevant on success
        var text = outTask.GetAwaiter().GetResult().Trim();
        return CleanTranscript(text);
    }

    /// <summary>
    /// Whisper's non-speech annotations must never be typed into a terminal: silence comes back
    /// as "[BLANK_AUDIO]", breaths as "(sighs)" and so on. Strip bracketed/parenthesized runs;
    /// what remains is the dictation (possibly nothing, which the plugin treats as silence).
    /// </summary>
    internal static String CleanTranscript(String text)
    {
        var sb = new StringBuilder(text.Length);
        var depth = 0;
        foreach (var ch in text)
        {
            if (ch == '[' || ch == '(')
            {
                depth++;
            }
            else if ((ch == ']' || ch == ')') && depth > 0)
            {
                depth--;
            }
            else if (depth == 0)
            {
                sb.Append(ch);
            }
        }
        return String.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // The plugin polls this file and reads it the instant it appears; a partial write would type
    // half a sentence. Same discipline as the hook shim's WriteAtomic.
    private static void WriteAtomic(String path, String content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    // ---- selftest ----------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static Int32 SelfTest()
    {
        var devices = waveInGetNumDevs();
        Console.WriteLine($"claude-console-voice selftest\n\n  capture devices: {devices}");
        if (devices == 0)
        {
            Console.WriteLine("  no microphone — check Settings > Privacy & security > Microphone");
            return 1;
        }

        Console.WriteLine("  recording 1s from the default mic...");
        var stop = Path.Combine(Path.GetTempPath(), $"cc-voice-selftest-{Environment.ProcessId}");
        var pcm = Record(stop, maxSec: 1);

        // RMS over 16-bit samples: ~0 means the mic is present but muted or access-denied.
        Double sum = 0;
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            Double s = BitConverter.ToInt16(pcm, i);
            sum += s * s;
        }
        var rms = pcm.Length > 1 ? Math.Sqrt(sum / (pcm.Length / 2)) : 0;

        Console.WriteLine($"  captured {pcm.Length} bytes, RMS {rms:F1}");
        Console.WriteLine(rms < 1
            ? "  DEAD SILENCE — mic muted, or desktop apps are denied microphone access"
            : "  mic is live");
        return 0;
    }

    // ---- WinMM -------------------------------------------------------------

    private const UInt32 WaveMapper = unchecked((UInt32)(-1));
    private const Int32 WHDR_DONE = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public Int16 wFormatTag;
        public Int16 nChannels;
        public Int32 nSamplesPerSec;
        public Int32 nAvgBytesPerSec;
        public Int16 nBlockAlign;
        public Int16 wBitsPerSample;
        public Int16 cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public Int32 dwBufferLength;
        public Int32 dwBytesRecorded;
        public IntPtr dwUser;
        public Int32 dwFlags;
        public Int32 dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")]
    private static extern Int32 waveInGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern Int32 waveInOpen(out IntPtr handle, UInt32 deviceId, ref WAVEFORMATEX format,
        IntPtr callback, IntPtr instance, UInt32 flags);

    [DllImport("winmm.dll")]
    private static extern Int32 waveInPrepareHeader(IntPtr handle, IntPtr header, Int32 size);

    [DllImport("winmm.dll")]
    private static extern Int32 waveInUnprepareHeader(IntPtr handle, IntPtr header, Int32 size);

    [DllImport("winmm.dll")]
    private static extern Int32 waveInAddBuffer(IntPtr handle, IntPtr header, Int32 size);

    [DllImport("winmm.dll")]
    private static extern Int32 waveInStart(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern Int32 waveInStop(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern Int32 waveInReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern Int32 waveInClose(IntPtr handle);
}
