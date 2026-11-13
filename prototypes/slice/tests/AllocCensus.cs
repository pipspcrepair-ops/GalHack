using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

namespace Slice.Tests;

/// <summary>
/// What is being allocated, by type, on every thread: the runtime's own allocation sample (an event each
/// time about 100 KB more has been allocated, naming the type that crossed it), counted while on. A type's
/// share of the samples is its share of the bytes, near enough.
/// </summary>
public sealed class AllocCensus : EventListener
{
    readonly Dictionary<string, int> _types = new();
    int _samples;
    public bool On;

    /// <summary>The OS id of the thread that made this (the main thread), to tell its allocations from the workers'.</summary>
    public readonly long Main = GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    protected override void OnEventSourceCreated(EventSource source)
    {
        // GC events (keyword 0x1) at verbose level carry the allocation sample.
        if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (!On || e.EventName == null || !e.EventName.StartsWith("GCAllocationTick")) return;
        int i = e.PayloadNames?.IndexOf("TypeName") ?? -1;
        string type = (e.OSThreadId == Main ? "main " : "") + (i >= 0 ? e.Payload?[i] as string ?? "?" : "?");
        lock (_types)
        {
            _types[type] = _types.GetValueOrDefault(type) + 1;
            _samples++;
        }
    }

    /// <summary>The types most allocated since the last report, with their share, and starts over.</summary>
    public string Take(int most = 12)
    {
        lock (_types)
        {
            string s = _samples == 0 ? "(none)" : string.Join(", ", _types.OrderByDescending(kv => kv.Value).Take(most).Select(kv => $"{kv.Key} {kv.Value * 100.0 / _samples:F0}%"));
            _types.Clear();
            _samples = 0;
            return s;
        }
    }
}
