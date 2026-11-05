using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SurvivalVitals.Model;

/// <summary>What a player keeps through any death, fresh starts included.</summary>
public sealed class Progress
{
    public int Level = 3;
    public readonly List<string> Unlocks = new() { "survival-replicator", "survival-tent", "galley-replicator" };
    public readonly SortedDictionary<string, double> Standing = new() { ["Vharn Dominion"] = -5 };

    public string Fingerprint() =>
        $"{Level}|{string.Join(",", Unlocks)}|{string.Join(",", Standing.Select(kv => $"{kv.Key}={kv.Value}"))}";
}

public sealed class Survivor
{
    public Vitals Vitals;
    public readonly Inventory Inventory = new();
    public readonly Loadout Gear = new();
    /// <summary>Lives outside the inventory, so no drop rule can ever take it.</summary>
    public bool HasSurvivalTool = true;
    public readonly Progress Progress = new();
    public Pos Position;
    public bool Dead;
    public string DeathCause;
    public Pos DeathPosition;
}

public sealed class EscapePod
{
    public int Id;
    public Pos Position;
    public int Rations;
    public bool FreshStart;
}

public sealed class Bush
{
    public int Id;
    public Pos Position;
    public double RipeAt;
    public bool Ripe(double now) => now >= RipeAt;
}

/// <summary>
/// One player surviving on one planet patch: the body, what they carry and wear, and the
/// world's survival objects (replicators, respawn points, escape pods, bushes, backpacks).
/// The scene sets the player's position and activity each physics tick and calls the actions
/// below on key presses; tests drive exactly the same calls without any scene at all.
/// </summary>
public sealed class SurvivalSim
{
    public readonly SurvivalData Data;
    public readonly Planet Planet;
    public readonly int Seed;
    public double Time;

    public readonly Survivor Player = new();
    public readonly WorldStore World = new();
    public readonly List<Replicator> Replicators = new();
    public readonly List<RespawnPoint> RespawnPoints = new();
    public readonly List<EscapePod> Pods = new();
    public readonly List<Bush> Bushes = new();
    public readonly Inventory BaseStorage = new();
    public readonly PowerGrid BasePower;
    public DropRule DropRule;

    /// <summary>Things that happened on their own (helmet sealed, bottle plugged in, death), for the visor.</summary>
    public readonly List<string> Events = new();
    public TickReport Last = new();
    public Backpack LastBackpack;

    bool _airOkBefore = true;
    int _nextId = 1;

    VitalsRules Rules => Data.Vitals;

    public SurvivalSim(SurvivalData data, int seed)
    {
        Data = data;
        Seed = seed;
        Planet = new Planet(data.Planet, seed);
        PlanetData p = data.Planet;
        DropRule = data.Vitals.Death.Drop;

        BasePower = new PowerGrid { Stored = p.BasePower };
        BaseStorage.Organics = p.Storage.Organics;
        foreach (PlacedReplicator placed in p.Replicators)
            Replicators.Add(new Replicator
            {
                Id = _nextId++, Tier = data.Food.Tier(placed.Tier), Position = placed.Position,
                Energy = BasePower, Storage = BaseStorage,
            });
        foreach (RespawnSpot spot in p.RespawnPoints)
            RespawnPoints.Add(new RespawnPoint { Id = spot.Id, Name = spot.Name, Kind = spot.Kind, Position = spot.Position, Stand = spot.Stand });
        Pods.Add(new EscapePod { Id = _nextId++, Position = p.EscapePod.Position, Rations = p.EscapePod.Rations });
        foreach (Pos at in p.Bushes) Bushes.Add(new Bush { Id = _nextId++, Position = at });

        StartData start = p.Start;
        StartingInventory kit = start.Inventory;
        Inventory inv = Player.Inventory;
        inv.Organics = kit.Organics;
        inv.SmallBottles = kit.SmallBottles;
        inv.LargeBottles = kit.LargeBottles;
        inv.Medkits = kit.Medkits;
        for (int i = 0; i < kit.ReplicatorKits; i++) inv.Kits.Add(new ReplicatorKit { Fuel = SurvivalTier.Fuel });
        foreach (string s in kit.Suits) inv.Suits.Add(new SuitItem { Kind = s });
        inv.Boosters.AddRange(kit.Boosters);
        if (start.Suit != null) Player.Gear.Suit = new SuitItem { Kind = start.Suit };
        Player.Vitals = new Vitals(Rules, OxygenCapacity) { Food = start.Food * Rules.Food.Max };
        Player.Position = start.Position;
    }

    // ---- State the scene and HUD read ----------------------------------------------------

