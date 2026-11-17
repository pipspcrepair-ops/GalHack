using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;

namespace PlanetSurface;

/// <summary>TEMP profiling: named main-thread spans by process frame.</summary>
public static class Prof
{
    static readonly Dictionary<string, double>[] Ring = Enumerable.Range(0, 8).Select(_ => new Dictionary<string, double>()).ToArray();
    static readonly ulong[] FrameOf = new ulong[8];
    public static bool On;

    public static long Now() => Stopwatch.GetTimestamp();

    public static void Add(string what, long since)
    {
        if (!On) return;
        double ms = Stopwatch.GetElapsedTime(since).TotalMilliseconds;
        ulong f = Engine.GetProcessFrames();
        int i = (int)(f % 8);
        if (FrameOf[i] != f) { Ring[i].Clear(); FrameOf[i] = f; }
        Ring[i][what] = Ring[i].GetValueOrDefault(what) + ms;
    }

    public static string Of(ulong f)
    {
        int i = (int)(f % 8);
        return FrameOf[i] != f ? "-" : string.Join(", ", Ring[i].Where(kv => kv.Value >= 0.2).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:F1}"));
    }
}
