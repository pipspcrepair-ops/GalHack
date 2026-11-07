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
            w.Bloom = w.Mutagen > 0f;
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
        Check(kinds.Any(s => s.Figure?.Husk == true), "a Bloom world has husks walking in it");
        Check(kinds.Where(s => s.Figure?.Husk == true).All(s => !s.CommonName.Contains("hollow", StringComparison.OrdinalIgnoreCase) && !s.CommonName.Contains("husk", StringComparison.OrdinalIgnoreCase)), "and nothing labels them");
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