    public Modifiers Mods => Player.Gear.Total(Data.Armor);
    public double OxygenCapacity => Player.Gear.OxygenCapacity(Data.Armor, Rules.Oxygen);
    public GearState Gear => new(Player.Gear.HasSuit, Player.Gear.HelmetOn, OxygenCapacity, Mods);
    public Exposure Here => Environment?.Invoke(Player.Position, Time) ?? Planet.Sample(Player.Position, Time);

    /// <summary>
    /// Optional: where the exposure comes from instead of the data file's planet (a game with its
    /// own generated world, climate and weather). Same contract: a pure function of place and time.
    /// </summary>
    public Func<Pos, double, Exposure> Environment;
    public ReplicatorTier SurvivalTier => Data.Food.Replicators.First(r => r.Portable);

    // ---- The tick ------------------------------------------------------------------------

    public TickReport Tick(double dt, Activity activity)
    {
        Time += dt;
        if (Player.Dead)
        {
            Last = new TickReport { Exposure = Here };
            return Last;
        }

        Exposure env = Here;
        OxygenRules o = Rules.Oxygen;
        bool airOk = env.Volume != null || env.OxygenPercent >= o.BreathablePercent;
        // The helmet seals itself on the way into bad air, once; the player can still open it.
        if (o.AutoHelmet && _airOkBefore && !airOk && Player.Gear.HasSuit && !Player.Gear.HelmetOn)
        {
            Player.Gear.HelmetOn = true;
            Events.Add("Helmet sealed: the air here isn't breathable.");
        }
        _airOkBefore = airOk;

        Last = Player.Vitals.Tick(dt, activity, env, Gear);
        if (o.AutoBottle && !airOk) AutoBottle();
        if (Player.Vitals.Dead) Die(CauseOfDeath(Player.Vitals.LastDamage));
        return Last;
    }

    /// <summary>Plugs in a bottle when suit oxygen runs low: the biggest one that won't be wasted.</summary>
    void AutoBottle()
    {
        Vitals v = Player.Vitals;
        Inventory inv = Player.Inventory;
        double cap = OxygenCapacity;
        if (!Player.Gear.HelmetOn || cap <= 0 || v.Oxygen >= Rules.Oxygen.WarnBelow * cap) return;
        bool large = inv.LargeBottles > 0 && (cap - v.Oxygen >= Rules.Oxygen.LargeBottle || inv.SmallBottles == 0);
        if (!large && inv.SmallBottles == 0) return;
        Outcome used = UseBottle(large);
        if (used.Ok) Events.Add("Suit oxygen low: " + used.Message);
    }

    /// <summary>The player hit the ground at this speed. Returns the health lost.</summary>
    public double Land(double speed)
    {
        if (Player.Dead) return 0;
        double lost = Player.Vitals.Land(speed, Mods);
        if (lost > 0) Events.Add($"Hard landing: -{lost:F0} health.");
        if (Player.Vitals.Dead) Die(CauseOfDeath(DamageKind.Fall));
        return lost;
    }

    // ---- Death and respawn ---------------------------------------------------------------

    public void Kill()
    {
        if (Player.Dead) return;
        Player.Vitals.Kill();
        Die(CauseOfDeath(DamageKind.Debug));
    }

    static string CauseOfDeath(DamageKind kind) => kind switch
    {
        DamageKind.Starvation => "Starved",
        DamageKind.Suffocation => "Suffocated",
        DamageKind.Cold => "Froze",
        DamageKind.Heat => "Overheated",
        DamageKind.Radiation => "Radiation",
        DamageKind.Lava => "Fell into lava",
        DamageKind.Fall => "Hard landing",
        DamageKind.Hit => "Killed",
        _ => "Self-destruct (K)",
    };

    void Die(string cause)
    {
        Survivor p = Player;
        p.Dead = true;
        p.DeathCause = cause;
        p.DeathPosition = p.Position;

        // Worn armor takes the knock, whatever the drop setting.
        if (p.Gear.Suit != null)
            p.Gear.Suit.Durability = Math.Max(0, p.Gear.Suit.Durability - Rules.Death.ArmorDurabilityLoss);

        Inventory dropped = new();
        if (DropRule != DropRule.Nothing) dropped = p.Inventory.TakeAll();
        if (DropRule == DropRule.Everything)
        {
            if (p.Gear.Suit != null) dropped.Suits.Add(p.Gear.Suit);
            dropped.Boosters.AddRange(p.Gear.Boosters);
            p.Gear.Suit = null;
            p.Gear.Boosters.Clear();
            p.Gear.HelmetOn = false;
        }

        LastBackpack = dropped.IsEmpty ? null : World.Drop(p.Position, dropped, Time, cause);
        Events.Add(LastBackpack != null
            ? $"{cause}. Backpack with {dropped.ItemCount} items left at the marker."
            : $"{cause}. Nothing dropped.");
    }

