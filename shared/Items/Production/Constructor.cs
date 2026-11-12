using System;
using System.Collections.Generic;
using System.Linq;

namespace GalHack.Items.Production;

/// <summary>Which queue a job sits in. Lanes run in this order of priority: sub-parts, orders, restock.</summary>
public enum Lane
{
    /// <summary>The player's own orders.</summary>
    Order,
    /// <summary>Parts the constructor added by itself for an order.</summary>
    SubPart,
    /// <summary>Ammo the constructor queued by itself to keep the guns stocked, and its parts. Lowest priority.</summary>
    Restock,
}

/// <summary>One line in a constructor's queue: so many runs of one recipe.</summary>
public sealed class Job
{
    public ItemDef Item { get; }
    public Recipe Recipe => Item.Recipe;
    public Lane Lane { get; }
    /// <summary>Sub-parts: the order this part is for.</summary>
    public Job Feeds { get; }
    /// <summary>Restock jobs: the ammo they are for.</summary>
    public string Restocking { get; }
    public int Runs { get; internal set; }
    public int RunsDone { get; internal set; }
    public bool Running { get; internal set; }
    /// <summary>0..1 through the current run.</summary>
    public double Progress { get; internal set; }

    /// <summary>Units this line makes in all, and has made so far.</summary>
    public int Count => Runs * Recipe.Count;
    public int Done => RunsDone * Recipe.Count;
    public bool Auto => Lane != Lane.Order;
    public int RunsLeft => Runs - RunsDone;

    internal Job(ItemDef item, int runs, Lane lane, Job feeds = null, string restocking = null)
    {
        Item = item;
        Runs = runs;
        Lane = lane;
        Feeds = feeds;
        Restocking = restocking;
    }

    public override string ToString() => $"{Lane} {Item.Id} {Done}/{Count}";
}

public enum OrderStatus { Queued, UnknownItem, NoRecipe, NeedsHigherTier, BadCount }

public readonly record struct OrderResult(OrderStatus Status, Job Job, string Message)
{
    public bool Ok => Status == OrderStatus.Queued;
}

/// <summary>
/// What queued jobs will take from storage (reserved) and put into it (incoming). Projected
/// stock is storage plus incoming minus reserved; planning never counts the same unit twice.
/// </summary>
sealed class Ledger
{
    readonly Dictionary<string, int> _reserved = new(StringComparer.Ordinal), _incoming = new(StringComparer.Ordinal);

    public int Reserved(string id) => _reserved.GetValueOrDefault(id);
    public int Incoming(string id) => _incoming.GetValueOrDefault(id);
    public IEnumerable<string> ReservedIds => _reserved.Keys;

    public void Reserve(string id, int n) => Bump(_reserved, id, n);
    public void Expect(string id, int n) => Bump(_incoming, id, n);

    public Ledger Clone()
    {
        var l = new Ledger();
        foreach (var (k, v) in _reserved) l._reserved[k] = v;
        foreach (var (k, v) in _incoming) l._incoming[k] = v;
        return l;
    }

    public void Clear()
    {
        _reserved.Clear();
        _incoming.Clear();
    }

    static void Bump(Dictionary<string, int> d, string id, int n)
    {
        int v = d.GetValueOrDefault(id) + n;
        if (v == 0) d.Remove(id);
        else d[id] = v;
    }
}

/// <summary>
/// A constructor: the player's orders in one queue, the sub-parts it adds by itself in a second
/// (design section 10), and ammo it queues to keep the guns stocked in a third, lowest-priority
/// lane. Ordering something you lack the parts for plans the whole chain in one pass, one line per
/// part, inputs before what they make; raw materials nobody here can make are reported missing.
///
/// Everything comes from and goes back to the structure's shared storage. A product that has no
/// room waits in the machine's output tray; nothing is ever lost. In creative mode it still runs,
/// for testing, but checks and consumes no materials.
/// </summary>
public sealed class Constructor
{
    readonly List<Job> _orders = new(), _subParts = new(), _restock = new();
    readonly Ledger _ledger = new();     // player lanes only: restock never counts on it
    readonly Bill _tray = new();
    int _rulesSeen, _playerVersion;
    (int, int, int, int) _restockKey = (-1, -1, -1, -1);

