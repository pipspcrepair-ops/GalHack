using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// What you meet: fresh temperate worlds, each forced to one fall or theme (a biohazard, a nuclear
/// war, a machine uprising, a civil war, a plague, a parade world, a living reserve and a world eaten
/// by grey goo), each started as a game starts and walked for ten minutes of game time from the
/// bunker. Everything that comes within 100 m and is drawn is counted once, by what it is: the
/// world's own kinds (what its story left), its places' holders round their places, its Husks, the
/// beasts its layers changed (twisted, loose, gone feral), the plain wild, and townsfolk. Printed per
/// world, with when the world's own first turned up and the longest wait between them.
///
/// Run: godot --headless --path prototypes/slice --fixed-fps 60 res://tests/OwnSweep.tscn [-- --worlds=biohazard,plague]
/// </summary>
public partial class OwnSweep : Node
{
    const int WatchdogMinutes = 75;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    static readonly (string Label, WorldForce Force)[] All =
    {
        ("biohazard", new WorldForce { Fall = "biohazard" }),
        ("nuclear", new WorldForce { Fall = "nuclear_war" }),
        ("machine", new WorldForce { Fall = "machine_uprising" }),
        ("war", new WorldForce { Fall = "civil_war" }),
        ("plague", new WorldForce { Fall = "plague" }),
        ("parade", new WorldForce { Theme = "parade_ground", Fall = "slow_fade" }),
        ("living", new WorldForce { Theme = "nature_reserve", Fall = "slow_fade" }),
        ("dead", new WorldForce { Fall = "grey_goo" }),
    };

    /// <summary>Game seconds walked on each world.</summary>
    const float WalkSeconds = 600f;

    readonly Dictionary<string, Result> _results = new();

    sealed class Result
    {
        public readonly Dictionary<string, HashSet<long>> Met = new();
        public readonly List<float> OwnTimes = new();
        public string Kinds = "";
        public int Total => Met.Values.Sum(s => s.Count);
        public int Of(string c) => Met.TryGetValue(c, out var s) ? s.Count : 0;
    }

    public override void _Ready()
    {
        GalHack.Testing.Backstop.Arm(1, WatchdogMinutes * 60);
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    IEnumerable<int> Scenario()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string pick = args.FirstOrDefault(a => a.StartsWith("--worlds=", StringComparison.Ordinal))?["--worlds=".Length..];
        var worlds = pick == null ? All : All.Where(w => pick.Split(',').Contains(w.Label)).ToArray();
        foreach (var (label, force) in worlds)
            foreach (int x in OneWorld(label, force)) yield return x;
        Summary();
    }

    static string Category(Creature c, WorldLife life)
    {
        Species s = c.Species;
        if (life.Signature.Is(s)) return "own";
        if (life.Settlers is { } home && (s == home || home.Hosts?.Contains(s) == true)) return "settlers";
        if (s.Husk) return "husks";
        if (Contains(life.Hollows, s) || Contains(life.Twisted, s) || Contains(life.Machines, s) || Contains(life.Cyborgs, s)) return "holders";
        if (s.Id.Contains("-twisted") || s.Id.Contains("-loose-") || s.Corrupted || s.Figure is { Occupancy: Occupancy.Feral } || s.Mechanical) return "story";
        return "wild";
    }

    static bool Contains(IReadOnlyList<Species> list, Species s)
    {
        foreach (Species x in list) if (x == s || x == s.Adult) return true;
        return false;
    }

