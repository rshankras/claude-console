import Foundation
import AVFoundation
import AppKit

// ClaudeVoiceHelper — records the mic, transcribes with whisper.cpp, writes a transcript file.
//
// Why this exists as a standalone .app bundle (not inline in the C# plugin):
// LogiPluginService is a background daemon with no Microphone TCC grant and no way to show a
// permission prompt, so shelling out to a recorder from the plugin gets silently denied. A
// properly-bundled app with its own NSMicrophoneUsageDescription is its own TCC subject — macOS
// can prompt for and remember mic access for IT, independent of LogiPluginService.
//
// Flow: request mic -> record 16kHz mono WAV -> poll stop-flag (or hit maxsec) -> whisper-cli ->
// write transcript atomically. The plugin launches this, then types the transcript via its own
// (already-granted) Accessibility path.
//
// Args (all optional, sane defaults):
//   --out <wav>         recording path            (/tmp/claude-console-voice.wav)
//   --model <bin>       whisper ggml model        (~/.claude/claude-console/whisper/ggml-base.en.bin)
//   --transcript <txt>  where to write transcript (/tmp/claude-console-voice-transcript.txt)
//   --stopflag <path>   touch this to stop early  (/tmp/claude-console-voice.stop)
//   --whisper <bin>     whisper-cli binary        (auto-detected: bundled whisper-bin, else Homebrew)
//   --maxsec <N>        hard cap on recording     (30)

func argValue(_ name: String) -> String? {
    let a = CommandLine.arguments
    if let i = a.firstIndex(of: name), i + 1 < a.count { return a[i + 1] }
    return nil
}

func log(_ s: String) {
    FileHandle.standardError.write(Data(("[voicehelper] " + s + "\n").utf8))
}

let home = NSHomeDirectory()
let outWav = argValue("--out") ?? "/tmp/claude-console-voice.wav"
let modelPath = argValue("--model") ?? "\(home)/.claude/claude-console/whisper/ggml-base.en.bin"
let transcriptPath = argValue("--transcript") ?? "/tmp/claude-console-voice-transcript.txt"
let stopFlag = argValue("--stopflag") ?? "/tmp/claude-console-voice.stop"
let maxSec = Double(argValue("--maxsec") ?? "30") ?? 30

func findWhisper() -> String? {
    if let w = argValue("--whisper") { return w }
    // Prefer the self-contained bundle (no Homebrew needed); fall back to a system install.
    for c in ["\(home)/.claude/claude-console/whisper-bin/whisper-cli",
              "/opt/homebrew/bin/whisper-cli", "/usr/local/bin/whisper-cli",
              "/opt/homebrew/bin/whisper-cpp", "/usr/local/bin/whisper-cpp"] {
        if FileManager.default.isExecutableFile(atPath: c) { return c }
    }
    return nil
}

let fm = FileManager.default

// Fresh start: clear any stale transcript so the plugin never types a previous result.
try? fm.removeItem(atPath: transcriptPath)
try? fm.removeItem(atPath: stopFlag)

// 1) Microphone permission — this triggers the TCC prompt (attributed to THIS bundle).
let sem = DispatchSemaphore(value: 0)
var granted = false
AVCaptureDevice.requestAccess(for: .audio) { ok in granted = ok; sem.signal() }
sem.wait()
if !granted {
    log("microphone permission DENIED")
    exit(2)
}
log("microphone permission granted")

// 2) Record 16kHz mono PCM WAV — the format whisper.cpp reads directly (no ffmpeg step).
let url = URL(fileURLWithPath: outWav)
try? fm.removeItem(at: url)
let settings: [String: Any] = [
    AVFormatIDKey: Int(kAudioFormatLinearPCM),
    AVSampleRateKey: 16000.0,
    AVNumberOfChannelsKey: 1,
    AVLinearPCMBitDepthKey: 16,
    AVLinearPCMIsFloatKey: false,
    AVLinearPCMIsBigEndianKey: false,
]

guard let recorder = try? AVAudioRecorder(url: url, settings: settings) else {
    log("failed to create AVAudioRecorder")
    exit(3)
}
recorder.isMeteringEnabled = true
guard recorder.record() else {
    log("recorder.record() returned false")
    exit(3)
}
NSSound(named: "Tink")?.play()  // audible "speak now" cue (also the product's recording-started feedback)
log("recording -> \(outWav)  (touch \(stopFlag) to stop, max \(maxSec)s)")

// 3) Wait for the stop flag or the hard cap. RunLoop (not Thread.sleep) keeps AVFoundation happy.
let start = Date()
while true {
    RunLoop.current.run(until: Date().addingTimeInterval(0.12))
    if fm.fileExists(atPath: stopFlag) { log("stop flag seen"); break }
    if Date().timeIntervalSince(start) > maxSec { log("max duration reached"); break }
}
recorder.stop()
try? fm.removeItem(atPath: stopFlag)
let dur = Date().timeIntervalSince(start)
let attrs = try? fm.attributesOfItem(atPath: outWav)
let size = (attrs?[.size] as? Int) ?? 0
log(String(format: "recorded %.1fs, %d bytes", dur, size))

// 4) Transcribe with whisper.cpp. stderr -> /dev/null (progress noise); stdout = transcription.
guard let whisper = findWhisper() else {
    log("whisper-cli not found — install with: brew install whisper-cpp")
    exit(4)
}
guard fm.fileExists(atPath: modelPath) else {
    log("model not found at \(modelPath)")
    exit(5)
}

let p = Process()
p.executableURL = URL(fileURLWithPath: whisper)
p.arguments = ["-m", modelPath, "-f", outWav, "-nt"]
let outPipe = Pipe()
p.standardOutput = outPipe
p.standardError = FileHandle.nullDevice
do {
    try p.run()
} catch {
    log("whisper launch failed: \(error)")
    exit(6)
}
let data = outPipe.fileHandleForReading.readDataToEndOfFile()
p.waitUntilExit()

var text = String(data: data, encoding: .utf8) ?? ""
text = text.replacingOccurrences(of: "\n", with: " ")
           .trimmingCharacters(in: .whitespacesAndNewlines)
// whisper emits "[BLANK_AUDIO]" / "(silence)" markers when it hears nothing — treat as empty.
if text == "[BLANK_AUDIO]" || text == "(silence)" || text == "[ Silence ]" { text = "" }

log("transcript: \"\(text)\"")
try? text.write(toFile: transcriptPath, atomically: true, encoding: .utf8)
exit(0)
