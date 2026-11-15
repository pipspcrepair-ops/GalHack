using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Slice.Surface;
using Wildlife;
using Rng = Wildlife.Rng;

namespace Slice.Tests;

/// <summary>
/// Culture (docs/research/culture.md, phase 1). Over fresh worlds every run, never pinned: each world's
/// culture rolls by its values' weights and its world's leans, breaks none of its rules, comes near today's
/// on about 3 worlds in 10 and with an extreme on about 1 in 8; towns drift from their world and factions
/// keep their own; and the people it makes show it: who is which sex in which job, what they wear and who
/// carries arms (the young by how desperate the world is), and who runs hot.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/CultureTest.tscn
/// </summary>
public partial class CultureTest : Node
{
    const int WatchdogMinutes = 12;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _rng = new();
    PoiLibrary _lib;

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        try
        {
            _lib = WorldChoice.Library;
            Data();
            var worlds = Worlds(1000);
            Shares(worlds);
            Strength(worlds);
            Towns(worlds);
            Factions();
            People(worlds);
            Spacing();
        }
        catch (Exception e) { Check(false, $"the test threw: {e}"); }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    static readonly string[] Archetypes = { "Temperate", "Arid", "Ice", "Lava", "Alien" };

    ulong Seed() => (ulong)_rng.NextInt64();

    /// <summary>A fresh settled world (a seed's own depth in the galaxy, so one seed is one world whatever is forced).</summary>
    WorldRoll Roll(ulong seed, string arch, WorldRoll.Given given = null)
    {
        WorldProfile profile = WorldProfile.Roll(seed, arch, false);
        if (!profile.Inhabited) profile = WorldProfile.Roll(seed ^ 0x5EED, arch, true);
        return WorldRoll.Roll(_lib, seed, arch, profile, false, given, (seed >> 20) % 1000 / 1000f);
    }

    List<WorldRoll> Worlds(int n)
    {
        var all = new List<WorldRoll>();
        for (int i = 0; i < n; i++)
            if (Roll(Seed(), Archetypes[_rng.Next(Archetypes.Length)]) is { Culture: not null } r) all.Add(r);
        return all;
    }

    static string Pct(float f) => $"{f * 100f:F1}%";

    void Data()
    {
        GD.Print("-- the data --");
        foreach (LayerKind k in CultureReading.Axes)
        {
            var values = _lib.Layers.Where(l => l.Kind == k).ToList();
            Check(values.Count >= 3 && values.Count(l => l.Blank) == 1, $"{k}: {values.Count} values, one of them today's");
        }
        var sets = _lib.Layers.Where(l => l.Kind == LayerKind.Culture).ToList();
        bool named = sets.All(s => s.Sets.All(t => Enum.TryParse(t.Fact, true, out LayerKind k) && t.Value.Split(',').All(id => _lib.Layers.Any(l => l.Kind == k && l.Id == id))));
        Check(sets.Count == 6 && named, $"{sets.Count} factions keep a culture of their own, every value real");
    }

    /// <summary>What the roller gave a value: its weight, the likes of what was rolled before it, the world's desperation.</summary>
    static float Odds(WorldRoll w, WorldLayer v, int before)
    {
        float odds = v.Weight;
        for (int i = 0; i < before; i++)
            foreach (LayerLike like in w.Rolled[i].Like)
                if (like.Layer == v.Kind && like.Id == v.Id) odds *= like.By;
        if (v.Traits.ContainsKey("desperate")) odds *= MathF.Max(0.05f, 1f + w.Desperation * v.Trait("desperate"));
        return MathF.Max(odds, 1e-6f);
    }

