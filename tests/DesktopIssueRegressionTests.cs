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
            // A fresh path, like the first load in a new plugin process: the migrations themselves
            // must be idempotent — there is no cache to hide a rewrite behind.
            var fresh = Path.Combine(home.Dir, "fresh.json"); File.WriteAllBytes(fresh, bytes); File.SetLastWriteTimeUtc(fresh, stamp);
            if (chat) DesktopWorkflowCommand.LoadChatGptWorkflows(fresh).ToArray(); else DesktopWorkflowCommand.LoadWorkflows(fresh).ToArray();
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(fresh));
        }
        [Fact]
        public void Workflows_reload_after_a_user_edits_the_file()
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
            // The dictation is parked for an upgrade, not deleted; nothing else is.
            Assert.True(File.Exists(Path.Combine(DesktopUninstall.RuntimeCache(runtime), DesktopUninstall.PendingDraftFile)));
        }

        // ---- review follow-up (1 Oct): the fixes the first cut needed ----------------------------

        [Theory]
        [InlineData(5, true)]
        [InlineData(11, false)]
        public void Upgrade_keeps_the_unsent_dictation_but_never_the_speech_model(Int32 minutes, Boolean restored)
        {
            using var home = new TempHome(); var runtime = Path.Combine(home.Dir, "Desktop");
            var model = Path.Combine(runtime, "whisper", "ggml-base.en.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(model)); File.WriteAllText(model, "148 MB");
            File.WriteAllText(Path.Combine(runtime, DesktopUninstall.PendingDraftFile), "[\"words\",null]");
            var now = DateTime.UtcNow;
            Assert.True(DesktopUninstall.Clean(runtime, Path.Combine(home.Dir, "ipc"), Path.Combine(home.Dir, "apps"), now));
            Assert.False(Directory.Exists(runtime));                                   // the model went with it
            Assert.True(Directory.Exists(DesktopUninstall.RuntimeCache(runtime)));

            DesktopUninstall.RestoreRuntimeCache(runtime, now.AddMinutes(minutes));

            Assert.False(File.Exists(model));
            Assert.Equal(restored, File.Exists(Path.Combine(runtime, DesktopUninstall.PendingDraftFile)));
            Assert.False(Directory.Exists(DesktopUninstall.RuntimeCache(runtime)));   // consumed or discarded, never left
        }

        [Fact]
        public void Uninstall_with_no_dictation_parks_nothing()
        {
            using var home = new TempHome(); var runtime = Path.Combine(home.Dir, "Desktop");
            Directory.CreateDirectory(runtime); File.WriteAllText(Path.Combine(runtime, "desktop-workflows.json"), "custom");
            Assert.True(DesktopUninstall.Clean(runtime, Path.Combine(home.Dir, "ipc"), Path.Combine(home.Dir, "apps")));
            Assert.False(Directory.Exists(DesktopUninstall.RuntimeCache(runtime)));
        }

        [Fact]
        public void The_desktop_runtime_home_is_the_SDK_plugin_data_directory()
        {
            using var home = new TempHome();
            Assert.Equal(Path.Combine(home.Dir, "Library/Application Support", "Logi", "LogiPluginService", "PluginData", "VizhiDesktop"),
                ProductRuntime.For(home.Dir, "vizhi-desktop", false));
            Assert.Equal(Path.Combine(home.Dir, "AppData/Local", "Logi", "LogiPluginService", "PluginData", "VizhiDesktop"),
                ProductRuntime.For(home.Dir, "vizhi-desktop", true));
            // The terminal products stay beside their agent's configuration.
            Assert.Equal(Path.Combine(home.Dir, ".claude", "claude-console"), ProductRuntime.For(home.Dir, "claude-console", false));
            Assert.Equal(Path.Combine(home.Dir, ".codex", "vizhi-runtime"), ProductRuntime.For(home.Dir, "codex-console", false));
        }

        [Fact]
        public void A_registration_receipt_that_cannot_be_restored_is_discarded_without_throwing()
        {
            using var home = new TempHome(); var runtime = Path.Combine(home.Dir, "Desktop"); var apps = Path.Combine(home.Dir, "apps");
            var backup = Path.Combine(DesktopUninstall.RegistrationReceipt(runtime), "70", "@_vizhidesktop");
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(DesktopUninstall.RegistrationReceipt(runtime), "written-at"), DateTime.UtcNow.ToString("O"));
            File.WriteAllText(Path.Combine(home.Dir, "elsewhere.json"), "{}");
            try { File.CreateSymbolicLink(Path.Combine(backup, "ApplicationInfo.json"), Path.Combine(home.Dir, "elsewhere.json")); }
            catch (Exception) { return; }   // no symlink privilege here (Windows without developer mode): nothing to prove

            Assert.False(DesktopUninstall.TryRestoreRegistration(runtime, apps));

            Assert.False(Directory.Exists(DesktopUninstall.RegistrationReceipt(runtime)));   // not retried on every load
            Assert.False(Directory.Exists(Path.Combine(apps, "70", "@_vizhidesktop")));
        }

        [Theory]
        [InlineData(0, 20)]
        [InlineData(60, 50)]
        [InlineData(180, 110)]
        [InlineData(400, 110)]
        public void Transcript_wait_grows_with_the_recording_and_the_stale_window_covers_it(Double recorded, Double expected)
        {
            Assert.Equal(expected, VoiceCaptureState.TranscriptWaitSeconds(recorded));
            Assert.True(VoiceCaptureState.StaleAfter.TotalSeconds >
                VoiceCaptureState.RecordingCapSeconds + VoiceCaptureState.TranscriptWaitSeconds(VoiceCaptureState.RecordingCapSeconds));
        }

        [Fact]
        public void A_stop_records_how_long_the_capture_ran()
        {
            var voice = new VoiceCaptureState(); var t0 = DateTime.UnixEpoch;
            voice.Press(VoiceIntent.DesktopDraft, t0);
            Assert.Equal(VoiceAction.Stop, voice.Press(VoiceIntent.DesktopDraft, t0.AddSeconds(75)).Action);
            Assert.Equal(75, voice.LastRecordedSeconds, 3);
            voice.Finish();
            voice.Press(VoiceIntent.Desktop, t0);
            Assert.Equal(VoiceAction.Stop, voice.StopIfCapturing(VoiceIntent.Desktop, t0.AddSeconds(12)));
            Assert.Equal(12, voice.LastRecordedSeconds, 3);
        }

        [Fact]
        public void The_transcript_probe_runs_only_for_a_capture_that_looks_dead()
        {
            var probes = 0;
            var voice = new VoiceCaptureState { HasPendingTranscript = () => { probes++; return false; } };
            var t0 = DateTime.UnixEpoch;
            voice.Press(VoiceIntent.DesktopDraft, t0);
            voice.Press(VoiceIntent.DesktopDraft, t0.AddSeconds(30));              // an ordinary stop: no disk access
            Assert.Equal(0, probes);
            voice.Finish(); voice.Press(VoiceIntent.DesktopDraft, t0);
            Assert.Equal(VoiceAction.Start, voice.Press(VoiceIntent.DesktopDraft, t0 + VoiceCaptureState.StaleAfter + TimeSpan.FromSeconds(1)).Action);
            Assert.Equal(1, probes);
        }

        [Theory]
        [InlineData("DesktopDraft", true)]
        [InlineData("Desktop", true)]
        [InlineData("DesktopSearch", false)]
        [InlineData("Project", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("garbage", false)]
        public void Only_a_dictation_transcript_may_be_adopted_as_a_draft(String sidecar, Boolean draftable)
        {
            Assert.Equal(draftable, BridgeManager.LeftoverTranscriptIsDraftable(sidecar));
        }

        [Fact]
        public void Search_signal_verdict_never_fails_on_a_missing_recording()
        {
            Assert.True(BridgeManager.SearchSignalVerdict("signal", false, () => throw new IOException()));
            Assert.False(BridgeManager.SearchSignalVerdict("silent", false, () => throw new IOException()));
            Assert.Null(BridgeManager.SearchSignalVerdict("unknown", false, () => throw new IOException()));
            Assert.Null(BridgeManager.SearchSignalVerdict(null, false, () => throw new IOException()));
            Assert.False(BridgeManager.SearchSignalVerdict(null, true, () => false));
            Assert.True(BridgeManager.SearchSignalVerdict("unknown", true, () => true));
        }

        [Fact]
        public void Dictate_and_Send_shows_a_retained_draft_with_its_retry_hint()
        {
            var face = DesktopDictationFace.For(VoiceIntent.Desktop, new VoiceCaptureState(), null, "voice", pending: true, retryHint: "OPEN APP · TAP TO RETRY");
            Assert.Equal((VoiceFailure.InsertDraft, "voice", "OPEN APP · TAP TO RETRY"), face);
            var idle = DesktopDictationFace.For(VoiceIntent.Desktop, new VoiceCaptureState(), null, "voice");
            Assert.Equal(("Dictate & Send", "voice", "SEND"), idle);
        }

        // ---- device pass 1 Oct: what the Options+ replacement actually does ------------------------

        [Fact]
        public void Uninstall_parks_a_registration_that_only_nativePluginName_marks_as_ours()
        {
            // Options+ rewrites ApplicationInfo.json on install and drops selfRegisteredBy; the stamp
            // was the only gate, so nothing was parked on the device.
            using var home = new TempHome(); var runtime = Path.Combine(home.Dir, "Desktop"); var apps = Path.Combine(home.Dir, "apps");
            var app = Path.Combine(apps, "70", "@_vizhidesktop");
            Directory.CreateDirectory(Path.Combine(app, "Profiles", "CUSTOM")); File.WriteAllText(Path.Combine(app, "Profiles", "CUSTOM", "ProfileInfo.json"), "{}");
            File.WriteAllText(Path.Combine(app, "ApplicationInfo.json"), "{\"nativePluginName\":\"VizhiDesktop\",\"hasNativePlugin\":true}");
            File.WriteAllText(Path.Combine(app, ".vizhi-packaged-profile"), "STOCK");
            Assert.True(DesktopUninstall.Clean(runtime, Path.Combine(home.Dir, "ipc"), apps, DateTime.UtcNow));
            Assert.False(Directory.Exists(app));
            Assert.True(File.Exists(Path.Combine(DesktopUninstall.RegistrationReceipt(runtime), "70", "@_vizhidesktop", "Profiles", "CUSTOM", "ProfileInfo.json")));
            // Someone else's registration is never touched.
            var other = Path.Combine(apps, "70", "@_vizhidesktop"); Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "ApplicationInfo.json"), "{\"nativePluginName\":\"SomeoneElse\"}");
            Assert.True(DesktopUninstall.Clean(runtime, Path.Combine(home.Dir, "ipc"), apps, DateTime.UtcNow));
            Assert.True(Directory.Exists(other));
        }

        [Fact]
        public void Reinstall_merges_parked_profiles_into_the_registration_the_service_re_created()
        {
            // The service re-registers the app from the new package before Install()/Load() run, keeping
            // only the active profile; the parked ones must be merged in, never overwrite what is there.
            using var home = new TempHome(); var runtime = Path.Combine(home.Dir, "Desktop"); var apps = Path.Combine(home.Dir, "apps");
            var app = Path.Combine(apps, "70", "@_vizhidesktop");
            foreach (var p in new[] { "ACTIVE", "CUSTOM1", "CUSTOM2" })
            { Directory.CreateDirectory(Path.Combine(app, "Profiles", p)); File.WriteAllText(Path.Combine(app, "Profiles", p, "ProfileInfo.json"), "parked " + p); }
            File.WriteAllText(Path.Combine(app, "ApplicationInfo.json"), "{\"nativePluginName\":\"VizhiDesktop\"}");
            File.WriteAllText(Path.Combine(app, ".vizhi-packaged-profile"), "STOCK");
            var now = DateTime.UtcNow;
            Assert.True(DesktopUninstall.Clean(runtime, Path.Combine(home.Dir, "ipc"), apps, now));

            // What the service leaves behind after the replacement: a fresh ApplicationInfo, the active profile only.
            Directory.CreateDirectory(Path.Combine(app, "Profiles", "ACTIVE"));
            File.WriteAllText(Path.Combine(app, "Profiles", "ACTIVE", "ProfileInfo.json"), "live ACTIVE (rewritten by the service)");
            File.WriteAllText(Path.Combine(app, "ApplicationInfo.json"), "{\"nativePluginName\":\"VizhiDesktop\",\"defaultProfileName\":\"ACTIVE\"}");

            Assert.True(DesktopUninstall.TryRestoreRegistration(runtime, apps, now.AddMinutes(3)));

            Assert.Equal("live ACTIVE (rewritten by the service)", File.ReadAllText(Path.Combine(app, "Profiles", "ACTIVE", "ProfileInfo.json")));
            Assert.Equal("parked CUSTOM1", File.ReadAllText(Path.Combine(app, "Profiles", "CUSTOM1", "ProfileInfo.json")));
            Assert.Equal("parked CUSTOM2", File.ReadAllText(Path.Combine(app, "Profiles", "CUSTOM2", "ProfileInfo.json")));
            Assert.Equal("STOCK", File.ReadAllText(Path.Combine(app, ".vizhi-packaged-profile")));
            Assert.Contains("\"defaultProfileName\":\"ACTIVE\"", File.ReadAllText(Path.Combine(app, "ApplicationInfo.json")));   // the service's document is kept
            Assert.False(Directory.Exists(DesktopUninstall.RegistrationReceipt(runtime)));
        }

        [Fact]
        public void An_emptied_draft_file_restores_nothing()
        {
            using var home = new TempHome(); var path = Path.Combine(home.Dir, "pending-draft.json");
            File.WriteAllText(path, "[]");
            Assert.False(new DesktopDraftRecovery(new DesktopCommandRig.Automation(), path).Pending);
        }
    }
}
