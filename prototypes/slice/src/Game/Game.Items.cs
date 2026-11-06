using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Production;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Items;
using Slice.Player;
using Slice.Vessels;
using SurvivalVitals.Model;
using Progress = Slice.Items.Progress;
using Wildlife;
using N = System.Numerics;

namespace Slice.Game;

/// <summary>What the inventory view has open beside your bag, and where it is.</summary>
public sealed class OpenContainer
{
    public string Title;
    public ItemGrid Grid;
    public Func<Vector3> Where;
    public Lootable Loot;
    public Vessel Vessel;
    public Device Device;
}

public partial class Game
{
    /// <summary>The container open beside your bag in the inventory view (E on it), or null.</summary>
    public OpenContainer Opened { get; private set; }

    /// <summary>How far E reaches.</summary>
    public const float UseReach = 3.5f;

    // ---- E: containers, bodies, lockers, devices ------------------------------------------------

    /// <summary>
    /// E: feed at a replicator within reach; otherwise what is under the crosshair, in the world
    /// you are in: a container block (a locker, a cargo box: the structure's shared storage), a
    /// body or a backpack on the ground, or a device. Nothing else answers E.
    /// </summary>
    void Use()
    {
        if (Spaceflight.Use()) return; // in space: a wreck's doors
        Replicator rep = Suit.Sim.NearestReplicator(FeetPos(), 2.5);
        if (rep != null)
        {
            Report(Suit.Sim.Feed(rep));
            return;
        }
        Vector3 eye = Survivor.WorldEye(), dir = Survivor.WorldLook();
        if (UseKeycard(eye, dir)) return;
        bool hit = WorldRay.Cast(Zone, Survivor, UseReach, Layers.World | Layers.Hull | Layers.Props, out LookHit look);
        float reach = hit ? look.Distance : UseReach;
        // A living creature in the way stops the reach (its body is solid to E as well).
        if (Zone.Wildlife.Raycast(eye, dir, reach, out float creatureAt, out _) is Creature { Dead: false }) reach = creatureAt;
        Lootable loot = Zone.Loot.Pick(eye, dir, reach + 0.3f, out _);
        if (loot != null)
        {
            if (loot == Bunker?.MineLocker.Loot)
            {
                Bunker.MineLocker.Swing();
                if (Suit.Sim.Pods[0].Rations > 0) Report(Suit.Sim.LootPod(Suit.Sim.Pods[0]));
            }
            Open(new OpenContainer { Title = loot.Name, Grid = loot.Grid, Loot = loot, Where = () => loot.Position });
            return;
        }
        if (Bunker?.SealedAlong(eye, dir, reach + 0.3f) is Slice.Start.BunkerLocker sealedOne)
        {
            Say($"Locker {sealedOne.Number} is sealed: its sleeper never woke", Tones.Caution);
            return;
        }
        if (!hit)
        {
            Say("Nothing to use here", Tones.Caution);
            return;
        }
        if (Zone.Devices.FirstOrDefault(d => d.Collider == look.Collider) is Device device)
        {
            Open(new OpenContainer { Title = $"{device.Label} hopper", Grid = device.Grid, Device = device, Where = () => device.GlobalPosition });
            return;
        }
        if (look.Vessel is Vessel v && v.Structure.TryGet(look.Cell, out Block b))
        {
            BlockType type = v.Structure.TypeOf(b);
            if (type.IsContainer)
            {
                Open(new OpenContainer { Title = $"{v.Name} storage", Grid = v.StorageGrid, Vessel = v, Where = () => v.ToWorld(StructureBridge.CellCenter(v.Structure, look.Cell)) });
                return;
            }
            if (type.IsCommandChair)
            {
                Say("F to take the command chair", Tones.Text);
                return;
            }
            if (Zone.Data.Items.Machines.ForBlock(type.Id) != null)
            {
                Menu.OpenCore(v);
                return;
            }
        }
        if (UsePlace(look)) return;
        Say("Nothing to use here", Tones.Caution);
    }