    public MachineDef Def { get; }
    public ItemCatalogue Items { get; }
    public SharedStorage Storage { get; }
    public WorldRules Rules { get; }
    public AmmoRestock Restock { get; } = new();

    public int Version { get; private set; }

    public IReadOnlyList<Job> Orders => _orders;
    public IReadOnlyList<Job> SubParts => _subParts;
    public IReadOnlyList<Job> RestockJobs => _restock;
    public IEnumerable<Job> AllJobs => _subParts.Concat(_orders).Concat(_restock);
    /// <summary>Products waiting for room in storage.</summary>
    public Bill Tray => _tray.Clone();
    public Job Current => AllJobs.FirstOrDefault(j => j.Running);
    public bool Busy => _orders.Count + _subParts.Count + _restock.Count > 0;

    public Constructor(ItemCatalogue items, MachineDef def, SharedStorage storage, WorldRules rules)
    {
        if (def.Kind != MachineKind.Constructor) throw new ArgumentException($"{def.Id} is not a constructor");
        Items = items;
        Def = def;
        Storage = storage;
        Rules = rules;
        _rulesSeen = rules.Version;
    }

    /// <summary>True if this constructor knows how to make the item.</summary>
    public bool Knows(ItemDef item) => item.Recipe != null && item.Recipe.Tier <= Def.Tier;

    /// <summary>
    /// Why the tech tree keeps an item from being made here, or null if it doesn't (set by whoever
    /// owns progress). A locked sub-part is reported in <see cref="Blockers"/>, never queued.
    /// </summary>
    public Func<ItemDef, string> Lock;

    /// <summary>Can this constructor make the item now: it knows the recipe, and nothing locks it?</summary>
    public bool CanMake(ItemDef item) => Knows(item) && (Rules.Creative || Lock?.Invoke(item) == null);

    /// <summary>Seconds one run takes here.</summary>
    public double RunSeconds(Recipe r) => r.Seconds / Def.Speed;

    /// <summary>Have = in storage or waiting in the tray.</summary>
    int Have(string id) => Storage.Count(id) + _tray[id];

    // ---- Orders ---------------------------------------------------------------------------

    /// <summary>Queues an order for <paramref name="count"/> units and every sub-part it lacks.</summary>
    public OrderResult Order(string itemId, int count = 1)
    {
        if (count <= 0) return new(OrderStatus.BadCount, null, "nothing to make");
        if (!Items.TryGet(itemId, out ItemDef item)) return new(OrderStatus.UnknownItem, null, $"unknown item '{itemId}'");
        if (item.Recipe == null) return new(OrderStatus.NoRecipe, null, $"{item.Name} can't be made: it must be found");
        if (!Knows(item))
            return new(OrderStatus.NeedsHigherTier, null, $"{item.Name} needs a {Items.Machines.TierName(item.Recipe.Tier)} constructor");

        var order = new Job(item, item.Recipe.RunsFor(count), Lane.Order);
        PlanOrder(order, order.Runs);
        _orders.Add(order);
        _playerVersion++;
        Version++;
        return new(OrderStatus.Queued, order, null);
    }

    /// <summary>
    /// Cancels an order and its sub-parts. A run in progress is stopped and its inputs go back to
    /// storage; the other orders are replanned without it.
    /// </summary>
    public bool Cancel(Job order)
    {
        if (order == null || order.Lane != Lane.Order || !_orders.Contains(order)) return false;
        foreach (Job j in _subParts.Where(j => j.Feeds == order).Append(order).ToList())
        {
            if (j.Running && Rules.Survival) Refund(j.Recipe.InputsFor(1));
            _subParts.Remove(j);
            _orders.Remove(j);
        }
        Rebuild();
        return true;
    }