    void Shares(List<WorldRoll> worlds)
    {
        GD.Print($"-- value shares over {worlds.Count} fresh settled worlds --");
        var seen = new Dictionary<WorldLayer, float>();
        var expect = new Dictionary<WorldLayer, float>();
        int broken = 0;
        string brokeAt = "";
        foreach (WorldRoll w in worlds)
        {
            CultureReading c = w.Culture;
            foreach (var (k, v) in c.Values)
            {
                if (!v.Req.All(q => q.Holds(w.Facts))) { broken++; brokeAt = $"{k}.{v.Id} on {c}"; }
                if (v.Blank || v.Trait("extreme") > 0f) continue;
                // Among the off-default values that could stand, each by its odds.
                int at = w.Rolled.IndexOf(v);
                var pool = _lib.Layers.Where(l => l.Kind == k && !l.Off && !l.Blank && l.Trait("extreme") <= 0f && l.Req.All(q => q.Holds(w.Facts))).ToList();
                float total = pool.Sum(l => Odds(w, l, at));
                foreach (WorldLayer l in pool) expect[l] = expect.GetValueOrDefault(l) + Odds(w, l, at) / total;
                seen[v] = seen.GetValueOrDefault(v) + 1f;
            }
        }
        Check(broken == 0, $"no culture breaks its rules ({broken} broken{(broken > 0 ? ": " + brokeAt : "")})");
        int off = 0;
        var notes = new List<string>();
        foreach (var (v, e) in expect.Where(t => t.Value >= 20f).OrderBy(t => t.Key.Kind))
        {
            float got = seen.GetValueOrDefault(v);
            // (30%, or three standard deviations where the count is small.)
            if (MathF.Abs(got - e) > MathF.Max(0.3f * e, 3f * MathF.Sqrt(e))) { off++; notes.Add($"{v.Kind}.{v.Id} {got:F0} for {e:F0}"); }
        }
        Check(off == 0, $"every value's share within 30% (or 3 sd) of what its weight and its world's leans give it ({expect.Count(t => t.Value >= 20f)} values{(off > 0 ? "; off: " + string.Join(", ", notes) : "")})");
        float leadF = worlds.Count(w => w.Culture.Lead == "female") / (float)worlds.Count;
        Check(MathF.Abs(leadF - 0.5f) < 0.06f, $"side A is female on {Pct(leadF)} of worlds (half)");
        // The world's own leans show: warlords arm everyone more; machines that still run do the work.
        // (Among worlds whose rule is off today's: where the world's leans show.)
        float Share(Func<WorldRoll, bool> where, LayerKind k, string id)
        {
            var some = worlds.Where(where).Where(w => w.Culture.Values.TryGetValue(k, out var v) && !v.Blank).ToList();
            return some.Count == 0 ? float.NaN : some.Count(w => w.Culture.Id(k) == id) / (float)some.Count;
        }
        float warlords = Share(w => w.Rolled.Any(l => l.Kind == LayerKind.Presence && l.Id is "warlords" or "gangs" or "militias" or "bandit_riders"), LayerKind.Reign, "warband");
        float calm = Share(w => !w.Rolled.Any(l => l.Kind == LayerKind.Presence && l.Id is "warlords" or "gangs" or "militias" or "bandit_riders"), LayerKind.Reign, "warband");
        Check(warlords > 1.5f * calm, $"worlds of warlords and gangs are ruled by a warband more ({Pct(warlords)} against {Pct(calm)})");
        var desperate = worlds.OrderBy(w => w.Desperation).ToList();
        float low = desperate.Take(desperate.Count / 3).Average(w => w.Desperation), high = desperate.Skip(2 * desperate.Count / 3).Average(w => w.Desperation);
        float falling = worlds.Where(w => w.Fact("phase") == "falling").DefaultIfEmpty().Average(w => w?.Desperation ?? 0f), steady = worlds.Where(w => w.Fact("phase") != "falling").Average(w => w.Desperation);
        Check(high > low + 0.25f && falling > steady + 0.15f, $"desperation runs from {low:F2} (the easiest third) to {high:F2} (the worst), {falling:F2} on falling worlds against {steady:F2}");
    }

