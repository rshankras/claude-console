namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    public sealed class DesktopCaptureCommand : DesktopCommandBase
    {
        private readonly FailureFace _feedback;
        private String _parameter, _working;
        private Int32 _busy;
        public DesktopCaptureCommand()
        {
            this.SetWidget(true);
            _feedback = new FailureFace(() => this.ActionImageChanged(), 6000);
            DesktopServices.Lifetime.OnStop(_feedback.Dispose);
            DesktopServices.Lifetime.OnStop(() => { _working = null; Volatile.Write(ref _busy, 0); });
            if (!DesktopServices.Declared) return;
            this.AddParameter("selection", "Use Selection", "Context").SetDescription("Capture highlighted text, then open ChatGPT. Does not send.");
            this.AddParameter("clipboard", "Paste into Chat", "Context").SetDescription("Insert copied text into the current chat draft immediately, preserving existing text. Review, then press Send.");
            this.AddParameter("screenshot", "Screenshot", "Context").SetDescription("Choose a screen region and attach it to the current chat draft. Add your instruction, review, then press Send.");
            this.AddParameter("copy", "Copy Reply", "Context").SetDescription("Copy the latest completed answer in the open chat and retain it for Paste Reply. No text selection needed.");
            this.AddParameter("return", "Return to App", "Context").SetDescription("Return to the captured source window.");
            this.AddParameter("paste", "Paste Reply", "Context").SetDescription("Insert the retained reply into the empty field you selected in the source app. Never sends.");
            this.AddParameter("clear", "Clear Sources", "Context").SetDescription("Clear staged sources without changing app input or the clipboard.");
            DesktopServices.OnContextChanged(() => this.ActionImageChanged());
            DesktopServices.OnMonitorChanged(_ => this.ActionImageChanged());
        }
        protected override void RunCommand(String parameter)
        {
            if (!DesktopServices.Declared) return;
            if (parameter == "copy") DesktopServices.Context.TraceCopy("key-pressed");
            if (Interlocked.Exchange(ref _busy, 1) != 0)
            {
                if (parameter == "copy") DesktopServices.Context.TraceCopy("command-busy");
                return;
            }
            _working = parameter;
            // The system screenshot picker waits on the user; never block SDK rendering/dispatch.
            if (!DesktopServices.Run(() =>
            {
                try
                {
                    _feedback.Clear(); this.ActionImageChanged();
                    _parameter = parameter; _feedback.Show(DesktopServices.Context.Execute(parameter, BridgeManager.Instance.Voice));
                }
                finally { _working = null; Volatile.Write(ref _busy, 0); this.ActionImageChanged(); }
            }))
            {
                if (parameter == "copy") DesktopServices.Context.TraceCopy("action-unavailable");
                _working = null; Volatile.Write(ref _busy, 0);
            }
        }
        internal static (String Label, String Icon, String Footer) Face(String parameter, DesktopContextCapture context, String feedback = null, Boolean working = false)
        {
            var (label, icon) = parameter switch
            {
                "selection" => ("Use Selection", "document"), "clipboard" => ("Paste into Chat", "copy"),
                "screenshot" => ("Screenshot", "screenshot"), "copy" => ("Copy Reply", "copy"),
                "return" => ("Return to App", "quick_chat"), "paste" => ("Paste Reply", "writing"),
                _ => ("Clear Added", "stop"),
            };
            var footer = parameter switch
            {
                "clipboard" => feedback is "Pasted" or "Attached" ? "REVIEW · SEND" : "CLIPBOARD",
                "copy" => "LATEST ANSWER", "return" => context?.HasSource == true ? context.SourceName : "NO SOURCE",
                "paste" => context?.HasReply == true ? "EMPTY REPLY BOX" : "COPY FIRST",
                _ => context?.Count > 0 ? $"{context.Count} SOURCE{(context.Count == 1 ? "" : "S")}" : "ADD CONTENT",
            };
            if (parameter == "clipboard")
                return (working ? context?.AttachingImage == true ? "Attaching" : "Pasting" : feedback is "Pasted" or "Attached" ? feedback : label, icon,
                    working ? (context?.ClipboardKind ?? "Text").ToUpperInvariant() + " · WAIT" : feedback != null && feedback is not ("Pasted" or "Attached") ? feedback : footer);
            if (parameter == "screenshot")
                return (working ? context?.AttachingImage == true ? "Attaching" : "Select Area" : feedback == "Attached" ? "Attached" : label,
                    icon, working ? context?.AttachingImage == true ? "WAIT" : "ESC TO CANCEL"
                        : feedback == "Attached" ? "REVIEW · SEND" : feedback ?? "ADD TO CHAT");
            return (working ? parameter == "screenshot" ? "Select Area" : "Working" : feedback ?? label,
                icon, working ? parameter == "screenshot" ? "ESC TO CANCEL" : "WAIT" : footer);
        }
        protected override String GetCommandDisplayName(String _, PluginImageSize __) => "\u200B";
        internal static (String Label, String Icon, Boolean Enabled, String Footer) CopyFace(
            DesktopState state, String feedback = null, Boolean copying = false)
        {
            var waiting = state.Activity is DesktopActivity.Working or DesktopActivity.WaitingApproval
                || state.VoiceChat == DesktopVoiceState.Active
                || state.Conversations.Any(c => c.Selected && c.State is ConversationState.Running or ConversationState.Awaiting);
            var enabled = state.Available && state.CanCopyAnswer && !waiting;
            var unavailable = String.IsNullOrEmpty(state.CopyAnswerError) ? "No answer" : DesktopContextCapture.Problem(state.CopyAnswerError);
            var footer = !state.Available ? "Open Chat" : waiting ? "Wait" : enabled ? "LATEST ANSWER" : unavailable;
            return (copying ? "Copying" : feedback == "Copied" ? "Copied" : "Copy Reply", "copy", feedback == "Copied" || enabled,
                copying ? "WAIT" : feedback == "Copied" ? "READY TO PASTE" : feedback ?? footer);
        }

        internal static BitmapImage RenderCopy(PluginImageSize size, DesktopState state, String feedback, Boolean copying)
        {
            var face = CopyFace(state, feedback, copying);
            return KeyImage.RenderControlTile(size, face.Label, face.Icon, face.Enabled, face.Footer);
        }
        protected override BitmapImage GetCommandImage(String parameter, PluginImageSize size)
        {
            if (parameter == "copy") return RenderCopy(size,
                DesktopServices.Declared ? DesktopServices.Monitor.Current : DesktopState.Unavailable,
                _feedback.IsActive && _parameter == parameter ? _feedback.Text : null, _working == parameter);
            var face = Face(parameter, DesktopServices.Declared ? DesktopServices.Context : null,
                _feedback.IsActive && _parameter == parameter ? _feedback.Text : null, _working == parameter);
            return KeyImage.RenderIntentTile(size, face.Label, face.Icon, face.Footer);
        }
    }
}
