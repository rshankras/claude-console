namespace Loupedeck.ClaudeConsolePlugin.Desktop
{
    using System;
    using System.Threading.Tasks;

    /// <summary>One desktop gesture at a time, without holding Logitech's input callback.
    /// No backlog: a second gesture is refused while the current one finishes.</summary>
    internal sealed class DesktopActionRunner
    {
        private readonly Object _gate = new();
        private Boolean _active = true, _busy;
        private Int64 _generation;
        internal Action<Action> Schedule { get; set; } = work => Task.Run(work);
        internal Boolean IsBusy { get { lock (_gate) return _busy; } }
        internal Boolean Active { get { lock (_gate) return _active; } }

        internal Boolean TryRun(Action work, Action completed = null)
        {
            Int64 generation;
            lock (_gate)
            {
                if (!_active || _busy) return false;
                _busy = true; generation = _generation;
            }
            try
            {
                Schedule(() =>
                {
                    try
                    {
                        lock (_gate) if (!_active || generation != _generation) return;
                        work();
                    }
                    catch (Exception ex) { PluginLog.Warning(ex, "Desktop action failed"); }
                    finally
                    {
                        Boolean notify;
                        lock (_gate) { _busy = false; notify = _active && generation == _generation; }
                        if (notify) try { completed?.Invoke(); } catch { }
                    }
                });
                return true;
            }
            catch
            {
                lock (_gate) _busy = false;
                return false;
            }
        }

        internal void Start() { lock (_gate) _active = true; }
        internal void Stop() { lock (_gate) { _active = false; _generation++; } }
    }
}
