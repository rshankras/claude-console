namespace Loupedeck.ClaudeConsolePlugin
{
    using System;

    /// <summary>
    /// Where a finished transcript is sent. Fixed when recording STARTS — see below.
    /// Public only so the test suite can drive it as a [Theory] parameter; nothing outside the
    /// plugin consumes it.
    /// </summary>
    public enum VoiceIntent
    {
        /// <summary>Type it into the session and press Return (the Voice key).</summary>
        Send,

        /// <summary>Type it into the session and leave it there (the Voice Draft key).</summary>
        Draft,

        /// <summary>Treat it as a project name and open that project (the Go to Project key).</summary>
        Project,

        /// <summary>
        /// Hand it to the product's transcript sink and submit — a voice key aimed at something
        /// that is not a terminal, such as a desktop app's composer. The engine never learns what
        /// the sink is; see BridgeManager.TranscriptSink.
        /// </summary>
        Desktop,

        /// <summary>Hand it to the product's transcript sink and leave it there for review.</summary>
        DesktopDraft,

        /// <summary>Search query only. A separate sink is pinned when capture starts.</summary>
        DesktopSearch,
    }

    internal enum VoicePhase
    {
        Idle,
        Starting,
        Cancelling,
        Recording,
        Transcribing,
    }

    /// <summary>What a press should cause, decided by the state machine rather than by the key.</summary>
    internal enum VoiceAction
    {
        /// <summary>Nothing was running: start recording with the pressed key's intent.</summary>
        Start,

        /// <summary>Something was running: stop it. The intent is the STARTING key's, not this one's.</summary>
        Stop,

        /// <summary>Cancel startup; its worker retains ownership until the helper is stopped.</summary>
        Cancel,

        /// <summary>A transcript is already in flight; refuse and alert rather than discard it.</summary>
        Refuse,
    }

    /// <summary>
    /// The single owner of "is the microphone running, and where is the result going?" (#28).
    ///
    /// Before this, the answer lived in each KEY — three private copies, in Voice, Voice Draft and
    /// Go to Project, with none in the engine. Pressing a second voice key while the first was
    /// recording therefore launched a SECOND helper process against the same WAV, the same stop flag
    /// and the same transcript path.
    ///
    /// The duplicate process was the lesser problem. Because each key both started and routed, the
    /// destination was decided by whichever key you pressed SECOND: dictate a prompt, press Go to
    /// Project to stop, and the prompt was fuzzy-matched against project names and a project opened;
    /// the other way round and a project name was typed into the session and sent. A mis-press was
    /// enough.
    ///
    /// Hence the rule this class exists to enforce: <b>intent is fixed when recording starts, and a
    /// press of ANY voice key while recording means stop.</b> Nothing a later press can do changes
    /// where the transcript goes.
    ///
    /// Kept free of I/O and of the clock so the transitions can be tested exhaustively without a
    /// microphone; the caller passes the time in.
    /// </summary>
    internal sealed class VoiceCaptureState
    {
        /// <summary>
        /// How long a recording may sit before a new press treats it as dead. The helper stops
        /// itself at 60s and the transcript wait runs 20s, so anything past the sum means the helper
        /// died without a trace. A flag that could not expire would be worse than the bug it fixes:
        /// one crashed helper and every voice key is dead until the plugin reloads.
        /// </summary>
        internal static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);

        private readonly Object _lock = new Object();

        private VoicePhase _phase = VoicePhase.Idle;
        private VoiceIntent _intent;
        private DateTime _since;

        internal VoicePhase Phase { get { lock (this._lock) { return this._phase; } } }

        /// <summary>The intent of the capture in flight; meaningless while Idle.</summary>
        internal VoiceIntent Intent { get { lock (this._lock) { return this._intent; } } }

        /// <summary>Raised on every phase change, so the keys can repaint. Never raised while holding the lock.</summary>
        internal event Action Changed;

        /// <summary>Closing a voice surface can stop its capture, but can never start one.</summary>
        internal VoiceAction StopIfCapturing(VoiceIntent intent, DateTime now)
        {
            VoiceAction action;
            lock (_lock)
            {
                if (_intent != intent) return VoiceAction.Refuse;
                if (_phase == VoicePhase.Starting)
                { _phase = VoicePhase.Cancelling; action = VoiceAction.Cancel; }
                else if (_phase == VoicePhase.Recording)
                { _phase = VoicePhase.Transcribing; _since = now; action = VoiceAction.Stop; }
                else return VoiceAction.Refuse;
            }
            Changed?.Invoke();
            return action;
        }

        /// <summary>
        /// Decide what a press of the key with <paramref name="pressed"/> intent should do, and move
        /// the state accordingly. Returns the action to perform and, for a Stop, the intent the
        /// transcript must be routed to.
        /// </summary>
        internal (VoiceAction Action, VoiceIntent Intent) Press(VoiceIntent pressed, DateTime now, Boolean awaitReadiness = false)
        {
            var result = this.PressLocked(pressed, now, awaitReadiness);
            if (result.Changed)
            {
                this.Changed?.Invoke();
            }
            return (result.Action, result.Intent);
        }

        private (VoiceAction Action, VoiceIntent Intent, Boolean Changed) PressLocked(VoiceIntent pressed, DateTime now, Boolean awaitReadiness)
        {
            lock (this._lock)
            {
                // A capture older than any capture can legitimately be means the helper died without
                // writing anything. Treat it as over, so this press starts cleanly.
                if (this._phase is VoicePhase.Recording or VoicePhase.Transcribing && now - this._since > StaleAfter)
                {
                    this._phase = VoicePhase.Idle;
                }

                switch (this._phase)
                {
                    case VoicePhase.Starting:
                        this._phase = VoicePhase.Cancelling;
                        return (VoiceAction.Cancel, this._intent, true);

                    case VoicePhase.Cancelling:
                        return (VoiceAction.Refuse, this._intent, false);

                    case VoicePhase.Recording:
                        // ANY voice key stops the running capture, and the ORIGINAL intent survives.
                        this._phase = VoicePhase.Transcribing;
                        this._since = now;
                        return (VoiceAction.Stop, this._intent, true);

                    case VoicePhase.Transcribing:
                        // A result is in flight. Starting now would delete the transcript file the
                        // waiting thread is about to read.
                        return (VoiceAction.Refuse, this._intent, false);

                    default:
                        this._phase = awaitReadiness ? VoicePhase.Starting : VoicePhase.Recording;
                        this._intent = pressed;
                        this._since = now;
                        return (VoiceAction.Start, pressed, true);
                }
            }
        }

        /// <summary>A late readiness acknowledgement cannot revive a cancelled start.</summary>
        internal Boolean MarkReady(DateTime now)
        {
            lock (this._lock)
            {
                if (this._phase != VoicePhase.Starting) { return false; }
                this._phase = VoicePhase.Recording;
                this._since = now;
            }
            this.Changed?.Invoke();
            return true;
        }

        internal String StartupLabel(VoiceIntent intent)
        {
            lock (this._lock)
            {
                if (this._intent != intent) { return null; }
                return this._phase switch
                {
                    VoicePhase.Starting => "Starting",
                    VoicePhase.Cancelling => "Cancelling",
                    _ => null,
                };
            }
        }

        /// <summary>
        /// The capture is over — transcript consumed, failure reported, or the wait timed out. Every
        /// exit path must reach this, or the keys stay locked. Idempotent.
        /// </summary>
        internal void Finish()
        {
            Boolean changed;
            lock (this._lock)
            {
                changed = this._phase != VoicePhase.Idle;
                this._phase = VoicePhase.Idle;
            }

            if (changed)
            {
                this.Changed?.Invoke();
            }
        }

        /// <summary>True when the key with this intent is the one currently recording.</summary>
        internal Boolean IsRecording(VoiceIntent intent)
        {
            lock (this._lock)
            {
                return this._phase == VoicePhase.Recording && this._intent == intent;
            }
        }

        internal Boolean IsTranscribing(VoiceIntent intent)
        {
            lock (this._lock)
            {
                return this._phase == VoicePhase.Transcribing && this._intent == intent;
            }
        }
    }
}
