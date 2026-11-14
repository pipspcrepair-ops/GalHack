using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Wildlife;
using PInt3 = PoiGenerator.Gen.Int3;
using WNav = Wildlife.NavGrid;
using WTerrain = Wildlife.Terrain;

namespace Slice.Tests;

/// <summary>
/// The places nobody marked: over fresh worlds, what lives in the sewers and drains under the start town (people first,
/// threats, then a few vermin: counted by role and by kind), that none of it is in a sealed pocket and every cell keeps
/// its budget; that the sections are shaped (no square step left in an arched bore, every slope held by a wall, every face
/// coloured); what lives about the world's ruins and wrecks; and what all of it costs.
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/IncidentalTest.tscn  (-- --seeds N)
/// </summary>
public partial class IncidentalTest : Node
{
    const int WatchdogMinutes = 25;
    const int WorldsPerRun = 3;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    int _withTown, _people, _threats, _ambient, _cells, _pockets, _ruinsFilled, _wrecksFilled;
    readonly Dictionary<string, int> _kinds = new();

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e) { Check(false, $"scenario threw: {e}"); more = false; }
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
        int random = WorldsPerRun;
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--seeds") int.TryParse(args[i + 1], out random);
        var worlds = new List<WorldChoice>();
        if (WorldChoice.FromArgs(args) is WorldChoice only) worlds.Add(only);
        else
        {
            for (int i = 0; i < random; i++) worlds.Add(WorldChoice.Fresh("random"));
            worlds.Add(WorldChoice.Fresh("temperate"));
        }
        foreach (WorldChoice w in worlds)
            foreach (int x in OneWorld(w)) yield return x;
        Check(_withTown > 0, $"some world had a town with an underground to look at ({_withTown} of {worlds.Count})");
        Check(_withTown == 0 || _cells > 0 && _people + _threats + _ambient > 0, $"people {_people}, threats {_threats}, vermin {_ambient} in {_cells} cells of the network; {_pockets} sealed pockets left empty");
        GD.Print($"  by kind: {string.Join(", ", _kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}");
        Check(_ruinsFilled + _wrecksFilled > 0 || worlds.Count == 0, $"ruins picked over {_ruinsFilled}, wrecks picked over {_wrecksFilled} (creatures found within reach of one, over the worlds)");
    }

    IEnumerable<int> OneWorld(WorldChoice pinned)
    {
        var game = new Game.Game { Interactive = false, World = pinned, SkipIntro = true };
        AddChild(game);
        yield return 0;
        WorldChoice world = game.World;
        GD.Print($"-- {world} --");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!game.Zone.IsBuilt && watch.Elapsed.TotalSeconds < 180) yield return 0;
        string tag = $"{world.Name}, seed {world.Seed}";
        if (!game.Zone.IsBuilt) { Check(false, $"{tag}: the world never built"); game.QueueFree(); yield return 0; yield break; }
        var planet = game.Zone.Planet;
        game.Zone.Wildlife.Frozen = false;
        UnderBuilt built = planet.StartTown?.Under?.Built;
        if (built == null) GD.Print($"  (no underground on {tag})");
        else
        {
            _withTown++;
            foreach (int x in Sewers(game, built, tag)) yield return x;
            foreach (int x in Looks(planet.StartTown.Under, built, tag)) yield return x;
            foreach (int x in Standing(game, built, tag)) yield return x;
        }
        foreach (int x in Outdoors(game, tag)) yield return x;
        game.QueueFree();
        for (int i = 0; i < 10; i++) yield return 0;
    }

    /// <summary>Every cell of the network filled: roles, kinds, budget, and nothing in a sealed pocket.</summary>
    IEnumerable<int> Sewers(Game.Game game, UnderBuilt built, string tag)
    {
        var deep = game.Places.Fauna;
        HashSet<PInt3> air = IncidentalFauna.OpenAir(built);
        int pocket = built.Open.Count(c => !air.Contains(c));
        _pockets += pocket;
        var all = new List<IncidentalFauna.Made>();
        bool budget = true;
        foreach (var cell in IncidentalFauna.Cells(built, air).ToList())
        {
            var anchor = new Vector3I(cell.X * IncidentalFauna.Cell + IncidentalFauna.Cell / 2, (built.Min.Y + built.Max.Y) / 2, cell.Z * IncidentalFauna.Cell + IncidentalFauna.Cell / 2);
            var space = IncidentalFauna.Space(built, anchor);
            var terrain = new WTerrain((x, z) => 40f, 40f);
            var sim = new WildSim(deep.Data, terrain, new WNav(terrain.HalfSize, 3f));
            List<IncidentalFauna.Made> made;
            lock (deep.Lock) made = IncidentalFauna.Fill(sim, built, air, space, anchor, cell, deep, planet(game).Spec.Seed ^ 0x5E3E, _ => true);
            _cells++;
            budget &= made.Count <= IncidentalFauna.Budget
                && made.Count(m => m.Role == IncidentalFauna.Role.People) <= IncidentalFauna.PeopleMax
                && made.Count(m => m.Role == IncidentalFauna.Role.Threat) <= IncidentalFauna.ThreatMax
                && made.Count(m => m.Role == IncidentalFauna.Role.Ambient) <= IncidentalFauna.AmbientMax;
            all.AddRange(made);
            yield return 0;
        }
        _people += all.Count(m => m.Role == IncidentalFauna.Role.People);
        _threats += all.Count(m => m.Role == IncidentalFauna.Role.Threat);
        _ambient += all.Count(m => m.Role == IncidentalFauna.Role.Ambient);
        foreach (var m in all) _kinds[$"{m.Role}/{m.Net}/{m.Creature.Species.Name}"] = _kinds.GetValueOrDefault($"{m.Role}/{m.Net}/{m.Creature.Species.Name}") + 1;
        Check(budget, $"{tag}: every cell keeps its budget ({IncidentalFauna.Budget}: people {IncidentalFauna.PeopleMax}, threats {IncidentalFauna.ThreatMax}, vermin {IncidentalFauna.AmbientMax})");
        int inPocket = all.Count(m => !air.Contains(m.Cell) || !built.Open.Contains(m.Cell));
        Check(inPocket == 0, $"{tag}: nobody in a sealed pocket or in rock ({all.Count} creatures, {pocket} sealed open cells)");
        Check(all.Count(m => m.Role == IncidentalFauna.Role.Ambient) > 0 || air.Count == 0, $"{tag}: something lives in the drains ({all.Count} in {_cells} cells so far: {string.Join(", ", all.GroupBy(m => m.Net).Select(g => $"{g.Key} {g.Count()}"))})");
    }

    static PlanetSurface.World.PlanetWorld planet(Game.Game g) => g.Zone.Planet;

    /// <summary>The sections: no square step in an arched bore's roof, every slope held by a wall, every block coloured; and what the dressing costs to build.</summary>
    IEnumerable<int> Looks(PlanetSurface.Infra.UnderTown under, UnderBuilt built, string tag)
    {
        int steps = 0, slopes = 0, loose = 0, bare = 0;
        foreach (UnderRun r in built.Plan.Runs.Where(r => r.Arched))
        {
            var (lx, lz) = (Math.Abs(r.DirZ), Math.Abs(r.DirX));
            for (int i = 0; i < r.Length; i++)
            {
                var (x, z) = r.Column(i);
                foreach (int side in new[] { -1, 1 })
                {
                    PInt3 c = new(x + lx * side * r.HalfWidth, r.Floors[i] + r.Height - 1, z + lz * side * r.HalfWidth);
                    if (built.Open.Contains(c) || !built.Blocks.TryGetValue(c, out Block b)) continue;
                    PInt3 wall = c + new PInt3(lx * side, 0, lz * side);
                    if (b.Type == BlockType.Cube && !built.Open.Contains(wall) && built.Blocks.TryGetValue(wall, out Block back) && back.Type == BlockType.Cube) steps++;
                }
            }
        }
        foreach (var (c, b) in built.Blocks)
        {
            if (b.Type == BlockType.Chamfer)
            {
                slopes++;
                if (!built.Blocks.TryGetValue(c + b.Facing.Vec(), out Block back) || back.Type != BlockType.Cube) loose++;
            }
            if (b.Type != BlockType.Air && b.Color == default) bare++;
        }
        Check(slopes > 0 && steps == 0, $"{tag}: arched bores hold no square step ({steps} left, {slopes} slopes)");
        Check(loose == 0, $"{tag}: every slope is held by a wall behind it ({loose} floating of {slopes})");
        Check(bare == 0, $"{tag}: every block of the network has a colour ({bare} bare)");
        // What it costs to build: the dressing against the bare lining, three builds each.
        double Time(bool rounded)
        {
            Underground.Rounded = rounded;
            double best = double.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                var w = System.Diagnostics.Stopwatch.StartNew();
                Underground.Build(under.Scheme);
                best = Math.Min(best, w.Elapsed.TotalMilliseconds);
            }
            Underground.Rounded = true;
            return best;
        }
        double plain = Time(false), dressed = Time(true);
        GD.Print($"  (building the underground: {plain:F0} ms plain, {dressed:F0} ms rounded and stained)");
        Check(dressed < plain * 1.5 + 50, $"{tag}: shaping the sections adds little to building them ({dressed:F0} ms against {plain:F0} ms)");
        yield return 0;
    }

    /// <summary>The survivor at a way in: the cells near them stand, are drawn and cost little on the main thread.</summary>
    IEnumerable<int> Standing(Game.Game game, UnderBuilt built, string tag)
    {
        if (built.WaysIn.Count == 0) yield break;
        PInt3 way = built.WaysIn[0];
        var feet = new Vector3(way.X + 0.5f, way.Y, way.Z + 0.5f);
        feet = game.Surface.Planet.NearestCopy(feet, game.Survivor.WorldFeet);
        game.Survivor.PlaceOutside(new System.Numerics.Vector3(feet.X, feet.Y, feet.Z));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1800 && game.Underworld.Standing == 0; i++) yield return 0;
        int made = game.Underworld.All.Count();
        Check(game.Underworld.Standing > 0 || made == 0, $"{tag}: a sim stands near the way in ({game.Underworld.Standing} standing, {made} filled in {watch.Elapsed.TotalSeconds:F1} s)");
        for (int i = 0; i < 300; i++) yield return 0;
        Check(game.Underworld.WorstMs < 8.0, $"{tag}: the main thread's look over the network took {game.Underworld.WorstMs:F2} ms at worst");
        Check(game.Underworld.Standing <= 3, $"{tag}: at most three cells stand at once ({game.Underworld.Standing})");
    }

    /// <summary>A wreck and a ruin, the survivor beside each: the creatures that come to pick it over.</summary>
    IEnumerable<int> Outdoors(Game.Game game, string tag)
    {
        var net = game.Surface.Planet.Infra;
        var patch = game.Zone.Wildlife;
        var sites = new List<(string Kind, double X, double Z, float R)>();
        if (net.Wrecks.Count > 0) { var w = net.Wrecks[0]; var (wx, wz) = w.World(w.Width * 0.5f, w.Length * 0.5f); sites.Add(("wreck", wx, wz, MathF.Sqrt(w.Width * w.Width + w.Length * w.Length) * 0.5f)); }
        var ruin = net.Ruins.FirstOrDefault(r => r.Stamp != null && net.Town?.Houses.Contains(r) != true);
        if (ruin != null) { var (rx, rz) = ruin.Center; sites.Add(("ruin", rx, rz, 10f)); }
        foreach (var (kind, x, z, r) in sites)
        {
            var at = game.Surface.Planet.NearestCopy(new Vector3((float)x, 0f, (float)z), game.Survivor.WorldFeet);
            at.Y = game.Surface.Planet.GroundHeight(Planets.Gen.Mathx.WrapX(at.X, game.Surface.Planet.Width), Planets.Gen.Mathx.Wrap(at.Z, game.Surface.Planet.Height)) + 0.5f;
            var stand = at + new Vector3(60f, 0f, 0f);
            stand.Y = game.Surface.Planet.GroundHeight(Planets.Gen.Mathx.WrapX(stand.X, game.Surface.Planet.Width), Planets.Gen.Mathx.Wrap(stand.Z, game.Surface.Planet.Height)) + 0.5f;
            game.Survivor.PlaceOutside(new System.Numerics.Vector3(stand.X, stand.Y, stand.Z));
            int found = 0;
            for (int i = 0; i < 900 && found == 0; i++)
            {
                yield return 0;
                if (i % 30 != 0) continue;
                Vector3 origin = patch.Origin;
                found = patch.Sim.Creatures.Count(c => !c.Removed && !c.Dead && WildSim.Flat(c.Position + origin - at).Length() < r + 30f);
            }
            if (kind == "wreck") _wrecksFilled += found;
            GD.Print($"  ({tag}: {kind} at {x:F0},{z:F0}: {found} creatures within {r + 30f:F0} m; ruins picked so far {patch.RuinsPicked}, ruin cast {patch.Life.RuinCast.Count}, {patch.AnchorCounts})");
        }
        _ruinsFilled += patch.RuinsPicked;
        yield return 0;
    }
}
