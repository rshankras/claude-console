namespace Loupedeck.ClaudeConsolePlugin.Tests
{
    using Loupedeck.ClaudeConsolePlugin.Desktop;
    using Loupedeck.ClaudeConsolePlugin.DesktopActions;
    using Xunit;
    public class DesktopHomeTests
    {
        private static readonly OpenAiDesktopAdapter App = new();
        [Fact]
        public void A_displayed_stop_can_never_submit_when_the_response_finishes_before_the_tap()
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { SurfaceAvailable = true, CanSend = true }, Succeeds = false };
            var result = new DesktopComposerCommand.PressHandler().Execute("send_stop", App, fake, "stop");
            Assert.Equal("Nothing Running", result);
            var only = Assert.Single(fake.Calls); Assert.Equal("exact", only.Name); Assert.Equal(App.StopLabels, only.Labels);
            Assert.DoesNotContain(App.EndVoiceLabels, label => only.Labels.Contains(label));
        }
        [Fact]
        public void Combined_send_stop_requires_a_displayed_intent_and_debounces_the_transition()
        {
            var fake = new DesktopCommandRig.Automation(); long time = 0;
            var handler = new DesktopComposerCommand.PressHandler { Clock = () => time };
            Assert.Equal("Unavailable", handler.Execute("send_stop", App, fake)); Assert.Empty(fake.Calls);
            Assert.Equal("Sent", handler.Execute("send_stop", App, fake, "send"));
            Assert.Null(handler.Execute("send_stop", App, fake, "stop")); Assert.Single(fake.Calls);
            time = 1000;
            Assert.Equal("Stop Requested", handler.Execute("send_stop", App, fake, "stop"));
            Assert.Equal(new[] { "send", "exact" }, fake.Calls.Select(c => c.Name));
        }
        [Theory]
        [InlineData((Int32)DesktopActivity.Ready, true, "Send", "send")]
        [InlineData((Int32)DesktopActivity.Working, true, "Stop", "stop")]
        [InlineData((Int32)DesktopActivity.WaitingApproval, true, "Send", null)]
        [InlineData((Int32)DesktopActivity.Unavailable, true, "Send", null)]
        public void Home_send_face_follows_observed_activity(Int32 activity, Boolean canSend, String label, String intent)
        {
            var state = new DesktopState { Activity = (DesktopActivity)activity, CanSend = canSend };
            Assert.Equal(label, DesktopComposerCommand.FaceFor("send_stop", state).Label);
            Assert.Equal(intent, DesktopComposerCommand.SendStopIntent(state));
        }
        [Fact]
        public void Contextual_dictation_remains_a_draft_even_when_tapped_after_insertion()
        {
            var fake = new DesktopCommandRig.Automation(); var context = new DesktopContextCapture(fake); context.Execute("selection",new());
            var service = new DesktopWorkflowVoice(fake, new(fake), context); var capture = new VoiceCaptureState();
            var recipe = new DesktopWorkflowCommand.WorkflowDef { Id="context_voice", Input="voice", Prompt="{brief}" };
            Func<String,String> sink=null;
            service.Press(recipe,"ChatGPT",capture,new(fake),(i,s) => { capture.Press(i,DateTime.UnixEpoch); sink=s; }, allowSend:false);
            sink("reply politely"); capture.Finish();
            Assert.Equal("Draft Ready",service.Press(recipe,"ChatGPT",capture,new(fake),(_,_)=>Assert.Fail("Must not record"),allowSend:false));
            Assert.DoesNotContain(fake.Calls,c=>c.Name=="send"); Assert.Single(fake.Calls,c=>c.Name=="write");
        }
        [Fact]
        public void A_tools_mode_change_between_display_and_press_cannot_approve_or_capture()
        {
            var fake = new DesktopCommandRig.Automation { Next = new() { SurfaceAvailable=true,Mode="Codex" } };
            var result=DesktopToolsCommand.Execute("copy_approve",new() { Mode="ChatGPT" },fake,App,new(fake),new(),new(),()=>{},_=>throw new Exception());
            Assert.Equal("Mode Changed",result); Assert.Equal("status",Assert.Single(fake.Calls).Name);
        }
        [Theory]
        [InlineData("copy_approve")]
        [InlineData("deny")]
        public void Tools_approvals_keep_the_expected_card_and_two_press_high_risk_guard(String parameter)
        {
            var fake = new DesktopCommandRig.Automation { Next=new() { SurfaceAvailable=true,Mode="Codex" } };
            var shown = new DesktopState { Mode="Codex", Activity=DesktopActivity.WaitingApproval,CardText="fixture risky operation", Risk=ApprovalRisk.High };
            var confirmation=new DesktopApprovalConfirmation();
            void Tap() => DesktopToolsCommand.Execute(parameter,shown,fake,App,new(fake),new(),confirmation,()=>{},_=>throw new Exception());
            Tap(); Assert.DoesNotContain(fake.Calls,c=>c.Name=="guarded");
            Tap(); var press=Assert.Single(fake.Calls,c=>c.Name=="guarded"); Assert.Equal(shown.CardText,press.Text);
            Assert.Equal(parameter == "deny" ? App.DenyLabels : App.ApproveLabels, press.Labels);
        }
    }
}