    void Strength(List<WorldRoll> worlds)
    {
        GD.Print("-- strength and extremes --");
        int n = worlds.Count;
        float near = worlds.Count(w => w.Culture.OffDefault <= 2) / (float)n;
        var rest = worlds.Where(w => w.Culture.OffDefault > 2).ToList();
        float strong = rest.Count(w => w.Culture.OffDefault is >= 4 and <= 7) / (float)Math.Max(1, rest.Count);
        Check(MathF.Abs(near - WorldRoll.NearDefault) <= 0.08f, $"{Pct(near)} of settled worlds near today's ways (2 axes off or fewer; aim 30%)");
        Check(strong >= 0.9f, $"{Pct(strong)} of the rest 4 to 6 axes off (a forced one more at most)");
        float one = worlds.Count(w => w.Culture.Extremes >= 1) / (float)n, two = worlds.Count(w => w.Culture.Extremes >= 2) / (float)n;
        Check(MathF.Abs(one - WorldRoll.OneExtreme) <= 0.4f * WorldRoll.OneExtreme, $"{Pct(one)} of worlds have an extreme (aim 1 in 8, 12.5%)");
        Check(two > 0f && two <= 0.04f, $"{Pct(two)} have two (aim 1 in 60)");
        float secluded = worlds.Count(w => w.Culture.Id(LayerKind.Sexes) == "secluded") / (float)n;
        Check(secluded <= 1f / 30f, $"one sex secluded on {Pct(secluded)} of worlds (at most 1 in 30)");
        GD.Print($"     for example: {worlds[0].Culture}");
    }

    void Towns(List<WorldRoll> worlds)
    {
        GD.Print("-- towns drift from their world --");
        int kept = 0, axes = 0, keptHub = 0, axesHub = 0, broken = 0;
        foreach (WorldRoll w in worlds.Take(300))
            for (int t = 0; t < 6; t++)
            {
                bool hub = t == 5;
                CultureReading town = w.Culture.At(_lib, Seed(), hub: hub);
                var facts = new Dictionary<string, string>(w.Facts);
                foreach (var (k, v) in town.Values) facts[k.ToString().ToLowerInvariant()] = v.Id;
                foreach (var (k, v) in town.Values)
                {
                    if (!v.Req.All(q => q.Holds(facts))) broken++;
                    bool same = w.Culture.Id(k) == v.Id;
                    if (hub) { axesHub++; if (same) keptHub++; }
                    else { axes++; if (same) kept++; }
                }
            }
        float keep = kept / (float)axes, keepHub = keptHub / (float)axesHub;
        Check(MathF.Abs(keep - 0.82f) <= 0.08f, $"a town keeps its world's value on {Pct(keep)} of axes (80%, a little more where a step is ruled out)");
        Check(keepHub < keep - 0.05f, $"a crossroads drifts more: {Pct(keepHub)} kept");
        Check(broken == 0, $"no town's culture breaks a rule ({broken})");
    }

    void Factions()
    {
        GD.Print("-- factions keep their own --");
        WorldRoll w = Roll(Seed(), "Temperate");
        foreach (WorldLayer set in _lib.Layers.Where(l => l.Kind == LayerKind.Culture))
        {
            var faction = Enum.Parse<Faction>(set.Id, true);
            int kept = 0, total = 0;
            // (Only what this world allows: no machines doing the work on a world that can't build them.)
            bool Allowed(LayerKind k, string ids) => ids.Split(',').Any(id => _lib.Layers.First(l => l.Kind == k && l.Id == id).Req.All(q => q.Holds(w.Facts) || CultureReading.Axes.Any(a => a.ToString().ToLowerInvariant() == q.Fact)));
            for (int i = 0; i < 200; i++)
            {
                CultureReading c = w.Culture.At(_lib, Seed(), faction);
                foreach (var (axis, ids) in set.Sets)
                {
                    if (!Allowed(Enum.Parse<LayerKind>(axis, true), ids)) continue;
                    total++;
                    if (ids.Split(',').Contains(c.Id(Enum.Parse<LayerKind>(axis, true)))) kept++;
                }
            }
            Check(kept / (float)total >= 0.85f, $"{set.Id} keeps its set on {Pct(kept / (float)total)} of axes over 200 places (drift 0.1)");
        }
    }

