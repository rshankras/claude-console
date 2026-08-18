namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.IO;

    /// <summary>
    /// One definition of the private IPC layout, shared by BridgeManager and SessionRegistry.
    ///
    /// The bash hook/statusline scripts hardcode /tmp, so the plugin MUST read/write there too.
    /// On macOS Path.GetTempPath() returns /var/folders/.../T/ — a DIFFERENT dir — which is why
    /// the live displays once showed defaults. Match the scripts: /tmp on macOS/Linux, temp on Windows.
    ///
    /// Everything lives under ONE private root (0700 dirs / 0600 files — see PrivateFiles):
    ///   sessions/&lt;tty&gt;.json + sessions/shared.json   statusline state (per tab / fallback)
    ///   activity/&lt;tty&gt;.json + activity/shared.json   hook-pushed activity
    ///   registry.json                                 slot → tty assignments for the session grid
    ///   voice/                                        stop flag, transcript, recording
    /// Session state and transcripts carry the user's prompts and dictation, so they must not be
    /// world-readable the way the pre-1.4 loose /tmp/claude-console-*.json files were.
    /// </summary>
    internal static class IpcPaths
    {
        public static readonly String TempDir =
            Environment.OSVersion.Platform == PlatformID.Win32NT ? Path.GetTempPath() : "/tmp";

        public static readonly String Root = Path.Combine(TempDir, "claude-console");
        public static readonly String SessionsDir = Path.Combine(Root, "sessions");
        public static readonly String ActivityDir = Path.Combine(Root, "activity");
        public static readonly String VoiceDir = Path.Combine(Root, "voice");

        /// <summary>Fallback (last-writer-wins) state, used when no per-tab file matches.</summary>
        public static readonly String SharedStateFile = Path.Combine(SessionsDir, "shared.json");
        public static readonly String SharedActivityFile = Path.Combine(ActivityDir, "shared.json");

        public static readonly String RegistryFile = Path.Combine(Root, "registry.json");

        public static readonly String VoiceStopFile = Path.Combine(VoiceDir, "stop");
        public static readonly String VoiceTranscriptFile = Path.Combine(VoiceDir, "transcript.txt");
        public static readonly String VoiceWavFile = Path.Combine(VoiceDir, "capture.wav");

        /// <summary>Per-tab state file, e.g. sessions/ttys003.json. The bash scripts write these.</summary>
        public static String StateFor(String tty) => Path.Combine(SessionsDir, tty + ".json");

        /// <summary>Per-tab activity file, e.g. activity/ttys003.json.</summary>
        public static String ActivityFor(String tty) => Path.Combine(ActivityDir, tty + ".json");

        /// <summary>"shared" is the fallback file's name, never a real TTY — grid rows must skip it.</summary>
        public const String SharedName = "shared";

        /// <summary>Create the whole tree owner-only. Safe to call repeatedly.</summary>
        public static void EnsureAll()
        {
            PrivateFiles.EnsurePrivateDirectory(Root);
            PrivateFiles.EnsurePrivateDirectory(SessionsDir);
            PrivateFiles.EnsurePrivateDirectory(ActivityDir);
            PrivateFiles.EnsurePrivateDirectory(VoiceDir);
        }
    }
}
