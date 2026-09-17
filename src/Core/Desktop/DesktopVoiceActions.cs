namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;

    internal enum DesktopVoiceState { Unavailable, Ready, Active }

    /// <summary>Serializes this plugin's native Voice and dictation requests, across key instances.</summary>
    internal sealed class DesktopVoiceActions
    {
        private readonly IDesktopAutomation _automation;
        private readonly Object _gate = new Object();
        private Int64? _lastVoiceRequest;
        internal Func<Int64> Clock { get; set; } = () => Environment.TickCount64;

        public DesktopVoiceActions(IDesktopAutomation automation) => _automation = automation;

        public Boolean RequestVoice(DesktopVoiceState seen, VoiceCaptureState capture, out String feedback)
        {
            lock (_gate)
            {
                feedback = null;
                // A rapid second press must not reverse a request whose face has just refreshed.
                if (_lastVoiceRequest.HasValue && Clock() - _lastVoiceRequest.Value < 1200) { return false; }
                if (seen == DesktopVoiceState.Unavailable) { feedback = "No Voice"; return false; }
                var start = seen == DesktopVoiceState.Ready;
                if (start && capture.Phase != VoicePhase.Idle) { feedback = "Dictating"; return false; }
                // The helper checks the expected state and exact button again in one pinned window.
                // An outdated End request can never start a new session, or vice versa.
                if (!_automation.SetVoiceChat(start, out var error))
                {
                    feedback = error == "voice-state-changed" ? "Changed" : "Check App";
                    PluginLog.Warning($"DesktopVoiceActions: {error}");
                    return false;
                }
                _lastVoiceRequest = Clock();
                // A successful press is not proof of recording: first use can open setup/permissions.
                feedback = "Check App";
                return true;
            }
        }

        public Boolean RequestDictation(VoiceIntent intent, VoiceCaptureState capture,
            Action<VoiceIntent> toggle, out String feedback)
        {
            lock (_gate)
            {
                feedback = null;
                // Always allow an existing local capture to stop/cancel using its original intent.
                if (capture.Phase == VoicePhase.Idle)
                {
                    if (_lastVoiceRequest.HasValue && Clock() - _lastVoiceRequest.Value < 1200)
                    { feedback = "Check App"; return false; }
                    if (_automation.Status().VoiceChat == DesktopVoiceState.Active)
                    { feedback = "End Voice"; return false; }
                }
                toggle(intent);
                return true;
            }
        }
    }
}
