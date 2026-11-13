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
/// A crashed capital ship is a real vessel (design, "Wrecks can be restored"): on a fresh random world with a
/// crashed freighter forced near the start, buried nose down or as a long hauler, it is set down as coreless
/// wrecks of the player's own blocks whose devices count as devices; a Big Core set into its core room claims
/// it; Creative fills in what the crash and the years took; in Capital Mode nothing is missing, its thrust beats
/// its weight, it has power and fuel; and it lifts off along capital-base's clean lift-off, the ground it lies
/// in cut and crumbling away, straight up through the cut without a snag.
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
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(20));
            Console.Error.WriteLine("HulkTest took over 20 minutes of wall-clock time: watchdog");
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

    /// <summary>
    /// A crashed freighter near the start, a long hauler (buried along its length), abandoned. Set before the world is
    /// made. (A nose-down hulk's cut runs past the lift-off's reference limit: its tilt widens every block's margin,
    /// so it is refused and dug out by hand, as the design says.)
    /// </summary>
    public static void Force()
    {
        PoiSiting.ForceFamily = "crashed_freighter";
        PoiSiting.ForceVariant = 3;
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
    public static int Fill(Vessel v, CapitalHull.HullPiece piece, Catalogue catalogue)
    {
        Structure intact = Hulks.Build(piece.Text("plan", new HashSet<PoiGenerator.Gen.Int3>()), catalogue, "plan");
        int placed = 0;
        for (int pass = 0; pass < 8; pass++)
        {
            int before = placed;
            foreach (var (p, b) in intact.Blocks().ToList())
                if (!v.Structure.Has(p) && GalHack.Structures.Building.Place(v.Structure, p, b, null, GalHack.Structures.Building.DefaultOwner) == PlaceError.None) placed++;
            if (placed == before) break;
        }
        v.ApplyEdits();
        return placed;
    }

    IEnumerable<int> Scenarios()
    {
        Force();
        _game = new Game.Game { Interactive = false, SkipIntro = true, Mode = GalHack.Items.GameMode.Creative, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        PoiSite site = SiteOf(_game);
        CapitalHull.HullPlan plan = CapitalHull.Of(site.Instance.Layout);
        GD.Print($"-- {site.Family.Name} '{site.Instance.Layout.Variant?.Name}' ({site.Instance.State}), {plan.Pieces.Count} piece(s), {_game.Places.DistanceTo(site, _game.Survivor.WorldFeet):F0} m off --");
        Vector3 gate = _game.Zone.Planet.NearestCopy(new Vector3(site.Instance.Layout.EntranceCell.X + site.Origin.X + 0.5f, site.Origin.Y, site.Instance.Layout.EntranceCell.Z + site.Origin.Z + 0.5f), _game.Survivor.WorldFeet);
        gate.Y = _game.Zone.GroundAt(gate.X, gate.Z).Y + 0.3f;
        _game.Survivor.PlaceOutside(gate.ToNumerics());
        for (int i = 0; i < 1800 && !_game.Hulks.Down.ContainsKey(site); i++) yield return 0;
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(120)) yield return x;
        Check(_game.Hulks.Down.TryGetValue(site, out List<Vessel> wrecks) && wrecks.Count == plan.Pieces.Count, $"set down as {wrecks?.Count ?? 0} wreck(s) once the survivor came near");
        if (wrecks == null || wrecks.Count == 0) yield break;
        Vessel v = wrecks[0];
        CapitalHull.HullPiece piece = plan.Pieces[0];

        GD.Print("-- a coreless vessel of real blocks --");
        var stats = StructureStats.Compute(v.Structure);
        Check(v.Structure.Core == null && v.IsDerelict, $"{v.Name}: no core, a wreck of {v.Structure.Count} blocks");
        Check(stats.Thrusters > 0 && stats.Generators > 0 && stats.Containers > 0 && stats.Fuel > 0, $"its devices count as devices: {stats.Thrusters} thrusters, {stats.Generators} generators, {stats.Containers} containers, {stats.Fuel:F0} fuel");
        int chairs = v.Structure.Blocks().Count(b => v.Structure.TypeOf(b.block).IsCommandChair);
        Check(chairs >= 1, $"{chairs} command chairs came through");
        Check(!v.Storage.IsEmpty, "its holds carry salvage");
        float pitch = MathX.Angle(N.Vector3.Transform(N.Vector3.UnitY, v.Body.Rotation), N.Vector3.UnitY) * 180f / MathF.PI;
        float cell = v.Structure.Grid.CellSize;
        float ground = site.Origin.Y;
        int buried = v.Structure.Blocks().Count(b => v.ToWorld(StructureBridge.CellCenter(v.Structure, b.pos)).Y + cell * 0.5f < ground);
        Check(buried > 100, $"it lies in the ground as it came down: pitched {pitch:F1} deg, {buried} blocks below the ground it came down on");
        int missing = piece.Intact.Count - v.Structure.Count;
        Check(missing > 50, $"restoring takes work: {missing} blocks of the plan are gone");

        GD.Print("-- claimed with a core --");
        string why = piece.CoreSlot is PoiGenerator.Gen.Int3 slot ? v.ClaimWith(new GalHack.Structures.Int3(slot.X, slot.Y, slot.Z)) : "no seat for a core";
        Check(why == null && v.Structure.Core != null && !v.IsDerelict, $"a core set into its core room claims it{(why != null ? ": " + why : "")}");

        GD.Print("-- what's missing filled in (Creative) --");
        int put = Fill(v, piece, _game.Zone.Data.Catalogue);
        foreach (int x in _drive.Wait(30)) yield return x;
        v.SwitchMode(GalHack.Structures.CoreMode.Capital);
        foreach (int x in _drive.Wait(30)) yield return x;
        var missingFor = v.MissingFor(GalHack.Structures.CoreMode.Capital);
        stats = StructureStats.Compute(v.Structure);
        double weight = stats.Mass * StructureStats.G * _game.Zone.Physics.Zone.Strength / 9.81;
        double up = stats.Thrust[(int)GalHack.Structures.Dir.PosY];
        Check(put > 0 && missingFor.Count == 0, $"{put} blocks put back; Capital Mode lacks nothing{(missingFor.Count > 0 ? ": " + string.Join(", ", missingFor) : "")}");
        Check(up > weight && stats.Powered && stats.PowerOut >= stats.PowerUse * 0.99 && stats.Fuel > 0,
            $"thrust {up / 1000:F0} MN up against {weight / 1000:F0} MN of weight, {stats.PowerOut:F0} MW for {stats.PowerUse:F0} MW drawn, {stats.Fuel:F0} fuel");

        for (int i = 0; i < 1800 && _game.Places.PlaceOf(site) is not { Ready: true }; i++) yield return 0;
        GD.Print($"-- lifted off, the ground crumbling away (its place stood up: {_game.Places.PlaceOf(site)?.Ready ?? false}) --");
        N.Vector3 start = v.Body.Position;
        CapitalBase.Core.CutPlan cut = v.LiftOff();
        Check(cut.Ok && cut.Cells.Count > 0, $"the cut is planned: {cut.Cells.Count} cells of ground, a {cut.Climb:F1} m climb, buried {cut.DeepestBurial:F1} m deep{(cut.Ok ? "" : ": " + cut.Refusal)}");
        {
            // Where the cut lies against the hull: under its keel, beside it, inside its outline, over it.
            var (lo, hi) = v.Structure.Bounds();
            float cs = v.Structure.Grid.CellSize;
            int under = 0, beside = 0, inside = 0, over = 0;
            foreach (var c in cut.Cells)
            {
                N.Vector3 l = MathX.Unrotate(v.Body.Rotation, new N.Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f) - v.Body.Position) / cs;
                bool inPlan = l.X > lo.X - 0.5f && l.X < hi.X + 0.5f && l.Z > lo.Z - 0.5f && l.Z < hi.Z + 0.5f;
                if (!inPlan) beside++;
                else if (l.Y < lo.Y - 0.5f) under++;
                else if (l.Y > hi.Y + 0.5f) over++;
                else inside++;
            }
            GD.Print($"  cut: {under} under the keel, {beside} beside, {inside} within its bounds, {over} over it; ground at the place {site.Origin.Y}, hull origin {v.Body.Position.Y:F1}");
        }
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