    public List<RespawnOption> RespawnOptions() => Respawning.Options(Player.DeathPosition, RespawnPoints);

    public Outcome Respawn(RespawnChoice choice)
    {
        if (!Player.Dead) return Outcome.No("Still alive.");
        RespawnOption option = RespawnOptions().First(o => o.Choice == choice);
        if (!option.Available) return Outcome.No(option.Detail);

        Pos at;
        string where;
        if (choice == RespawnChoice.FreshStart)
        {
            EscapePod pod = LandFreshPod();
            at = new Pos(pod.Position.X + 2.5, 0, pod.Position.Z + 2.5);
            where = "a new escape pod";
        }
        else
        {
            at = option.Point.Stand;
            where = option.Point.Name.ToLowerInvariant();
        }

        Player.Position = at;
        Player.Dead = false;
        Player.Vitals.ResetForRespawn(OxygenCapacity);
        _airOkBefore = true;
        return Outcome.Done($"Back on your feet at {where}.");
    }

    /// <summary>
    /// Picks a seeded spot on a ring around the patch, clear of hazards, faction land and rooms,
    /// and lands a new pod there with a few ration packs. Old pods stay where they are.
    /// </summary>
    EscapePod LandFreshPod()
    {
        int index = Pods.Count(p => p.FreshStart);
        Band ring = Data.Planet.FreshStartRing;
        Pos spot = default;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            long key = index * 64L + attempt;
            double angle = SeededHash.Unit(Seed, key, salt: 2) * Math.PI * 2;
            double radius = ring.Low + (ring.High - ring.Low) * SeededHash.Unit(Seed, key, salt: 3);
            spot = Pos.Flat(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
            bool clear = Data.Planet.Zones.All(z => spot.FlatDistanceTo(z.Center) > z.Radius + z.Falloff + 6)
                && Planet.TerritoryAt(spot) == null && Planet.VolumeAt(spot) == null;
            if (clear) break;
        }
        var pod = new EscapePod { Id = _nextId++, Position = spot, Rations = Data.Planet.EscapePod.Rations, FreshStart = true };
        Pods.Add(pod);
        return pod;
    }

    public Outcome SetHome(RespawnPoint point)
    {
        if (!point.CanBeHome) return Outcome.No("Only a clone chamber or healing booth can be home.");
        foreach (RespawnPoint p in RespawnPoints) p.IsHome = false;
        point.IsHome = true;
        return Outcome.Done($"{point.Name} is now home.");
    }

    // ---- Gear ----------------------------------------------------------------------------

    public Outcome ToggleHelmet()
    {
        if (!Player.Gear.HasSuit) return Outcome.No("No suit, no helmet: put on a suit (I).");
        Player.Gear.HelmetOn = !Player.Gear.HelmetOn;
        return Outcome.Done(Player.Gear.HelmetOn ? "Helmet sealed." : "Helmet open.");
    }

    public Outcome EquipSuit(string kind)
    {
        Inventory inv = Player.Inventory;
        Loadout gear = Player.Gear;
        SuitItem suit = inv.Suits.FirstOrDefault(s => s.Kind == kind);
        if (suit == null) return Outcome.No("You don't have that suit.");
        inv.Suits.Remove(suit);
        if (gear.Suit != null) inv.Suits.Add(gear.Suit);
        gear.Suit = suit;
        int slots = gear.Slots(Data.Armor);
        int removed = 0;
        while (gear.Boosters.Count > slots)
        {
            inv.Boosters.Add(gear.Boosters[^1]);
            gear.Boosters.RemoveAt(gear.Boosters.Count - 1);
            removed++;
        }
        Player.Vitals.Oxygen = Math.Min(Player.Vitals.Oxygen, OxygenCapacity);
        string name = Data.Armor.Suit(kind).Name;
        return Outcome.Done(removed > 0
            ? $"Wearing the {name.ToLowerInvariant()}: {removed} booster(s) back in your pack."
            : $"Wearing the {name.ToLowerInvariant()} ({slots} booster slots).");
    }

    public Outcome RemoveSuit()
    {
        Loadout gear = Player.Gear;
        if (gear.Suit == null) return Outcome.No("No suit on.");
        Player.Inventory.Suits.Add(gear.Suit);
        Player.Inventory.Boosters.AddRange(gear.Boosters);
        gear.Suit = null;
        gear.Boosters.Clear();
        gear.HelmetOn = false;
        Player.Vitals.Oxygen = 0;
        return Outcome.Done("Suit off: no helmet, no suit oxygen, no jetpack.");
    }

