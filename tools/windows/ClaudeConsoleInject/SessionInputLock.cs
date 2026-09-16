#nullable enable
using System;
using System.Threading;

// Separate helper invocations must keep text + settle + Return together. Keying by the process
// generation also prevents an old queued request from owning a recycled PID's input lock.
internal sealed class SessionInputLock : IDisposable
{
    private readonly Mutex mutex;
    private SessionInputLock(Mutex mutex) => this.mutex = mutex;
    internal static string Name(int pid, long ticks) => $"Local\\VizhiConsole.Input.{pid}.{ticks}";

    internal static SessionInputLock? TryAcquire(int pid, long ticks, int timeoutMs = 1500)
    {
        if (pid <= 0 || ticks <= 0) { return null; }
        var mutex = new Mutex(false, Name(pid, ticks));
        try
        {
            if (mutex.WaitOne(timeoutMs)) { return new SessionInputLock(mutex); }
        }
        catch (AbandonedMutexException)
        {
            // The previous helper may have died after writing half a command. Do not append a
            // new command to that unknown composer state. WaitOne grants ownership in this case.
            mutex.ReleaseMutex();
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
