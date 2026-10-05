#nullable enable
using System.Runtime.InteropServices;

namespace VizhiDesktopUia;

/// <summary>Keep one window root, never an action snapshot. Every Read executes a fresh query.
/// Only read-only queries may be retried after a provider invalidates an element.</summary>
internal sealed class UiaRootCache<T> where T : class
{
    private Int64 _hwnd;
    private Int32 _pid;
    private T? _root;
    internal void Invalidate() { _root = null; _hwnd = 0; _pid = 0; }

    internal TResult Read<TResult>(Int64 hwnd, Int32 pid, Func<T> acquire, Func<T, TResult> query)
    {
        if (_root == null || hwnd != _hwnd || pid != _pid)
        {
            Invalidate();
            _root = acquire(); _hwnd = hwnd; _pid = pid;
        }
        try { return query(_root); }
        catch (COMException)
        {
            // A window can be recreated with the same HWND/PID. The old provider then fails.
            // Reacquire once; if the fresh read fails too, leave no cached root behind.
            Invalidate();
            try
            {
                _root = acquire(); _hwnd = hwnd; _pid = pid;
                return query(_root);
            }
            catch { Invalidate(); throw; }
        }
    }
}