    IEnumerable<int> OneWorld(string label, WorldForce force)
    {
        var game = new Game.Game { Interactive = false, World = WorldChoice.Fresh("temperate") with { Force = force } };
        AddChild(game);
        yield return 0;
        GD.Print($"-- {label}: {game.World} --");
        var watch = Stopwatch.StartNew();
        while (game.Phase != Phase.Playing && watch.Elapsed.TotalSeconds < 180) yield return 0;
        if (game.Phase != Phase.Playing)
        {
            Check(false, $"{label}: the game never started");
            game.QueueFree();
            yield break;
        }
        Surface.WildlifePatch patch = game.Zone.Wildlife;
        WorldLife life = patch.Life;
        var r = new Result { Kinds = string.Join(", ", life.Signature.Kinds.Select(k => k.Species.CommonName)) };
        _results[label] = r;
        GD.Print($"  fall {life.World.Fall} [{life.World.FallGroup}], theme {life.World.Theme}, era {life.World.Before}/{life.World.Now}, people {(life.World.Peopled ? "yes" : "none")}; livable {patch.Livable:F2}; layers: {game.Zone.Planet.Reading}");
        GD.Print($"  its own: {(r.Kinds == "" ? "none" : r.Kinds)}; wild kinds {life.Fauna.Species.Count}; haunts {patch.Haunts.Count}; anchors {patch.AnchorCounts}");

        var drive = new Driver(game);
        var play = new Playthrough(game);
        Slice.Start.Bunker b = game.Bunker;
        foreach (int x in drive.Tap(Key.F)) yield return x;
        foreach (int x in drive.Wait(40)) yield return x;
        foreach (int x in drive.WalkTo(b.CryoFront, 0.3f, 10f)) yield return x;
        foreach (int x in drive.ClimbOut(b)) yield return x;
        float start = patch.Sim.Time;
        Vector3 facing = b.Outside - b.StairTop;
        GD.Print($"  out: nearest haunts {patch.NearestHaunts()}; homes lived in {patch.HomesLived}");
        int tick = 0, leg = 0;
        var firstSeen = new Dictionary<long, float>();
        while (patch.Sim.Time - start < WalkSeconds && leg < 40)
        {
            Vector3 from = game.Survivor.WorldFeet;
            Vector3 to = play.AwayFrom(from, facing, 200f);
            facing = (to - from).Rotated(Vector3.Up, leg % 2 == 0 ? 0.7f : -0.9f);
            leg++;
            foreach (int x in drive.WalkTo(to, 4f, 240f, sprint: false, dig: false))
            {
                if (++tick % 30 == 0)
                {
                    float t = patch.Sim.Time - start;
                    Vector3 feet = patch.PatchLocal(game.Survivor.WorldFeet);
                    foreach (Creature c in patch.Sim.Creatures)
                    {
                        if (c.Removed || c.Dead || WildSim.Flat(c.Position - feet).Length() > 100f) continue;
                        if (patch.View.ViewOf(c) is not CreatureView { Visible: true } v || v.Shows < 0.5f) continue;
                        string cat = Category(c, life);
                        if (!r.Met.TryGetValue(cat, out var set)) r.Met[cat] = set = new HashSet<long>();
                        if (set.Add(c.Id) && cat == "own") r.OwnTimes.Add(t);
                    }
                    foreach (Places.Place p in game.Places.Standing)
                        if (p.Memory.Town is { } town)
                            for (int i = 0; i < town.People.Count; i++)
                            {
                                var q = town.People[i];
                                if (q.Dead || q.Indoors || q.Gone || town.World(q.Pos).DistanceTo(game.Survivor.WorldFeet) > 100f) continue;
                                if (!r.Met.TryGetValue("townsfolk", out var set)) r.Met["townsfolk"] = set = new HashSet<long>();
                                set.Add(((long)p.Inst.SpawnSeed & 0xFFFFFFF) * 1000 + i);
                            }
                }
                if (patch.Sim.Time - start >= WalkSeconds) break;
                yield return x;
            }
        }
        float walked = patch.Sim.Time - start;
        GD.Print($"  at the end: nearest haunts {patch.NearestHaunts()}; homes given their people {patch.HomesFilled}");
        var gaps = new List<float>();
        float last = 0f;
        foreach (float t in r.OwnTimes.OrderBy(t => t)) { if (t - last > 20f) gaps.Add(t - last); last = t; }
        if (r.OwnTimes.Count > 0) gaps.Add(walked - last);
        string cats = string.Join(", ", new[] { "own", "settlers", "holders", "husks", "story", "wild", "townsfolk" }.Select(c => $"{c} {r.Of(c)}"));
        GD.Print($"  {walked / 60f:F1} min walked ({leg} legs): met {r.Total} ({cats}); the world's own first at {(r.OwnTimes.Count > 0 ? $"{r.OwnTimes.Min() / 60f:F1} min" : "never")}, longest wait {(gaps.Count > 0 ? gaps.Max() / 60f : walked / 60f):F1} min; brought to haunts: {string.Join(" ", patch.OwnBrought.Select(kv => $"{kv.Key} {kv.Value}"))}");
        if (r.Kinds != "")
        {
            int storyMet = r.Of("own") + r.Of("holders") + r.Of("husks") + r.Of("story");
            int beings = r.Total - r.Of("townsfolk");
            float share = beings == 0 ? 0f : storyMet / (float)beings;
            Check(r.Of("own") >= 4 && share >= 0.3f && r.OwnTimes.Count > 0 && r.OwnTimes.Min() < 240f && (gaps.Count == 0 || gaps.Max() < 300f),
                $"{label}: the world's own ({r.Kinds}) met: {r.Of("own")}, and its story is {share:P0} of all met; first at {(r.OwnTimes.Count > 0 ? r.OwnTimes.Min() / 60f : 99):F1} min");
        }
        game.QueueFree();
        for (int i = 0; i < 10; i++) yield return 0;
    }

    void Summary()
    {
        GD.Print("-- the sweep --");
        foreach (var (label, r) in _results)
        {
            int beings = r.Total - r.Of("townsfolk");
            int story = r.Of("own") + r.Of("holders") + r.Of("husks") + r.Of("story");
            GD.Print($"  {label,-10} met {r.Total,3}: own {r.Of("own"),3}, settlers {r.Of("settlers"),2}, holders {r.Of("holders"),2}, husks {r.Of("husks"),2}, story {r.Of("story"),2}, wild {r.Of("wild"),3}, townsfolk {r.Of("townsfolk"),2}; the world's {(beings == 0 ? 0 : story * 100 / beings),3}% | {r.Kinds}");
        }
        if (_results.TryGetValue("living", out var living) && _results.TryGetValue("dead", out var dead))
            Check(living.Of("wild") > dead.Of("wild") * 2 + 3, $"a living world teems where a dead one is quiet: {living.Of("wild")} wild met against {dead.Of("wild")}");
        var kinds = _results.Where(kv => kv.Value.Kinds != "").Select(kv => kv.Value.Kinds).ToList();
        Check(kinds.Distinct().Count() == kinds.Count, "each world's own kinds differ from every other's");
    }
}