    // ---- People ------------------------------------------------------------------------------------

    static WorldConditions Conditions(WorldRoll roll, ulong seed, string arch)
    {
        WorldReading r = WorldReading.Of(roll);
        var w = new WorldConditions { Seed = seed, Alienness = 0.25f, Coreward = Math.Max(0f, roll.Depth), Mutagen = r.Mutagen, TempC = arch switch { "Ice" => -15f, "Arid" => 34f, _ => 15f } };
        WildlifePatch.Story(w, roll, null, r);
        return w;
    }

    WildlifeData _data;

    /// <summary>A fresh settled world with some of its culture forced, its conditions, and its people's generator.</summary>
    (WorldRoll Roll, WorldConditions World, PeopleGenerator Gen) Forced(string arch, params (LayerKind Kind, string Id)[] values) => Forced(Seed(), arch, values);

    (WorldRoll Roll, WorldConditions World, PeopleGenerator Gen) Forced(ulong seed, string arch, params (LayerKind Kind, string Id)[] values)
    {
        var given = new WorldRoll.Given();
        foreach (var (k, id) in values) given.Set(k, id);
        WorldRoll roll = Roll(seed, arch, given);
        WorldConditions w = Conditions(roll, roll.Culture.Seed, arch);
        return (roll, w, new PeopleGenerator(_data.People, w));
    }

    static readonly PersonRole[] Crowd = { PersonRole.Guard, PersonRole.Lawman, PersonRole.Trader, PersonRole.Worker, PersonRole.Mechanic, PersonRole.Farmer, PersonRole.Medic, PersonRole.Elder, PersonRole.Scavenger, PersonRole.Worker };

    List<Figure> People(PeopleGenerator gen, int n, Func<int, PersonRole> role = null) =>
        Enumerable.Range(0, n).Select(i => gen.Person(Seed(), role?.Invoke(i) ?? Crowd[i % Crowd.Length]).Figure).ToList();

    void People(List<WorldRoll> worlds)
    {
        GD.Print("-- who does which job --");
        _data = DataFiles.LoadWildlife();
        float SideShare((WorldRoll Roll, WorldConditions World, PeopleGenerator Gen) f, PersonRole role)
        {
            Culture cu = f.World.Culture;
            Sex side = cu.SexOf(cu.GroupA(Culture.GroupOf(role)));
            return People(f.Gen, 400, _ => role).Count(p => p.Sex == side) / 400f;
        }
        var split = Forced("Temperate", (LayerKind.Sexes, "split"));
        var same = Forced("Temperate", (LayerKind.Sexes, "same"));
        float s1 = SideShare(split, PersonRole.Guard), s2 = SideShare(split, PersonRole.Worker), s0 = SideShare(same, PersonRole.Guard);
        Check(s1 > 0.9f && s2 > 0.9f && MathF.Abs(s0 - 0.5f) < 0.08f, $"roles split by sex: {Pct(s1)} of guards and {Pct(s2)} of workers from their group's side (95%); {Pct(s0)} where roles go by chance (50%)");
        Dress();
        Arms(worlds);
        Tempers();
    }

