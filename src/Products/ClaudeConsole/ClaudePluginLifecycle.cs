namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json.Nodes;

    using Loupedeck.ClaudeConsolePlugin.Platform;

    /// <summary>
    /// Claude Console's Windows package lifecycle, independent of Load(), polling and SDK UI.
    /// Options+ calls Uninstall then Install for replacements too. Keep ONLY our previous wiring
    /// in a receipt outside the package, so replacement/reinstall retains the user's preference.
    /// Never restore a whole settings backup over subsequent user edits.
    /// </summary>
    internal sealed class ClaudePluginLifecycle
    {
        private readonly ClaudeSettingsStore _settings;
        private readonly String _runtime;
        internal String ReceiptFile => Path.Combine(_runtime, "reinstall-wiring.json");
        internal String LogFile => Path.Combine(_runtime, "lifecycle.log");
        private String ChainFile => Path.Combine(_runtime, "statusline-chain");
        private String OffFile => Path.Combine(_runtime, "no-autowire");

        internal ClaudePluginLifecycle(String home)
        {
            _settings = new ClaudeSettingsStore(home);
            _runtime = Path.Combine(home, ".claude", "claude-console");
        }

        internal Boolean Uninstall()
        {
            try
            {
                using var guard = _settings.AcquireLock();
                ClaudeSettingsStore.RefuseLink(ChainFile);
                var chain = File.Exists(ChainFile) ? File.ReadAllText(ChainFile).Trim() : null;
                var ok = _settings.RewriteLocked(root =>
                {
                    var owned = CaptureOwned(root);
                    if (owned.Count == 0) { return false; }
                    BridgeWiring.Unwire(root, chain);
                    if (!File.Exists(OffFile))
                    {
                        // Persist BEFORE committing settings. A retry can safely repeat cleanup;
                        // a crash after commit must not lose the enabled preference on upgrade.
                        WriteAtomic(ReceiptFile, new JsonObject
                        {
                            ["schema"] = 1,
                            ["wiring"] = owned,
                            ["afterStatusLine"] = root["statusLine"]?.DeepClone(),
                            ["chain"] = chain,
                        }.ToJsonString());
                    }
                    return true;
                }, out var changed);
                if (!ok) { throw new IOException("Settings could not be safely read or kept changing"); }
                Record("uninstall", changed ? "complete; owned wiring removed" : "complete; no owned wiring");
                // Keep chain and receipt as recovery data; neither executes after unwiring.
                // Do not set no-autowire: removal during an update is not a request to turn off.
                return true;
            }
            catch (Exception ex)
            {
                Record("uninstall", "FAILED; settings cleanup incomplete; " + ex.Message);
                // The installed host still deletes the package after false. Never claim this
                // aborts uninstall. The missing-exe guard remains the last line of defense.
                return false;
            }
        }

        internal Boolean Restore(String assemblyFilePath)
        {
            // A fresh install is not consent to enable live status.
            if (!File.Exists(ReceiptFile)) { return true; }
            try
            {
                using var guard = _settings.AcquireLock();
                ClaudeSettingsStore.RefuseLink(ReceiptFile);
                if (File.Exists(OffFile))
                {
                    File.Delete(ReceiptFile);
                    Record("install", "live status remains off");
                    return true;
                }

                var receipt = JsonNode.Parse(File.ReadAllText(ReceiptFile)) as JsonObject;
                if (receipt?["schema"]?.GetValue<Int32>() != 1 ||
                    receipt["wiring"] is not JsonObject wiring ||
                    !JsonNode.DeepEquals(wiring, CaptureOwned(wiring)))
                {
                    throw new IOException("Unrecognized wiring receipt; retained for recovery");
                }
                var directory = String.IsNullOrEmpty(assemblyFilePath) ? null : Path.GetDirectoryName(assemblyFilePath);
                var exe = directory == null ? null : Path.Combine(directory, "claude-console-hook.exe");
                if (exe == null || !File.Exists(exe))
                {
                    throw new IOException("Packaged hook executable is unavailable; restoration deferred until load");
                }
                BridgeWiring.UpgradeOwnedCommands(wiring, true, exe, exe);

                var skippedStatusLine = false;
                var ok = _settings.RewriteLocked(root =>
                {
                    skippedStatusLine = false;
                    var changed = BridgeWiring.UpgradeOwnedCommands(root, true, exe, exe);
                    changed |= RestoreHooks(root, wiring);
                    if (wiring["statusLine"] is JsonObject savedStatus)
                    {
                        if (BridgeWiring.IsOurs(BridgeWiring.Str(root["statusLine"]?["command"])))
                        {
                            // Cleanup may have failed before package replacement. Keep the live
                            // entry's options, and migrate only its command to the new package.
                            changed |= BridgeWiring.UpgradeOwnedCommands(root, true, exe, exe);
                        }
                        else if (JsonNode.DeepEquals(root["statusLine"], receipt["afterStatusLine"]))
                        {
                            // Chain must exist before its caller becomes visible in settings.
                            var chain = BridgeWiring.Str(receipt["chain"]);
                            if (!String.IsNullOrWhiteSpace(chain)) { WriteAtomic(ChainFile, chain); }
                            else
                            {
                                ClaudeSettingsStore.RefuseLink(ChainFile);
                                File.Delete(ChainFile);
                            }
                            root["statusLine"] = savedStatus.DeepClone();
                            changed = true;
                        }
                        else
                        {
                            // A new user status line installed while we were absent wins.
                            skippedStatusLine = true;
                        }
                    }
                    return changed;
                }, out _);
                if (!ok) { throw new IOException("Settings could not be safely read or kept changing"); }
                File.Delete(ReceiptFile);
                Record("install", skippedStatusLine
                    ? "hooks restored; newer user status line preserved (use Enable Live Status to repair)"
                    : "previous owned wiring restored");
                return true;
            }
            catch (Exception ex)
            {
                Record("install", "DEFERRED; recovery receipt retained; " + ex.Message);
                return false;
            }
        }

        private static JsonObject CaptureOwned(JsonObject root)
        {
            var result = new JsonObject();
            if (root["hooks"] is JsonObject hooks)
            {
                var savedHooks = new JsonObject();
                foreach (var item in hooks)
                {
                    if (item.Value is not JsonArray entries) { continue; }
                    var savedEntries = new JsonArray();
                    foreach (var entry in entries.OfType<JsonObject>())
                    {
                        if (entry["hooks"] is not JsonArray inner) { continue; }
                        var ours = inner.OfType<JsonObject>()
                            .Where(h => BridgeWiring.IsOurHook(BridgeWiring.Str(h["command"])))
                            .Select(h => h.DeepClone()).ToArray();
                        if (ours.Length == 0) { continue; }
                        var saved = (JsonObject)entry.DeepClone();
                        saved["hooks"] = new JsonArray(ours);
                        savedEntries.Add(saved);
                    }
                    if (savedEntries.Count > 0) { savedHooks[item.Key] = savedEntries; }
                }
                if (savedHooks.Count > 0) { result["hooks"] = savedHooks; }
            }
            if (root["statusLine"] is JsonObject status && BridgeWiring.IsOurs(BridgeWiring.Str(status["command"])))
            {
                result["statusLine"] = status.DeepClone();
            }
            return result;
        }

        private static Boolean RestoreHooks(JsonObject root, JsonObject wiring)
        {
            if (wiring["hooks"] is not JsonObject savedHooks) { return false; }
            if (root["hooks"] != null && root["hooks"] is not JsonObject)
            { throw new IOException("User hooks value is not an object; leaving it unchanged"); }
            var hooks = root["hooks"] as JsonObject;
            if (hooks == null) { hooks = new JsonObject(); root["hooks"] = hooks; }
            var changed = false;
            foreach (var item in savedHooks)
            {
                if (hooks[item.Key] != null && hooks[item.Key] is not JsonArray)
                { throw new IOException("User hook event is not an array: " + item.Key); }
                var entries = hooks[item.Key] as JsonArray;
                if (entries == null) { entries = new JsonArray(); hooks[item.Key] = entries; }
                foreach (var saved in ((JsonArray)item.Value).OfType<JsonObject>())
                {
                    var metadata = (JsonObject)saved.DeepClone();
                    metadata.Remove("hooks");
                    var target = entries.OfType<JsonObject>().FirstOrDefault(entry =>
                    {
                        var candidate = (JsonObject)entry.DeepClone();
                        candidate.Remove("hooks");
                        return entry["hooks"] is JsonArray && JsonNode.DeepEquals(candidate, metadata);
                    });
                    if (target == null)
                    {
                        entries.Add(saved.DeepClone());
                        changed = true;
                        continue;
                    }
                    var inner = (JsonArray)target["hooks"];
                    foreach (var hook in (JsonArray)saved["hooks"])
                    {
                        if (inner.Any(h => JsonNode.DeepEquals(h, hook))) { continue; }
                        inner.Add(hook.DeepClone());
                        changed = true;
                    }
                }
            }
            return changed;
        }

        private static void WriteAtomic(String path, String text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ClaudeSettingsStore.RefuseLink(path);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }

        private void Record(String operation, String detail)
        {
            var message = $"Claude Console lifecycle {operation}: {detail}";
            PluginLog.Info(message);
            try
            {
                Directory.CreateDirectory(_runtime);
                ClaudeSettingsStore.RefuseLink(LogFile);
                if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 65536)
                { WriteAtomic(LogFile, String.Empty); }
                File.AppendAllText(LogFile, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
            }
            catch (Exception ex) { PluginLog.Warning(ex, "Could not write the lifecycle recovery log"); }
        }
    }
}
