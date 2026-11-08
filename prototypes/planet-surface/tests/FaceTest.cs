using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Planets.Gen;
using PlanetSurface.Poi;
using PlanetSurface.World;
using PoiGenerator.Gen;
using Archetype = Planets.Gen.Archetype;
using NV = System.Numerics.Vector3;
using PChunk = PoiGenerator.Gen.ChunkMesh;
using PInt3 = PoiGenerator.Gen.Int3;
using PoiSite = PlanetSurface.Poi.PoiSite;
using TChunk = TerrainDigging.Terrain.ChunkMesh;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace PlanetSurface;

/// <summary>
/// Seeing through a block should never happen, so this doesn't take the mesher's word for it. It casts
/// rays at the triangles the game actually draws, front faces only as the renderer does, and walks the
/// same rays through the block grid and the ground: a ray that enters solid without having met a drawn
/// face went through something you could see through. Fresh worlds every run, real places and ruins as
/// decay left them, from all round, from above, from under the ground and from their rooms; once as if
/// all the ground were dug away, once with the real ground, once from a pit really dug round them.
/// Then ground meeting walls, held to the drawn ground too: no dip at the foot of a wall, at any height.
///   godot --headless --path prototypes/planet-surface res://tests/FaceTest.tscn
/// -- --worlds=N (default 3), --seed=N to replay a world a failure named.
/// </summary>
public partial class FaceTest : Node
{
    int _failures;
    Random _rand = new();
    ulong? _seed;