    /// <summary>What E (or F) would do with what is under the crosshair, for the prompt beside it; empty if nothing usable is in reach.</summary>
    public string UsePrompt()
    {
        if (Survivor == null || Suit == null || Suit.Dead || Survivor.Seated != null || Menu.IsOpen) return "";
        if (Spaceflight.UsePrompt() is string door) return door;
        if (Bunker?.Mine is Slice.Start.CryoPod pod && !pod.Open && Survivor.WorldFeet.DistanceTo(Bunker.CryoStand) < 1.5f) return "F  Open the pod";
        Vector3 eye = Survivor.WorldEye(), dir = Survivor.WorldLook();
        bool hit = WorldRay.Cast(Zone, Survivor, UseReach, Layers.World | Layers.Hull | Layers.Props, out LookHit look);
        if (Zone.Loot.Pick(eye, dir, (hit ? look.Distance : UseReach) + 0.3f, out _) is Lootable loot) return $"E  Open {loot.Name.ToLowerInvariant()}";
        if (Bunker?.SealedAlong(eye, dir, (hit ? look.Distance : UseReach) + 0.3f) is Slice.Start.BunkerLocker sealedOne) return $"Locker {sealedOne.Number}: sealed";
        if (PlacePrompt(eye, dir, hit, look) is { Length: > 0 } place) return place;
        if (!hit) return "";
        if (Zone.Devices.FirstOrDefault(d => d.Collider == look.Collider) is Device device) return $"E  Open the {device.Label.ToLowerInvariant()}";
        if (look.Vessel is Vessel v && v.Structure.TryGet(look.Cell, out Block b))
        {
            BlockType type = v.Structure.TypeOf(b);
            if (type.IsContainer) return v == Bunker?.Ruin ? "E  Open locker" : "E  Open storage";
            if (type.IsCommandChair) return "F  Take the command chair";
            if (Zone.Data.Items.Machines.ForBlock(type.Id) != null) return "E  Machine";
        }
        return "";
    }

    /// <summary>Opens a container beside your bag in the inventory view.</summary>
    public void Open(OpenContainer c)
    {
        Opened = c;
        Zone.Loot.Open = c.Loot;
        c.Grid.Reconcile();
        Menu.OpenInventory();
    }

    /// <summary>The container view closes (the menu closed, or you walked away from it).</summary>
    public void CloseContainer()
    {
        Opened = null;
        Zone.Loot.Open = null;
    }

    /// <summary>Uses one of a consumable from a toolbar or bag slot: eats, heals or refills air.</summary>
    public bool Consume(int slot)
    {
        Slot s = Pack.Grid[slot];
        if (s.Item == null || !Zone.Data.Consumables.TryGetValue(s.Item, out ConsumableEffect e)) return false;
        Vitals v = Suit.Vitals;
        VitalsRules r = Suit.Sim.Data.Vitals;
        if (e.Food > 0 && v.Food >= r.Food.Max - 0.5 && e.Health <= 0 && e.Oxygen <= 0)
        {
            Say("Not hungry", Tones.Text);
            return false;
        }
        string name = Zone.Data.Items.Item(s.Item).Name;
        if (!Pack.Grid.TakeOne(slot)) return false;
        if (e.Food > 0) v.Food = Math.Min(r.Food.Max, v.Food + e.Food * r.Food.Max);
        if (e.Health > 0) v.Heal(e.Health);
        if (e.Oxygen > 0) v.Oxygen = Math.Min(Suit.Sim.OxygenCapacity, v.Oxygen + e.Oxygen * Suit.Sim.OxygenCapacity);
        Say($"Used a {name}", Tones.Good);
        Stats.Used++;
        return true;
    }

    /// <summary>Sets things down where they can be picked up again (a full bag, a full storage, a refund with nowhere to go). Never lost.</summary>
    public void SetDown(Bill items, Vector3 at)
    {
        if (items == null || items.IsEmpty) return;
        Vector3 ground = Zone.GroundAt(at.X, at.Z);
        Zone.Loot.Spill(items, new Vector3(at.X, MathF.Max(at.Y - 0.2f, ground.Y), at.Z));
        Say($"Set down: {Zone.Data.Items.Describe(items, 3)} (no room)", Tones.Caution);
    }

