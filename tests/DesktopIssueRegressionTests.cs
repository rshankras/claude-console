namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration;
    using Xunit;

    public class DesktopIssueRegressionTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Repeated_workflow_loads_never_rewrite_current_stock(Boolean chat)
        {
            using var home = new TempHome(); var path = Path.Combine(home.Dir, "workflows.json");
            File.WriteAllText(path, JsonSerializer.Serialize(chat ? DesktopWorkflowMigration.ChatGptDefaults : DesktopWorkflowMigration.CodexDefaults));
            void Load() { if (chat) DesktopWorkflowCommand.LoadChatGptWorkflows(path).ToArray(); else DesktopWorkflowCommand.LoadWorkflows(path).ToArray(); }
            Load(); var bytes = File.ReadAllBytes(path); var stamp = DateTime.UtcNow.AddDays(-2);
            File.SetLastWriteTimeUtc(path, stamp); stamp = File.GetLastWriteTimeUtc(path);
            for (var i = 0; i < 5; i++) Load();
            Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
            // A fresh path bypasses the cache, like the first load in a new plugin process.
            var fresh = Path.Combine(home.Dir, "fresh.json"); File.WriteAllBytes(fresh, bytes); File.SetLastWriteTimeUtc(fresh, stamp);
            if (chat) DesktopWorkflowCommand.LoadChatGptWorkflows(fresh).ToArray(); else DesktopWorkflowCommand.LoadWorkflows(fresh).ToArray();
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(fresh));
        }
        [Fact]
        public void Cached_workflows_reload_after_a_user_edits_the_file()
        {
            using var home = new TempHome(); var path = Path.Combine(home.Dir, "workflows.json");
            DesktopWorkflowCommand.LoadWorkflows(path).ToArray();
            var doc = JsonNode.Parse(File.ReadAllText(path)).AsArray(); doc[0]["Label"] = "My Review";
            File.WriteAllText(path, doc.ToJsonString());
            Assert.Equal("My Review", DesktopWorkflowCommand.LoadWorkflows(path).First().Label);
        }
        [Fact]
        public void Flow_migration_preserves_custom_source_and_scope()
        {
            using var home = new TempHome(); var path = Path.Combine(home.Dir, "workflows.json");
            var doc = JsonSerializer.SerializeToNode(DesktopWorkflowMigration.ChatGptDefaults).AsArray();
            doc[0]["Scope"] = "CUSTOM"; doc[0]["SourcePrompt"] = "Keep my wording";
            File.WriteAllText(path, doc.ToJsonString());
            var slot = DesktopWorkflowCommand.LoadChatGptWorkflows(path).First();
            Assert.Equal("CUSTOM", slot.Scope); Assert.Equal("Keep my wording", slot.SourcePrompt);
        }
        [Fact]
        public void Migration_copies_owned_settings_once_without_changing_other_products_or_existing_files()
        {
            using var home = new TempHome(); Directory.CreateDirectory(home.RuntimeHome);
            File.WriteAllText(Path.Combine(home.RuntimeHome, "desktop-workflows.json"), "legacy");
            File.WriteAllText(Path.Combine(home.RuntimeHome, "prompts.json"), "terminal");
            var target = ProductRuntime.For(home.Dir, "vizhi-desktop", false);
            Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "desktop-workflows.json"), "current");
            ProductRuntime.MigrateDesktop(home.Dir, target);
            Assert.Equal("current", File.ReadAllText(Path.Combine(target, "desktop-workflows.json")));
            Assert.Equal("legacy", File.ReadAllText(Path.Combine(home.RuntimeHome, "desktop-workflows.json")));
            Assert.False(File.Exists(Path.Combine(target, "prompts.json")));
            File.Delete(Path.Combine(target, "desktop-workflows.json")); ProductRuntime.MigrateDesktop(home.Dir, target);
            Assert.False(File.Exists(Path.Combine(target, "desktop-workflows.json")));
            Assert.Contains("AppData", ProductRuntime.For(home.Dir, "vizhi-desktop", true));
        }
        [Fact]
        public void Failed_send_retains_and_copies_then_retry_only_inserts()
        {
            using var home = new TempHome(); var fake = new DesktopCommandRig.Automation();
            var path = Path.Combine(home.Dir, "pending.json");
            var recovery = new DesktopDraftRecovery(fake, path);
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge()); String copy = null;
            bridge.TranscriptSink = (_, _) => "app-not-running";
            bridge.DraftRecoverySink = text => recovery.Retain(text, "app-not-running");
            bridge.FailedSendCopy = text => copy = text;
            bridge.DeliverToSink("Keep தமிழ்", true);
            Assert.Equal("Keep தமிழ்", copy);
            var restored = new DesktopDraftRecovery(fake, path);
            Assert.True(restored.Pending); Assert.Equal("OPEN APP · TAP TO RETRY", restored.RetryHint);
            Assert.Equal("Draft Ready", restored.Insert());
            Assert.False(Assert.Single(fake.Calls, x => x.Name == "write").Send);
            Assert.False(File.Exists(path));
        }
        [Fact]
        public void Recording_cap_updates_the_face_and_an_old_watcher_cannot_change_a_new_capture()
        {
            var capture = new VoiceCaptureState(); var now = DateTime.UtcNow;
            capture.Press(VoiceIntent.DesktopDraft, now); var first = capture.CaptureId;
            capture.MarkCapped(first);
            Assert.Equal("Recording ended", DesktopDictationFace.For(VoiceIntent.DesktopDraft, capture, null, "voice").Label);
            capture.Finish(); capture.Press(VoiceIntent.DesktopDraft, now);
            capture.MarkCapped(first); Assert.False(capture.Capped);
        }
        [Fact]
        public void Finished_transcript_survives_a_press_after_the_stale_timeout()
        {
            var state = new VoiceCaptureState { HasPendingTranscript = () => true };
            var now = DateTime.UtcNow; state.Press(VoiceIntent.DesktopDraft, now);
            Assert.Equal(VoiceAction.Stop, state.Press(VoiceIntent.DesktopDraft, now + VoiceCaptureState.StaleAfter + TimeSpan.FromSeconds(5)).Action);
        }
        [Fact]
        public void Model_reuse_checks_the_hash_and_copies_without_mutating_source()
        {
            using var home = new TempHome(); var source = Path.Combine(home.Dir, "model"); var dest = Path.Combine(home.Dir, "new", "model");
            File.WriteAllText(source, "verified model"); var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            Assert.False(BridgeManager.ReuseVoiceModel(source, dest, "bad")); Assert.False(File.Exists(dest));
            Assert.True(BridgeManager.ReuseVoiceModel(source, dest, hash)); File.WriteAllText(dest, "changed");
            Assert.Equal("verified model", File.ReadAllText(source));
        }
        [Theory]
        [InlineData(5, true)]
        [InlineData(11, false)]
        public void Reinstall_restores_owned_registration_only_during_the_replacement_window(Int32 minutes, Boolean restored)
        {
            using var home = new TempHome(); var runtime = Path.Combine(home.Dir, "Desktop"); var apps = Path.Combine(home.Dir, "apps");
            var app = Path.Combine(apps, "70", "@_vizhidesktop"); Directory.CreateDirectory(app);
            File.WriteAllText(Path.Combine(app, "ApplicationInfo.json"), "{\"selfRegisteredBy\":\"VizhiDesktop\"}");
            File.WriteAllText(Path.Combine(app, ".vizhi-packaged-profile"), "revision-deleted-by-user");
            var now = DateTime.UtcNow;
            Assert.True(DesktopUninstall.Clean(runtime, Path.Combine(home.Dir, "ipc"), apps, now));
            DesktopUninstall.RestoreRegistration(runtime, apps, now.AddMinutes(minutes));
            Assert.Equal(restored, Directory.Exists(app));
            if (restored) {
                Assert.Equal("revision-deleted-by-user", File.ReadAllText(Path.Combine(app, ".vizhi-packaged-profile")));
                Assert.False(Directory.Exists(Path.Combine(app, "Profiles")));
            }
        }
        [Fact]
        public void Uninstall_removes_runtime_audio_and_owned_registration_preserving_settings_and_neighbors()
        {
            using var home = new TempHome(); var runtime = Path.Combine(home.Dir, "Desktop"); var ipc = Path.Combine(home.Dir, "ipc"); var apps = Path.Combine(home.Dir, "apps");
            Directory.CreateDirectory(runtime); Directory.CreateDirectory(ipc);
            File.WriteAllText(Path.Combine(runtime, "desktop-workflows.json"), "custom");
            File.WriteAllText(Path.Combine(runtime, "pending-draft.json"), "private");
            File.WriteAllText(Path.Combine(ipc, "capture.wav"), "audio");
            var owned = Path.Combine(apps, "70", "@_vizhidesktop"); Directory.CreateDirectory(owned);
            File.WriteAllText(Path.Combine(owned, "ApplicationInfo.json"), "{\"selfRegisteredBy\":\"VizhiDesktop\"}");
            Directory.CreateDirectory(home.RuntimeHome); File.WriteAllText(Path.Combine(home.RuntimeHome, "model"), "neighbor");
            Assert.True(DesktopUninstall.Clean(runtime, ipc, apps));
            Assert.False(Directory.Exists(runtime)); Assert.False(Directory.Exists(ipc)); Assert.False(Directory.Exists(owned));
            DesktopUninstall.RestoreSettings(runtime);
            Assert.Equal("custom", File.ReadAllText(Path.Combine(runtime, "desktop-workflows.json")));
            Assert.False(File.Exists(Path.Combine(runtime, "pending-draft.json")));
            Assert.Equal("neighbor", File.ReadAllText(Path.Combine(home.RuntimeHome, "model")));
        }
    }
}