    public override void _Ready()
    {
        int worlds = 3;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--worlds=")) worlds = int.Parse(arg["--worlds=".Length..]);
            if (arg.StartsWith("--seed="))
            {
                // Replaying a failure: the same world, and the same structures of it.
                _seed = ulong.Parse(arg["--seed=".Length..]);
                _rand = new Random((int)_seed.Value);
            }
        }
        var watch = Stopwatch.StartNew();
        try
        {
            Archetype[] kinds = { Archetype.Temperate, Archetype.Arid, Archetype.Ice };
            var totals = new Tally();
            for (int i = 0; i < worlds; i++) World(kinds[i % kinds.Length], totals);
            totals.Report(this);
        }
        catch (Exception e)
        {
            Fail($"test crashed: {e}");
        }
        GD.Print($"total test time {watch.Elapsed.TotalSeconds:F1} s");
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        GalHack.Testing.Backstop.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) GD.Print($"PASS  {what}");
        else Fail(what);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
        GD.Print($"FAIL  {what}");
    }

    // ---- What was found ---------------------------------------------------------------------------

    sealed class Tally
    {
        public int Structures, Rays, SeeThrough, Walls, Dips, Holes, Inner, Poke, Walled;
        public long Triangles, GroundTriangles;
        public float Worst;
        public readonly HashSet<string> Families = new();
        public readonly Dictionary<string, int> Causes = new();
        public readonly List<string> First = new(), Dip = new();
        public string WorstAt = "";
        public readonly Dictionary<string, string> PokedAt = new();

        public void Saw(string cause, string detail)
        {
            SeeThrough++;
            Causes[cause] = Causes.GetValueOrDefault(cause) + 1;
            if (First.Count < 8 && !First.Any(f => f.StartsWith(cause))) First.Add($"{cause}: {detail}");
        }

        public void Report(FaceTest t)
        {
            string causes = string.Join(", ", Causes.OrderByDescending(c => c.Value).Take(10).Select(c => $"{c.Value} {c.Key}"));
            t.Check(SeeThrough == 0 && Structures >= 12 && Rays > 100000,
                $"no ray reaches solid without a drawn face: {Rays} rays at {Structures} structures of {Families.Count} families, {Triangles} triangles of blocks, {GroundTriangles} of ground round them, {SeeThrough} saw through{(SeeThrough > 0 ? $" ({causes}); first: {string.Join("; ", First)}" : "")}");
            t.Check(Poke * 2000 <= Inner && Inner > 1000, $"the ground stays behind a room's walls: {Inner} inner faces, {Poke} with ground in front of them (one in 2000 allowed: a lump of rock left between two shafts){(Poke > 0 ? $"; {string.Join("; ", PokedAt.Values.Take(6))}" : "")}");
            t.Check(Walled > 200, $"a place with blocks taken out, its chunks redrawn as the game redraws them: {Walled} blocks gone, and the rays above saw nothing through what they left");
            t.Check(Dips * 2000 <= Walls && Holes == 0 && Walls > 500,
                $"the drawn ground meets walls flush: {Walls} wall feet, {Dips} dip (one in 2000 allowed), the worst {Worst:F2} m, {Holes} with a gap to the ground{(Dip.Count > 0 ? $"; first: {string.Join("; ", Dip)}" : "")}; worst at {WorstAt}");
        }
    }

    // ---- Triangles ----------------------------------------------------------------------------------

    /// <summary>Triangles as the renderer has them (winding, not stored normals), binned by the cells they touch. Local to the structure's origin.</summary>
    sealed class Soup
    {
        readonly List<(NV A, NV B, NV C)> _tris = new();
        readonly Dictionary<(int, int, int), List<int>> _bins = new();
        public int Count => _tris.Count;

        public void Add(NV a, NV b, NV c)
        {
            int index = _tris.Count;
            _tris.Add((a, b, c));
            NV lo = NV.Min(a, NV.Min(b, c)) - new NV(0.02f), hi = NV.Max(a, NV.Max(b, c)) + new NV(0.02f);
            for (int y = (int)MathF.Floor(lo.Y); y <= (int)MathF.Floor(hi.Y); y++)
                for (int z = (int)MathF.Floor(lo.Z); z <= (int)MathF.Floor(hi.Z); z++)
                    for (int x = (int)MathF.Floor(lo.X); x <= (int)MathF.Floor(hi.X); x++)
                    {
                        if (!_bins.TryGetValue((x, y, z), out List<int> bin)) _bins[(x, y, z)] = bin = new List<int>(4);
                        bin.Add(index);
                    }
        }

        /// <summary>The nearest front-facing triangle a ray meets in one cell, or infinity. Godot's front faces wind clockwise as the camera sees them.</summary>
        public float Hit(int x, int y, int z, NV o, NV d)
        {
            if (!_bins.TryGetValue((x, y, z), out List<int> bin)) return float.PositiveInfinity;
            float best = float.PositiveInfinity;
            foreach (int i in bin)
            {
                (NV a, NV b, NV c) = _tris[i];
                NV e1 = b - a, e2 = c - a;
                if (NV.Dot(d, NV.Cross(e1, e2)) <= 0f) continue; // its back: not drawn
                NV p = NV.Cross(d, e2);
                float det = NV.Dot(e1, p);
                if (MathF.Abs(det) < 1e-9f) continue;
                float inv = 1f / det;
                NV s = o - a;
                float u = NV.Dot(s, p) * inv;
                if (u < -1e-4f || u > 1.0001f) continue;
                NV q = NV.Cross(s, e1);
                float v = NV.Dot(d, q) * inv;
                if (v < -1e-4f || u + v > 1.0001f) continue;
                float t = NV.Dot(e2, q) * inv;
                if (t > -0.02f && t < best) best = t;
            }
            return best;
        }
    }

    sealed class Subject
    {
        public string Name;
        public BlockGrid Blocks, Built;
        public bool Crater;
        public CellMask Occupied;
        public int X, Y, Z;
        public Soup Drawn;
        public PInt3 Lo, Hi;
        public int Quads;
    }

    static bool Solid(BlockGrid g, PInt3 p)
    {
        Block b = g.Get(p);
        return !b.IsAir && BlockInfo.IsFullCube(b.Type) && !BlockInfo.IsTransparent(b.Material);
    }

    static Soup DrawnOf(List<PChunk> chunks, out int quads)
    {
        var soup = new Soup();
        quads = 0;
        foreach (PChunk chunk in chunks)
        {
            quads += chunk.Quads;
            for (int s = 0; s < chunk.Surfaces.Length; s++)
            {
                if (s == (int)Surface.Glass) continue; // glass is seen through
                MeshBuffer b = chunk.Surfaces[s];
                NV At(int i) => new(b.Positions[i * 3], b.Positions[i * 3 + 1], b.Positions[i * 3 + 2]);
                for (int i = 0; i + 2 < b.Indices.Count; i += 3)
                    soup.Add(At(b.Indices[i]), At(b.Indices[i + 1]), At(b.Indices[i + 2]));
            }
        }
        return soup;
    }

    /// <summary>The ground drawn round a structure, in the structure's own space, built as the streamer builds it.</summary>
    const int GroundMargin = 24;

    static Soup Ground(PlanetWorld w, Subject s)
    {
        var mesher = new TerrainDigging.Terrain.SurfaceNetsMesher();
        var soup = new Soup();
        var store = w.Terrain.Store;
        var lo = new TInt3(s.X + s.Lo.X - GroundMargin, s.Y + s.Lo.Y - 8, s.Z + s.Lo.Z - GroundMargin);
        var hi = new TInt3(s.X + s.Hi.X + GroundMargin, s.Y + s.Hi.Y + 8, s.Z + s.Hi.Z + GroundMargin);
        TInt3 cl = TerrainDigging.Terrain.ChunkStore.ChunkOf(lo), ch = TerrainDigging.Terrain.ChunkStore.ChunkOf(hi);
        var origin = new NV(s.X, s.Y, s.Z);
        for (int y = cl.Y; y <= ch.Y; y++)
            for (int z = cl.Z; z <= ch.Z; z++)
                for (int x = cl.X; x <= ch.X; x++)
                {
                    TChunk m = mesher.Build(store.CaptureNeighborhood(new TInt3(x, y, z)));
                    for (int i = 0; i + 2 < m.Indices.Length; i += 3)
                        soup.Add(m.Positions[m.Indices[i]] - origin, m.Positions[m.Indices[i + 1]] - origin, m.Positions[m.Indices[i + 2]] - origin);
                }
        return soup;
    }

    // ---- The rays -----------------------------------------------------------------------------------

    /// <summary>
    /// Walks a ray cell by cell from an open point. Returns false, with the cell and the side it came in
    /// by, if it enters a solid block, or ground well under the surface, before meeting a drawn face.
    /// </summary>
    static bool Walk(Subject s, Soup ground, Func<NV, bool> buried, NV o, NV d, float reach, out PInt3 cell, out int side, out bool earth, out bool met)
    {
        met = false;
        int x = (int)MathF.Floor(o.X), y = (int)MathF.Floor(o.Y), z = (int)MathF.Floor(o.Z);
        int sx = d.X > 0 ? 1 : -1, sy = d.Y > 0 ? 1 : -1, sz = d.Z > 0 ? 1 : -1;
        float dx = d.X != 0 ? MathF.Abs(1f / d.X) : float.MaxValue, dy = d.Y != 0 ? MathF.Abs(1f / d.Y) : float.MaxValue, dz = d.Z != 0 ? MathF.Abs(1f / d.Z) : float.MaxValue;
        float tx = d.X != 0 ? ((d.X > 0 ? x + 1 : x) - o.X) / d.X : float.MaxValue;
        float ty = d.Y != 0 ? ((d.Y > 0 ? y + 1 : y) - o.Y) / d.Y : float.MaxValue;
        float tz = d.Z != 0 ? ((d.Z > 0 ? z + 1 : z) - o.Z) / d.Z : float.MaxValue;
        float enter = 0f;
        int under = 0;
        side = -1;
        earth = false;
        for (int step = 0; step < 400; step++)
        {
            cell = new PInt3(x, y, z);
            float near = MathF.Min(s.Drawn.Hit(x, y, z, o, d), ground?.Hit(x, y, z, o, d) ?? float.PositiveInfinity);
            float exit = MathF.Min(tx, MathF.Min(ty, tz));
            if (near <= enter + 0.03f) { met = near <= reach; return true; }
            if (step > 0 && Solid(s.Blocks, cell)) return false;
            if (near <= exit) { met = near <= reach; return true; }
            // The smoothed ground may round a cell's corner off: only two cells in is a ray under it for sure.
            if (step > 0 && buried != null && buried(o + d * exit))
            {
                if (++under >= 2) { earth = true; return false; }
            }
            else under = 0;
            // Past the ground gathered for this structure.
            if (ground != null && (x < s.Lo.X - GroundMargin + 2 || x > s.Hi.X + GroundMargin - 2 || z < s.Lo.Z - GroundMargin + 2 || z > s.Hi.Z + GroundMargin - 2 || y < s.Lo.Y - 6 || y > s.Hi.Y + 6)) return true;
            if (exit > reach) return true;
            enter = exit;
            if (tx <= ty && tx <= tz) { x += sx; tx += dx; side = sx > 0 ? 3 : 1; }
            else if (ty <= tz) { y += sy; ty += dy; side = sy > 0 ? 5 : 4; }
            else { z += sz; tz += dz; side = sz > 0 ? 0 : 2; }
        }
        cell = default;
        return true;
    }

    static readonly string[] Sides = { "north", "east", "south", "west", "top", "bottom" };

    void Cast(Subject s, Soup ground, Func<NV, bool> buried, NV o, NV target, string how, Tally t)
    {
        NV d = NV.Normalize(target - o);
        t.Rays++;
        if (Walk(s, ground, buried, o, d, 90f, out PInt3 c, out int side, out bool earth, out _)) return;
        Block b = s.Blocks.Get(c);
        string what = earth ? "ground" : b.Type.ToString();
        string where = c.Y >= 0 ? "above" : "below";
        string cause = $"{how} {what} {(side >= 0 ? Sides[side] : "?")} {where}";
        t.Saw(cause, $"{s.Name} cell {c}, {b.Material}, seen from {o - new NV(0)}; its origin ({s.X}, {s.Y}, {s.Z})");
    }

    NV Rand(NV lo, NV hi) => new(lo.X + (float)_rand.NextDouble() * (hi.X - lo.X), lo.Y + (float)_rand.NextDouble() * (hi.Y - lo.Y), lo.Z + (float)_rand.NextDouble() * (hi.Z - lo.Z));

    /// <summary>A ray at the inside of a shape that isn't a cube must meet one of its drawn faces on the way: tops, undersides, faces wound the wrong way round.</summary>
    void Shape(Subject s, PInt3 c, Block b, NV target, Tally t)
    {
        // From anywhere round it, level, over and under.
        NV n = Rand(new NV(-1f), new NV(1f));
        if (n.LengthSquared() < 0.01f) return;
        n = NV.Normalize(n);
        NV o = target + n * (1.1f + (float)_rand.NextDouble() * 2.5f);
        if (!Open(s, null, o)) return;
        float reach = (target - o).Length();
        t.Rays++;
        Walk(s, null, null, o, NV.Normalize(target - o), reach, out PInt3 at, out _, out _, out bool met);
        if (met || Solid(s.Blocks, at)) return;
        string facing = n.Y > 0.7f ? "from above" : n.Y < -0.7f ? "from below" : "from the side";
        t.Saw($"shape {b.Type} {facing}", $"{s.Name} cell {c} ({b.Facing}), seen from {o}; its origin ({s.X}, {s.Y}, {s.Z})");
    }

    /// <summary>Whether a viewpoint is one a person could have their head in: out of the blocks and, with ground, out of it.</summary>
    static bool Open(Subject s, Func<NV, float> density, NV p)
    {
        var c = new PInt3((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
        if (Solid(s.Blocks, c)) return false;
        if (density == null) return true;
        // The ground's surface keeps to the lattice cubes with a corner on each side: one wholly in air is clear of it.
        NV q = new(MathF.Floor(p.X - 0.5f) + 0.5f, MathF.Floor(p.Y - 0.5f) + 0.5f, MathF.Floor(p.Z - 0.5f) + 0.5f);
        for (int k = 0; k < 8; k++)
            if (density(q + new NV(k & 1, (k >> 1) & 1, k >> 2)) > 0f) return false;
        return true;
    }

    /// <summary>Every open block face, aimed at from its open side, from near and far; then from all round the structure and from above.</summary>
    void Rays(Subject s, Soup ground, Func<NV, bool> buried, Func<NV, float> density, string how, Tally t, int viewpoints)
    {
        NV lo = new(s.Lo.X, s.Lo.Y, s.Lo.Z), hi = new(s.Hi.X + 1, s.Hi.Y + 1, s.Hi.Z + 1);
        // The faces themselves.
        foreach (var (c, b) in s.Blocks.Blocks())
        {
            if (!Solid(s.Blocks, c)) continue;
            foreach (Dir dir in Dirs.All)
            {
                PInt3 v = dir.Vec();
                if (Solid(s.Blocks, c + v)) continue;
                NV n = new(v.X, v.Y, v.Z);
                NV middle = new NV(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f) + n * 0.5f;
                for (int k = 0; k < 2; k++)
                {
                    NV target = middle + (Rand(new NV(-0.45f), new NV(0.45f)) * (NV.One - NV.Abs(n)));
                    NV o = target + NV.Normalize(n + Rand(new NV(-0.9f), new NV(0.9f))) * (0.4f + (float)_rand.NextDouble() * (k == 0 ? 1.2f : 7f));
                    if (Open(s, density, o)) Cast(s, ground, buried, o, target - n * 0.1f, how, t);
                }
            }
        }
        // Every other shape, when nothing else stands in the way.
        if (ground == null)
            foreach (var (c, b) in s.Blocks.Blocks())
            {
                if (BlockInfo.IsFullCube(b.Type) || BlockInfo.IsTransparent(b.Material) || b.Type == BlockType.Door && how == "near") continue;
                NV at = new(c.X, c.Y, c.Z);
                if (b.Type == BlockType.Ramp)
                {
                    PoiGenerator.Gen.V3 v = Shapes.Turn(new PoiGenerator.Gen.V3(0.5f, 0.2f, 0.25f), b.Facing);
                    Shape(s, c, b, at + new NV(v.X, v.Y, v.Z), t);
                }
                else if (b.Type == BlockType.Spike) Shape(s, c, b, at + new NV(0.5f, 0.2f, 0.5f), t);
                else
                    foreach (ShapeBox box in Shapes.For(b, c, s.Blocks))
                    {
                        if (box.SkipBottom || box.Surface == Surface.Glass) continue;
                        NV mid = new((box.Lo.X + box.Hi.X) * 0.5f, (box.Lo.Y + box.Hi.Y) * 0.5f, (box.Lo.Z + box.Hi.Z) * 0.5f);
                        Shape(s, c, b, at + mid, t);
                    }
            }
        // All round it, near the ground and up, and over it.
        NV middleOf = (lo + hi) * 0.5f;
        for (int k = 0; k < viewpoints; k++)
        {
            float ang = (float)_rand.NextDouble() * MathF.Tau;
            float reach = 4f + (float)_rand.NextDouble() * 30f;
            float high = (float)_rand.NextDouble();
            var o = new NV(middleOf.X + MathF.Cos(ang) * ((hi.X - lo.X) * 0.5f + reach), lo.Y - 6f + (hi.Y - lo.Y + 30f) * high * high, middleOf.Z + MathF.Sin(ang) * ((hi.Z - lo.Z) * 0.5f + reach));
            if (k % 4 == 0) o = Rand(new NV(lo.X - 8f, hi.Y + 2f, lo.Z - 8f), new NV(hi.X + 8f, hi.Y + 40f, hi.Z + 8f));
            if (k % 4 == 1) o = Rand(lo, hi); // out of its rooms, too, or wherever a pit takes you
            if (!Open(s, density, o)) continue;
            for (int r = 0; r < 24; r++) Cast(s, ground, buried, o, Rand(lo, hi), how, t);
        }
    }

    // ---- A world ------------------------------------------------------------------------------------

    void World(Archetype kind, Tally t)
    {
        ulong seed = _seed ?? (ulong)_rand.NextInt64();
        GD.Print($"world: seed {seed}, {kind}");
        PlanetWorld w = PlanetWorld.Build(PlanetSpec.Roll(seed, kind, 0), Main.Catalog);
        var subjects = new List<Subject>();
        var perFamily = new Dictionary<string, int>();
        foreach (PoiSite p in w.Pois.OrderBy(_ => _rand.Next()))
        {
            if (perFamily.GetValueOrDefault(p.Family.Id) >= 2 || subjects.Count >= 14) continue;
            perFamily[p.Family.Id] = perFamily.GetValueOrDefault(p.Family.Id) + 1;
            subjects.Add(Make(p.Name, p.Instance, p.Origin.X, p.Origin.Y, p.Origin.Z, PoiLayer.Mesh(p)));
        }
        foreach (PlanetSurface.Infra.FlavourRuin r in (w.Infra?.Ruins ?? new List<PlanetSurface.Infra.FlavourRuin>()).OrderBy(_ => _rand.Next()).Take(8))
        {
            PoiInstance inst = r.Spawn(w.Profile, w.Source);
            subjects.Add(Make(r.Family.Id, inst, r.OriginX, r.OriginY, r.OriginZ, PlanetSurface.Infra.InfraLayer.RuinMesh(inst)));
        }
        foreach (Subject s in subjects)
        {
            t.Structures++;
            t.Families.Add(s.Name.Split(':')[0]);
            t.Triangles += s.Drawn.Count;
            var store = w.Terrain.Store;
            // All the ground dug away: only the blocks' own faces stand between you and the inside.
            Rays(s, null, null, null, "bare", t, 200);
            Near(s, t);
            Func<int, int, int, float> density = (x, y, z) => store.DensityAt(new TInt3(s.X + x, s.Y + y, s.Z + z));
            Func<NV, float> sample = p => store.SampleDensity(p + new NV(s.X, s.Y, s.Z));
            Func<NV, bool> buried = p => sample(p) > 0.7f;
            Soup ground = Ground(w, s);
            t.GroundTriangles += ground.Count;
            Rays(s, ground, buried, sample, "ground", t, 200);
            Flush(s, ground, t, seed, kind);
            Clear(s, ground, density, t);
            // A real pit dug round it, down to its feet.
            var dug = new List<TInt3>();
            for (int y = s.Lo.Y - 1; y <= s.Hi.Y; y++)
                for (int z = s.Lo.Z - 4; z <= s.Hi.Z + 4; z++)
                    for (int x = s.Lo.X - 4; x <= s.Hi.X + 4; x++)
                        if (!s.Blocks.InBounds(new PInt3(x, y, z)) || s.Blocks.Get(new PInt3(x, y, z)).IsAir && !s.Occupied[new PInt3(x, y, z)])
                            dug.Add(new TInt3(s.X + x, s.Y + y, s.Z + z));
            store.DigCells(dug);
            ground = Ground(w, s);
            Rays(s, ground, buried, sample, "dug", t, 120);
        }
    }

    /// <summary>
    /// As a place near you is drawn: small chunks, no doors of its own, each built alone. Then blocks go, as a
    /// shot or the tool takes them, and only the chunks that touches are drawn again, on top of the old ones.
    /// </summary>
    void Near(Subject s, Tally t)
    {
        const int Size = 8;
        MeshOptions Options(PInt3 min)
        {
            MeshOptions o = MeshOptions.OnPlanet(min);
            o.ChunkSize = Size;
            o.Doors = false;
            return o;
        }
        BlockGrid g = s.Blocks.Clone();
        var chunks = new Dictionary<PInt3, PChunk>();
        foreach (var (c, _) in g.Blocks())
        {
            var ch = new PInt3((int)MathF.Floor(c.X / (float)Size), (int)MathF.Floor(c.Y / (float)Size), (int)MathF.Floor(c.Z / (float)Size));
            if (!chunks.ContainsKey(ch)) chunks[ch] = Mesher.BuildChunk(g, s.Occupied, ch, Size, Options);
        }
        List<PInt3> walls = g.Blocks().Where(b => Solid(g, b.Cell)).Select(b => b.Cell).OrderBy(_ => _rand.Next()).Take(40).ToList();
        foreach (PInt3 c in walls) g.Set(c, Block.Air);
        t.Walled += walls.Count;
        foreach (PInt3 ch in Mesher.ChunksTouching(walls, Size))
        {
            PChunk mesh = Mesher.BuildChunk(g, s.Occupied, ch, Size, Options);
            if (mesh == null) chunks.Remove(ch);
            else chunks[ch] = mesh;
        }
        var near = new Subject { Name = s.Name, Blocks = g, Built = s.Built, Occupied = s.Occupied, X = s.X, Y = s.Y, Z = s.Z, Lo = s.Lo, Hi = s.Hi, Drawn = DrawnOf(chunks.Values.Where(c => c != null).ToList(), out _) };
        Rays(near, null, null, null, "near", t, 60);
    }

    static Subject Make(string name, PoiInstance inst, int x, int y, int z, List<PChunk> meshes)
    {
        PInt3 lo = new(int.MaxValue, int.MaxValue, int.MaxValue), hi = new(int.MinValue, int.MinValue, int.MinValue);
        foreach (var (c, _) in inst.Blocks.Blocks())
        {
            lo = PInt3.Min(lo, c);
            hi = PInt3.Max(hi, c);
        }
        return new Subject { Name = name, Blocks = inst.Blocks, Built = inst.Layout.Blocks, Occupied = inst.Layout.Occupied, Crater = inst.Layout.Extras.Blast != null, X = x, Y = y, Z = z, Lo = lo, Hi = hi, Drawn = DrawnOf(meshes, out int quads), Quads = quads };
    }

    /// <summary>The ground stays out of the rooms: no cell of one has ground in it, and from just off a wall inside, the wall is the first thing met.</summary>
    void Clear(Subject s, Soup ground, Func<int, int, int, float> density, Tally t)
    {
        if (s.Crater) return; // a blast heaped the ground on it, in its rooms and all
        foreach (var (c, _) in s.Built.Blocks())
        {
            if (!Solid(s.Blocks, c)) continue;
            foreach (Dir dir in Dirs.Horizontal)
            {
                PInt3 v = dir.Vec();
                PInt3 n = c + v;
                if (!s.Occupied[n] || !s.Blocks.Get(n).IsAir || !s.Built.Get(n).IsAir || density(n.X, n.Y, n.Z) > 0f) continue;
                NV normal = new(v.X, v.Y, v.Z);
                NV face = new NV(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f) + normal * 0.5f;
                NV o = face + normal * 0.45f + Rand(new NV(-0.3f), new NV(0.3f)) * (NV.One - NV.Abs(normal));
                float met = float.PositiveInfinity;
                for (int k = 0; k < 2; k++)
                {
                    NV at = o - normal * (k * 0.5f);
                    met = MathF.Min(met, ground.Hit((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), (int)MathF.Floor(at.Z), o, -normal));
                }
                t.Inner++;
                if (met < 0.39f)
                {
                    t.Poke++;
                    t.PokedAt.TryAdd(s.Name, $"{s.Name} cell {c} {dir}, {0.45f - met:F2} m proud, under {c.Y + 1}: {s.Blocks.Get(c + PInt3.Up).Type}; over: {s.Built.Get(n + PInt3.Up).Type} {density(n.X, n.Y + 1, n.Z):F1}, beyond {density(c.X - v.X, c.Y, c.Z - v.Z):F1}");
                }
            }
        }
    }

    // ---- The foot of a wall -------------------------------------------------------------------------

    /// <summary>How high the drawn ground stands at a point, from just under the top of the search down; NaN where none is drawn.</summary>
    static float Top(Soup ground, Soup blocks, float x, float z, float from, float to)
    {
        var o = new NV(x, from, z);
        NV d = NV.Normalize(new NV(0.0001f, -1f, 0.0001f));
        int cx = (int)MathF.Floor(x), cz = (int)MathF.Floor(z);
        for (int y = (int)MathF.Floor(from); y >= to; y--)
        {
            float t = MathF.Min(ground.Hit(cx, y, cz, o, d), blocks?.Hit(cx, y, cz, o, d) ?? float.PositiveInfinity);
            if (!float.IsInfinity(t)) return from - t;
        }
        return float.NaN;
    }

    void Flush(Subject s, Soup ground, Tally t, ulong seed, Archetype kind)
    {
        // What holds the ground back: under the pad a wall as built (a gap decay left fills with ground); over it, as it stands (the gap stays open, and the ground falls in at it).
        bool Stands(PInt3 p) => Solid(p.Y < 0 ? s.Built : s.Blocks, p);
        float top = s.Hi.Y + 30f, bottom = s.Lo.Y - 12f;
        foreach (var (c, b) in s.Built.Blocks())
        {
            if (!Stands(c)) continue;
            foreach (Dir dir in Dirs.Horizontal)
            {
                PInt3 v = dir.Vec();
                PInt3 n = c + v;
                if (!s.Built.Get(n).IsAir) continue;
                float fx = c.X + 0.5f + v.X * 0.5f, fz = c.Z + 0.5f + v.Z * 0.5f;
                // Across the face a little, so a corner of the terrain's lattice doesn't speak for the whole foot.
                float ax = v.Z != 0 ? 0.33f : 0f, az = v.X != 0 ? 0.33f : 0f;
                float far = Top(ground, null, fx + v.X * 2.5f, fz + v.Z * 2.5f, top, bottom), mid = Top(ground, null, fx + v.X * 1.5f, fz + v.Z * 1.5f, top, bottom);
                if (float.IsNaN(far) || float.IsNaN(mid)) continue;
                // A face the ground comes to: this block holds the height the ground has out there.
                if (MathF.Floor(mid) != c.Y && MathF.Floor(mid - 0.02f) != c.Y) continue;
                if (s.Occupied[n] || !s.Built.Get(n + PInt3.Up).IsAir) continue;
                // Ground beside another wall out there says nothing of the lie of the land.
                if (s.Occupied[n + v] || s.Occupied[n + v * 2] || s.Occupied[n + v + PInt3.Down] || s.Occupied[n + v * 2 + PInt3.Down]) continue;
                // A lintel: under it is an opening, and the ground may fall in at it.
                if (s.Occupied[c + PInt3.Down] && !Stands(c + PInt3.Down)) continue;
                // Ground lying over a porch or a yard the place carved: a skin of it, too thin for the lattice to hold level.
                if (s.Occupied[n + PInt3.Down] && !Structure.OuterWall(s.Built, s.Occupied, n + PInt3.Down)) continue;
                float slope = mid - far;
                t.Walls++;
                foreach (float off in new[] { 0.06f, 0.3f, 0.7f })
                {
                    float want = mid + MathF.Min(0f, slope) * (1.5f - off); // ground falling toward a wall goes on falling; rising, it may level out
                    for (int side = -1; side <= 1; side++)
                    {
                        // A gap in the wall beside this block is an opening: the ground may fall in at it.
                        if (side != 0 && !Stands(c + new PInt3(v.Z != 0 ? side : 0, 0, v.X != 0 ? side : 0))) continue;
                        PInt3 under = c + new PInt3(v.Z != 0 ? side : 0, -1, v.X != 0 ? side : 0);
                        if (side != 0 && s.Occupied[under] && !Stands(under)) continue;
                        // Likewise a window or a door along the wall.
                        PInt3 along = new(v.Z != 0 ? 1 : 0, 0, v.X != 0 ? 1 : 0);
                        if (s.Occupied[c + along] && !Stands(c + along) || s.Occupied[c - along] && !Stands(c - along)) continue;
                        float near = Top(ground, s.Drawn, fx + v.X * off + ax * side, fz + v.Z * off + az * side, mid + 1.5f, bottom);
                        if (float.IsNaN(near))
                        {
                            t.Holes++;
                            if (t.Dip.Count < 3) t.Dip.Add($"no ground at {s.Name} {b.Type} at {c}, face {dir}, {off} m off; mid {mid}, far {far}, top {top}, origin ({s.X}, {s.Y}, {s.Z})");
                            continue;
                        }
                        float dip = want - near;
                        if (dip > 0.15f)
                        {
                            t.Dips++;
                            if (dip > t.Worst) t.WorstAt = $"{s.Name} {b.Type} at {c}, face {dir}, {off} m off: {dip:F2} m below {want + s.Y:F2}; origin ({s.X}, {s.Y}, {s.Z}), seed {seed}, {kind}";
                        }
                        t.Worst = MathF.Max(t.Worst, dip);
                    }
                }
            }
        }
    }
}
