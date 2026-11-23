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
    /// <summary>The light block the CORE tab is styling (Shift+E on it), or null for every light.</summary>
    public Int3? LightEdit;

    /// <summary>The container open beside your bag in the inventory view (E on it), or null.</summary>
    public OpenContainer Opened { get; private set; }

    /// <summary>How far E reaches.</summary>
    public const float UseReach = 3.5f;

    // ---- E: containers, bodies, lockers, devices ------------------------------------------------

    /// <summary>
    /// E: feed at a replicator within reach; otherwise what is under the crosshair, in the world
    /// you are in: a container block (a locker, a cargo box: the structure's shared storage), a
    /// body or a bag on the ground, or a device. Nothing else answers E.
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
        if (PickUpBike(eye, dir)) return;
        if (UseBuiltDoor(eye, dir)) return;
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
            return;
        }
        if (!hit)
        {
            return;
        }
        if (Zone.Devices.FirstOrDefault(d => d.Collider == look.Collider) is Device device)
        {
            // The full panel: hopper beside the recipes and queue. A climate pod has only its fuel slot, opened beside the bag.
            if (device.Constructor != null) Menu.OpenCore(device);
            else Open(new OpenContainer { Title = device.Label, Grid = device.Grid, Device = device, Where = () => device.GlobalPosition });
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
                return;
            }
            if (type.IsLight && Godot.Input.IsKeyPressed(Godot.Key.Shift))
            {
                LightEdit = look.Cell; // Shift+E: style this one light on the CORE tab
                Menu.OpenCore(v, 0);
                return;
            }
            if (type.IsLight && v.ToggleLight(look.Cell))
            {
                return;
            }
            if (Zone.Data.Items.Machines.ForBlock(type.Id) != null)
            {
                int at = 0, k = 0;
                foreach (var cell in v.Machines.Keys) { if (cell.Equals(look.Cell)) at = k; k++; }
                Menu.OpenCore(v, at);
                return;
            }
        }
        if (UsePlace(look)) return;
    }

    /// <summary>F at a climate pod: it goes back in the pack with the fuel still in its slot.</summary>
    bool PickUpPod()
    {
        if (Menu.IsOpen || !WorldRay.Cast(Zone, Survivor, UseReach, Layers.World | Layers.Hull | Layers.Props, out LookHit look)
            || Zone.Devices.FirstOrDefault(d => d.Collider == look.Collider && d.IsClimate) is not Device pod) return false;
        var back = new Bill();
        back.Add(Device.ClimateItem, 1);
        foreach (var (id, n) in pod.Storage.Contents) back.Add(id, n);
        var spill = new Bill();
        foreach (var (id, n) in back)
            foreach (var (left, m) in Pack.Add(id, n)) spill.Add(left, m);
        if (!spill.IsEmpty) SetDown(spill, pod.GlobalPosition);
        Zone.Devices.Remove(pod);
        pod.QueueFree();
        return true;
    }

    /// <summary>What E (or F) would do with what is under the crosshair, for the prompt beside it; empty if nothing usable is in reach.</summary>
    public string UsePrompt()
    {
        if (Survivor != null && Survivor.OnBike != null && !Menu.IsOpen) return "F  Get off  ·  W/S throttle  Mouse or A/D steer  Alt look  Shift boost  Space hop";
        if (Survivor == null || Suit == null || Suit.Dead || Survivor.Seated != null || Menu.IsOpen) return "";
        if (Spaceflight.UsePrompt() is string door) return door;
        if (Bikes?.Aimed(Survivor.WorldEye(), Survivor.WorldLook(), UseReach) != null) return "F  Ride  ·  E  Pick up";
        if (Bunker?.Mine is Slice.Start.CryoPod pod && !pod.Open && Survivor.WorldFeet.DistanceTo(Bunker.CryoStand) < 1.5f) return "F  Open the pod";
        Vector3 eye = Survivor.WorldEye(), dir = Survivor.WorldLook();
        bool hit = WorldRay.Cast(Zone, Survivor, UseReach, Layers.World | Layers.Hull | Layers.Props, out LookHit look);
        if (Zone.Loot.Pick(eye, dir, (hit ? look.Distance : UseReach) + 0.3f, out _) is Lootable loot) return $"E  Open {loot.Name.ToLowerInvariant()}";
        if (Bunker?.SealedAlong(eye, dir, (hit ? look.Distance : UseReach) + 0.3f) is Slice.Start.BunkerLocker sealedOne) return $"LOCKER {sealedOne.Number} · SEALED";
        if (PlacePrompt(eye, dir, hit, look) is { Length: > 0 } place) return place;
        if (!hit) return "";
        if (Zone.Devices.FirstOrDefault(d => d.Collider == look.Collider) is Device device)
            return device.Constructor != null ? $"E  Open {device.Label.ToLowerInvariant()}"
                : $"E  Fuel  ·  F  Pick up  ·  {(device.Running ? "ON" : "OFF")}  ·  {Slice.Ui.Alerts.Clock(device.SecondsLeft)}";
        if (look.Vessel is Vessel v && v.Structure.TryGet(look.Cell, out Block b))
        {
            BlockType type = v.Structure.TypeOf(b);
            if (type.IsContainer) return v == Bunker?.Ruin ? "E  Open locker" : "E  Open storage";
            if (type.IsCommandChair) return "F  Command chair";
            if (type.IsLight) return (b.Off ? "E  Light on" : "E  Light off") + "   ·   Shift+E  Colour";
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
        // On the remote toolbar the food stays in its core's storage until it is eaten.
        bool remote = Pack.OnRemoteBar && slot == Pack.Selected;
        Slot s = remote ? Pack.InHand : Pack.Grid[slot];
        if (remote && !Pack.HandUsable) return false;
        if (s.Item == null || !Zone.Data.Consumables.TryGetValue(s.Item, out ConsumableEffect e)) return false;
        Vitals v = Suit.Vitals;
        VitalsRules r = Suit.Sim.Data.Vitals;
        if (e.Food > 0 && v.Food >= r.Food.Max - 0.5 && e.Health <= 0 && e.Oxygen <= 0)
        {
            Say("NOT HUNGRY", Tones.Text);
            return false;
        }
        string name = Zone.Data.Items.Item(s.Item).Name;
        if (remote ? Pack.HandSource.Remove(s.Item, 1) != 1 : !Pack.Grid.TakeOne(slot)) return false;
        if (e.Food > 0) v.Food = Math.Min(r.Food.Max, v.Food + e.Food * r.Food.Max);
        if (e.Health > 0) v.Heal(e.Health);
        if (e.Oxygen > 0) v.Oxygen = Math.Min(Suit.Sim.OxygenCapacity, v.Oxygen + e.Oxygen);
        Stats.Used++;
        return true;
    }

    /// <summary>Sets things down where they can be picked up again (a full bag, a full storage, a refund with nowhere to go): into your bag lying there, or a new one. Never lost.</summary>
    public void SetDown(Bill items, Vector3 at)
    {
        if (items == null || items.IsEmpty) return;
        Zone.Loot.Spill(items, at);
        Say($"BAG FULL · DROPPED {Zone.Data.Items.Describe(items, 3)}", Tones.Caution);
    }

    /// <summary>Drops a stack from the bag onto the ground in front of you (dragged out of the inventory window): into your bag there (in space it floats where it was let go).</summary>
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
        Zone.Loot.Spill(bill, Survivor.WorldFeet + new Vector3(look.X, 0f, look.Z).Normalized() * 1.2f + Vector3.Up * 0.3f);
    }

    // ---- Death and the bag ----------------------------------------------------------------------

    bool _wasDead;

    /// <summary>The bag your last death left, while it holds anything.</summary>
    public Lootable DeathBag { get; private set; }

    /// <summary>
    /// On death your things go into your bag where you fell, marked on the compass and the map; the
    /// toolbar and the Sleeper tool stay with you, and nothing is lost. Then whoever was about takes it
    /// in (<see cref="Aftermath"/>).
    /// </summary>
    void CheckDeath()
    {
        if (Suit.Dead && !_wasDead)
        {
            _wasDead = true;
            if (Survivor.Seated != null) Survivor.StandUp();
            Bill all = Pack.TakeBagForDeath();
            Vector3 feet = Survivor.WorldFeet, ahead = Survivor.WorldLook() with { Y = 0f };
            ahead = ahead.LengthSquared() > 1e-4f ? ahead.Normalized() : Vector3.Forward;
            // At the feet, a little ahead of them: the body goes down the other way.
            if (!all.IsEmpty) DeathBag = Zone.Loot.Bag(all, feet + ahead * 0.5f, "Your bag", Zone.Loot.YourColour, yours: true);
            Stats.Deaths++;
            CloseContainer();
            Menu.Close();
            Aftermath(feet, -ahead);
        }
        if (DeathBag != null && !Zone.Loot.All.Contains(DeathBag)) DeathBag = null;
    }

    void Respawn()
    {
        Outcome o = Suit.Sim.Respawn(RespawnChoice.NearestTent);
        if (!o.Ok) return;
        WakeAgain();
        Survivor.Body.Velocity = N.Vector3.Zero;
        _wasDead = false;
        Report(o);
    }

    // ---- Creatures' bodies ---------------------------------------------------------------------

    /// <summary>
    /// A creature died: its body holds its drops (wildlife's drop table: meat by size, hide, and a rare part
    /// by chance; a person's weapon, ammo and goods) and is searched as a body (owner). Blown apart or
    /// burned away, a person's bag drops from them, and what an animal had is in a bag where it was. The
    /// player's kills are worth experience. The sleeper's own body is none of this: its things are in
    /// its bag already.
    /// </summary>
    void OnCreatureDied(Creature c, ITarget killer)
    {
        Species s = c.Species;
        if (s.Id == Slice.Player.CharacterModel.Sleeper.Id) return;
        Laid(s);
        if (s.Rig == RigKind.Swarm)
        {
            // A swarm doesn't lie down and leave a body: the last of it goes in a burst.
            Zone.Wildlife.SimOf(c, out Vector3 origin, out _);
            Zone.Blood.Pop(c.Position + origin + Vector3.Up * c.Altitude, s.Blood, 40);
            Zone.Wildlife.Sound(origin, a => a.Play("pop", c.Position + Vector3.Up, 0.7f, 2f));
            c.Removed = true;
            if (killer == Target)
            {
                Stats.Kills++;
                Progress.Earn(s.Stats.Xp, $"{s.CommonName} brought down");
            }
            return;
        }
        Bill drops = BodyDrops(s, Wildlife.Rng.For(s.Seed, "drop", c.Id));
        Arms?.People?.Drops(c, drops); // the weapon it carried, and the ammo it had left for it
        if (_given.Remove(c, out Bill given)) drops = Less(drops, given); // (what it tossed you is on the ground already)
        if (!EndCreature(c, out Vector3 left)) Corpse(s, drops, Zone.Wildlife.WorldOf(c), c, () => c.Removed);
        else if (BagCarrier(s)) DropBag(s, drops, left, c.HitDir, c);
        else Zone.Loot.Leftovers(drops, left, SackColour(s), c);
        if (killer == Target)
        {
            Stats.Kills++;
            Progress.Earn(s.Stats.Xp, $"{s.CommonName} brought down");
            // One of a band out on a town's ground that never came for you: its people hear of it.
            if (s.Behaviour == Behaviour.Folk && c.Herd?.Party?.Temper.Stance is { } rolled && rolled != Stance.Hostile)
                Places?.TownHolding(Zone.Wildlife.WorldOf(c), BandReach)?.Did(Slice.Places.Deed.Kill);
        }
    }

    /// <summary>How far out from a town its people go about on its ground (bands on its roads), m: your standing with the town is your standing with them.</summary>
    public const float BandReach = 400f;

    /// <summary>Whether a body carries its things in a bag: a person (not a Husk, not a machine).</summary>
    public static bool BagCarrier(Species s) => s.Figure != null && !s.Husk && !s.Mechanical;

    /// <summary>The colour of someone's bag: their clothes' colour, gone toward their leather.</summary>
    public static Color BagColour(Figure f) => f.Cloth.Lerp(f.Leather, 0.35f);

    /// <summary>What's left of a bill once another is taken out of it.</summary>
    static Bill Less(Bill from, Bill taken)
    {
        var left = new Bill();
        foreach (var (id, n) in from) if (n > taken[id]) left.Add(id, n - taken[id]);
        return left;
    }

    /// <summary>A person blown apart or burned away: their bag drops from their hip as they go, onto the ground where they were, on the side they didn't fall to.</summary>
    void DropBag(Species s, Bill drops, Vector3 feet, Vector3 fell, object owner)
    {
        if (drops.IsEmpty) return;
        Vector3 back = new(-fell.X, 0f, -fell.Z);
        back = back.LengthSquared() > 1e-4f ? back.Normalized() : Vector3.Back;
        Zone.Loot.Toss(drops, feet + Vector3.Up * s.Figure.Height * 0.5f, feet + back * 0.45f, "Bag", BagColour(s.Figure), owner);
    }

    /// <summary>
    /// What a body holds follows what it was. A machine (robot, drone, cyborg) leaves salvage by its
    /// size: metal, wiring, and a board or plate by chance. A person carries the goods of their trade
    /// (slice.json "carried", each by chance) and what their hands held besides a weapon (their weapon
    /// and its ammo are added from what they really had), never meat or hide. An animal, a Husk or a
    /// Hollow leaves meat and hide (no coat has a material of its own yet) and its rare part by chance.
    /// </summary>
    public Bill BodyDrops(Species s, Wildlife.Rng rng)
    {
        var drops = new Bill();
        if (s.Mechanical)
        {
            float r = MathF.Max(0.3f, s.Stats.Radius);
            int bulk = Math.Clamp((int)MathF.Round(r * 4f), 1, 12);
            drops.Add("iron-bar", bulk);
            drops.Add("copper-wire", Math.Max(1, bulk / 2));
            if (rng.Chance(MathF.Min(0.9f, 0.3f + r * 0.3f))) drops.Add("circuit-board", Math.Max(1, bulk / 4));
            if (r >= 1f) drops.Add("hull-plate", Math.Max(1, bulk / 3));
            return drops;
        }
        if (s.Figure != null && !s.Husk)
        {
            switch (s.Figure.Prop)
            {
                // (A weapon and its ammo are what they carried: Slice.Arms.SimArms.Drops.)
                case "hoe" or "cane": if (rng.Chance(0.5f)) drops.Add("iron-bar", 1); break;
                case "umbrella": drops.Add("polymer", 1); break;
            }
            if (Zone.Data.Rules.Carried.TryGetValue(s.Figure.Role.ToString().ToLowerInvariant(), out var goods))
                foreach (SliceRules.CarriedGood g in goods)
                    if (rng.Chance(g.Chance) && Zone.Data.Items.Has(g.Item)) drops.Add(g.Item, rng.Int(1, Math.Max(1, g.Most)));
            return drops;
        }
        if (s.Drops.Meat > 0) drops.Add("meat", s.Drops.Meat);
        drops.Add("hide", Math.Max(1, s.Drops.Meat / 3));
        if (s.Drops.RarePart != "" && rng.Chance(s.Drops.RareChance)) drops.Add("rare-tissue", 1);
        return drops;
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
            if (d.Constructor == null)
            {
                d.Tick(dt); // a climate node draws in real time, whatever crafting's test scale
                continue;
            }
            Snapshot(d.Constructor);
            d.Tick(t);
            Finished(d.Constructor);
        }
        foreach (Vessel v in Zone.Vessels)
        {
            // A vessel on its build stand holds there until its pilot first takes its seat.
            if (v.Stand != null && Survivor.Seated == v) v.Stand = null;
            v.HoldOnStand();
            v.TickMode();
            v.TickPower(dt, (Mode == GameMode.Creative || FreePower) && v.IsPlayers); // in real time (Creative's free power is the player's own: others burn theirs): what its devices drew this tick, the climate nodes by it too
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
        foreach (Device d in Zone.Devices) d.Grid?.Reconcile();
        AutoTransmit(dt);
        Zone.Loot.Tick();
        // A structure's storage stays open while you are in its area; anything else within a few steps; an emptied bag is gone at once.
        if (Opened != null && (Opened.Loot != null && Zone.Loot.Open == null || (Opened.Vessel != null
                ? !Slice.Construction.StructureArea.Contains(Opened.Vessel, Survivor.WorldFeet, Zone.Data.Rules.Build.Area)
                : Opened.Where().DistanceTo(Survivor.WorldFeet) > 6f))) CloseContainer();
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
        c.Lock ??= item => Progress.ItemAllowed(item) ? null : Progress.LockReason(item.Id);
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
            if (!now.Contains(job) && job.Lane == Lane.Order) Notices.Post(Slice.Ui.NoticeKind.Made, "MADE", $"{job.Count} {job.Item.Name}");
        }
    }
}
