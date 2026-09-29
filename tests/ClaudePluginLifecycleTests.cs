namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Security.AccessControl;
    using System.Security.Principal;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;

    using Loupedeck.ClaudeConsolePlugin.Platform;
    using Xunit;

    public class ClaudePluginLifecycleTests
    {
        private static JsonObject Read(TempHome home) => JsonNode.Parse(home.ReadSettings(),
            documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }).AsObject();

        private static String AssemblyPath(TempHome home) => Path.Combine(Path.GetDirectoryName(home.HookExe), "ClaudeConsolePlugin.dll");

        private static void Wire(TempHome home, Boolean full = true)
        {
            Directory.CreateDirectory(home.RuntimeHome);
            var root = new JsonObject { ["model"] = "opus", ["hooks"] = new JsonObject() };
            var hooks = (JsonObject)root["hooks"];
            foreach (var spec in BridgeWiring.HookSpecs.Take(full ? 5 : 1))
            {
                BridgeManager.EnsureHook(hooks, spec.Event, spec.Matcher,
                    BridgeWiring.ActivityCommand(true, home.HookExe, spec.State));
            }
            root["statusLine"] = new JsonObject
            {
                ["type"] = "command", ["padding"] = 2,
                ["command"] = BridgeWiring.StatuslineCommand(true, home.HookExe),
            };
            home.WriteSettings(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static Int32 HookCount(JsonObject root) => root["hooks"] is JsonObject hooks
            ? hooks.Sum(kv => kv.Value is JsonArray entries
                ? entries.OfType<JsonObject>().Sum(e => e["hooks"] is JsonArray inner
                    ? inner.OfType<JsonObject>().Count(h => BridgeWiring.IsOurHook(BridgeWiring.Str(h["command"]))) : 0) : 0) : 0;

        [Fact]
        public void Uninstall_preserves_foreign_source_and_restores_the_chained_status_line()
        {
            using var home = new TempHome();
            Wire(home);
            var root = Read(home);
            ((JsonArray)root["hooks"]["Stop"][0]["hooks"]).Add(new JsonObject { ["type"] = "command", ["command"] = "echo mine" });
            var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                .Replace("\"model\": \"opus\",", "\"model\": \"opus\", // keep this comment\n  \"custom\": {\"unicode\":\"தமிழ்\",\"list\":[1,2,],},");
            File.WriteAllText(home.Settings, text.Replace("\n", "\r\n") + "\r\n", new UTF8Encoding(true));
            File.WriteAllText(home.ChainFile, "echo my-status");
            var before = File.ReadAllBytes(home.Settings);
            var lifecycle = new ClaudePluginLifecycle(home.Dir);

            Assert.True(lifecycle.Uninstall());

            Assert.Equal(0, HookCount(Read(home)));
            Assert.Equal("echo my-status", Read(home)["statusLine"]["command"].GetValue<String>());
            Assert.Equal(2, Read(home)["statusLine"]["padding"].GetValue<Int32>());
            Assert.Contains("echo mine", home.ReadSettings());
            Assert.Contains("// keep this comment\r\n", home.ReadSettings());
            Assert.Contains("\"custom\": {\"unicode\":\"தமிழ்\",\"list\":[1,2,],}", home.ReadSettings());
            Assert.Equal(new Byte[] { 0xef, 0xbb, 0xbf }, File.ReadAllBytes(home.Settings).Take(3));
            Assert.Equal(before, File.ReadAllBytes(home.Backup));
            Assert.False(File.Exists(home.Marker));
            Assert.DoesNotContain("echo mine", File.ReadAllText(lifecycle.ReceiptFile));
            Assert.Contains("owned wiring removed", File.ReadAllText(lifecycle.LogFile));
            Assert.Empty(home.LeftoverTemps());
        }

        [Fact]
        public void Repeated_uninstall_preserves_the_receipt_and_backup()
        {
            using var home = new TempHome();
            Wire(home);
            var lifecycle = new ClaudePluginLifecycle(home.Dir);
            Assert.True(lifecycle.Uninstall());
            var receipt = File.ReadAllText(lifecycle.ReceiptFile);
            var backup = File.ReadAllBytes(home.Backup);
            var after = home.ReadSettings();
            Assert.True(lifecycle.Uninstall());
            Assert.Equal(after, home.ReadSettings());
            Assert.Equal(receipt, File.ReadAllText(lifecycle.ReceiptFile));
            Assert.Equal(backup, File.ReadAllBytes(home.Backup));
        }

        [Fact]
        public void Fresh_install_and_uninstall_do_not_create_settings_or_a_restore_receipt()
        {
            using var home = new TempHome();
            var lifecycle = new ClaudePluginLifecycle(home.Dir);
            Assert.True(lifecycle.Restore(null));
            Assert.True(lifecycle.Uninstall());
            Assert.False(File.Exists(home.Settings));
            Assert.False(File.Exists(home.Backup));
            Assert.False(File.Exists(lifecycle.ReceiptFile));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Replacement_restores_exactly_the_previous_full_or_partial_wiring(Boolean full)
        {
            using var home = new TempHome();
            Wire(home, full);
            File.WriteAllText(home.ChainFile, "echo my-status");
            var lifecycle = new ClaudePluginLifecycle(home.Dir);
            var before = Read(home);
            Assert.True(lifecycle.Uninstall());
            var unwired = Read(home);
            unwired["newUserSetting"] = "keep";
            home.WriteSettings(unwired.ToJsonString());

            // Fresh object, just like the host's new Install context.
            Assert.True(new ClaudePluginLifecycle(home.Dir).Restore(AssemblyPath(home)));
            before["newUserSetting"] = "keep";
            Assert.True(JsonNode.DeepEquals(before, Read(home)));
            Assert.Equal("echo my-status", File.ReadAllText(home.ChainFile));
            Assert.False(File.Exists(lifecycle.ReceiptFile));
            var restored = home.ReadSettings();
            Assert.True(lifecycle.Restore(AssemblyPath(home)));
            Assert.Equal(restored, home.ReadSettings());
        }

        [Fact]
        public void Upgrade_migrates_the_helper_path_and_merges_mixed_hook_groups_without_duplicates()
        {
            using var home = new TempHome();
            Wire(home);
            var root = Read(home);
            ((JsonArray)root["hooks"]["Stop"][0]["hooks"]).Add(new JsonObject { ["type"] = "command", ["command"] = "echo mine" });
            home.WriteSettings(root.ToJsonString());
            var lifecycle = new ClaudePluginLifecycle(home.Dir);
            Assert.True(lifecycle.Uninstall());
            var next = Path.Combine(home.Dir, "new package's bin");
            Directory.CreateDirectory(next);
            var newExe = Path.Combine(next, "claude-console-hook.exe");
            File.WriteAllText(newExe, String.Empty);
            Assert.True(lifecycle.Restore(Path.Combine(next, "ClaudeConsolePlugin.dll")));
            var restored = Read(home);
            Assert.Equal(5, HookCount(restored));
            Assert.Single((JsonArray)restored["hooks"]["Stop"]);
            Assert.Equal(2, ((JsonArray)restored["hooks"]["Stop"][0]["hooks"]).Count);
            Assert.Equal(BridgeWiring.StatuslineCommand(true, newExe), restored["statusLine"]["command"].GetValue<String>());
        }

        [Fact]
        public void A_new_user_status_line_wins_over_the_saved_one()
        {
            using var home = new TempHome();
            Wire(home);
            var lifecycle = new ClaudePluginLifecycle(home.Dir);
            Assert.True(lifecycle.Uninstall());
            var root = Read(home);
            root["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = "echo newer" };
            home.WriteSettings(root.ToJsonString());
            Assert.True(lifecycle.Restore(AssemblyPath(home)));
            Assert.Equal("echo newer", Read(home)["statusLine"]["command"].GetValue<String>());
            Assert.Equal(5, HookCount(Read(home)));
            Assert.Contains("newer user status line preserved", File.ReadAllText(lifecycle.LogFile));
        }

        [Fact]
        public void Explicit_off_wins_over_a_previous_restore_receipt()
        {
            using var home = new TempHome();
            Wire(home);
            var lifecycle = new ClaudePluginLifecycle(home.Dir);
            Assert.True(lifecycle.Uninstall());
            File.WriteAllText(home.Marker, String.Empty);
            var before = home.ReadSettings();
            Assert.True(lifecycle.Restore(AssemblyPath(home)));
            Assert.Equal(before, home.ReadSettings());
            Assert.False(File.Exists(lifecycle.ReceiptFile));
            Assert.True(File.Exists(home.Marker));
        }

        [Theory]
        [InlineData("{ invalid")]
        [InlineData("[1,2]")]
        public void Unreadable_settings_fail_without_writes_and_leave_a_diagnostic(String text)
        {
            using var home = new TempHome();
            home.WriteSettings(text);
            var lifecycle = new ClaudePluginLifecycle(home.Dir);
            Assert.False(lifecycle.Uninstall());
            Assert.Equal(text, home.ReadSettings());
            Assert.False(File.Exists(home.Backup));
            Assert.Contains("FAILED", File.ReadAllText(lifecycle.LogFile));
        }

        [Fact]
        public void Backup_failure_prevents_settings_replacement_but_preserves_recovery_data()
        {
            using var home = new TempHome();
            Wire(home);
            Directory.CreateDirectory(home.Backup);
            var before = home.ReadSettings();
            var lifecycle = new ClaudePluginLifecycle(home.Dir);
            Assert.False(lifecycle.Uninstall());
            Assert.Equal(before, home.ReadSettings());
            Assert.True(File.Exists(lifecycle.ReceiptFile));
            Assert.Empty(home.LeftoverTemps());
        }

        [Fact]
        public void Receipt_failure_prevents_settings_replacement()
        {
            using var home = new TempHome();
            Wire(home);
            var lifecycle = new ClaudePluginLifecycle(home.Dir);
            Directory.CreateDirectory(lifecycle.ReceiptFile);
            var before = home.ReadSettings();
            Assert.False(lifecycle.Uninstall());
            Assert.Equal(before, home.ReadSettings());
            Assert.False(File.Exists(home.Backup));
            Assert.Empty(Directory.GetFiles(home.RuntimeHome, "*.tmp"));
        }

        [Fact]
        public void Lock_held_past_the_wait_fails_and_the_same_cleanup_succeeds_after_release()
        {
            using var home = new TempHome();
            Wire(home);
            var store = new ClaudeSettingsStore(home.Dir);
            var lifecycle = new ClaudePluginLifecycle(home.Dir, lockWait: TimeSpan.Zero);
            var before = home.ReadSettings();
            using (store.AcquireLock())
            {
                Assert.False(lifecycle.Uninstall());
                Assert.False(BridgeManager.RewriteSettings(root => { root["lost"] = true; return true; }, out _));
            }
            Assert.Equal(before, home.ReadSettings());
            Assert.True(lifecycle.Uninstall());
        }

        [Fact]
        public async Task Uninstall_waits_out_a_briefly_held_lock_instead_of_failing()
        {
            // The host deletes the package whatever Uninstall returns: one try, so a writer that
            // is mid-edit (an Enable press, the macOS unwire script) must be waited for.
            using var home = new TempHome();
            Wire(home);
            var held = new ClaudeSettingsStore(home.Dir).AcquireLock();
            var release = Task.Delay(300).ContinueWith(_ => held.Dispose());
            Assert.True(new ClaudePluginLifecycle(home.Dir).Uninstall());
            await release;
            Assert.Equal(0, HookCount(Read(home)));
        }

        [Theory]
        [InlineData(11)]   // a real uninstall, reinstalled later
        [InlineData(-5)]   // a clock that jumped: not a receipt to trust
        public void Reinstall_outside_the_replacement_window_leaves_live_status_off(Int32 minutesLater)
        {
            using var home = new TempHome();
            Wire(home);
            var uninstalledAt = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
            Assert.True(new ClaudePluginLifecycle(home.Dir, () => uninstalledAt).Uninstall());
            var before = home.ReadSettings();

            var install = new ClaudePluginLifecycle(home.Dir, () => uninstalledAt.AddMinutes(minutesLater));
            Assert.True(install.Restore(AssemblyPath(home)));

            // A fresh install is not consent (#31): nothing is written, and nothing is left to act later.
            Assert.Equal(before, home.ReadSettings());
            Assert.Equal(0, HookCount(Read(home)));
            Assert.False(File.Exists(install.ReceiptFile));
            Assert.False(File.Exists(home.Marker));
            Assert.Contains("earlier uninstall", File.ReadAllText(install.LogFile));
        }

        [Fact]
        public void Replacement_inside_the_window_restores_the_setup()
        {
            using var home = new TempHome();
            Wire(home);
            var uninstalledAt = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
            Assert.True(new ClaudePluginLifecycle(home.Dir, () => uninstalledAt).Uninstall());
            var install = new ClaudePluginLifecycle(home.Dir,
                () => uninstalledAt + ClaudePluginLifecycle.RestoreWindow - TimeSpan.FromSeconds(1));
            Assert.True(install.Restore(AssemblyPath(home)));
            Assert.Equal(LiveStatusWiring.Enabled, BridgeWiring.Inspect(Read(home)));
        }

        [Fact]
        public void A_receipt_without_a_timestamp_is_dropped_not_restored()
        {
            // The 2.3.1 test candidate wrote receipts without one.
            using var home = new TempHome();
            Wire(home);
            var lifecycle = new ClaudePluginLifecycle(home.Dir);
            Assert.True(lifecycle.Uninstall());
            var receipt = JsonNode.Parse(File.ReadAllText(lifecycle.ReceiptFile)).AsObject();
            receipt.Remove("writtenAt");
            File.WriteAllText(lifecycle.ReceiptFile, receipt.ToJsonString());
            Assert.True(lifecycle.Restore(AssemblyPath(home)));
            Assert.Equal(0, HookCount(Read(home)));
            Assert.False(File.Exists(lifecycle.ReceiptFile));
        }

        [Fact]
        public async Task A_separate_windows_process_holding_the_lock_blocks_cleanup()
        {
            if (!OperatingSystem.IsWindows()) { return; }
            using var home = new TempHome();
            Wire(home);
            var lockPath = new ClaudeSettingsStore(home.Dir).LockFile.Replace("'", "''");
            var script = "$ErrorActionPreference='Stop'; $f=[IO.File]::Open('" + lockPath +
                "',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); " +
                "try { [Console]::WriteLine('LOCKED'); [Console]::In.ReadLine() | Out-Null } finally { $f.Dispose() }";
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true,
            };
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) { start.ArgumentList.Add(arg); }
            using var child = Process.Start(start);
            try
            {
                Assert.Equal("LOCKED", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
                var before = home.ReadSettings();
                Assert.False(new ClaudePluginLifecycle(home.Dir).Uninstall());
                Assert.Equal(before, home.ReadSettings());
                await child.StandardInput.WriteLineAsync("release");
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(0, child.ExitCode);
                Assert.True(new ClaudePluginLifecycle(home.Dir).Uninstall());
            }
            finally
            {
                if (!child.HasExited) { child.Kill(entireProcessTree: true); }
            }
        }

        [Fact]
        public void Replacing_settings_preserves_its_windows_access_control()
        {
            if (!OperatingSystem.IsWindows()) { return; }
            using var home = new TempHome();
            Wire(home);
            var file = new FileInfo(home.Settings);
            var access = new FileSecurity();
            access.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            access.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,
                FileSystemRights.FullControl, AccessControlType.Allow));
            file.SetAccessControl(access);
            var before = file.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);

            Assert.True(new ClaudePluginLifecycle(home.Dir).Uninstall());

            Assert.Equal(before, file.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
            Assert.Equal(0, HookCount(Read(home)));
        }

        [Fact]
        public void Locked_chain_fails_before_removing_the_status_line()
        {
            using var home = new TempHome();
            Wire(home);
            File.WriteAllText(home.ChainFile, "echo mine");
            var before = home.ReadSettings();
            using var locked = new FileStream(home.ChainFile, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.False(new ClaudePluginLifecycle(home.Dir).Uninstall());
            Assert.Equal(before, home.ReadSettings());
        }

        [Fact]
        public void Failed_cleanup_then_install_does_not_duplicate_existing_hooks()
        {
            if (!OperatingSystem.IsWindows()) { return; } // Windows denies rename without FILE_SHARE_DELETE.
            using var home = new TempHome();
            Wire(home);
            var before = home.ReadSettings();
            var lifecycle = new ClaudePluginLifecycle(home.Dir);
            using (new FileStream(home.Settings, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.False(lifecycle.Uninstall());
            }
            Assert.Equal(before, home.ReadSettings());
            Assert.True(File.Exists(lifecycle.ReceiptFile));
            Assert.True(lifecycle.Restore(AssemblyPath(home)));
            Assert.Equal(5, HookCount(Read(home)));
            Assert.Equal(before, home.ReadSettings());
        }

        [Fact]
        public void Missing_helper_defers_restoration_until_a_later_load_can_retry()
        {
            using var home = new TempHome();
            Wire(home);
            var lifecycle = new ClaudePluginLifecycle(home.Dir);
            Assert.True(lifecycle.Uninstall());
            var before = home.ReadSettings();
            Assert.False(lifecycle.Restore(Path.Combine(home.Dir, "absent", "plugin.dll")));
            Assert.Equal(before, home.ReadSettings());
            Assert.True(File.Exists(lifecycle.ReceiptFile));
            Assert.True(lifecycle.Restore(AssemblyPath(home)));
            Assert.Equal(LiveStatusWiring.Enabled, BridgeWiring.Inspect(Read(home)));
        }

        [Fact]
        public void Receipt_cannot_restore_foreign_hooks_or_roll_back_other_settings()
        {
            using var home = new TempHome();
            Wire(home);
            var lifecycle = new ClaudePluginLifecycle(home.Dir);
            Assert.True(lifecycle.Uninstall());
            var receipt = JsonNode.Parse(File.ReadAllText(lifecycle.ReceiptFile));
            receipt["wiring"]["model"] = "stale";
            File.WriteAllText(lifecycle.ReceiptFile, receipt.ToJsonString());
            var before = home.ReadSettings();
            Assert.False(lifecycle.Restore(AssemblyPath(home)));
            Assert.Equal(before, home.ReadSettings());
            Assert.True(File.Exists(lifecycle.ReceiptFile));
        }

        [Fact]
        public void Actual_sdk_uninstall_override_works_without_Load_and_Unload_never_unwires()
        {
            if (!OperatingSystem.IsWindows()) { return; }
            using var home = new TempHome();
            Wire(home);
            var before = home.ReadSettings();
            // The callback must not depend on an initialized SDK context or a prior Load.
            var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(global::Loupedeck.ClaudeConsolePlugin.ClaudeConsolePlugin));
            plugin.Unload();
            Assert.Equal(before, home.ReadSettings());
            Assert.True(plugin.Uninstall());
            Assert.Equal(0, HookCount(Read(home)));
            Assert.True(plugin.Uninstall());
        }
    }
}
