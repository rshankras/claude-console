namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;

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

        /// <summary>
        /// Press the first control matching any of <paramref name="labels"/> WITHOUT focusing
        /// the app. Returns false (matched = null) when nothing matched or the press failed.
        /// </summary>
        Boolean Press(String[] labels, out String matched);

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

        /// <summary>Switch the app to <paramref name="modeName"/> (switcher press, then menu pick).</summary>
        Boolean SwitchMode(String modeName);

        /// <summary>The one deliberate focus: bring the app's window forward.</summary>
        Boolean FocusApp();
    }
}
