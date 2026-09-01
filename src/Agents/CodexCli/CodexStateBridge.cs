namespace Loupedeck.ClaudeConsolePlugin.Agents
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    /// <summary>How far along the Codex state bridge is. Drives what the plugin tells the user.</summary>
    internal enum CodexBridgeStatus
    {
        /// <summary>No hooks file of ours. Nothing will ever report.</summary>
        NotInstalled = 0,

        /// <summary>
        /// Installed, but no event has ever arrived. Almost always the trust grant: Codex lists
        /// hooks and runs them only once the user approves in <c>/hooks</c>. Distinguishing this
        /// from "working" is the difference between a keypad that looks broken and one that says
        /// what to do.
        /// </summary>
        AwaitingTrust = 1,

        /// <summary>Installed and events are arriving.</summary>
        Active = 2,

        /// <summary>
        /// A hooks file exists that we did not write. We refuse to touch it — a user's own hooks,
        /// or another tool's, are not ours to rewrite — and the plugin explains the manual step.
        /// </summary>
        ForeignHooksFile = 3,
    }

    /// <summary>
    /// Installs and inspects the Codex end of the state bridge.
    ///
    /// Claude Code's equivalent edits <c>~/.claude/settings.json</c>, which it must share with
    /// whatever statusline the user already had — hence the chaining. Codex is easier and harder in
    /// different places:
    ///
    /// EASIER: hooks are multi-consumer, and Codex reads them from <c>~/.codex/hooks.json</c> as
    /// well as from <c>config.toml</c>. Writing our OWN file means nothing the user configured is
    /// ever edited, and uninstalling is deleting one file rather than surgically removing a marker
    /// block from a config that may have changed underneath us. (Vizhi edited config.toml.)
    ///
    /// HARDER: Codex trusts hooks BY HASH and re-prompts whenever a hook changes. So the installed
    /// command must be STABLE across plugin versions — a path to a small launcher, never a command
    /// line that embeds a version, a temp directory, or anything else that churns. If this file
    /// starts rewriting the hook on every load, every user gets nagged on every update.
    ///
    /// And it means installation can never be silent, unlike Claude Code's. That's why
    /// <see cref="CodexBridgeStatus.AwaitingTrust"/> exists as a first-class state.
    /// </summary>
    internal sealed class CodexStateBridge
    {
        /// <summary>Stamped into the file so we can recognise our own work — and only ours.</summary>
        internal const String Marker = "Codex Console — keypad state bridge";

        /// <summary>The events we subscribe to. SessionEnd included so a closed tab leaves the grid.</summary>
        internal static readonly String[] Events =
        {
            "SessionStart", "UserPromptSubmit", "PreToolUse",
            "PermissionRequest", "PostToolUse", "Stop", "SessionEnd",
        };

        /// <summary>
        /// The two edges any transport must be able to express, named so a second transport
        /// (CodexRolloutBridge on Windows) writes the SAME envelope rather than inventing a
        /// dialect. CodexStateReader.ActivityFor maps these to busy / done.
        /// </summary>
        internal const String BusyEvent = "UserPromptSubmit";

        internal const String IdleEvent = "Stop";

        private readonly String _codexHome;
        private readonly String _sessionsDir;

        public CodexStateBridge(String codexHome = null, String sessionsDir = null)
        {
            this._codexHome = codexHome ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            this._sessionsDir = sessionsDir ?? Path.Combine(IpcPaths.TempDir, "codex-console", "sessions");
        }

        public String HooksFile => Path.Combine(this._codexHome, "hooks.json");

        /// <summary>
        /// Whether hooks should be installed. Settable so the no-install failure path remains
        /// testable; current Codex supports command hooks on Windows through commandWindows.
        /// </summary>
        internal Boolean InstallsHooks { get; set; } = true;

        /// <summary>
        /// Where the launcher lives. Under Codex's own directory, mirroring how Claude Console keeps
        /// its scripts under <c>~/.claude/</c>, and stable because the trust hash depends on it.
        /// </summary>
        public String HookScript =>
            Path.Combine(this._codexHome, "codex-console", "scripts", "codex-hook.sh");

        /// <summary>
        /// The Windows launcher: the packaged hook exe's codex verb. Windows has no /bin/sh and
        /// cannot run the .sh, so the command in hooks.json points at the exe instead. Resolved
        /// lazily — the SDK provides the plugin path after construction — and settable for tests.
        /// </summary>
        private String _hookExe;

        internal String HookExe
        {
            get => this._hookExe ?? Platform.PluginPaths.PackagedFile("claude-console-hook.exe") ?? "claude-console-hook.exe";
            set => this._hookExe = value;
        }

        /// <summary>
        /// The command Codex runs for one lifecycle event. Split by OS because the launch vehicle
        /// differs (sh script vs exe verb); both are STABLE strings, which trust-by-hash requires.
        /// The OS-free overload exists so tests can pin both shapes from any machine.
        /// </summary>
        internal String HookCommand(String eventName) =>
            this.HookCommand(eventName, OperatingSystem.IsWindows());

        internal String HookCommand(String eventName, Boolean windows) =>
            windows
                ? $"\"{this.HookExe}\" codex {eventName}"
                : $"/bin/sh '{this.HookScript}' {eventName}";

        /// <summary>
        /// Install the launcher and our hooks file if they are missing or out of date. Returns true
        /// when something was written — the caller can then tell the user a trust grant is due.
        /// Never throws: a failed install must degrade to a plugin that simply reports nothing.
        /// </summary>
        public Boolean EnsureInstalled(String scriptContents)
        {
            try
            {
                if (!this.InstallsHooks)
                {
                    PluginLog.Info("CodexStateBridge: hook installation disabled");
                    return false;
                }

                if (this.Status == CodexBridgeStatus.ForeignHooksFile)
                {
                    return false;
                }

                if (IsSymlink(this._codexHome) || IsSymlink(this.HooksFile))
                {
                    PluginLog.Info("CodexStateBridge: refusing to write through a symlink");
                    return false;
                }

                var wrote = this.WriteScript(scriptContents);
                wrote |= this.WriteHooksFile();
                return wrote;
            }
            catch (Exception ex)
            {
                PluginLog.Info($"CodexStateBridge: install failed — {ex.Message}");
                return false;
            }
        }

        public CodexBridgeStatus Status
        {
            get
            {
                try
                {
                    if (!File.Exists(this.HooksFile))
                    {
                        return CodexBridgeStatus.NotInstalled;
                    }

                    if (!this.IsOurs(File.ReadAllText(this.HooksFile)))
                    {
                        return CodexBridgeStatus.ForeignHooksFile;
                    }

                    // The only honest evidence that Codex is actually running our hook is that it
                    // has run it. Trust state itself is Codex's business and not ours to read.
                    // Windows also has rollout-derived state. Only an envelope explicitly written
                    // by the hook proves the hook was trusted and executed.
                    var seen = Directory.Exists(this._sessionsDir)
                        && Directory.EnumerateFiles(this._sessionsDir, "*.json").Any(IsHookEnvelope);

                    return seen ? CodexBridgeStatus.Active : CodexBridgeStatus.AwaitingTrust;
                }
                catch (Exception ex)
                {
                    PluginLog.Info($"CodexStateBridge: status check failed — {ex.Message}");
                    return CodexBridgeStatus.NotInstalled;
                }
            }
        }

        /// <summary>Remove what we installed, and nothing else.</summary>
        public Boolean Uninstall()
        {
            try
            {
                if (File.Exists(this.HooksFile) && this.IsOurs(File.ReadAllText(this.HooksFile)))
                {
                    File.Delete(this.HooksFile);
                    return true;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Info($"CodexStateBridge: uninstall failed — {ex.Message}");
            }

            return false;
        }

        internal Boolean IsOurs(String json)
        {
            if (String.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var node = JsonNode.Parse(json);
                var description = node?["description"]?.GetValue<String>();
                return description != null && description.Contains(Marker, StringComparison.Ordinal);
            }
            catch (JsonException)
            {
                // Unparseable is emphatically not ours, and must not be overwritten: it is far more
                // likely to be a user's file we would destroy than junk we may replace.
                return false;
            }
        }

        /// <summary>The hooks document, built once so the install and the tests cannot disagree.</summary>
        internal String BuildHooksJson() => this.BuildHooksJson(OperatingSystem.IsWindows());

        internal String BuildHooksJson(Boolean windows)
        {
            var events = new JsonObject();
            foreach (var e in Events)
            {
                var handler = new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = this.HookCommand(e, windows: false),
                    ["timeout"] = 5,
                };
                if (windows)
                {
                    // Official Codex Windows override. Keep command as the portable fallback and
                    // put Windows quoting/executable semantics in the dedicated field.
                    handler["commandWindows"] = this.HookCommand(e, windows: true);
                }

                events[e] = new JsonArray(
                    new JsonObject
                    {
                        ["matcher"] = "*",
                        ["hooks"] = new JsonArray(handler),
                    });
            }

            var doc = new JsonObject
            {
                ["description"] = Marker + ". Managed by the plugin; safe to delete.",
                ["hooks"] = events,
            };

            return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        }

        private static Boolean IsHookEnvelope(String path)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                return doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("transport", out var transport)
                    && transport.ValueKind == JsonValueKind.String
                    && String.Equals(transport.GetString(), "hook", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private Boolean WriteHooksFile()
        {
            var wanted = this.BuildHooksJson();

            // Rewriting an identical file would re-flag the hook for trust on some Codex versions.
            // Only ever write when the content actually differs.
            if (File.Exists(this.HooksFile) && File.ReadAllText(this.HooksFile) == wanted)
            {
                return false;
            }

            Directory.CreateDirectory(this._codexHome);
            File.WriteAllText(this.HooksFile, wanted);
            PrivateFiles.EnsurePrivateFile(this.HooksFile);
            PluginLog.Info($"CodexStateBridge: wrote {this.HooksFile} — a /hooks trust grant is now due");
            return true;
        }

        private Boolean WriteScript(String contents)
        {
            if (String.IsNullOrEmpty(contents))
            {
                return false;
            }

            var dir = Path.GetDirectoryName(this.HookScript);
            if (File.Exists(this.HookScript) && File.ReadAllText(this.HookScript) == contents)
            {
                return false;
            }

            PrivateFiles.EnsurePrivateDirectory(dir);
            File.WriteAllText(this.HookScript, contents);
            PrivateFiles.EnsurePrivateFile(this.HookScript);   // also refuses a symlinked target
            MakeExecutable(this.HookScript);
            return true;
        }

        private static void MakeExecutable(String path)
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(path,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Info($"CodexStateBridge: chmod failed — {ex.Message}");
            }
        }

        private static Boolean IsSymlink(String path)
        {
            try
            {
                return (File.Exists(path) || Directory.Exists(path))
                    && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
            }
            catch
            {
                return false;
            }
        }
    }
}
