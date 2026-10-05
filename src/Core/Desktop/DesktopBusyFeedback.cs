namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;

    /// <summary>Short feedback for each rejected key; it never schedules or replays work.</summary>
    internal sealed class DesktopBusyFeedback : IDisposable
    {
        internal const Int32 HoldMs = 1800;
        private readonly Object _gate = new();
        private readonly Dictionary<String, Int64> _until = new();
        private readonly Action _changed;
        private readonly Func<Int64> _now;
        private Timer _timer;
        internal DesktopBusyFeedback(Action changed, Func<Int64> now = null)
        { _changed = changed; _now = now ?? (() => Environment.TickCount64); }

        internal Boolean Contains(String parameter)
        { lock (_gate) return _until.TryGetValue(parameter ?? "", out var until) && _now() < until; }

        internal void Show(String parameter)
        {
            lock (_gate)
            {
                RemoveExpired();
                _until[parameter ?? ""] = _now() + HoldMs;
                _timer ??= new Timer(_ => Expire(), null, 100, 100);
            }
            _changed();
        }

        internal void Clear(String parameter)
        {
            Boolean changed;
            lock (_gate) changed = _until.Remove(parameter ?? "");
            if (changed) _changed();
        }

        private Boolean RemoveExpired()
        {
            var expired = _until.Where(pair => pair.Value <= _now()).Select(pair => pair.Key).ToArray();
            foreach (var key in expired) _until.Remove(key);
            return expired.Length > 0;
        }

        internal void Expire()
        {
            Boolean changed;
            lock (_gate)
            {
                changed = RemoveExpired();
                if (_until.Count == 0) { _timer?.Dispose(); _timer = null; }
            }
            if (changed) _changed();
        }

        public void Dispose()
        { lock (_gate) { _until.Clear(); _timer?.Dispose(); _timer = null; } }
    }
}
