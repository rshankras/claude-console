namespace Loupedeck.ClaudeConsolePlugin.DesktopActions
{
    using System;
    using Loupedeck.ClaudeConsolePlugin.Desktop;

    public sealed class DesktopSearchCommand : DesktopCommandBase
    {
        public DesktopSearchCommand()
        {
            this.SetWidget(true);
            this.AddParameter("speak", "Speak Query", "Find Chat");
            this.AddParameter("type", "Type Now", "Find Chat");
            this.AddParameter("status", "Search Query", "Find Chat");
            if (!DesktopServices.Declared) return;
            DesktopServices.OnSearchChanged(() => this.ActionImageChanged());
            DesktopServices.OnVoiceChanged(() => this.ActionImageChanged());
            DesktopServices.OnVoiceFailed((intent, feedback) =>
            {
                if (intent == VoiceIntent.DesktopSearch) DesktopServices.Search.ShowFeedback(feedback);
            });
        }

        internal static void Execute(String parameter, DesktopSearch search, DesktopVoiceActions voice,
            VoiceCaptureState capture, Action<VoiceIntent, Func<String, String>> toggle, Action close)
        {
            if (parameter == "type") { search.TypeQuery(); return; }
            if (parameter == "status") { search.TypeQuery(); return; }
            if (parameter == "speak")
            {
                if (capture.Phase != VoicePhase.Idle && capture.Intent != VoiceIntent.DesktopSearch)
                { search.ShowFeedback("Dictating"); return; }
                var sink = capture.Phase == VoicePhase.Idle ? search.CaptureSink() : null;
                if (capture.Phase == VoicePhase.Idle && sink == null) { search.ShowFeedback(DesktopSearch.ProblemFor(search.Current.Error).Hint); return; }
                voice.RequestDictation(VoiceIntent.DesktopSearch, capture, intent => toggle(intent, sink), out var feedback);
                if (feedback != null) search.ShowFeedback(feedback);
                return;
            }
            if (search.Select(parameter)) close();
        }

        protected override void RunCommand(String parameter)
        {
            RunDesktopAction(parameter, () => this.RunDesktopCommand(parameter));
        }

        private void RunDesktopCommand(String parameter)
        {
            if (!DesktopServices.Declared) return;
            Execute(parameter, DesktopServices.Search, DesktopServices.VoiceActions, BridgeManager.Instance.Voice,
                (intent, sink) =>
                {
                    if (sink != null) BridgeManager.Instance.SearchTranscriptSink = sink;
                    BridgeManager.Instance.ToggleVoice(intent);
                },
                // Leaving the page is the folder's job (DesktopSearch.Selected → FindChatDynamicFolder.Close).
                // The generic NavigateUp action used here before is refused by the host ("Unknown
                // command '@DynamicFolderGoUp'", device 2 Oct) and left the folder open (#151).
                () => { });
            this.ActionImageChanged();
        }

        protected override String GetCommandDisplayName(String parameter, PluginImageSize size) => "\u200B";
        protected override BitmapImage GetDesktopCommandImage(String parameter, PluginImageSize size)
        {
            if (!DesktopServices.Declared) return KeyImage.RenderControlTile(size, "Find Chat", "search", false, "Unavailable");
            var search = DesktopServices.Search;
            var current = search.Current;
            var capture = BridgeManager.Instance.Voice;
            var problem = DesktopSearch.ProblemFor(current.Error);
            if (parameter == "speak")
            {
                var ours = capture.Phase != VoicePhase.Idle && capture.Intent == VoiceIntent.DesktopSearch;
                if (!ours && !current.Available) return KeyImage.RenderControlTile(size, "Speak Query", "voice", false, problem.Hint);
                var label = ours ? capture.Phase switch { VoicePhase.Recording => "Listening", VoicePhase.Transcribing => "Searching", _ => "Starting" } : "Speak Query";
                return KeyImage.RenderIntentTile(size, label, "voice", ours && capture.Phase == VoicePhase.Recording ? "PRESS TO END" : search.Feedback ?? (current.Available ? "SEARCH" : "USE APP"));
            }
            if (parameter == "type") return KeyImage.RenderControlTile(size, current.Available ? "Type Now" : "Open Search", current.Available ? "writing" : "search", true, current.Available ? "Tap to Focus" : "Retry");
            if (parameter == "status") return DesktopConversationRenderer.Render(size,
                current.Available ? String.IsNullOrWhiteSpace(current.Query) ? "Speak or Type" : current.Query : "Open Search",
                search.Feedback ?? (current.Available ? current.Query.Length == 0 ? "TAP TO TYPE" : current.Results.Count == 0 ? "NO MATCHES YET" : $"{current.Results.Count} FOUND · EDIT" : "TAP TO RETRY"), KeyImage.Gray, false);
            var result = search.Resolve(parameter);
            return DesktopConversationRenderer.Render(size, result == null ? "Result changed" : DesktopConversationLabels.Display(search.Mode, result.Title),
                result == null ? "Refresh" : "Open chat", KeyImage.Gray, false);
        }
    }
}
