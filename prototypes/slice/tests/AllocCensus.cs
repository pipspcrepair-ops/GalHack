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

    /// <summary>Only the main thread's allocations counted.</summary>
    public bool MainOnly;

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
        if (MainOnly && e.OSThreadId != Main) return;
        int i = e.PayloadNames?.IndexOf("TypeName") ?? -1, k = e.PayloadNames?.IndexOf("AllocationKind") ?? -1;
        // The large-object heap's (arrays of 85 KB and more) marked: they never fill the young generation.
        bool large = k >= 0 && System.Convert.ToInt32(e.Payload?[k]) == 1;
        string type = (e.OSThreadId == Main ? "main " : "") + (large ? "LOH " : "") + (i >= 0 ? e.Payload?[i] as string ?? "?" : "?");
        lock (_types)
        {
            _types[type] = _types.GetValueOrDefault(type) + 1;
            _samples++;
        }
    }

    /// <summary>A compiler-made type (a closure, an iterator) named only by its own name: the types that declare one so named.</summary>
    static string Owners(string key)
    {
        string name = key[(key.LastIndexOf(' ') + 1)..];
        if (!name.StartsWith('<')) return "";
        var owners = System.AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return System.Type.EmptyTypes; } })
            .Where(t => t.Name == name && t.DeclaringType != null).Select(t => $"{t.DeclaringType.FullName}.{string.Join("/", Makers(t))}").Distinct().Take(8);
        return $" (in {string.Join(" | ", owners)})";
    }

    /// <summary>The methods of a compiler-made type's owner that make one (a newobj of its constructor in their code).</summary>
    static IEnumerable<string> Makers(System.Type made)
    {
        const System.Reflection.BindingFlags All = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
        var ctors = made.GetConstructors(All).Select(c => c.MetadataToken).ToHashSet();
        foreach (var m in made.DeclaringType.GetMethods(All).Cast<System.Reflection.MethodBase>().Concat(made.DeclaringType.GetConstructors(All)))
        {
            byte[] il;
            try { il = m.GetMethodBody()?.GetILAsByteArray(); } catch { continue; }
            if (il == null) continue;
            for (int i = 0; i + 4 < il.Length; i++)
                if (il[i] == 0x73 && ctors.Contains(System.BitConverter.ToInt32(il, i + 1))) { yield return m.Name; break; }
        }
    }

    /// <summary>The types most allocated since the last report, with their share, and starts over.</summary>
    public string Take(int most = 12)
    {
        lock (_types)
        {
            string s = _samples == 0 ? "(none)" : string.Join(", ", _types.OrderByDescending(kv => kv.Value).Take(most).Select(kv => $"{kv.Key}{Owners(kv.Key)} {kv.Value * 100.0 / _samples:F0}%"));
            _types.Clear();
            _samples = 0;
            return s;
        }
    }
}
