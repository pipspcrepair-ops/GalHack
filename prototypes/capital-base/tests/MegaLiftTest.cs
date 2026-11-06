using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CapitalBase.Core;
using CapitalBase.World;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using GalHack.Terrain;
using Godot;
using PhysicsSandbox.Bridges;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using ShipHandling.Handling;
using TerrainDigging.Terrain;
using N = System.Numerics;
using PBlockType = PoiGenerator.Gen.BlockType;
using PInt3 = PoiGenerator.Gen.Int3;
using SBlock = GalHack.Structures.Block;
using SBlockType = GalHack.Structures.BlockType;
using SInt3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace CapitalBase.Tests;

/// <summary>
/// Feasibility test, not part of the gate (see the .manual file): can a semi-submerged mega
/// structure bore itself out of the ground? The biggest place the POI generator makes, as real
/// blocks on one core, tied together by a foundation slab, set half buried in the real terrain and
/// lifted off through the real <see cref="CutFree"/> and <see cref="LiftOff"/>, with its limits lifted.
/// Rock is left in the rooms, as the game's own SetInto leaves it, so the cut is the worst case.
///
/// Numbers (headless, three trials a size, the best one counts):
///   godot --headless --fixed-fps 60 --path prototypes/capital-base res://tests/MegaLiftTest.tscn -- --rungs=1000,10000,50000,0
/// Pictures (windowed, the largest; stills and every Nth tick as a frame for a GIF):
///   godot --fixed-fps 60 --path prototypes/capital-base res://tests/MegaLiftTest.tscn -- --shots=DIR --every=5
/// --half sinks the hull to half its whole height. Bedrock lies 30 m under the low ground within
/// 48 m, which over six seeds of every kind of world left at most 31 m for a hull this wide, so this
/// run lowers bedrock to 130 m, moves the patch to the deepest gentle ground, and climbs at 8 m/s, not
/// 3, to keep a run to minutes. --preview stops after the first still.
/// </summary>
public partial class MegaLiftTest : Node
{
    const float FreeClimbSpeed = 15f; // once the pilot has it: well above the 3 m/s of the cut climb

    Main _main;
    CapitalWorld _w;
    CapitalWorld W => _w;
    IEnumerator<int> _run;
    readonly Dictionary<PInt3, Role> _full = new();
    readonly List<RescueInfo> _rescues = new();
    readonly List<ImpactInfo> _impacts = new();
    int[] _rungs = { 1000, 10000, 50000, 0 };
    int _trials = 3, _every = 5;
    bool _half, _preview;
    string _shots, _poi, _grab, _grabSmall;
    long _lastHead, _head;
    double _frameMs, _scriptMs;
    int _failures;

    /// <summary>One hull, and how it stands.</summary>
    sealed record Hull(Structure Structure, int Blocks, int Jets, float Tonnes, float Upward, float Weight, int SlabY);

    sealed class Row
    {
        public int Blocks, Cells, Jets, Shapes, Stalls, Rescues, Impacts, CutTicks, Floating, FloatingCells, ChunksCut;
        public float Width, Depth, Height, Buried, Sunk, Mean, Top, Climb, Sideways, Tilt, RiseSpread, BlocksUnder, RoofUnder;
        public double ClearanceMs;
        public double BuildMs, CollidersMs, SeatMs, PlanMs, HitchMs, CrumbleMs, CutWorstMs, FirstFrameMs, ClimbMedianMs, ClimbWorstMs, ScriptMedianMs, IdleMs, MemMb, CutSeconds;
        public string Note = "";
    }

    public override void _Ready()
    {
        ProcessPhysicsPriority = -100;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--rungs=")) _rungs = arg["--rungs=".Length..].Split(',').Select(int.Parse).ToArray();
            else if (arg.StartsWith("--trials=")) _trials = int.Parse(arg["--trials=".Length..]);
            else if (arg.StartsWith("--shots=")) _shots = arg["--shots=".Length..];
            else if (arg.StartsWith("--every=")) _every = int.Parse(arg["--every=".Length..]);
            else if (arg == "--half") _half = true;
            else if (arg == "--preview") _preview = true;
        }

