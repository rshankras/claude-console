namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System.IO.Compression;
    using System.Text.Json.Nodes;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Loupedeck.ClaudeConsolePlugin.VizhiDesktop.Registration;
    using Xunit;

    /// <summary>
    /// 0.17.18 layout: Deny/Approve on Home where the family keypads put No/Yes, All Chats beside
    /// Find Chat on Tools, and no key blank in either mode — a blank key is what a failed plugin
    /// looks like, and the owner read the old ChatGPT Tools page as broken.
    /// </summary>
    public class DesktopHomeToolsLayoutTests
    {
        private static String Package()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "tools/make-desktop-profile.py"))) root = root.Parent;
            return Path.Combine(root.FullName, "src/Products/VizhiDesktop/package/profiles/DefaultProfile70.lp5");
        }

        private static JsonNode PackagedProfile()
        {
            using var zip = ZipFile.OpenRead(Package());
            using var reader = new StreamReader(zip.GetEntry("ProfileInfo.json").Open());
            return JsonNode.Parse(reader.ReadToEnd());
        }

        private static JsonArray Controls(JsonNode doc, String pageName) => doc["layout"]["layoutModes"].AsArray()
            .Single(m => (String)m["modeName"] == "main")["workspaces"][0]["pressPages"].AsArray()
            .Single(p => (String)p["displayName"] == pageName)["controls"].AsArray();

        private static String[] Page(JsonNode doc, String pageName) =>
            Controls(doc, pageName).OrderBy(c => (Int32)c["controlId"]).Select(c => (String)c["pressAction"]).ToArray();

        private static void SetPage(JsonNode doc, String pageName, String[] bindings)
        {
            foreach (var c in Controls(doc, pageName)) c["pressAction"] = bindings[(Int32)c["controlId"]];
        }

        [Fact]
        public void The_migration_target_is_exactly_the_packaged_layout()
        {
            var doc = PackagedProfile();
            Assert.Equal(DesktopHomeToolsLayoutMigration.NewHome, Page(doc, "Home"));
            Assert.Equal(DesktopHomeToolsLayoutMigration.NewTools, Page(doc, "Tools"));
        }

        [Theory]
        [InlineData("ChatGPT")]
        [InlineData("Codex")]
        public void No_default_key_is_blank_in_either_mode(String mode)
        {
            var doc = PackagedProfile();
            foreach (var binding in Page(doc, "Home").Concat(Page(doc, "Tools")))
            {
                var parameter = binding.Substring(binding.LastIndexOf("___", StringComparison.Ordinal) + 3);
                if (binding.Contains("DesktopToolsCommand___"))
                {
                    Assert.False(DesktopToolsCommand.IsHidden(parameter, mode), binding);
                    Assert.NotNull(DesktopToolsCommand.Resolve(parameter, mode).Kind);
                }
                if (binding.Contains("DesktopNavigateCommand___"))
                    Assert.False(DesktopNavigateCommand.IsHidden(parameter, mode), binding);
            }
        }

        [Fact]
        public void Paste_and_clear_no_longer_borrow_copy_and_stop_glyphs()
        {
            Assert.Equal("paste", DesktopCaptureCommand.Face("clipboard", null).Icon);
            Assert.Equal("clear", DesktopCaptureCommand.Face("clear", null).Icon);
        }

        [Fact]
        public void Home_approval_keys_use_the_family_green_and_red()
        {
            var confirm = new Desktop.DesktopApprovalConfirmation();
            var now = DateTime.UtcNow;
            var idle = new Desktop.DesktopState { Activity = Desktop.DesktopActivity.Ready, Mode = "ChatGPT" };
            Assert.Equal(("Approve", KeyImage.Green, ApprovalRisk.None), DesktopApprovalCommand.FaceFor("approve", idle, confirm, now));
            Assert.Equal(("Deny", KeyImage.Red, ApprovalRisk.None), DesktopApprovalCommand.FaceFor("deny", idle, confirm, now));

            var waiting = new Desktop.DesktopState { Activity = Desktop.DesktopActivity.WaitingApproval, Mode = "Codex", Risk = ApprovalRisk.Normal };
            Assert.Equal(ApprovalRisk.Normal, DesktopApprovalCommand.FaceFor("deny", waiting, confirm, now).Risk);   // the badge
            Assert.Equal(KeyImage.Red, DesktopApprovalCommand.FaceFor("deny", waiting, confirm, now).Color);

            Assert.Equal(KeyImage.Gray, DesktopApprovalCommand.FaceFor("approve", Desktop.DesktopState.Unavailable, confirm, now).Color);

            var titled = new Desktop.DesktopState { Activity = Desktop.DesktopActivity.WaitingApproval, Mode = "Codex",
                Risk = ApprovalRisk.Normal, ActiveTitle = "Refactor the parser" };
            Assert.Equal("Approve", DesktopApprovalCommand.FaceFor("approve", titled, confirm, now).Label);   // the verb stays
            Assert.NotNull(DesktopApprovalCommand.TargetFor("approve", titled));                            // the title is the caption
            Assert.Null(DesktopApprovalCommand.TargetFor("deny", titled));
            Assert.Null(DesktopApprovalCommand.TargetFor("approve", idle));
        }

        [Fact]
        public void Schedule_is_a_spoken_chatgpt_request_never_a_codex_one()
        {
            var schedule = DesktopWorkflowCommand.ScheduleWorkflow;
            Assert.True(DesktopWorkflowCommand.IsUsable(schedule));
            Assert.True(schedule.RequiresSpeech);   // speak what and when; review; Send
            Assert.False(schedule.Submits);
            var none = new Dictionary<String, DesktopWorkflowCommand.WorkflowDef>();
            var chatGpt = DesktopWorkflowCommand.ChatGptDefaults; var codex = DesktopWorkflowCommand.CodexDefaults;
            Assert.Same(schedule, DesktopWorkflowCommand.Resolve("schedule", "ChatGPT", chatGpt, codex, none));
            Assert.Null(DesktopWorkflowCommand.Resolve("schedule", "Codex", chatGpt, codex, none));
        }

        [Fact]
        public void Screenshot_is_a_plain_area_capture()
        {
            Assert.Equal(("Screenshot", "screenshot", "ADD TO CHAT"), DesktopCaptureCommand.Face("screenshot", null));
        }

        [Fact]
        public void Clear_added_and_approve_share_one_assignable_key()
        {
            Assert.Equal(("capture", "clear"), DesktopToolsCommand.Resolve("clear_approve", "ChatGPT"));
            Assert.Equal(("approval", "approve"), DesktopToolsCommand.Resolve("clear_approve", "Codex"));
        }

        [Theory]
        [InlineData(0, true)]    // 0.17.14 – 0.17.15
        [InlineData(1, true)]    // 0.17.16 – 0.17.17
        [InlineData(2, true)]    // unreleased 0.17.18 build
        public void A_stock_install_moves_to_the_new_layout_with_one_backup(Int32 stockTools, Boolean changed)
        {
            var directory = Path.Combine(Path.GetTempPath(), "vizhi-layout-" + Guid.NewGuid());
            try
            {
                var doc = PackagedProfile();
                SetPage(doc, "Home", DesktopHomeToolsLayoutMigration.OldHome);
                SetPage(doc, "Tools", DesktopHomeToolsLayoutMigration.StockTools[stockTools]);
                Controls(doc, "Tools")[0]["custom-metadata"] = "preserved";
                var file = Write(directory, doc);
                var original = File.ReadAllText(file);

                Assert.Equal(changed, DesktopHomeToolsLayoutMigration.Upgrade(directory));

                var after = JsonNode.Parse(File.ReadAllText(file));
                Assert.Equal(DesktopHomeToolsLayoutMigration.NewHome, Page(after, "Home"));
                Assert.Equal(DesktopHomeToolsLayoutMigration.NewTools, Page(after, "Tools"));
                Assert.Equal("preserved", (String)Controls(after, "Tools")[0]["custom-metadata"]);
                Assert.Equal(original, File.ReadAllText(file + ".before-0.17.18"));

                var once = File.ReadAllText(file);
                Assert.False(DesktopHomeToolsLayoutMigration.Upgrade(directory));   // no flip-flop on the next load
                Assert.Equal(once, File.ReadAllText(file));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Theory]
        [InlineData("Home", 8)]    // e.g. Voice Chat replaced on Home
        [InlineData("Tools", 1)]   // e.g. a custom key where Approve was
        public void Any_customized_key_leaves_both_pages_untouched(String page, Int32 control)
        {
            var directory = Path.Combine(Path.GetTempPath(), "vizhi-layout-custom-" + Guid.NewGuid());
            try
            {
                var doc = PackagedProfile();
                SetPage(doc, "Home", DesktopHomeToolsLayoutMigration.OldHome);
                SetPage(doc, "Tools", DesktopHomeToolsLayoutMigration.StockTools[1]);
                Controls(doc, page).Single(c => (Int32)c["controlId"] == control)["pressAction"] = "my-custom-action";
                var file = Write(directory, doc);
                var original = File.ReadAllText(file);

                Assert.False(DesktopHomeToolsLayoutMigration.Upgrade(directory));
                Assert.Equal(original, File.ReadAllText(file));
                Assert.False(File.Exists(file + ".before-0.17.18"));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        private static String Write(String directory, JsonNode doc)
        {
            var file = Path.Combine(directory, "Profiles", DesktopHomeToolsLayoutMigration.Profile, "ProfileInfo.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, doc.ToJsonString());
            return file;
        }
    }
}
