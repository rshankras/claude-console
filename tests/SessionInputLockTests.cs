namespace Loupedeck.ClaudeConsolePlugin.Tests;
using System;
using System.Threading;
using Xunit;

public sealed class SessionInputLockTests
{
    [Fact] public void Waiting_request_times_out_but_other_target_and_generation_remain_available()
    {
        var ticks = DateTime.UtcNow.Ticks;
        using var held = SessionInputLock.TryAcquire(Environment.ProcessId, ticks);
        Assert.NotNull(held);
        bool timedOut = false, independent = false, nextGeneration = false;
        var thread = new Thread(() => {
            using var same = SessionInputLock.TryAcquire(Environment.ProcessId, ticks, 30);
            timedOut = same == null;
            using var other = SessionInputLock.TryAcquire(Environment.ProcessId + 1, ticks, 0);
            independent = other != null;
            using var next = SessionInputLock.TryAcquire(Environment.ProcessId, ticks + 1, 0);
            nextGeneration = next != null;
        });
        thread.Start(); Assert.True(thread.Join(3000));
        Assert.True(timedOut); Assert.True(independent); Assert.True(nextGeneration);
    }
    [Fact] public void Completed_delivery_releases_next_request()
    {
        var ticks = DateTime.UtcNow.Ticks;
        var held = SessionInputLock.TryAcquire(Environment.ProcessId, ticks);
        using var waiting = new ManualResetEventSlim();
        bool acquired = false;
        var thread = new Thread(() => {
            waiting.Set();
            using var next = SessionInputLock.TryAcquire(Environment.ProcessId, ticks, 2000);
            acquired = next != null;
        });
        thread.Start(); Assert.True(waiting.Wait(1000)); held.Dispose();
        Assert.True(thread.Join(3000)); Assert.True(acquired);
    }
    [Fact] public void Abandoned_delivery_is_rejected_then_lock_can_be_recovered()
    {
        var ticks = DateTime.UtcNow.Ticks;
        using var keepAlive = new Mutex(false, SessionInputLock.Name(Environment.ProcessId, ticks));
        var thread = new Thread(() => { keepAlive.WaitOne(); });
        thread.Start(); Assert.True(thread.Join(3000));
        Assert.Null(SessionInputLock.TryAcquire(Environment.ProcessId, ticks, 0));
        using var retry = SessionInputLock.TryAcquire(Environment.ProcessId, ticks, 0);
        Assert.NotNull(retry);
    }
    [Theory] [InlineData(0, 1)] [InlineData(1, 0)] [InlineData(-1, 1)]
    public void Unverified_identity_is_rejected(int pid, long ticks) => Assert.Null(SessionInputLock.TryAcquire(pid, ticks, 0));
}
