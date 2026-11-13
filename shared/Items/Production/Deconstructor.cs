using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Items.Production;

/// <summary>One load in the deconstructor: items or wreck blocks, already taken out of storage.</summary>
public sealed class DeconJob
{
    public string Label { get; init; }
    /// <summary>What comes back when it finishes, as exact fractions (the return rate applied).</summary>
    public IReadOnlyDictionary<string, double> Returns { get; init; }
    public double Seconds { get; init; }
    public double Progress { get; internal set; }
}

public enum DeconStatus { Queued, UnknownItem, CantBreak, NotEnough }

public readonly record struct DeconResult(DeconStatus Status, DeconJob Job, string Message)
{
    public bool Ok => Status == DeconStatus.Queued;
}

/// <summary>
/// Breaks items, and blocks salvaged from wrecks, back into what they were made of, one step
/// down the tree, at the stated return rate (machines.json; 50%). Fractions carry over inside
/// the machine: breaking down one item that held one hull plate gives nothing yet, and the second
/// gives the plate, so over time exactly the return rate comes back and nothing is ever made from
/// nothing. Raw ore and unique finds can't be broken down.
///
/// Survival takes the item out of storage first; creative runs without checking (a test bench).
/// </summary>
public sealed class Deconstructor
{
    readonly Queue<DeconJob> _queue = new();
    readonly Dictionary<string, double> _carry = new(StringComparer.Ordinal);
    readonly Bill _tray = new();

    public MachineDef Def { get; }
    public ItemCatalogue Items { get; }
    public SharedStorage Storage { get; }
    public WorldRules Rules { get; }
    public int Version { get; private set; }

    public double ReturnRate => Items.Machines.DeconstructorReturn;
    public IReadOnlyDictionary<string, double> Carry => _carry;
    public IEnumerable<DeconJob> Queue => _queue;
    public DeconJob Current => _queue.Count > 0 ? _queue.Peek() : null;
    public Bill Tray => _tray.Clone();
    /// <summary>Everything ever given back, for the screen and the conservation tests.</summary>
    public Bill Returned { get; } = new();

    public Deconstructor(ItemCatalogue items, MachineDef def, SharedStorage storage, WorldRules rules)
    {
        if (def.Kind != MachineKind.Deconstructor) throw new ArgumentException($"{def.Id} is not a deconstructor");
        Items = items;
        Def = def;
        Storage = storage;
        Rules = rules;
    }

    /// <summary>Null if the item can be broken down, otherwise why not.</summary>
    public string WhyNot(ItemDef item) =>
        item.IsUnique ? $"{item.Name} is a unique find: it can't be broken down"
        : item.Recipe == null ? $"{item.Name} is raw: there is nothing to break it into"
        : null;

    /// <summary>What <paramref name="count"/> of an item break into, as exact fractions.</summary>
    public Dictionary<string, double> ReturnsOf(ItemDef item, int count)
    {
        var r = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (input, per) in item.Recipe.Inputs)
            r[input.Id] = r.GetValueOrDefault(input.Id) + (double)per * count * ReturnRate / item.Recipe.Count;
        return r;
    }

    /// <summary>What wreck blocks with this summed bill break into: their parts, at the return rate.</summary>
    public Dictionary<string, double> ReturnsOfBlocks(Bill blocks) =>
        blocks.ToDictionary(kv => kv.Key, kv => kv.Value * ReturnRate, StringComparer.Ordinal);

    /// <summary>The whole items those fractions would give right now, with what the machine already carries.</summary>
    public Bill Preview(IReadOnlyDictionary<string, double> returns)
    {
        var whole = new Bill();
        foreach (var (id, amount) in returns)
            whole.Add(id, (int)Math.Floor(_carry.GetValueOrDefault(id) + amount + 1e-9));
        return whole;
    }

    /// <summary>Loads items from storage into the machine (survival) and queues them.</summary>
    public DeconResult BreakItems(string itemId, int count = 1)
    {
        if (!Items.TryGet(itemId, out ItemDef item)) return new(DeconStatus.UnknownItem, null, $"unknown item '{itemId}'");
        string why = WhyNot(item);
        if (why != null) return new(DeconStatus.CantBreak, null, why);
        if (count <= 0) return new(DeconStatus.NotEnough, null, "nothing to break down");
        if (Rules.Survival)
        {
            if (Storage.Count(itemId) < count) return new(DeconStatus.NotEnough, null, $"only {Storage.Count(itemId)} {item.Name} in storage");
            Storage.Remove(itemId, count);
        }
        else Storage.Remove(itemId, Math.Min(count, Storage.Count(itemId))); // creative: tidy storage, never refuse

        var job = new DeconJob
        {
            Label = count == 1 ? item.Name : $"{count} x {item.Name}",
            Returns = ReturnsOf(item, count),
            Seconds = item.Recipe.Seconds * Items.Machines.DeconstructorSecondsFactor * count / Def.Speed,
        };
        _queue.Enqueue(job);
        Version++;
        return new(DeconStatus.Queued, job, null);
    }

    /// <summary>
    /// Queues blocks salvaged from a wreck, already taken off it by the caller: their summed bill
    /// comes back at the return rate (whole components, not ore), taking as long as fitting them took
    /// (<paramref name="fitSeconds"/>, the blocks' assembly seconds) at its speed.
    /// </summary>
    public DeconJob BreakBlocks(Bill blocksBill, double fitSeconds, string label)
    {
        var job = new DeconJob
        {
            Label = label,
            Returns = ReturnsOfBlocks(blocksBill),
            Seconds = fitSeconds / Def.Speed,
        };
        _queue.Enqueue(job);
        Version++;
        return job;
    }

    public void Tick(double dt)
    {
        FlushTray();
        double time = dt;
        while (time > 0 && _queue.Count > 0)
        {
            DeconJob job = _queue.Peek();
            double left = (1 - job.Progress) * job.Seconds;
            if (time < left)
            {
                job.Progress += time / Math.Max(job.Seconds, 1e-9);
                Version++;
                return;
            }
            time -= left;
            _queue.Dequeue();
            Pay(job.Returns);
            Version++;
        }
    }

    void Pay(IReadOnlyDictionary<string, double> returns)
    {
        var whole = new Bill();
        foreach (var (id, amount) in returns)
        {
            double c = _carry.GetValueOrDefault(id) + amount;
            int n = (int)Math.Floor(c + 1e-9);
            c -= n;
            if (c < 1e-9) _carry.Remove(id);
            else _carry[id] = c;
            whole.Add(id, n);
        }
        Returned.Add(whole);
        _tray.Add(Storage.AddAll(whole));
    }

    void FlushTray()
    {
        if (_tray.IsEmpty) return;
        Bill left = Storage.AddAll(_tray);
        foreach (string id in _tray.Ids.ToList()) _tray.Add(id, -_tray[id]);
        _tray.Add(left);
        Version++;
    }

    public string Status => Current != null ? $"BREAKING DOWN {Current.Label.ToUpperInvariant()}"
        : !_tray.IsEmpty ? "STORAGE FULL: HOLDING PARTS" : "IDLE";
}