    /// <summary>Raw materials (or parts this tier can't make) the player's jobs still need and nobody here can make.</summary>
    public Bill Missing
    {
        get
        {
            var missing = new Bill();
            if (Rules.Creative) return missing;
            foreach (string id in _ledger.ReservedIds)
            {
                int deficit = _ledger.Reserved(id) - Have(id) - _ledger.Incoming(id);
                if (deficit > 0 && !CanMake(Items.Item(id))) missing.Add(id, deficit);
            }
            return missing;
        }
    }

    /// <summary>
    /// What is stopping the queue, one readable line per thing, saying exactly what is needed:
    /// "12 Iron ore: not in storage", "2 Servo: needs a small constructor", "1 Reactor: locked: level 9".
    /// </summary>
    public List<string> Blockers
    {
        get
        {
            var lines = new List<string>();
            foreach (var (id, n) in Missing)
            {
                ItemDef item = Items.Item(id);
                string why = item.Recipe == null ? "not in storage"
                    : !Knows(item) ? $"needs a {Items.Machines.TierName(item.Recipe.Tier)} constructor"
                    : Lock?.Invoke(item) ?? "not in storage";
                lines.Add($"{n} {item.Name}: {why}");
            }
            return lines;
        }
    }

    /// <summary>Items in <see cref="Missing"/> that a higher-tier constructor could make, with the tier.</summary>
    public IEnumerable<(ItemDef Item, int Tier)> NeedsHigherTier =>
        Missing.Ids.Select(Items.Item).Where(i => i.Recipe != null).Select(i => (i, i.Recipe.Tier));

    /// <summary>
    /// Plans an order's chain in one pass over the tree, consumers before what they consume, so
    /// every part's needs are summed before it is rounded to whole runs. Lines go in build order.
    /// </summary>
    void PlanOrder(Job order, int runs)
    {
        if (Rules.Creative || runs <= 0) return;
        _ledger.Expect(order.Item.Id, runs * order.Recipe.Count);
        List<Job> lines = PlanTree(order.Item, runs, _ledger, Have, Lane.SubPart, order, null, null);
        _subParts.AddRange(lines);
    }

    /// <summary>
    /// The planner both lanes share. Reserves the inputs of <paramref name="runs"/> runs of
    /// <paramref name="top"/> and returns sub-part jobs for every shortfall this tier can make, in
    /// build order. Shortfalls it can't make go into <paramref name="missing"/>.
    /// </summary>
    List<Job> PlanTree(ItemDef top, int runs, Ledger ledger, Func<string, int> have, Lane lane, Job feeds, string restocking, Bill missing)
    {
        var wanted = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (input, per) in top.Recipe.Inputs) wanted[input.Id] = wanted.GetValueOrDefault(input.Id) + (long)per * runs;

