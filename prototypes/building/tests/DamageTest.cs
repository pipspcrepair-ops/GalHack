using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using N = System.Numerics;

namespace BlockBuilding;

/// <summary>
/// Headless test of block damage: the hit point rules and what the mesher draws from them. The
/// game's end of it, real crashes and shots included, is the slice's tests/DamageTest. Run with:
///   .\tools\test.ps1 -Project prototypes/building -Scene res://tests/DamageTest.tscn
/// Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class DamageTest : Node
{
    int _passes, _failures;
    Catalogue _cat;

    public override void _Ready()
    {
        _cat = GameData.Catalogue;

        Buckets();
        ShotsShowInTheMesh();
        Materials();
        FacesStayInTheirCell();
        OnlyTheAffectedChunk();
        Deterministic();
        Crashes();
        Repairs();
        Devices();
        NothingPiles();
        RemeshCost();

        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
        if (ok) _passes++;
        else _failures++;
    }

    Structure Plate(int width, int depth, string type = "steel")
    {
        var s = new Structure(_cat);
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                s.Set(new Int3(x, 0, z), Building.Make(s, _cat.Type(type)));
        s.DirtyChunks.Clear();
        return s;
    }

    /// <summary>Brings a block to a share of its health without going through a weapon.</summary>
    static void Wear(Structure s, Int3 p, float health) => s.SetHp(p, s.MaxHp(s.Get(p)) * health);

    static int DamageOf(Vector2 uv2) => ((int)uv2.Y >> BlockMaterials.DamageShift) & 3;
    static int WearOf(Vector2 uv2) => ((int)uv2.Y >> BlockMaterials.WearShift) & 3;

    static int Worst(MeshBuffers m) => m.UV2s.Count == 0 ? 0 : m.UV2s.Max(DamageOf);

    void Buckets()
    {
        Check(Damage.Bucket(100, 100) == 0 && Damage.Bucket(76, 100) == 0, "a block down to three quarters of its health looks whole");
        Check(Damage.Bucket(75, 100) == 1 && Damage.Bucket(51, 100) == 1, "under three quarters it is scuffed");
        Check(Damage.Bucket(50, 100) == 2 && Damage.Bucket(26, 100) == 2, "under half it is cracked and dented");
        Check(Damage.Bucket(25, 100) == 3 && Damage.Bucket(1, 100) == 3, "under a quarter it is holed");
        Check(Damage.Bucket(0, 100) == 0, "a live block placed with no hit points at all is drawn whole, not holed");
    }

    void ShotsShowInTheMesh()
    {
        Structure s = Plate(3, 1);
        var p = new Int3(1, 0, 0);
        float max = s.MaxHp(s.Get(p));
        ChunkMeshData whole = ChunkMesher.Build(s, Int3.Zero);
        int quads = whole.Opaque.VertexCount;
        Check(Worst(whole.Opaque) == 0, $"an undamaged plate has no damage in its mesh ({quads} vertices)");

        var report = new DamageReport();
        Damage.Hit(s, p, DamageType.Kinetic, 200f, report);
        float expected = (200f - 10f) * 0.9f;
        Check(MathF.Abs(s.Get(p).Hp - (max - expected)) < 0.01f && report.BlocksHit == 1, $"a shot takes armour and resistance off: {max:F0} to {s.Get(p).Hp:F0} hit points");
        Check(s.RecentHits.Count == 1 && s.RecentHits[0].Cell == p && !s.RecentHits[0].Destroyed, "and it is noted for the effects");

        int vertices = quads;
        int[] looks = new int[4];
        for (int bucket = 1; bucket <= 3; bucket++)
        {
            Wear(s, p, bucket == 1 ? 0.7f : bucket == 2 ? 0.4f : 0.1f);
            ChunkMeshData data = ChunkMesher.Build(s, Int3.Zero);
            looks[bucket] = Worst(data.Opaque);
            if (bucket >= 2) Check(data.Opaque.VertexCount > vertices, $"stage {bucket} has real geometry: {vertices} to {data.Opaque.VertexCount} vertices");
            vertices = data.Opaque.VertexCount;
        }
        Check(looks[1] == 1 && looks[2] == 2 && looks[3] == 3, $"its damage bucket goes through the mesh data stage by stage: {looks[1]}, {looks[2]}, {looks[3]}");

        Structure gone = Plate(3, 1);
        var kill = new DamageReport();
        Damage.Hit(gone, p, DamageType.Kinetic, 1e6f, kill);
        Check(!gone.Has(p) && kill.Destroyed.Count == 1 && gone.RecentHits[^1].Destroyed, "a block out of hit points is removed");
    }

    void Materials()
    {
        foreach (var (id, want) in new[] { ("steel", DamageLook.Metal), ("hardened", DamageLook.Metal), ("combat", DamageLook.Metal), ("concrete", DamageLook.Concrete) })
        {
            Structure s = Plate(1, 1, id);
            Wear(s, Int3.Zero, 0.4f);
            ChunkMeshData data = ChunkMesher.Build(s, Int3.Zero);
            Check(_cat.Type(id).DamageLook == want && data.Opaque.UV2s.All(uv => WearOf(uv) == (int)want), $"{id} wears as {want}");
        }
        Check(_cat.Type("glass").DamageLook == DamageLook.Glass, "glass wears as glass");

        Structure pane = Plate(3, 1, "glass");
        int before = ChunkMesher.Build(pane, Int3.Zero).Glass.VertexCount;
        Wear(pane, new Int3(1, 0, 0), 0.1f);
        ChunkMeshData cracked = ChunkMesher.Build(pane, Int3.Zero);
        Check(cracked.Opaque.VertexCount == 0 && cracked.Glass.VertexCount > before && Worst(cracked.Glass) == 3, "glass stays in the glass surface, its block's damage in its own faces (the shader shatters it)");

        BlockType steel = _cat.Type("steel");
        DamageLook kept = steel.DamageLook;
        steel.DamageLook = DamageLook.Alien; // no alien armour in building's own catalogue; the slice's has precursor alloy
        Structure alloy = Plate(1, 1);
        Wear(alloy, Int3.Zero, 0.4f);
        Check(ChunkMesher.Build(alloy, Int3.Zero).Opaque.UV2s.All(uv => WearOf(uv) == (int)DamageLook.Alien), "alien alloy wears its own way");
        steel.DamageLook = kept;
    }

    void FacesStayInTheirCell()
    {
        GridDef grid = _cat.Grid;
        for (int bucket = 2; bucket <= 3; bucket++)
            {
                Structure s = Plate(1, 1);
                Wear(s, Int3.Zero, bucket == 2 ? 0.4f : 0.1f);
                ChunkMeshData data = ChunkMesher.Build(s, Int3.Zero);
                float half = grid.CellSize * 0.5f + 1e-3f;
                bool inside = data.Opaque.Vertices.All(v => MathF.Abs(v.X) <= half && MathF.Abs(v.Y) <= half && MathF.Abs(v.Z) <= half);
                int corners = 0;
                foreach (int sx in new[] { -1, 1 })
                    foreach (int sy in new[] { -1, 1 })
                        foreach (int sz in new[] { -1, 1 })
                            if (data.Opaque.Vertices.Any(v => v.DistanceTo(new Vector3(sx, sy, sz) * grid.CellSize * 0.5f) < 1e-3f)) corners++;
                bool front = true;
                var outward = data.Opaque.Normals.Zip(data.Opaque.Vertices).ToList();
                for (int i = 0; i + 2 < data.Opaque.Indices.Count; i += 3)
                {
                    // Godot's front faces are clockwise: the normal must agree with the winding.
                    Vector3 a = data.Opaque.Vertices[data.Opaque.Indices[i]], b = data.Opaque.Vertices[data.Opaque.Indices[i + 1]], c = data.Opaque.Vertices[data.Opaque.Indices[i + 2]];
                    if ((c - a).Cross(b - a).Dot(data.Opaque.Normals[data.Opaque.Indices[i]]) < -1e-6f) front = false;
                }
                Check(inside && corners == 8 && front, $"{grid.Id} grid, stage {bucket}: every vertex in its own cell, the eight corners where they were (no gaps by its neighbours), every triangle facing out");
            }

        Structure wall = Plate(3, 3);
        Wear(wall, new Int3(1, 0, 1), 0.1f);
        ChunkMeshData holed = ChunkMesher.Build(wall, Int3.Zero);
        Check(holed.Boxes.Count == 1 && holed.Boxes[0].size.IsEqualApprox(new Vector3(6, 2, 6)), "a holed block still collides as a whole cell: the look changes, the collider doesn't");
    }

    void OnlyTheAffectedChunk()
    {
        Structure s = Plate(48, 1);
        var edge = new Int3(15, 0, 0); // the last cell in its chunk: a shape change there would dirty the next one too
        var report = new DamageReport();
        int version = s.Version;
        Damage.Hit(s, edge, DamageType.Kinetic, 100f, report);
        Check(s.DirtyChunks.Count == 1 && s.DirtyChunks.Contains(Structure.ChunkOf(edge)), $"wear on a chunk's edge redraws only its own chunk ({s.DirtyChunks.Count} of {s.Chunks.Count})");
        Check(s.Version == version, "and leaves the structure's version alone: stats and rooms stay valid");
        s.DirtyChunks.Clear();
        Damage.Hit(s, edge, DamageType.Kinetic, 1e6f, report);
        Check(s.DirtyChunks.Contains(Structure.ChunkOf(edge)) && s.DirtyChunks.Contains(Structure.ChunkOf(edge + new Int3(1, 0, 0))) && s.Version != version,
            "destroying it changes the neighbour's culling too: both redraw");
    }

    void Deterministic()
    {
        Structure Scene()
        {
            Structure s = Plate(6, 6);
            Wear(s, new Int3(2, 0, 2), 0.1f);
            Wear(s, new Int3(3, 0, 2), 0.4f);
            Wear(s, new Int3(4, 0, 4), 0.7f);
            return s;
        }
        ChunkMeshData a = ChunkMesher.Build(Scene(), Int3.Zero), b = ChunkMesher.Build(Scene(), Int3.Zero);
        Check(a.Opaque.Vertices.SequenceEqual(b.Opaque.Vertices) && a.Opaque.Colors.SequenceEqual(b.Opaque.Colors) && a.Opaque.UV2s.SequenceEqual(b.Opaque.UV2s),
            $"the same damage gives the same mesh, vertex for vertex ({a.Opaque.VertexCount})");

        // The look of one block doesn't move when the blocks round it change.
        static List<(Vector3, Color)> Around(ChunkMeshData d, Vector3 centre, float reach) =>
            d.Opaque.Vertices.Zip(d.Opaque.Colors).Where(x => (x.First - centre).Abs().X <= reach && (x.First - centre).Abs().Z <= reach && x.Second.A < 1f)
                .Select(x => (x.First, x.Second)).OrderBy(x => x.First.X).ThenBy(x => x.First.Y).ThenBy(x => x.First.Z).ToList();
        Structure changed = Scene();
        Wear(changed, new Int3(1, 0, 2), 0.4f);
        Wear(changed, new Int3(2, 0, 1), 0.7f);
        var centre = new Vector3(4, 0, 4);
        var alone = Around(ChunkMesher.Build(Scene(), Int3.Zero), centre, 0.99f);
        var crowded = Around(ChunkMesher.Build(changed, Int3.Zero), centre, 0.99f);
        Check(alone.Count > 0 && alone.SequenceEqual(crowded), $"a holed block keeps its exact shape and seed when its neighbours are damaged ({alone.Count} vertices)");

        var seeds = new HashSet<uint>();
        for (int x = 0; x < 32; x++)
            for (int z = 0; z < 32; z++)
                seeds.Add(ChunkMesher.FaceSeed(new Int3(x, 0, z), Dir.PosY) & 0x7f);
        Check(seeds.Count > 90, $"and blocks differ from each other: {seeds.Count} of 128 looks across a 32×32 wall");
    }

    void Crashes()
    {
        Structure s = Plate(9, 9);
        var report = new DamageReport();
        var point = new N.Vector3(4f, -0.5f, 4f);
        Damage.Crash(s, point, N.Vector3.UnitY, 600f, report);
        float centre = s.MaxHp(s.Get(new Int3(4, 0, 4))) - s.Get(new Int3(4, 0, 4)).Hp;
        float side = s.TryGet(new Int3(6, 0, 4), out Block b) ? s.MaxHp(b) - b.Hp : float.MaxValue;
        float corner = s.TryGet(new Int3(8, 0, 8), out Block c) ? s.MaxHp(c) - c.Hp : float.MaxValue;
        Check(report.BlocksHit > 1 && centre > side && corner == 0f, $"a crash spreads from where it struck, hardest there: {centre:F0} at the point, {side:F0} two cells off, {corner:F0} at the far corner");
        Check(MathF.Abs(report.Dealt - 600f * 0.9f) < 1f, $"and every hit point of it lands, less kinetic resistance ({report.Dealt:F0} of 600)");

        Structure combat = Plate(9, 9, "combat");
        var soft = new DamageReport();
        Damage.Crash(combat, point, N.Vector3.UnitY, 100f, soft);
        Check(soft.Dealt > 0f, $"a gentle crash isn't stopped by flat armour, spread thin as it is ({soft.Dealt:F0} of 100 on combat steel, armour 50)");

        var wide = new DamageReport();
        Damage.Crash(Plate(21, 21), new N.Vector3(10f, -0.5f, 10f), N.Vector3.UnitY, 60000f, wide);
        Check(wide.BlocksHit > report.BlocksHit && wide.Destroyed.Count > 0, $"a harder one reaches further and breaks blocks: {wide.BlocksHit} hit, {wide.Destroyed.Count} gone");

        Structure far = Plate(3, 3);
        var off = new DamageReport();
        Damage.Crash(far, new N.Vector3(1f, -6f, 1f), N.Vector3.UnitY, 100f, off);
        Check(off.BlocksHit > 0, "struck well away from any block (a leg's foot), the nearest block takes it");

        Damage.Crash(new Structure(_cat), point, N.Vector3.UnitY, 100f, new DamageReport());
        Check(true, "an empty structure takes a crash without complaint");
    }

    void Repairs()
    {
        Structure s = Plate(3, 1);
        var p = new Int3(1, 0, 0);
        ChunkMeshData whole = ChunkMesher.Build(s, Int3.Zero);
        var pristine = whole.Opaque.Vertices.ToArray();
        var flags = whole.Opaque.UV2s.ToArray();
        float max = s.MaxHp(s.Get(p));

        Wear(s, p, 0.1f);
        var bag = new Inventory { Creative = false };
        Check(Building.Repair(s, p, bag) == RepairStatus.NotEnoughMaterials && Damage.Bucket(s.Get(p).Hp, max) == 3, "with nothing to pay with, a repair restores nothing");

        Dictionary<string, int> part = Building.RepairCost(s, p, 0.25f), all = Building.RepairCost(s, p);
        Check(part["iron"] == 3 && all["iron"] == 9, $"repairs cost a share of the block's recipe: {part["iron"]} iron for a quarter of its health, {all["iron"]} for the 90% it lost (10 to build)");

        bag.Add(new Dictionary<string, int> { ["iron"] = 40 });
        var stages = new List<int>();
        for (int step = 0; step < 4; step++)
        {
            Check(Building.Repair(s, p, bag, 0.25f) == RepairStatus.Repaired, $"a quarter repaired, step {step + 1}");
            stages.Add(Worst(ChunkMesher.Build(s, Int3.Zero).Opaque));
        }
        Check(stages.SequenceEqual(new[] { 2, 1, 0, 0 }) || stages.SequenceEqual(new[] { 2, 1, 1, 0 }), $"its look steps back as its health returns: {string.Join(", ", stages)}");
        Check(bag["iron"] < 40, $"and it was paid for: {40 - bag["iron"]} iron");
        Check(!Building.IsDamaged(s, p) && Building.Repair(s, p, bag) == RepairStatus.NotDamaged, "a whole block has nothing to repair");

        ChunkMeshData back = ChunkMesher.Build(s, Int3.Zero);
        Check(back.Opaque.Vertices.SequenceEqual(pristine) && back.Opaque.UV2s.SequenceEqual(flags), "fully repaired, its mesh is exactly the undamaged one");
    }

    void Devices()
    {
        var s = new Structure(_cat);
        var p = new Int3(2, 0, 0);
        s.Set(p, Building.Make(s, _cat.Type("generator")));
        Check(ChunkMesher.Build(s, Int3.Zero).Smoking.Count == 0, "a healthy device doesn't smoke");
        Wear(s, p, 0.1f);
        ChunkMeshData data = ChunkMesher.Build(s, Int3.Zero);
        Check(data.Smoking.Count == 1 && data.Smoking[0] == p, "a badly damaged device does");
        Check(Worst(data.Opaque) == 3 && data.Opaque.Colors.Any(c => c.A < 1f), "and its parts carry the damage, each block's own seed with them");
        Building.Repair(s, p);
        Check(ChunkMesher.Build(s, Int3.Zero).Smoking.Count == 0, "repaired, it stops");

        var lamp = new Structure(_cat);
        lamp.Set(Int3.Zero, Building.Make(lamp, _cat.Type("light")));
        Wear(lamp, Int3.Zero, 0.1f);
        bool lit = ChunkMesher.Build(lamp, Int3.Zero).Opaque.UV2s.Any(uv => (((int)uv.Y >> BlockMaterials.GlowShift) & 1) == 1);
        Check(!lit, "a smashed light goes out");
    }

    void NothingPiles()
    {
        Structure s = Plate(40, 40);
        var report = new DamageReport();
        for (int i = 0; i < 1000; i++) Damage.Hit(s, new Int3(i % 40, 0, i / 40 % 40), DamageType.Kinetic, 20f, report);
        Check(s.RecentHits.Count == 256, $"hits nobody draws don't pile up: capped at {s.RecentHits.Count}");
    }

    /// <summary>A chunk's whole shell at one health, and the best of three timings of remeshing it (mesh only).</summary>
    (double ms, int vertices, int faces) ShellCost(float health)
    {
        var s = new Structure(_cat);
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
                for (int z = 0; z < 16; z++)
                    if (x == 0 || y == 0 || z == 0 || x == 15 || y == 15 || z == 15)
                    {
                        Block b = Building.Make(s, _cat.Type("steel"));
                        b.Hp = s.MaxHp(b) * health;
                        s.Set(new Int3(x, y, z), b);
                    }
        var data = new ChunkMeshData();
        double best = double.MaxValue;
        for (int window = 0; window < 3; window++)
        {
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++) ChunkMesher.Build(s, Int3.Zero, data, colliders: false);
            best = Math.Min(best, clock.Elapsed.TotalMilliseconds / 5);
        }
        return (best, data.Opaque.VertexCount, data.Opaque.Indices.Count / 3);
    }

    void RemeshCost()
    {
        var whole = ShellCost(1f);
        var scuffed = ShellCost(0.7f);
        var dented = ShellCost(0.4f);
        var holed = ShellCost(0.1f);
        GD.Print($"      shell remesh: whole {whole.ms:F2} ms {whole.vertices} v; scuffed {scuffed.ms:F2} ms {scuffed.vertices} v; dented {dented.ms:F2} ms {dented.vertices} v; holed {holed.ms:F2} ms {holed.vertices} v");
        // Every face of a 16-cube shell holed, inside and out: 2712 faces, far past anything a barrage
        // leaves. What matters is what a hole costs, so the frame's 3 ms budget can be read off it.
        const int HoledFaces = 2712;
        double perFace = holed.ms * 1000.0 / HoledFaces;
        Check(perFace < 10.0, $"a holed face costs {perFace:F1} microseconds to mesh, so a frame's 3 ms covers about {3000.0 / perFace:F0} of them (best of three)");
        Check(holed.ms < 33.0, $"even a chunk with every face holed remeshes inside one 30 fps frame: {holed.ms:F2} ms, {holed.vertices} vertices");
    }
}
