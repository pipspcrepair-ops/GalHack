using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CapitalBase.World;
using GalHack.Physics;
using GalHack.Physics.Engine;
using PhysicsSandbox.Bridges;
using GalHack.Structures;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// Crashed capital ships and orbit's derelicts, one hull builder (HullVessel), redone from the Empyrion study
/// (docs/place-patterns.md 1.4, 5.2; docs/ship-forms.md). First the faction silhouettes, livery off, each hull judged
/// without it. Then every capital site's hull on fresh seeds, built away from the world: its faction's form with its
/// decks fitted inside, damaged, of the player's blocks, at Empyrion's sizes, 4 m decks (2 m only where crushed), rooms
/// dressed and lit for their purpose, corridors about 60% of the floor, machines and containers about 2.7 per
/// 100 m², the engines, power, gyros, warp drive, turrets, cargo and command chairs, no cockpit and no fuel tanks, no
/// core but a seat for one, once repaired nothing missing to fly, and each hull its own. Then on a fresh world with a crashed freighter
/// forced near the start (any of its four poses): it is set down as coreless wrecks nobody waits in; a core set into
/// its core room claims it; Creative fills in what the crash and the years took; in Flight Mode nothing is missing,
/// its thrust beats its weight and it has power; and it lifts off along capital-base's clean lift-off, the ground it
/// lies in cut (within the 20,000 cells) and crumbling away. Last, a start system's derelict capital ship is the same
/// builder's, dressed.
/// </summary>
public partial class HulkTest : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100;
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(25));
            Console.Error.WriteLine("HulkTest took over 25 minutes of wall-clock time: watchdog");
            System.Environment.Exit(2);
        }) { IsBackground = true }.Start();
        _run = Scenarios().GetEnumerator();
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

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"  FAIL  {what}");
    }

    bool Check(bool ok, string what)
    {
        if (ok) { _passes++; GD.Print($"  ok    {what}"); }
        else Fail(what);
        return ok;
    }

    /// <summary>A crashed freighter near the start, abandoned, in one of its four poses (a fresh pick each run unless given). Set before the world is made.</summary>
    public static void Force(int? variant = null)
    {
        PoiSiting.ForceFamily = "crashed_freighter";
        PoiSiting.ForceVariant = variant ?? Random.Shared.Next(4);
        PoiSiting.ForceState = PoiState.Abandoned;
    }

    public static PoiSite SiteOf(Game.Game game)
    {
        var all = game.Zone.Planet.Pois.Where(s => s.Family.Id == "crashed_freighter").ToList();
        PoiSite site = all.Where(s => s.Instance != null && CapitalHull.Of(s.Instance.Layout) != null).OrderBy(s => game.Places.DistanceTo(s, game.Survivor.WorldFeet)).FirstOrDefault();
        if (site == null) GD.PrintErr($"no crashed capital hulk: {all.Count} crashed freighter sites, {all.Count(s => s.Instance != null)} with a place, {all.Count(s => s.Instance != null && CapitalHull.Of(s.Instance.Layout) != null)} with a vessel plan; forced {PoiSiting.ForceFamily}/{PoiSiting.ForceVariant}");
        return site ?? throw new InvalidOperationException("no crashed capital hulk on this world");
    }

    /// <summary>Every block of the plan the wreck lacks, put back as Creative builds (repeatedly, as each needs a neighbour to hang on).</summary>
    public static int Fill(Vessel v, Catalogue catalogue)
    {
        HullVessel.Result hull = v.Derelict.Hull;
        Structure intact = HullVessel.Build(hull.Plan, hull.Piece, catalogue, hull.State, hull.Seed, intact: true).Structure;
        int placed = 0;
        for (int pass = 0; pass < 8; pass++)
        {
            int before = placed;
            foreach (var (p, b) in intact.Blocks().ToList())
                if (!b.IsFiller && !v.Structure.Has(p) && GalHack.Structures.Building.Place(v.Structure, p, b, null, GalHack.Structures.Building.DefaultOwner) == PlaceError.None) placed++;
            if (placed == before) break;
        }
        v.ApplyEdits();
        return placed;
    }

    /// <summary>The catalogue: the world's, or the game's data on its own when the hulls are checked without one.</summary>
    Catalogue Cat => _game?.Zone.Data.Catalogue ?? GameData.Shared.Catalogue;

    IEnumerable<int> Scenarios()
    {
        string only = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--only="))?[7..];
        // The hulls on their own need no world: built away from one, as the game builds them on a worker.
        if (only is "styles" or "fresh" or "hulls")
        {
            if (only is "styles" or "hulls") foreach (int x in Styles()) yield return x;
            if (only is "fresh" or "hulls") foreach (int x in Fresh()) yield return x;
            yield break;
        }
        string variant = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--variant="))?[10..];
        Force(variant != null ? int.Parse(variant) : null);
        _game = new Game.Game { Interactive = false, SkipIntro = true, Mode = GalHack.Items.GameMode.Creative, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        if (only is null or "styles") foreach (int x in Styles()) yield return x;
        if (only is null or "fresh") foreach (int x in Fresh()) yield return x;
        if (only is null or "crashed") foreach (int x in Crashed()) yield return x;
        if (only is null or "adrift") foreach (int x in Adrift()) yield return x;
    }

    IEnumerable<int> Crashed()
    {
        PoiSite site = SiteOf(_game);
        CapitalHull.HullPlan plan = CapitalHull.Of(site.Instance.Layout);
        GD.Print($"-- {site.Family.Name} '{site.Instance.Layout.Variant?.Name}' ({site.Instance.State}), {plan.Pieces.Count} piece(s), {_game.Places.DistanceTo(site, _game.Survivor.WorldFeet):F0} m off --");
        Vector3 gate = _game.Zone.Planet.NearestCopy(new Vector3(site.Instance.Layout.EntranceCell.X + site.Origin.X + 0.5f, site.Origin.Y, site.Instance.Layout.EntranceCell.Z + site.Origin.Z + 0.5f), _game.Survivor.WorldFeet);
        gate.Y = _game.Zone.GroundAt(gate.X, gate.Z).Y + 0.3f;
        _game.Survivor.PlaceOutside(gate.ToNumerics());
        for (int i = 0; i < 3600 && !_game.Hulks.Down.ContainsKey(site); i++) yield return 0;
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(120)) yield return x;
        Check(_game.Hulks.Down.TryGetValue(site, out List<Vessel> wrecks) && wrecks.Count == plan.Pieces.Count, $"set down as {wrecks?.Count ?? 0} wreck(s) once the sleeper came near (built on a worker, {_game.Hulks.LastSetDownMs:F0} ms on the main thread, dressed a few pieces a tick)");
        if (wrecks == null || wrecks.Count == 0) yield break;
        Vessel v = wrecks.FirstOrDefault(w => w.Derelict.Hull?.CoreSlot != null) ?? wrecks[0];

        GD.Print("-- a coreless vessel of real blocks --");
        var stats = StructureStats.Compute(v.Structure);
        Check(wrecks.All(w => w.Structure.Core == null && w.IsDerelict), $"{v.Name}: no core, a wreck of {v.Structure.Count} blocks, {v.Decos.Count} pieces of dressing on its grid");
        Check(stats.Thrusters > 0 && stats.Generators > 0 && stats.Containers > 0, $"its devices count as devices: {stats.Thrusters} thrusters, {stats.Generators} generators, {stats.Containers} containers");
        int chairs = v.Structure.Blocks().Count(b => v.Structure.TypeOf(b.block).IsCommandChair);
        Check(wrecks.Sum(w => w.Structure.Blocks().Count(b => w.Structure.TypeOf(b.block).IsCommandChair)) >= 1, "command chairs came through");
        Check(!v.Storage.IsEmpty, "its holds carry salvage");
        float pitch = MathX.Angle(N.Vector3.Transform(N.Vector3.UnitY, v.Body.Rotation), N.Vector3.UnitY) * 180f / MathF.PI;
        float cell = v.Structure.Grid.CellSize, ground = site.Origin.Y;
        int buried = v.Structure.Blocks().Count(b => v.ToWorld(StructureBridge.CellCenter(v.Structure, b.pos)).Y + cell * 0.5f < ground);
        Check(buried > 100, $"it lies in the ground as it came down: pitched {pitch:F1} deg, {buried} blocks below the ground it came down on");
        // Nobody waits inside: whatever the place holds stands outside the hull (rule S2).
        int inside = 0;
        foreach (var e in site.Instance.Enemies)
        {
            Vector3 w = _game.Zone.Planet.NearestCopy(new Vector3(e.Cell.X + site.Origin.X + 0.5f, e.Cell.Y + site.Origin.Y + 0.5f, e.Cell.Z + site.Origin.Z + 0.5f), v.ToWorld(N.Vector3.Zero));
            foreach (Vessel x in wrecks)
            {
                var (lo, hi) = x.Structure.Bounds();
                N.Vector3 l = x.Body.ToLocal(w.ToNumerics()) / cell;
                if (l.X > lo.X && l.X < hi.X && l.Y > lo.Y && l.Y < hi.Y && l.Z > lo.Z && l.Z < hi.Z) inside++;
            }
        }
        Check(inside == 0, $"nobody waits inside: {site.Instance.Enemies.Count} of the place's occupants, {inside} within a hull");

        GD.Print("-- claimed with a core --");
        string why = v.Derelict.Hull?.CoreSlot is GalHack.Structures.Int3 slot ? v.ClaimWith(slot) : "no seat for a core";
        Check(why == null && v.Structure.Core != null && !v.IsDerelict, $"a core set into its core room claims it{(why != null ? ": " + why : "")}");

        GD.Print("-- what's missing filled in (Creative) --");
        int put = Fill(v, _game.Zone.Data.Catalogue);
        foreach (int x in _drive.Wait(30)) yield return x;
        v.SwitchMode(GalHack.Structures.CoreMode.Flight);
        foreach (int x in _drive.Wait(30)) yield return x;
        var missingFor = v.MissingFor(GalHack.Structures.CoreMode.Flight);
        stats = StructureStats.Compute(v.Structure);
        double weight = stats.Mass * StructureStats.G * _game.Zone.Physics.Zone.Strength / 9.81;
        double up = stats.Thrust[(int)GalHack.Structures.Dir.PosY];
        Check(put > 0 && missingFor.Count == 0, $"{put} blocks put back; Flight Mode lacks nothing{(missingFor.Count > 0 ? ": " + string.Join(", ", missingFor) : "")}");
        Check(up > weight && stats.Powered && stats.PowerOut >= stats.PowerUse * 0.99,
            $"thrust {up / 1000:F0} MN up against {weight / 1000:F0} MN of weight, {stats.PowerOut:F0} MW for {stats.PowerUse:F0} MW drawn");

        for (int i = 0; i < 1800 && _game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;
        GD.Print($"-- lifted off, the ground crumbling away (its place stood up: {_game.Places.PlaceOf(site)?.Ready ?? false}) --");
        N.Vector3 start = v.Body.Position;
        CapitalBase.Core.CutPlan cut = v.LiftOff();
        Check(cut.Ok && cut.Cells.Count > 0 && cut.Cells.Count <= Vessel.LiftRules.MaxCells,
            $"the cut is planned: {cut.Cells.Count} cells of ground (the limit {Vessel.LiftRules.MaxCells}), a {cut.Climb:F1} m climb, buried {cut.DeepestBurial:F1} m deep{(cut.Ok ? "" : ": " + cut.Refusal)}");
        if (!cut.Ok) yield break;
        int solid = cut.Cells.Count(c => _game.Zone.Planet.Terrain.Store.IsSolid(c));
        Check(solid == 0, $"every cell of the cut is gone ({solid} left)");
        for (int i = 0; i < 60 * 90 && v.Lifting.Phase != LiftOffPhase.Done; i++) yield return 0;
        LiftOff run = v.Lifting;
        Check(run.Phase == LiftOffPhase.Done && run.Climbed >= cut.Climb - 0.1f && run.Stalls == 0,
            $"it climbed {run.Climbed:F1} m straight up in {run.ClimbTicks / 60f:F1} s (waited {run.CuttingTicks} ticks for the ground), {run.Stalls} stalls, {run.Sideways:F2} m sideways, top rise {run.TopRise:F2} m/s");
        Check(v.Body.Position.Y - start.Y >= cut.Climb - 0.1f, $"clear of the ground: {v.Body.Position.Y - start.Y:F1} m up");
    }
}
