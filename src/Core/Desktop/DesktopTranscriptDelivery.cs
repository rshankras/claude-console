namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;

    /// <summary>The product's transcript sink, shared by live capture and the inert command rig.</summary>
    internal static class DesktopTranscriptDelivery
    {
        internal static String Write(IDesktopAutomation automation, String text, Boolean send, Action draftReady = null)
        {
            if (!automation.WriteComposer(text, send, out var error))
            {
                return error ?? "the composer did not accept the text";
            }
            if (!send) { automation.FocusApp(); draftReady?.Invoke(); }
            return null;
        }
    }
}
