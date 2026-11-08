using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Stars;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Start;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The start options across the galaxy. First the gates, over hundreds of fresh rolls: depth runs
/// from the arms' tips to the core and a home asked for at a depth lies near it; the rim never has
/// a faction and a sheltered start nothing rough; Husk worlds, Husk and something-wrong-here places
/// and the wildlife's Husk odds rise with depth. Then the matrix: depth bands (the rim as a default
/// game rolls it, then outer, inner and core) by tech eras, each cell a fresh random world of a
/// random type and fall, mid-collapse or not, started through the real game: it starts, what was
/// forced took, the depth gates hold on it, and the castaway wakes, climbs out, walks off and back.
/// Nothing is pinned: every run rolls new worlds.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/MatrixTest.tscn
/// (-- --cells=N: only the first N cells of the shuffled grid, a quick run; the gate runs them all)
/// </summary>
public partial class MatrixTest : Node
{
    const int WatchdogMinutes = 55;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _rng = new();

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
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

    void Fail(string what) => Check(false, what);

    /// <summary>The bands a cell's depth is drawn from: null is the rim as a default game rolls it (an arm's outer tip).</summary>
    static readonly (string Band, float Lo, float Hi)[] Bands = { ("rim", -1f, -1f), ("outer", 0.3f, 0.45f), ("inner", 0.55f, 0.75f), ("core", 0.85f, 0.97f) };

    /// <summary>The old world's era and the living's, low to high.</summary>
    static readonly (TechLevel Old, TechLevel Now)[] Eras = { (TechLevel.Low, TechLevel.Low), (TechLevel.Mid, TechLevel.Low), (TechLevel.High, TechLevel.Mid) };

    IEnumerable<int> Scenario()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int cells = int.MaxValue;
        foreach (string a in args)
            if (a.StartsWith("--cells=", StringComparison.Ordinal)) int.TryParse(a["--cells=".Length..], out cells);
        PoiLibrary lib = WorldChoice.Library;
        try
        {
            Depths();
            Gates(lib);
            Arguments();
        }
        catch (Exception e) { Fail($"the gates threw: {e}"); }

