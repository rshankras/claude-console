using System;
using System.IO;
using System.Threading;
if (args.Length == 1 && args[0] == "--benchmark")
{
    var ticks = DateTime.UtcNow.Ticks;
    var timer = System.Diagnostics.Stopwatch.StartNew();
    for (var n = 0; n < 10000; n++)
    {
        using var held = SessionInputLock.TryAcquire(Environment.ProcessId, ticks);
        if (held == null) return 3;
    }
    Console.WriteLine($"Uncontended named-lock acquire/release: {timer.Elapsed.TotalMilliseconds / 10000:F4} ms average (10,000 iterations)");
    return 0;
}
using var input = SessionInputLock.TryAcquire(int.Parse(args[0]), long.Parse(args[1]), 5000);
if (input == null) return 3;
File.AppendAllText(args[2], args[3] + " text\n");
Thread.Sleep(80); // model the gap between text and Return in the real helper
File.AppendAllText(args[2], args[3] + " enter\n");
return 0;
