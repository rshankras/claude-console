namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using System.Text.Json;
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;
    using Workflow = Loupedeck.ClaudeConsolePlugin.DesktopActions.DesktopWorkflowCommand;

    /// <summary>Calls the same handlers as the SDK, with inert automation and deterministic clocks.
    /// No helper processes, native app inspection, clipboard access, or microphone recording.</summary>
    public class DesktopCommandRig
    {
        private static readonly OpenAiDesktopAdapter App = new();
        private static JsonElement Catalog => JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "desktop", "commands.json"))).RootElement;

        public static IEnumerable<Object[]> Cases()
        {
            foreach (var command in Catalog.GetProperty("commands").EnumerateArray())
                foreach (var mode in command.GetProperty("modes").EnumerateArray())
                    foreach (var success in new[] { true, false })
                        yield return new Object[] { command.GetProperty("id").GetString() + "." + mode.GetString().ToLowerInvariant(), success };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Catalog_dispatch(String caseId, Boolean success)
        {
            var split = caseId.LastIndexOf('.');
            var id = caseId[..split];
            var mode = caseId[(split + 1)..] == "chatgpt" ? "ChatGPT" : "Codex";
            var c = Catalog.GetProperty("commands").EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
            var parameter = c.GetProperty("parameter").GetString();
            var fake = new Automation { Succeeds = success, Next = Snapshot(mode), Error = "test-failure" };
            var repaint = 0;
            switch (c.GetProperty("kind").GetString())
            {
                case "files":
                case "file-control":
                    var emptyDownloads = Path.Combine(Path.GetTempPath(), "vizhi-file-rig-" + Guid.NewGuid());
                    var picker = new DesktopFilePicker(fake, new(fake), emptyDownloads); picker.Begin();
                    Assert.Empty(picker.Current.Files);
                    if (parameter != "*") Assert.Equal(parameter == "browse" && success, picker.Execute(parameter, App, new()));
                    Assert.DoesNotContain(fake.Calls, call => call.Name is "write" or "append" or "files" or "send");
                    picker.End();
                    break;
                case "find-legacy":
                    var legacyFinder = new DesktopSearch(fake);
                    Assert.Equal(mode == "ChatGPT", FindChatDynamicFolder.Open(fake, App, legacyFinder));
                    Assert.Equal(mode == "ChatGPT" && success, legacyFinder.Current.Available);
                    if (mode == "Codex") Assert.Equal("status", Assert.Single(fake.Calls).Name);
                    break;
                case "find-chat-only":
                    if (DesktopNavigateCommand.IsHidden(parameter, mode)) { Assert.Empty(fake.Calls); break; }
                    goto case "find";
                case "find":
                    var finder = new DesktopSearch(fake);
                    var searches = 0;
                    var navigation = DesktopNavigateCommand.Execute(mode, fake, () => { searches++; finder.Begin(); });
                    if (mode == "ChatGPT")
                    {
                        Assert.Equal(1, searches); Assert.Null(navigation);
                        Assert.Equal(success, finder.Current.Available);
                        Assert.Equal("search:open", Assert.Single(fake.Calls, x => x.Name != "status").Name);
                    }
                    else
                    {
                        Assert.Equal(0, searches);
                        Assert.Equal("open-changes", Assert.Single(fake.Calls, x => x.Name != "status").Name);
                        Assert.Equal(success ? "Opened" : "Couldn't open", navigation);
                    }
                    break;
                case "search":
                    var search = new DesktopSearch(fake); search.Begin(); fake.Calls.Clear();
                    var captures = new List<VoiceIntent>();
                    DesktopSearchCommand.Execute(parameter, search, new(fake), new VoiceCaptureState(),
                        (intent, sink) => { Assert.NotNull(sink); captures.Add(intent); }, () => Assert.Fail("query control closed folder"));
                    if (parameter == "speak")
                        Assert.Equal(success && mode == "ChatGPT" ? new[] { VoiceIntent.DesktopSearch } : Array.Empty<VoiceIntent>(), captures);
                    else if (parameter is "type" or "status")
                        Assert.Equal(success && mode == "ChatGPT" ? "search:focus" : "search:open", Assert.Single(fake.Calls).Name);
                    else if (success && mode == "ChatGPT") Assert.Equal("search:read", Assert.Single(fake.Calls).Name);
                    else Assert.Empty(fake.Calls);
                    Assert.DoesNotContain(fake.Calls, x => x.Name is "write" or "send");
                    break;
                case "conversation":
                    var title = "Conversation " + parameter + " — exact / தமிழ்";
                    Assert.Equal(success, DesktopConversationCommand.Execute(new DesktopConversation { Title = title }, fake));
                    Assert.Equal(title, Assert.Single(fake.Calls, x => x.Name == "conversation").Text);
                    Assert.Equal(success ? 1 : 0, fake.Calls.Count(x => x.Name == "focus"));
                    break;
                case "folder":
                    var closed = 0;
                    var conversations = new[] { new DesktopConversation { Title = "A" }, new DesktopConversation { Title = "B" } };
                    var folderAction = ActionString.FromString(AllChatsDynamicFolder.ConversationActionNames("VizhiDesktop", conversations)[1]);
                    DesktopConversationCommand.ExecuteFolder(folderAction.ActionParameter, conversations,
                        fake, () => closed++, () => repaint++);
                    Assert.Equal("B", fake.Calls[0].Text);
                    Assert.Equal(success ? 1 : 0, closed);
                    Assert.Equal(0, repaint);
                    break;
                case "control":
                    var controlResult = DesktopControlCommand.Execute(parameter, App, fake);
                    if (parameter == "show_diff" && mode != "Codex")
                    { Assert.Equal("Mode Changed", controlResult); Assert.Equal("status", Assert.Single(fake.Calls).Name); break; }
                    var operation = parameter switch { "stop" => "exact", "new_chat" => "press", "mode" => "mode", "focus" => "focus", "show_diff" => "open-changes", _ => throw new Exception(parameter) };
                    var call = Assert.Single(fake.Calls, x => x.Name != "status");
                    Assert.Equal(operation, call.Name);
                    if (parameter == "stop") Assert.Equal(App.StopLabels, call.Labels);
                    if (parameter == "new_chat") Assert.Equal(new[] { App.NewChatLabel }, call.Labels);
                    if (parameter == "mode") Assert.Equal(mode == "ChatGPT" ? "Codex" : "ChatGPT", call.Text);
                    break;
                case "approval":
                    DesktopApprovalCommand.Execute(parameter, Pending(mode), App, fake,
                        new DesktopApprovalConfirmation(), DateTime.UnixEpoch, () => repaint++);
                    var guarded = Assert.Single(fake.Calls);
                    Assert.Equal("guarded", guarded.Name);
                    Assert.Equal("read fixture.txt", guarded.Text);
                    Assert.Equal(parameter == "approve" ? App.ApproveLabels : App.DenyLabels, guarded.Labels);
                    Assert.Equal(success ? 0 : 1, repaint);
                    break;
                case "context":
                    DesktopContextCommand.Execute(parameter, App, fake, () => repaint++);
                    var control = Enum.Parse<DesktopControl>(c.GetProperty("controls").GetProperty(mode).GetString());
                    var contextual = Assert.Single(fake.Calls, x => x.Name != "status");
                    Assert.Equal(control == DesktopControl.Changes ? "open-changes" : "in-mode", contextual.Name);
                    if (control != DesktopControl.Changes) { Assert.Equal(mode, contextual.Text); Assert.Equal(App.ControlLabels(control), contextual.Labels); }
                    Assert.True(DesktopContextCommand.FaceFor(parameter, mode, control).Enabled);
                    break;
                case "composer":
                    var feedback = new DesktopComposerCommand.PressHandler().Execute(parameter, App, fake, "send");
                    if (parameter is "send" or "send_stop")
                    {
                        Assert.Equal("send", Assert.Single(fake.Calls).Name); // no Status/write retargeting
                        Assert.Equal(success ? "Sent" : "Not Sent", feedback);
                    }
                    else if (mode == "Codex")
                    {
                        Assert.Equal("open-changes", fake.Calls[1].Name);
                        Assert.Equal(success ? "Opened" : "Couldn't open", feedback);
                    }
                    else
                    {
                        Assert.Equal("status", Assert.Single(fake.Calls).Name);
                        Assert.Equal("Unsupported", feedback); // unsupported is not functional success
                    }
                    break;
                case "workflow":
                    // Distinct text in every position proves the real resolver dispatches the right configuration.
                    var gpt = Workflows(Workflow.ChatGptDefaults, "gpt");
                    var codex = Workflows(Workflow.CodexDefaults, "codex");
                    var named = codex.Concat(Workflow.ExtraWorkflows).ToDictionary(w => w.Id);
                    var capture = new VoiceCaptureState();
                    var recovery = new DesktopDraftRecovery(fake);
                    var spoken = new DesktopWorkflowVoice(fake, recovery);
                    Func<String, String> sink = null;
                    var result = Workflow.Execute(parameter, fake, (p, m) => Workflow.Resolve(p, m, gpt, codex, named),
                        spoken, capture, new(fake), (i, callback) => { capture.Press(i, DateTime.UnixEpoch); sink = callback; });
                    if (mode == "Codex" && parameter == "draft_reply") { Assert.Equal("Use ChatGPT", result); break; }
                    if (mode == "ChatGPT" && parameter != "draft_reply" && !parameter.StartsWith("slot_"))
                    {
                        Assert.Equal("Use Codex", result);
                        Assert.Equal("status", Assert.Single(fake.Calls).Name);
                        break;
                    }
                    var expected = parameter.StartsWith("slot_") || parameter.StartsWith("task_")
                        ? (mode == "ChatGPT" ? gpt : codex)[Int32.Parse(parameter[5..]) - 1] : parameter == "draft_reply" ? gpt.Single(w => w.Id == "draft") : named[parameter];
                    if (expected.RequiresSpeech)
                    {
                        Assert.DoesNotContain(fake.Calls, x => x.Name is "write" or "send");
                        if (!success) { Assert.Equal("Check App", result); Assert.Null(sink); break; }
                        Assert.Null(result); Assert.NotNull(sink);
                        Assert.Null(sink("specific spoken brief")); capture.Finish();
                        Assert.Equal("Send Draft", spoken.Face(expected.Id, mode, capture)?.Label);
                    }
                    var write = Assert.Single(fake.Calls, x => x.Name == "write");
                    Assert.Equal(expected.Prompt.Replace("{brief}", "specific spoken brief"), write.Text);
                    Assert.Equal(c.GetProperty("submits").GetProperty(mode).GetBoolean(), write.Send);
                    if (!expected.RequiresSpeech)
                    {
                        Assert.Equal(success ? expected.Submits ? "Sent" : "Draft Ready" : expected.Submits ? "Not Sent" : "Not Typed", result);
                        var face = Workflow.FaceFor(expected, mode, result);
                        Assert.Equal(result, face.Label);
                        Assert.Equal(success ? expected.Submits ? "REQUEST SENT" : "SEND DRAFT" : "CHECK APP", face.Footer);
                    }
                    Assert.Equal(success && !expected.Submits ? 1 : 0, fake.Calls.Count(x => x.Name == "focus"));
                    break;
                case "tools":
                    var route = DesktopToolsCommand.Resolve(parameter, mode);
                    var toolContext = new DesktopContextCapture(fake);
                    fake.Succeeds=true; toolContext.Execute("selection",new()); fake.Calls.Clear(); fake.Succeeds=success;
                    var toolResult = DesktopToolsCommand.Execute(parameter, Pending(mode), fake, App, toolContext, new(),new(),()=>repaint++,
                        id => Workflow.Execute(id,fake,(p,_)=>Workflow.CodexDefaults.Single(w=>w.Id==p)));
                    if (route.Kind == null)
                    {
                        Assert.Null(toolResult); Assert.Empty(fake.Calls);
                    }
                    else if (route.Kind=="capture")
                    {
                        if (route.Id == "screenshot" && !success) Assert.DoesNotContain(fake.Calls, c => c.Name == "context:screenshot");
                        else if(route.Id!="clear") Assert.Single(fake.Calls,c=>c.Name=="context:"+route.Id);
                        else Assert.Equal("Sources Cleared",toolResult);
                        Assert.DoesNotContain(fake.Calls,c=>c.Name is "write" or "send" or "guarded");
                    }
                    else if(route.Kind=="approval")
                    {
                        var approved=Assert.Single(fake.Calls,c=>c.Name=="guarded");
                        Assert.Equal(route.Id=="approve" ? App.ApproveLabels : App.DenyLabels, approved.Labels);
                        Assert.Equal(success?0:1,repaint);
                    }
                    else
                    {
                        var written=Assert.Single(fake.Calls,c=>c.Name=="write");
                        Assert.Equal(Workflow.CodexDefaults.Single(w=>w.Id==route.Id).Prompt,written.Text);
                        Assert.Equal(success?"Sent":"Not Sent",toolResult);
                    }
                    break;
                case "saved-prompts":
                    Assert.Equal(mode == "Codex" ? 8 : 9,DesktopSavedPromptsDynamicFolder.Actions("VizhiDesktop", mode).Length);
                    Assert.Empty(fake.Calls);
                    break;
                case "capture":
                    var sources = new DesktopContextCapture(fake);
                    var state = new VoiceCaptureState();
                    fake.Succeeds = true;
                    if (parameter is "return" or "paste" or "clear") sources.Execute("selection", state);
                    if (parameter == "paste") sources.Execute("copy", state);
                    fake.Calls.Clear(); fake.Succeeds = success;
                    var captureResult = sources.Execute(parameter, state);
                    Assert.DoesNotContain(fake.Calls, x => x.Name == "send" || x.Name == "write");
                    if (parameter == "clear") { Assert.Equal("Sources Cleared", captureResult); Assert.Empty(fake.Calls); }
                    else if (parameter == "screenshot")
                    {
                        Assert.Equal(success ? "Attached" : "Try Again", captureResult);
                        Assert.Equal(success ? 1 : 0, fake.Calls.Count(c => c.Name == "context:screenshot"));
                        Assert.Equal(success ? 1 : 0, fake.Calls.Count(c => c.Name == "attach"));
                        Assert.Equal(0, sources.Count);
                    }
                    else
                    {
                        Assert.Single(fake.Calls, x => x.Name == "context:" + parameter);
                        Assert.Equal(success ? parameter switch { "selection" => "Text Added", "clipboard" => "Pasted", "screenshot" => "Image Captured", "copy" => "Copied", "return" => "Returned", _ => "Reply Inserted" } : "Try Again", captureResult);
                    }
                    break;
                case "ask":
                    Assert.Contains(AskChatGptDynamicFolder.Actions("VizhiDesktop"), x => x.EndsWith("___selection"));
                    Assert.Empty(fake.Calls);
                    break;
                case "more":
                    Assert.NotEmpty(DesktopMoreDynamicFolder.Actions("VizhiDesktop", mode));
                    Assert.Empty(fake.Calls);
                    break;
                case "voice":
                    fake.HasVoiceShortcut = true;
                    Assert.Equal(success, new DesktopVoiceActions(fake).RequestVoice(DesktopVoiceState.Unavailable,
                        new VoiceCaptureState(), out var voiceFeedback));
                    Assert.Equal(success ? "Requested" : "Check App", voiceFeedback);
                    Assert.Equal("toggle", Assert.Single(fake.Calls).Name);
                    Assert.Equal("Voice Chat", DesktopVoiceChatCommand.LabelFor(DesktopState.Unavailable, true));
                    break;
                case "draft":
                case "dictate":
                    var intent = c.GetProperty("kind").GetString() == "draft" ? VoiceIntent.DesktopDraft : VoiceIntent.Desktop;
                    if (!success) fake.Next = new DesktopSnapshot { VoiceChat = DesktopVoiceState.Active };
                    var toggles = new List<VoiceIntent>();
                    Assert.Equal(success, new DesktopVoiceActions(fake).RequestDictation(intent, new VoiceCaptureState(), toggles.Add, out var dictationFeedback));
                    Assert.Equal(success ? null : "End Voice", dictationFeedback);
                    Assert.Equal(success ? new[] { intent } : Array.Empty<VoiceIntent>(), toggles);
                    break;
                case "status":
                    DesktopStatusCommand.Execute();
                    Assert.Empty(fake.Calls);
                    Assert.Equal("Ready", DesktopStatusCommand.FaceFor(DesktopMonitor.Map(fake.Next)).label);
                    break;
                default: Assert.Fail("No production handler scenario for " + id); break;
            }
        }

        private static Workflow.WorkflowDef[] Workflows(Workflow.WorkflowDef[] defaults, String prefix) =>
            defaults.Select((w, i) => new Workflow.WorkflowDef { Id = w.Id, Label = w.Label, Icon = w.Icon,
                Submit = w.Submit, Input = w.Input, Prompt = prefix + ":" + (i + 1) + ": exact text\nதமிழ்" + (w.RequiresSpeech ? " {brief}" : "") }).ToArray();

        private static DesktopSnapshot Snapshot(String mode = "ChatGPT") => new()
        { SurfaceAvailable = true, Mode = mode, AvailableControls = (DesktopControl)1023, CanSend = true };
        private static DesktopState Pending(String mode = "Codex", String card = "read fixture.txt", ApprovalRisk risk = ApprovalRisk.Normal) => new()
        { Activity = DesktopActivity.WaitingApproval, Mode = mode, CardText = card, Risk = risk, ActiveTitle = "Test" };

        [Fact]
        public void Empty_stale_and_failed_conversation_keys_never_focus_or_retarget()
        {
            var fake = new Automation(); var closed = 0; var repaint = 0;
            Assert.False(DesktopConversationCommand.Execute(null, fake));
            DesktopConversationCommand.ExecuteFolder("chat:%invalid", Array.Empty<DesktopConversation>(), fake, () => closed++, () => repaint++);
            DesktopConversationCommand.ExecuteFolder("chat:" + AllChatsDynamicFolder.EncodeTitle("Old"),
                new[] { new DesktopConversation { Title = "New" } }, fake, () => closed++, () => repaint++);
            Assert.Empty(fake.Calls); Assert.Equal(0, closed); Assert.Equal(1, repaint);
        }

        [Fact]
        public void Unknown_mode_and_unavailable_context_are_noops_with_repaint()
        {
            var fake = new Automation(); var repaint = 0;
            DesktopControlCommand.Execute("mode", App, fake);
            DesktopContextCommand.Execute("primary", App, fake, () => repaint++);
            Assert.All(fake.Calls, x => Assert.Equal("status", x.Name));
            Assert.Equal(1, repaint);
        }

        [Fact]
        public void Context_uses_fresh_mode_and_preserves_the_helper_guard()
        {
            var fake = new Automation { Next = Snapshot("ChatGPT") };
            using var monitor = new DesktopMonitor(fake); monitor.PollOnce();
            fake.Next = Snapshot("Codex"); fake.Succeeds = false; fake.Calls.Clear();
            DesktopContextCommand.Execute("primary", App, fake, () => { });
            Assert.Equal("ChatGPT", monitor.Current.Mode);
            Assert.Equal("open-changes", fake.Calls[1].Name);
            Assert.Equal(2, fake.Calls.Count); // no unguarded fallback
        }

        [Fact]
        public void Send_debounce_includes_clock_zero_and_failure_can_retry_immediately()
        {
            var fake = new Automation(); long now = 0;
            var handler = new DesktopComposerCommand.PressHandler { Clock = () => now };
            Assert.Equal("Sent", handler.Execute("send", App, fake));
            Assert.Null(handler.Execute("send", App, fake));
            Assert.Single(fake.Calls);
            now = 1000; fake.Succeeds = false; fake.Error = "no-sendable-draft";
            Assert.Equal("No Draft", handler.Execute("send", App, fake));
            fake.Succeeds = true;
            Assert.Equal("Sent", handler.Execute("send", App, fake));
            Assert.Equal(3, fake.Calls.Count);
        }

        [Theory]
        [InlineData("approve")]
        [InlineData("deny")]
        public void High_risk_requires_same_card_action_and_unexpired_confirmation(String parameter)
        {
            var fake = new Automation { Succeeds = false, Error = "card-changed" };
            var confirmation = new DesktopApprovalConfirmation(); var repaints = 0;
            var state = Pending(risk: ApprovalRisk.High); var now = DateTime.UnixEpoch;
            void Press(DesktopState s, double seconds, String p = null) => DesktopApprovalCommand.Execute(
                p ?? parameter, s, App, fake, confirmation, now.AddSeconds(seconds), () => repaints++);
            Press(state, 0); Assert.Empty(fake.Calls);
            Assert.Equal("Press again", DesktopApprovalCommand.LabelFor(parameter, state, confirmation, now));
            Assert.NotEqual("Press again", DesktopApprovalCommand.LabelFor(parameter, state, confirmation, now.AddSeconds(4)));
            Press(state, 4); Assert.Empty(fake.Calls); // expired, re-arm
            Press(Pending(card: "different request", risk: ApprovalRisk.High), 5); Assert.Empty(fake.Calls);
            Press(state, 6); Assert.Empty(fake.Calls);
            Press(state, 7); Assert.Equal("guarded", Assert.Single(fake.Calls).Name);
            Assert.False(confirmation.IsArmed(parameter, state, now.AddSeconds(7)));
            Assert.Equal(5, repaints); // four arms and helper guard refusal
            Press(DesktopState.Unavailable, 8); Press(state, 8, "unknown");
            Assert.Single(fake.Calls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Workflow_preserves_existing_draft_and_reports_failed_delivery(Boolean submits)
        {
            var fake = new Automation { Succeeds = false, Error = "draft-exists", Next = Snapshot() };
            var workflow = new Workflow.WorkflowDef { Id = "test", Prompt = "configured text", Submit = submits };
            Assert.Equal("Draft Exists", Workflow.Execute("slot_1", fake, (_, _) => workflow));
            Assert.Equal(new[] { "status", "write" }, fake.Calls.Select(x => x.Name));
            Assert.Null(Workflow.Execute("slot_1", fake, (_, _) => null));
        }

        [Theory]
        [InlineData(VoiceIntent.DesktopDraft)]
        [InlineData(VoiceIntent.Desktop)]
        public void Dictation_faces_follow_start_record_process_delivery_and_original_intent(VoiceIntent intent)
        {
            var capture = new VoiceCaptureState(); var fake = new Automation(); var actions = new DesktopVoiceActions(fake);
            var changes = 0; capture.Changed += () => changes++;
            var now = DateTime.UnixEpoch;
            void Toggle(VoiceIntent pressed) => capture.Press(pressed, now, awaitReadiness: true);
            String Label() => DesktopDictationFace.For(intent, capture, null, "frame").Label;
            Assert.True(actions.RequestDictation(intent, capture, Toggle, out _));
            Assert.Equal("Starting", Label()); Assert.True(capture.MarkReady(now));
            Assert.Equal("Listening", Label());
            Assert.Equal("PRESS TO STOP", DesktopDictationFace.For(intent, capture, null, "frame").Footer);
            var other = intent == VoiceIntent.DesktopDraft ? VoiceIntent.Desktop : VoiceIntent.DesktopDraft;
            Assert.True(actions.RequestDictation(other, capture, Toggle, out _));
            Assert.Equal(intent, capture.Intent); Assert.Equal("Transcribing", Label());
            Assert.Equal("WAIT", DesktopDictationFace.For(intent, capture, null, "frame").Footer);
            Assert.Equal(VoiceAction.Refuse, capture.Press(other, now).Action);
            capture.Finish(); Assert.Equal(4, changes);
            Assert.Equal(intent == VoiceIntent.DesktopDraft ? "Dictate" : "Dictate & Send", Label());
            var recovered = DesktopDictationFace.For(intent, capture, VoiceFailure.InsertDraft, "frame");
            Assert.Equal(intent == VoiceIntent.DesktopDraft ? "HOLD TO DISCARD" : "CHECK APP", recovered.Footer);
        }

        [Theory]
        [InlineData("hidden", "Hidden")]
        [InlineData("no-permission", "No Access")]
        [InlineData("not-running", "App Off")]
        [InlineData("no-signal", "No Signal")]
        public void Activity_distinguishes_unavailability_and_does_not_repaint_identical_snapshots(String reason, String label)
        {
            var fake = new Automation { Next = Snapshot() }; using var monitor = new DesktopMonitor(fake);
            var paints = 0; monitor.OnChanged += _ => paints++;
            monitor.PollOnce(); monitor.PollOnce(); Assert.Equal(1, paints);
            fake.Next = new DesktopSnapshot { UnavailableReason = reason };
            monitor.PollOnce(); monitor.PollOnce(); Assert.Equal(2, paints);
            Assert.Equal(label, DesktopStatusCommand.FaceFor(monitor.Current).label);
        }

        [Theory]
        [InlineData(VoiceIntent.DesktopDraft, true, true)]
        [InlineData(VoiceIntent.DesktopDraft, false, true)]
        [InlineData(VoiceIntent.DesktopDraft, false, false)]
        [InlineData(VoiceIntent.Desktop, true, true)]
        [InlineData(VoiceIntent.Desktop, false, true)]
        public void Captured_intent_reaches_production_sink_and_recovery_without_retargeting(
            VoiceIntent intent, Boolean accepted, Boolean copied)
        {
            var fake = new Automation { Succeeds = accepted, Error = "write-not-applied" };
            var bridge = new BridgeManager(new PlatformSeamTests.FakePlatformBridge());
            bridge.TranscriptSink = (text, send) => DesktopTranscriptDelivery.Write(fake, text, send);
            var clipboard = new List<String>();
            bridge.DraftRecoverySink = text => { clipboard.Add(text); return copied; };
            var failures = new List<(VoiceIntent Intent, String Label)>();
            bridge.OnVoiceFailed += (which, label) => failures.Add((which, label));
            var capture = new VoiceCaptureState();
            var actions = new DesktopVoiceActions(fake);
            actions.RequestDictation(intent, capture, i => capture.Press(i, DateTime.UnixEpoch), out _);
            actions.RequestDictation(intent == VoiceIntent.DesktopDraft ? VoiceIntent.Desktop : VoiceIntent.DesktopDraft,
                capture, i => capture.Press(i, DateTime.UnixEpoch), out _);
            // Fixed transcript is injected at the recorder boundary: no real microphone is opened.
            bridge.DeliverToSink("Original transcript தமிழ்", capture.Intent == VoiceIntent.Desktop);
            capture.Finish();
            var write = Assert.Single(fake.Calls, c => c.Name == "write");
            Assert.Equal("Original transcript தமிழ்", write.Text);
            Assert.Equal(intent == VoiceIntent.Desktop, write.Send);
            Assert.Equal(accepted && intent == VoiceIntent.DesktopDraft ? 1 : 0, fake.Calls.Count(c => c.Name == "focus"));
            if (accepted) { Assert.Empty(clipboard); Assert.Empty(failures); }
            else
            {
                var failure = Assert.Single(failures);
                Assert.Equal(intent, failure.Intent);
                if (intent == VoiceIntent.DesktopDraft)
                {
                    Assert.Equal("Original transcript தமிழ்", Assert.Single(clipboard));
                    Assert.Equal(copied ? VoiceFailure.InsertDraft : VoiceFailure.NotTyped, failure.Label);
                }
                else { Assert.Empty(clipboard); }
            }
            Assert.Equal(VoicePhase.Idle, capture.Phase);
        }

        internal sealed record Call(String Name, String Text = null, Boolean Send = false, String[] Labels = null);
        internal sealed class Automation : IDesktopAutomation
        {
            internal readonly List<Call> Calls = new();
            internal DesktopSnapshot Next = DesktopSnapshot.Unavailable;
            internal Boolean Succeeds = true;
            internal String Error;
            public Boolean HasVoiceShortcut { get; set; }
            public DesktopSnapshot Status() { Calls.Add(new("status")); return Next; }
            public DesktopSearchSnapshot Search(String action, String target = null, String query = null, String value = null, String title = null, String origin = null)
            { Calls.Add(new("search:" + action, value)); return Succeeds && Next.Mode == "ChatGPT" ? DesktopSearchTests.Ready() : new(); }
            public Boolean Press(String[] labels, out String matched) { Calls.Add(new("press", Labels: labels)); matched = null; return Succeeds; }
            public Boolean PressExact(String[] labels) { Calls.Add(new("exact", Labels: labels)); return Succeeds; }
            public Boolean OpenChanges(out String error) { Calls.Add(new("open-changes")); error = Error; return Succeeds; }
            public Boolean PressInMode(String[] labels, String mode, out String matched) { Calls.Add(new("in-mode", mode, Labels: labels)); matched = null; return Succeeds; }
            public Boolean PressConversation(String title) { Calls.Add(new("conversation", title)); return Succeeds; }
            public Boolean PressGuarded(String[] labels, String card, out String matched, out String error) { Calls.Add(new("guarded", card, Labels: labels)); matched = null; error = Error; return Succeeds; }
            public Boolean WriteComposer(String text, Boolean send, out String error) { Calls.Add(new("write", text, send)); error = Error; return Succeeds; }
            public String PrepareDraft(String mode, Boolean requireEmpty, out String error) { Calls.Add(new("prepare", mode)); error = Error; return Succeeds ? "fixture-target" : null; }
            public Boolean WritePreparedDraft(String text, String mode, String target, Boolean retry, out String error) => WriteComposer(text, false, out error);
            public Boolean WritePreparedPrompt(String text, String mode, String target, Boolean send, out String error)
            { Calls.Add(new("prompt", text, send, new[] { mode, target })); error = Error; return Succeeds; }
            public Boolean SupportsAppend { get; set; }
            internal DesktopAppendTarget AppendDestination = new() { Target = "fixture-target", Fingerprint = "fixture-draft-hash" };
            internal Func<String, String, DesktopAppendTarget, Boolean, (Boolean Ok, String Error)> Appender;
            public DesktopAppendTarget PrepareAppend(String mode, out String error)
            { Calls.Add(new("prepare-append", mode)); error = Error; return Succeeds ? AppendDestination : null; }
            public Boolean AppendPreparedDraft(String text, String mode, DesktopAppendTarget target, Boolean retry, out String error)
            {
                Calls.Add(new("append", text, Labels: new[] { mode, target.Target, target.Fingerprint, retry.ToString() }));
                var result = Appender?.Invoke(text, mode, target, retry) ?? (Succeeds, Error);
                error = result.Item2; return result.Item1;
            }
            public Boolean SendPreparedDraft(String mode, String target, out String error) => SendComposer(out error);
            internal Func<String, String, String, (Boolean Ok, String Error)> PromptSender;
            public Boolean SendPreparedPrompt(String text, String mode, String target, out String error)
            {
                Calls.Add(new("send-prompt", text, true, new[] { mode, target }));
                var result = PromptSender?.Invoke(text, mode, target) ?? (Succeeds, Error);
                error = result.Item2; return result.Item1;
            }
            public Boolean SendComposer(out String error) { Calls.Add(new("send")); error = Error; return Succeeds; }
            internal Func<String, DesktopCaptureResult> CaptureResult;
            public DesktopCaptureResult Context(String action, String source = null, String text = null)
            {
                Calls.Add(new("context:" + action, text, Labels: source == null ? null : new[] { source }));
                return CaptureResult?.Invoke(action) ?? new() { Ok = Succeeds, Error = Error,
                    Text = action == "screenshot" ? null : "fixture context", Image = action == "screenshot" ? "/fixture/image.png" : null,
                    Source = "source-window", AppName = "Fixture" };
            }
            internal Func<String, String, String, (Boolean Ok, String Error)> Attacher;
            internal Func<DesktopFile[], String, String, (Boolean Ok, String Error)> FilesAttacher;
            public Boolean AttachPreparedFiles(DesktopFile[] files, String mode, String target, out String error)
            {
                Calls.Add(new("files", String.Join("\n", files.Select(f => f.Path)), Labels: new[] { mode, target }));
                var result = FilesAttacher?.Invoke(files, mode, target) ?? (Succeeds, Error);
                error = result.Item2; return result.Item1;
            }
            public Boolean AttachPreparedImage(String path, String mode, String target, out String error)
            {
                Calls.Add(new("attach", path, Labels: new[] {mode,target}));
                var result = Attacher?.Invoke(path, mode, target) ?? (Succeeds, Error);
                error = result.Item2; return result.Item1;
            }
            public Boolean CopyAnswer(out String error) { Calls.Add(new("copy")); error = Error; return Succeeds; }
            public Boolean SetVoiceChat(Boolean active, out String error) { Calls.Add(new("voice", Send: active)); error = Error; return Succeeds; }
            public Boolean ToggleVoiceChat(out String error) { Calls.Add(new("toggle")); error = Error; return Succeeds; }
            public Boolean SwitchMode(String mode) { Calls.Add(new("mode", mode)); return Succeeds; }
            public Boolean FocusApp() { Calls.Add(new("focus")); return Succeeds; }
        }
    }
}