    public Outcome EquipBooster(string id)
    {
        Loadout gear = Player.Gear;
        if (!gear.HasSuit) return Outcome.No("Boosters fit into a suit.");
        int slots = gear.Slots(Data.Armor);
        if (gear.Boosters.Contains(id)) return Outcome.No("Already fitted.");
        if (gear.Boosters.Count >= slots)
            return Outcome.No(slots == 0 ? "This suit has no booster slots." : $"All {slots} slots are full: remove a booster first.");
        if (!Player.Inventory.Boosters.Remove(id)) return Outcome.No("You don't have that booster.");
        gear.Boosters.Add(id);
        return Outcome.Done($"{Data.Armor.Booster(id).Name} fitted.");
    }

    public Outcome RemoveBooster(string id)
    {
        if (!Player.Gear.Boosters.Remove(id)) return Outcome.No("Not fitted.");
        Player.Inventory.Boosters.Add(id);
        Player.Vitals.Oxygen = Math.Min(Player.Vitals.Oxygen, OxygenCapacity);
        return Outcome.Done($"{Data.Armor.Booster(id).Name} removed.");
    }

    // ---- Consumables ---------------------------------------------------------------------

    public Outcome Eat(string kind)
    {
        Vitals v = Player.Vitals;
        if (Rules.Food.Max - v.Food < 1) return Outcome.No("Not hungry.");
        FoodItem item = Player.Inventory.TakeOldestFood(kind);
        if (item == null) return Outcome.No("None left.");
        FoodKind food = Data.Food.Food(kind);
        double gain = Data.Food.NourishmentOf(item, Time);
        double fresh = Data.Food.Freshness(food, Time - item.MadeAt);
        v.Food = Math.Min(Rules.Food.Max, v.Food + gain);
        return Outcome.Done($"Ate {food.Name.ToLowerInvariant()}: +{gain:F0} food ({fresh:P0} fresh).");
    }

    public Outcome UseBottle(bool large)
    {
        Inventory inv = Player.Inventory;
        double cap = OxygenCapacity;
        if ((large ? inv.LargeBottles : inv.SmallBottles) <= 0) return Outcome.No($"No {(large ? "large" : "small")} oxygen bottles.");
        if (cap <= 0) return Outcome.No("No suit to fill.");
        if (Player.Vitals.Oxygen >= cap) return Outcome.No("Suit oxygen is already full.");
        if (large) inv.LargeBottles--;
        else inv.SmallBottles--;
        double amount = large ? Rules.Oxygen.LargeBottle : Rules.Oxygen.SmallBottle;
        Player.Vitals.Oxygen = Math.Min(cap, Player.Vitals.Oxygen + amount);
        return Outcome.Done($"{(large ? "Large" : "Small")} oxygen bottle: +{amount:F0}.");
    }

    public Outcome UseMedkit() => Healing.UseMedkit(Player.Vitals, Player.Inventory, Rules);

    public Outcome UseBooth() => Healing.UseBooth(Player.Vitals, Rules);

    // ---- Replicators ---------------------------------------------------------------------

    public Replicator NearestReplicator(Pos from, double reach) =>
        Replicators.Where(r => r.Position.FlatDistanceTo(from) <= reach)
                   .OrderBy(r => r.Position.FlatDistanceTo(from)).FirstOrDefault();

    public Outcome Feed(Replicator replicator) =>
        replicator.FeedToFull(Player.Vitals, Player.Inventory, Rules.Food.Max);

    public Outcome Craft(Replicator replicator, int tier)
    {
        FoodKind food = Data.Food.Foods.FirstOrDefault(f => f.Tier == tier);
        return food == null ? Outcome.No("Nothing to make at that tier.") : replicator.Craft(food, Player.Inventory, Time);
    }

