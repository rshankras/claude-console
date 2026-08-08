namespace Loupedeck.ClaudeConsolePlugin.Platform
{
    using System;

    /// <summary>
    /// A key the plugin can send to a Claude session, named by intent rather than by any one
    /// platform's encoding. macOS maps these to AppleScript key codes; Windows maps them to
    /// virtual-key codes in an INPUT_RECORD. Action classes only ever name the key.
    /// </summary>
    public enum TerminalKey
    {
        Escape,
        Return,
        Tab,
        ArrowUp,
        ArrowDown,
        ArrowLeft,
        ArrowRight,
        PageUp,
        PageDown,
    }

    /// <summary>Chord modifiers. Command is macOS-only; Windows bridges must reject it.</summary>
    [Flags]
    public enum KeyModifiers
    {
        None = 0,
        Shift = 1,
        Control = 2,
        Alt = 4,
        Command = 8,
    }

    /// <summary>A single key press, optionally with modifiers (e.g. Shift+Tab to cycle modes).</summary>
    public readonly record struct KeyStroke(TerminalKey Key, KeyModifiers Modifiers = KeyModifiers.None)
    {
        public static KeyStroke Escape => new(TerminalKey.Escape);
        public static KeyStroke Return => new(TerminalKey.Return);
        public static KeyStroke Tab => new(TerminalKey.Tab);
        public static KeyStroke ArrowUp => new(TerminalKey.ArrowUp);
        public static KeyStroke ArrowDown => new(TerminalKey.ArrowDown);
        public static KeyStroke PageUp => new(TerminalKey.PageUp);
        public static KeyStroke PageDown => new(TerminalKey.PageDown);

        /// <summary>Shift+Tab — cycles Claude Code's input modes.</summary>
        public static KeyStroke ShiftTab => new(TerminalKey.Tab, KeyModifiers.Shift);

        public override String ToString() =>
            this.Modifiers == KeyModifiers.None ? this.Key.ToString() : $"{this.Modifiers}+{this.Key}";
    }

    /// <summary>
    /// Terminal navigation the keypad can drive. These are whole-application gestures (not typing
    /// into a session), so they are not session-targeted and are not covered by the injection guard.
    /// </summary>
    public enum TerminalAction
    {
        /// <summary>Bring the terminal application to the front.</summary>
        Activate,
        /// <summary>Open a new tab — a fresh shell, no claude.</summary>
        NewTab,
        /// <summary>Open a new tab AND start a claude session in it.</summary>
        NewClaudeTab,
        NextTab,
        PreviousTab,
        /// <summary>Open a new WINDOW running claude.</summary>
        NewClaudeWindow,
        NextWindow,
        PreviousWindow,
    }

    /// <summary>
    /// Why an injection did or didn't happen. The guard's whole point is that "we could not reach
    /// the target" is a distinct, visible outcome from "typed it" — never a silent type-somewhere-else.
    /// </summary>
    public enum InjectionOutcome
    {
        /// <summary>Focused the target session and typed.</summary>
        Ok,
        /// <summary>The terminal application isn't running — nothing was typed (we never auto-launch it).</summary>
        NoTerminal,
        /// <summary>The terminal is running but the target session is gone — nothing was typed.</summary>
        SessionMissing,
        /// <summary>
        /// The target session is elevated and unreachable from this (non-elevated) process.
        /// Windows-only in practice: AttachConsole fails with error 5 across integrity levels.
        /// </summary>
        SessionElevated,
        /// <summary>The injection mechanism itself failed (timeout, permission, crash).</summary>
        Failed,
        /// <summary>Nothing to do — e.g. empty text.</summary>
        Skipped,
        /// <summary>This platform has no injection backend.</summary>
        Unsupported,
    }
}
