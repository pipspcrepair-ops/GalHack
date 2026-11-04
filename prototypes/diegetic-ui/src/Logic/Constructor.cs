using System;
using System.Collections.Generic;
using System.Linq;

namespace DiegeticUi.Logic;

public sealed record Recipe(ItemDef Output, int OutputCount, (ItemDef Item, int Count)[] Inputs, double Seconds);

public static class Recipes
{
    static readonly Dictionary<string, Recipe> ByOutput = new();

    public static readonly Recipe IronPlate = Add(new(Catalog.IronPlate, 1, new[] { (Catalog.IronOre, 2) }, 1.5));
    public static readonly Recipe CopperWire = Add(new(Catalog.CopperWire, 2, new[] { (Catalog.CopperOre, 1) }, 1.0));
    public static readonly Recipe Motor = Add(new(Catalog.Motor, 1, new[] { (Catalog.IronPlate, 1), (Catalog.CopperWire, 3) }, 3.0));
    public static readonly Recipe RoverWheel = Add(new(Catalog.RoverWheel, 1,
        new[] { (Catalog.IronPlate, 2), (Catalog.Motor, 1), (Catalog.Polymer, 1) }, 4.0));
    public static readonly Recipe HullPlate = Add(new(Catalog.HullPlate, 1, new[] { (Catalog.IronPlate, 4) }, 3.0));
    public static readonly Recipe O2Canister = Add(new(Catalog.O2Canister, 1, new[] { (Catalog.IronPlate, 1), (Catalog.Ice, 2) }, 2.0));
    public static readonly Recipe MedKit = Add(new(Catalog.MedKit, 1, new[] { (Catalog.Polymer, 1), (Catalog.BioGel, 1) }, 2.0));

    public static Recipe For(ItemDef item) => ByOutput.GetValueOrDefault(item.Id);

    static Recipe Add(Recipe recipe)
    {
        ByOutput[recipe.Output.Id] = recipe;
        return recipe;
    }
}

/// <summary>One line in a constructor queue.</summary>
public sealed class Job
{
    public ItemDef Item { get; init; }
    public int Count { get; internal set; }
    public int Done { get; internal set; }
    /// <summary>Added by the constructor itself, to make a part an order needs.</summary>
    public bool Auto { get; init; }
    /// <summary>The player's order this sub-part feeds (null for orders).</summary>
    public Job Feeds { get; init; }
    /// <summary>0..1 through the current run.</summary>
    public double Progress { get; internal set; }
    public bool Running { get; internal set; }
    public Recipe Recipe => Recipes.For(Item);
}

/// <summary>
/// A constructor (or replicator) with two queues: the player's orders above, and the sub-parts
/// it adds by itself below. Ordering something you lack the parts for plans the whole chain:
/// every missing intermediate part becomes an auto job, built first, and raw materials that no
/// recipe makes are reported as missing instead.
///
/// Planning keeps a ledger of what queued jobs will produce and consume, so two orders that
/// need the same part never both count on the same unit in storage.
/// </summary>
public sealed class ConstructorQueue
{
    readonly List<Job> _orders = new(), _subParts = new();
    readonly Dictionary<string, int> _ledger = new();     // future change in stock from queued jobs
    readonly Dictionary<string, int> _missing = new();

    public Inventory Storage { get; }
    public double Speed { get; set; } = 1;
    public int Version { get; private set; }

    public IReadOnlyList<Job> Orders => _orders;
    public IReadOnlyList<Job> SubParts => _subParts;
    /// <summary>Raw materials the queued jobs still need and nobody can make.</summary>
    public IReadOnlyDictionary<string, int> Missing => _missing;
    public bool Busy => _orders.Count + _subParts.Count > 0;

    public ConstructorQueue(Inventory storage) => Storage = storage;