    /// <summary>
    /// B: packs up a survival replicator within reach, or sets one down at <paramref name="at"/>,
    /// building a new one from ore if none is carried.
    /// </summary>
    public Outcome DeployOrPack(Pos at)
    {
        ReplicatorTier tier = SurvivalTier;
        Replicator mine = Replicators.Where(r => r.Tier.Portable && r.Position.FlatDistanceTo(Player.Position) <= 3.5)
                                     .OrderBy(r => r.Position.FlatDistanceTo(Player.Position)).FirstOrDefault();
        if (mine != null)
        {
            Replicators.Remove(mine);
            Player.Inventory.Kits.Add(new ReplicatorKit { Fuel = ((FuelTank)mine.Energy).Fuel });
            return Outcome.Done($"{tier.Name} packed up.");
        }

        Outcome allowed = Deployment.Check(tier, at, Planet);
        if (!allowed.Ok) return allowed;

        Inventory inv = Player.Inventory;
        ReplicatorKit kit = inv.Kits.FirstOrDefault();
        string message = allowed.Message;
        if (kit != null) inv.Kits.Remove(kit);
        else if (inv.Ore >= tier.BuildOre)
        {
            inv.Ore -= tier.BuildOre;
            kit = new ReplicatorKit { Fuel = tier.Fuel };
            message = $"Built a {tier.Name.ToLowerInvariant()} from {tier.BuildOre} ore and set it down.";
        }
        else
        {
            return Outcome.No($"No {tier.Name.ToLowerInvariant()} to set down. Building one takes {tier.BuildOre} ore; you have {inv.Ore}. Mine rocks (hold the left mouse button).");
        }

        Replicators.Add(new Replicator { Id = _nextId++, Tier = tier, Position = at, Energy = new FuelTank { Fuel = kit.Fuel } });
        return Outcome.Done(message);
    }

    // ---- Foraging, salvage, storage ------------------------------------------------------

    public Outcome PickBush(Bush bush)
    {
        if (!bush.Ripe(Time)) return Outcome.No($"No fruit yet: it regrows in {(bush.RipeAt - Time) / 60:F0} min.");
        ForageRules f = Data.Food.Forage;
        for (int i = 0; i < f.FruitPerBush; i++) Player.Inventory.Food.Add(new FoodItem { Kind = "fruit", MadeAt = Time });
        Player.Inventory.Organics += f.OrganicsPerBush;
        bush.RipeAt = Time + f.RegrowMinutes * 60;
        return Outcome.Done($"Picked {f.FruitPerBush} fruit and {f.OrganicsPerBush:F0} kg of plant matter.");
    }

    public Outcome LootPod(EscapePod pod)
    {
        if (pod.Rations <= 0) return Outcome.No("The pod's ration locker is empty.");
        for (int i = 0; i < pod.Rations; i++) Player.Inventory.Food.Add(new FoodItem { Kind = "ration-pack", MadeAt = Time });
        string message = $"Took {pod.Rations} ration packs from the pod.";
        pod.Rations = 0;
        return Outcome.Done(message);
    }

    public Outcome Recover(Backpack pack) => World.Recover(pack, Player.Inventory);

    public Outcome TakeFromStorage(double kg)
    {
        double amount = Math.Min(kg, BaseStorage.Organics);
        if (amount <= 0) return Outcome.No("Storage has no organics.");
        BaseStorage.Organics -= amount;
        Player.Inventory.Organics += amount;
        return Outcome.Done($"Took {amount:F0} kg organics for the field.");
    }

    public void AddOre(int count) => Player.Inventory.Ore += count;

    /// <summary>Time passing with the player away: nothing in the world expires.</summary>
    public void AdvanceWorld(double seconds) => Time += seconds;

    // ---- Determinism ---------------------------------------------------------------------

    /// <summary>Every number that matters, exactly, so two runs can be compared bit for bit.</summary>
    public string Digest()
    {
        static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        Vitals v = Player.Vitals;
        var sb = new StringBuilder();
        sb.Append($"t={R(Time)} hp={R(v.Health)} food={R(v.Food)} st={R(v.Stamina)} o2={R(v.Oxygen)} ");
        sb.Append($"temp={R(v.BodyTemperature)} rad={R(v.Radiation)} breath={R(v.Breath)} dead={Player.Dead} ");
        sb.Append($"weather={Planet.WeatherAt(Time).Name} inv={Player.Inventory.Fingerprint()} ");
        sb.Append($"suit={Player.Gear.Suit?.Kind}:{R(Player.Gear.Suit?.Durability ?? 0)} boosters={string.Join(",", Player.Gear.Boosters)} ");
        foreach (Backpack b in World.Backpacks) sb.Append($"pack{b.Id}@{b.Position}:{b.Contents.Fingerprint()} ");
        foreach (Replicator r in Replicators) sb.Append($"rep{r.Id}:{r.Tier.Id}@{r.Position}:{R(r.Energy.Available)} ");
        foreach (EscapePod pod in Pods) sb.Append($"pod{pod.Id}@{pod.Position}:{pod.Rations} ");
        sb.Append($"storage={R(BaseStorage.Organics)} power={R(BasePower.Stored)}");
        return sb.ToString();
    }
}
