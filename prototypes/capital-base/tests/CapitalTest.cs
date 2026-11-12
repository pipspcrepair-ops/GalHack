using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CapitalBase.Core;
using CapitalBase.Records;
using CapitalBase.World;
using GalHack.Persistence;
using GalHack.Physics;
using GalHack.Structures;
using GalHack.Terrain;
using Godot;
using ShipHandling.Handling;
using TerrainDigging.Terrain;
using Weather.Model;
using LoadResult = GalHack.Persistence.LoadResult;
using N = System.Numerics;
using SInt3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace CapitalBase.Tests;

/// <summary>
/// Headless tests of the capital-base loop, on the real planet terrain. Run with:
///   .\tools\test.ps1 -Project prototypes/capital-base -Scene res://tests/CapitalTest.tscn
/// (test-all passes --fixed-fps 60 from CapitalTest.args, so it runs as fast as the machine can.)
///
/// Pure rules first (cargo, auto-transmit, the cut-free planner), then one world: Bastion 7 lands,
/// becomes a base, is built onto and into the ground, dug around, has Prospector 3 parked on it,
/// and lifts off clean, three times in a row at three spots; every stage is saved and loaded, and
/// every item and block is counted after every step. Then the too-heavy refusal, the
/// auto-transmit cases and the timing checks. Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class CapitalTest : Node
{
    CapitalWorld _w;
    SaveGame _save;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly List<RescueInfo> _rescues = new();
    readonly List<ImpactInfo> _impacts = new();
    string _saveDir;
    (int Items, int Blocks) _baseline;
    int _saves;

    Craft Bastion => _w.CraftById("bastion");
    Craft Prospector => _w.CraftById("prospector");
    Craft Outpost => _w.CraftById("outpost");

    /// <summary>Bastion's landing pad, in its own frame: the middle of the roof aft of the tower.</summary>
    static readonly N.Vector3 Pad = new(0f, 7f, 6f);

    public override void _Ready()
    {
        ProcessPhysicsPriority = -30; // inputs before the physics host (-10) and the world (0)
        GD.Print("-- pure rules --");
        PureChecks();
        _run = Scenarios().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try
        {
            more = _run.MoveNext();
        }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0
            ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)"
            : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        if (_failures == 0 && _saveDir != null && Directory.Exists(_saveDir)) Directory.Delete(_saveDir, true); // test scratch only
        // C# wrappers the run dropped (collision results, rebuilt hull shapes) are finalized now,
        // while the engine is whole, not by the finalizer thread racing its teardown: that race
        // crashed the exit (0xC0000005 in GodotObject.Finalize) after every check had passed.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok)
        {
            _passes++;
            GD.Print($"  ok    {what}");
        }
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"  FAIL  {what}");
    }

    // ==== Pure rules ===============================================================================

    void PureChecks()
    {
        var items = new ItemCatalog(new Dictionary<string, double> { ["rock"] = 1, ["ore"] = 2 });

        // Part-full: what fits moves, in item order, whole units; nothing is deleted.
        var vessel = new Cargo();
        vessel.Add("ore", 7);
        vessel.Add("rock", 9);
        var home = new Cargo();
        home.Add("rock", 85);
        int moved = Cargo.MoveWhatFits(vessel, home, 100, items);
        Check(moved == 8 && home["ore"] == 7 && home["rock"] == 86 && vessel["ore"] == 0 && vessel["rock"] == 8
              && vessel.Total + home.Total == 101 && home.UsedSu(items) <= 100,
            $"part-full Home: 7 ore (14 SU) then 1 rock fill its last 15 SU; 8 rock stay aboard; 101 units before and after");

        var (toHome, aboard) = Cargo.Split(new Dictionary<string, int> { ["rock"] = 12, ["ore"] = 3 }, home, 120, vessel, items);
        Check(toHome + aboard == 15 && home.UsedSu(items) <= 120 && vessel.Total == 8 + aboard,
            $"a fresh haul splits: {toHome} into Home's last {120 - 100} SU, {aboard} aboard, none lost");

        // The link's state, from the facts.
        var empty = new Cargo();
        var holding = new Cargo();
        holding.Add("rock", 3);
        Check(AutoTransmit.Evaluate(false, true, 10, 1000, 500, items, holding) == LinkState.Off
              && AutoTransmit.Evaluate(true, false, 10, 1000, 500, items, holding) == LinkState.NoHome
              && AutoTransmit.Evaluate(true, true, 1000.5f, 1000, 500, items, holding) == LinkState.OutOfRange
              && AutoTransmit.Evaluate(true, true, 999f, 1000, 500, items, holding) == LinkState.InRange
              && AutoTransmit.Evaluate(true, true, 999f, 1000, 0.5, items, holding) == LinkState.HomeFull
              && AutoTransmit.Evaluate(true, true, 999f, 1000, 1.5, items, empty) == LinkState.HomeFull,
            "link states: off, no Home, out of range past 1000 m, in range, Home full (no room for one more unit)");

        // Exactly one Home.
        var flag = new HomeFlag();
        flag.Flag("bastion");
        string old = flag.Flag("outpost");
        Check(old == "bastion" && flag.IsHome("outpost") && !flag.IsHome("bastion"), "flagging a new Home clears the old one: there is only ever one");

        // The cut-free planner on a synthetic ground: solid below y = 10.
        var rules = new CapitalRules.LiftoffData();
        Verdict Flat(TInt3 c) => c.Y < 10 ? Verdict.Removable : Verdict.AlreadyAir;
        var buried = new BlockBox(new N.Vector3(0.5f, 10f, 0.5f), N.Quaternion.Identity, new N.Vector3(1f), 0f); // 1 m into the ground
        CutPlan a = CutFree.Plan(new[] { buried }, 0f, rules, Flat);
        CutPlan b = CutFree.Plan(new[] { buried }, 0f, rules, Flat);
        bool within = a.Cells.All(c => MathF.Abs(c.Center.X - 0.5f) <= 1.75f && MathF.Abs(c.Center.Z - 0.5f) <= 1.75f && c.Center.Y >= 9f - 0.75f);
        Check(a.Ok && a.Cells.Count > 0 && within && a.Cells.SequenceEqual(b.Cells) && a.Cells.SequenceEqual(a.Cells.OrderBy(c => c)),
            $"a block 1 m into the ground: {a.Cells.Count} cells cut, all within the margin of its sweep, sorted, the same twice; climb {a.Climb:F1} m");
        var above = new BlockBox(new N.Vector3(0.5f, 14f, 0.5f), N.Quaternion.Identity, new N.Vector3(1f), 0f);
        Check(CutFree.Plan(new[] { above }, 0f, rules, Flat).Cells.Count == 0, "a hull standing clear of the ground cuts nothing");
        Verdict Rock(TInt3 c) => c.Y < 10 ? Verdict.Bedrock : Verdict.AlreadyAir;
        CutPlan refused = CutFree.Plan(new[] { buried }, 0f, rules, Rock);
        Check(!refused.Ok && refused.Refusal.Contains("BEDROCK"), $"keyed into bedrock: refused ({refused.Refusal})");
        var deep = new BlockBox(new N.Vector3(0.5f, -60f, 0.5f), N.Quaternion.Identity, new N.Vector3(1f), 0f);
        CutPlan tooDeep = CutFree.Plan(new[] { deep }, 0f, rules, Flat);
        Check(!tooDeep.Ok && tooDeep.Refusal.Contains("BURIED"), "buried 70 m deep: refused, nothing cut");
    }

    // ==== The world ================================================================================

    IEnumerable<int> Scenarios()
    {
        GD.Print("-- the world --");
        _w = new CapitalWorld { Visuals = false };
        AddChild(_w);
        _w.Physics.Rescued += _rescues.Add;
        _w.Physics.Impact += _impacts.Add;
        _w.Weather.Calm = true;
        _saveDir = Paths.Own(".saves", $"test-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{System.Environment.ProcessId}");
        _save = new SaveGame(_saveDir);
        _save.Attach(_w);
        Check(_w.Spots.Count >= 4, $"planet built in {_w.PlanetBuildMs:F0} ms; {_w.Patch.ChunkCount} voxel chunks around the site; landing spots: {string.Join("; ", _w.Spots)}");

        Spot a = _w.Spots[0];
        _w.SpawnBase("outpost", "outpost.gbp", _w.Spots[3].Flat, 0.6f);
        _w.SpawnFlying("bastion", "bastion.gbp", new N.Vector3(a.X, a.Ground + 45f, a.Z), 0f);
        _w.Spawn("prospector", "prospector.gbp", N.Vector3.Zero, N.Quaternion.Identity);
        _w.DockAt(Prospector, Bastion, Pad, 0f);
        _w.FlagHome(Bastion);
        _w.SetAutoTransmit(Prospector, true);
        for (int i = 0; i < 3; i++) yield return 0;
        Check(Outpost.State == CraftState.Anchored && Bastion.State == CraftState.Flying && Prospector.State == CraftState.Docked,
            $"Outpost 2 set into the ground as a base ({Outpost.Structure.Count} blocks), Bastion 7 flying ({Bastion.Structure.Count} blocks, {Bastion.Tonnes:F0} t), Prospector 3 parked on its pad");
        Conserved("spawned");
        foreach (int x in Reload("the start")) yield return x;

        for (int cycle = 1; cycle <= 3; cycle++)
            foreach (int x in Cycle(cycle, _w.Spots[cycle - 1])) yield return x;

        foreach (int x in WindOnCapital()) yield return x;
        foreach (int x in TransmitCases()) yield return x;
        foreach (int x in Timing()) yield return x;
    }

    // ---- One full cycle ----------------------------------------------------------------------------

    IEnumerable<int> Cycle(int n, Spot spot)
    {
        GD.Print($"-- cycle {n}: {spot} --");

        // 1. Land.
        foreach (int x in LandAt(Bastion, spot.Flat, 45f, 3600)) yield return x;
        Craft b = Bastion;
        LandingGear gear = b.Vessel.Gear;
        gear.Probe(_w.Physics, b.Body);
        var clearances = Enumerable.Range(0, gear.Count).Where(i => gear.FootFound[i])
            .Select(i => b.Body.ToParent(gear.Leg(i).Hip).Y - gear.Leg(i).Length - gear.FootGround[i].Y).ToList();
        float lowest = clearances.Min(), highest = clearances.Max();
        int standing = clearances.Count(c => c <= 0.25f);
        bool onGear = lowest <= 0.25f && n == 1;
        Check(b.State == CraftState.Landed && b.Tilt < 12.01f * Mathf.Pi / 180f && (n > 1 || onGear),
            $"landed on uneven ground: settled at {b.Tilt * 180f / Mathf.Pi:F1}° (gear allows 12°), its feet {lowest:F2} to {highest:F2} m off the ground under them, " +
            (n == 1 ? "standing on its gear" : $"{standing} of six feet on the ground, beside foundation pylons from earlier cycles that reach as low as its feet"));
        Check(Prospector.State == CraftState.Docked && Prospector.Pin.Host == b.Body && PadPoseKept(),
            "Prospector 3 rode the flight and the landing on the pad, its pose on the pad unchanged to the bit");
        Conserved("landed");
        foreach (int x in Reload($"cycle {n}, landed")) yield return x;

        // 2. Anchor: base mode at the core console.
        b = Bastion;
        string before = Fingerprint(b);
        Health was = HealthOf(b);
        N.Vector3 pose = b.Body.Position;
        N.Quaternion turn = b.Body.Rotation;
        string reply = _w.SwitchMode(b, CoreMode.Base);
        yield return 0;
        Health now = HealthOf(b);
        Check(b.IsBase && b.State == CraftState.Anchored && Fingerprint(b) == before && b.Body.Position == pose && b.Body.Rotation == turn,
            $"base mode at the core console: anchored where it stood, every block and the pose unchanged ({reply})");
        Check(was == now, $"storage, power and life support carry on through the switch: {now}");
        Check(LiftRefusedAsBase(b), "a base can't fly: lift-off is refused in base mode, and its handling model has no thrust");
        foreach (int x in WindOn(b, "base", expectMoved: false)) yield return x;
        Conserved("anchored");
        foreach (int x in Reload($"cycle {n}, anchored")) yield return x;

        // 3. Build onto it, into the ground, in creative (cycles 1 and 3) or survival (cycle 2).
        foreach (int x in BuildOnto(n)) yield return x;
        foreach (int x in Reload($"cycle {n}, built onto")) yield return x;

        // 4. Dig around it.
        foreach (int x in DigAround()) yield return x;

        // 5. The parked vessel goes farming, sends it Home, and parks again.
        foreach (int x in Farm(n)) yield return x;
        foreach (int x in Reload($"cycle {n}, farmed and parked")) yield return x;

        // 6. Back to capital mode.
        b = Bastion;
        was = HealthOf(b);
        before = Fingerprint(b);
        reply = _w.SwitchMode(b, CoreMode.Capital);
        yield return 0;
        Check(!b.IsBase && Fingerprint(b) == before && HealthOf(b) == was,
            $"capital mode again: every block unchanged, storage, power and life support carried on ({reply})");
        Conserved("capital mode");
        foreach (int x in Reload($"cycle {n}, capital mode")) yield return x;

        // 7. Lift off clean.
        if (n == 3)
            foreach (int x in TooHeavyRefusal()) yield return x;
        foreach (int x in LiftOffClean(n)) yield return x;
        foreach (int x in Reload($"cycle {n}, in flight")) yield return x;
    }

    // ---- Steps --------------------------------------------------------------------------------------

    /// <summary>Climbs to a height over the ground at a spot, flies over it and lands there.</summary>
    IEnumerable<int> LandAt(Craft c, N.Vector2 spot, float height, int limit) =>
        LandOn(c, spot, _w.GroundHeight(spot.X, spot.Y) + height, limit);

    /// <summary>Climbs to an altitude, flies over a spot at it and lands on whatever is there.</summary>
    IEnumerable<int> LandOn(Craft c, N.Vector2 spot, float altitude, int limit)
    {
        string id = c.Id;
        var over = new N.Vector3(spot.X, altitude, spot.Y);
        int t = 0;
        bool airborne = false;
        for (; t < limit; t++)
        {
            c = _w.CraftById(id);
            airborne |= c.State == CraftState.Flying && c.Body.Velocity.Y > 0.5f || MathX.Flat(c.Body.Position - over).Length() <= 3f;
            if (airborne && c.State != CraftState.Flying && !c.Vessel.Settling) break;
            if (!airborne)
            {
                c.Flight.Input = Pilot.Velocity(c, N.Vector3.UnitY * 4f); // off the ground first
                yield return 0;
                continue;
            }
            float across = MathX.Flat(c.Body.Position - over).Length();
            c.Flight.Input = across > 3f && c.Body.Position.Y < over.Y - 6f
                ? Pilot.Velocity(c, N.Vector3.UnitY * 8f)
                : across > 3f ? Pilot.Toward(c, new N.Vector3(over.X, c.Body.Position.Y, over.Z), 30f) : Pilot.Land(c, spot, 3f);
            yield return 0;
        }
        for (int i = 0; i < 40; i++) yield return 0;
        Check(t < limit, $"{c.Name} landed at ({spot.X:F0}, {spot.Y:F0}) after {t / 60f:F1} s of flight");
    }

    bool PadPoseKept()
    {
        Craft p = Prospector, b = Bastion;
        if (p.Pin is not { Docked: true } pin) return false;
        N.Vector3 expected = b.Body.ToParent(pin.Position);
        return N.Vector3.Distance(p.Body.Position, expected) < 1e-3f;
    }

    bool LiftRefusedAsBase(Craft b)
    {
        string reply = _w.LiftOffFrom(b);
        HandlingStats stats = HandlingModel.Compute(b.Spec, new FlightConditions(_w.Physics.Zone.Strength, true), _w.Handling);
        return reply.Contains("REFUSED") && reply.Contains("base") && stats.UpwardThrust == 0f && !_w.IsLiftingOff(b);
    }

    /// <summary>
    /// A storm on a craft: a base, or a vessel parked on one, doesn't move at all. A flying capital
    /// ship cruising with nobody at the controls (the autopilot holds course, so any push shows) is
    /// blown off its line.
    /// </summary>
    IEnumerable<int> WindOn(Craft c, string what, bool expectMoved)
    {
        _w.Weather.Calm = false;
        _w.Weather.Sim.Snap(WeatherKind.Hurricane);
        if (expectMoved) c.PlaceFlying(c.Body.Position, c.Body.Rotation, MathX.Forward(c.Body.Rotation) * 12f);
        N.Vector3 start = c.Body.Position, velocity = c.Body.Velocity;
        N.Quaternion turn = c.Body.Rotation;
        float most = 0f;
        for (int i = 0; i < 180; i++)
        {
            if (expectMoved) c.Flight.Input = PilotInput.Autopilot;
            yield return 0;
            most = MathF.Max(most, c.Body.LastForce(ForceKind.Weather).Length());
        }
        // Where it would be with no wind (holding course), and where it is.
        float moved = N.Vector3.Distance(c.Body.Position, start + velocity * 3f);
        float wind = _w.Weather.Sim.WindAt(start.ToGodotVector()).Length();
        _w.Weather.Calm = true;
        _w.Weather.Sim.Snap(WeatherKind.Clear);
        if (expectMoved)
        {
            Check(moved > 1f && most > 0f, $"wind moves a {what}: cruising at 12 m/s with nobody at the controls, a {wind:F0} m/s hurricane blew it {moved:F1} m off its line in 3 s (up to {most / 1000f:F0} kN)");
            for (int i = 0; i < 120; i++)
            {
                c.Flight.Input = PilotInput.LetGo;
                yield return 0;
            }
        }
        else
            Check(moved == 0f && c.Body.Rotation == turn && most == 0f, $"no wind moves a {what}: a {wind:F0} m/s hurricane for 3 s, 0 N on it, moved exactly 0 m");
    }

    IEnumerable<int> BuildOnto(int n)
    {
        Craft b = Bastion;
        Structure s = b.Structure;
        bool survival = n == 2;
        _w.Inventory.Creative = !survival;
        if (survival)
        {
            _w.Inventory.Add(new Dictionary<string, int> { ["iron"] = 120 });
            _w.Declare(120, 0); // a stock to build with, given on purpose
        }

        // A foundation pylon from the keel, where the ground under the hull stands highest, built
        // down block by block until it is in the ground (at least the plane of the feet).
        SInt3 column = PylonColumn(b);
        Block hazard = s.Get(new SInt3(-3, 3, -1));
        BlockType steel = Type("steel");
        int iron = _w.Inventory["iron"];
        int cleared = 0, placed = 0, pylon = 0;
        for (int y = -1; y >= -6 && (y >= -3 || cleared == 0); y--)
        {
            Block block = Building.Make(s, steel);
            block.Paint = hazard.Paint;
            var (error, cut) = _w.PlaceBlock(b, new SInt3(column.X, y, column.Z), block);
            if (error == PlaceError.None) pylon++;
            cleared += cut;
        }
        placed += pylon;
        // A container on the roof: more storage, built onto a base.
        SInt3 roof = new[] { new SInt3(3, 4, -3), new SInt3(-3, 4, -3), new SInt3(3, 4, -4) }[n - 1];
        var (containerError, _) = _w.PlaceBlock(b, roof, Building.Make(s, Type("container")));
        if (containerError == PlaceError.None) placed++;
        yield return 0;
        Check(placed == pylon + 1 && pylon >= 3 && cleared > 0,
            $"built onto the base in {(survival ? "survival" : "creative")}: a {pylon}-block foundation pylon into the ground (its ground cut through the edit API: {cleared} cells) and a container on the roof");
        if (survival)
        {
            int cost = pylon * 10 + 16;
            Check(_w.Inventory["iron"] == iron - cost, $"survival pays: {cost} iron for {pylon} steel blocks and a container ({iron} → {_w.Inventory["iron"]})");
            BlockType combat = Type("combat");
            int count = s.Count;
            var (refusedError, _) = _w.PlaceBlock(b, new SInt3(-3, 4, -4), Building.Make(s, combat));
            Check(refusedError == PlaceError.NotEnoughMaterials && s.Count == count, "survival refuses a block the player can't pay for (no tungsten), changing nothing");
            _w.Inventory.Creative = true;
        }
        else Check(_w.Inventory["iron"] == iron, "creative places for free");
        Check(_w.Ground.History.Last(e => e.Why == Cut.Build).Removed > 0, "the cleared ground is in the terrain log");
        Conserved("built onto");
    }

    /// <summary>The keel column where the ground under the hull stands highest above the plane of the feet.</summary>
    SInt3 PylonColumn(Craft b)
    {
        Structure s = b.Structure;
        SInt3 best = default;
        float bestDepth = float.MinValue;
        foreach (int x in new[] { -3, -1, 0, 1, 3 })
            for (int z = -8; z <= 8; z++)
            {
                if (Enumerable.Range(1, 6).Any(d => s.Has(new SInt3(x, -d, z)))) continue;
                if (!s.Has(new SInt3(x, 0, z))) continue;
                N.Vector3 bottom = b.Body.ToParent(new N.Vector3(x * 2f, -7f, z * 2f));
                float depth = _w.GroundTop(bottom.X, bottom.Z) - bottom.Y;
                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    best = new SInt3(x, -1, z);
                }
            }
        return best;
    }

    IEnumerable<int> DigAround()
    {
        Craft b = Bastion;
        int items = _w.Inventory.Total, removed = 0;
        foreach (N.Vector3 local in new[] { new N.Vector3(-17f, 0f, -6f), new N.Vector3(17f, 0f, 12f), new N.Vector3(-4f, 0f, 26f) })
        {
            N.Vector3 at = b.Body.ToParent(local);
            at.Y = _w.GroundTop(at.X, at.Z) - 1f;
            removed += _w.Dig(at, 2f).Removed.Count;
            yield return 0;
        }
        Check(removed > 0 && _w.Inventory.Total - items == removed && b.State == CraftState.Anchored,
            $"dug around the base: {removed} cells, all {_w.Inventory.Total - items} units into the player's inventory; the base didn't move");

        // Dig anywhere: tunnel right under the base (under its keel and any landing foot on the
        // ground). The ground goes; the base stays exactly where it was built.
        N.Vector3 pose = b.Body.Position;
        N.Quaternion turn = b.Body.Rotation;
        string before = Fingerprint(b);
        int under = 0, refused = 0;
        foreach (N.Vector3 local in new[] { new N.Vector3(0f, 0f, 0f), new N.Vector3(-4f, 0f, 6f), new N.Vector3(4f, 0f, -6f) })
        {
            N.Vector3 at = b.Body.ToParent(local);
            at.Y = _w.GroundTop(at.X, at.Z) - 1.5f;
            PlanetEdit edit = _w.Dig(at, 2f);
            under += edit.Removed.Count;
            refused += edit.Bedrock + edit.Outside;
            yield return 0;
        }
        for (int i = 0; i < 30; i++) yield return 0;
        Check(under > 0 && refused == 0 && b.State == CraftState.Anchored && b.Body.Position == pose && b.Body.Rotation == turn && Fingerprint(b) == before,
            $"dug right under the base: {under} cells, none refused; it stands exactly where it was built (pose and every block unchanged to the bit)");
        Conserved("dug around");
    }

    IEnumerable<int> Farm(int n)
    {
        Craft p = Prospector;
        Craft b = Bastion;
        int homeBefore = b.Cargo.Total;

        // Undock: thrust lifts it off the pad.
        for (int i = 0; i < 40; i++)
        {
            p = Prospector;
            p.Flight.Input = Pilot.Velocity(p, N.Vector3.UnitY * 4f);
            yield return 0;
        }
        Check(p.State == CraftState.Flying && p.Pin == null, "thrust lifts Prospector 3 off the pad: no longer parked");

        // Out to a patch of ground beside the base, land, drill twice.
        N.Vector3 field = b.Body.ToParent(new N.Vector3(26f, 0f, -24f));
        foreach (int x in LandAt(p, new N.Vector2(field.X, field.Z), 10f, 2400)) yield return x;
        p = Prospector;
        int dug = 0, home = 0;
        for (int i = 0; i < 2; i++)
        {
            CapitalWorld.DrillResult r = _w.Drill(p);
            dug += r.Dug;
            home += r.ToHome;
            yield return 0;
        }
        Check(dug > 0 && home == dug && Bastion.Cargo.Total - homeBefore == dug && p.Cargo.Total == 0 && p.Link.State == LinkState.InRange,
            $"auto-transmit in range: Prospector drilled {dug} units {_w.DistanceToHome(p, Bastion):F0} m from Home and all went straight into Bastion's storage ({_w.LinkText(p)})");
        Conserved("farmed");

        // Back to the pad.
        N.Vector3 pad = Bastion.Body.ToParent(Pad);
        foreach (int x in LandOn(p, new N.Vector2(pad.X, pad.Z), pad.Y + 8f, 2400)) yield return x;
        for (int i = 0; i < 10; i++) yield return 0;
        p = Prospector;
        Check(p.State == CraftState.Docked && p.Pin.Host == Bastion.Body, "Prospector 3 landed on Bastion's pad and parked there");
        foreach (int x in WindOn(p, "vessel parked on a base", expectMoved: false)) yield return x;
        Conserved("parked");
    }

    IEnumerable<int> LiftOffClean(int n)
    {
        Craft b = Bastion, p = Prospector;
        CutPlan first = _w.PlanCut(b), second = _w.PlanCut(b);
        Check(first.Cells.SequenceEqual(second.Cells) && first.Climb == second.Climb,
            $"the cut is deterministic: {first.Cells.Count} cells, a {first.Climb:F1} m climb, the same plan twice");
        int edits = _w.Ground.History.Count;
        string hostBlocks = Fingerprint(b), vesselBlocks = Fingerprint(p);
        var counts = _w.Count();
        N.Vector3 hostStart = b.Body.Position, vesselStart = p.Body.Position;
        int rescues = _rescues.Count(r => r.Body == b.Body || r.Body == p.Body);
        int impacts = _impacts.Count(i => i.Body == b.Body && i.Other == null);

        string reply = _w.LiftOffFrom(b);
        LiftOff run = _w.LiftOffOf(b);
        Check(run != null && reply.StartsWith("LIFT-OFF:"), $"lift-off from the console: {reply}");
        if (run == null) yield break;
        GroundEdit cut = _w.Ground.History[edits];
        Check(cut.Why == Cut.LiftOff && cut.Removed == first.Cells.Count && first.Cells.All(c => !_w.Ground.Store.IsSolid(c)),
            $"every cell of the plan was cut through the edit API and logged ({cut.Removed} cells: {CapitalBase.World.Ground.Describe(cut.Materials)})");
        if (n == 1) Check(cut.Removed > 0, "the first lift-off had ground to cut: the foundation was keyed into it");
        int t = 0;
        for (; t < 1800 && _w.IsLiftingOff(b); t++)
        {
            if (Bastion.Body.Position.Y - hostStart.Y > run.Plan.Climb + 2f) break;
            yield return 0;
        }
        b = Bastion;
        p = Prospector;
        Check(run.Phase == LiftOffPhase.Done && run.Stalls == 0 && run.TopRise <= _w.Rules.Liftoff.ClimbSpeed + 0.25f && run.Sideways < 0.5f,
            $"lifted off clean: climbed {run.Climbed:F1} m in {run.ClimbTicks / 60f:F1} s, never snagged (0 stalls), never launched (top rise {run.TopRise:F2} m/s against {_w.Rules.Liftoff.ClimbSpeed:F1}), strayed {run.Sideways:F2} m sideways");
        int newImpacts = _impacts.Count(i => i.Body == b.Body && i.Other == null) - impacts;
        Check(_rescues.Count(r => r.Body == b.Body || r.Body == p.Body) == rescues && newImpacts == 0,
            "no stuck rescue and no impact with the ground on the way out");
        N.Vector3 hostMoved = b.Body.Position - hostStart, vesselMoved = p.Body.Position - vesselStart;
        Check(p.State == CraftState.Docked && PadPoseKept() && vesselMoved.Length() > 0.5f * hostMoved.Length(),
            $"the parked vessel rode along: still exactly on the pad, carried {vesselMoved.Length():F2} m up with its host ({hostMoved.Length():F2} m at the core)");
        Check(Fingerprint(b) == hostBlocks && Fingerprint(p) == vesselBlocks && _w.Count() == counts && _w.Crafts.Count == 3,
            $"every block, item and vessel accounted for: {counts.Blocks} blocks, {counts.Items} items, 3 crafts");
        Conserved("lifted off");
        for (int i = 0; i < 30; i++)
        {
            Bastion.Flight.Input = PilotInput.LetGo;
            yield return 0;
        }
    }

    IEnumerable<int> TooHeavyRefusal()
    {
        Craft b = Bastion;
        Structure s = b.Structure;
        BlockType combat = Type("combat");
        Block dark = s.Get(new SInt3(-4, 1, 9));
        var bunker = new List<SInt3>();
        foreach (int x in new[] { -5, 5 })
        {
            for (int z = 0; z <= 8; z++) bunker.Add(new SInt3(x, 2, z));
            for (int z = 2; z <= 4; z++) bunker.Add(new SInt3(x, 3, z));
        }
        foreach (SInt3 c in bunker)
        {
            Block block = Building.Make(s, combat);
            block.Paint = dark.Paint;
            _w.PlaceBlock(b, c, block);
        }
        yield return 0;
        int edits = _w.Ground.History.Count;
        CraftState state = b.State;
        string reply = _w.LiftOffFrom(b);
        Check(reply.Contains("TOO HEAVY") && reply.Contains("Drop") && !_w.IsLiftingOff(b) && _w.Ground.History.Count == edits && b.State == state,
            $"a bunker of {bunker.Count} combat-steel blocks ({bunker.Count * 4} t) makes it too heavy: refused with a clear message and nothing cut ({reply})");
        foreach (SInt3 c in bunker.AsEnumerable().Reverse()) _w.RemoveBlock(b, c);
        yield return 0;
        Check(!bunker.Any(s.Has), $"the bunker comes off again (its materials to the player): {_w.Inventory["iron"]} iron in hand");
        Conserved("bunker built and taken down");
    }

    // ---- After the cycles --------------------------------------------------------------------------

    IEnumerable<int> WindOnCapital()
    {
        GD.Print("-- wind on a capital ship --");
        Craft b = Bastion;
        Check(!b.IsBase && b.State == CraftState.Flying, "Bastion 7 is flying in capital mode, Prospector 3 parked on it");
        float most = 0f;
        foreach (int x in WindOn(b, "flying capital ship", expectMoved: true))
        {
            most = MathF.Max(most, Prospector.Body.LastForce(ForceKind.Weather).Length());
            yield return x;
        }
        Check(most == 0f && Prospector.State == CraftState.Docked && PadPoseKept(),
            "the vessel parked on it is sheltered: 0 N of wind on it all through the hurricane, and it rode the drift on the pad");
    }

    IEnumerable<int> TransmitCases()
    {
        GD.Print("-- auto-transmit to Home --");
        // Land Bastion at the first spot again and let Prospector off to hover near it.
        foreach (int x in LandAt(Bastion, _w.Spots[0].Flat, 30f, 3600)) yield return x;
        for (int i = 0; i < 40; i++)
        {
            Prospector.Flight.Input = Pilot.Velocity(Prospector, N.Vector3.UnitY * 4f);
            yield return 0;
        }
        N.Vector3 field = Bastion.Body.ToParent(new N.Vector3(-30f, 0f, 20f));
        foreach (int x in Hover(Prospector, field, 4f, 900)) yield return x;
        Craft p = Prospector;

        // Out of range: 1.2 km from Home it keeps what it digs.
        N.Vector3 home = Bastion.Body.Position;
        var far = new N.Vector3(home.X + 1200f, 0f, home.Z);
        far.Y = _w.GroundTop(far.X, far.Z) + 4f;
        p.PlaceFlying(far, p.Body.Rotation, N.Vector3.Zero);
        int homeHeld = Bastion.Cargo.Total;
        foreach (int x in Hold(p, far, 30)) yield return x;
        CapitalWorld.DrillResult r = _w.Drill(p);
        foreach (int x in Hold(p, far, 60)) yield return x;
        Check(r.Dug > 0 && r.ToHome == 0 && p.Cargo.Total == r.Dug && Bastion.Cargo.Total == homeHeld && p.Link.State == LinkState.OutOfRange,
            $"out of range: {_w.DistanceToHome(p, Bastion):F0} m from Home, {r.Dug} units stay aboard ({_w.LinkText(p)})");
        Conserved("out of range");
        foreach (int x in Reload("auto-transmit, holding out of range")) yield return x;
        p = Prospector;

        // Back in range: what it holds goes across by itself.
        N.Vector3 near = Bastion.Body.ToParent(new N.Vector3(-30f, 0f, 20f));
        near.Y = _w.GroundTop(near.X, near.Z) + 4f;
        int holding = p.Cargo.Total;
        p.PlaceFlying(near, p.Body.Rotation, N.Vector3.Zero);
        foreach (int x in Hold(p, near, 45)) yield return x;
        Check(p.Cargo.Total == 0 && Bastion.Cargo.Total == homeHeld + holding && p.Link.State == LinkState.InRange,
            $"back in range, the {holding} units it held went to Home by themselves within {_w.Rules.AutoTransmit.Interval:F1} s");

        // The Home flag moved: Outpost 2 is Home now, and it is nearly full.
        Craft outpost = Outpost;
        string reply = _w.FlagHome(outpost);
        Check(_w.Home.HomeId == "outpost" && !_w.Home.IsHome("bastion"), $"the Home flag moved: {reply}");
        double capacity = outpost.StorageCapacity;
        int stock = (int)(capacity - outpost.Cargo.UsedSu(_w.Rules.Catalog)) - 10;
        outpost.Cargo.Add("rock", stock);
        outpost.CargoChanged();
        _w.Declare(stock, 0); // a full store to test against, made on purpose
        N.Vector3 byOutpost = outpost.Body.ToParent(new N.Vector3(14f, 0f, 10f));
        byOutpost.Y = _w.GroundTop(byOutpost.X, byOutpost.Z) + 4f;
        p.PlaceFlying(byOutpost, p.Body.Rotation, N.Vector3.Zero);
        foreach (int x in Hold(p, byOutpost, 30)) yield return x;
        int bastionHeld = Bastion.Cargo.Total, outpostHeld = outpost.Cargo.Total;
        r = _w.Drill(p);
        foreach (int x in Hold(p, byOutpost, 2)) yield return x;
        Check(r.ToHome == 10 && r.Aboard == r.Dug - 10 && outpost.Cargo.Total == outpostHeld + 10 && Bastion.Cargo.Total == bastionHeld
              && p.Link.State == LinkState.HomeFull && outpost.FreeSu < 1,
            $"Home part-full: of {r.Dug} units dug, the 10 that fit went to Outpost 2 (not Bastion, the old Home), {r.Aboard} stay aboard ({_w.LinkText(p)})");
        Conserved("Home part-full");
        foreach (int x in Reload("auto-transmit, Home full")) yield return x;
        p = Prospector;
        outpost = Outpost;

        // Home frees room: the player takes 200 rock out of the outpost's store.
        outpost.Cargo.Take("rock", 200);
        outpost.CargoChanged();
        _w.Inventory.Add(new Dictionary<string, int> { ["rock"] = 200 });
        holding = p.Cargo.Total;
        foreach (int x in Hold(p, byOutpost, 45)) yield return x;
        Check(p.Cargo.Total == 0 && p.Link.State == LinkState.InRange, $"Home freed room: the {holding} units held aboard went across by themselves");
        Conserved("Home freed room");

        // A moving Home: Bastion flagged again, flown away past the range and back while Prospector drills.
        _w.FlagHome(Bastion);
        foreach (int x in Hover(Prospector, field, 4f, 900)) yield return x;
        foreach (int x in MovingHome()) yield return x;
    }

    IEnumerable<int> MovingHome()
    {
        Craft b = Bastion;
        // Lift off (nothing holds it now: it landed on its gear and pylon).
        _w.LiftOffFrom(b);
        for (int i = 0; i < 900 && _w.IsLiftingOff(Bastion); i++) yield return 0;
        b = Bastion;
        Craft p = Prospector;
        N.Vector3 hover = p.Body.Position;
        N.Vector3 start = b.Body.Position;
        var away = new N.Vector3(start.X + 1300f, start.Y + 80f, start.Z);
        int deliveredMoving = 0, heldFar = 0, drills = 0;
        float fastest = 0f, farthest = 0f;
        bool returned = false, transferredOnReturn = false;
        for (int t = 0; t < 60 * 120; t++)
        {
            b = Bastion;
            p = Prospector;
            N.Vector3 target = returned ? start + N.Vector3.UnitY * 80f : away;
            b.Flight.Input = Pilot.Toward(b, target, 45f);
            // A fresh patch of ground for each drill: it works along a line, 4.5 m a hole.
            N.Vector3 spot = hover + new N.Vector3(drills * 4.5f, 0f, 0f);
            spot.Y = _w.GroundTop(spot.X, spot.Z) + 4f;
            p.Flight.Input = Pilot.Toward(p, spot, 6f);
            float d = _w.DistanceToHome(p, b);
            farthest = MathF.Max(farthest, d);
            // Back once Home is well out of range, or once it has got where it was sent: how far
            // that leaves it from the drilling vessel depends on Home's attitude (flying vessels keep
            // theirs, nothing levels them) and on how far the drill line has crept toward it.
            if (!returned && (d > 1250f || N.Vector3.Distance(b.Body.Position, away) < 15f)) returned = true;
            if (t % 90 == 45)
            {
                int homeBefore = b.Cargo.Total;
                CapitalWorld.DrillResult r = _w.Drill(p);
                drills++;
                if (r.Refusal == null && d < 900f && !returned && b.Body.Velocity.Length() > 10f && b.Cargo.Total - homeBefore == r.Dug) deliveredMoving += r.Dug;
                if (r.Refusal == null && d > 1050f) heldFar += r.Aboard;
                fastest = MathF.Max(fastest, b.Body.Velocity.Length());
            }
            if (returned && d < 900f && heldFar > 0)
            {
                for (int i = 0; i < 40; i++)
                {
                    Bastion.Flight.Input = PilotInput.LetGo;
                    Prospector.Flight.Input = PilotInput.LetGo;
                    yield return 0;
                }
                transferredOnReturn = Prospector.Cargo.Total == 0;
                break;
            }
            yield return 0;
        }
        Check(deliveredMoving > 0, $"a moving Home works: {deliveredMoving} units reached Bastion while it flew at up to {fastest:F0} m/s, still in range");
        Check(heldFar > 0 && transferredOnReturn,
            $"as Home flew out to {farthest:F0} m, {heldFar} units were held aboard; when it came back in range they went across by themselves");
        Conserved("moving Home");
        foreach (int x in Reload("auto-transmit, moving Home")) yield return x;
    }

    /// <summary>Hovers a vessel at a height over a point.</summary>
    IEnumerable<int> Hover(Craft c, N.Vector3 at, float height, int limit)
    {
        at.Y = _w.GroundTop(at.X, at.Z) + height;
        for (int t = 0; t < limit; t++)
        {
            c = _w.CraftById(c.Id);
            c.Flight.Input = Pilot.Toward(c, at, 12f);
            if (Pilot.At(c, at, 1f)) break;
            yield return 0;
        }
    }

    IEnumerable<int> Hold(Craft c, N.Vector3 at, int ticks)
    {
        for (int t = 0; t < ticks; t++)
        {
            c = _w.CraftById(c.Id);
            c.Flight.Input = Pilot.Toward(c, at, 4f);
            yield return 0;
        }
    }

    // ---- Timing ---------------------------------------------------------------------------------

    IEnumerable<int> Timing()
    {
        GD.Print("-- timing (best of three windows, so a busy machine can't fail it) --");
        Craft b = Bastion;
        var plans = new List<double>();
        for (int i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew();
            _w.PlanCut(b);
            plans.Add(watch.Elapsed.TotalMilliseconds);
        }
        Check(plans.Min() < 25.0, $"planning a lift-off cut for {b.Structure.Count + Prospector.Structure.Count} blocks: best {plans.Min():F1} ms of 3 (budget 25 ms, one frame)");

        var windows = new List<double>();
        for (int w = 0; w < 3; w++)
        {
            double total = 0;
            for (int i = 0; i < 100; i++)
            {
                Bastion.Flight.Input = Pilot.Velocity(Bastion, new N.Vector3(12f, 0f, 0f));
                Prospector.Flight.Input = PilotInput.LetGo;
                yield return 0;
                total += _w.Host.LastStepMs;
            }
            windows.Add(total / 100);
        }
        Check(windows.Min() < 2.0, $"the shared physics step with Bastion flying, Prospector parked on it and Outpost anchored: best window {windows.Min():F2} ms a tick (budget 2 ms; windows {string.Join(", ", windows.Select(x => x.ToString("F2")))})");
    }

    BlockType Type(string id) => _w.Catalogue.TryGetType(id, out BlockType t) ? t : throw new KeyNotFoundException(id);

    // ==== Saves and accounting =========================================================================

    /// <summary>Every item and block is where it should be: the start plus everything declared.</summary>
    void Conserved(string when)
    {
        var (items, blocks) = _w.Count();
        int wantItems = _baseline.Items + _w.DeclaredItems, wantBlocks = _baseline.Blocks + _w.DeclaredBlocks;
        Check(items == wantItems && blocks == wantBlocks, $"conserved ({when}): {blocks} blocks, {items} items");
    }

    /// <summary>
    /// Saves through the persistence core (whose ledger refuses any loss), loads it back into a
    /// rebuilt world, and checks the two worlds are the same to the bit.
    /// </summary>
    IEnumerable<int> Reload(string when)
    {
        string before = Snapshot();
        var counts = _w.Count();
        CommitResult commit;
        try
        {
            commit = _save.Save();
        }
        catch (SaveRefusedException e)
        {
            Fail($"save refused ({when}): {e.Message}");
            yield break;
        }
        LoadResult loaded = _save.Load();
        _saves++;
        _w.Patch.RebuildAllNow();
        _baseline = _w.Count();
        string after = Snapshot();
        bool same = before == after;
        if (!same) GD.PrintErr($"before:\n{before}\nafter:\n{after}");
        Check(loaded.Ok && same && _baseline == counts && loaded.Found.Items == counts.Items && loaded.Found.Blocks == counts.Blocks,
            $"save {_saves} and load ({when}): {commit.Written} records written, audit {loaded.Found}; every craft, state, pose, block and item back as it was");
        yield return 0;
    }

    /// <summary>Everything that must survive a save, as text: crafts (blueprint, state, pose, cargo, host), Home, the inventory, the ground.</summary>
    string Snapshot()
    {
        var lines = new List<string>();
        foreach (Craft c in _w.Crafts.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            Body b = c.Body;
            string host = c.Pin is { Docked: true } pin ? _w.CraftOf(pin.Host)?.Id : "-";
            lines.Add($"{c.Id} {c.State} {Fingerprint(c)} pos {b.Position.X:R},{b.Position.Y:R},{b.Position.Z:R} rot {b.Rotation.X:R},{b.Rotation.Y:R},{b.Rotation.Z:R},{b.Rotation.W:R} " +
                      $"cargo [{c.Cargo}] host {host} tx {c.AutoTransmit}");
        }
        lines.Add($"home {_w.Home.HomeId} creative {_w.Inventory.Creative} inventory {string.Join(",", _w.Inventory.Items.Select(kv => $"{kv.Key}:{kv.Value}"))}");
        lines.Add($"ground {_w.Ground.Store.EditStateHash():X16} cells {_w.Ground.Store.RemovedCount}");
        return string.Join("\n", lines);
    }

    /// <summary>A craft's blocks, as building's canonical blueprint text (byte-identical on a round trip), mode included.</summary>
    static string Fingerprint(Craft c)
    {
        string text = Blueprint.Save(c.Structure);
        var lines = text.Split('\n').Where(l => !l.StartsWith("mode ") && !l.StartsWith("cargo ")).ToArray();
        ulong h = 14695981039346656037ul;
        foreach (string l in lines)
            foreach (char ch in l) h = (h ^ ch) * 1099511628211ul;
        return $"{c.Structure.Count}b:{h:X16}";
    }

    /// <summary>What must carry on through a mode switch: power, storage and its contents, life support and sealed air.</summary>
    readonly record struct Health(double PowerOut, double PowerUse, double Storage, string Cargo, bool LifeSupport, bool Gravity, int SealedRooms, int SealedCells)
    {
        public override string ToString() =>
            $"power {PowerOut:F1}/{PowerUse:F1} MW, storage {Storage:F0} SU holding [{Cargo}], life support {(LifeSupport ? "on" : "off")}, core gravity {(Gravity ? "on" : "off")}, {SealedRooms} sealed rooms ({SealedCells} cells of air)";
    }

    static Health HealthOf(Craft c)
    {
        StructureStats s = c.Stats;
        RoomReport rooms = Rooms.Detect(c.Structure);
        return new Health(s.PowerOut, s.PowerUse, s.StorageCapacity, c.Cargo.ToString(), s.LifeSupport, s.GravityActive,
            rooms.Sealed.Count(), rooms.Sealed.Sum(r => r.Cells.Count));
    }
}

static class VectorExtensions
{
    public static Vector3 ToGodotVector(this N.Vector3 v) => new(v.X, v.Y, v.Z);
}