    /// <summary>Queues an order and every sub-part it lacks. False if nothing makes that item.</summary>
    public bool Order(ItemDef item, int count = 1)
    {
        if (Recipes.For(item) == null || count <= 0) return false;
        var order = new Job { Item = item, Count = count };
        Plan(order, order);
        _orders.Add(order);
        Version++;
        return true;
    }

    /// <summary>
    /// Reserves a job's inputs, planning auto jobs for any shortfall first (depth first, so
    /// sub-parts land in the queue in the order they must be built).
    /// </summary>
    void Plan(Job job, Job order)
    {
        Recipe recipe = job.Recipe;
        int runs = Runs(recipe, job.Count);
        foreach (var (input, perRun) in recipe.Inputs)
        {
            int need = runs * perRun;
            int shortfall = need - Projected(input);
            if (shortfall > 0)
            {
                if (Recipes.For(input) != null) QueueSubPart(input, shortfall, order);
                else _missing[input.Id] = _missing.GetValueOrDefault(input.Id) + shortfall;
            }
            _ledger[input.Id] = _ledger.GetValueOrDefault(input.Id) - need;
        }
        _ledger[job.Item.Id] = _ledger.GetValueOrDefault(job.Item.Id) + runs * recipe.OutputCount;
    }

    void QueueSubPart(ItemDef item, int count, Job order)
    {
        var job = new Job { Item = item, Count = count, Auto = true, Feeds = order };
        Plan(job, order);
        // One line per part per order reads better than three lines of "IRON PLATE x1".
        Job same = _subParts.FirstOrDefault(j => j.Item == item && j.Feeds == order && !j.Running && j.Done == 0);
        if (same != null)
        {
            same.Count += count;
            // Moving it to the end keeps "parts before the things made from them".
            _subParts.Remove(same);
            _subParts.Add(same);
        }
        else _subParts.Add(job);
    }

    int Projected(ItemDef item) => Storage.Count(item.Id) + _ledger.GetValueOrDefault(item.Id);

    static int Runs(Recipe recipe, int count) => (count + recipe.OutputCount - 1) / recipe.OutputCount;

    /// <summary>Runs the fabricator: one job at a time, sub-parts first.</summary>
    public void Tick(double dt)
    {
        Job job = _subParts.FirstOrDefault(j => j.Running) ?? _orders.FirstOrDefault(j => j.Running)
            ?? _subParts.FirstOrDefault(CanStart) ?? _orders.FirstOrDefault(CanStart);
        if (job == null) return;

        Recipe recipe = job.Recipe;
        if (!job.Running)
        {
            foreach (var (input, perRun) in recipe.Inputs)
            {
                Storage.Remove(input.Id, perRun);
                _ledger[input.Id] += perRun; // the reservation is now real consumption
            }
            job.Running = true;
            job.Progress = 0;
        }

        job.Progress += dt * Speed / recipe.Seconds;
        if (job.Progress < 1)
        {
            Version++;
            return;
        }

        int made = Storage.Add(recipe.Output, recipe.OutputCount);
        _ledger[recipe.Output.Id] -= made; // the promised output is now in storage
        job.Done += recipe.OutputCount;
        job.Running = false;
        job.Progress = 0;
        if (job.Done >= job.Count)
        {
            _subParts.Remove(job);
            _orders.Remove(job);
        }
        Version++;
    }

    public bool CanStart(Job job) =>
        job.Recipe.Inputs.All(i => Storage.Count(i.Item.Id) >= i.Count)
        && Storage.RoomFor(job.Item) >= job.Recipe.OutputCount;

    /// <summary>
    /// Updates the missing list after storage changed (the player dropped ore in). The ledger
    /// holds every queued reservation, so storage plus ledger below zero is exactly the shortfall.
    /// </summary>
    public void RecheckMissing()
    {
        foreach (string id in _missing.Keys.ToList())
        {
            int shortfall = -(Storage.Count(id) + _ledger.GetValueOrDefault(id));
            if (shortfall <= 0) _missing.Remove(id);
            else _missing[id] = shortfall;
        }
        Version++;
    }
}