    /// <summary>How many hues a crowd's clothes fall into (each holding 5% of it or more; greys left out).</summary>
    static int Clusters(IEnumerable<Figure> crowd)
    {
        var hues = crowd.Select(f => f.Cloth).Where(c => c.S > 0.12f).Select(c => c.H).OrderBy(h => h).ToList();
        if (hues.Count == 0) return 0;
        var groups = new List<int> { 1 };
        for (int i = 1; i < hues.Count; i++)
            if (hues[i] - hues[i - 1] > 0.05f) groups.Add(1);
            else groups[^1]++;
        // Round the wheel: the last group and the first are one where they meet.
        if (groups.Count > 1 && hues[0] + 1f - hues[^1] <= 0.05f) { groups[0] += groups[^1]; groups.RemoveAt(groups.Count - 1); }
        return groups.Count(g => g >= 0.05f * hues.Count);
    }

    void Dress()
    {
        GD.Print("-- dress --");
        foreach (var (garb, want) in new[] { ("uniform", 1), ("by_sex", 2), ("by_caste", 3) })
        {
            var f = garb == "by_caste" ? Forced("Temperate", (LayerKind.Garb, garb), (LayerKind.Caste, "castes"), (LayerKind.Sexes, "same"))
                : Forced("Temperate", (LayerKind.Garb, garb), (LayerKind.Sexes, "split"));
            var crowd = People(f.Gen, 120).Where(p => p.Stage == LifeStage.Adult).ToList();
            int got = Clusters(crowd);
            Check(got == want, $"garb {garb}: {got} colour clusters in a crowd of {crowd.Count} (want {want})");
        }
        // Dressed as they like: their colours spread far wider than a uniform's, on the same world.
        ulong seed = Seed();
        float Spread(string garb)
        {
            var crowd = People(Forced(seed, "Temperate", (LayerKind.Garb, garb)).Gen, 80).Select(p => p.Cloth).ToList();
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < crowd.Count; i++)
                for (int j = i + 1; j < crowd.Count; j++, n++)
                    sum += MathF.Abs(crowd[i].R - crowd[j].R) + MathF.Abs(crowd[i].G - crowd[j].G) + MathF.Abs(crowd[i].B - crowd[j].B);
            return sum / n;
        }
        float free = Spread("free"), uniform = Spread("uniform");
        Check(free >= 1.3f * uniform, $"dressed as they like, their colours spread {free:F3} apart on average against a uniform's {uniform:F3}");
        // The decency piece on every grown-up it's for, outdoors.
        // (On clean air: a hazard hood or a suit's helmet wins the head.)
        var hood = Forced("Temperate", (LayerKind.Cover, "hood"));
        hood.World.Mutagen = hood.World.Radiation = 0f;
        var grown = People(new PeopleGenerator(_data.People, hood.World), 200).Where(p => p.Stage != LifeStage.Child && hood.World.Culture.Covers(p.Sex, false)).ToList();
        float hooded = grown.Count(p => p.Wear.Contains("hood")) / (float)Math.Max(1, grown.Count);
        Check(grown.Count > 40 && hooded >= 0.97f, $"a hood decency asks for: on {Pct(hooded)} of the {grown.Count} grown-ups it's for");
        // The air wins: suited in vacuum whatever the culture, which shows as the suit's colour.
        var airless = Forced("Lava", (LayerKind.Garb, "uniform"));
        airless.World.Vacuum = true;
        airless.World.Oxygen = 0f;
        var gen = new PeopleGenerator(_data.People, airless.World);
        var suits = People(gen, 80);
        Check(suits.All(p => p.Dress == "suit") && Clusters(suits) == 1, $"in vacuum all {suits.Count} in suits, one colour of suit ({Clusters(suits)})");
        var foul = Forced("Temperate", (LayerKind.Cover, "hood"));
        foul.World.Oxygen = 10f;
        var masked = People(new PeopleGenerator(_data.People, foul.World), 80).Where(p => p.Stage != LifeStage.Child).ToList();
        Check(masked.All(p => p.Wear.Any(w => HumanBody.Slot(w) == "mouth")), "where the air is bad every grown-up still has a mask on");
    }

    void Arms(List<WorldRoll> worlds)
    {
        GD.Print("-- who carries arms --");
        bool Armed(Figure f) => Weapons.Armed(f.Prop);
        foreach (string arms in new[] { "everyone", "side_a", "fighters", "few", "none" })
        {
            var f = Forced("Temperate", (LayerKind.Arms, arms), (LayerKind.Sexes, "split"));
            Culture cu = f.World.Culture;
            var crowd = People(f.Gen, 600).Where(p => p.Stage != LifeStage.Child).ToList();
            var fighters = crowd.Where(p => p.Role is PersonRole.Guard).ToList();
            // (Not the jobs that carry a weapon anyway.)
            var others = crowd.Where(p => !PeopleGenerator.Fighter(p.Role, Occupancy.Friendly) && !Weapons.Armed(_data.People.Roles.GetValueOrDefault(p.Role.ToString())?.Prop ?? "")).ToList();
            float fight = fighters.Count(Armed) / (float)fighters.Count;
            var a = others.Where(p => cu.A(p.Sex)).ToList();
            var b = others.Where(p => !cu.A(p.Sex)).ToList();
            float sa = a.Count(Armed) / (float)Math.Max(1, a.Count), sb = b.Count(Armed) / (float)Math.Max(1, b.Count);
            float wantFight = cu.Trait("fight", 1f), wantA = cu.ArmedChance(PersonRole.Worker, cu.Lead, false, false), wantB = cu.ArmedChance(PersonRole.Worker, Culture.Other(cu.Lead), false, false);
            Check(MathF.Abs(fight - wantFight) <= 0.1f && MathF.Abs(sa - wantA) <= 0.1f && MathF.Abs(sb - wantB) <= 0.1f,
                $"arms {arms}: guards {Pct(fight)} armed ({Pct(wantFight)}), others of side A {Pct(sa)} ({Pct(wantA)}), of side B {Pct(sb)} ({Pct(wantB)})");
            if (arms == "none") Check(People(f.Gen, 300).Count(Armed) == 0, "no weapons at all where the culture has none");
        }
        // Children: armed by how desperate the world is, and more where the culture arms everyone.
        var byWorld = new List<(float Desperation, float Share)>();
        foreach (WorldRoll roll in worlds.OrderBy(_ => _rng.Next()).Take(240))
        {
            WorldConditions w = Conditions(roll, roll.Culture.Seed, "Temperate");
            var gen = new PeopleGenerator(_data.People, w);
            var kids = People(gen, 60, _ => PersonRole.Child).Where(p => p.Age >= 9f).ToList();
            byWorld.Add((roll.Desperation, kids.Count(Armed) / (float)Math.Max(1, kids.Count)));
        }
        byWorld.Sort((x, y) => x.Desperation.CompareTo(y.Desperation));
        int third = byWorld.Count / 3;
        float easy = byWorld.Take(third).Average(t => t.Share), hard = byWorld.Skip(2 * third).Average(t => t.Share);
        Check(hard >= 0.05f && hard >= 3f * easy, $"armed children: {Pct(easy)} on the easiest third of worlds, {Pct(hard)} on the most desperate (3x or more)");
        ulong world = Seed();
        string seen = "";
        float KidShare(string arms)
        {
            var f = Forced(world, "Temperate", (LayerKind.Arms, arms), (LayerKind.Phase, "falling"));
            seen += $" [{f.World.Culture}]";
            var kids = People(f.Gen, 400, _ => PersonRole.Child).Where(p => p.Age >= 9f).ToList();
            return kids.Count(Armed) / (float)kids.Count;
        }
        float all = KidShare("everyone"), some = KidShare("fighters");
        Check(all > some + 0.05f, $"on falling worlds a culture that arms everyone arms its children more: {Pct(all)} against {Pct(some)}{seen}");
    }

    void Tempers()
    {
        GD.Print("-- who runs hot --");
        var f = Forced("Temperate", (LayerKind.Fierce, "one_side"), (LayerKind.Sexes, "split"));
        Culture cu = f.World.Culture;
        bool hotA = cu.GroupA("hot");
        var crowd = Enumerable.Range(0, 600).Select(i => f.Gen.Person(Seed(), Crowd[i % Crowd.Length])).Where(s => s.Figure.Stage == LifeStage.Adult).ToList();
        float Hot(IEnumerable<Species> some) => some.Count(s => s.Temperament is Temperament.Aggressive or Temperament.Bold) / (float)Math.Max(1, some.Count());
        float hot = Hot(crowd.Where(s => cu.A(s.Figure.Sex) == hotA)), cold = Hot(crowd.Where(s => cu.A(s.Figure.Sex) != hotA));
        Check(hot >= 2f * cold && hot > 0.3f, $"one side runs hot: {Pct(hot)} of the hot side bold or aggressive, {Pct(cold)} of the other");
        var even = Forced("Temperate", (LayerKind.Fierce, "even"));
        float today = Hot(Enumerable.Range(0, 300).Select(i => even.Gen.Person(Seed(), Crowd[i % Crowd.Length])));
        Check(today == 0f, $"tempers as they come: friendly townsfolk calm as today ({Pct(today)} hot)");
    }

    /// <summary>
    /// Settlements of fresh worlds with their culture forced, their people walked through a day as in play
    /// (Town.Rehearse): two talking stand at the culture's distance, the sexes mix as it says, and children keep
    /// within reach of whoever minds them, of the right side, or run in packs of their own.
    /// </summary>
    void Spacing()
    {
        GD.Print("-- spacing and grouping in towns --");
        Decor decor = Decor.Shared;
        var settled = _lib.Families.Where(f => f.Tags.Contains("settlement")).ToList();
        var cases = new (string Name, (LayerKind, string)[] Values)[]
        {
            ("close, roles by chance", new[] { (LayerKind.Space, "close"), (LayerKind.Sexes, "same"), (LayerKind.Young, "anyone") }),
            ("wide, the sexes apart, children with side A", new[] { (LayerKind.Space, "wide"), (LayerKind.Sexes, "apart"), (LayerKind.Young, "side_a") }),
            ("apart, split, children in packs", new[] { (LayerKind.Space, "apart"), (LayerKind.Sexes, "split"), (LayerKind.Young, "packs"), (LayerKind.Self, "lone") }),
            ("even, children kept close", new[] { (LayerKind.Space, "even"), (LayerKind.Sexes, "same"), (LayerKind.Young, "kept") }),
        };
        foreach (var (name, values) in cases)
        {
            var f = Forced("Temperate", values);
            WorldLife life = Bestiary.Roll(_data, f.World);
            Culture cu = f.World.Culture;
            var chat = new List<float>();
            var pairs = new HashSet<(Town.Person, Town.Person)>();
            int mixed = 0, adultPairs = 0, kids = 0, kept = 0, places = 0;
            float leash = cu.Trait("minder");
            bool packs = cu.Has("packs");
            int side = (int)cu.Trait("minder_side");
            for (int t = 0; t < 12 && places < 5; t++)
            {
                FamilyDef fam = settled[_rng.Next(settled.Count)];
                PoiInstance inst;
                try { inst = PoiFactory.Spawn(_lib, PoiFactory.BuildLayout(_lib, fam, 0, fam.Rarity, Seed()), PoiState.Active, Seed(), null, null); }
                catch (Exception) { continue; }
                var memory = new PlaceMemory(new PoiSite { Instance = inst, Family = inst.Family, Name = inst.Family.Id });
                memory.Rooms = decor.Plan(memory);
                Town town = Town.For(memory);
                if (town == null) continue;
                places++;
                town.Culture = cu;
                var roles = Rng.For(inst.SpawnSeed, "culture-test");
                bool farm = fam.Tags.Contains("farm");
                bool Walking(Town.Person p) => p.Path != null && p.Step < p.Path.Count;
                bool Out(Town.Person p) => !p.Dead && !p.Indoors && !p.Away && p.Figure != null;
                town.Rehearse(240f, 0.1f, _ => false,
                    (p, i) =>
                    {
                        PersonRole job = PeopleGenerator.RoleFor(p.Role, farm, roles);
                        return (life.People.Person(Rng.Mix(inst.SpawnSeed, (ulong)i + 1), job, Occupancy.Friendly, culture: cu), job);
                    },
                    (p, at) =>
                    {
                        if (at < 20f || !Out(p)) return;
                        if (p.Partner is { } q && Out(q))
                        {
                            var key = p.GetHashCode() < q.GetHashCode() ? (p, q) : (q, p);
                            if (pairs.Add(key) && p.Figure.Stage != LifeStage.Child && q.Figure.Stage != LifeStage.Child && !p.Working && !q.Working)
                            {
                                adultPairs++;
                                if (p.Figure.Sex != q.Figure.Sex) mixed++;
                            }
                            // Settled talking, sampled once a second.
                            if (key.Item1 == p && !Walking(p) && !Walking(q) && !p.Sitting && !q.Sitting && (int)(at * 10f) % 10 == 0)
                                chat.Add(new Vector2(p.Pos.X - q.Pos.X, p.Pos.Z - q.Pos.Z).Length());
                        }
                        // (Children out while somebody who could mind them, or another child, is out too.)
                        if (p.Figure.Playful && (leash > 0f || packs) && (int)(at * 10f) % 10 == 0)
                        {
                            var near = town.People.Where(o => o != p && Out(o) && (packs ? o.Figure.Playful : o.Figure.Stage != LifeStage.Child && (side == 0 || o.Figure.Sex == cu.SexOf(side == 1))))
                                .OrderBy(o => o.Pos.DistanceSquaredTo(p.Pos)).FirstOrDefault();
                            if (near != null)
                            {
                                kids++;
                                if (near.Pos.DistanceTo(p.Pos) <= (packs ? 8f : leash + 1.5f)) kept++;
                            }
                        }
                    });
            }
            GD.Print($"     {name}: {places} towns, {chat.Count} chat samples, {adultPairs} pairs of grown-ups, {kids} child samples");
            float want = cu.Trait("chat", 1.1f), median = chat.Count > 0 ? chat.OrderBy(d => d).ElementAt(chat.Count / 2) : float.NaN;
            Check(chat.Count >= 10 && MathF.Abs(median - want) <= 0.15f * want, $"{name}: two talking stand {median:F2} m apart (median; the culture's {want:F2} m)");
            float mix = adultPairs > 0 ? mixed / (float)adultPairs : float.NaN;
            float wantMix = cu.Trait("mix", 1f);
            // (Within 35 to 65% where the sexes mix freely, under 5% where they keep apart; a sample this size allowed its spread.)
            float sd = MathF.Sqrt(0.25f / Math.Max(1, adultPairs));
            if (adultPairs >= 8 && (wantMix >= 0.99f || wantMix <= 0.05f))
                Check(wantMix >= 0.99f ? MathF.Abs(mix - 0.5f) <= MathF.Max(0.15f, 2.5f * sd) : mixed <= 0.05f * adultPairs + 1, $"{name}: {Pct(mix)} of {adultPairs} grown-ups' talks off work are between the sexes ({(wantMix >= 0.99f ? "35 to 65%" : "under 5%, one pair allowed")})");
            else GD.Print($"     {name}: {Pct(mix)} of {adultPairs} pairs of grown-ups of both sexes");
            if (kids >= 20)
                Check(kept >= 0.9f * kids, $"{name}: {Pct(kept / (float)kids)} of child samples {(packs ? "within 8 m of another of their pack" : $"within {leash + 1.5f:F1} m of a grown-up minding them{(side > 0 ? " of their side" : "")}")} (90%)");
        }
    }
}
