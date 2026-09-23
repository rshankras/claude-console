namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    // Shared by the running bridge and the fresh SDK install/uninstall context.
    // The lock serializes cooperating writers; fingerprints also detect external edits.
    internal sealed class ClaudeSettingsStore
    {
        private readonly String ClaudeDir;
        private readonly String SettingsFile;
        private readonly String SettingsBackup;
        internal String LockFile => Path.Combine(ClaudeDir, ".claude-console-unwire.lock");

        internal ClaudeSettingsStore(String home)
        {
            ClaudeDir = Path.Combine(home, ".claude");
            SettingsFile = Path.Combine(ClaudeDir, "settings.json");
            SettingsBackup = SettingsFile + ".claude-console.bak";
        }

        internal IDisposable AcquireLock()
        {
            Directory.CreateDirectory(ClaudeDir);
            RefuseLink(LockFile);
            // Never delete this file: unlinking it would let another writer bypass a held lock.
            return new FileStream(LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        internal Boolean Rewrite(Func<JsonObject, Boolean> mutate, out Boolean changed)
        {
            changed = false;
            try
            {
                using var guard = AcquireLock();
                return RewriteLocked(mutate, out changed);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Live status: settings transaction failed; retry after resolving the file error");
                return false;
            }
        }

        internal static void RefuseLink(String path)
        {
            if (new FileInfo(path).LinkTarget != null)
            {
                throw new IOException("Refusing symbolic link: " + path);
            }
        }

        private static void TryDelete(String path)
        {
            try { File.Delete(path); } catch { /* original failure is more useful */ }
        }

        /// <summary>
        /// The one way settings.json is rewritten. <paramref name="mutate"/> edits the parsed document
        /// in place and returns whether it changed anything: read → mutate → write, where the write
        /// refuses if the file's bytes moved since the read (Claude Code itself writes this file), and
        /// one retry runs the whole thing again from the fresh contents. Returns false when the file
        /// could not be touched at all (symlink, invalid JSON, kept changing); <paramref name="changed"/>
        /// reports whether a write actually happened — a mutate that finds nothing to do costs no
        /// write and no backup.
        /// </summary>
        internal Boolean RewriteLocked(Func<JsonObject, Boolean> mutate, out Boolean changed)
        {
            changed = false;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var root = ReadSettingsForRewrite(out var fingerprint, out var layout, out var original);
                if (root == null)
                {
                    return false;
                }
                var render = SettingsText.Capture(original, root, layout);
                if (!mutate(root))
                {
                    return true;
                }
                if (WriteSettings(root, fingerprint, layout, render(root)))
                {
                    changed = true;
                    return true;
                }
                PluginLog.Warning("Live status: settings.json changed while it was being edited — retrying from the new contents");
            }

            PluginLog.Warning("Live status: settings.json kept changing — giving up; try again");
            return false;
        }

        // SHA-256 of a file's bytes, or null when there is no file. The identity a write checks
        // against: "the file I am about to replace is the file I read".
        private static Byte[] Fingerprint(String path)
        {
            return File.Exists(path) ? SHA256.HashData(File.ReadAllBytes(path)) : null;
        }

        private static Boolean SameBytes(Byte[] a, Byte[] b) =>
            a == null ? b == null : b != null && a.AsSpan().SequenceEqual(b);

        // settings.json as a document we may rewrite, or null when we must not touch it: a symlink
        // (a planted link could redirect the rename-over-write), invalid JSON, or a non-object root.
        // A missing or empty file is an empty object — wiring a fresh install is the common case.
        // The fingerprint is of exactly the bytes that were parsed, so a write can prove nothing
        // slipped in between.
        internal JsonObject ReadSettingsForRewrite(out Byte[] fingerprint) =>
            ReadSettingsForRewrite(out fingerprint, out _, out _);

        // The layout is read alongside the document so the write can hand the file back the way it
        // was found (#72): same indentation, same line ending, same trailing newline.
        internal JsonObject ReadSettingsForRewrite(out Byte[] fingerprint, out SettingsLayout layout, out String original)
        {
            original = null;
            fingerprint = null;
            layout = SettingsLayout.Default;
            if (new FileInfo(SettingsFile).LinkTarget != null)
            {
                PluginLog.Warning("Live status: settings.json is a symlink — leaving it untouched");
                return null;
            }

            if (!File.Exists(SettingsFile))
            {
                return new JsonObject();
            }

            var bytes = File.ReadAllBytes(SettingsFile);
            fingerprint = SHA256.HashData(bytes);
            String text;
            using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                text = reader.ReadToEnd();
            }
            if (String.IsNullOrWhiteSpace(text))
            {
                return new JsonObject();
            }
            layout = SettingsLayout.Detect(bytes, text);
            original = text;

            JsonNode parsed;
            try
            {
                parsed = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Live status: settings.json isn't valid JSON — leaving it untouched");
                return null;
            }

            if (parsed is not JsonObject root)
            {
                PluginLog.Warning("Live status: settings.json isn't a JSON object — leaving it untouched");
                return null;
            }

            return root;
        }

        // Write atomically, but only over the file that was read. The temp name is unique per call
        // so two writers can never truncate each other's temp; the file is re-fingerprinted at the
        // last possible moment before the rename and the write is REFUSED on a mismatch — the caller
        // (RewriteSettings) re-reads and tries once more. The backup is ROLLING — taken immediately
        // before every write that goes ahead, overwriting the last one (#31). It used to be taken once,
        // on the first load, and never again: on QA's machine it was a month stale, so "restore the
        // backup" would have rolled back every unrelated change the user had made since. A backup
        // that is always the state one write ago is the only kind worth telling people about.
        //
        // The bytes are laid out the way the file was found (#72). The default writer escaped every
        // quote, ampersand, apostrophe, angle bracket and non-ASCII character to \uXXXX and dropped
        // the trailing newline — valid JSON, but a whole-file diff for anyone who keeps ~/.claude in
        // git, applied to every entry in the file including the ones the plugin does not own.
        private Boolean WriteSettings(JsonObject root, Byte[] expected, SettingsLayout layout, String json)
        {
            Directory.CreateDirectory(ClaudeDir);
            var parsed = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!JsonNode.DeepEquals(parsed, root))
            { throw new InvalidOperationException("settings source edit disagrees with the intended values"); }
            var tmp = SettingsFile + ".cc." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, json, layout.Encoding);

                if (!SameBytes(Fingerprint(SettingsFile), expected))
                {
                    TryDelete(tmp);
                    return false;
                }

                try
                {
                    if (File.Exists(SettingsFile))
                    {
                        RefuseLink(SettingsBackup);
                    File.Copy(SettingsFile, SettingsBackup, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    throw new IOException("Could not back up settings.json; leaving it unchanged", ex);
                }

                RefuseLink(SettingsFile);
                if (!SameBytes(Fingerprint(SettingsFile), expected))
                {
                    TryDelete(tmp);
                    return false;
                }
                if (expected == null) { File.Move(tmp, SettingsFile); }
                else { File.Replace(tmp, SettingsFile, null); }
                return true;
            }
            catch
            {
                TryDelete(tmp);
                throw;
            }
        }

    }
}