    /// <summary>Drops a stack from the bag onto the ground in front of you (dragged out of the inventory window).</summary>
    public void Drop(ItemGrid from, int slot)
    {
        Slot s = from[slot];
        if (s.Item == null) return;
        var bill = new Bill();
        bill.Add(s.Item, s.Count);
        from.Storage.Remove(s.Item, s.Count);
        s.Clear();
        from.Reconcile();
        Vector3 look = Survivor.WorldLook();
        Vector3 ahead = Survivor.WorldFeet + new Vector3(look.X, 0f, look.Z).Normalized() * 1.2f;
        Vector3 ground = Zone.GroundAt(ahead.X, ahead.Z);
        Zone.Loot.Spill(bill, float.IsFinite(ground.Y) ? ground : ahead + Vector3.Up * 0.6f); // in space it floats where it was let go
        Say($"Dropped {Zone.Data.Items.Describe(bill)}", Tones.Text);
    }

    // ---- Death and the backpack -----------------------------------------------------------------

    bool _wasDead;

    /// <summary>The backpack your last death left, if it still holds anything.</summary>
    public Lootable Backpack { get; private set; }

    /// <summary>On death everything you carry goes into a backpack where you fell, marked on the compass; nothing is lost.</summary>
    void CheckDeath()
    {
        if (Suit.Dead && !_wasDead)
        {
            _wasDead = true;
            if (Survivor.Seated != null) Survivor.StandUp();
            Bill all = Pack.Storage.Contents;
            Pack.Storage.TryTake(all);
            foreach (Slot s in Pack.Grid.Slots) if (s.Item != null) s.Clear();
            Pack.Grid.Reconcile();
            int[] size = Zone.Data.Rules.Containers.Backpack;
            Vector3 feet = Survivor.WorldFeet;
            if (!all.IsEmpty) Backpack = Zone.Loot.Create(LootKind.Backpack, "Your backpack", feet, size[0], size[1], all);
            Stats.Deaths++;
            CloseContainer();
            Menu.Close();
        }
        if (Backpack != null && !Zone.Loot.All.Contains(Backpack)) Backpack = null;
    }

    void Respawn()
    {
        Outcome o = Suit.Sim.Respawn(RespawnChoice.NearestTent);
        if (!o.Ok) return;
        WakeAgain();
        Survivor.Body.Velocity = N.Vector3.Zero;
        _wasDead = false;
        Report(o);
        if (Backpack != null) Say("Your backpack is marked on the compass: E on it to take everything back", Tones.Caution);
    }

    // ---- Creatures' bodies ---------------------------------------------------------------------

    /// <summary>
    /// A creature died: its body becomes a container holding its drops (wildlife's drop table:
    /// meat by size, hide, and a rare part by chance). The player's kills are worth experience.
    /// </summary>
    void OnCreatureDied(Creature c, ITarget killer)
    {
        Species s = c.Species;
        var drops = new Bill();
        if (s.Drops.Meat > 0) drops.Add("meat", s.Drops.Meat);
        drops.Add("hide", Math.Max(1, s.Drops.Meat / 3));
        var rng = Wildlife.Rng.For(s.Seed, "drop", c.Id);
        if (s.Drops.RarePart != "" && rng.Chance(s.Drops.RareChance)) drops.Add("rare-tissue", 1);
        int[] size = Zone.Data.Rules.Containers.Corpse;
        Vector3 at = Zone.Wildlife.WorldOf(c);
        Lootable body = Zone.Loot.Create(LootKind.Corpse, $"{Cap(s.CommonName)} body", at, size[0], size[1], drops);
        body.Creature = c;
        body.Radius = MathF.Max(0.5f, s.Stats.Radius);
        if (killer == Target)
        {
            Stats.Kills++;
            Progress.Earn(s.Stats.Xp, $"{s.CommonName} brought down");
            Say($"{Cap(s.CommonName)} down: E on its body to take its meat and hide", Tones.Good);
        }
    }

    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ---- Machines -----------------------------------------------------------------------------

    readonly Dictionary<Constructor, List<(Job job, int done)>> _watch = new();