        var lines = new List<Job>();
        IReadOnlyList<ItemDef> order = Items.Graph.BuildOrder;
        for (int i = order.Count - 1; i >= 0; i--)
        {
            ItemDef item = order[i];
            if (!wanted.TryGetValue(item.Id, out long need) || need <= 0) continue;
            int n = checked((int)need);
            int projected = have(item.Id) + ledger.Incoming(item.Id) - ledger.Reserved(item.Id);
            int shortfall = n - Math.Max(0, projected);
            ledger.Reserve(item.Id, n);
            if (shortfall <= 0) continue;
            if (!CanMake(item))
            {
                missing?.Add(item.Id, shortfall);
                continue;
            }
            int subRuns = item.Recipe.RunsFor(shortfall);
            ledger.Expect(item.Id, subRuns * item.Recipe.Count);
            lines.Add(new Job(item, subRuns, lane, feeds, restocking));
            foreach (var (input, per) in item.Recipe.Inputs)
                wanted[input.Id] = wanted.GetValueOrDefault(input.Id) + (long)per * subRuns;
        }
        lines.Reverse();
        return lines;
    }

    /// <summary>
    /// Rebuilds the player's plan from what is left of each order. Used when the mode changes or
    /// storage lost something the plan counted on (a player took it out, or placed it as a block).
    /// Runs in progress finish as they are.
    /// </summary>
    void Rebuild()
    {
        _ledger.Clear();
        _subParts.RemoveAll(j => !j.Running);
        foreach (Job j in _subParts)
        {
            j.Runs = j.RunsDone + 1;
            if (Rules.Survival) _ledger.Expect(j.Item.Id, j.Recipe.Count);
        }
        foreach (Job order in _orders)
        {
            if (order.Running && Rules.Survival) _ledger.Expect(order.Item.Id, order.Recipe.Count);
            PlanOrder(order, order.RunsLeft - (order.Running ? 1 : 0));
        }
        _playerVersion++;
        Version++;
    }

    bool MakeableDeficit()
    {
        if (Rules.Creative) return false;
        foreach (string id in _ledger.ReservedIds)
            if (_ledger.Reserved(id) - Have(id) - _ledger.Incoming(id) > 0 && CanMake(Items.Item(id))) return true;
        return false;
    }

    // ---- Running ----------------------------------------------------------------------------

    /// <summary>Runs the machine: one job at a time, sub-parts first, then orders, then restock.</summary>
    public void Tick(double dt)
    {
        FlushTray();
        if (Rules.Version != _rulesSeen)
        {
            _rulesSeen = Rules.Version;
            Rebuild();
        }
        if (MakeableDeficit()) Rebuild();
        ReplanRestockIfChanged();

        double time = dt;
        for (int guard = 0; guard < 100000 && time > 0; guard++)
        {
            Job job = Current ?? Next();
            if (job == null) break;
            if (!job.Running) Start(job);
            double runSeconds = RunSeconds(job.Recipe);
            double left = (1 - job.Progress) * runSeconds;
            if (time < left)
            {
                job.Progress += time / runSeconds;
                Version++;
                break;
            }
            time -= left;
            Finish(job);
            ReplanRestockIfChanged();
        }
    }

    Job Next() =>
        _subParts.FirstOrDefault(CanStart) ?? _orders.FirstOrDefault(CanStart)
        ?? (Restock.Enabled ? _restock.FirstOrDefault(CanStart) : null);

    /// <summary>True if the job's next run could start now.</summary>
    public bool CanStart(Job job)
    {
        if (job.Running || job.RunsLeft <= 0) return false;
        Bill inputs = Rules.Survival ? job.Recipe.InputsFor(1) : new Bill();
        double outVolume = job.Item.VolumeSu * job.Recipe.Count;
        if (outVolume > Storage.FreeSu + Items.VolumeOf(inputs) + 1e-9) return false; // no room for what it makes
        if (Rules.Creative) return true;
        if (job.Lane != Lane.Restock) return Storage.Has(inputs);
        // Restock never touches what the player's jobs have reserved.
        return inputs.All(kv => Storage.Count(kv.Key) - _ledger.Reserved(kv.Key) >= kv.Value);
    }

    void Start(Job job)
    {
        if (Rules.Survival)
        {
            Bill inputs = job.Recipe.InputsFor(1);
            if (!Storage.TryTake(inputs)) throw new InvalidOperationException($"{Def.Id}: started {job.Item.Id} without its inputs");
            if (job.Lane != Lane.Restock)
                foreach (var (id, n) in inputs) _ledger.Reserve(id, -n);
        }
        job.Running = true;
        job.Progress = 0;
        Version++;
    }

    void Finish(Job job)
    {
        int made = job.Recipe.Count;
        int stored = Storage.Add(job.Item.Id, made);
        if (stored < made) _tray.Add(job.Item.Id, made - stored);
        if (job.Lane != Lane.Restock && Rules.Survival) _ledger.Expect(job.Item.Id, -made);
        job.RunsDone++;
        job.Running = false;
        job.Progress = 0;
        if (job.RunsLeft <= 0)
        {
            _subParts.Remove(job);
            _orders.Remove(job);
            _restock.Remove(job);
            if (job.Lane != Lane.Restock) _playerVersion++;
        }
        Version++;
    }

    void Refund(Bill bill)
    {
        Bill left = Storage.AddAll(bill);
        _tray.Add(left);
    }

    void FlushTray()
    {
        if (_tray.IsEmpty) return;
        Bill left = Storage.AddAll(_tray);
        if (left.Equals(_tray)) return;
        foreach (string id in _tray.Ids.ToList()) _tray.Add(id, -_tray[id]);
        _tray.Add(left);
        Version++;
    }

    /// <summary>Seconds of work left in every queue at this machine's speed.</summary>
    public double SecondsLeft => AllJobs.Sum(j => (j.RunsLeft - (j.Running ? j.Progress : 0)) * RunSeconds(j.Recipe));

    /// <summary>A one-line state for the screen's status strip.</summary>
    public string Status
    {
        get
        {
            Job current = Current;
            if (current != null) return current.Lane == Lane.Restock ? $"RESTOCKING {current.Item.Name.ToUpperInvariant()}" : $"BUILDING {current.Item.Name.ToUpperInvariant()}";
            if (!Missing.IsEmpty) return "WAITING ON RAW MATERIALS";
            if (_orders.Count + _subParts.Count > 0) return Storage.FreeSu < 1 ? "STORAGE FULL" : "WAITING FOR PARTS";
            return "IDLE";
        }
    }

    // ---- Ammo restock -----------------------------------------------------------------------

    void ReplanRestockIfChanged()
    {
        var key = (Storage.Version, _playerVersion, Restock.Version, Rules.Version);
        if (key == _restockKey) return;
        _restockKey = key;
        ReplanRestock();
    }

    /// <summary>
    /// Refills every ammo the structure's guns use up to its threshold, from materials nobody has
    /// reserved. It plans as many whole boxes as free materials allow; for the rest it pauses and
    /// says exactly what it needs. Jobs not yet started are replanned whenever anything changes,
    /// so the lane always matches the stock.
    /// </summary>
    void ReplanRestock()
    {
        _restock.RemoveAll(j => !j.Running);
        var ledger = new Ledger();
        foreach (Job j in _restock)
        {
            j.Runs = j.RunsDone + 1; // let the run in progress finish
            ledger.Expect(j.Item.Id, j.Recipe.Count);
        }

        var need = new Bill();
        var locked = new List<ItemDef>();
        int planned = 0;
        if (!Restock.Enabled)
        {
            Restock.SetStatus(RestockState.Off, need, locked, Items);
            Version++;
            return;
        }

        // Free stock: what's here that no player job has claimed.
        int Free(string id) => Have(id) - _ledger.Reserved(id);
        foreach (string ammo in Restock.InUse)
        {
            if (!Items.TryGet(ammo, out ItemDef item) || item.Recipe == null) continue;
            int threshold = Restock.Threshold(ammo, Items);
            if (threshold <= 0) continue;
            int stock = Have(ammo) + _ledger.Incoming(ammo) - _ledger.Reserved(ammo) + ledger.Incoming(ammo);
            int boxes = item.Recipe.RunsFor(threshold - stock);
            if (boxes <= 0) continue;
            if (!Knows(item))
            {
                locked.Add(item);
                continue;
            }
            if (Rules.Creative)
            {
                _restock.Add(new Job(item, boxes, Lane.Restock, restocking: ammo));
                planned += boxes;
                continue;
            }

            // The most boxes free materials cover (more boxes never need less).
            int lo = 0, hi = boxes;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                var miss = new Bill();
                PlanTree(item, mid, ledger.Clone(), Free, Lane.Restock, null, ammo, miss);
                if (miss.IsEmpty) lo = mid;
                else hi = mid - 1;
            }
            if (lo > 0)
            {
                ledger.Expect(ammo, lo * item.Recipe.Count);
                _restock.AddRange(PlanTree(item, lo, ledger, Free, Lane.Restock, null, ammo, null));
                _restock.Add(new Job(item, lo, Lane.Restock, restocking: ammo));
                planned += lo;
            }
            if (lo < boxes) PlanTree(item, boxes - lo, ledger.Clone(), Free, Lane.Restock, null, ammo, need);
        }

        RestockState state = !need.IsEmpty ? RestockState.Paused
            : locked.Count > 0 ? RestockState.NeedsTier
            : _restock.Count > 0 ? RestockState.Restocking
            : Restock.InUse.Count == 0 ? RestockState.NoWeapons
            : RestockState.Stocked;
        Restock.SetStatus(state, need, locked, Items);
        Version++;
    }
}
