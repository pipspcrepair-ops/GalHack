using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PlanetSpec = Planets.Gen.PlanetSpec;
using Archetype = Planets.Gen.Archetype;
using Modifier = Planets.Gen.Modifier;

namespace Wildlife;

/// <summary>
/// Headless test of the bestiary's rules, on fresh worlds every run:
///   godot --headless --path prototypes/wildlife res://tests/BestiaryTest.tscn
/// Nothing is designed here: each check rolls many worlds and asks whether the rules produced
/// what they should. A blind thing with big ears in the dark, wool on the ice, nothing that
/// breathes in vacuum. Exits 0 if every check passes.
/// </summary>
public partial class BestiaryTest : Node
{
    WildlifeData _data;
    int _failures, _checks;
    readonly Rng _fresh = new((ulong)DateTime.UtcNow.Ticks);

    public override void _Ready()
    {
        try
        {
            _data = DataFiles.LoadWildlife();
            GD.Print($"(worlds rolled from seed {_fresh.NextU64()})");
            SameWorldSameLife();
            BodiesFitTheClimate();
            VacuumHasNothingThatBreathes();
            DarkMakesBlindEars();
            PeopleVary();
            LooksFollowJobAndClimate();
            Walking();
            AllRigsRun();
            MutationIsALayer();
            SpitIsDodgeable();
            SensesFollowLight();
            ScentFollowsTheWind();
            VoicesAreTheirOwn();
            HeavyThingsSoundHeavy();
            StatesHold();
            YoungStayInTheMiddle();
            QuietInsidePlaces();
            BreakersBreakWeakBlocks();
            NobodyReactsAtOnce();
            EveryBuildWorks();
            TempersByTech();
            TwentyPeopleAreCheap();
            LooksClusterBySettlement();
            HusksSpreadFromTheCore();
            HusksAreTheirOwnPeople();
            SeizedPeopleTurn();
            ThePlayerIsNeverInfected();
            TheCooldownHolds();
            DensHideFromOutside();
            MassesHoldBodyParts();
            CarriersLookNormalUntilAlone();
            CarriersTurnPastTheShare();
            WrongnessRisesWithHusks();
            HollowsAreLesserAndScarce();
            NothingLivesInsideWalls();
            HuskDensHaveNoEggs();
            ShortHusksNeverSeize();
            TwentyHusksAreCheap();
        }
        catch (Exception e)
        {
            Fail($"crashed: {e}");
        }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_checks} checks)" : $"{_failures} OF {_checks} CHECK(S) FAILED");
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        _checks++;
        if (ok) GD.Print($"  ok    {what}");
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.Print($"  FAIL  {what}");
    }

    ulong Seed() => _fresh.NextU64();

    WorldConditions World(float tempC, bool vacuum = false, float oxygen = 21f, float mutagen = 0f, float alien = 0.25f) => new()
    {
        Seed = Seed(), Alienness = alien, TempC = tempC, Vacuum = vacuum, Oxygen = vacuum ? 0f : oxygen, Mutagen = mutagen,
    };

    void SameWorldSameLife()
    {
        ulong seed = Seed();
        string Print(WorldLife l) => string.Join("\n", l.Fauna.Species.Select(s => $"{s.Id} {s.CommonName} {s.Coat} {s.Body.Width} {s.Senses}"))
            + l.People.Person(5, PersonRole.Trader).Figure.Height;
        var w = new WorldConditions { Seed = seed, Alienness = 0.3f, TempC = -20f };
        var v = new WorldConditions { Seed = seed + 1, Alienness = 0.3f, TempC = -20f };
        Check(Print(Bestiary.Roll(_data, w)) == Print(Bestiary.Roll(_data, w)) && Print(Bestiary.Roll(_data, w)) != Print(Bestiary.Roll(_data, v)), "the same world gives the same life, another world another");
    }

    void BodiesFitTheClimate()
    {
        var ice = new List<Species>();
        var hot = new List<Species>();
        for (int i = 0; i < 40; i++)
        {
            ice.AddRange(Bestiary.Roll(_data, World(-25f)).Fauna.Species);
            hot.AddRange(Bestiary.Roll(_data, World(50f)).Fauna.Species);
        }
        ice = ice.Where(s => !s.Mechanical).ToList();
        hot = hot.Where(s => !s.Mechanical).ToList();
        Check(!ice.Any(s => s.Rig is RigKind.Chain or RigKind.Swarm), $"no snakes, centipedes or insect clouds in {ice.Count} ice species");
        var warm = ice.Where(s => s.Coat is Coat.Fur or Coat.Wool or Coat.Blubber).ToList();
        Check(warm.Count > ice.Count * 0.75f && !ice.Any(s => s.Coat is Coat.Scale or Coat.ThinSkin), $"{warm.Count} of {ice.Count} ice species are shaggy, woolly or fat-layered, none scaled or thin-skinned");
        var dry = hot.Where(s => s.Coat is Coat.Scale or Coat.ThinSkin).ToList();
        Check(dry.Count > hot.Count * 0.6f && !hot.Any(s => s.Coat is Coat.Fur or Coat.Wool or Coat.Blubber), $"{dry.Count} of {hot.Count} hot-world species have scale or thin skin, none fur or fat");
        float Stock(IEnumerable<Species> l) => l.Where(s => s.Rig == RigKind.Legged).Select(s => s.Body.Width / s.Body.LegLength).DefaultIfEmpty(0f).Average();
        Check(Stock(ice) > Stock(hot) * 1.25f, $"cold bodies are stocky and short-legged, hot ones long ({Stock(ice):F2} vs {Stock(hot):F2} width to leg)");
        Check(hot.Average(s => s.EarSize) > ice.Average(s => s.EarSize) + 0.2f, $"hot-world bodies shed heat through big ears ({hot.Average(s => s.EarSize):F2} vs {ice.Average(s => s.EarSize):F2})");
    }

    void VacuumHasNothingThatBreathes()
    {
        int alive = 0, machines = 0, worlds = 400;
        bool clean = true;
        for (int i = 0; i < worlds; i++)
        {
            foreach (Species s in Bestiary.Roll(_data, World(-60f, vacuum: true)).Fauna.Species)
            {
                if (s.Mechanical) machines++;
                else if (s.Hardened) alive++;
                else clean = false;
            }
        }
        Check(clean && machines >= worlds * 2, $"vacuum worlds hold only machines and sealed things ({machines} machines)");
        Check(alive > 0 && alive < worlds * 0.25f, $"sealed animals are super rare: on {alive} of {worlds} worlds");
        var spec = new PlanetSpec(Seed(), Archetype.Arid, 0, Modifier.Cratered);
        Check(WorldConditions.Of(spec).Vacuum && !WorldConditions.Of(new PlanetSpec(Seed(), Archetype.Temperate, 0, Modifier.None)).Vacuum, "cratered rock has no air; a temperate world has");
    }

    void DarkMakesBlindEars()
    {
        int blind = 0, big = 0, echo = 0, lit = 0, litBig = 0;
        for (int i = 0; i < 30; i++)
        {
            var deep = new DeepFauna(_data, Seed(), 0.4f);
            foreach (DeepRole role in new[] { DeepRole.Lurker, DeepRole.Prowler, DeepRole.Ambusher })
            {
                Species far = deep.For(role, PoiTheme.Natural, _data.Deep.Depth.BlindFrom + 1);
                if (far.Blind)
                {
                    blind++;
                    if (far.EarSize >= 0.4f && far.Senses.Hearing >= 1.4f && far.Senses.Sight == 0f) big++;
                    if (far.Senses.Echo) echo++;
                }
                Species near = deep.For(role, PoiTheme.Natural, 0);
                lit++;
                if (near.EarSize >= 0.4f) litBig++;
            }
        }
        Check(blind > 0 && big == blind, $"dark caves make blind things with big ears: {big} of {blind} far down, nobody designed them");
        Check(echo > blind / 3, $"some of them click to find their way ({echo} of {blind})");
        Check(litBig < big, $"near the way in eyes still work, and fewer have those ears ({litBig} of {lit})");
    }

    void PeopleVary()
    {
        var life = Bestiary.Roll(_data, World(15f));
        var folk = Enumerable.Range(0, 300).Select(i => life.People.Person(Seed(), PersonRole.Worker)).ToList();
        float men = folk.Where(s => s.Figure.Sex == Sex.Male).Average(s => s.Figure.Height), women = folk.Where(s => s.Figure.Sex == Sex.Female).Average(s => s.Figure.Height);
        Check(men > women + 0.04f * life.People.Folk.Dimorphism, $"men and women differ in height ({men:F2} and {women:F2} m)");
        float wide = folk.Where(s => s.Figure.Sex == Sex.Male).Average(s => s.Figure.ShoulderW / s.Figure.HipW), broad = folk.Where(s => s.Figure.Sex == Sex.Female).Average(s => s.Figure.ShoulderW / s.Figure.HipW);
        Check(wide > broad, $"and in shoulder to hip ({wide:F2} and {broad:F2})");
        Check(folk.Max(s => s.Figure.Height) - folk.Min(s => s.Figure.Height) > 0.25f && folk.Select(s => s.Figure.Build).Distinct().Count() > 150, "heights and builds spread");
        Check(life.People.Person(Seed(), PersonRole.Child).Figure.Height < 1.5f, "a child is a child");
        var e = life.People.Person(Seed(), PersonRole.Elder).Figure;
        Check(e.Age > 60f && e.Stoop > 0.2f, $"an elder is old and bent ({e.Age:F0}, stoop {e.Stoop:F2})");
        var looks = folk.Select(s => s.Figure.Height.ToString("F3") + s.Figure.Cloth + string.Join(",", s.Figure.Wear)).Distinct().Count();
        Check(looks > 290, $"{looks} of 300 people look different");
    }

    void LooksFollowJobAndClimate()
    {
        var mild = Bestiary.Roll(_data, World(15f)).People;
        int Count(PersonRole r, Occupancy o, PeopleGenerator g, Func<Figure, bool> test) => Enumerable.Range(0, 40).Count(i => test(g.Person(Seed(), r, o).Figure));
        Check(Count(PersonRole.Guard, Occupancy.Friendly, mild, f => f.Has("vest") && f.Prop == "rifle") > 30, "guards wear armour and carry rifles");
        Check(Count(PersonRole.Raider, Occupancy.Raider, mild, f => f.HairStyle == "mohawk" && f.Has("bandoleer") | f.Has("spike_pads")) > 30, "raiders have mohawks, spikes and bandoleers");
        Check(Count(PersonRole.Medic, Occupancy.Friendly, mild, f => f.Has("medic_band")) == 40, "medics carry their kit");
        Check(Count(PersonRole.Farmer, Occupancy.Friendly, mild, f => f.Has("straw_hat") | f.Has("wide_hat") | f.Prop == "hoe") > 30, "farmers have hats and hoes");
        Check(Count(PersonRole.Elder, Occupancy.Friendly, mild, f => f.Prop == "cane") > 18, "elders have sticks");
        Check(Count(PersonRole.Drunk, Occupancy.Friendly, mild, f => f.Stagger > 0.3f) == 40, "drunks stagger");
        Check(Count(PersonRole.Trader, Occupancy.Friendly, Bestiary.Roll(_data, World(-25f)).People, f => f.Dress == "furs") == 40, "on ice everyone is in furs");
        Check(Count(PersonRole.Trader, Occupancy.Friendly, Bestiary.Roll(_data, World(45f)).People, f => f.Dress == "wraps" && (f.Has("head_wrap") | f.Has("wide_hat"))) >= 30, "in the heat, wraps and something on the head");
        Check(Count(PersonRole.Trader, Occupancy.Friendly, Bestiary.Roll(_data, World(10f, oxygen: 9f)).People, f => f.Dress == "hazard" && f.Has("respirator")) == 40, "where the air is bad, masks");
        Check(Count(PersonRole.Raider, Occupancy.Raider, Bestiary.Roll(_data, World(-60f, vacuum: true)).People, f => f.Dress == "suit" && f.Has("suit_helmet") && f.Has("air_tank")) == 40, "on a vacuum world, even raiders are in suits");
    }

    void Walking()
    {
        var life = Bestiary.Roll(_data, World(15f));
        Species s = life.People.Person(Seed(), PersonRole.Worker);
        Figure f = s.Figure;
        var rig = new HumanRig(s);
        Vector3 feet = new(3f, 0f, -2f);
        float walked = 0f, worst = 0f, worstRun = 0f, off = 0f;
        var last = new[] { Vector3.Zero, Vector3.Zero };
        var was = new[] { false, false };
        bool finite = true;
        int steps0 = rig.Steps;
        for (int i = 0; i < 400; i++)
        {
            bool run = i >= 200;
            float speed = run ? 3.4f : 1.3f, dt = 1f / 60f;
            feet += Vector3.Forward * speed * dt;
            walked += speed * dt;
            rig.Drive(new Gesture { Feet = feet, Yaw = 0f, Speed = speed, Walked = walked, Clock = i * dt });
            for (int k = 0; k < 2; k++)
            {
                if (rig.FootPlanted(k))
                {
                    off = MathF.Max(off, rig.FootTarget(k).DistanceTo(rig.FootDrawn(k)));
                    if (was[k] && i % 200 != 0)
                    {
                        float slip = rig.FootTarget(k).DistanceTo(last[k]);
                        if (run) worstRun = MathF.Max(worstRun, slip); else worst = MathF.Max(worst, slip);
                    }
                }
                was[k] = rig.FootPlanted(k);
                last[k] = rig.FootTarget(k);
            }
            foreach (Transform3D t in rig.Pose) finite &= Geo.Finite(t);
        }
        Check(finite, "every pose is finite");
        Check(worst < 0.012f, $"a planted foot stays planted at a walk (slides {worst * 100f:F1} cm a frame at most)");
        Check(worstRun < 0.03f, $"and at a run ({worstRun * 100f:F1} cm)");
        Check(off < 0.06f, $"legs reach their feet ({off * 100f:F1} cm short at worst, the heel coming up at the end of a stride)");
        Check(rig.Steps - steps0 > 10, $"the walk takes steps ({rig.Steps - steps0})");
        foreach (float sit in new[] { 0f, 1f })
        foreach (float work in new[] { 0f, 1f })
        foreach (float aim in new[] { 0f, 1f })
        {
            rig.Drive(new Gesture { Feet = Vector3.Zero, Yaw = 1f, Sit = sit, Work = work, Aim = aim, Clock = 3f, Down = aim });
            foreach (Transform3D t in rig.Pose) finite &= Geo.Finite(t);
        }
        Check(finite, "sitting, bending, aiming and dying are finite too");
    }

    /// <summary>Everything a world can roll runs on its rig in the sim: people, machines, and what's left on a vacuum world.</summary>
    void AllRigsRun()
    {
        var terrain = new Terrain((x, z) => 0f, 200f);
        var sim = new WildSim(_data, terrain, new NavGrid(200f, 3f));
        var target = new DummyTarget { Position = new Vector3(4f, 0f, 4f) };
        sim.Targets.Add(target);
        var worlds = new[] { World(-20f), World(45f), World(15f, mutagen: 0.9f), World(-60f, vacuum: true) };
        var kinds = new List<Species>();
        foreach (WorldConditions w in worlds)
        {
            w.Coreward = w.Mutagen > 0f ? 1f : 0f;
            WorldLife life = Bestiary.Roll(_data, w);
            kinds.AddRange(life.Moving);
            kinds.Add(Robots.Roll(w, 0));
            kinds.Add(Robots.Roll(w, 1));
            kinds.Add(Robots.Roll(w, 2));
        }
        foreach (Species s in kinds.Where(s => s.Figure != null || s.Mechanical || s.Rig == RigKind.Legged).Take(24)) sim.Spawn(s, new Vector3(_fresh.Range(-30f, 30f), 0f, _fresh.Range(-30f, 30f)));
        bool finite = true;
        for (int i = 0; i < 600; i++)
        {
            sim.Step(1f / 30f);
            foreach (Creature c in sim.Creatures) foreach (Transform3D t in c.Rig.Pose) finite &= Geo.Finite(t);
        }
        Check(finite, $"{sim.Creatures.Count} walkers, machines, ferals and husks run in the sim for 20 s with every pose finite");
    }

    // ---- The Husks ----------------------------------------------------------------------------

    static IEnumerable<Species> HusksOf(WorldLife life) => life.Moving.Where(s => s.Husk).SelectMany(s => s.Hosts ?? new List<Species> { s });

    /// <summary>The Husks spread outward from the core: scarce on the rim, common coreward, and more of them there.</summary>
    void HusksSpreadFromTheCore()
    {
        int rim = 0, core = 0, worlds = 150;
        float rimScarcity = 0f, coreScarcity = 0f;
        for (int i = 0; i < worlds; i++)
        {
            var w = World(_fresh.Range(-10f, 35f));
            var c = World(_fresh.Range(-10f, 35f));
            c.Coreward = 1f;
            if (Bestiary.Roll(_data, w).Moving.FirstOrDefault(s => s.Husk) is { } a) { rim++; rimScarcity = a.Scarcity; }
            if (Bestiary.Roll(_data, c).Moving.FirstOrDefault(s => s.Husk) is { } b) { core++; coreScarcity = b.Scarcity; }
        }
        Check(rim < worlds * 0.35f && rim > 0, $"Husks walk {rim} of {worlds} rim worlds: there, but scarce");
        Check(core > worlds * 0.85f, $"and {core} of {worlds} worlds at the core");
        Check(coreScarcity > rimScarcity * 2f, $"with more of them coreward ({coreScarcity:F2} against {rimScarcity:F2} of an animal's groups)");
    }

    /// <summary>
    /// A Husk is a person of its world, taken: torn clothes of their own job, worms at every opening,
    /// one or two wrong shapes, a voice, no two alike, called what it is. And nothing fungal on a person.
    /// </summary>
    void HusksAreTheirOwnPeople()
    {
        var husks = new List<Species>();
        int worlds = 0;
        while (husks.Count < 60 && worlds < 200)
        {
            var w = World(_fresh.Range(-20f, 45f));
            w.Coreward = 1f;
            husks.AddRange(HusksOf(Bestiary.Roll(_data, w)));
            worlds++;
        }
        Check(husks.Count >= 60, $"{husks.Count} Husks from {worlds} core worlds");
        int Worms(Species s) => new HumanRig(s).Layout.Pieces.First(p => p.Name == "head").Prims.Count(m => m.Material == Mat.Worm);
        var worms = husks.Select(Worms).ToList();
        Check(worms.Min() >= 4 * 9, $"worms out of the mouth, nostrils, eyes and ears of every one ({worms.Min() / 4}-{worms.Max() / 4} worms each)");
        Check(husks.All(s => s.Figure.Torn && s.Figure.Prop == "" && !s.Figure.Has("suit_helmet")), "all in their own clothes, torn, hands empty, nothing over the face");
        var wrongs = husks.Select(s => System.Numerics.BitOperations.PopCount((uint)s.Figure.Wrongs)).ToList();
        Check(wrongs.All(n => n is 1 or 2), "each with one or two of the wrong shapes");
        foreach (Wrong k in new[] { Wrong.Jaw, Wrong.Arm, Wrong.Hunch, Wrong.Head })
            Check(husks.Count(s => s.Figure.Wrongs.HasFlag(k)) > husks.Count / 10, $"{husks.Count(s => s.Figure.Wrongs.HasFlag(k))} with {k.ToString().ToLowerInvariant()} wrong");
        Check(husks.Where(s => s.Figure.Wrongs.HasFlag(Wrong.Arm)).All(s => s.Figure.UpperArmOf(s.Figure.LongArm) > s.Figure.UpperArmOf(1 - s.Figure.LongArm) * 1.2f), "a long arm is a fifth longer than the other at least");
        Check(husks.Select(s => (s.Figure.Role, s.Figure.Dress)).Distinct().Count() >= 6, $"taken from {husks.Select(s => s.Figure.Role).Distinct().Count()} jobs and {husks.Select(s => s.Figure.Dress).Distinct().Count()} kinds of dress");
        var oneWorld = HusksOf(Bestiary.Roll(_data, new WorldConditions { Seed = Seed(), Coreward = 1f, Alienness = 0.2f })).ToList();
        Check(oneWorld.Count == 0 || oneWorld.Select(s => $"{s.Figure.Height:F3}{s.Figure.Cloth}{s.Figure.Wrongs}").Distinct().Count() == oneWorld.Count, $"a world's Husks are {oneWorld.Count} different people");
        Check(husks.All(s => s.CommonName == "husk" && s.Figure.Voice != null && s.Spit == SpitKind.Maggots && s.Attack.Style == AttackStyle.Seize), "called husks, murmuring, with maggots in the throat and the seize for an attack");
        Check(husks.All(s => !s.Corrupted), "and no person carries the fungus");
    }

    /// <summary>A Husk seizes a person and they turn; an animal dies of it; only people turn.</summary>
    void SeizedPeopleTurn()
    {
        var w = World(15f);
        w.Coreward = 1f;
        WorldLife life = Bestiary.Roll(_data, w);
        Species husk = TallHusk(life);
        int turned = 0, killed = 0, seized = 0, tries = 0, outOfReach = 0;
        for (int t = 0; t < 6; t++)
        {
            var sim = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
            sim.Seized += _ => seized++;
            sim.Turned += _ => turned++;
            bool person = t % 2 == 0;
            Species prey = person ? life.People.Person(Seed(), PersonRole.Farmer) : life.Moving.Where(s => !s.Mechanical && s.Figure == null && !s.Flies && s.Rig == RigKind.Legged).OrderBy(s => s.BodyHeight).First(); // one whose head it can reach
            if (!person && prey.BodyHeight > husk.Figure.Height + Seizing.ReachAbove) { GD.Print($"    (this world's smallest walker stands {prey.BodyHeight:F2} m: out of a Husk's reach, so not tried)"); outOfReach++; continue; }
            Creature h = sim.Spawn(husk, new Vector3(0f, 0f, 0f));
            Creature v = sim.Spawn(prey, new Vector3(1.6f, 0f, 0f));
            v.Health = v.Stats.MaxHealth = 10000f; // only the seize may end it
            Vector3 stay = v.Position;
            for (int i = 0; i < 30 * 40 && !v.Removed && !v.Dead; i++)
            {
                // Keep the prey within reach, cornered (an animal would keep off a Husk if it could).
                if (h.Phase == AttackPhase.None && h.Holding == null && !h.Attacking) stay = h.Position + new Vector3(1.4f, 0f, 0f);
                if (v.HeldBy == null)
                {
                    v.Position = stay;
                    v.Velocity = Vector3.Zero;
                }
                sim.Step(1f / 30f);
            }
            tries++;
            if (person && v.Removed) Check(sim.Creatures.Any(c => c.Species.Husk && c != h && c.Species.Figure.Height == prey.Figure.Height), "a seized person turns into a Husk of the same body");
            if (!person && v.Dead) killed++;
        }
        Check(seized > 0, $"Husks seize the living ({seized} holds)");
        Check(turned > 0 && turned <= 3, $"{turned} seized people turned");
        Check(killed > 0 || outOfReach == 3, $"{killed} seized animals died and none turned");
    }

    /// <summary>A Husk of this world tall enough to seize a head.</summary>
    Species TallHusk(WorldLife life)
    {
        for (int i = 0; i < 200; i++)
        {
            Species s = life.People.Person(Seed(), PersonRole.Worker, Occupancy.Friendly, 0.2f, husk: true);
            if (s.Figure.Height + Seizing.ReachAbove >= Seizing.StandingHead + 0.05f) return s;
        }
        throw new InvalidOperationException("no tall Husk in 200 rolls");
    }

    /// <summary>A Husk too short to bring its face to a standing head (a child) strikes and retches instead; crouch near it, and it can.</summary>
    void ShortHusksNeverSeize()
    {
        WorldLife life = Bestiary.Roll(_data, World(15f));
        int seized = 0, seizedCrouched = 0, blows = 0;
        for (int t = 0; t < 4; t++)
        {
            Species child = life.People.Person(Seed(), PersonRole.Child, Occupancy.Friendly, 0.2f, husk: true);
            var sim = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
            var you = new DummyTarget { Position = new Vector3(0f, 0f, -3f), Health = new Health(1e9f), Crouched = t % 2 == 1 };
            sim.Targets.Add(you);
            bool crouched = t % 2 == 1;
            sim.Seized += _ => { if (crouched) seizedCrouched++; else seized++; };
            sim.Attacked += e => { if (e.Target == you) blows++; };
            sim.Spawn(child, Vector3.Zero);
            for (int i = 0; i < 30 * 25; i++) sim.Step(1f / 30f);
        }
        Check(seized == 0 && blows > 0, $"a Husked child never takes a standing head ({blows} blows and retches instead)");
        Check(seizedCrouched > 0, $"but takes a crouched one ({seizedCrouched} holds)");
    }

    /// <summary>The player is held for the game to settle (death or a struggle), and never infected: a broken hold throws the Husk back.</summary>
    void ThePlayerIsNeverInfected()
    {
        var w = World(15f);
        WorldLife life = Bestiary.Roll(_data, w);
        Species husk = TallHusk(life);
        var sim = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
        var you = new DummyTarget { Position = new Vector3(0f, 0f, -6f), Health = new Health(1e6f) };
        sim.Targets.Add(you);
        SeizeEvent held = null, letGo = null;
        int turned = 0;
        sim.Seized += e => held ??= e;
        sim.LetGo += e => letGo = e;
        sim.Turned += _ => turned++;
        Creature h = sim.Spawn(husk, Vector3.Zero);
        for (int i = 0; i < 30 * 60 && held == null; i++) sim.Step(1f / 30f);
        Check(held != null && held.Victim == you, "a Husk comes for you and seizes you");
        float heldFor = 0f;
        for (int i = 0; i < 30 * 8 && h.Phase == AttackPhase.Hold; i++) { sim.Step(1f / 30f); heldFor += 1f / 30f; }
        Check(held == null || heldFor >= 7.9f && turned == 0, $"it holds you face to face for the game to settle ({heldFor:F1} s), and you never turn");
        Vector3 before = h.Position;
        Seizing.Break(sim, h);
        Check(letGo is { Broken: true } && WildSim.Flat(h.Position - you.Position).Length() > 1.5f, $"broken free, it's thrown back {WildSim.Flat(h.Position - before).Length():F1} m and staggers");
    }

    /// <summary>A ring of Husks can't pass you hand to hand: once a hold breaks, nothing seizes you again for the cooldown, though they still strike.</summary>
    void TheCooldownHolds()
    {
        var w = World(15f);
        WorldLife life = Bestiary.Roll(_data, w);
        var sim = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
        var you = new DummyTarget { Position = Vector3.Zero, Health = new Health(1e9f) };
        sim.Targets.Add(you);
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.Tau / 4f;
            sim.Spawn(TallHusk(life), new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * 2.2f);
        }
        var seizedAt = new List<float>();
        sim.Seized += e => seizedAt.Add(e.Time);
        float cooldown = _data.Husks.SeizeCooldown;
        int breaks = 0, early = 0, again = 0;
        for (int i = 0; i < 30 * 90 && breaks < 3; i++)
        {
            sim.Step(1f / 30f);
            Creature h = Seizing.Holder(sim, you);
            if (h == null || h.HeldFor < 0.5f) continue;
            // Thrown off: who takes you again, and how soon.
            Seizing.Break(sim, h);
            breaks++;
            float at = sim.Time;
            int before = seizedAt.Count;
            for (int k = 0; k < 30 * 12 && seizedAt.Count == before; k++) sim.Step(1f / 30f);
            if (seizedAt.Count > before)
            {
                if (seizedAt[before] - at < cooldown - 0.05f) early++;
                else again++;
            }
        }
        Check(breaks >= 2, $"a ring of four Husks seizes you {breaks} times, once each hold is thrown off");
        Check(early == 0, $"and none takes you within the {cooldown:F0} s after a hold breaks ({early} too soon)");
        Check(again > 0, $"after it they can again ({again} times)");
    }

    /// <summary>Hollows: a lesser thing than the animal, scarce off a Hollow world, common on one; only animals.</summary>
    void HollowsAreLesserAndScarce()
    {
        int with = 0, worlds = 120;
        bool lesser = true, scarce = true, animals = true;
        for (int i = 0; i < worlds; i++)
        {
            WorldLife life = Bestiary.Roll(_data, World(_fresh.Range(-10f, 35f)));
            var hollows = life.Moving.Where(s => s.Corrupted).ToList();
            if (hollows.Count > 0) with++;
            foreach (Species h in hollows)
            {
                Species host = life.Moving.FirstOrDefault(s => !s.Corrupted && s.Seed == h.Seed);
                animals &= h.Figure == null;
                if (host == null) continue;
                lesser &= h.Stats.MaxHealth < host.Stats.MaxHealth && h.Stats.RunSpeed < host.Stats.RunSpeed;
                scarce &= h.Scarcity <= host.Scarcity * 0.2f;
            }
        }
        Check(with > 0 && with < worlds * 0.5f, $"{with} of {worlds} ordinary worlds have a few Hollows");
        Check(lesser && scarce, "beside their healthy kind, slower, weaker and far fewer");
        Check(animals, "and only animals carry the fungus");
        var bloom = World(15f);
        bloom.Bloom = true;
        var all = Bestiary.Roll(_data, bloom).Moving.Where(s => s.Figure == null && !s.Mechanical).ToList();
        Check(all.Count(s => s.Corrupted) > all.Count / 2, $"on a Hollow world most kinds are Hollows ({all.Count(s => s.Corrupted)} of {all.Count})");
        Check(all.Where(s => s.Corrupted).All(s => s.Name.StartsWith(_data.Fauna.Corruption.Prefix)), "and they're called Hollows");
    }

    /// <summary>Nothing spawned in a place is inside its walls, pillars or stairs; nothing out on the surface walks into one.</summary>
    void NothingLivesInsideWalls()
    {
        // A room with pillars, a stair and a raised step: what the bug was found in.
        bool Solid(int x, int y, int z) => y < 0 || y >= 4 || x is < -8 or >= 8 || z is < -8 or >= 8
            || (x % 4 == 0 && z % 4 == 0 && y < 3) || (z == 5 && x >= 0 && y < x / 2) || (x < -5 && y < 1);
        var space = new BlockSpace(Solid, new Vector3I(-9, -2, -9), new Vector3I(9, 6, 9));
        var sim = new WildSim(_data, new Terrain((x, z) => 0f, 40f), new NavGrid(40f, 3f));
        WorldLife life = Bestiary.Roll(_data, World(15f));
        var kinds = life.Moving.Where(s => !s.Flies && s.Rig != RigKind.Swarm).Take(6).ToList();
        int made = 0;
        for (int i = 0; i < 160; i++)
        {
            var at = new Vector3(_fresh.Range(-7.5f, 7.5f), _fresh.Range(0f, 2.5f), _fresh.Range(-7.5f, 7.5f));
            Creature c = sim.SpawnIn(kinds[i % kinds.Count], space, at);
            if (!c.Removed) made++;
        }
        Check(Interpenetration.Scan(sim).Count == 0, $"{made} of 160 creatures put down at random in a room of pillars and stairs, none inside them (the rest found no room)");
        for (int i = 0; i < 30 * 20; i++) sim.Step(1f / 30f);
        Check(Interpenetration.Scan(sim).Count == 0 && sim.Freed == 0, $"and after 20 s none has walked into one ({sim.Freed} freed)");

        // Out on the surface: a block standing on the ground.
        var open = new WildSim(_data, new Terrain((x, z) => 0f, 60f), new NavGrid(60f, 3f));
        open.SolidAt = p => MathF.Abs(p.X) < 6f && MathF.Abs(p.Z) < 6f && p.Y < 4f;
        open.Targets.Add(new DummyTarget { Position = new Vector3(9f, 0f, 0f) });
        var walkers = life.Moving.Where(s => !s.Flies && s.Rig is RigKind.Legged or RigKind.Humanoid && !s.Mechanical).ToList();
        int spawned = 0;
        foreach (Species s in walkers) spawned += open.SpawnGroup(s, new Vector3(_fresh.Range(-10f, 10f), 0f, _fresh.Range(-10f, 10f)), 4).Count;
        bool inside = false;
        for (int i = 0; i < 30 * 30; i++)
        {
            open.Step(1f / 30f);
            if (i % 15 == 0) inside |= open.Creatures.Any(c => !c.Removed && !c.Dead && Interpenetration.Inside(open, c));
        }
        Check(!inside, $"{spawned} walkers spawned round a building on the surface: none spawns or walks inside it in 30 s");
    }

    /// <summary>
    /// A Husk den spreads by the worms alone: Husks and worm masses, never an egg. A den the Turned
    /// hold alone has no nest anywhere; one shared with others keeps their nests out of its rooms.
    /// </summary>
    void HuskDensHaveNoEggs()
    {
        var w = World(15f);
        w.Coreward = 1f;
        DeepFauna fauna = Bestiary.Roll(_data, w).Deep();
        int dens = 0, eggs = 0, mixed = 0, husks = 0, masses = 0;
        foreach (bool shared in new[] { false, true })
            for (int i = 0; i < 40; i++)
            {
                var plan = new PoiPlan { Seed = Seed(), State = (PlaceState)_fresh.Int(1, 3), Theme = (PoiTheme)_fresh.Int(0, 2), Husks = true, Shared = shared, Derelict = _fresh.Chance(0.2f) };
                for (int r = 0; r < 9; r++)
                {
                    int depth = r / 3;
                    var room = new PoiRoom
                    {
                        Index = r, Depth = depth, Dark = depth > 0,
                        Kind = r % 3 == 0 ? RoomKind.Corridor : r % 3 == 1 ? RoomKind.Room : RoomKind.Hall,
                        Min = new Vector3(r * 12f, -depth * 4f, 0f), Max = new Vector3(r * 12f + (r % 3 == 0 ? 3f : 8f), -depth * 4f + 3f, r % 3 == 0 ? 10f : 8f),
                    };
                    if (r > 0) { room.Links.Add(r - 1); plan.Rooms[r - 1].Links.Add(r); }
                    plan.Rooms.Add(room);
                }
                PoiPopulation pop = PoiFauna.Populate(plan, fauna, _data.Deep);
                dens++;
                husks += pop.Spawns.Count(s => s.Species.Husk);
                masses += pop.Masses.Count;
                if (!shared) eggs += pop.Nests.Count;
                var nestRooms = pop.Spawns.Where(s => s.Nest >= 0).Select(s => s.Room).ToHashSet();
                mixed += pop.Masses.Count(m => nestRooms.Contains(m.Room)) + pop.Spawns.Count(s => s.Species.Husk && nestRooms.Contains(s.Room));
            }
        Check(husks > dens && masses > dens * 2, $"{dens} dens hold {husks} Husks and {masses} worm masses");
        Check(eggs == 0, $"a den the Turned hold alone has no egg nest ({eggs} found)");
        Check(mixed == 0, $"and a shared den keeps others' nests out of its rooms ({mixed} Husks or masses among eggs)");
    }

    /// <summary>A den's rooms on fresh seeds: a way in, then rooms branching off it, some a level or two down, each its own box of open air.</summary>
    PoiPlan DenPlan(float severity, out BlockSpace space)
    {
        var plan = new PoiPlan { Seed = Seed(), State = PlaceState.Abandoned, Theme = (PoiTheme)_fresh.Int(0, 2), Husks = true, Severity = severity };
        int rooms = _fresh.Int(7, 13);
        for (int r = 0; r < rooms; r++)
        {
            int parent = r == 0 ? -1 : _fresh.Int(Math.Max(0, r - 3), r - 1);
            int depth = parent < 0 ? 0 : plan.Rooms[parent].Depth + (_fresh.Chance(0.25f) ? 1 : 0);
            RoomKind kind = r == 0 ? RoomKind.Entrance : _fresh.Chance(0.3f) ? RoomKind.Corridor : _fresh.Chance(0.3f) ? RoomKind.Hall : RoomKind.Room;
            var min = new Vector3((r % 5) * 14f, -depth * 5f, (r / 5) * 14f);
            var room = new PoiRoom { Index = r, Depth = depth, Dark = depth > 0, Kind = kind, Min = min, Max = min + new Vector3(kind == RoomKind.Corridor ? 3f : 8f, 3f, kind == RoomKind.Corridor ? 10f : 8f) };
            if (parent >= 0) { room.Links.Add(parent); plan.Rooms[parent].Links.Add(r); }
            plan.Rooms.Add(room);
        }
        bool Solid(int x, int y, int z)
        {
            foreach (PoiRoom r in plan.Rooms)
                if (x >= r.Min.X && x < r.Max.X && y >= r.Min.Y && y < r.Max.Y && z >= r.Min.Z && z < r.Max.Z) return false;
            return true;
        }
        space = new BlockSpace(Solid, new Vector3I(-2, -16, -2), new Vector3I(72, 6, 44));
        return plan;
    }

    /// <summary>
    /// A den is never plain from outside: nothing in the way in or what opens off it, at any
    /// severity. Light, almost nothing shows (a Husk or two in the furthest rooms, a faint vein,
    /// hardly a mass); severe, masses and veins room after room once past the way in.
    /// </summary>
    void DensHideFromOutside()
    {
        var w = World(15f);
        w.Coreward = 1f;
        DeepFauna fauna = Bestiary.Roll(_data, w).Deep();
        int dens = 0, front = 0, lightMasses = 0, severeMasses = 0, lightVeins = 0, severeVeins = 0, lightDens = 0, severeDens = 0, lightNear = 0, lightHusks = 0, severeRooms = 0, severeFilled = 0;
        bool veinsOnSurfaces = true, veinsInRooms = true;
        for (int i = 0; i < 60; i++)
        {
            bool light = i % 2 == 0;
            float sev = light ? _fresh.Range(0.03f, 0.15f) : _fresh.Range(0.75f, 1f);
            PoiPlan plan = DenPlan(sev, out BlockSpace space);
            PoiPopulation pop = PoiFauna.Populate(plan, fauna, _data.Deep);
            var hops = PoiFauna.Hops(plan);
            dens++;
            bool Front(int room) => hops.TryGetValue(room, out int h) && h <= 1;
            front += pop.Masses.Count(m => Front(m.Room)) + pop.Spawns.Count(s => s.Species.Husk && Front(s.Room))
                     + pop.VeinRoots.Count(v => plan.Rooms.Where(r => Front(r.Index)).Any(r => Inside(r, v.At)));
            var sim = new WildSim(_data, new Terrain((x, z) => 0f, 80f), new NavGrid(80f, 3f));
            PoiFauna.Spawn(sim, pop, space);
            foreach (Vein v in sim.Veins)
                foreach (Vector3 p in v.Points)
                {
                    float d = space.Density(p);
                    veinsOnSurfaces &= d < 0.05f && d > -0.12f;
                    veinsInRooms &= !plan.Rooms.Where(r => hops.GetValueOrDefault(r.Index, 9) == 0).Any(r => Inside(r, p));
                }
            if (light)
            {
                lightDens++;
                lightMasses += pop.Masses.Count;
                lightVeins += sim.Veins.Count;
                var husks = pop.Spawns.Where(s => s.Species.Husk).ToList();
                lightHusks += husks.Count;
                lightNear += husks.Select(s => s.Room).Distinct().Count();
            }
            else
            {
                severeDens++;
                severeMasses += pop.Masses.Count;
                severeVeins += sim.Veins.Count;
                var inner = plan.Rooms.Where(r => hops.GetValueOrDefault(r.Index, 0) >= 2 && r.Kind != RoomKind.Corridor).ToList();
                severeRooms += inner.Count;
                severeFilled += inner.Count(r => pop.Masses.Any(m => m.Room == r.Index));
            }
        }
        Check(front == 0, $"{dens} dens: nothing in the way in or the rooms off it ({front} Husks, masses or veins there)");
        Check(veinsOnSurfaces && veinsInRooms, "every vein lies on a floor, a wall or a ceiling, and none reaches the way in");
        Check(lightHusks >= lightDens && lightNear <= lightDens * 2, $"light dens: {lightHusks} Husks in {lightDens}, in {lightNear} rooms between them (the furthest few)");
        Check(lightMasses <= lightDens / 2 && lightVeins >= lightDens / 2 && lightVeins <= lightDens * 4, $"and hardly a mass ({lightMasses}), a faint vein or two ({lightVeins})");
        Check(severeFilled >= severeRooms * 0.7f && severeMasses > lightMasses * 4 + severeDens * 2, $"severe dens: masses in {severeFilled} of {severeRooms} inner rooms ({severeMasses} in {severeDens} dens)");
        Check(severeVeins > Math.Max(1, lightVeins) * 6, $"and veins room after room ({severeVeins}, against {lightVeins} in the light ones)");
    }

    static bool Inside(PoiRoom r, Vector3 p) => p.X >= r.Min.X - 0.3f && p.X <= r.Max.X + 0.3f && p.Y >= r.Min.Y - 0.3f && p.Y <= r.Max.Y + 0.3f && p.Z >= r.Min.Z - 0.3f && p.Z <= r.Max.Z + 0.3f;

    /// <summary>
    /// Silent carriers look like everyone else (they are rolled as anyone is) and give nothing away
    /// in company: only alone with you does one rise a Husk; alone long enough with one of their own,
    /// the worms pass on.
    /// </summary>
    void CarriersLookNormalUntilAlone()
    {
        var w = World(15f);
        w.Coreward = 1f;
        WorldLife life = Bestiary.Roll(_data, w);
        HuskRules r = _data.Husks;
        Species person = life.People.Person(Seed(), PersonRole.Worker);
        Species risen = Husks.Turn(person, Seed(), r);
        Check(!person.Husk && !person.Figure.Torn && person.Figure.Wrongs == Wrong.None && risen.Husk && risen.Figure.Height == person.Figure.Height,
            "a carrier is one of the town's people to look at; risen, the same body is a Husk");
        int notHere = 0, nobody = 0;
        for (int i = 0; i < 200; i++) notHere += Carriers.Roll(r, false, 1f, 12, new Rng(Seed()));
        for (int i = 0; i < 200; i++) nobody += Carriers.Roll(r, true, 1f, 12, new Rng(Seed())) > 0 ? 1 : 0;
        Check(notHere == 0 && nobody > 20 && nobody < 180, $"no carriers on a world without Husks; on a core world {nobody} of 200 towns have some");

        // In company: you right beside it, the town about.
        var town = new List<Carried>();
        for (int i = 0; i < 9; i++) town.Add(new Carried { At = new Vector3(_fresh.Range(-4f, 4f), 0f, _fresh.Range(-4f, 4f)), Out = true });
        town[0].Carrier = true;
        var rise = new List<int>();
        for (int k = 0; k < 40; k++) Carriers.Step(r, town, town[0].At + Vector3.Right, 0.25f, rise);
        Check(rise.Count == 0, "beside you in company it gives nothing away");
        // Alone: the others gone off down the street.
        for (int i = 1; i < town.Count; i++) town[i].At += new Vector3(30f, 0f, 0f);
        Carriers.Step(r, town, town[0].At + Vector3.Right, 0.25f, rise);
        Check(rise.Count == 1 && rise[0] == 0, "alone with you, it rises");

        // Alone with one of its own, long enough: the worms pass; with someone else there, they don't.
        var pair = new List<Carried> { new() { At = Vector3.Zero, Out = true, Carrier = true }, new() { At = Vector3.Right, Out = true }, new() { At = new Vector3(4f, 0f, 0f), Out = true } };
        for (int i = 0; i < 7; i++) pair.Add(new Carried { At = new Vector3(60f + i, 0f, 60f), Out = true }); // the rest of the town, across it
        rise.Clear();
        for (float t = 0f; t < r.CarrierPass * 2f; t += 0.25f) Carriers.Step(r, pair, null, 0.25f, rise);
        bool watched = !pair[1].Carrier;
        pair[2].At = new Vector3(40f, 0f, 0f);
        for (float t = 0f; t < r.CarrierPass + 1f; t += 0.25f) Carriers.Step(r, pair, null, 0.25f, rise);
        Check(watched && pair[1].Carrier && rise.Count == 0, "the worms pass to one of its own only when the two of them are alone, and nothing shows");
    }

    /// <summary>Once carriers are past the share of a settlement, they all drop the act; short of it, none does in company.</summary>
    void CarriersTurnPastTheShare()
    {
        HuskRules r = _data.Husks;
        var town = new List<Carried>();
        for (int i = 0; i < 20; i++) town.Add(new Carried { At = new Vector3(_fresh.Range(-5f, 5f), 0f, _fresh.Range(-5f, 5f)), Out = true });
        int under = (int)MathF.Ceiling(r.CarrierOpenShare * town.Count) - 1;
        for (int i = 0; i < under; i++) town[i].Carrier = true;
        var rise = new List<int>();
        Carriers.Step(r, town, null, 0.25f, rise);
        Check(rise.Count == 0 && !Carriers.Open(r, town), $"{under} carriers of {town.Count} keep up the act");
        town[under].Carrier = true;
        Carriers.Step(r, town, null, 0.25f, rise);
        Check(Carriers.Open(r, town) && rise.Count == under + 1, $"one more, past the {r.CarrierOpenShare:P0}, and all {rise.Count} drop it at once");
    }

    /// <summary>The wrongness rises with how many Husks there are and how near, and a deep den adds to it; animals keep off them.</summary>
    void WrongnessRisesWithHusks()
    {
        var w = World(15f);
        w.Coreward = 1f;
        WorldLife life = Bestiary.Roll(_data, w);
        Species husk = TallHusk(life);
        var felt = new List<float>();
        foreach (int n in new[] { 0, 1, 3, 6, 12 })
        {
            var sim = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f)) { Frozen = true };
            for (int i = 0; i < n; i++) sim.Spawn(husk, new Vector3(_fresh.Range(-3f, 3f), 0f, 14f + _fresh.Range(-3f, 3f)));
            felt.Add(Wrongness.At(sim, Vector3.Zero));
        }
        bool rises = true;
        for (int i = 1; i < felt.Count; i++) rises &= felt[i] > felt[i - 1];
        Check(felt[0] == 0f && rises && felt[1] < 0.15f && felt[^1] > 0.5f, $"felt 14 m from 0, 1, 3, 6 and 12 Husks: {string.Join(", ", felt.Select(f => f.ToString("F2")))} (faint near one, strong near many)");
        var near = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f)) { Frozen = true };
        for (int i = 0; i < 5; i++) near.Spawn(husk, new Vector3(i * 0.8f, 0f, 0f));
        float at5 = Wrongness.At(near, new Vector3(0f, 0f, 5f)), at20 = Wrongness.At(near, new Vector3(0f, 0f, 20f)), at40 = Wrongness.At(near, new Vector3(0f, 0f, 40f));
        near.DenSeverity = 1f;
        float den = Wrongness.At(near, new Vector3(0f, 0f, 20f));
        Check(at5 > at20 && at20 > at40 && at40 == 0f, $"it builds as you come near five of them: {at40:F2} at 40 m, {at20:F2} at 20, {at5:F2} at 5");
        Check(den > at20, $"and a deep den's are felt more ({den:F2} against {at20:F2} at 20 m)");

        // Animals won't go near: a herd grazing round a den's spot keeps off it; without it they wander anywhere.
        float Closest(bool den)
        {
            var sim = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
            if (den) sim.WrongPlaces.Add(Vector3.Zero);
            Species grazer = life.Moving.First(s => s.Behaviour is Behaviour.Grazer or Behaviour.Herd && s.Figure == null && !s.Flies && !s.Mechanical && !s.Husk);
            for (int i = 0; i < 6; i++) sim.Spawn(grazer, new Vector3(_fresh.Range(-12f, 12f), 0f, _fresh.Range(-12f, 12f)));
            float closest = float.MaxValue;
            for (int i = 0; i < 30 * 40; i++)
            {
                sim.Step(1f / 30f);
                if (i > 30 * 15) foreach (Creature c in sim.Creatures) closest = MathF.Min(closest, WildSim.Flat(c.Position).Length());
            }
            return closest;
        }
        float kept = Closest(true), free = Closest(false);
        Check(kept > _data.Husks.AnimalShun * 0.7f && kept > free, $"animals keep off a Husk: the nearest came {kept:F1} m, against {free:F1} m with nothing there");
    }

    /// <summary>A worm mass made of someone shows them: a hand, a foot or part of a face, from their own body.</summary>
    void MassesHoldBodyParts()
    {
        var w = World(15f);
        w.Coreward = 1f;
        WorldLife life = Bestiary.Roll(_data, w);
        Species host = life.People.Person(Seed(), PersonRole.Worker, Occupancy.Friendly, 0.2f, husk: true);
        var kinds = new HashSet<int>();
        bool more = true, skin = true;
        for (int i = 0; i < 12; i++)
        {
            var bare = new WormMass { Size = _fresh.Range(0.7f, 1.3f), Seed = Seed() };
            var made = new WormMass { Size = bare.Size, Seed = bare.Seed, Host = host };
            Piece a = WormMasses.Build(bare), b = WormMasses.Build(made);
            more &= b.Prims.Count >= a.Prims.Count + 2;
            var own = HumanBody.Build(host).Pieces.SelectMany(q => q.Prims).Select(q => q.Color).ToList();
            skin &= b.Prims.Skip(a.Prims.Count).All(p => own.Any(c => c.IsEqualApprox(p.Color)));
            kinds.Add(WormMasses.RemnantKind(made));
        }
        Check(more && skin, "a mass made of someone has part of their own body (their skin, their boots) sticking out of it");
        Check(kinds.Count == 3, $"a hand, a foot or a face ({kinds.Count} of the three kinds in 12 masses)");
    }

    /// <summary>Twenty Husks in view, worms and all, cost under 2 ms a frame to think, move and pose.</summary>
    void TwentyHusksAreCheap()
    {
        var w = World(15f);
        w.Coreward = 1f;
        WorldLife life = Bestiary.Roll(_data, w);
        var sim = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
        sim.Targets.Add(new DummyTarget { Position = new Vector3(0f, 0f, -30f) });
        var holder = new Node3D();
        AddChild(holder);
        var views = new List<CreatureView>();
        for (int i = 0; i < 20; i++)
        {
            Creature c = sim.Spawn(life.People.Person(Seed(), (PersonRole)(i % 9), Occupancy.Friendly, 0.2f, husk: true), new Vector3((i % 5) * 2.5f, 0f, (i / 5) * 2.5f));
            var view = new CreatureView();
            holder.AddChild(view);
            view.Build(c);
            views.Add(view);
        }
        double worst = 0;
        for (int f = 0; f < 150; f++)
        {
            long from = System.Diagnostics.Stopwatch.GetTimestamp();
            sim.Step(1f / 60f);
            foreach (CreatureView v in views) v.Sync();
            if (f > 20) worst = Math.Max(worst, System.Diagnostics.Stopwatch.GetElapsedTime(from).TotalMilliseconds);
        }
        Check(worst < 2.0, $"twenty Husks think, move, pose and sync in {worst:F2} ms a frame at worst (budget 2 ms)");
        holder.QueueFree();
    }

    void MutationIsALayer()
    {
        var life = Bestiary.Roll(_data, World(15f, mutagen: 0.8f));
        PeopleGenerator g = life.People;
        ulong seed = Seed();
        Species clean = g.Person(seed, PersonRole.Worker), mild = g.Person(seed, PersonRole.Worker, Occupancy.Friendly, 0.2f), monster = g.Person(seed, PersonRole.Raider, Occupancy.Raider, 0.95f);
        int Prims(Species s) => new HumanRig(s).Layout.Pieces.Sum(p => p.Prims.Count);
        int a = Prims(clean), b = Prims(mild), c = Prims(monster);
        Check(b > a && Mutation.Of(mild).Growths >= 1 && Mutation.Of(mild).Growths <= 3, $"a mild mutation adds a few growths ({Mutation.Of(mild).Growths})");
        Check(c > b + 8 && monster.Monstrous, $"a monstrous one changes the body ({Mutation.Of(monster).Growths} growths, {a} to {c} parts)");
        var wild = g.Person(Seed(), PersonRole.Worker, Occupancy.Feral, 0.8f);
        Check(wild.Figure.Dress == "rags" && wild.Stats.Hunts, "feral people are in rags and hunt; the mutation is the same layer");
        var friendly = g.Person(Seed(), PersonRole.Trader, Occupancy.Friendly, 0.8f);
        Check(!friendly.Stats.Hunts && friendly.Mutation > 0f, "a mutated townsperson is still a townsperson: occupancy decides");
        var machine = Robots.Roll(life.World, 0);
        machine.Mutation = 1f;
        Check(Mutation.Decorate(new RigLayout(), machine).Pieces.Count == 0 && Mutation.Of(machine).Growths == 0, "machines don't mutate");
        Species cow = new FaunaGenerator(_data.Fauna, _data.Activity).RollOne(Seed(), "strider", Behaviour.Herd, 0.2f);
        Species twisted = new FaunaGenerator(_data.Fauna, _data.Activity).Mutate(cow, 0.8f);
        twisted.Monstrous = true;
        Check(new LeggedRig(twisted).Layout.Pieces.Sum(p => p.Prims.Count) > new LeggedRig(cow).Layout.Pieces.Sum(p => p.Prims.Count) + 8, "the same layer works on an animal");
    }

    void SpitIsDodgeable()
    {
        var life = Bestiary.Roll(_data, World(15f, mutagen: 1f));
        Species spitter = null, bile = null;
        for (int i = 0; i < 300 && (spitter == null || bile == null); i++)
        {
            Species s = life.People.Person(Seed(), PersonRole.Worker, Occupancy.Feral, 1f);
            if (s.Spit == SpitKind.Blood) spitter ??= s;
            if (s.Spit == SpitKind.Bile) bile ??= s;
        }
        Check(spitter != null && bile != null, "mutated throats bring up blood and bile");
        if (spitter == null || bile == null) return;
        Check(Spitting.ColourOf(spitter) == spitter.Blood, "blood comes up the colour of their own blood");
        Color g = Spitting.ColourOf(bile);
        Check(g.G > g.R && g.G > g.B * 2f, $"bile is green-yellow ({g})");
        _ = new HumanRig(spitter).Layout;
        Check(Mutation.Of(spitter).Throat, "a throat like that shows");

        (bool hit, int gobs) Fight(bool dodge)
        {
            var sim = new WildSim(_data, new Terrain((x, z) => 0f, 100f), new NavGrid(100f, 3f));
            var t = new DummyTarget { Position = new Vector3(0f, 0f, -8f) };
            sim.Targets.Add(t);
            Creature c = sim.Spawn(spitter, Vector3.Zero);
            c.Yaw = 0f;
            int gobs = 0;
            bool hit = false;
            sim.Spat += _ => gobs++;
            sim.Splashed += (_, h) => hit |= h;
            Attacks.Start(sim, c, t);
            for (int i = 0; i < 240 && (gobs == 0 || sim.Gobs.Count > 0); i++)
            {
                if (dodge && gobs > 0) t.Position = new Vector3(4f, 0f, -8f);
                c.Desired = Vector3.Zero;
                sim.Step(1f / 60f);
            }
            return (hit, gobs);
        }
        var stood = Fight(false);
        var stepped = Fight(true);
        Check(stood.gobs == 1 && stood.hit, "standing still, it lands");
        Check(stepped.gobs == 1 && !stepped.hit, "stepping aside once it's let go, it misses");
    }

    void SensesFollowLight()
    {
        var gen = new FaunaGenerator(_data.Fauna, _data.Activity);
        Species hunter = gen.RollOne(Seed(), "stalker", Behaviour.Predator, 0.2f);
        hunter.Stats.Perception = 50f;
        float Awareness(SenseProfile p, float light, float lamp = 0f, bool crouched = false)
        {
            Species s = hunter.Copy();
            s.Senses = p;
            var sim = new WildSim(_data, new Terrain((x, z) => 0f, 100f), new NavGrid(100f, 3f)) { LightAt = _ => light };
            var t = new LitTarget { Position = new Vector3(0f, 0f, -35f), LampOn = lamp, Crouched = crouched };
            sim.Targets.Add(t);
            Creature c = sim.Spawn(s, Vector3.Zero);
            c.Yaw = 0f;
            float top = 0f;
            for (int i = 0; i < 60; i++)
            {
                Senses.Surface(sim, c, t, 35f, 0.05f);
                top = MathF.Max(top, c.Awareness);
                sim.Time += 0.05f;
            }
            return top;
        }
        var plain = new SenseProfile();
        var night = new SenseProfile { DarkVision = 0.95f };
        Check(Awareness(plain, 1f) >= 1f, "in daylight it sees you at 35 m");
        Check(Awareness(plain, 0.005f) < 0.2f, $"in the dark it doesn't ({Awareness(plain, 0.005f):F2})");
        Check(Awareness(plain, 0.005f, lamp: 1f) >= 1f, "unless you carry a torch");
        Check(Awareness(night, 0.005f) >= 1f, "and eyes made for the dark see you anyway");
        Check(Awareness(new SenseProfile { Sight = 0f }, 1f) < 0.1f, "a blind thing doesn't, however bright it is");
        float Ear(bool crouched)
        {
            Species s = hunter.Copy();
            s.Senses = new SenseProfile { Sight = 0f, Hearing = 1.6f };
            var sim = new WildSim(_data, new Terrain((x, z) => 0f, 100f), new NavGrid(100f, 3f));
            var t = new LitTarget { Position = new Vector3(0f, 0f, -22f), Crouched = crouched };
            sim.Targets.Add(t);
            Creature c = sim.Spawn(s, Vector3.Zero);
            for (int i = 0; i < 30; i++) { Senses.Surface(sim, c, t, 22f, 0.05f); sim.Time += 0.05f; }
            return c.Awareness;
        }
        Check(Ear(false) > 0.3f && Ear(true) < 0.05f, $"good ears hear a walker, not someone creeping ({Ear(false):F2} and {Ear(true):F2})");
        int clicks = 0;
        Species bat = hunter.Copy();
        bat.Senses = new SenseProfile { Sight = 0f, Hearing = 1.5f, Echo = true };
        var world = new WildSim(_data, new Terrain((x, z) => 0f, 100f), new NavGrid(100f, 3f));
        var prey = new LitTarget { Position = new Vector3(0f, 0f, -14f) };
        world.Targets.Add(prey);
        world.Clicked += _ => clicks++;
        Creature b = world.Spawn(bat, Vector3.Zero);
        for (int i = 0; i < 60; i++) { Senses.Surface(world, b, prey, 14f, 0.05f); world.Time += 0.05f; }
        Check(clicks >= 2 && b.Awareness >= 1f, $"an echo hunter finds you in the dark, and clicks as it does ({clicks} clicks)");
    }

    sealed class LitTarget : ITarget
    {
        public string Name => "Lit";
        public Vector3 Position { get; set; }
        public Vector3 Forward => Vector3.Forward;
        public Health Health { get; } = new(100f);
        public bool Attackable => true;
        public float ThreatScale => 1f;
        public bool Crouched { get; set; }
        public float LampOn;
        public float Lamp => LampOn;
    }

    void ScentFollowsTheWind()
    {
        var gen = new FaunaGenerator(_data.Fauna, _data.Activity);
        Species nose = gen.RollOne(Seed(), "stalker", Behaviour.Predator, 0.2f);
        nose.Stats.Perception = 50f;
        nose.Senses = new SenseProfile { Sight = 0f, Hearing = 0.1f, Scent = 1.3f };
        float Awareness(Vector3 wind)
        {
            var sim = new WildSim(_data, new Terrain((x, z) => 0f, 100f), new NavGrid(100f, 3f)) { Wind = wind };
            var t = new LitTarget { Position = new Vector3(0f, 0f, -44f) };
            sim.Targets.Add(t);
            Creature c = sim.Spawn(nose, Vector3.Zero);
            for (int i = 0; i < 100; i++) { Senses.Surface(sim, c, t, 44f, 0.05f); sim.Time += 0.05f; }
            return c.Awareness;
        }
        // Wind from you to it (+Z here) carries you to it; the other way doesn't.
        Check(Awareness(new Vector3(0f, 0f, 7f)) > 0.5f, $"downwind it smells you at 44 m ({Awareness(new Vector3(0f, 0f, 7f)):F2})");
        Check(Awareness(new Vector3(0f, 0f, -7f)) < 0.05f, "upwind it doesn't");
        Check(Awareness(Vector3.Zero) < 0.05f, "and in still air not that far");
    }

    static float Rms(short[] s) => MathF.Sqrt(s.Average(v => v * (float)v)) / 32768f;

    /// <summary>How often the wave crosses zero, a second: rough brightness and pitch.</summary>
    static float Crossings(short[] s)
    {
        int n = 0;
        for (int i = 1; i < s.Length; i++) if ((s[i - 1] < 0) != (s[i] < 0)) n++;
        return n / (s.Length / (float)Voice.Rate);
    }

    static float Difference(short[] a, short[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        double total = 0;
        for (int i = 0; i < n; i++) total += Math.Abs(a[i] - b[i]);
        return (float)(total / n / 32768.0);
    }

    void VoicesAreTheirOwn()
    {
        var life = Bestiary.Roll(_data, World(15f));
        PeopleGenerator g = life.People;
        Species a = g.Person(Seed(), PersonRole.Trader), b = g.Person(Seed(), PersonRole.Trader);
        short[] clip = a.Figure.Voice.Render(VoiceMood.Murmur, 0);
        Check(clip.Length > Voice.Rate / 3 && Rms(clip) > 0.02f, $"a voice makes sound ({clip.Length / (float)Voice.Rate:F1} s, level {Rms(clip):F2})");
        Check(a.Figure.Voice.Render(VoiceMood.Murmur, 0).SequenceEqual(clip), "the same person says the same thing the same way");
        Check(Difference(clip, b.Figure.Voice.Render(VoiceMood.Murmur, 0)) > 0.01f, "two people don't sound alike");
        Check(Rms(a.Figure.Voice.Render(VoiceMood.Shout, 0)) > Rms(clip), "a shout is fuller than a murmur");
        Check(Crossings(a.Figure.Voice.Render(VoiceMood.Whisper, 0)) > Crossings(clip) * 1.6f, "a whisper is breath, not voice");
        Check(a.Figure.Voice.Render(VoiceMood.Cry, 0).Length > 0, "a cry renders");
        Species child = g.Person(Seed(), PersonRole.Child);
        Species big = null;
        for (int i = 0; i < 80; i++)
        {
            Species s = g.Person(Seed(), PersonRole.Guard);
            if (s.Figure.Sex == Sex.Male && (big == null || s.Figure.Mass > big.Figure.Mass)) big = s;
        }
        Check(big.Figure.Voice.Pitch < child.Figure.Voice.Pitch * 0.7f, $"a big man speaks lower than a child ({big.Figure.Voice.Pitch:F0} and {child.Figure.Voice.Pitch:F0} Hz)");
        Dialect here = Dialect.For(Seed()), there = Dialect.For(Seed());
        Check(here.Vowels.Length >= 4 && (MathF.Abs(here.Rate - there.Rate) > 0.01f || here.Vowels[0] != there.Vowels[0]), "each world has its own way of speaking");
        string text = System.IO.File.ReadAllText(System.IO.Path.Combine(DataFiles.Root, "Audio", "Voices.cs"));
        Check(!text.Contains("AudioStreamWav") && !text.Contains(".wav") && !text.Contains(".ogg"), "voices are made in code, from nothing");
    }

    void HeavyThingsSoundHeavy()
    {
        var gen = new FaunaGenerator(_data.Fauna, _data.Activity);
        Species small = gen.RollOne(Seed(), "strider", Behaviour.Herd, 0.2f), huge = gen.RollOne(Seed(), "colossus", Behaviour.TerritorialGiant, 0.2f);
        Check(Heft.Mass(huge) > Heft.Mass(small) * 4f, $"a colossus outweighs a strider ({Heft.Mass(huge):F0} and {Heft.Mass(small):F0} kg)");
        Check(Heft.Pitch(huge) < Heft.Pitch(small) && Heft.Loudness(huge) > Heft.Loudness(small), "and sounds it: lower and louder, from the same number");
        Check(Heft.TurnRate(new Creature { Species = huge }, 0f) < Heft.TurnRate(new Creature { Species = small }, 0f), "and turns slower");
    }

    /// <summary>Creatures near someone walking about among them change state cleanly: a state, once left, isn't flipped straight back.</summary>
    void StatesHold()
    {
        var sim = new WildSim(_data, new Terrain((x, z) => 0f, 150f), new NavGrid(150f, 3f));
        var walker = new DummyTarget { Position = new Vector3(-40f, 0f, 0f) };
        sim.Targets.Add(walker);
        WorldLife life = Bestiary.Roll(_data, World(15f));
        foreach (Species s in life.Fauna.Species.Where(s => !s.Mechanical && s.Figure == null).Take(5))
            sim.SpawnGroup(s, new Vector3(_fresh.Range(-30f, 30f), 0f, _fresh.Range(-30f, 30f)), Math.Max(1, s.GroupMin));
        for (int i = 0; i < 1800; i++)
        {
            // Walking a slow figure of eight through them, about the edge of what they notice.
            float t = i / 60f;
            walker.Position = new Vector3(MathF.Sin(t * 0.21f) * 40f, 0f, MathF.Sin(t * 0.42f) * 20f);
            walker.Health.Restore();
            sim.Step(1f / 60f);
        }
        int changes = sim.Creatures.Sum(c => c.StateChanges), flips = sim.Creatures.Sum(c => c.StateFlips);
        Check(changes > 0 && flips <= changes * 0.05f, $"states hold: {flips} quick flips back in {changes} changes over 30 s near someone ({string.Join(", ", sim.Creatures.Where(c => c.StateFlips > 0).Select(c => $"{c.Species.Behaviour} {c.LastFlip}"))})");
    }

    void YoungStayInTheMiddle()
    {
        Species herd = null;
        for (int i = 0; i < 30 && herd == null; i++) herd = Bestiary.Roll(_data, World(15f)).Fauna.Species.FirstOrDefault(s => s.Young != null);
        Check(herd != null, "herds have young");
        if (herd == null) return;
        Check(herd.Young.Body.Size < herd.Body.Size * 0.6f && herd.Young.Body.HeadSize / herd.Young.Body.Size > herd.Body.HeadSize / herd.Body.Size && herd.Young.Stats.Damage == 0f, "young are small, big-headed for their size and harmless, from the same body");
        var sim = new WildSim(_data, new Terrain((x, z) => 0f, 100f), new NavGrid(100f, 3f));
        var group = sim.SpawnGroup(herd, Vector3.Zero, 8);
        Vector3 middle = group.Aggregate(Vector3.Zero, (a, c) => a + c.Position) / group.Count;
        float Ring(IEnumerable<Creature> l) => l.Select(c => WildSim.Flat(c.Position - middle).Length()).DefaultIfEmpty(0f).Average();
        var young = group.Where(c => c.Species.Adult != null).ToList();
        Check(young.Count > 0 && Ring(young) < Ring(group.Where(c => c.Species.Adult == null)), "a herd keeps its young in the middle");
        Check(Heft.Pitch(herd.Young) > Heft.Pitch(herd), "and they sound higher, from the same numbers");
        float Moved(Func<Creature, bool> who)
        {
            var at = group.Where(who).ToDictionary(c => c, c => c.Position);
            float most = 0f;
            for (int i = 0; i < 600; i++) { sim.Step(1f / 60f); foreach (var c in at.Keys) most = MathF.Max(most, WildSim.Flat(c.Velocity).Length()); }
            return most;
        }
        float youngTop = Moved(c => c.Species.Adult != null), adultTop = Moved(c => c.Species.Adult == null);
        Check(youngTop > adultTop, $"at ease the young dash about while the adults go steadily ({youngTop:F1} and {adultTop:F1} m/s at most)");
        group[0].Herd.Alarm(Vector3.Zero, sim.Time);
        sim.Step(1f / 60f);
        Check(young.All(c => c.FriskFor <= 0f), "and the play stops at once when the herd takes fright");
    }

    /// <summary>Inside a place, nothing calls out: something moving out of sight makes a small sound, through a wall a quieter one; something still makes none.</summary>
    void QuietInsidePlaces()
    {
        var sim = new WildSim(_data, new Terrain((x, z) => 0f, 100f), new NavGrid(100f, 3f));
        var you = new DummyTarget { Position = new Vector3(0f, 0f, 0f) };
        bool wall = true;
        sim.SightLine = (a, b) => !wall;
        var gen = new FaunaGenerator(_data.Fauna, _data.Activity);
        Species s = gen.RollOne(Seed(), "stalker", Behaviour.Predator, 0.2f);
        Creature c = sim.Spawn(s, new Vector3(8f, 0f, 0f));
        c.Space = new BoxSpace();
        var heard = new List<QuietSound>();
        for (int i = 0; i < 600; i++) Quiet.Step(sim, you, 1f / 60f, heard);
        Check(heard.Count == 0, "standing still in a place, it makes no sound at all");
        for (int i = 0; i < 600; i++) { c.Velocity = Vector3.Forward * 1.5f; Quiet.Step(sim, you, 1f / 60f, heard); }
        Check(heard.Count >= 1 && heard.All(q => q.ThroughWall && q.Kind is QuietKind.Footsteps or QuietKind.Clatter), $"moving behind a wall, it is heard: {heard.Count} footfalls in 10 s, quieter for the wall");
        heard.Clear();
        wall = false;
        for (int i = 0; i < 600; i++) { c.Velocity = Vector3.Forward * 1.5f; Quiet.Step(sim, you, 1f / 60f, heard); }
        Check(heard.Count == 0, "in plain sight it makes no telltale sound");
    }

    /// <summary>A shot into a peaceful herd: the whole herd bolts, but not on one tick; the young last.</summary>
    void NobodyReactsAtOnce()
    {
        var gen = new FaunaGenerator(_data.Fauna, _data.Activity);
        Species s = gen.RollOne(Seed(), "strider", Behaviour.Herd, 0.2f);
        s.Young = Fit.Young(s, new Rng(Seed()));
        var sim = new WildSim(_data, new Terrain((x, z) => 0f, 120f), new NavGrid(120f, 3f));
        var hunter = new DummyTarget { Position = new Vector3(0f, 0f, 60f) };
        sim.Targets.Add(hunter);
        var herd = sim.SpawnGroup(s, Vector3.Zero, 10);
        for (int i = 0; i < 60; i++) sim.Step(1f / 60f);
        float shot = sim.Time;
        sim.Damage(herd[^1], 1f, hunter);
        for (int i = 0; i < 60 * 4; i++) sim.Step(1f / 60f);
        var times = herd.Where(c => c.ReactedAt >= shot).Select(c => c.ReactedAt - shot).ToList();
        Check(times.Count >= herd.Count - 1, $"the whole herd bolts ({times.Count} of {herd.Count})");
        if (times.Count < 2) return;
        Check(times.Max() - times.Min() > 0.4f && times.Select(t => MathF.Round(t * 60f)).Distinct().Count() > times.Count / 2 && times.Max() < Reaction.Most + 0.5f, $"each in its own time: {times.Min():F2} to {times.Max():F2} s after the shot, never all on one tick");
        var young = herd.Where(c => c.Species.Adult != null && c.ReactedAt >= shot).Select(c => c.ReactedAt - shot).DefaultIfEmpty(-1f).Average();
        var grown = herd.Where(c => c.Species.Adult == null && c.ReactedAt >= shot).Select(c => c.ReactedAt - shot).Average();
        Check(young < 0f || young > grown * 0.9f, $"the young are slower to go ({young:F2} against {grown:F2} s)");
        var machine = Robots.Roll(World(15f), 0);
        var a = sim.Spawn(machine, new Vector3(30f, 0f, 0f));
        var b = sim.Spawn(machine, new Vector3(34f, 0f, 0f));
        Check(Reaction.Delay(a, 5f, false) == Reaction.Delay(b, 30f, true), "machines react together, on the same tick, whatever their distance");
    }

    /// <summary>Twenty people close by: one drawn body each, posed in well under the frame budget, with nothing allocated.</summary>
    void TwentyPeopleAreCheap()
    {
        PeopleGenerator g = Bestiary.Roll(_data, World(15f)).People;
        var crowd = new List<(HumanRig rig, PersonView view)>();
        var holder = new Node3D();
        AddChild(holder);
        for (int i = 0; i < 20; i++)
        {
            var rig = new HumanRig(g.Person(Seed(), (PersonRole)(i % 10)));
            var view = new PersonView();
            holder.AddChild(view);
            view.Build(rig);
            crowd.Add((rig, view));
        }
        double worst = 0;
        long bytes = 0;
        for (int f = 0; f < 120; f++)
        {
            long from = System.Diagnostics.Stopwatch.GetTimestamp(), alloc = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < crowd.Count; i++)
            {
                float t = f / 60f + i;
                crowd[i].rig.Drive(new Gesture { Feet = new Vector3(i, 0f, -t * 1.3f), Speed = 1.3f, Walked = t * 1.3f, Clock = t, Act = i % 3 == 0 ? BodyAct.Greet : BodyAct.None, ActAmount = 1f });
                crowd[i].view.Sync();
            }
            bytes += GC.GetAllocatedBytesForCurrentThread() - alloc;
            if (f > 10) worst = Math.Max(worst, System.Diagnostics.Stopwatch.GetElapsedTime(from).TotalMilliseconds);
        }
        Check(crowd.All(c => c.view.DrawnNear == 1), "each person is one drawn body close to, not a node per piece");
        Check(worst < 1.5, $"twenty people walking and gesturing pose in {worst:F2} ms a frame at worst (budget 2 ms with drawing)");
        Check(bytes == 0, $"and allocate nothing doing it ({bytes} B)");
        holder.QueueFree();
    }

    /// <summary>Groups' first reactions to an outsider, by tech: every level shows more than one kind, and the mix shifts.</summary>
    void TempersByTech()
    {
        var mixes = new Dictionary<string, Dictionary<Stance, int>>();
        foreach (string tech in new[] { "low", "mid", "high" })
        {
            var mix = mixes[tech] = new Dictionary<Stance, int>();
            for (int i = 0; i < 400; i++)
            {
                Stance s = Temper.Roll(tech, _fresh.Range(0f, 0.6f), Seed()).Stance;
                mix[s] = mix.GetValueOrDefault(s) + 1;
            }
            GD.Print($"  ({tech} tech: {string.Join(", ", mix.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))})");
        }
        Check(mixes.Values.All(m => m.Count >= 3), "every tech level meets outsiders more than one way");
        int Of(string t, Stance s) => mixes[t].GetValueOrDefault(s);
        Check(Of("low", Stance.Superstitious) > Of("high", Stance.Superstitious) && Of("high", Stance.Indifferent) > Of("low", Stance.Indifferent) && Of("mid", Stance.Bargaining) > Of("low", Stance.Bargaining), "and the mix shifts: superstition low, indifference high, bargaining in the middle");
    }

    /// <summary>Every combination of proportions walks, reaches and gestures: feet planted, hands where they're sent.</summary>
    void EveryBuildWorks()
    {
        PeopleGenerator g = Bestiary.Roll(_data, World(15f)).People;
        float worstFoot = 0f, worstHand = 0f;
        bool finite = true;
        var roles = Enum.GetValues<PersonRole>();
        for (int n = 0; n < 40; n++)
        {
            Species s = g.Person(Seed(), roles[n % roles.Length], founders: g.FoundersOf(Seed()));
            var rig = new HumanRig(s);
            float walked = 0f;
            for (int i = 0; i < 120; i++)
            {
                walked += 1.3f / 60f;
                rig.Drive(new Gesture { Feet = Vector3.Forward * walked, Speed = 1.3f, Walked = walked, Clock = i / 60f });
                for (int k = 0; k < 2; k++) if (rig.FootPlanted(k)) worstFoot = MathF.Max(worstFoot, rig.FootTarget(k).DistanceTo(rig.FootDrawn(k)));
                foreach (Transform3D t in rig.Pose) finite &= Geo.Finite(t);
            }
            foreach (BodyAct act in new[] { BodyAct.HandsUp, BodyAct.Greet, BodyAct.Warn, BodyAct.Cower })
            {
                rig.Drive(new Gesture { Feet = Vector3.Zero, Act = act, ActAmount = 1f, Clock = 1f });
                foreach (Transform3D t in rig.Pose) finite &= Geo.Finite(t);
                int arm = rig.Layout.Pieces.FindIndex(p => p.Name == "arm1l");
                Transform3D fore = rig.Pose[arm];
                Vector3 wrist = fore.Origin + fore.Basis.Y;
                worstHand = MathF.Max(worstHand, MathF.Abs(fore.Basis.Y.Length() - 1f));
            }
        }
        Check(finite, "every build of 40 random people poses finite, walking and gesturing");
        Check(worstFoot < 0.075f, $"and every one keeps feet on the ground ({worstFoot * 100f:F1} cm at worst, at toe-off on the longest strides)");
        Check(worstHand < 0.01f, "and no arm stretches to reach a gesture");
    }

    /// <summary>A settlement mostly shares a look, two settlements differ, and nobody's look follows their job or side.</summary>
    void LooksClusterBySettlement()
    {
        PeopleGenerator g = Bestiary.Roll(_data, World(15f)).People;
        float Spread(List<Species> l) { float m = l.Average(s => s.Figure.Skin.V); return MathF.Sqrt(l.Average(s => (s.Figure.Skin.V - m) * (s.Figure.Skin.V - m))); }
        Founders a = g.FoundersOf(Seed()), b = g.FoundersOf(Seed());
        var here = Enumerable.Range(0, 60).Select(i => g.Person(Seed(), PersonRole.Worker, founders: a)).ToList();
        var everywhere = Enumerable.Range(0, 60).Select(i => g.Person(Seed(), PersonRole.Worker)).ToList();
        Check(Spread(here) < Spread(everywhere) * 0.8f, $"a settlement mostly shares a look ({Spread(here):F3} spread against {Spread(everywhere):F3} across the world)");
        ulong one = Seed();
        var asRaider = g.Person(one, PersonRole.Raider, Occupancy.Raider, founders: a).Figure;
        var asTrader = g.Person(one, PersonRole.Trader, Occupancy.Friendly, founders: a).Figure;
        Check(asRaider.Skin == asTrader.Skin && asRaider.Brow == asTrader.Brow, "the same person looks the same whatever their job or side: looks come only from where their people settled");
    }

    /// <summary>An open room: for things that only need to know they're inside.</summary>
    sealed class BoxSpace : ISolidSpace
    {
        public float Density(Vector3 p) => -p.Y;
        public bool SolidCell(int x, int y, int z) => y < 0;
    }

    /// <summary>One wall across the way, of a given armour and hit points, under the game's rule: each blow loses the armour first.</summary>
    sealed class Wall : IBlockWorld
    {
        public float Armour, Hp, X = 4f;
        public bool Base;
        public int Blows;
        public bool Blocking(Vector3 from, Vector3 to, out BlockAt b)
        {
            b = new BlockAt { Point = new Vector3(X, 1f, 0f), PlayerBase = Base };
            return Hp > 0f && (from.X - X) * (to.X - X) < 0f;
        }
        public void Strike(in BlockAt b, float amount, BreakerKind kind) { Blows++; Hp -= MathF.Max(0f, amount - Armour); }
    }

    void BreakersBreakWeakBlocks()
    {
        WorldLife life = Bestiary.Roll(_data, World(15f));
        Species ram = life.Breaker(BreakerKind.Ram, new Rng(Seed()));
        int breakers = Enumerable.Range(0, 200).Count(_ => Bestiary.Roll(_data, World(15f)).Fauna.Species.Any(s => s.Breaker != BreakerKind.None));
        Check(breakers > 20 && breakers < 120, $"block-breakers are their own uncommon class: on {breakers} of 200 worlds");
        (Wall wall, float warnedAt, float firstBlowAt) Batter(float armour, float hp, bool isBase = false, bool bases = true)
        {
            var sim = new WildSim(_data, new Terrain((x, z) => 0f, 100f), new NavGrid(100f, 3f)) { BreakBases = bases };
            var wall = new Wall { Armour = armour, Hp = hp, Base = isBase };
            sim.Blocks = wall;
            var you = new DummyTarget { Position = new Vector3(6f, 0f, 0f) };
            sim.Targets.Add(you);
            sim.SightLine = (a, b) => wall.Hp <= 0f;
            Creature c = sim.Spawn(ram, new Vector3(2f, 0f, 0f));
            float warned = -1f, blow = -1f;
            sim.Battering += (_, _) => { if (warned < 0f) warned = sim.Time; };
            sim.Broke += (_, _) => { if (blow < 0f) blow = sim.Time; };
            for (int i = 0; i < 60 * 25 && wall.Hp > 0f; i++)
            {
                c.Noticed = you;
                c.NoticedAt = sim.Time;
                you.Health.Restore();
                sim.Step(1f / 60f);
            }
            return (wall, warned, blow);
        }
        var scrap = Batter(5f, 120f);
        Check(scrap.wall.Hp <= 0f, $"a ram breaks through a scrap wall ({scrap.wall.Blows} blows of {Breaking.Blow(ram):F0})");
        Check(scrap.warnedAt >= 0f && scrap.firstBlowAt - scrap.warnedAt >= Breaking.Warning - 0.05f, $"and gives warning first ({scrap.firstBlowAt - scrap.warnedAt:F1} s of battering before the first blow)");
        var steel = Batter(Breaking.Blow(ram) + 10f, 400f);
        Check(steel.wall.Blows > 2 && steel.wall.Hp >= 400f, $"combat steel holds ({steel.wall.Blows} blows, not a scratch)");
        var safe = Batter(5f, 120f, isBase: true, bases: false);
        Check(safe.wall.Blows == 0 && safe.wall.Hp >= 120f, "with damage to bases switched off, a base takes none");
    }
}