        Largest();
        int span = Math.Max(_full.Keys.Max(p => p.X) - _full.Keys.Min(p => p.X), _full.Keys.Max(p => p.Z) - _full.Keys.Min(p => p.Z)) * 2;
        CapitalWorld.Tune = r =>
        {
            r.Planet.PatchColumns = Math.Max(r.Planet.PatchColumns, (_half ? span / 16 + 9 : span * 3 / 32 + 8)); // bigger overruns Jolt's 10,240 bodies: one a chunk
            r.Liftoff.MaxClimb = 400f;
            r.Liftoff.MaxCells = int.MaxValue;
            if (_half) r.Liftoff.ClimbSpeed = 8f;
        };
        if (_half) PlanetTerrainSource.BedrockDepth = 130f; // no world's bedrock leaves room otherwise: see the class summary
        if (_half) CapitalWorld.TuneArea = (planet, near) => Massif(planet, near, span);
        if (_shots == null)
        {
            _w = new CapitalWorld { Visuals = false };
            AddChild(_w);
            _w.Debris.Visible = true; // headless too: Crumble's walk over the cells is part of the cost
        }
        else
        {
            GetViewport().UseDebanding = false;
            _main = new Main { Interactive = false };
            AddChild(_main);
            _w = _main.World;
            RenderingServer.FramePostDraw += Grab;
        }
        CapitalWorld.Tune = null;
        CapitalWorld.TuneArea = null;
        W.Weather.Calm = true;
        W.Physics.Rescued += _rescues.Add;
        W.Physics.Impact += _impacts.Add;
        AddChild(new Tail { Done = () => _scriptMs = ((long)Time.GetTicksUsec() - _head) / 1000.0 });
        GD.Print($"LIFT patch {W.Patch.Columns} columns, {W.Patch.ChunkCount} chunks, built in {W.PlanetBuildMs:F0} ms; poi {_poi}, {_full.Count} blocks in one piece");
        _run = (_shots == null ? Numbers() : Pictures()).GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        long now = (long)Time.GetTicksUsec();
        _frameMs = _lastHead == 0 ? 0 : (now - _lastHead) / 1000.0;
        _lastHead = _head = now;
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            GD.Print($"FAIL threw: {e}");
            _failures++;
            more = false;
        }
        if (!more) GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ==== The structure ===========================================================================

    /// <summary>The blocks of a layout that are building, not furniture.</summary>
    static IEnumerable<(PInt3 Cell, Role Role)> Solid(Layout l)
    {
        foreach (var (c, b) in l.Blocks.Blocks())
            if (BlockInfo.IsStructure(b.Role) && b.Type is PBlockType.Cube or PBlockType.Breakable or PBlockType.Pane or PBlockType.Lattice)
                yield return (c, b.Role);
    }

    /// <summary>
    /// The layout that makes the largest one rigid piece once a foundation slab joins its parts: a
    /// site's buildings stand apart, and one core needs one piece.
    /// </summary>
    void Largest()
    {
        PoiLibrary lib = PoiCatalog.Load(Paths.Repo("prototypes", "poi-generator", "data")).Library;
        var rows = new List<(string name, int raw, int alone, int slabbed, Dictionary<PInt3, Role> cells)>();
        foreach (FamilyDef f in lib.Families)
        for (int v = 0; v < f.Variants.Count; v++)
        {
            Layout l = PoiFactory.BuildLayout(lib, f, v, Rarity.Unique);
            var cells = new Dictionary<PInt3, Role>();
            foreach (var (c, r) in Solid(l)) cells[c] = r;
            if (cells.Count == 0) continue;
            int alone = Biggest(cells.Keys.ToHashSet()).Count;
            int x0 = cells.Keys.Min(p => p.X), x1 = cells.Keys.Max(p => p.X), z0 = cells.Keys.Min(p => p.Z), z1 = cells.Keys.Max(p => p.Z);
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                cells.TryAdd(new PInt3(x, Assembler.GroundY, z), Role.Floor);
            HashSet<PInt3> piece = Biggest(cells.Keys.ToHashSet());
            rows.Add(($"{f.Id}/{v}", cells.Count, alone, piece.Count, cells.Where(kv => piece.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value)));
        }
        foreach (var r in rows.OrderByDescending(r => r.slabbed).Take(5))
            GD.Print($"LIFT poi {r.name}: {r.raw} blocks with its slab, {r.alone} in its largest piece without one, {r.slabbed} rigid with it");
        var best = rows.OrderByDescending(r => r.slabbed).First();
        _poi = best.name;
        foreach (var kv in best.cells) _full[kv.Key] = kv.Value;
    }

    static HashSet<PInt3> Biggest(HashSet<PInt3> cells)
    {
        PInt3[] steps = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };
        var seen = new HashSet<PInt3>();
        var best = new HashSet<PInt3>();
        var queue = new Queue<PInt3>();
        foreach (PInt3 s in cells)
        {
            if (!seen.Add(s)) continue;
            var piece = new HashSet<PInt3>();
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                PInt3 p = queue.Dequeue();
                piece.Add(p);
                foreach (PInt3 d in steps)
                    if (cells.Contains(p + d) && seen.Add(p + d)) queue.Enqueue(p + d);
            }
            if (piece.Count > best.Count) best = piece;
        }
        return best;
    }

    /// <summary>A rung of the ladder: the middle of the largest, cropped to about this many blocks (0: all of it).</summary>
    Dictionary<PInt3, Role> Crop(int target)
    {
        if (target <= 0 || target >= _full.Count) return _full;
        int x0 = _full.Keys.Min(p => p.X), x1 = _full.Keys.Max(p => p.X), z0 = _full.Keys.Min(p => p.Z), z1 = _full.Keys.Max(p => p.Z);
        float mx = (x0 + x1) * 0.5f, mz = (z0 + z1) * 0.5f, lo = 0f, hi = 1f;
        Dictionary<PInt3, Role> kept = null;
        for (int i = 0; i < 12; i++)
        {
            float f = (lo + hi) * 0.5f;
            float hx = (x1 - x0) * 0.5f * f, hz = (z1 - z0) * 0.5f * f;
            var cells = _full.Where(kv => MathF.Abs(kv.Key.X - mx) <= hx && MathF.Abs(kv.Key.Z - mz) <= hz).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (cells.Count == 0) { lo = f; continue; }
            HashSet<PInt3> piece = Biggest(cells.Keys.ToHashSet());
            kept = cells.Where(kv => piece.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (kept.Count > target) hi = f;
            else lo = f;
        }
        return kept;
    }

    /// <summary>The cells as a capital hull: one core, generators, a gyro, and enough lift jets let into the slab.</summary>
    Hull Build(Dictionary<PInt3, Role> cells)
    {
        Structure sample = W.LoadBlueprint("bastion.gbp");
        Catalogue cat = sample.Catalogue;
        SBlockType Type(string id) => cat.TryGetType(id, out SBlockType t) ? t : throw new InvalidOperationException($"no block {id}");
        SBlock jet = sample.Blocks().Select(b => b.block).First(b => sample.TypeOf(b).IsThruster && Orientations.ToWorld(b.Rot, GalHack.Structures.Dir.NegZ).Normal().Y > 0.9f);
        SBlock Like(string id) => sample.Blocks().Select(b => b.block).First(b => sample.TypeOf(b) == Type(id));

        int cx = (cells.Keys.Min(p => p.X) + cells.Keys.Max(p => p.X)) / 2, cz = (cells.Keys.Min(p => p.Z) + cells.Keys.Max(p => p.Z)) / 2;
        int slab = Assembler.GroundY;

        for (int step = 24; ; step = step > 8 ? step - 4 : step - 1)
        {
            var s = new Structure(cat, sample.Grid) { Name = $"Mega {cells.Count}", Mode = CoreMode.Capital };
            foreach (var (p, role) in cells)
            {
                string type = role == Role.Window ? "glass" : role == Role.Wall ? "concrete" : "steel";
                s.Set(new SInt3(p.X - cx, p.Y, p.Z - cz), GalHack.Structures.Building.Make(s, Type(type)));
            }
            int jets = 0;
            foreach (PInt3 p in cells.Keys)
            {
                if (p.Y != slab || (p.X - cx) % step != 0 || (p.Z - cz) % step != 0 || (p.X == cx && p.Z == cz)) continue;
                // One generator to thirty jets: short power derates every jet.
                s.Set(new SInt3(p.X - cx, p.Y, p.Z - cz), ++jets % 30 == 0 ? Like("generator") : jet);
            }
            // Beside the core, in the slab: the only fittings a jet needs.
            s.Set(new SInt3(0, slab, 0), Like("core"));
            s.Set(new SInt3(1, slab, 0), Like("generator"));
            s.Set(new SInt3(-1, slab, 0), Like("generator"));
            s.Set(new SInt3(1, slab, 1), Like("fueltank"));
            s.Set(new SInt3(-1, slab, 1), Like("gyro"));
            // Four CPU extenders for the top tier: past the core's own 200,000 CPU every jet derates.
            for (int e = 2; e < 6; e++) s.Set(new SInt3(e, slab, 0), GalHack.Structures.Building.Make(s, Type("cpuext")));
            s.DirtyChunks.Clear();

            HandlingStats h = HandlingModel.Compute(new StructureVessel(s, W.Handling), new FlightConditions(W.Physics.Zone.Strength, true), W.Handling);
            float weight = h.Mass * W.Physics.Zone.Strength;
            if (h.UpwardThrust >= weight * 1.25f || step == 1)
            {
                if (h.UpwardThrust < weight) GD.Print($"FAIL even a jet in every slab block can't lift {h.Mass:F0} t");
                return new Hull(s, s.Count, jets, h.Mass, h.UpwardThrust, weight, slab);
            }
        }
    }

    /// <summary>Where the hull stands: half its height under the mean ground, as deep as bedrock lets it.</summary>
    N.Vector3 Seat(Hull hull, Row row)
    {
        Structure s = hull.Structure;
        (SInt3 min, SInt3 max) = s.Bounds();
        float cell = s.Grid.CellSize;
        N.Vector2 centre = W.SiteCentre;
        float mean = 0f, bedrock = float.MinValue, top = float.MinValue;
        int n = 0;
        for (int x = min.X; x <= max.X; x += 8)
        for (int z = min.Z; z <= max.Z; z += 8)
        {
            float wx = centre.X + x * cell, wz = centre.Y + z * cell, g = W.GroundHeight(wx, wz);
            mean += g;
            top = MathF.Max(top, g);
            n++;
            int y = (int)MathF.Floor(g) - 1;
            while (W.Ground.Check(new TInt3((int)wx, y, (int)wz)) is Verdict.Removable or Verdict.AlreadyAir && y > g - 400) y--;
            bedrock = MathF.Max(bedrock, y + 1f);
        }
        mean /= n;
        float height = (max.Y - hull.SlabY) * cell;
        float wanted = _half
            ? mean - (max.Y - min.Y + 1) * cell * 0.5f - min.Y * cell + cell * 0.5f
            : mean - height * 0.5f - hull.SlabY * cell;
        float floor = bedrock + 6f + cell * 0.5f - min.Y * cell; // the lowest block stays over bedrock
        float y0 = MathF.Max(wanted, floor);
        row.Buried = mean - (y0 + hull.SlabY * cell);
        row.Sunk = mean - (y0 + (min.Y - 0.5f) * cell);
        row.Mean = mean;
        row.Top = top;
        var tops = new Dictionary<(int, int), int>();
        int under = 0;
        foreach (var (p, _) in s.Blocks())
        {
            float wx = centre.X + p.X * cell, wz = centre.Y + p.Z * cell;
            if (y0 + p.Y * cell < W.GroundHeight(wx, wz)) under++;
            tops[(p.X, p.Z)] = tops.TryGetValue((p.X, p.Z), out int was) ? Math.Max(was, p.Y) : p.Y;
        }
        row.BlocksUnder = under / (float)s.Count;
        row.RoofUnder = tops.Count(kv => y0 + (kv.Value + 0.5f) * cell < W.GroundHeight(centre.X + kv.Key.Item1 * cell, centre.Y + kv.Key.Item2 * cell)) / (float)tops.Count;
        row.Width = (max.X - min.X + 1) * cell;
        row.Depth = (max.Z - min.Z + 1) * cell;
        row.Height = (max.Y - min.Y + 1) * cell;
        if (y0 > wanted + 0.01f) row.Note += $"bedrock limits the slab to {row.Buried:F0} m deep (half would be {height * 0.5f:F0}); ";
        return new N.Vector3(centre.X, y0, centre.Y);
    }

    static double MemMb()
    {
        using var p = Process.GetCurrentProcess();
        return p.WorkingSet64 / 1048576.0;
    }

    static PilotInput Up(Craft c, float speed)
    {
        float top = c.Flight.Stats?.TopSpeed ?? 100f;
        return PilotInput.Hands(MathX.Unrotate(c.Body.Rotation, N.Vector3.UnitY * Math.Min(1f, speed / top)).ToGodot());
    }

    static float HullBottom(Craft c) => c.Body.Position.Y + c.Structure.Bounds().min.Y * c.Structure.Grid.CellSize - c.Structure.Grid.CellSize * 0.5f;

    // ==== The numbers =============================================================================

    IEnumerable<int> Numbers()
    {
        var best = new List<Row>();
        foreach (int rung in _half ? new[] { 0 } : _rungs)
        {
            Hull hull = Build(Crop(rung));
            var rows = new List<Row>();
            GD.Print($"LIFT hull {hull.Blocks} blocks, {hull.Jets} jets, {hull.Tonnes:F0} t, lift {hull.Upward:F0} kN against {hull.Weight:F0} kN");
            for (int t = 0; t < _trials; t++)
            {
                var row = new Row();
                foreach (int x in Trial(hull, row, t == 0)) yield return x;
                Print("TRIAL", row);
                rows.Add(row);
                W.Clear();
                W.Ground.Restore(new EditLog());
                W.Patch.RebuildAllNow();
                _rescues.Clear();
                _impacts.Clear();
                if (row.Cells == 0) break;
            }
            // Best of the windows, as other runs may share the machine: each cost's own best.
            Row b = rows[0];
            b.PlanMs = rows.Min(r => r.PlanMs);
            b.HitchMs = rows.Min(r => r.HitchMs);
            b.CrumbleMs = rows.Min(r => r.CrumbleMs);
            b.FirstFrameMs = rows.Min(r => r.FirstFrameMs);
            b.CutWorstMs = rows.Min(r => r.CutWorstMs);
            b.CutSeconds = rows.Min(r => r.CutSeconds);
            b.ClimbMedianMs = rows.Min(r => r.ClimbMedianMs);
            b.ClimbWorstMs = rows.Min(r => r.ClimbWorstMs);
            b.ScriptMedianMs = rows.Min(r => r.ScriptMedianMs);
            b.BuildMs = rows.Min(r => r.BuildMs);
            b.CollidersMs = rows.Min(r => r.CollidersMs);
            b.SeatMs = rows.Min(r => r.SeatMs);
            b.Stalls = rows.Max(r => r.Stalls);
            b.Sideways = rows.Max(r => r.Sideways);
            b.Tilt = rows.Max(r => r.Tilt);
            b.RiseSpread = rows.Max(r => r.RiseSpread);
            b.Rescues = rows.Max(r => r.Rescues);
            b.Impacts = rows.Max(r => r.Impacts);
            best.Add(b);
        }
        foreach (Row r in best) Print("BEST", r);
        GD.Print($"LIFT summary: {best.Count} sizes, {_failures} failed");
    }

    static void Print(string tag, Row r) => GD.Print(
        $"LIFT {tag} blocks={r.Blocks} jets={r.Jets} size={r.Width:F0}x{r.Depth:F0}x{r.Height:F0}m buried={r.Buried:F0}m sunk={r.Sunk:F0}m under={r.BlocksUnder:P0} roofUnderRock={r.RoofUnder:P0} climb={r.Climb:F0}m cells={r.Cells} chunks={r.ChunksCut} " +
        $"planMs={r.PlanMs:F0} editHitchMs={r.HitchMs:F0} crumbleMs={r.CrumbleMs:F0} firstFrameMs={r.FirstFrameMs:F0} " +
        $"cutWorstMs={r.CutWorstMs:F1} cutTicks={r.CutTicks} cutS={r.CutSeconds:F1} idleMs={r.IdleMs:F1} climbMedMs={r.ClimbMedianMs:F1} climbWorstMs={r.ClimbWorstMs:F1} scriptMedMs={r.ScriptMedianMs:F1} clearanceMs={r.ClearanceMs:F1} " +
        $"adoptMs={r.BuildMs:F0} collidersMs={r.CollidersMs:F0} shapes={r.Shapes} seatMs={r.SeatMs:F0} memMB={r.MemMb:F0} " +
        $"stalls={r.Stalls} sideways={r.Sideways:F2} tilt={r.Tilt:F3} riseSpread={r.RiseSpread:F2} rescues={r.Rescues} impacts={r.Impacts} floating={r.Floating}/{r.FloatingCells} {r.Note}");

    IEnumerable<int> Trial(Hull hull, Row row, bool inspect)
    {
        row.Blocks = hull.Blocks;
        row.Jets = hull.Jets;
        N.Vector3 at = Seat(hull, row);
        Structure s = hull.Structure;
        var watch = Stopwatch.StartNew();
        Craft c = W.Adopt("mega", s, at, N.Quaternion.Identity);
        row.BuildMs = watch.Elapsed.TotalMilliseconds;
        row.Shapes = c.Shapes.ShapeCount;
        watch.Restart();
        StructureBridge.Colliders(s);
        row.CollidersMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        W.RestoreAnchor(c, at, N.Quaternion.Identity);
        W.Patch.RebuildAllNow();
        row.SeatMs = watch.Elapsed.TotalMilliseconds;

        var idle = new List<double>();
        for (int i = 0; i < 30; i++)
        {
            yield return 0;
            if (i > 2) idle.Add(_frameMs);
        }
        row.IdleMs = Median(idle);

        watch.Restart();
        string reply = W.LiftOffFrom(c);
        row.HitchMs = watch.Elapsed.TotalMilliseconds;
        LiftOff run = W.LiftOffOf(c);
        if (run == null)
        {
            row.Note += reply;
            GD.Print($"FAIL refused: {reply}");
            _failures++;
            yield break;
        }
        row.PlanMs = run.PlanMs;
        row.HitchMs -= run.PlanMs;
        row.Cells = run.Edit.Removed.Count;
        row.ChunksCut = run.Edit.DirtyChunks.Count;
        row.Climb = run.Plan.Climb;
        row.MemMb = MemMb();

        var climbing = new List<double>();
        var scripts = new List<double>();
        var rises = new List<float>();
        N.Vector3 start = c.Body.Position;
        int ticks = 0;
        yield return 0;
        row.FirstFrameMs = _frameMs;
        var cutClock = Stopwatch.StartNew();
        while (W.IsLiftingOff(c) && ticks++ < 18000)
        {
            yield return 0;
            if (run.Phase == LiftOffPhase.Cutting)
            {
                row.CutWorstMs = Math.Max(row.CutWorstMs, _frameMs);
                row.CutTicks = run.CuttingTicks;
                row.CutSeconds = cutClock.Elapsed.TotalSeconds;
                continue;
            }
            climbing.Add(_frameMs);
            scripts.Add(_scriptMs);
            Track(c, start, row);
            if (run.TopRise >= W.Rules.Liftoff.ClimbSpeed * 0.9f) rises.Add(c.Body.Velocity.Y);
            if (run.ClimbTicks == 120 && run.Climbed < 0.05f) Blocked(c);
            if (run.ClimbTicks % 30 == 0) row.ClearanceMs = Math.Max(row.ClearanceMs, TimeClearance(c));
        }
        row.Stalls = run.Stalls;
        if (W.IsLiftingOff(c))
        {
            row.Note += $"SNAGGED at {run.Climbed:F1} of {run.Plan.Climb:F1} m; ";
            GD.Print($"FAIL snagged at {run.Climbed:F1} m");
            _failures++;
        }

        // The pilot has it: out of the pit, faster, until the lowest block is well over the ground.
        float clear = row.Top + 10f;
        int free = 0, stuck = 0;
        float was = c.Body.Position.Y;
        while (HullBottom(c) < clear && free++ < 1800)
        {
            c.Flight.Input = Up(c, FreeClimbSpeed);
            yield return 0;
            climbing.Add(_frameMs);
            scripts.Add(_scriptMs);
            Track(c, start, row);
            if (c.Body.Position.Y - was < 0.001f) stuck++;
            was = c.Body.Position.Y;
        }
        if (stuck > 60) row.Note += $"stopped rising for {stuck} ticks out of the pit; ";
        row.ClimbMedianMs = Median(climbing);
        row.ClimbWorstMs = climbing.Count == 0 ? 0 : climbing.Max();
        row.ScriptMedianMs = Median(scripts);
        if (rises.Count > 1)
        {
            float mean = rises.Average();
            row.RiseSpread = rises.Max(v => MathF.Abs(v - mean));
        }
        row.Rescues = _rescues.Count;
        row.Impacts = _impacts.Count;

        watch.Restart();
        W.Debris.Crumble(run.Edit.Removed, W.Planet, 0.01f);
        row.CrumbleMs = watch.Elapsed.TotalMilliseconds;
        if (inspect) (row.Floating, row.FloatingCells) = Floating(run.Edit);
        row.MemMb = Math.Max(row.MemMb, MemMb());
    }

    /// <summary>One tick's look under a hull for a place to rest (Craft.CheckRestingOnHull), which walks every block.</summary>
    static double TimeClearance(Craft c)
    {
        var watch = Stopwatch.StartNew();
        c.CheckRestingOnHull(0f);
        return watch.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// The place on the planet where a hull this wide can sink deepest: the mean ground over its
    /// footprint against the highest bedrock under it (30 m below the low ground within 48 m).
    /// </summary>
    static N.Vector2 Massif(PlanetSurface.World.PlanetWorld planet, N.Vector2 near, int span) => Massif(planet, near, span, out _, false);

    static N.Vector2 Massif(PlanetSurface.World.PlanetWorld planet, N.Vector2 near, int span, out float room, bool quiet)
    {
        const int Grid = 32, Reach = 1; // fewer samples than the terrain's own 48 m: errs high, so the seat below holds
        int nx = (int)(planet.Width / Grid), nz = (int)(planet.Height / Grid), half = (span / 2 + Grid) / Grid;
        var heights = new float[nx * nz];
        System.Threading.Tasks.Parallel.For(0, nz, z =>
        {
            for (int x = 0; x < nx; x++) heights[x + nx * z] = planet.Source.SurfaceHeight(x * Grid, z * Grid);
        });
        float H(int x, int z) => heights[((x % nx) + nx) % nx + nx * Math.Clamp(z, 0, nz - 1)];
        var floors = new float[nx * nz];
        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            float low = float.MaxValue;
            for (int j = -Reach; j <= Reach; j++)
            for (int i = -Reach; i <= Reach; i++)
                low = MathF.Min(low, H(x + i, z + j));
            floors[x + nx * z] = low;
        }
        (float depth, int x, int z, float mean, float relief) best = (float.MinValue, 0, 0, 0, 0);
        for (int z = nz / 8; z < nz - nz / 8; z++)
        for (int x = 0; x < nx; x++)
        {
            float sum = 0f, lo = float.MaxValue, hi = float.MinValue, bed = float.MinValue;
            for (int j = -half; j <= half; j++)
            for (int i = -half; i <= half; i++)
            {
                float h = H(x + i, z + j);
                sum += h;
                lo = MathF.Min(lo, h);
                hi = MathF.Max(hi, h);
                bed = MathF.Max(bed, floors[((x + i) % nx + nx) % nx + nx * Math.Clamp(z + j, 0, nz - 1)]);
            }
            if (lo < 3f || hi - lo > 60f) continue;
            float mean = sum / ((half * 2 + 1) * (half * 2 + 1));
            float depth = mean - (bed - PlanetTerrainSource.BedrockDepth);
            if (depth > best.depth) best = (depth, x, z, mean, hi - lo);
        }
        room = best.depth;
        if (!quiet) GD.Print($"LIFT massif at {best.x * Grid},{best.z * Grid}: mean ground {best.mean:F0} m, relief {best.relief:F0} m, room to sink {best.depth:F0} m");
        return new N.Vector2(best.x * Grid, best.z * Grid);
    }

    /// <summary>What a hull that won't rise is resting against.</summary>
    static void Blocked(Craft c)
    {
        var hit = new KinematicCollision3D();
        bool hits = c.Hull.TestMove(c.Hull.GlobalTransform, Vector3.Up * 0.5f, hit, 0.02f);
        string what = hits ? $"{(hit.GetCollider() as Node)?.Name} at {hit.GetPosition()} normal {hit.GetNormal()}, safe {hit.GetTravel().Length():F3}" : "nothing";
        GD.Print($"LIFT blocked: velocity {c.Body.Velocity}, frozen {c.Vessel.Frozen}, settling {c.Vessel.Settling}, asleep {c.Body.Asleep}, pin {c.Pin != null}, hit {what}");
    }

    static void Track(Craft c, N.Vector3 start, Row row)
    {
        row.Sideways = MathF.Max(row.Sideways, MathX.Flat(c.Body.Position - start).Length());
        row.Tilt = MathF.Max(row.Tilt, c.Tilt);
    }

    static double Median(List<double> v)
    {
        if (v.Count == 0) return 0;
        List<double> sorted = v.OrderBy(x => x).ToList();
        return sorted[sorted.Count / 2];
    }

    /// <summary>Pieces of ground the cut left hanging: solid cells near it that reach nothing outside a box 12 m round it.</summary>
    (int pieces, int cells) Floating(PlanetEdit edit)
    {
        if (edit.Removed.Count == 0) return (0, 0);
        TInt3 lo = edit.Removed[0].Cell, hi = lo;
        foreach (RemovedCell r in edit.Removed)
        {
            lo = new TInt3(Math.Min(lo.X, r.Cell.X), Math.Min(lo.Y, r.Cell.Y), Math.Min(lo.Z, r.Cell.Z));
            hi = new TInt3(Math.Max(hi.X, r.Cell.X), Math.Max(hi.Y, r.Cell.Y), Math.Max(hi.Z, r.Cell.Z));
        }
        const int Pad = 12;
        int x0 = lo.X - Pad, y0 = lo.Y - Pad, z0 = lo.Z - Pad;
        int w = hi.X - lo.X + 1 + Pad * 2, h = hi.Y - lo.Y + 1 + Pad * 2, d = hi.Z - lo.Z + 1 + Pad * 2;
        var solid = new bool[w * h * d];
        var mark = new bool[w * h * d];
        int Index(int x, int y, int z) => x + w * (z + d * y);
        ChunkStore store = W.Ground.Store;
        for (int y = 0; y < h; y++)
        for (int z = 0; z < d; z++)
        for (int x = 0; x < w; x++)
            solid[Index(x, y, z)] = store.IsSolid(new TInt3(x0 + x, y0 + y, z0 + z));
        var stack = new Stack<(int, int, int)>();
        void Push(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= d) return;
            int i = Index(x, y, z);
            if (!solid[i] || mark[i]) return;
            mark[i] = true;
            stack.Push((x, y, z));
        }
        int Flood()
        {
            int n = 0;
            while (stack.Count > 0)
            {
                var (a, b, c) = stack.Pop();
                n++;
                Push(a + 1, b, c); Push(a - 1, b, c); Push(a, b + 1, c); Push(a, b - 1, c); Push(a, b, c + 1); Push(a, b, c - 1);
            }
            return n;
        }
        for (int y = 0; y < h; y++)
        for (int z = 0; z < d; z++)
        for (int x = 0; x < w; x++)
            if (x == 0 || y == 0 || z == 0 || x == w - 1 || y == h - 1 || z == d - 1)
                Push(x, y, z);
        Flood();
        int pieces = 0, cells = 0;
        for (int y = 0; y < h; y++)
        for (int z = 0; z < d; z++)
        for (int x = 0; x < w; x++)
        {
            int i = Index(x, y, z);
            if (!solid[i] || mark[i]) continue;
            pieces++;
            Push(x, y, z);
            cells += Flood();
        }
        return (pieces, cells);
    }

    // ==== The pictures ============================================================================

    IEnumerable<int> Pictures()
    {
        Directory.CreateDirectory(Path.Combine(_shots, "frames"));
        Hull hull = Build(_full);
        var row = new Row();
        N.Vector3 at = Seat(hull, row);
        GD.Print($"LIFT hull {hull.Blocks} blocks, {hull.Jets} jets, {hull.Tonnes:F0} t, sunk {row.Sunk:F0} m of {row.Height:F0}, {row.BlocksUnder:P0} of blocks under ground, {row.RoofUnder:P0} of roof under rock, ground {row.Mean:F0} to {row.Top:F0} {row.Note}");
        Craft c = W.Adopt("mega", hull.Structure, at, N.Quaternion.Identity);
        W.RestoreAnchor(c, at, N.Quaternion.Identity);
        W.Patch.RebuildAllNow();

        // One camera from start to end, well back and high, the whole hull in frame.
        float span = MathF.Max(MathF.Max(row.Width, row.Depth), row.Height);
        float ground = row.Mean;
        var look = new Vector3(at.X, ground + row.Height * 0.3f, at.Z);
        Vector3 eye = look + new Vector3(-0.62f, 0.42f, -0.86f) * span * 1.12f;
        eye.Y = MathF.Max(eye.Y, MathF.Max(W.GroundHeight(eye.X, eye.Z), row.Top) + 40f);
        _main.FixedCamera = new Transform3D(Basis.LookingAt(look - eye, Vector3.Up), eye);

        for (int i = 0; i < 1800 && (i < 120 || !W.Patch.FarReady); i++) yield return 0;
        Still("01-before");
        yield return 0;
        yield return 0;
        if (_preview) yield break;

        var debris = new MegaDebris { Name = "MegaDebris" };
        AddChild(debris);
        int frame = 0, ticks = 0, after = 0;
        string reply = W.LiftOffFrom(c);
        LiftOff run = W.LiftOffOf(c);
        GD.Print($"LIFT {reply}");
        if (run == null)
        {
            _failures++;
            yield break;
        }
        debris.Start(run.Edit, W.Planet);
        var frames = new List<double>();
        bool part = false, cut = false;
        int pending = W.Patch.Pending;
        float clear = row.Top + 30f;
        N.Vector3 start = c.Body.Position;
        while (after < 30 && ticks < 6000)
        {
            bool shot = ticks++ % _every == 0;
            if (shot) _grabSmall = Path.Combine(_shots, "frames", $"f{frame++:D4}.png");
            if (!W.IsLiftingOff(c)) c.Flight.Input = Up(c, FreeClimbSpeed);
            if (!cut && (W.Patch.Pending <= pending / 2 || ticks == 120))
            {
                cut = true;
                Still("02-mid-cut");
            }
            if (!part && c.Body.Position.Y - start.Y > (clear - start.Y) * 0.32f)
            {
                part = true;
                Still("03-partway");
            }
            if (HullBottom(c) >= clear)
            {
                if (after == 0) Still("04-clear");
                after++;
            }
            yield return 0;
            if (!shot && ticks > 2) frames.Add(_frameMs);
            if (ticks % 120 == 0) GD.Print($"LIFT tick {ticks}: {run.Phase}, up {c.Body.Position.Y - start.Y:F1} m, pending {W.Patch.Pending}, clods {W.Debris.Live}+{debris.Live}, debris update {debris.LastMs:F2} ms, frame {_frameMs:F1} ms");
        }
        GD.Print($"LIFT windowed: plan {run.PlanMs:F0} ms, {run.Edit.Removed.Count} cells, climb {run.Plan.Climb:F0} m, stalls {run.Stalls}, rescues {_rescues.Count}, {frame} frames, {ticks} ticks, cut {run.CuttingTicks} ticks, median frame {Median(frames):F1} ms, worst {(frames.Count > 0 ? frames.Max() : 0):F1} ms, peak debris update {debris.PeakMs:F2} ms at {debris.Count} clods");
        yield return 0;
        yield return 0;
    }

    void Still(string name) => _grab = Path.Combine(_shots, name + ".png");

    void Grab()
    {
        if (_grab == null && _grabSmall == null) return;
        Image image = GetViewport().GetTexture().GetImage();
        if (_grab != null)
        {
            image.SavePng(_grab);
            GD.Print($"LIFT saved {_grab}");
            _grab = null;
        }
        if (_grabSmall != null)
        {
            image.Resize(800, 450, Image.Interpolation.Bilinear);
            image.SavePng(_grabSmall);
            _grabSmall = null;
        }
    }
}

