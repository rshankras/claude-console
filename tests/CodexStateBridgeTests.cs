namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Text.Json.Nodes;

    using Loupedeck.ClaudeConsolePlugin.Agents;

    using Xunit;

    /// <summary>
    /// Installing the Codex end of the state bridge.
    ///
    /// Two properties matter more than the rest, and both are about restraint. Codex trusts hooks by
    /// hash and re-prompts when one changes, so a rewrite that changes nothing must not happen — it
    /// would nag every user on every plugin load. And a hooks file we didn't write belongs to
    /// someone else; we would rather do nothing and explain than "helpfully" replace it.
    /// </summary>
    public class CodexStateBridgeTests : IDisposable
    {
        private readonly String _home =
            Path.Combine(Path.GetTempPath(), "cx-bridge-" + Guid.NewGuid().ToString("N"));

        private readonly String _sessions =
            Path.Combine(Path.GetTempPath(), "cx-sess-" + Guid.NewGuid().ToString("N"));

        private const String Script = "#!/bin/bash\n# stable launcher\nprintf '{}'\n";

        // Supply the expected package file on every OS. Missing-helper behavior has dedicated
        // tests; these installer/trust tests must also exercise the healthy path on Windows.
        private CodexStateBridge New()
        {
            Directory.CreateDirectory(this._home);
            var helper = Path.Combine(this._home, "claude-console-hook.exe");
            if (!File.Exists(helper)) { File.WriteAllText(helper, "fixture"); }
            return new CodexStateBridge(this._home, this._sessions) { InstallsHooks = true, HookExe = helper };
        }

        public void Dispose()
        {
            try { Directory.Delete(this._home, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(this._sessions, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public void A_fresh_machine_reports_nothing_installed() =>
            Assert.Equal(CodexBridgeStatus.NotInstalled, this.New().Status);

        /// <summary>
        /// Where codex's hook runner spawns nothing — Windows, proven on hardware
        /// (docs/spike-windows-codex-hooks.md) — the installer writes NOTHING. Not the hooks
        /// file, not the launcher. Writing them would cost the user a /hooks trust prompt for
        /// keys that can never light, and leave a file behind that uninstall has to reason
        /// about. The rollout bridge carries state there instead.
        /// </summary>
        [Fact]
        public void Where_hooks_do_not_run_nothing_is_installed()
        {
            var b = new CodexStateBridge(this._home, this._sessions) { InstallsHooks = false };

            Assert.False(b.EnsureInstalled(Script));
            Assert.False(File.Exists(b.HooksFile));
            Assert.False(File.Exists(b.HookScript));
        }

        /// <summary>Current Codex supports command hooks on Windows as well as Unix.</summary>
        [Fact]
        public void The_default_follows_the_platform() =>
            Assert.True(new CodexStateBridge(this._home, this._sessions).InstallsHooks);

        [Fact]
        public void Install_writes_the_hooks_file_and_the_launcher()
        {
            var b = this.New();

            Assert.True(b.EnsureInstalled(Script));
            Assert.True(File.Exists(b.HooksFile));
            Assert.True(File.Exists(b.HookScript));
            Assert.Equal(Script, File.ReadAllText(b.HookScript));
        }

        /// <summary>
        /// Every event we rely on must be subscribed, and each must carry EXACTLY the command
        /// HookCommand builds, ending in its own event name — that argument is the only thing
        /// telling the hook which event it is handling. The command's per-OS SHAPE (exe verb on
        /// Windows, sh script elsewhere) is pinned separately in WindowsHookTests via the OS-free
        /// overload; this test is the wiring: the file contains what the builder built, on
        /// whichever OS the test runs.
        /// </summary>
        [Fact]
        public void Every_event_is_subscribed_and_passes_its_own_name()
        {
            var b = this.New();
            b.EnsureInstalled(Script);

            var hooks = JsonNode.Parse(File.ReadAllText(b.HooksFile))["hooks"].AsObject();

            foreach (var evt in CodexStateBridge.Events)
            {
                Assert.True(hooks.ContainsKey(evt), $"missing subscription: {evt}");
                var command = hooks[evt][0]["hooks"][0]["command"].GetValue<String>();
                Assert.Equal(b.HookCommand(evt), command);
                // POSIX: the event is the last argument. Windows: it is the last literal inside
                // the encoded launcher (#126); the OS-free test below pins that shape in full.
                if (command.StartsWith("powershell.exe ", StringComparison.Ordinal))
                {
                    Assert.Contains($" 'codex' '{evt}'; ", WindowsHookTests.DecodeLauncher(command));
                }
                else
                {
                    Assert.EndsWith(" " + evt, command);
                }
            }
        }

        [Fact]
        public void Windows_hooks_use_the_official_commandWindows_override()
        {
            var b = this.New();
            var hooks = JsonNode.Parse(b.BuildHooksJson(windows: true))["hooks"].AsObject();

            foreach (var evt in CodexStateBridge.Events)
            {
                var handler = hooks[evt][0]["hooks"][0];
                Assert.Equal(b.HookCommand(evt, windows: true), handler["command"].GetValue<String>());
                Assert.Equal(b.HookCommand(evt, windows: true), handler["commandWindows"].GetValue<String>());
            }
        }

        /// <summary>
        /// #126. Until 1.6.1 the Windows command ran the exe directly, so a quarantined or
        /// execution-blocked helper failed without a trace and the keys stayed Healthy. The
        /// command is now the same guarded launcher Claude Code uses, aimed at the codex-console
        /// root, so a missing or unlaunchable helper leaves the marker the plugin turns into
        /// Blocked. Pinned OS-free, since this is what every Windows user re-trusts once.
        /// </summary>
        [Fact]
        public void Windows_hook_command_is_the_guarded_launcher_aimed_at_the_codex_root()
        {
            var b = this.New();
            var command = b.HookCommand("PermissionRequest", windows: true);
            Assert.StartsWith("powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand ", command);
            var source = WindowsHookTests.DecodeLauncher(command);
            Assert.Contains("'" + b.HookExe.Replace("'", "''") + "'", source);
            Assert.Contains(" 'codex' 'PermissionRequest'; ", source);
            Assert.Contains("'codex-console', 'hook-health', '" + WindowsHookHealth.HealthDirectoryName(b.HookExe) + "'", source);
            Assert.DoesNotContain("'claude-console'", source);
            Assert.Contains("Write-HookFailure 'missing' 'helper'", source);
            Assert.Contains("Write-HookFailure 'launch-failed' 'helper'", source);
            Assert.Contains("Write-HookFailure 'nonzero-exit' 'helper'", source);
        }

        /// <summary>
        /// Codex trusts the command by hash. Two loads against the same install must produce the
        /// same bytes, or every load is a fresh trust prompt; and fourteen copies of the launcher
        /// must stay a size Codex reads without complaint.
        /// </summary>
        [Fact]
        public void The_installed_windows_command_depends_only_on_the_helper_path()
        {
            Assert.Equal(this.New().HookCommand("Stop", windows: true), this.New().HookCommand("Stop", windows: true));
            Assert.InRange(this.New().BuildHooksJson(windows: true).Length, 1, 80_000);
        }

        /// <summary>
        /// A home directory with a space in it is ordinary on macOS, and an unquoted path would
        /// split into two arguments — the hook would then run with the wrong event name, or not
        /// at all. The single-quoting is a POSIX-shape property, so it is asserted through the
        /// OS-free overload and holds from any machine; the file itself carries the running OS's
        /// shape, which the wiring assertion covers.
        /// </summary>
        [Fact]
        public void The_script_path_is_quoted_so_a_space_cannot_split_it()
        {
            var spaced = Path.Combine(this._home, "Application Support", ".codex");
            var b = new CodexStateBridge(spaced, this._sessions) { InstallsHooks = true };
            var command = JsonNode.Parse(b.BuildHooksJson(windows: false))
                ["hooks"]["Stop"][0]["hooks"][0]["command"].GetValue<String>();

            Assert.Equal(b.HookCommand("Stop", windows: false), command);
            Assert.Contains($"'{b.HookScript}'", b.HookCommand("Stop", windows: false));
        }

        /// <summary>
        /// The nagging guard. Installing twice must be a no-op the second time, because rewriting
        /// the file changes its hash and Codex asks the user to trust it again.
        /// </summary>
        [Fact]
        public void Installing_again_changes_nothing()
        {
            var b = this.New();

            Assert.True(b.EnsureInstalled(Script));
            Assert.False(b.EnsureInstalled(Script));
        }

        [Fact]
        public void Installed_but_never_fired_means_the_trust_grant_is_still_due()
        {
            var b = this.New();
            b.EnsureInstalled(Script);

            Assert.Equal(CodexBridgeStatus.AwaitingTrust, b.Status);
        }

        /// <summary>
        /// The only honest evidence the hook is trusted is that it has run. Trust state lives in
        /// Codex and is not ours to read.
        /// </summary>
        [Fact]
        public void A_single_arrived_event_proves_the_bridge_is_live()
        {
            var b = this.New();
            b.EnsureInstalled(Script);

            Directory.CreateDirectory(this._sessions);
            File.WriteAllText(
                Path.Combine(this._sessions, "ttys003.json"),
                "{\"schema\":1,\"transport\":\"hook\",\"event\":\"SessionStart\"}");

            Assert.Equal(CodexBridgeStatus.Active, b.Status);
        }

        /// <summary>
        /// The launcher does not stamp a transport — the rollout fallback is the only writer that
        /// does, so absence of the field means the hook wrote it. Demanding an explicit "hook"
        /// would have failed every envelope written before that field existed: on update each
        /// user's own working hook would read as untrusted, and an installation whose launcher
        /// rewrite failed would say "Run /hooks" for good while the hook kept running.
        /// </summary>
        [Fact]
        public void An_envelope_with_no_transport_field_is_the_hooks_and_proves_trust()
        {
            var b = this.New();
            b.EnsureInstalled(Script);

            Directory.CreateDirectory(this._sessions);
            File.WriteAllText(
                Path.Combine(this._sessions, "ttys003.json"),
                "{\"schema\":1,\"agent\":\"codex-cli\",\"event\":\"SessionStart\",\"ts\":1,\"payload\":null}");

            Assert.Equal(CodexBridgeStatus.Active, b.Status);
        }

        [Fact]
        public void An_event_from_before_the_current_launcher_does_not_falsely_prove_trust()
        {
            var b = this.New();
            b.EnsureInstalled(Script);

            Directory.CreateDirectory(this._sessions);
            var envelope = Path.Combine(this._sessions, "ttys003.json");
            File.WriteAllText(envelope, "{\"schema\":1,\"transport\":\"hook\",\"event\":\"SessionStart\"}");
            File.SetLastWriteTimeUtc(envelope, DateTime.UtcNow.AddMinutes(-5));

            // A changed launcher is a changed trusted program even though its stable command in
            // hooks.json remains identical. Until that launcher fires, the old envelope is stale.
            Assert.True(b.EnsureInstalled(Script + "\n# updated"));
            Assert.Equal(CodexBridgeStatus.AwaitingTrust, b.Status);

            File.WriteAllText(envelope, "{\"schema\":1,\"transport\":\"hook\",\"event\":\"Stop\"}");
            Assert.Equal(CodexBridgeStatus.Active, b.Status);
        }

        [Fact]
        public void Rollout_state_does_not_falsely_prove_hook_trust()
        {
            var b = this.New();
            b.EnsureInstalled(Script);

            Directory.CreateDirectory(this._sessions);
            File.WriteAllText(
                Path.Combine(this._sessions, "pid-1.json"),
                "{\"schema\":1,\"transport\":\"rollout\",\"event\":\"UserPromptSubmit\"}");

            Assert.Equal(CodexBridgeStatus.AwaitingTrust, b.Status);
        }

        [Theory]
        [InlineData("rollout")]
        [InlineData("rollout-code-mode")]
        public void Fresh_fallback_approval_cannot_prove_hook_trust(String transport)
        {
            var bridge = New();
            bridge.EnsureInstalled(Script);
            Directory.CreateDirectory(_sessions);
            var envelope = Path.Combine(_sessions, "pid-42.json");
            File.WriteAllText(envelope, System.Text.Json.JsonSerializer.Serialize(new
            {
                schema = 1, transport, @event = "PermissionRequest",
                payload = new { tool_name = "Bash", tool_input = new { command = "git push" } },
            }));
            File.SetLastWriteTimeUtc(envelope, DateTime.UtcNow.AddSeconds(2));
            Assert.Equal(CodexBridgeStatus.AwaitingTrust, bridge.Status);
            File.WriteAllText(envelope, "{\"schema\":1,\"transport\":\"hook\",\"event\":\"Stop\"}");
            Assert.Equal(CodexBridgeStatus.Active, bridge.Status);
            var timestamp = File.GetLastWriteTimeUtc(bridge.HooksFile);
            Assert.False(bridge.EnsureInstalled(Script));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(bridge.HooksFile));
            Assert.Equal(CodexBridgeStatus.Active, bridge.Status);
        }

        [Fact]
        public void A_hooks_file_we_did_not_write_is_left_completely_alone()
        {
            Directory.CreateDirectory(this._home);
            var theirs = "{\"description\":\"my own hooks\",\"hooks\":{\"Stop\":[]}}";
            var b = this.New();
            File.WriteAllText(b.HooksFile, theirs);

            Assert.Equal(CodexBridgeStatus.ForeignHooksFile, b.Status);
            Assert.False(b.EnsureInstalled(Script));
            Assert.Equal(theirs, File.ReadAllText(b.HooksFile));
        }

        /// <summary>
        /// Unparseable is not "junk we may replace" — far more likely a user's file mid-edit than
        /// anything of ours, and destroying it would be unrecoverable.
        /// </summary>
        [Fact]
        public void An_unreadable_hooks_file_is_also_left_alone()
        {
            Directory.CreateDirectory(this._home);
            var b = this.New();
            File.WriteAllText(b.HooksFile, "{ this is not json");

            Assert.Equal(CodexBridgeStatus.ForeignHooksFile, b.Status);
            Assert.False(b.EnsureInstalled(Script));
            Assert.Equal("{ this is not json", File.ReadAllText(b.HooksFile));
        }

        [Fact]
        public void Uninstall_removes_our_file_but_never_someone_elses()
        {
            var b = this.New();
            b.EnsureInstalled(Script);
            Assert.True(b.Uninstall());
            Assert.False(File.Exists(b.HooksFile));

            var theirs = "{\"description\":\"my own hooks\"}";
            File.WriteAllText(b.HooksFile, theirs);
            Assert.False(b.Uninstall());
            Assert.Equal(theirs, File.ReadAllText(b.HooksFile));
        }

        /// <summary>Payloads carry prompts and pending commands; the file must not be world-readable.</summary>
        [Fact]
        public void The_installed_files_are_owner_only()
        {
            if (OperatingSystem.IsWindows())
            {
                return;   // POSIX modes don't apply
            }

            var b = this.New();
            b.EnsureInstalled(Script);

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                         File.GetUnixFileMode(b.HooksFile));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                         File.GetUnixFileMode(b.HookScript));
        }

        /// <summary>
        /// The launcher must actually be IN the assembly. Nothing else fails loudly if it isn't:
        /// the plugin would load, show its keys, and quietly never install a hook — the same class
        /// of silent packaging bug as the missing application registration in 1.8.4.
        /// </summary>
        [Fact]
        public void The_launcher_is_embedded_in_the_assembly()
        {
            var script = CodexCliAdapter.HookScriptContents();

            Assert.False(String.IsNullOrWhiteSpace(script), "codex-hook.sh is not embedded — check the csproj EmbeddedResource");
            Assert.StartsWith("#!/bin/bash", script);
            Assert.Contains("CODEX_CONSOLE_IPC_ROOT", script);
            Assert.Contains("codex-cli", script);
        }

        /// <summary>The embedded launcher is what actually gets installed, byte for byte.</summary>
        [Fact]
        public void Install_writes_the_embedded_launcher_verbatim()
        {
            var b = this.New();
            var embedded = CodexCliAdapter.HookScriptContents();

            b.EnsureInstalled(embedded);

            Assert.Equal(embedded, File.ReadAllText(b.HookScript));
        }

        /// <summary>Runs during plugin load; a broken environment must not take the plugin down.</summary>
        [Fact]
        public void Install_never_throws_on_a_bad_environment()
        {
            var b = new CodexStateBridge("/proc/nonexistent/definitely-not-writable", this._sessions);

            var ex = Record.Exception(() => b.EnsureInstalled(Script));
            Assert.Null(ex);
        }
    }
}
