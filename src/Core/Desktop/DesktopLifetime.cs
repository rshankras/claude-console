namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Collections.Generic;

    /// <summary>Paired subscriptions survive a Load/Unload cycle without retaining old
    /// plugin instances in the voice singleton. Disposal releases the registrations too.</summary>
    internal sealed class DesktopLifetime : IDisposable
    {
        private readonly Object _gate = new();
        private readonly List<(Action Add, Action Remove)> _bindings = new();
        private readonly List<Action> _stop = new();
        private Boolean _active = true, _disposed;
        private Int64 _generation;
        internal Boolean Active { get { lock (_gate) return _active && !_disposed; } }

        internal Func<Boolean> CaptureGuard()
        {
            Int64 generation;
            lock (_gate) generation = _generation;
            return () => { lock (_gate) return _active && !_disposed && generation == _generation; };
        }

        internal void Bind(Action add, Action remove)
        {
            lock (_gate)
            {
                if (_disposed) return;
                _bindings.Add((add, remove));
                if (_active) add();
            }
        }
        internal void OnStop(Action stop) { lock (_gate) { if (!_disposed) _stop.Add(stop); } }
        internal void Start()
        {
            lock (_gate)
            {
                if (_disposed || _active) return;
                _active = true;
                foreach (var binding in _bindings) binding.Add();
            }
        }
        internal void Stop()
        {
            Action[] cleanup;
            lock (_gate)
            {
                if (!_active) return;
                _active = false; _generation++;
                foreach (var binding in _bindings) binding.Remove();
                cleanup = _stop.ToArray();
            }
            foreach (var stop in cleanup) try { stop(); } catch { }
        }
        public void Dispose()
        {
            lock (_gate) { if (_disposed) return; _disposed = true; }
            Stop();
            lock (_gate) { _bindings.Clear(); _stop.Clear(); }
        }
    }
}