/// <summary>Runs after everything else in a physics tick, to time it.</summary>
public partial class Tail : Node
{
    public Action Done;
    public override void _Ready() => ProcessPhysicsPriority = 100;
    public override void _PhysicsProcess(double delta) => Done?.Invoke();
}

/// <summary>
/// The test's own debris, for the pictures and for the question of a cap: the game's 1,500 small
/// clods can't be seen from where a hull this size fits in frame, so these are many more and far
/// bigger, shed over the first seconds. Not in the game. One multimesh, its buffer written whole
/// each frame.
/// </summary>
public partial class MegaDebris : Node3D
{
    public const int Most = 40000;
    const float SpreadSeconds = 7f;
    const int Stride = 16; // 12 for the transform, 4 for the colour

    MultiMesh _mesh;
    float[] _buffer, _size, _born, _life;
    Vector3[] _position, _velocity;
    Color[] _color;
    float _age;

    public int Count { get; private set; }
    public int Live { get; private set; }
    public double LastMs { get; private set; }
    public double PeakMs { get; private set; }

    public void Start(PlanetEdit edit, PlanetSurface.World.PlanetWorld planet)
    {
        int cells = edit.Removed.Count;
        Count = Math.Min(Most, cells);
        _position = new Vector3[Count];
        _velocity = new Vector3[Count];
        _size = new float[Count];
        _born = new float[Count];
        _life = new float[Count];
        _color = new Color[Count];
        _buffer = new float[Count * Stride];
        var random = new Random(47);
        for (int i = 0; i < Count; i++)
        {
            RemovedCell r = edit.Removed[(int)((long)i * cells / Count)];
            _position[i] = new Vector3(r.Cell.X + 0.5f, r.Cell.Y + 0.5f, r.Cell.Z + 0.5f);
            _velocity[i] = new Vector3((float)(random.NextDouble() - 0.5) * 9f, 3f + (float)random.NextDouble() * 15f, (float)(random.NextDouble() - 0.5) * 9f);
            _size[i] = 0.8f + (float)random.NextDouble() * 2.6f;
            _born[i] = MathF.Pow((float)random.NextDouble(), 2f) * SpreadSeconds; // most at first, tailing off
            _life[i] = 2.5f + (float)random.NextDouble() * 2.5f;
            _color[i] = planet.Palette.Vertex(r.Material, r.Cell.X, r.Cell.Z, r.Cell.Y, out _);
        }
        _mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One },
            InstanceCount = Count,
        };
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = _mesh,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.95f },
            CustomAabb = new Aabb(-Vector3.One * 8000f, Vector3.One * 16000f),
        });
    }

    public override void _Process(double delta)
    {
        if (_mesh == null) return;
        var watch = Stopwatch.StartNew();
        float dt = (float)delta;
        _age += dt;
        int live = 0;
        for (int i = 0; i < Count; i++)
        {
            float t = _age - _born[i];
            float scale = 0f;
            if (t > 0f && t < _life[i])
            {
                _velocity[i] += Vector3.Down * 9.8f * dt;
                _position[i] += _velocity[i] * dt;
                scale = _size[i] * Mathf.Clamp((_life[i] - t) / (_life[i] * 0.3f), 0f, 1f);
                live++;
            }
            int o = i * Stride;
            _buffer[o] = scale; _buffer[o + 1] = 0f; _buffer[o + 2] = 0f; _buffer[o + 3] = _position[i].X;
            _buffer[o + 4] = 0f; _buffer[o + 5] = scale; _buffer[o + 6] = 0f; _buffer[o + 7] = _position[i].Y;
            _buffer[o + 8] = 0f; _buffer[o + 9] = 0f; _buffer[o + 10] = scale; _buffer[o + 11] = _position[i].Z;
            _buffer[o + 12] = _color[i].R; _buffer[o + 13] = _color[i].G; _buffer[o + 14] = _color[i].B; _buffer[o + 15] = 1f;
        }
        _mesh.Buffer = _buffer;
        Live = live;
        LastMs = watch.Elapsed.TotalMilliseconds;
        PeakMs = Math.Max(PeakMs, LastMs);
    }
}