        var grid = new List<(int Band, int Era)>();
        for (int b = 0; b < Bands.Length; b++)
            for (int e = 0; e < Eras.Length; e++) grid.Add((b, e));
        grid = grid.OrderBy(_ => _rng.Next()).ToList();
        int n = Math.Min(cells, grid.Count);
        GD.Print($"-- the matrix: {n} of {grid.Count} cells (depth band x tech eras), each a fresh world --");
        List<string> falls = WorldForce.Values(lib, LayerKind.Fall), themes = WorldForce.Values(lib, LayerKind.Theme);
        var failed = new List<string>();
        for (int i = 0; i < n; i++)
        {
            var (b, e) = grid[i];
            var band = Bands[b];
            var type = WorldChoice.LayerTypes[_rng.Next(WorldChoice.LayerTypes.Count)];
            var force = new WorldForce
            {
                Depth = band.Lo < 0f ? null : band.Lo + (float)_rng.NextDouble() * (band.Hi - band.Lo),
                Fall = falls[_rng.Next(falls.Count)],
                Theme = _rng.Next(2) == 0 ? themes[_rng.Next(themes.Count)] : null,
                MidCollapse = _rng.Next(2) == 0,
                Before = Eras[e].Old,
                Now = Eras[e].Now,
            };
            var world = WorldChoice.Fresh(type.Id) with { WidthKm = _rng.Next(4) == 0 ? 8 : 16, Force = force };
            int before = _failures;
            foreach (int x in Cell(world, band.Band)) yield return x;
            if (_failures > before) failed.Add($"{band.Band}/{WorldProfile.Tag(Eras[e].Old)}-{WorldProfile.Tag(Eras[e].Now)} {world}");
        }
        GD.Print($"cells: {n - failed.Count} of {n} passed{(failed.Count > 0 ? $" (failed: {string.Join("; ", failed)})" : "")}");
    }

    // ---- The gates, over many rolls ------------------------------------------------------------

    /// <summary>Depth from the galaxy itself: the core is 1, the arms' tips 0, a default home is on the rim, and a home asked for at a depth lies near it.</summary>
    void Depths()
    {
        ulong seed = (ulong)_rng.NextInt64();
        Galaxy g = Galaxy.Make(seed);
        GD.Print($"galaxy seed {seed}");
        Check(Galaxy.Depth(N.Vector3.Zero) == 1f && Galaxy.Depth(new N.Vector3(Galaxy.TipRadius, 0f, 0f)) == 0f && g.Stars.All(s => Galaxy.Depth(s.Position) is >= 0f and <= 1f),
            "depth is 1 at the core, 0 at the arms' tips, and within 0..1 for every star");
        var kinds = new[] { StarKind.YellowDwarf, StarKind.OrangeDwarf, StarKind.RedDwarf, StarKind.WhiteStar };
        float worstRim = 0f, worstNear = 0f;
        string worst = "";
        foreach (StarKind kind in kinds)
        {
            ulong key = (ulong)_rng.NextInt64();
            worstRim = MathF.Max(worstRim, g.DepthOf(g.Home(kind, key, false)));
            foreach (float want in new[] { 0.35f, 0.65f, 0.9f })
            {
                float got = g.DepthOf(g.Home(kind, key, _rng.Next(3) == 0, want));
                if (MathF.Abs(got - want) > worstNear) (worstNear, worst) = (MathF.Abs(got - want), $"{kind} at {want:0.00}: {got:0.00}");
            }
        }
        Check(worstRim < 0.25f, $"a default home lies on the rim at an arm's tip (deepest {worstRim:0.00})");
        Check(worstNear <= 0.08f, $"a home asked for at a depth lies near it (furthest off: {worst})");
        bool agree = true;
        for (int i = 0; i < 200; i++)
        {
            float d = (float)_rng.NextDouble();
            string zone = WorldForce.ZoneAt(d), band = Galaxy.Band(d);
            agree &= (zone, band) is ("rim", "RIM") or ("arm", "OUTER") or ("inner", "INNER") or ("core", "CORE");
        }
        Check(agree, "the galaxy map's bands and the roll's zones split depth at the same places");
    }

    /// <summary>The depth gates over fresh rolls in every band: factions, roughness, Husks and the wrong.</summary>
    void Gates(PoiLibrary lib)
    {
        const int Rolls = 800;
        var archetypes = Enum.GetValues<Planets.Gen.Archetype>();
        var husky = new float[Bands.Length];
        var huskPlaces = new float[Bands.Length];
        var wrong = new float[Bands.Length];
        int rimFactions = 0, roughStarts = 0, deepRough = 0, deepStarts = 0, startFactions = 0;
        for (int b = 0; b < Bands.Length; b++)
        {
            int worlds = 0;
            for (int i = 0; i < Rolls * 2; i++)
            {
                bool start = i % 2 == 1;
                ulong seed = (ulong)_rng.NextInt64();
                string arch = archetypes[_rng.Next(archetypes.Length)].ToString();
                WorldProfile profile = WorldProfile.Roll(seed, arch, start);
                if (!profile.Inhabited) continue;
                float depth = b == 0 ? (float)_rng.NextDouble() * 0.24f : Bands[b].Lo + (float)_rng.NextDouble() * (Bands[b].Hi - Bands[b].Lo);
                WorldRoll roll = WorldRoll.Roll(lib, seed, arch, profile, start, null, depth);
                if (roll == null) continue;
                bool faction = roll.Rolled.Any(l => l.Is.Contains("faction")) || roll.Occupancy.ContainsKey("FAC");
                if (b == 0 && faction) rimFactions++;
                if (start && faction) startFactions++;
                bool rough = roll.Rolled.Any(l => l.Kind != LayerKind.Zone && l.Is.Contains("rough"));
                if (start && b == 0 && rough) roughStarts++;
                if (start && b > 0) { deepStarts++; if (rough) deepRough++; }
                if (start) continue;
                worlds++;
                if (roll.Rolled.Any(l => l.Id is "husks_fall" or "husk_nights")) husky[b]++;
                float all = roll.Occupancy.Values.Sum();
                if (all > 0f)
                {
                    huskPlaces[b] += roll.Occupancy.GetValueOrDefault("HSK") / all;
                    wrong[b] += roll.Occupancy.GetValueOrDefault("HOL") / all;
                }
            }
            husky[b] /= Math.Max(1, worlds);
            huskPlaces[b] /= Math.Max(1, worlds);
            wrong[b] /= Math.Max(1, worlds);
        }
        string Row(float[] v) => string.Join(" / ", v.Select(x => $"{x:P1}"));
        Check(rimFactions == 0, $"no faction on the rim, over {Rolls * 2} rim rolls ({rimFactions})");
        Check(startFactions == 0, $"no faction holds ground on a start world at any depth ({startFactions})");
        Check(roughStarts == 0, $"a start world on the rim rolls nothing rough ({roughStarts})");
        Check(deepRough > 0, $"a start world begun deeper in can roll rough things ({deepRough} of {deepStarts})");
        Check(husky[3] > husky[0] * 1.5f && husky[2] > husky[0] && husky[3] > husky[1], $"Husk worlds grow more common toward the core, rim / outer / inner / core: {Row(husky)}");
        Check(huskPlaces[3] > huskPlaces[0] && huskPlaces[1] > huskPlaces[0] && huskPlaces[3] > huskPlaces[1], $"Husk-held places' share rises with depth: {Row(huskPlaces)}");
        Check(wrong[0] < 0.005f && wrong[3] > wrong[1] && wrong[3] > 0.01f, $"something-wrong-here places rise toward the core: {Row(wrong)}");
        var husks = Slice.Game.GameData.Shared.Wildlife.Husks;
        float[] odds = Bands.Select(band => husks.WorldChanceAt(band.Lo < 0f ? 0.1f : (band.Lo + band.Hi) / 2f)).ToArray();
        Check(odds.Zip(odds.Skip(1)).All(p => p.Second > p.First), $"the wildlife's odds of Husks on a world rise with depth: {Row(odds)}");
    }

    /// <summary>The command line's options, parsed: what is given is forced, the rest left to the roll.</summary>
    void Arguments()
    {
        string fall = WorldForce.Values(WorldChoice.Library, LayerKind.Fall)[0];
        WorldChoice w = WorldChoice.FromArgs(new[] { "--depth", "0.7", "--type", "dune", "--fall", fall, "--mid-collapse", "--era-old", "high", "--era-now=low", "--size", "32x16" });
        Check(w is { Archetype: Planets.Gen.Archetype.Arid, Variant: 0, WidthKm: 32 } && w.Force is { Depth: 0.7f, MidCollapse: true, Before: TechLevel.High, Now: TechLevel.Low, Theme: null } && w.Force.Fall == fall,
            $"the command line forces what it names and rolls the rest: {w}");
        Check(WorldChoice.FromArgs(new[] { "--seed", "42", "--planet", "ice" }) == new WorldChoice(42, Planets.Gen.Archetype.Ice, 0) && WorldChoice.FromArgs(new[] { "--creative" }) == null,
            "a seed and a planet alone force nothing more, and no option pins nothing");
    }

    // ---- One cell ------------------------------------------------------------------------------

    IEnumerable<int> Cell(WorldChoice world, string band)
    {
        WorldForce force = world.Force;
        GD.Print($"-- {band}: {world} --");
        var game = new Game.Game { Interactive = false, World = world };
        AddChild(game);
        yield return 0;
        var watch = Stopwatch.StartNew();
        while (game.Phase != Phase.Playing && watch.Elapsed.TotalSeconds < 150)
        {
            if (game.Zone.IsBuilt && game.Bunker == null) break;
            yield return 0;
        }
        bool started = game.Bunker != null && game.Phase == Phase.Playing;
        Check(started && game.WorldStanding, $"the world starts ({watch.Elapsed.TotalSeconds:F0} s): a start site, awake, everything round it standing");
        if (!started)
        {
            game.QueueFree();
            for (int i = 0; i < 10; i++) yield return 0;
            yield break;
        }
        var planet = game.Zone.Planet;
        WorldRoll roll = planet.Roll;
        float depth = planet.Depth;
        string Id(LayerKind k) => roll?.One(k)?.Id ?? "-";
        GD.Print($"  (depth {depth:0.00} {Galaxy.Band(depth)}, star {planet.Catalogue.Name(planet.HomeStar)}; {roll}; {planet.Profile})");
        // What was forced took.
        Check(planet.Spec.Archetype == world.Archetype && planet.Spec.Variant == world.Variant && planet.Spec.WidthKm == world.WidthKm && Id(LayerKind.Type) == world.TypeId,
            $"the type and size took: {world.TypeId}, {planet.Spec.WidthKm}x{planet.Spec.HeightKm} km");
        Check(force.Depth is float want ? MathF.Abs(depth - want) <= 0.08f : depth < 0.25f, $"the depth took: {depth:0.00} for {(force.Depth is float d ? $"{d:0.00}" : "the rim, rolled")}");
        Check(Id(LayerKind.Zone) == WorldForce.ZoneAt(depth), $"the world rolled the zone of its depth: {Id(LayerKind.Zone)}");
        Check(planet.Profile.Before == force.Before && planet.Profile.Now == force.Now, $"the eras took: {WorldProfile.Tag(planet.Profile.Before)} then, {WorldProfile.Tag(planet.Profile.Now)} now");
        Check(Id(LayerKind.Fall) == force.Fall && (force.Theme == null || roll.Theme.Id == force.Theme), $"the fall{(force.Theme != null ? " and the theme" : "")} took: {Id(LayerKind.Fall)}, {roll?.Theme?.Id}");
        Check(Id(LayerKind.Phase) == "falling" == force.MidCollapse, $"{(force.MidCollapse == true ? "mid-collapse" : "not mid-collapse")}: phase {Id(LayerKind.Phase)}");
        // The depth gates, on this world.
        bool faction = roll.Rolled.Any(l => l.Is.Contains("faction")) || roll.Occupancy.ContainsKey("FAC")
                       || planet.Pois.Any(p => ThemeDef.FactionOwned(p.Family) && p.Instance?.State == PoiState.Active);
        Check(!faction, $"no faction holds ground here ({(depth < 0.25f ? "the rim" : "a start world")})");
        Check(Math.Abs(game.Zone.Wildlife.Life.World.Coreward - depth) < 1e-4f, $"the wildlife knows its depth: coreward {game.Zone.Wildlife.Life.World.Coreward:0.00}");
        // The start is playable: the castaway wakes, climbs out, walks off and comes back in.
        game.Zone.Weather.Bridge.Calm = true;
        game.Zone.Wildlife.Frozen = true;
        var play = new Playthrough(game);
        foreach (int x in play.WakeAndLeave(Check, $"{band}: ")) yield return x;
        Check(!game.Suit.Dead, "the castaway is alive at the end");
        game.QueueFree();
        for (int i = 0; i < 10; i++) yield return 0;
    }
}
