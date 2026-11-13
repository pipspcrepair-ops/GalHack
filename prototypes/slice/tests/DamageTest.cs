using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Surface;
using Slice.Vessels;
using Weather.Model;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// Block damage end to end, in the slice's real world: a vessel really dropped on the ground, a
/// vessel really flown into a base, shots, bites, lightning, severing, repair and a 200-hit
/// barrage against the frame budget. Building's tests/DamageTest covers the mesher itself. Run:
///   .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/DamageTest.tscn
/// (test-all adds --fixed-fps 60 from DamageTest.args.) Exits 0 if every check passes, 1 if not.
/// </summary>
public partial class DamageTest : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly List<ImpactInfo> _impacts = new();

    SurfaceZone Zone => _game.Zone;
    Catalogue Cat => Zone.Data.Catalogue;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100;
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(10));
            Console.Error.WriteLine("FAIL  the run took over 10 minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true, Name = "DamageTest watchdog" };
        thread.Start();
        _run = Scenarios().GetEnumerator();
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
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
        if (ok) _passes++;
        else _failures++;
    }

    IEnumerable<int> Scenarios()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        Zone.Weather.Bridge.Calm = true;
        Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Zone.Wildlife.Frozen = true;
        Zone.Physics.Impact += _impacts.Add;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;

        // The slice's catalogue has blocks building's own doesn't: constructors, guns, precursor alloy.
        {
            BlockBuilding.OcclusionCheck.Result r = BlockBuilding.OcclusionCheck.Run(Cat);
            foreach (string w in r.Worst) GD.Print($"      {w}");
            Check(r.Holes == 0, $"no block leaves a hole: {r.Cases} cases, {r.Culled} cube faces hidden, {r.Holes} holes");
        }

        foreach (int x in Shots()) yield return x;
        foreach (int x in Crash()) yield return x;
        foreach (int x in Ram()) yield return x;
        foreach (int x in OnlyAffectedChunks()) yield return x;
        foreach (int x in Barrage()) yield return x;
        foreach (int x in Repair()) yield return x;
        foreach (int x in Severing()) yield return x;
        foreach (int x in CoreAndSeat()) yield return x;
        foreach (int x in BiteAndLightning()) yield return x;
        foreach (int x in Hull()) yield return x;
    }

    // ---- Staging --------------------------------------------------------------------------

    N.Vector3 Spot(float east, float height)
    {
        Vector3 p = _game.Survivor.WorldFeet + new Vector3(east, 0f, 40f);
        Vector3 g = Zone.GroundAt(p.X, p.Z);
        return new N.Vector3(g.X, g.Y + height, g.Z);
    }

    /// <summary>A flat wall of blocks, x along it, z through it, as a base: nothing ever moves it.</summary>
    Vessel Wall(int width, int height, N.Vector3 at, string type = "steel", int depth = 1)
    {
        var s = new Structure(Cat) { Name = $"Wall{_walls++}" };
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                for (int z = 0; z < depth; z++)
                    s.Set(new Int3(x, y, z), Building.Make(s, Cat.Type(type)));
        s.DirtyChunks.Clear();
        return Vessel.SpawnBase(Zone, s, at, 0f);
    }

    int _walls;

    static int Worst(ChunkMeshData d) => d.Opaque.UV2s.Count == 0 ? 0 : d.Opaque.UV2s.Max(uv => ((int)uv.Y >> BlockMaterials.DamageShift) & 3);

    int WorstInMesh(Vessel v, Int3 cell) => Worst(ChunkMesher.Build(v.Structure, Structure.ChunkOf(cell)));

    static N.Vector3 Up => N.Vector3.UnitY;

    /// <summary>The world point on a cell's -Z face, and that face's normal: where a shot from the front lands.</summary>
    (N.Vector3 point, N.Vector3 normal) Front(Vessel v, Int3 cell)
    {
        N.Vector3 local = StructureBridge.CellCenter(v.Structure, cell) - N.Vector3.UnitZ * v.Structure.Grid.CellSize * 0.5f;
        return (v.Body.ToParent(local), MathX.Rotate(v.Body.Rotation, -N.Vector3.UnitZ));
    }

    void Shoot(Vessel v, Int3 cell, float amount, DamageType type = DamageType.Energy)
    {
        var (point, normal) = Front(v, cell);
        v.Hits.Shot(cell, point, normal, amount, type);
    }

    // ---- Scenarios ------------------------------------------------------------------------

    IEnumerable<int> Shots()
    {
        Vessel v = Wall(6, 6, Spot(-10f, 1f));
        var cell = new Int3(2, 2, 0);
        float max = v.Structure.MaxHp(v.Structure.Get(cell));
        float shot = Zone.Data.Rules.Tool.ShotDamage;
        foreach (int x in _drive.Wait(2)) yield return x;
        int rebuilt = v.View.Rebuilt;

        Shoot(v, cell, shot);
        Check(v.Hits.Queued == 1 && v.Structure.Get(cell).Hp == max, "a hit waits for the frame: nothing changes in the middle of a physics step");
        foreach (int x in _drive.Wait(2)) yield return x;
        float hp = v.Structure.Get(cell).Hp, expected = max - (shot - 10f) * 0.9f;
        Check(MathF.Abs(hp - expected) < 0.01f, $"a survival tool bolt lowers a block's hit points, armour and resistance applied: {max:F0} to {hp:F1}");
        Check(v.View.Rebuilt > rebuilt && v.View.Waiting == 0, "and its chunk is redrawn within two frames");
        Check(v.Body.Damage > 0f, $"and it counts on the hull, down the same path as a crash ({v.Body.Damage:F4})");

        // A plated 1 m cube outlasts many tool bolts: heavier ones, sized to wear it away in six.
        float wear = hp / 5.5f / 0.9f + 10f;
        var seen = new List<int>();
        for (int i = 0; i < 7 && v.Structure.Has(cell); i++)
        {
            seen.Add(WorstInMesh(v, cell));
            Shoot(v, cell, wear);
            foreach (int x in _drive.Wait(2)) yield return x;
        }
        Check(!v.Structure.Has(cell) && seen.Distinct().Count() >= 3 && seen.SequenceEqual(seen.OrderBy(b => b)),
            $"six heavy bolts wear a plated cube away stage by stage, then it's gone: {string.Join(" ", seen)}");
        Check(v.View.Fx.Marks > 0 && v.View.Fx.Bursts > 0, $"each hit leaves sparks and an impact mark ({v.View.Fx.Bursts} bursts, {v.View.Fx.Marks} marks)");
        Check(v.View.Fx.Chips > 0, $"a destroyed block throws chips ({v.View.Fx.Chips})");

        Vessel armour = Wall(2, 2, Spot(-20f, 1f), "hardened");
        Shoot(armour, new Int3(1, 1, 0), shot);
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(armour.Structure.Get(new Int3(1, 1, 0)).Hp == armour.Structure.MaxHp(armour.Structure.Get(new Int3(1, 1, 0))), "the tool's small bolt doesn't get through hardened armour");

        foreach (int x in _drive.Wait(60 * 8)) yield return x; // a chip lives up to 3 s in the air, 3 s at rest and 1.2 s fading
        Check(v.View.Fx.Chips == 0, "eight seconds on, every chip has settled and faded: nothing is left floating");
    }

    IEnumerable<int> Crash()
    {
        Structure s = Zone.Data.Blueprint("starter.gbp");
        s.Name = "Crasher";
        Playthrough.Clearing(Zone, Spot(30f, 0f).ToGodot(), 12f); // vessels collide with trunks and crowns: a crown would catch it
        // Low over the ground: the Skiff's flight assist brakes at over 100 m/s² on its 1 m grid thrusters, and from higher it sets down gently.
        Vessel v = Vessel.Spawn(Zone, s, Spot(30f, 5f), 0f, landed: false);
        var (min, max) = s.Bounds();
        int total = s.Count;
        float hp0 = s.Blocks().Sum(b => b.block.Hp);
        v.Body.Velocity = new N.Vector3(0f, -60f, 0f);
        _impacts.Clear();
        ImpactInfo hit = null;
        for (int i = 0; i < 180 && hit == null; i++)
        {
            hit = _impacts.FirstOrDefault(x => x.Body == v.Body && x.Damage > 0f);
            yield return 0;
        }
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(hit != null, $"the vessel hits the ground for real: {hit?.Speed:F1} m/s, {hit?.Surface}, {hit?.Damage:F2} damage");
        if (hit == null) yield break;

        float formula = Impacts.Damage(v.Body.Mass + 0f, hit.Speed, Zone.Physics.Rules.Contacts, v.Body.Kind);
        Check(v.Body.Damage >= hit.Damage - 1e-3f, $"the hull counts what the one formula said ({v.Body.Damage:F2} of {v.Body.Integrity:F1}; formula at its present mass {formula:F2})");

        var worn = s.Blocks().Where(b => Building.IsDamaged(s, b.pos)).Select(b => b.pos).ToList();
        int lost = total - s.Count - Zone.Vessels.Where(o => o != v && o.Name.StartsWith("Crasher")).Sum(o => o.Structure.Count);
        float mid = (min.Y + max.Y) * 0.5f;
        int low = worn.Count(p => p.Y <= mid), high = worn.Count - low;
        Check(worn.Count + lost > 0 && low > high, $"the blocks on the impacted face took it: {low} low and {high} high worn, {lost} destroyed");
        Check(s.Blocks().Sum(b => b.block.Hp) < hp0, "its blocks have really lost hit points");
        Check(v.View.Waiting == 0 && Worst(ChunkMesher.Build(s, worn.Count > 0 ? Structure.ChunkOf(worn[0]) : Int3.Zero)) > 0, "and the crash shows in its mesh");
    }

    IEnumerable<int> Ram()
    {
        N.Vector3 at = Spot(70f, 2f);
        Vessel wall = Wall(16, 12, at, depth: 2);
        foreach (int x in _drive.Wait(2)) yield return x;

        Structure s = Zone.Data.Blueprint("starter.gbp");
        s.Name = "Rammer";
        float cell = wall.Structure.Grid.CellSize;
        N.Vector3 middle = at + new N.Vector3(8f * cell, 3f * cell, -14f);
        Vessel v = Vessel.Spawn(Zone, s, middle, 0f, landed: false);
        v.Body.Velocity = new N.Vector3(0f, 0f, 45f);
        _impacts.Clear();
        ImpactInfo hit = null;
        for (int i = 0; i < 120 && hit == null; i++)
        {
            hit = _impacts.FirstOrDefault(x => x.Body == v.Body && x.Other == wall.Body);
            yield return 0;
        }
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(hit != null, $"a vessel flown into a base hits it: {hit?.Speed:F1} m/s");
        if (hit == null) yield break;

        Structure w = wall.Structure;
        var front = w.Blocks().Where(b => b.pos.Z == 0 && Building.IsDamaged(w, b.pos)).ToList();
        var back = w.Blocks().Where(b => b.pos.Z == 1 && Building.IsDamaged(w, b.pos)).ToList();
        int gone = 16 * 12 * 2 - w.Count;
        Check(front.Count + gone > 0, $"the base's wall takes it at the contact: {front.Count} worn on the struck face, {back.Count} behind, {gone} gone");
        Check(s.Blocks().Any(b => Building.IsDamaged(s, b.pos)) || s.Count < 1 || Zone.Vessels.Any(o => o != v && o.Name.StartsWith("Rammer")), "and the rammer is hurt too");
    }

    IEnumerable<int> OnlyAffectedChunks()
    {
        Vessel v = Wall(48, 4, Spot(-40f, 1f));
        foreach (int x in _drive.Wait(2)) yield return x;
        int rebuilt = v.View.Rebuilt, chunks = v.Structure.Chunks.Count;
        Check(chunks >= 3, $"a wall across {chunks} chunks");
        Shoot(v, new Int3(15, 1, 0), 40f); // the last cell of its chunk
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(v.View.Rebuilt - rebuilt == 1, $"one hit, even on a chunk's edge, redraws one chunk of {chunks} ({v.View.Rebuilt - rebuilt})");
        Check(v.Structure.DirtyChunks.Count == 0, "and leaves nothing for the structure's edit path: colliders, mass and sleepers untouched");

        rebuilt = v.View.Rebuilt;
        Shoot(v, new Int3(20, 1, 0), 40f);
        Shoot(v, new Int3(21, 1, 0), 40f);
        Shoot(v, new Int3(22, 2, 0), 40f);
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(v.View.Rebuilt - rebuilt == 1, $"three hits in one chunk in one frame coalesce into one redraw ({v.View.Rebuilt - rebuilt})");
    }

    /// <summary>Three 200-hit barrages on a wall, frame by frame: the best of the three, and how many frames it took to draw.</summary>
    IEnumerable<int> Barrages(Vessel v, Random rng, string label, Action<(double ms, int frames, int chunks)> done)
    {
        var best = (ms: double.MaxValue, frames: 0, chunks: 0);
        for (int window = 0; window < 3; window++)
        {
            for (int i = 0; i < 200; i++) Shoot(v, new Int3(rng.Next(64), rng.Next(12), 0), 10.5f, DamageType.Kinetic);
            int rebuilt = v.View.Rebuilt, frames = 0;
            double worst = 0;
            var trace = new List<string>();
            // Real frames: each one, StructureMesh's _Process applies the hits and redraws, and says what it spent.
            do
            {
                int was = v.View.Rebuilt;
                yield return 0;
                frames++;
                worst = Math.Max(worst, v.View.LastFrameMs);
                trace.Add($"{v.View.Rebuilt - was} chunks {v.View.LastFrameMs:F2} ms");
            }
            while ((v.View.Waiting > 0 || v.Hits.Queued > 0) && frames < 30);
            GD.Print($"      {label} barrage {window + 1}: {string.Join(", ", trace)}");
            if (worst < best.ms) best = (worst, frames, v.View.Rebuilt - rebuilt);
            foreach (int x in _drive.Wait(6)) yield return x;
        }
        Check(v.Hits.HitsTaken == 600 && v.Hits.BlocksLost == 0, $"{label}: three 200-hit barrages all land and wear the wall without breaking it, so this is the redraw path alone ({v.Hits.HitsTaken} hits, {v.Hits.BlocksLost} blocks gone)");
        done(best);
    }

    IEnumerable<int> Barrage()
    {
        var rng = new Random(7);

        // The barrage the game should expect: a sound hull wall across several chunks, hit all over in one frame.
        Vessel fresh = Wall(64, 12, Spot(-70f, 1f), depth: 2);
        foreach (int x in _drive.Wait(3)) yield return x;
        var healthy = (ms: 0.0, frames: 0, chunks: 0);
        foreach (int x in Barrages(fresh, rng, "sound hull", r => healthy = r)) yield return x;
        Check(healthy.ms < 8.0 && healthy.frames <= 2, $"a 200-hit barrage on a sound hull costs {healthy.ms:F2} ms in its worst frame, drawn within {healthy.frames} frame(s), {healthy.chunks} chunks redrawn (best of three)");
        Check(fresh.View.Fx.Bursts <= 3 * 12, $"and shows as a handful of bursts, not 600 ({fresh.View.Fx.Bursts} all told)");

        // The dearest thing there is to redraw: both faces of a whole wall holed already.
        Vessel v = Wall(64, 12, Spot(-90f, 1f), depth: 2);
        foreach (var (p, b) in v.Structure.Blocks().ToList()) v.Structure.SetHp(p, v.Structure.MaxHp(b) * 0.24f);
        v.View.Queue(v.Structure.DirtyChunks);
        v.Structure.DirtyChunks.Clear();
        foreach (int x in _drive.Wait(8)) yield return x;

        // Where a chunk's cost goes.
        Int3 busiest = v.Structure.Chunks.Keys.OrderByDescending(c => v.Structure.Chunks[c].Count).First();
        var data = new ChunkMeshData();
        double meshing = double.MaxValue, upload = double.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            var clock = Stopwatch.StartNew();
            ChunkMesher.Build(v.Structure, busiest, data, colliders: false);
            meshing = Math.Min(meshing, clock.Elapsed.TotalMilliseconds);
            clock.Restart();
            ChunkMesher.ToMesh(data)?.Dispose();
            upload = Math.Min(upload, clock.Elapsed.TotalMilliseconds);
        }
        GD.Print($"      a fully holed wall chunk: {data.Opaque.VertexCount} vertices, {meshing:F2} ms to mesh, {upload:F2} ms to hand to the engine");

        var holed = (ms: 0.0, frames: 0, chunks: 0);
        foreach (int x in Barrages(v, rng, "holed wall", r => holed = r)) yield return x;
        Check(holed.ms < 16.6 && holed.frames <= holed.chunks, $"on a wall already holed through, the worst case, it spreads out, one chunk a frame: {holed.ms:F2} ms in its worst frame over {holed.frames} frames, {holed.chunks} chunks (best of three)");

        // The same with blocks going: the edit path runs too, once a frame however many went.
        var lost = (ms: double.MaxValue, gone: 0);
        Vessel w = Wall(64, 12, Spot(-110f, 1f), depth: 2);
        foreach (int x in _drive.Wait(2)) yield return x;
        for (int window = 0; window < 3; window++)
        {
            for (int i = 0; i < 200; i++) Shoot(w, new Int3(rng.Next(64), rng.Next(12), 0), 40f, DamageType.Kinetic);
            int before = w.Hits.BlocksLost;
            yield return 0;
            lost = w.View.LastFrameMs < lost.ms ? (w.View.LastFrameMs, w.Hits.BlocksLost - before) : lost;
            foreach (int x in _drive.Wait(6)) yield return x;
        }
        GD.Print($"      a barrage that destroys {lost.gone} blocks at once: {lost.ms:F2} ms (best of three), {w.Structure.Count} blocks left");
        Check(lost.ms < 33.0, $"a barrage that takes blocks out, edit path and all, stays inside a frame at 30 fps ({lost.ms:F2} ms, {lost.gone} blocks gone)");
    }

    IEnumerable<int> Repair()
    {
        Vessel v = Wall(4, 4, Spot(-150f, 1f));
        var cell = new Int3(1, 1, 0);
        foreach (int x in _drive.Wait(2)) yield return x;
        int clean = ChunkMesher.Build(v.Structure, Int3.Zero).Opaque.VertexCount;
        float max = v.Structure.MaxHp(v.Structure.Get(cell));
        v.Structure.SetHp(cell, max * 0.1f);
        v.Structure.DirtyChunks.Clear();
        int holed = WorstInMesh(v, cell);

        v.Storage.CapacitySu = 1000;
        bool survival = !Zone.WorldRules.Creative;
        Check(survival && v.Hits.Repair(cell) == RepairStatus.NotEnoughMaterials && v.Structure.Get(cell).Hp < max, "with its storage empty, nothing is repaired");
        Bill bill = v.Hits.RepairBill(cell);
        foreach (var (id, n) in bill) v.Storage.Add(id, n * 2);
        Bill full = v.Storage.Contents;
        Check(v.Hits.Repair(cell, 0.5f) == RepairStatus.Repaired && WorstInMesh(v, cell) < holed, $"half repaired, from the structure's shared storage, its look steps back ({holed} to {WorstInMesh(v, cell)})");
        Check(!v.Storage.Contents.Equals(full), $"and materials were consumed (a full repair would cost {Zone.Data.Items.Describe(bill, 3)})");
        Check(v.Hits.Repair(cell) == RepairStatus.Repaired, "the rest repaired");
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(v.Structure.Get(cell).Hp == max && ChunkMesher.Build(v.Structure, Int3.Zero).Opaque.VertexCount == clean && v.View.Waiting == 0,
            "and it is whole again, its mesh as it was and redrawn");
        Check(v.Hits.Repair(cell) == RepairStatus.NotDamaged, "a whole block has nothing to repair");
    }

    IEnumerable<int> Severing()
    {
        var s = new Structure(Cat) { Name = "Spar" };
        s.Set(Int3.Zero, Building.Make(s, Cat.CoreType));
        for (int x = 1; x < 8; x++) s.Set(new Int3(x, 0, 0), Building.Make(s, Cat.Type("steel")));
        s.DirtyChunks.Clear();
        Vessel v = Vessel.Spawn(Zone, s, Spot(-180f, 30f), 0f, landed: false);
        foreach (int x in _drive.Wait(2)) yield return x;
        int vessels = Zone.Vessels.Count;
        Shoot(v, new Int3(4, 0, 0), 1e6f, DamageType.Kinetic);
        foreach (int x in _drive.Wait(2)) yield return x;
        Vessel wreck = Zone.Vessels.FirstOrDefault(o => o.Name.StartsWith("Spar wreck"));
        Check(s.Count == 4 && wreck != null && wreck.Structure.Count == 3 && Zone.Vessels.Count == vessels + 1,
            $"a shot through a spar severs what's beyond it: {s.Count} blocks stay, {wreck?.Structure.Count} become a wreck");
        Check(wreck?.Body.Kind == BodyKind.Wreck && wreck.Body.DamageSink == wreck.Hits, "the wreck is a free body, and takes damage in its own right");
    }

    IEnumerable<int> CoreAndSeat()
    {
        Structure s = Zone.Data.Blueprint("starter.gbp");
        s.Name = "Seated";
        Vessel v = Vessel.Spawn(Zone, s, Spot(-210f, 3f), 0f, landed: true);
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(_game.Survivor.Sit(v), "the sleeper sits at the controls");
        foreach (int x in _drive.Wait(5)) yield return x;
        Int3 cockpit = s.Blocks().First(b => s.TypeOf(b.block).IsCommandChair).pos;
        Shoot(v, cockpit, 1e6f, DamageType.Kinetic);
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_game.Survivor.Seated == null && v.Seat == null, "the cockpit shot away, the pilot is out of the seat, and nothing falls over");

        if (s.Core is Int3 core)
        {
            Vessel flyer = v;
            flyer.Controller?.Wake(flyer.Body);
            Shoot(flyer, core, 1e6f, DamageType.Kinetic);
            foreach (int x in _drive.Wait(3)) yield return x;
            Check(flyer.Body.Kind == BodyKind.Wreck && flyer.Binding is FreeBinding, "the core shot away, the vessel is a free wreck");
            Check(flyer.View.Fx.Structure == s && GodotObject.IsInstanceValid(flyer.View), "and its mesh and effects came along");
            int worn = s.Blocks().Count(b => Building.IsDamaged(s, b.pos));
            Int3 anyone = s.Blocks().First().pos;
            Shoot(flyer, anyone, 30f, DamageType.Kinetic);
            foreach (int x in _drive.Wait(3)) yield return x;
            Check(!s.Has(anyone) || s.Blocks().Count(b => Building.IsDamaged(s, b.pos)) >= worn, "a wreck goes on taking damage and showing it");
        }
        else Check(false, "the starter vessel has a core");
    }

    IEnumerable<int> BiteAndLightning()
    {
        Wildlife.Creature c = Zone.Wildlife.Sim.Creatures.Where(x => !x.Dead).OrderByDescending(x => x.Stats.Damage).FirstOrDefault();
        Check(c != null, "the world has a creature");
        if (c == null) yield break;
        // Not every world's fauna out-bites steel, and it may be anywhere, this test's own walls included: stage one in the open.
        if (c.Stats.Damage <= 10f) c.Stats.Damage = 20f;
        N.Vector3 open = Enumerable.Range(1, 12).Select(i => Spot(-90f - 30f * i, 0f))
            .OrderByDescending(p => Zone.Vessels.Select(o => N.Vector3.Distance(o.Body.Position, p)).DefaultIfEmpty(1e6f).Min()).First();
        Vector3 at = open.ToGodot(), local = at - Zone.Wildlife.Origin;
        var wallAt = new N.Vector3(at.X + 1.5f, at.Y - 1f, at.Z - 2f);
        Vessel wall = Wall(2, 8, wallAt, depth: 8);
        foreach (int x in _drive.Wait(3)) yield return x;
        float before = wall.Structure.Blocks().Sum(b => b.block.Hp);
        Vector3 beyond = at + new Vector3(8f, 0.8f, 0f);
        c.Position = local; // the simulation sends a creature home between ticks: this is where it lunges from
        bool struck = VesselDamage.Struck(Zone, c, beyond);
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(struck && wall.Structure.Blocks().Sum(b => b.block.Hp) < before, $"a creature's lunge at someone behind a wall lands on the wall ({before - wall.Structure.Blocks().Sum(b => b.block.Hp):F1} hit points, {c.Species.CommonName})");
        c.Position = local;
        Check(!VesselDamage.Struck(Zone, c, at + new Vector3(-6f, 0.8f, 0f)), "with nothing in the way it lands where it was going");

        Vessel target = Wall(8, 8, Spot(-250f, 1f));
        foreach (int x in _drive.Wait(2)) yield return x;
        var (point, _) = Front(target, new Int3(4, 4, 0));
        target.Hits.OnWeather(new WeatherEvent(WeatherEventKind.LightningStrike, 0, new Vector3(point.X, point.Y, point.Z), default, 45f, 2f));
        foreach (int x in _drive.Wait(2)) yield return x;
        int struckBlocks = target.Structure.Blocks().Count(b => Building.IsDamaged(target.Structure, b.pos));
        Check(target.Hits.HitsTaken == 1 && struckBlocks > 0 && target.Structure.Count == 64, $"lightning on a small hull wears the blocks around the strike ({struckBlocks}) and takes none out");
        Vessel clear = Wall(4, 4, Spot(-280f, 1f));
        clear.Hits.OnWeather(new WeatherEvent(WeatherEventKind.LightningStrike, 0, new Vector3(point.X, point.Y, point.Z), default, 45f, 2f));
        Check(clear.Hits.Queued == 0, "lightning somewhere else doesn't");
    }

    IEnumerable<int> Hull()
    {
        var s = new Structure(Cat) { Name = "Grown" };
        s.Set(Int3.Zero, Building.Make(s, Cat.CoreType));
        s.DirtyChunks.Clear();
        Vessel v = Vessel.Spawn(Zone, s, Spot(-310f, 3f), 0f, landed: true);
        float start = v.Body.Integrity;
        for (int x = 1; x < 40; x++) s.Set(new Int3(x, 0, 0), Building.Make(s, Cat.Type("steel")));
        v.ApplyEdits();
        Check(v.Body.Integrity > start * 2f, $"a vessel built up from its core gets the hull points of what it became: {start:F2} to {v.Body.Integrity:F2}");
        float grown = v.Body.Integrity;
        Shoot(v, new Int3(39, 0, 0), 1e6f, DamageType.Kinetic);
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(v.Body.Integrity == grown, "and shooting it apart doesn't hand them back");
    }
}
