namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;

    /// <summary>An opaque composer identity and content fingerprint; never contains draft text.</summary>
    internal sealed class DesktopAppendTarget
    {
        public String Target { get; init; }
        public String Fingerprint { get; init; }
        public Boolean HasContent { get; init; }
    }

    /// <summary>
    /// The platform half of the desktop seam: how we read and actuate a GUI app's controls.
    /// macOS implements it over the AX helper (<see cref="MacDesktopAutomation"/>); Windows uses
    /// the UIA helper through <see cref="WindowsDesktopAutomation"/>. Nothing above this interface
    /// may know which.
    ///
    /// Every method is synchronous and bounded — implementations run one short-lived helper
    /// invocation per call under a kill-on-timeout budget, so a caller can never be wedged by a
    /// locked screen or a hung window server. Failure is a return value, never an exception:
    /// keys report failure states, they don't crash the service.
    /// </summary>
    internal interface IDesktopAutomation
    {
        /// <summary>One observation of the app's UI. Never null — worst case is Unavailable.</summary>
        DesktopSnapshot Status();
        /// <summary>Cheap process-only foreground check for passive polling; no UI traversal.</summary>
        Boolean? IsAppFrontmost() => true;

        /// <summary>Search-only operations. Unsupported platforms never fall back to the composer.</summary>
        DesktopSearchSnapshot Search(String action, String target = null, String query = null, String value = null, String title = null, String origin = null) => new();

        /// <summary>
        /// Press the first control matching any of <paramref name="labels"/> WITHOUT focusing
        /// the app. Returns false (matched = null) when nothing matched or the press failed.
        /// </summary>
        Boolean Press(String[] labels, out String matched);

        /// <summary>Press one enabled button with an exact label; ambiguous matches fail closed.</summary>
        Boolean PressExact(String[] labels) => false;

        /// <summary>Request a specific native voice transition, never a blind toggle.</summary>
        Boolean SetVoiceChat(Boolean active, out String error) { error = "unsupported"; return false; }

        /// <summary>A user-configured toggle is independent of AX voice-state recognition.</summary>
        Boolean HasVoiceShortcut => false;
        Boolean ToggleVoiceChat(out String error) { error = "unsupported"; return false; }

        /// <summary>Open one exact, unambiguous sidebar title. Unsupported helpers fail closed.</summary>
        Boolean PressConversation(String title) => false;

        /// <summary>Press a contextual destination only if the observed mode still matches.</summary>
        Boolean PressInMode(String[] labels, String mode, out String matched) { matched = null; return false; }

        /// <summary>Ensure the current Codex diff panel is visible; never blindly toggle it.</summary>
        Boolean OpenChanges(out String error) { error = "unsupported"; return false; }

        /// <summary>
        /// Press with the expected-card guard: <paramref name="expectCard"/> is the card text
        /// the keypad RENDERED; the press is refused ("card-changed") when the card beside the
        /// control no longer matches — closing the race where the seen card resolves and a new
        /// one appears between the glance and the thumb.
        /// </summary>
        Boolean PressGuarded(String[] labels, String expectCard, out String matched, out String error);

        /// <summary>
        /// Put <paramref name="text"/> into the app's composer and optionally submit it.
        /// The desktop injection law: the text lands in the composer of the conversation the
        /// user targeted, or nowhere, and the failure is reported.
        /// </summary>
        Boolean WriteComposer(String text, Boolean send, out String error);

        /// <summary>Retry a retained draft; an exact, already-inserted copy counts as success.</summary>
        Boolean RecoverDraft(String text, out String error) => WriteComposer(text, false, out error);

        String PrepareDraft(String mode, Boolean requireEmpty, out String error) { error = "unsupported"; return null; }
        Boolean WritePreparedDraft(String text, String mode, String target, Boolean retry, out String error) { error = "unsupported"; return false; }
        Boolean WritePreparedPrompt(String text, String mode, String target, Boolean send, out String error) { error = "unsupported"; return false; }
        Boolean SupportsAppend => false;
        DesktopAppendTarget PrepareAppend(String mode, out String error) { error = "unsupported"; return null; }
        Boolean AppendPreparedDraft(String text, String mode, DesktopAppendTarget target, Boolean retry, out String error) { error = "unsupported"; return false; }
        Boolean SendPreparedDraft(String mode, String target, out String error) { error = "unsupported"; return false; }
        /// <summary>Submit a freshly inserted preset only while its complete text is unchanged.</summary>
        Boolean SendPreparedPrompt(String text, String mode, String target, out String error) { error = "unsupported"; return false; }

        /// <summary>Submit the existing draft without replacing it. Unsupported helpers fail closed.</summary>
        Boolean SendComposer(out String error) { error = "unsupported"; return false; }

        /// <summary>Copy only an identifiable, completed assistant answer.</summary>
        DesktopCaptureResult Context(String action, String source = null, String text = null) => new() { Error = "unsupported" };
        Boolean AttachPreparedImage(String path, String mode, String target, out String error) { error = "unsupported"; return false; }
        Boolean AttachPreparedFiles(DesktopFile[] files, String mode, String target, out String error) { error = "unsupported"; return false; }

        Boolean SupportsCopyAnswer => false;
        Boolean CopyAnswer(out String error) { error = "unsupported"; return false; }

        /// <summary>Switch the app to <paramref name="modeName"/> (switcher press, then menu pick).</summary>
        Boolean SwitchMode(String modeName);

        /// <summary>The one deliberate focus: bring the app's window forward.</summary>
        Boolean FocusApp();
    }
}
