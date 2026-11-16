using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Slice.Tests;

/// <summary>
/// Where the main thread was when it stopped coming past the frame's markers: each marker (the frame split's edges, the
/// system split's marks) stamps its name and the time, and a watchdog thread looking every 2 ms notes any stretch of more
/// than <see cref="StallMs"/> with no stamp, after which marker it began, how long it lasted, and the garbage collector's
/// pause and collections over it. A stall that no system's time accounts for still names the marker it followed.
/// </summary>
public static class StallWatch
{
    public static double StallMs = 40;
    static volatile string _after = "(start)";
    static long _at = Stopwatch.GetTimestamp();
    static Thread _thread;

    /// <summary>The stalls seen since the last <see cref="Take"/>, oldest first.</summary>
    static readonly ConcurrentQueue<string> Seen = new();

    /// <summary>The main thread is past this marker now.</summary>
    public static void Stamp(string after)
    {
        _after = after;
        Volatile.Write(ref _at, Stopwatch.GetTimestamp());
    }

    public static void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Watch) { IsBackground = true, Name = "Stall watch", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>The stalls seen since last asked, as one note (empty: none).</summary>
    public static string Take()
    {
        var parts = new System.Collections.Generic.List<string>();
        while (Seen.TryDequeue(out string s)) parts.Add(s);
        return string.Join("; ", parts);
    }

    static void Watch()
    {
        long stalledAt = 0;
        string after = null;
        TimeSpan pause = default;
        int gen0 = 0, gen1 = 0, gen2 = 0;
        while (true)
        {
            Thread.Sleep(2);
            long at = Volatile.Read(ref _at);
            double since = (Stopwatch.GetTimestamp() - at) * 1000.0 / Stopwatch.Frequency;
            if (stalledAt == 0 && since > StallMs)
            {
                // The stall's start: what it followed, and the collector's count so far.
                stalledAt = at;
                after = _after;
                pause = GC.GetTotalPauseDuration();
                (gen0, gen1, gen2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
            }
            else if (stalledAt != 0 && at != stalledAt)
            {
                double ms = (at - stalledAt) * 1000.0 / Stopwatch.Frequency;
                Seen.Enqueue(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{ms:F0} ms after {after} (gc pause {(GC.GetTotalPauseDuration() - pause).TotalMilliseconds:F1} ms, collections {GC.CollectionCount(0) - gen0}/{GC.CollectionCount(1) - gen1}/{GC.CollectionCount(2) - gen2})"));
                stalledAt = 0;
            }
        }
    }
}
