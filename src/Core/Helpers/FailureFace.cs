namespace Loupedeck.ClaudeConsolePlugin
{
    using System;
    using System.Threading;

    /// <summary>
    /// The transient red "it failed, and here is why" face a voice key wears after a dictation
    /// goes wrong (#18). Companion to <see cref="ListeningFace"/>: one owner for the hold timer,
    /// so every voice key fails the same way and none re-implements it.
    ///
    /// The face is short-lived on purpose. The key's job is to tell the user what to do next —
    /// "Mic denied", "No speech" — and then get out of the way; a failure that stayed on the key
    /// would read as the key being broken rather than the last attempt having failed.
    /// </summary>
    internal sealed class FailureFace : IDisposable
    {
        // Long enough to read two words at a glance, short enough not to outlive the user's next
        // press. Overridable so tests need not wait it out.
        private const Int32 DefaultHoldMs = 2500;

        private readonly Action _onChange;
        private readonly Int32 _holdMs;
        private readonly Object _lock = new Object();
        private Timer _timer;
        private String _text;

        public FailureFace(Action onChange, Int32 holdMs = DefaultHoldMs)
        {
            _onChange = onChange;
            _holdMs = holdMs;
        }

        /// <summary>True while the failure is on display.</summary>
        public Boolean IsActive
        {
            get { lock (_lock) { return _text != null; } }
        }

        /// <summary>The words on the key, or null when nothing is being shown.</summary>
        public String Text
        {
            get { lock (_lock) { return _text; } }
        }

        /// <summary>Show <paramref name="text"/> now and clear it after the hold. A second failure restarts the hold.</summary>
        public void Show(String text)
        {
            lock (_lock)
            {
                _text = text;
                _timer?.Dispose();
                _timer = new Timer(_ => this.Clear(), null, _holdMs, Timeout.Infinite);
            }
            _onChange();
        }

        /// <summary>Take the face down now (the hold timer would have; a caller that knows better may too).</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _text = null;
                _timer?.Dispose();
                _timer = null;
            }
            _onChange();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
                _text = null;
            }
        }
    }
}