    /// <summary>
    /// Every machine runs while you play: the suit fabricator in your bag, each device, and each
    /// powered structure's constructors (at the tests' marked time-scale, 1 in play). Finished
    /// runs earn crafting experience; products with no room wait in the machine (shared/Items).
    /// </summary>
    void TickMachines(float dt)
    {
        double t = dt * CraftTimeScale;
        Craft(Fabricator, t);
        foreach (Device d in Zone.Devices)
        {
            Snapshot(d.Constructor);
            d.Tick(t);
            Finished(d.Constructor);
        }
        foreach (Vessel v in Zone.Vessels)
        {
            // A vessel on its build stand holds there until its pilot first takes its seat.
            if (v.Stand != null && Survivor.Seated == v) v.Stand = null;
            v.HoldOnStand();
            if (v.Machines.Count == 0) continue;
            foreach (Constructor c in v.Machines.Values) Snapshot(c);
            v.TickMachines(t);
            foreach (Constructor c in v.Machines.Values) Finished(c);
        }
        // Storage the machines filled past what slots show is set down, never lost (only the bag has a fixed grid).
        Pack.Grid.Reconcile();
        if (!Pack.Grid.Overflow.IsEmpty)
        {
            Bill over = Pack.Grid.Overflow;
            foreach (var (id, n) in over) Pack.Storage.Remove(id, n);
            Pack.Grid.Reconcile();
            SetDown(over, Survivor.WorldFeet);
        }
        foreach (Device d in Zone.Devices) d.Grid.Reconcile();
        AutoTransmit(dt);
        Zone.Loot.Tick();
        // A structure's storage stays open while you are in its area; anything else within a few steps.
        if (Opened != null && (Opened.Vessel != null
                ? !Slice.Construction.StructureArea.Contains(Opened.Vessel, Survivor.WorldFeet, Zone.Data.Rules.Build.Area)
                : Opened.Where().DistanceTo(Survivor.WorldFeet) > 6f)) CloseContainer();
    }

    float _transmitTimer;

    /// <summary>
    /// Auto-transmit (capital-base's rule, at its numbers): a vessel with it on sends what its
    /// storage holds to Home's storage every half second while Home is within 1 km, whatever
    /// fits, never deleting anything.
    /// </summary>
    void AutoTransmit(float dt)
    {
        if ((_transmitTimer -= dt) > 0f) return;
        _transmitTimer = 0.5f;
        Vessel home = Zone.Vessels.FirstOrDefault(v => v.Home && !v.IsWreck);
        if (home == null) return;
        foreach (Vessel v in Zone.Vessels)
        {
            if (!v.AutoTransmit || v == home || v.Storage.IsEmpty) continue;
            if (v.Body.WorldPosition.ToGodot().DistanceTo(home.Body.WorldPosition.ToGodot()) > 1000f) continue;
            foreach (var (item, n) in v.Storage.Stacks.ToList())
            {
                int moved = home.Storage.Add(item.Id, n);
                if (moved > 0) v.Storage.Remove(item.Id, moved);
            }
        }
    }

    void Craft(Constructor c, double dt)
    {
        Snapshot(c);
        c.Tick(dt);
        Finished(c);
    }

    void Snapshot(Constructor c)
    {
        c.Lock ??= item => Progress.ItemAllowed(item) ? null : Progress.LockReason(item.Id)?.ToLowerInvariant();
        if (!c.Busy) { _watch.Remove(c); return; }
        _watch[c] = c.AllJobs.Select(j => (j, j.RunsDone)).ToList();
    }

    /// <summary>Runs finished since the snapshot: a job still queued counts its new runs, one gone finished all it had left.</summary>
    void Finished(Constructor c)
    {
        if (!_watch.TryGetValue(c, out var before)) return;
        var now = c.AllJobs.ToHashSet();
        foreach (var (job, done) in before)
        {
            int runs = now.Contains(job) ? job.RunsDone - done : job.Runs - done;
            if (runs <= 0) continue;
            Stats.Crafted += runs * job.Recipe.Count;
            int xp = runs * Math.Max(1, (int)Math.Round(job.Recipe.Seconds / Progress.Rules.Xp.CraftSecondsPerXp));
            Progress.Earn(xp, $"made {job.Item.Name}");
            if (!now.Contains(job) && job.Lane == Lane.Order) Say($"{c.Def.Name}: {job.Count} {job.Item.Name} done", Tones.Good);
        }
    }
}
