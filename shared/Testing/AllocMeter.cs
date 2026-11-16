using System;
using System.Collections.Concurrent;
using System.Linq;

namespace GalHack.Testing;

/// <summary>
/// What each kind of worker job allocates, bytes, while a check is measuring (<see cref="On"/>): a job wraps its work in
/// <see cref="Measure"/> with its name. Off, it costs a flag test.
/// </summary>
public static class AllocMeter
{
    public static bool On;
    static readonly ConcurrentDictionary<string, long> Bytes = new();

    /// <summary>The bytes this thread allocates in the work, under its name.</summary>
    public static T Measure<T>(string name, Func<T> work)
    {
        if (!On) return work();
        long before = GC.GetAllocatedBytesForCurrentThread();
        try { return work(); }
        finally
        {
            long d = GC.GetAllocatedBytesForCurrentThread() - before;
            Bytes.AddOrUpdate(name, d, (_, b) => b + d);
        }
    }

    public static void Measure(string name, Action work) => Measure(name, () => { work(); return 0; });

    /// <summary>MB a second by name since the last take, the most first, and starts over.</summary>
    public static string Take(double seconds)
    {
        string s = string.Join(", ", Bytes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / 1048576.0 / seconds:F1}"));
        Bytes.Clear();
        return s.Length == 0 ? "(none)" : s;
    }
}
