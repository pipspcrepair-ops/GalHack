using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Godot;
using SurvivalVitals.Model;

namespace SurvivalVitals;

/// <summary>
/// Headless test of survival. Run with:
///   godot --headless --path prototypes/survival-vitals res://tests/VitalsTest.tscn
/// The model checks run without any scene; the last stage builds the playable scene and
/// presses real keys in it. Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class VitalsTest : Node
{
    const double Dt = 1.0 / 60.0;
    int _failures;

    public override void _Ready()
    {
        Section(DrainRates);
        Section(WarningsComeFirst);
        Section(ColdZone);
        Section(RadiationZone);
        Section(Oxygen);
        Section(Feeding);
        Section(Deploying);
        Section(FoodDecay);
        Section(HealingTools);
        Section(NoLingeringConditions);
        Section(DeathDrops);
        Section(Respawning);
        Section(DamageIsNotCapped);
        Section(Determinism);
        Section(EarlyGameIsLightPressure);
        StartSceneStage();
    }

    // ---- Food, stamina and starving ------------------------------------------------------

    void DrainRates()
    {
        SurvivalData d = Fresh();
        VitalsRules r = d.Vitals;
        GearState gear = GearOf(d, Suit("light"));
        foreach (var (name, pace, mining, multiplier) in new[]
        {
            ("walking", Pace.Walk, false, r.Food.Pace.Walk),
            ("running", Pace.Run, false, r.Food.Pace.Run),
            ("sprinting", Pace.Sprint, false, r.Food.Pace.Sprint),
            ("mining", Pace.Idle, true, r.Food.Pace.Idle * r.Food.MiningMultiplier),
        })
        {
            var v = new Vitals(r, gear.OxygenCapacity) { Food = 400 };
            const double seconds = 8;
            for (int i = 0; i < seconds * 60; i++) v.Tick(Dt, new Activity(pace, mining), Exposure.Mild, gear);
            double lost = 400 - v.Food, expected = r.Food.DrainPerMinute / 60 * multiplier * seconds;
            Check(Math.Abs(lost - expected) < 1e-6,
                $"food drain {name}: {lost:F3} in {seconds} s, data says {expected:F3} ({multiplier}x of {r.Food.DrainPerMinute}/min)");
        }

        var winded = new Vitals(r, gear.OxygenCapacity);
        TickReport last = null;
        double healthBefore = winded.Health;
        double dry = r.Stamina.Max / r.Stamina.SprintDrainPerSecond;
        for (int i = 0; i < (dry + 1) * 60; i++) last = winded.Tick(Dt, new Activity(Pace.Sprint), Exposure.Mild, gear);
        Check(winded.Winded && last.Pace == Pace.Walk && winded.Health == healthBefore,
            $"sprinting stamina dry ({dry:F0} s) drops you to a walk and costs no health");

        var starving = new Vitals(r, gear.OxygenCapacity) { Food = 0, Health = 300 };
        for (int i = 0; i < 10 * 60; i++) starving.Tick(Dt, new Activity(Pace.Idle), Exposure.Mild, gear);
        double drained = 300 - starving.Health, data = r.Food.StarvingDamagePerSecond * 10;
        Check(Math.Abs(drained - data) < 1e-6, $"at zero food health drains {drained:F2} in 10 s (data says {data:F2})");

        var kit = new Inventory { Medkits = 1 };
        double before = starving.Health;
        Outcome medkit = Healing.UseMedkit(starving, kit, r);
        Outcome booth = Healing.UseBooth(starving, r);
        Check(!medkit.Ok && !booth.Ok && kit.Medkits == 1 && starving.Health == before,
            $"at zero food healing is blocked: \"{booth.Message}\"");
    }

    // ---- Every hazard flashes before it hurts --------------------------------------------

    void WarningsComeFirst()
    {
        SurvivalData d = Calm();
        VitalsRules r = d.Vitals;
        var planet = new Planet(d.Planet, 1);
        double noon = TimeAtHour(d.Planet, 12), min = r.MinimumWarningSeconds;
        GearState light = GearOf(d, Suit("light"));
        Loadout sealedSuit = Suit("light");
        sealedSuit.HelmetOn = true;
        GearState helmet = GearOf(d, sealedSuit);
        GearState noSuit = GearOf(d, new Loadout());
        Exposure At(string zone, double east) =>
            planet.Sample(new Pos(d.Planet.Zone(zone).Center.X + east, 0, d.Planet.Zone(zone).Center.Z), noon);
        Exposure airless = At("airless-sinkhole", 0);

        var cases = new (string name, Vitals body, Exposure env, GearState gear, Alert flags)[]
        {
            ("food", new Vitals(r, light.OxygenCapacity) { Food = 0.17 * r.Food.Max }, Exposure.Mild, light, Alert.Food),
            ("suit oxygen", new Vitals(r, helmet.OxygenCapacity) { Oxygen = 150 }, airless, helmet, Alert.Oxygen | Alert.Breath),
            ("no suit in no air", new Vitals(r, 0), airless, noSuit, Alert.Breath),
            ("cold", new Vitals(r, light.OxygenCapacity), At("frozen-valley", 0), light, Alert.Cold),
            ("heat", new Vitals(r, light.OxygenCapacity), At("volcanic-vent", 6), light, Alert.Heat),
            ("radiation", new Vitals(r, light.OxygenCapacity), At("radiation-crater", 0), light, Alert.Radiation),
        };
        foreach (var (name, body, env, gear, flags) in cases)
        {
            var (warn, harm) = FirstWarnAndHarm(body, _ => env, gear, flags, 2000);
            Check(!double.IsNaN(harm) && warn < harm && harm - warn >= min,
                $"{name}: the visor flashes {harm - warn:F1} s before health drains (needs {min} s)");
        }

        // Lava: sprint straight at a pool from 30 m out. The flash comes with distance to spare.
        Zone vent = d.Planet.Zone("volcanic-vent");
        double sprint = r.Movement.SprintSpeed, startX = vent.Center.X + vent.LavaRadius + 30;
        Exposure Run(double t) => planet.Sample(new Pos(startX - sprint * t, 0, vent.Center.Z), noon);
        var lavaBody = new Vitals(r, light.OxygenCapacity);
        var (lavaWarn, lavaHarm) = FirstWarnAndHarm(lavaBody, Run, light, Alert.Lava, 10);
        double warnedAt = Run(lavaWarn).LavaMetres;
        Check(!double.IsNaN(lavaHarm) && lavaWarn < lavaHarm && warnedAt >= r.Lava.WarnMetres - sprint * Dt,
            $"lava: the visor flashes {warnedAt:F1} m from the edge, {lavaHarm - lavaWarn:F1} s ahead even at a sprint");

        // Falls: the visor flashes once you fall faster than a safe landing, before you land.
        (double warned, double landedAfter, double lost) Drop(double height)
        {
            var body = new Vitals(r, light.OxygenCapacity);
            double h = height, speed = 0, t = 0, first = double.NaN;
            while (h > 0)
            {
                speed += r.Movement.Gravity * Dt;
                h -= speed * Dt;
                t += Dt;
                TickReport rep = body.Tick(Dt, new Activity(Pace.Idle, false, speed), Exposure.Mild, light);
                if (double.IsNaN(first) && (rep.Warnings & Alert.Fall) != 0) first = t;
            }
            return (first, t, body.Land(speed, light.Mods));
        }
        var (fallWarn, fallLand, fallLost) = Drop(25);
        Check(!double.IsNaN(fallWarn) && fallWarn < fallLand && fallLost > 0,
            $"a 25 m fall flashes {fallLand - fallWarn:F1} s before the landing that costs {fallLost:F0} health");
        var (hopWarn, _, hopLost) = Drop(4);
        Check(double.IsNaN(hopWarn) && hopLost == 0, "a 4 m drop neither flashes nor hurts");
    }

    // ---- Cold, radiation, oxygen ---------------------------------------------------------

    void ColdZone()
    {
        SurvivalData d = Calm();
        VitalsRules r = d.Vitals;
        TemperatureRules t = r.Temperature;
        var planet = new Planet(d.Planet, 1);
        double noon = TimeAtHour(d.Planet, 12), midnight = TimeAtHour(d.Planet, 0);
        GearState light = GearOf(d, Suit("light"));
        GearState insulated = GearOf(d, Suit("light", "insulation"));
        Pos valley = d.Planet.Zone("frozen-valley").Center;
        Exposure cold = planet.Sample(valley, noon);

        var body = new Vitals(r, light.OxygenCapacity);
        bool harmedInBand = false;
        double firstHarm = double.NaN;
        for (double s = 0; s < 120; s += Dt)
        {
            TickReport rep = body.Tick(Dt, new Activity(Pace.Walk), cold, light);
            if (rep.Damage <= 0) continue;
            if (body.BodyTemperature >= t.ComfortBand.Low) harmedInBand = true;
            if (double.IsNaN(firstHarm)) firstHarm = s;
        }
        Check(!harmedInBand && !double.IsNaN(firstHarm),
            $"the valley ({cold.Temperature:F0} °C at noon) harms a light suit only once body temperature leaves the {t.ComfortBand.Low}-{t.ComfortBand.High} °C band, {firstHarm:F0} s in");

        Exposure open = planet.Sample(d.Planet.Start.Position, noon);
        double before = body.Health;
        TickReport outside = body.Tick(Dt, new Activity(Pace.Walk), open, light);
        Check(outside.Damage == 0 && outside.Harm == Alert.None && body.Health == before,
            $"leaving the valley stops harm on the next tick (body still {body.BodyTemperature:F1} °C, recovering)");

        foreach (var (label, time) in new[] { ("noon", noon), ("midnight", midnight) })
        {
            Exposure env = planet.Sample(valley, time);
            var warm = new Vitals(r, insulated.OxygenCapacity);
            double lost = 0;
            for (int i = 0; i < 300 * 60; i++) lost += warm.Tick(Dt, new Activity(Pace.Walk), env, insulated).Damage;
            Check(lost == 0,
                $"an insulation booster makes the valley harmless at {label} ({env.Temperature:F0} °C; body settles at {warm.BodyTemperature:F1} °C)");
        }
    }

    void RadiationZone()
    {
        SurvivalData d = Calm();
        VitalsRules r = d.Vitals;
        var planet = new Planet(d.Planet, 1);
        Exposure crater = planet.Sample(d.Planet.Zone("radiation-crater").Center, 0);
        GearState light = GearOf(d, Suit("light"));
        GearState shielded = GearOf(d, Suit("light", "radiation"));

        double Lost(GearState gear, Exposure env)
        {
            var body = new Vitals(r, gear.OxygenCapacity);
            for (int i = 0; i < 60 * 60; i++) body.Tick(Dt, new Activity(Pace.Idle), env, gear);
            return r.Health.Max - body.Health;
        }
        double bare = Lost(light, crater), boosted = Lost(shielded, crater);
        Check(bare > 0, $"the crater's {crater.Radiation:F0} RAD drains a light suit: -{bare:F0} health in 60 s");
        Check(boosted < bare * 0.5, $"a radiation booster cuts that to -{boosted:F0}");

        Exposure belowLine = Exposure.Mild with { Radiation = (r.Radiation.DamageAbove - 0.3) / (1 - light.Mods.RadiationShielding) };
        Check(Lost(light, belowLine) == 0, $"just under {r.Radiation.DamageAbove} RAD the visor flashes but health holds");
    }

    void Oxygen()
    {
        SurvivalData d = Calm();
        VitalsRules r = d.Vitals;
        OxygenRules o = r.Oxygen;
        var planet = new Planet(d.Planet, 1);
        Pos sink = d.Planet.Zone("airless-sinkhole").Center;
        Loadout sealedSuit = Suit("light");
        sealedSuit.HelmetOn = true;
        GearState helmet = GearOf(d, sealedSuit), open = GearOf(d, Suit("light"));

        double Used(GearState gear, Exposure env, double seconds)
        {
            var body = new Vitals(r, gear.OxygenCapacity);
            for (int i = 0; i < seconds * 60; i++) body.Tick(Dt, new Activity(Pace.Walk), env, gear);
            return gear.OxygenCapacity - body.Oxygen;
        }
        Exposure vacuum = planet.Sample(sink, 0);
        double used = Used(helmet, vacuum, 10);
        Check(vacuum.OxygenPercent == 0 && Math.Abs(used - o.DrainPerSecond * 10) < 1e-6,
            $"helmet on in the sinkhole's no air: -{used:F2} suit oxygen in 10 s (data says {o.DrainPerSecond * 10:F2})");
        double thin = Used(helmet, Exposure.Mild with { OxygenPercent = o.BreathablePercent / 2 }, 10);
        Check(Math.Abs(thin - used / 2) < 1e-6, $"thin air at {o.BreathablePercent / 2}% drains half as fast (-{thin:F2})");
        Exposure field = planet.Sample(d.Planet.Start.Position, 0);
        Check(Used(open, field, 10) == 0 && Used(helmet, field, 10) == 0, "breathable open air costs no suit oxygen, helmet on or off");

        void Refills(Planet where, Pos at, VolumeKind kind, string name)
        {
            Exposure inside = where.Sample(at, 0);
            var body = new Vitals(r, helmet.OxygenCapacity) { Oxygen = 10 };
            double highest = 0;
            for (int i = 0; i < 60 * 60; i++)
            {
                body.Tick(Dt, new Activity(Pace.Idle), inside, helmet);
                highest = Math.Max(highest, body.Oxygen);
            }
            Check(inside.Volume == kind && body.Oxygen == helmet.OxygenCapacity && highest <= helmet.OxygenCapacity,
                $"suit oxygen refills to exactly {helmet.OxygenCapacity:F0} in a {name}, never more");
        }
        Volume shelter = d.Planet.Volumes.First();
        Refills(planet, new Pos((shelter.Min.X + shelter.Max.X) / 2, 0.1, (shelter.Min.Z + shelter.Max.Z) / 2), VolumeKind.Room, "breathable room");
        d.Planet.Volumes.Add(new Volume
        {
            Id = "lander", Name = "Lander cockpit", Kind = VolumeKind.Cockpit, Temperature = 20,
            Min = new Pos(sink.X - 2, -20, sink.Z - 2), Max = new Pos(sink.X + 2, 20, sink.Z + 2),
        });
        Refills(new Planet(d.Planet, 1), sink, VolumeKind.Cockpit, "cockpit parked in the airless sinkhole");

        var sim = new SurvivalSim(Fresh(), 1);
        Inventory inv = sim.Player.Inventory;
        Vitals v = sim.Player.Vitals;
        double cap = sim.OxygenCapacity;
        v.Oxygen = 100;
        inv.SmallBottles = 1;
        inv.LargeBottles = 2;
        sim.UseBottle(large: false);
        double afterSmall = v.Oxygen;
        sim.UseBottle(large: true);
        double afterLarge = v.Oxygen;
        sim.UseBottle(large: true);
        Check(afterSmall == 100 + o.SmallBottle && afterLarge == afterSmall + o.LargeBottle && v.Oxygen == cap,
            $"bottles add {o.SmallBottle} and {o.LargeBottle}; topping up stops at {cap:F0} ({afterSmall:F0} -> {afterLarge:F0} -> {v.Oxygen:F0})");
        inv.SmallBottles = 1;
        Outcome wasted = sim.UseBottle(large: false);
        Check(!wasted.Ok && inv.SmallBottles == 1, "a full suit refuses a bottle instead of wasting it");

        sim.Player.Position = sink;
        sim.Player.Gear.HelmetOn = false;
        sim.Tick(Dt, new Activity(Pace.Walk));
        Check(sim.Player.Gear.HelmetOn && sim.Events.Any(e => e.StartsWith("Helmet sealed")),
            "walking into no air seals the helmet by itself");
        v.Oxygen = 50;
        inv.LargeBottles = 1;
        sim.Tick(Dt, new Activity(Pace.Walk));
        Check(inv.LargeBottles == 0 && v.Oxygen > 50 + o.LargeBottle - 1, $"low suit oxygen plugs in a bottle by itself ({v.Oxygen:F0})");
    }

    // ---- Replicators and food ------------------------------------------------------------

    void Feeding()
    {
        SurvivalData d = Fresh();
        double max = d.Vitals.Food.Max;
        var sim = new SurvivalSim(d, 1);
        Pos start = d.Planet.Start.Position;
        sim.Player.Position = start;
        Check(sim.DeployOrPack(new Pos(start.X + 2, 0, start.Z)).Ok, "the survival replicator sets down in the open");
        Replicator t1 = sim.Replicators.Last();
        Inventory inv = sim.Player.Inventory;
        Vitals v = sim.Player.Vitals;

        (double organics, double energy) Feed(double hunger)
        {
            v.Food = max - hunger;
            inv.Organics = 100;
            double fuel = t1.Energy.Available;
            sim.Feed(t1);
            return (100 - inv.Organics, fuel - t1.Energy.Available);
        }
        var small = Feed(100);
        bool full = v.Food == max;
        var big = Feed(300);
        Check(full && v.Food == max && Math.Abs(big.organics - 3 * small.organics) < 1e-9 && Math.Abs(big.energy - 3 * small.energy) < 1e-9,
            $"feeding to full costs in proportion to hunger: 100 food = {small.organics:F1} kg + {small.energy:F0} energy, 300 = {big.organics:F1} kg + {big.energy:F0}");

        var quotes = d.Food.Replicators.OrderBy(t => t.Tier).Select(t => (t, q: new Replicator { Tier = t }.Quote(300))).ToList();
        bool better = quotes.Zip(quotes.Skip(1), (a, b) => b.q.Organics < a.q.Organics && b.q.Energy < a.q.Energy).All(x => x);
        Check(better, "higher tiers are more efficient: 300 food costs " +
            string.Join(", ", quotes.Select(x => $"T{x.t.Tier} {x.q.Organics:F2} kg / {x.q.Energy:F0} energy")));

        v.Food = max - 300;
        inv.Organics = 2;
        sim.Feed(t1);
        double expected = max - 300 + 2 * t1.Tier.NourishmentPerOrganic;
        Check(Math.Abs(v.Food - expected) < 1e-9 && inv.Organics == 0, $"short of organics it feeds as far as they go ({v.Food:F0} of {max:F0})");

        Replicator galley = sim.Replicators.First(x => x.Tier.Tier == 2);
        v.Food = max - 160;
        inv.Organics = 5;
        double stored = sim.BaseStorage.Organics;
        sim.Feed(galley);
        Check(v.Food == max && inv.Organics == 5 && sim.BaseStorage.Organics < stored,
            "the galley replicator feeds from the shelter's storage first");
    }

    void Deploying()
    {
        SurvivalData d = Fresh();
        var sim = new SurvivalSim(d, 1);
        Territory land = d.Planet.Territories.First();
        int kits = sim.Player.Inventory.Kits.Count, replicators = sim.Replicators.Count;
        sim.Player.Position = land.Center;
        Outcome refused = sim.DeployOrPack(new Pos(land.Center.X + 1.5, 0, land.Center.Z));
        Check(!refused.Ok && refused.Message.Contains(land.Name) && sim.Player.Inventory.Kits.Count == kits && sim.Replicators.Count == replicators,
            $"T1 can't deploy in faction territory: \"{refused.Message}\"");

        Pos start = d.Planet.Start.Position;
        sim.Player.Position = start;
        Check(sim.DeployOrPack(new Pos(start.X + 2, 0, start.Z)).Ok, "outside the ring it sets down");
        Check(!Deployment.Check(d.Food.Tier(2), start, sim.Planet).Ok, "a galley replicator can't be carried about and set down");

        sim.Player.Inventory.Kits.Clear();
        sim.Player.Position = new Pos(start.X + 20, 0, start.Z);
        Outcome noOre = sim.DeployOrPack(new Pos(start.X + 22, 0, start.Z));
        sim.AddOre(sim.SurvivalTier.BuildOre);
        Outcome built = sim.DeployOrPack(new Pos(start.X + 22, 0, start.Z));
        Check(!noOre.Ok && built.Ok && sim.Player.Inventory.Ore == 0, $"with no kit, B builds one from ore: \"{built.Message}\"");
    }

    void FoodDecay()
    {
        SurvivalData d = Fresh();
        FoodCatalog cat = d.Food;
        FoodKind paste = cat.Food("ration-paste"), meal = cat.Food("meal"), haul = cat.Food("long-haul-ration");
        Check(paste.ShelfLifeHours < meal.ShelfLifeHours && haul.ShelfLifeHours == null,
            $"shelf life: T1 {paste.ShelfLifeHours} h, T2 {meal.ShelfLifeHours} h, T3 never");

        var sim = new SurvivalSim(d, 1);
        Pos start = d.Planet.Start.Position;
        sim.Player.Position = start;
        sim.DeployOrPack(new Pos(start.X + 2, 0, start.Z));
        bool made = sim.Craft(sim.Replicators.Last(), 1).Ok
            & sim.Craft(sim.Replicators.First(x => x.Tier.Tier == 2), 2).Ok
            & sim.Craft(sim.Replicators.First(x => x.Tier.Tier == 3), 3).Ok
            & !sim.Craft(sim.Replicators.First(x => x.Tier.Tier == 2), 3).Ok;
        Check(made, "each replicator makes food up to its own tier (a galley can't make long-haul rations)");

        double Freshness(string kind) =>
            cat.NourishmentOf(sim.Player.Inventory.Food.First(f => f.Kind == kind), sim.Time) / cat.Food(kind).Nourishment;
        string Line() => $"T1 {Freshness("ration-paste"):P0}, T2 {Freshness("meal"):P0}, T3 {Freshness("long-haul-ration"):P0}";

        sim.AdvanceWorld(3600);
        Check(Freshness("ration-paste") < 0.7 && Freshness("meal") > 0.98 && Freshness("long-haul-ration") == 1, $"after 1 h: {Line()}");
        sim.AdvanceWorld(5 * 3600);
        Check(Freshness("ration-paste") == cat.MinFreshness && Freshness("meal") is > 0.9 and < 1 && Freshness("long-haul-ration") == 1,
            $"after 6 h: {Line()}");
        sim.AdvanceWorld(994 * 3600);
        Check(Freshness("ration-paste") == cat.MinFreshness && Freshness("meal") == cat.MinFreshness && Freshness("long-haul-ration") == 1
            && sim.Player.Inventory.Food.Count(f => f.Kind != "ration-pack") == 3,
            $"after 1,000 h: {Line()}, and all three still in the pack");
    }

    void HealingTools()
    {
        var sim = new SurvivalSim(Fresh(), 1);
        Vitals v = sim.Player.Vitals;
        VitalsRules r = sim.Data.Vitals;
        int medkits = sim.Player.Inventory.Medkits;
        v.Health = 100;
        v.Radiation = 6;
        v.BodyTemperature = 34;
        sim.UseMedkit();
        Check(v.Health == 100 + r.Healing.MedkitHealth && v.Health < r.Health.Max && sim.Player.Inventory.Medkits == medkits - 1,
            $"a medkit restores part of your health (100 -> {v.Health:F0} of {r.Health.Max:F0})");
        Outcome booth = sim.UseBooth();
        Check(booth.Ok && v.Health == r.Health.Max && v.Radiation == 0 && v.BodyTemperature == r.Temperature.Normal,
            "the healing booth restores full health and clears radiation and temperature");
    }

    // ---- Nothing lingers -----------------------------------------------------------------

    void NoLingeringConditions()
    {
        // The banned words are stored backwards, so this file never contains them itself.
        string[] banned = new[] { "tnemlia", "rujni", "rutcarf", "dnuow", "deelb", "nrub", "nosiop", "nixot", "tisarap",
                                        "tcefni", "saesid", "ssenlli", "ssenkcis", "suriv", "aesuan" }
            .Select(w => new string(w.Reverse().ToArray())).ToArray();

        string root = ProjectSettings.GlobalizePath("res://");
        var hits = new List<string>();
        int files = 0;
        foreach (string dir in new[] { "src", "tests", "data" })
            foreach (string path in Directory.EnumerateFiles(Path.Combine(root, dir), "*.*", SearchOption.AllDirectories)
                         .Where(p => p.EndsWith(".cs") || p.EndsWith(".json") || p.EndsWith(".tscn")))
            {
                files++;
                string text = File.ReadAllText(path).ToLowerInvariant();
                hits.AddRange(banned.Where(text.Contains).Select(w => $"{Path.GetFileName(path)}: {w}"));
            }

        foreach (Type type in Assembly.GetExecutingAssembly().GetTypes().Where(t => t.Namespace?.StartsWith("SurvivalVitals") == true))
        {
            var names = new List<string> { type.Name };
            names.AddRange(type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name));
            if (type.IsEnum) names.AddRange(Enum.GetNames(type));
            hits.AddRange(names.SelectMany(n => banned.Where(n.ToLowerInvariant().Contains).Select(w => $"{type.Name}.{n}: {w}")));
        }
        Check(files > 10 && hits.Count == 0,
            hits.Count == 0 ? $"no lingering conditions anywhere: {files} code and data files and every type, member and enum value are free of {banned.Length} banned words"
                            : "banned words found: " + string.Join(", ", hits.Distinct()));
        string[] kinds = Enum.GetNames(typeof(DamageKind));
        string[] allowed = { "Hit", "Fall", "Starvation", "Suffocation", "Cold", "Heat", "Radiation", "Lava", "Debug" };
        Check(kinds.All(allowed.Contains), "harm is only health loss, from: " + string.Join(", ", kinds));
    }

    // ---- Death and respawn ---------------------------------------------------------------

    void DeathDrops()
    {
        foreach (DropRule rule in Enum.GetValues<DropRule>())
        {
            SurvivalData d = Fresh();
            var sim = new SurvivalSim(d, 1) { DropRule = rule };
            Inventory inv = sim.Player.Inventory;
            sim.EquipBooster("insulation");
            inv.Food.Add(new FoodItem { Kind = "ration-paste", MadeAt = 0 });
            inv.Ore = 4;
            string carried = inv.Fingerprint();
            int items = inv.ItemCount;
            sim.Player.Position = d.Planet.Zone("frozen-valley").Center;
            sim.Kill();
            Backpack pack = sim.LastBackpack;

            bool dropped = rule switch
            {
                DropRule.Everything => pack != null && pack.Contents.ItemCount == items + 2 && pack.Contents.Suits.Any(s => s.Kind == "light")
                    && pack.Contents.Boosters.Contains("insulation") && inv.IsEmpty && sim.Player.Gear.Suit == null,
                DropRule.Inventory => pack != null && pack.Contents.Fingerprint() == carried && inv.IsEmpty
                    && sim.Player.Gear.Suit?.Kind == "light" && sim.Player.Gear.Boosters.Contains("insulation"),
                _ => pack == null && sim.World.Backpacks.Count == 0 && inv.Fingerprint() == carried,
            };
            Check(dropped && sim.Player.HasSurvivalTool,
                $"drop setting '{rule}': backpack holds {pack?.Contents.ItemCount ?? 0} items, survival tool kept");
            if (pack == null) continue;

            string contents = pack.Contents.Fingerprint();
            Pos where = pack.Position;
            sim.Respawn(RespawnChoice.NearestTent);
            sim.AdvanceWorld(1000 * 3600);
            Backpack still = sim.World.Backpacks.FirstOrDefault(b => b.Id == pack.Id);
            Check(still != null && still.OnMap && still.Position == where && still.Contents.Fingerprint() == contents,
                $"'{rule}': the backpack is still there and on the map after 1,000 in-game hours");
            Outcome back = sim.Recover(still);
            FoodItem paste = inv.Food.FirstOrDefault(f => f.Kind == "ration-paste");
            Check(back.Ok && sim.World.Backpacks.Count == 0 && paste != null && d.Food.NourishmentOf(paste, sim.Time) > 0,
                $"'{rule}': {back.Message}");
        }
    }

    void Respawning()
    {
        var first = new SurvivalSim(Fresh(), 3);
        first.Kill();
        Check(!first.RespawnOptions().First(o => o.Choice == RespawnChoice.Home).Available,
            "no home is offered until a clone chamber is flagged");

        foreach (RespawnChoice choice in Enum.GetValues<RespawnChoice>())
        {
            SurvivalData d = Fresh();
            var sim = new SurvivalSim(d, 3);
            VitalsRules r = d.Vitals;
            sim.SetHome(sim.RespawnPoints.First(p => p.Kind == RespawnKind.CloneChamber));
            Pos start = d.Planet.Start.Position;
            sim.Player.Position = start;
            sim.DeployOrPack(new Pos(start.X + 2, 0, start.Z));
            string progress = sim.Player.Progress.Fingerprint();
            int pods = sim.Pods.Count, structures = sim.Replicators.Count;

            sim.Player.Vitals.Food = 0;
            sim.Player.Position = d.Planet.Zone("radiation-crater").Center;
            sim.Kill();
            RespawnOption option = sim.RespawnOptions().First(o => o.Choice == choice);
            Outcome back = sim.Respawn(choice);
            Vitals v = sim.Player.Vitals;
            Pos expected = choice == RespawnChoice.FreshStart
                ? new Pos(sim.Pods.Last().Position.X + 2.5, 0, sim.Pods.Last().Position.Z + 2.5)
                : option.Point.Stand;
            bool ok = back.Ok && !sim.Player.Dead && v.Health == r.Health.Max && v.Food >= r.Death.RespawnFoodFloor * r.Food.Max
                && sim.Player.Position == expected
                && Math.Abs(sim.Player.Gear.Suit.Durability - (1 - r.Death.ArmorDurabilityLoss)) < 1e-12;
            if (choice == RespawnChoice.FreshStart)
                ok &= sim.Pods.Count == pods + 1 && sim.Pods.Last().Rations == d.Planet.EscapePod.Rations
                    && sim.Player.Progress.Fingerprint() == progress && sim.Replicators.Count == structures;
            Check(ok, $"respawn '{option.Label}': {back.Message} Full health, food {v.Food:F0}, armor at {sim.Player.Gear.Suit.Durability:P0}");
        }
    }

    void DamageIsNotCapped()
    {
        SurvivalData d = Fresh();
        VitalsRules r = d.Vitals;
        var body = new Vitals(r, 0);
        double lost = body.Damage(r.Health.Max * 0.9, DamageKind.Hit, new Modifiers());
        Check(lost == r.Health.Max * 0.9, $"vitals don't cap a hit ({lost:F0} of {r.Health.Max:F0}); fairness lives in combat code");
        var armored = new Vitals(r, 0);
        double softened = armored.Damage(100, DamageKind.Hit, GearOf(d, Suit("heavy", "armor")).Mods);
        Check(Math.Abs(softened - 58) < 1e-9, $"armor softens hits: a heavy suit and armor booster take 100 down to {softened:F0}");
    }

    // ---- Determinism ---------------------------------------------------------------------

    void Determinism()
    {
        string a = Scenario(42), b = Scenario(42), c = Scenario(7);
        Check(a == b, $"the same seed and the same inputs give exactly the same result ({a.Length:N0} characters of trace, bit for bit)");
        Check(a != c, "a different seed gives different weather, and so a different result");
    }

    /// <summary>
    /// A scripted 25-minute outing: mine, feed, cross the valley, crater and sinkhole, fall, die,
    /// respawn. Returns every vital every 10 s plus the final state, as exact numbers.
    /// </summary>
    static string Scenario(int seed)
    {
        var trace = new System.Text.StringBuilder();
        SurvivalData d = DataFiles.Load();
        var sim = new SurvivalSim(d, seed);
        Pos start = d.Planet.Start.Position, valley = d.Planet.Zone("frozen-valley").Center;
        Pos crater = d.Planet.Zone("radiation-crater").Center, sink = d.Planet.Zone("airless-sinkhole").Center;
        Pos bush = d.Planet.Bushes[0];
        var legs = new (double seconds, Pos to, Pace pace, bool mining)[]
        {
            (30, start, Pace.Idle, true),
            (20, bush, Pace.Walk, false),
            (70, valley, Pace.Run, false),
            (120, valley, Pace.Walk, false),
            (60, crater, Pace.Sprint, false),
            (60, crater, Pace.Idle, false),
            (90, sink, Pace.Run, false),
            (200, sink, Pace.Walk, false),
            (300, start, Pace.Walk, false),
            (600, start, Pace.Idle, false),
        };
        Pos from = sim.Player.Position;
        int tick = 0, leg = 0;
        foreach (var (seconds, to, pace, mining) in legs)
        {
            int steps = (int)(seconds * 60);
            for (int i = 1; i <= steps; i++, tick++)
            {
                double f = (double)i / steps;
                sim.Player.Position = new Pos(from.X + (to.X - from.X) * f, 0, from.Z + (to.Z - from.Z) * f);
                sim.Tick(Dt, new Activity(pace, mining));
                if (mining && tick % 90 == 0) sim.AddOre(1);
                if (tick % 600 == 0)
                {
                    Vitals v = sim.Player.Vitals;
                    trace.Append(FormattableString.Invariant($"{v.Health:R},{v.Food:R},{v.Stamina:R},{v.Oxygen:R},{v.BodyTemperature:R},{v.Radiation:R};"));
                }
                if (sim.Player.Dead)
                {
                    sim.Respawn(RespawnChoice.NearestTent);
                    from = sim.Player.Position;
                }
            }
            from = to;
            switch (leg++)
            {
                case 0:
                    sim.DeployOrPack(new Pos(start.X + 2, 0, start.Z));
                    sim.Feed(sim.Replicators.Last());
                    sim.Craft(sim.Replicators.Last(), 1);
                    break;
                case 1:
                    sim.PickBush(sim.Bushes[0]);
                    sim.Eat("fruit");
                    break;
                case 5:
                    sim.Land(16);
                    sim.UseMedkit();
                    break;
            }
        }
        return trace.Append(sim.Digest()).ToString();
    }

    // ---- Tuning: the condensed early game ------------------------------------------------

    /// <summary>
    /// 90 minutes of an experienced player's climb to a first vessel: every minute 24 s walking,
    /// 18 s running, 6 s sprinting and 12 s mining, over days, nights and weather. They eat only
    /// when the visor flashes, by feeding to full at the survival replicator they carry.
    /// </summary>
    void EarlyGameIsLightPressure()
    {
        SurvivalData d = Fresh();
        var sim = new SurvivalSim(d, 5);
        Pos start = d.Planet.Start.Position;
        sim.Player.Position = start;
        sim.DeployOrPack(new Pos(start.X + 2, 0, start.Z));
        Replicator t1 = sim.Replicators.Last();
        const double step = 0.1;
        int feeds = 0;
        double organicsUsed = 0, fuelUsed = 0, lost = 0;
        var minute = new (double seconds, Pace pace, bool mining)[] { (24, Pace.Walk, false), (18, Pace.Run, false), (6, Pace.Sprint, false), (12, Pace.Idle, true) };
        for (int m = 0; m < 90; m++)
            foreach (var (seconds, pace, mining) in minute)
                for (int i = 0; i < seconds / step; i++)
                {
                    lost += sim.Tick(step, new Activity(pace, mining)).Damage;
                    if ((sim.Last.Warnings & Alert.Food) == 0) continue;
                    sim.Player.Inventory.Organics = 100;
                    double fuel = t1.Energy.Available;
                    sim.Feed(t1);
                    organicsUsed += 100 - sim.Player.Inventory.Organics;
                    fuelUsed += fuel - t1.Energy.Available;
                    feeds++;
                }
        double startKit = d.Planet.Start.Inventory.Organics, bush = d.Food.Forage.OrganicsPerBush;
        int bushes = (int)Math.Ceiling(Math.Max(0, organicsUsed - startKit) / bush);
        Check(feeds <= 2 && lost == 0 && fuelUsed < t1.Tier.Fuel / 4,
            $"90 min of mixed activity: fed {feeds} times ({organicsUsed:F1} kg organics: the starting kit plus {bushes} bushes; {fuelUsed:F0} of {t1.Tier.Fuel:F0} fuel), never harmed");
    }

    // ---- Scene stage: the playable scene with real key presses ---------------------------

    Main _main;
    int _tick, _stage, _count, _flashedAt;

    void StartSceneStage()
    {
        GD.Print("-- SceneWithRealKeys");
        ProcessPhysicsPriority = 100; // check after Main and the player have moved
        _main = new Main();
        AddChild(_main);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_main == null) return;
        _tick++;
        try { SceneStep(); }
        catch (Exception e)
        {
            Fail($"scene stage threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            Finish();
        }
    }

    void SceneStep()
    {
        SurvivalSim sim = _main.Sim;
        PlanetData planet = sim.Data.Planet;
        Inventory inv = sim.Player.Inventory;
        switch (_stage)
        {
            case 0: // stand at the crash site, then try to set the replicator down on faction land
                if (_tick == 30)
                {
                    Check(_main.Player.IsOnFloor() && !sim.Player.Dead && sim.Last.Harm == Alert.None,
                        "the scene builds and the player stands unharmed at the crash site");
                    Territory land = planet.Territories[0];
                    _main.DebugPlace(land.Center.X + 4, land.Center.Z, 90f);
                    _count = sim.Replicators.Count;
                }
                if (_tick == 32) Tap(Key.B);
                if (_tick == 36)
                {
                    Check(sim.Replicators.Count == _count && LastMessage().StartsWith("Can't set down"),
                        $"B inside faction territory is refused: \"{LastMessage()}\"");
                    _main.DebugPlace(planet.Start.Position.X, planet.Start.Position.Z, 0f);
                    Tap(Key.B);
                }
                if (_tick == 40)
                {
                    bool node = _main.GetChildren().Any(n => n.Name.ToString() == "Replicator_survival");
                    Check(sim.Replicators.Count == _count + 1 && node, $"B outside it sets the survival replicator down: \"{LastMessage()}\"");
                    sim.Player.Vitals.Food = 300;
                    Tap(Key.F);
                }
                if (_tick == 44)
                {
                    Check(sim.Player.Vitals.Food > sim.Data.Vitals.Food.Max - 0.1, $"F feeds to full: \"{LastMessage()}\"");
                    Tap(Key.Key1);
                }
                if (_tick == 48)
                {
                    Check(inv.CountFood("ration-paste") == 1, $"1 makes ration paste to carry: \"{LastMessage()}\"");
                    _main.DebugPlace(planet.EscapePod.Position.X + 2.5, planet.EscapePod.Position.Z + 1.5, 0f);
                    Tap(Key.E);
                }
                if (_tick == 52)
                {
                    Check(inv.CountFood("ration-pack") == planet.EscapePod.Rations, $"E at the wrecked pod: \"{LastMessage()}\"");
                    Next();
                }
                break;

            case 1: // mine a rock: ore comes in and food drains at the mining rate
                if (_tick == 1)
                {
                    double[] rock = planet.Rocks[0];
                    _main.DebugPlace(rock[0], rock[1] + 3.0, 0f, -25f);
                    _main.DebugMining = true;
                    _count = inv.Ore;
                }
                if (_tick == 200)
                {
                    _main.DebugMining = false;
                    Check(_main.Mining && inv.Ore >= _count + 2, $"holding the mouse on a rock mines it: ore {_count} -> {inv.Ore}");
                    Next();
                }
                break;

            case 2: // walk into the airless sinkhole with the helmet open: it seals itself
                if (_tick == 1)
                {
                    sim.Player.Gear.HelmetOn = false;
                    Pos sink = planet.Zone("airless-sinkhole").Center;
                    _main.DebugPlace(sink.X, sink.Z, 0f);
                }
                if (_tick == 30)
                {
                    Check(sim.Player.Gear.HelmetOn && sim.Player.Vitals.Oxygen < sim.OxygenCapacity && sim.Last.Harm == Alert.None,
                        $"in the sinkhole the helmet seals by itself and suit oxygen drains ({sim.Player.Vitals.Oxygen:F1})");
                    Tap(Key.H);
                }
                if (_tick == 34)
                {
                    Check(!sim.Player.Gear.HelmetOn && (sim.Last.Warnings & Alert.Breath) != 0 && sim.Last.Harm == Alert.None,
                        "H opens it again: the visor flashes HOLD BREATH before any harm");
                    Tap(Key.H);
                    _main.DebugPlace(planet.Start.Position.X, planet.Start.Position.Z, 0f);
                    Tap(Key.K);
                }
                if (_tick == 40)
                {
                    bool beam = _main.GetChildren().Any(n => n.Name.ToString().StartsWith("Backpack"));
                    Check(sim.Player.Dead && _main.DeathScreen.Visible && sim.World.Backpacks.Count == 1 && beam,
                        "K kills: the death screen opens and the backpack's beam stands where you fell");
                    Tap(Key.Key3);
                }
                if (_tick == 44)
                {
                    RespawnPoint tent = sim.RespawnPoints.First(p => p.Kind == RespawnKind.SurvivalTent);
                    Check(!sim.Player.Dead && !_main.DeathScreen.Visible && _main.Player.GlobalPosition.DistanceTo(_main.GroundAt(tent.Stand)) < 0.5f
                        && sim.Player.Vitals.Health == sim.Data.Vitals.Health.Max && sim.World.Backpacks.Count == 1,
                        "3 respawns at the survival tent at full health; the backpack waits");
                    Next();
                }
                break;

            case 3: // drop the real player from 25 m: the visor flashes before the landing hurts
                if (_tick == 1)
                {
                    _main.Player.Teleport(_main.GroundAt(planet.Start.Position) + Vector3.Up * 25f, 0f);
                    _flashedAt = -1;
                }
                if (_flashedAt < 0 && (sim.Last.Warnings & Alert.Fall) != 0) _flashedAt = _tick;
                if (sim.Player.Vitals.Health < sim.Data.Vitals.Health.Max || _tick > 300)
                {
                    double lost = sim.Data.Vitals.Health.Max - sim.Player.Vitals.Health;
                    Check(_flashedAt > 0 && lost > 0 && _flashedAt < _tick,
                        $"a 25 m drop in the scene: IMPACT flashes {(_tick - _flashedAt) / 60f:F1} s before the landing costs {lost:F0} health");
                    Next();
                }
                break;

            default:
                Finish();
                break;
        }
    }

    string LastMessage() => _main.RecentMessages().LastOrDefault().text ?? "";

    void Next()
    {
        _stage++;
        _tick = 0;
    }

    /// <summary>Presses and releases a key, as if from the keyboard.</summary>
    static void Tap(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = false });
    }

    void Finish()
    {
        _main = null;
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---- Helpers -------------------------------------------------------------------------

    static SurvivalData Fresh() => DataFiles.Load();

    /// <summary>Real data with the weather held clear, so a check never depends on a cold snap.</summary>
    static SurvivalData Calm()
    {
        SurvivalData d = DataFiles.Load();
        d.Planet.Weather.Kinds = new List<WeatherKind> { new() { Name = "Clear", Weight = 1 } };
        return d;
    }

    static Loadout Suit(string kind, params string[] boosters)
    {
        var loadout = new Loadout { Suit = new SuitItem { Kind = kind } };
        loadout.Boosters.AddRange(boosters);
        return loadout;
    }

    static GearState GearOf(SurvivalData d, Loadout l) =>
        new(l.HasSuit, l.HelmetOn, l.OxygenCapacity(d.Armor, d.Vitals.Oxygen), l.Total(d.Armor));

    static double TimeAtHour(PlanetData p, double hour) => (hour - p.StartHour + 24) % 24 / 24 * p.DayLengthSeconds;

    /// <summary>Runs a hazard until it costs health: when did these flags first flash, and first hurt?</summary>
    static (double warn, double harm) FirstWarnAndHarm(Vitals body, Func<double, Exposure> env, GearState gear, Alert flags, double maxSeconds)
    {
        double warn = double.NaN;
        for (int i = 0; i * Dt < maxSeconds; i++)
        {
            double t = i * Dt;
            TickReport rep = body.Tick(Dt, new Activity(Pace.Walk), env(t), gear);
            if (double.IsNaN(warn) && (rep.Warnings & flags) != 0) warn = t;
            if (rep.Damage > 0 && (rep.Harm & flags) != 0) return (warn, t);
        }
        return (warn, double.NaN);
    }

    void Section(Action body)
    {
        GD.Print($"-- {body.Method.Name}");
        try { body(); }
        catch (Exception e) { Fail($"{body.Method.Name} threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
    }

    void Check(bool ok, string what)
    {
        if (ok) GD.Print($"PASS  {what}");
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
