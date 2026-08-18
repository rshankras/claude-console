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

        /// <summary>
        /// The product that owns this root. Two consoles can be installed on one keypad, and each
        /// must have its own tree — sharing one would have each plugin reading the other's sessions
        /// and reaping them as dead.
        ///
        /// Set ONCE, by the product's plugin class, before anything reads a path. Everything below
        /// is computed rather than captured at type-init precisely so that assignment is honoured;
        /// a `static readonly` alias elsewhere would freeze the default before the product ever ran.
        /// </summary>
        public static String ProductSlug { get; private set; } = "claude-console";

        /// <summary>Declare which product this process is. Idempotent; ignores a blank slug.</summary>
        public static void UseProduct(String slug)
        {
            if (!String.IsNullOrWhiteSpace(slug))
            {
                ProductSlug = slug;
            }
        }

        public static String Root => Path.Combine(TempDir, ProductSlug);
        public static String SessionsDir => Path.Combine(Root, "sessions");
        public static String ActivityDir => Path.Combine(Root, "activity");
        public static String VoiceDir => Path.Combine(Root, "voice");

        /// <summary>Fallback (last-writer-wins) state, used when no per-tab file matches.</summary>
        public static String SharedStateFile => Path.Combine(SessionsDir, "shared.json");
        public static String SharedActivityFile => Path.Combine(ActivityDir, "shared.json");

        public static String RegistryFile => Path.Combine(Root, "registry.json");

        public static String VoiceStopFile => Path.Combine(VoiceDir, "stop");
        public static String VoiceTranscriptFile => Path.Combine(VoiceDir, "transcript.txt");
        public static String VoiceWavFile => Path.Combine(VoiceDir, "capture.wav");

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
