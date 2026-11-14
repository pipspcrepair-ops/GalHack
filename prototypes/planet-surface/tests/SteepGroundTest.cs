using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.World;
using Archetype = Planets.Gen.Archetype;
using NV = System.Numerics.Vector3;
using Layers = GalHack.Physics.Layers;

namespace PlanetSurface;

/// <summary>
/// You must never see through the ground. Like FaceTest, this doesn't take the mesher's word for it:
/// it gathers the triangles the game is drawing at that moment (voxel chunks, far rings, horizon, with
/// the hole and sink numbers the shader has), front faces only as the renderer has them, and fires rays
/// at them from where the camera is. No tolerance: a ray from open air to a point in solid ground that
/// the drawn ground lies over has to cross a front face on the way, or the eye went through the ground.
///
/// Fresh worlds every run, the barren rugged ones first (mesas, buttes, canyons, spires), then the rest.
/// The steepest ground near each landing site is found, and the camera put at several distances from
/// it, on the ground and in the air, so that the boundaries between levels of detail cross cliffs. The
/// rays go through those crossings, and at steep ground anywhere in view. Each world is also flown past
/// at speed, its ground checked in mid-stream. Near the camera, where the ground is solid to the
/// walker, the drawn faces are held to collision too.
///   godot --headless --path prototypes/planet-surface res://tests/SteepGroundTest.tscn
/// -- --worlds=N (default 3), --world=ARCHETYPE:VARIANT:SEED to replay one a failure named.
/// </summary>
public partial class SteepGroundTest : Node
{
    /// <summary>Where a boundary crosses ground that rises at least this far within 6 m, it is a steep crossing.</summary>
    const float SteepRelief = 4f;

    /// <summary>Metres of the path past the crossing searched for a point under the drawn ground.</summary>
    const float PastSeam = 60f;

    static readonly (Archetype Kind, int Variant)[] Kinds =
    {
        (Archetype.Arid, 1), (Archetype.Alien, 0), (Archetype.Lava, 0), (Archetype.Arid, 0), (Archetype.Temperate, 0), (Archetype.Ice, 0), (Archetype.Alien, 1),
    };

    /// <summary>Whether the shader in use holds far triangles to their true facing: set from its source.</summary>
    static readonly bool FlatFaces = FileAccess.GetFileAsString("res://shaders/terrain.gdshader").Contains("dFdx(wpos)");

    int _failures;
    Main _main;
    readonly Random _rand = new();
    readonly Tally _tally = new();

    public override async void _Ready()
    {
        int worlds = 3;
        (Archetype, int, ulong)? replay = null;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--worlds=")) worlds = int.Parse(arg["--worlds=".Length..]);
            if (arg.StartsWith("--world="))
            {
                string[] p = arg["--world=".Length..].Split(':');
                replay = (Enum.Parse<Archetype>(p[0]), int.Parse(p[1]), ulong.Parse(p[2]));
            }
        }
        var watch = Stopwatch.StartNew();
        try
        {
            _main = new Main { Interactive = false };
            AddChild(_main);
            await Frames(2);
            if (replay is (Archetype kind, int variant, ulong seed)) await OneWorld(kind, variant, seed);
            else
                for (int i = 0; i < worlds; i++)
                {
                    var (kind2, variant2) = Kinds[i % Kinds.Length];
                    await OneWorld(kind2, variant2, (ulong)_rand.Next(1, int.MaxValue));
                }
            _tally.Report(this);
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

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async Task<bool> Until(Func<bool> condition, double seconds)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > seconds) return false;
            await Frames(1);
        }
        return true;
    }

    // ---- What was found ---------------------------------------------------------------------------

    sealed class Tally
    {
        public int Worlds, Views, Rays, Open, Through, Flight, Touched, Untouched, Cliffs, Painted;
        public long Voxel, Far, Horizon;
        public int Samples;
        public readonly Dictionary<string, int> AimedAt = new(), ThroughAt = new();
        public readonly List<string> First = new();
        public readonly Dictionary<string, float> WorstStep = new();
        public readonly List<string> FirstPaint = new(), FirstSolid = new();
        public readonly List<(int Level, float Error)> HeightError = new();

        public void Saw(string seam, string detail)
        {
            Through++;
            ThroughAt[seam] = ThroughAt.GetValueOrDefault(seam) + 1;
            if (First.Count < 10 && First.Count(f => f.StartsWith(seam)) < 3) First.Add($"{seam}: {detail}");
        }

        public void Report(SteepGroundTest t)
        {
            string aimed = string.Join(", ", AimedAt.OrderByDescending(a => a.Value).Select(a => $"{a.Value} at {a.Key} ({ThroughAt.GetValueOrDefault(a.Key)} through)"));
            t.Check(Through == 0 && Worlds > 0 && Open > Rays / 20 && Rays > 2000 * Worlds,
                $"no ray reaches solid ground without crossing a drawn face: {Rays} rays over {Views} views of {Worlds} worlds, {Open} clear as far as the steep ground, {Through} saw through; {aimed}{(Through > 0 ? $"; first: {string.Join("; ", First)}" : "")}");
            t.Check(Flight > 0, $"the rays held in mid-stream too: {Flight} views taken in flight, in the count above");
            t.Check(Untouched * 200 <= Touched && Touched > 100, $"what is drawn near the camera is what is solid: {Touched} rays hit drawn steep ground, and collision there, {Untouched} did not{(FirstSolid.Count > 0 ? $"; first: {string.Join("; ", FirstSolid)}" : "")}");
            t.Check(Painted * 500 <= Cliffs && Cliffs > 300 * Worlds,
                $"a cliff reads as a cliff: {Cliffs} rays hit faces steeper than 70 degrees, {Painted} of them on soil paint or a normal turned from the eye, the pale streak that looks like a hole (one in 500 allowed){(FirstPaint.Count > 0 ? $"; first: {string.Join("; ", FirstPaint)}" : "")}");
            GD.Print($"worst step up at a boundary, outward: {string.Join(", ", WorstStep.Select(s => $"{s.Key} {s.Value:F1} m"))}");
            static string Spread(List<(int Level, float Error)> e) => string.Join("; ", e.GroupBy(x => x.Level).OrderBy(g => g.Key).Select(g =>
            {
                var v = g.Select(x => x.Error).OrderBy(x => x).ToList();
                return $"ring {g.Key} ({Terrain.ClipGround.Spacing(g.Key):F0} m): median {v[v.Count / 2]:F2}, 95th pct {v[(int)(v.Count * 0.95)]:F2}, most {v[^1]:F1} m (n {v.Count})";
            }));
            if (HeightError.Count > 0)
            {
                GD.Print($"drawn far ground off the true ground, GPU rings: {Spread(HeightError)}");
                // The stated error: by ring, the 95th percentile within half the ring's spacing plus half a metre (its ground
                // leaves out the octaves finer than its spacing, and toward its edge takes the next ring's, twice as coarse).
                foreach (var g in HeightError.GroupBy(x => x.Level))
                {
                    var v = g.Select(x => x.Error).OrderBy(x => x).ToList();
                    float p95 = v[(int)(v.Count * 0.95)], bound = Terrain.ClipGround.Spacing(g.Key) / 2f + 0.5f;
                    t.Check(p95 <= bound, $"ring {g.Key}'s drawn ground within {bound:F1} m of the true ground at the 95th percentile: {p95:F2} m");
                }
            }
            GD.Print($"workers' mean time on a far chunk, levels 0, 1, 2: {string.Join(", ", Enumerable.Range(0, 3).Select(l => $"{t._main.Streamer.FarBuildMs[l] / Math.Max(1, t._main.Streamer.FarBuilds[l]):F1} ms of {t._main.Streamer.FarBuilds[l]}"))}");
            if (Samples > 0)
                GD.Print($"triangles drawn, mean of {Samples} views: {Voxel / Samples} voxel, {Far / Samples} far, {Horizon / Samples} horizon");
        }
    }

    // ---- What is drawn ------------------------------------------------------------------------------

    /// <summary>One kind of ground as the renderer has it: world-space triangles, binned on a grid in plan.</summary>
    sealed class Soup
    {
        readonly List<(NV A, NV B, NV C)> _tris = new();
        readonly List<(Vector3 A, Vector3 B, Vector3 C, Vector3 Bare)> _paint = new();
        readonly Dictionary<long, List<int>> _bins = new();
        readonly float _cell;
        readonly Vector4 _hole;
        readonly float _cut;
        int[] _seen;
        int _stamp;

        public int Count => _tris.Count;
        public bool Cuts, Flattens;

        /// <summary>The last hit: its triangle and where on it.</summary>
        public int LastTri = -1;
        public float LastU, LastV;

        public Soup(float cell, Vector4 hole = default, float cut = 0f)
        {
            _cell = cell;
            _hole = hole;
            _cut = cut;
        }

        static long Key(int x, int z) => (long)x << 32 | (uint)z;

        public void Add(NV a, NV b, NV c, Vector3 na, Vector3 nb, Vector3 nc, Vector3 bare)
        {
            int index = _tris.Count;
            _tris.Add((a, b, c));
            _paint.Add((na, nb, nc, bare));
            int x0 = (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)) / _cell), x1 = (int)MathF.Floor(MathF.Max(a.X, MathF.Max(b.X, c.X)) / _cell);
            int z0 = (int)MathF.Floor(MathF.Min(a.Z, MathF.Min(b.Z, c.Z)) / _cell), z1 = (int)MathF.Floor(MathF.Max(a.Z, MathF.Max(b.Z, c.Z)) / _cell);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (!_bins.TryGetValue(Key(x, z), out List<int> bin)) _bins[Key(x, z)] = bin = new List<int>(6);
                bin.Add(index);
            }
        }

        /// <summary>The shader's own measure: how far a point is inside the hole (negative outside).</summary>
        public static float InsideHole(Vector4 hole, float x, float z) => MathF.Min(MathF.Min(x - hole.X, hole.Z - x), MathF.Min(z - hole.Y, hole.W - z));

        bool Discarded(NV p) => Cuts && InsideHole(_hole, p.X, p.Z) > _cut;

        /// <summary>The nearest front face along a ray up to reach, or infinity. As in FaceTest, Godot's front faces wind clockwise seen from the camera.</summary>
        public float Hit(NV o, NV d, float reach)
        {
            _seen ??= new int[_tris.Count];
            if (_seen.Length < _tris.Count) _seen = new int[_tris.Count];
            _stamp++;
            float best = float.PositiveInfinity;
            float flat = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            float step = flat > 1e-4f ? _cell * 0.5f / flat : reach;
            long last = long.MinValue;
            for (float t = 0f; ; t = MathF.Min(t + step, reach))
            {
                // Every bin the ray's path in plan touches: half-cell steps can clip a corner, so the row beside it too.
                NV p = o + d * t;
                int cx = (int)MathF.Floor(p.X / _cell), cz = (int)MathF.Floor(p.Z / _cell);
                long key = Key(cx, cz);
                if (key != last)
                {
                    last = key;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (_bins.TryGetValue(Key(cx + dx, cz + dz), out List<int> bin)) best = Scan(bin, o, d, reach, best);
                }
                if (t >= reach || best < t - _cell * 2f / MathF.Max(flat, 0.01f)) return best;
            }
        }

        float Scan(List<int> bin, NV o, NV d, float reach, float best)
        {
            foreach (int i in bin)
            {
                if (_seen[i] == _stamp) continue;
                _seen[i] = _stamp;
                (NV a, NV b, NV c) = _tris[i];
                NV e1 = b - a, e2 = c - a;
                if (NV.Dot(d, NV.Cross(e1, e2)) <= 0f) continue; // its back: not drawn
                NV p = NV.Cross(d, e2);
                float det = NV.Dot(e1, p);
                if (MathF.Abs(det) < 1e-12f) continue;
                float inv = 1f / det;
                NV s = o - a;
                float u = NV.Dot(s, p) * inv;
                if (u < -1e-5f || u > 1.00001f) continue;
                NV q = NV.Cross(s, e1);
                float v = NV.Dot(d, q) * inv;
                if (v < -1e-5f || u + v > 1.00001f) continue;
                float t = NV.Dot(e2, q) * inv;
                if (t < 0f || t > reach || t >= best) continue;
                if (Discarded(o + d * t)) continue;
                best = t;
                LastTri = i;
                LastU = u;
                LastV = v;
            }
            return best;
        }

        /// <summary>
        /// What terrain.gdshader makes of the last hit, seen along d: how far the triangle really faces
        /// up, how bare it shades, and how far the normal it shades with faces the camera.
        /// </summary>
        public (float GeomUp, float Bare, float Facing, float Seen, float Rise) Paint(NV d)
        {
            (NV a, NV b, NV c) = _tris[LastTri];
            NV facing = NV.Normalize(NV.Cross(b - a, c - a));
            if (NV.Dot(facing, d) > 0f) facing = -facing;
            var (na, nb, nc, bare) = _paint[LastTri];
            float w = 1f - LastU - LastV;
            var mixed = na * w + nb * LastU + nc * LastV;
            NV n = NV.Normalize(new NV(mixed.X, mixed.Y, mixed.Z));
            float up = n.Y, facesUp = up, alpha = bare.X * w + bare.Y * LastU + bare.Z * LastV;
            NV p = a * w + b * LastU + c * LastV;
            if (Flattens && (!Cuts || InsideHole(_hole, p.X, p.Z) <= 0f) && facing.Y >= 0.01f && facing.Y < 0.5f)
            {
                n = NV.Normalize(NV.Lerp(n, facing, Smooth(0.6f, 0.85f, n.Y) * (1f - Smooth(0.2f, 0.4f, facing.Y))));
                n = NV.Normalize(n - d * MathF.Max(0f, 0.05f + NV.Dot(n, d)));
                up = n.Y;
                facesUp = MathF.Min(up, facing.Y);
            }
            return (MathF.Abs(facing.Y), MathF.Max(alpha, 1f - Smooth(0.3f, 0.62f, facesUp)), -NV.Dot(n, d), -NV.Dot(facing, d), MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) - MathF.Min(a.Y, MathF.Min(b.Y, c.Y)));
        }

        static float Smooth(float e0, float e1, float x)
        {
            float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }

    sealed class Drawn
    {
        public Soup Voxel = new(4f), Far, Horizon;
        public Vector4 VoxelHole, FarHole;

        public float Hit(NV o, NV d, float reach) => Hit(o, d, reach, out _);

        public float Hit(NV o, NV d, float reach, out Soup which)
        {
            float a = Voxel.Hit(o, d, reach), b = Far.Hit(o, d, reach), c = Horizon.Hit(o, d, reach);
            which = a <= b && a <= c ? Voxel : b <= c ? Far : Horizon;
            return MathF.Min(a, MathF.Min(b, c));
        }

        /// <summary>Whether drawn ground lies over a point: straight down from far above it, a front face before it.</summary>
        public bool Under(NV p)
        {
            var from = new NV(p.X, 4000f, p.Z);
            return !float.IsInfinity(Hit(from, -NV.UnitY, 4000f - p.Y));
        }
    }

    static void Gather(Soup soup, ArrayMesh mesh, Vector3 at, Vector4 hole, float sink, float sinkFrom)
    {
        Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
        var colors = (Color[])arrays[(int)Mesh.ArrayType.Color];
        Gather(soup, (Vector3[])arrays[(int)Mesh.ArrayType.Vertex], (int[])arrays[(int)Mesh.ArrayType.Index], (Vector3[])arrays[(int)Mesh.ArrayType.Normal], colors.Select(c => c.A).ToArray(), at, hole, sink, sinkFrom);
    }

    static void Gather(Soup soup, Vector3[] vertices, int[] indices, Vector3[] normals, float[] bare, Vector3 at, Vector4 hole, float sink, float sinkFrom)
    {
        var world = new NV[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i] + at;
            // The shader's vertex stage.
            if (sink > 0f && Soup.InsideHole(hole, v.X, v.Z) >= sinkFrom) v.Y -= sink;
            world[i] = new NV(v.X, v.Y, v.Z);
        }
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            soup.Add(world[a], world[b], world[c], normals[a], normals[b], normals[c], new Vector3(bare[a], bare[b], bare[c]));
        }
    }

    Drawn Snapshot(Vector3 eye)
    {
        var s = _main.Streamer;
        float Num(ShaderMaterial m, string name) => m.GetShaderParameter(name).VariantType == Variant.Type.Nil ? 0f : (float)m.GetShaderParameter(name);
        Vector4 farHole = (Vector4)s.FarMaterial.GetShaderParameter("hole"), horizonHole = (Vector4)s.HorizonMaterial.GetShaderParameter("hole");
        float farCut = Num(s.FarMaterial, "hole_cut"), horizonCut = Num(s.HorizonMaterial, "hole_cut");
        var drawn = new Drawn { VoxelHole = farHole, FarHole = horizonHole };
        drawn.Far = new Soup(8f, farHole, farCut) { Cuts = farHole != Vector4.Zero, Flattens = FlatFaces };
        drawn.Horizon = new Soup(64f, horizonHole, horizonCut) { Cuts = horizonHole != Vector4.Zero, Flattens = FlatFaces };
        foreach (var (mesh, offset) in s.DrawnVoxels()) Gather(drawn.Voxel, mesh, offset, default, 0f, 0f);
        foreach (var (v, indices, normals, bare) in s.Clip.Drawn(eye, float.MaxValue)) Gather(drawn.Far, v, indices, normals, bare, Vector3.Zero, farHole, Num(s.FarMaterial, "hole_sink"), Num(s.FarMaterial, "hole_sink_from"));
        var (horizon, horizonAt) = s.DrawnHorizon();
        if (horizon != null) Gather(drawn.Horizon, horizon, horizonAt, horizonHole, Num(s.HorizonMaterial, "hole_sink"), Num(s.HorizonMaterial, "hole_sink_from"));
        return drawn;
    }

    // ---- Where to look ------------------------------------------------------------------------------

    /// <summary>The steepest ground within a few kilometres of the landing site, a place at a time, steepest first.</summary>
    static List<(double X, double Z, float Rise)> SteepPlaces(PlanetWorld w, int count)
    {
        const int Half = 200;
        const double Step = 16;
        double ox = w.Landing.X, oz = w.Landing.Y;
        int n = Half * 2 + 1;
        var height = new float[n * n];
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++) height[j * n + i] = w.Source.SurfaceHeight(ox + (i - Half) * Step, oz + (j - Half) * Step, 4);
        });
        var found = new List<(double, double, float)>();
        for (int j = 1; j < n - 1; j++)
        for (int i = 1; i < n - 1; i++)
        {
            int k = j * n + i;
            float rise = 0f;
            foreach (int o in new[] { k - 1, k + 1, k - n, k + n }) rise = MathF.Max(rise, height[o] - height[k]);
            if (rise > 6f && height[k] > 1f) found.Add((ox + (i - Half) * Step, oz + (j - Half) * Step, rise));
        }
        found.Sort((a, b) => b.Item3.CompareTo(a.Item3));
        var places = new List<(double X, double Z, float Rise)>();
        foreach (var f in found)
        {
            if (places.Count >= count) break;
            if (places.All(p => Mathx.WrapDelta(p.X - f.Item1, w.Width) is var dx && Mathx.WrapDelta(p.Z - f.Item2, w.Height) is var dz && dx * dx + dz * dz > 500 * 500)) places.Add(f);
        }
        return places;
    }

    async Task OneWorld(Archetype kind, int variant, ulong seed)
    {
        var spec = PlanetSpec.Roll(seed, kind, variant);
        GD.Print($"--- {spec} (replay: --world={kind}:{variant}:{seed})");
        _main.Arrive(spec);
        if (!await Until(() => _main.IsReady, 120))
        {
            Fail($"landing on {spec} did not finish (pending {_main.Streamer.Pending})");
            return;
        }
        _tally.Worlds++;
        PlanetWorld w = _main.World;
        List<(double X, double Z, float Rise)> places = await Task.Run(() => SteepPlaces(w, 3));
        if (places.Count == 0)
        {
            GD.Print("no steep ground near this landing site");
            places.Add((w.Landing.X, w.Landing.Y, 0f));
        }
        _main.Player.ProcessMode = ProcessModeEnum.Disabled;
        GD.Print($"steepest ground: {string.Join(", ", places.Select(p => $"{p.Rise:F0} m over 16 m at {p.X:F0}, {p.Z:F0}"))}");

        // The distances put the boundaries over the cliff: voxels to far rings about 88 to 104 m, the far rings' levels about 384, 768 and 1,536 m.
        float[] distances = { 40f, 96f, 300f, 384f, 768f, 1536f };
        float[] heights = { 1.7f, 1.7f, 30f, 150f };
        int view = 0;
        foreach (var place in places)
            for (int k = 0; k < 2; k++, view++)
            {
                float d = distances[_rand.Next(distances.Length)] + (float)_rand.NextDouble() * 24f - 12f, up = heights[view % heights.Length], a = (float)_rand.NextDouble() * Mathf.Tau;
                double x = place.X + MathF.Cos(a) * d, z = place.Z + MathF.Sin(a) * d;
                var eye = new Vector3((float)x, w.GroundHeight(x, z) + up, (float)z);
                eye.Y = MathF.Max(eye.Y, up);
                _main.Fly.VelocityOverride = null;
                _main.Fly.Begin(eye, -a, 0f);
                await Frames(3);
                if (!await Until(() => _main.Streamer.Settled, 60))
                {
                    GD.Print($"not settled at {eye} (pending {_main.Streamer.Pending}): skipped");
                    continue;
                }
                Look(w, eye, $"{spec.Archetype} seed {seed}");
                if (k == 0 && view % 2 == 0 && up < 5f) await Solid(w, eye);
            }

        Heights(w);

        // Flown past at speed: the ground in mid-stream, levels and holes as they stand at that moment.
        var place0 = places[0];
        float b = (float)_rand.NextDouble() * Mathf.Tau;
        var dir = new Vector3(MathF.Cos(b), 0f, MathF.Sin(b));
        Vector3 start = new Vector3((float)place0.X, 0f, (float)place0.Z) - dir * 700f;
        for (float along = 0f; along < 1600f; along += 16f)
            start.Y = MathF.Max(start.Y, w.GroundHeight(start.X + dir.X * along, start.Z + dir.Z * along) + 20f);
        _main.Fly.Begin(start, 0f, 0f);
        await Frames(3);
        await Until(() => _main.Streamer.Settled, 60);
        _main.Fly.VelocityOverride = dir * FlightSpeed;
        for (int i = 0; i < 4; i++)
        {
            await Frames(20);
            Look(w, _main.Camera.GlobalPosition, $"{spec.Archetype} seed {seed}, in flight");
            _tally.Flight++;
        }
        _main.Fly.VelocityOverride = null;
    }

    const float FlightSpeed = 100f; // FreeFly's fast speed

    /// <summary>
    /// How far the drawn far ground stands off the true ground (the voxels' surface), ring by ring, at random points of each
    /// ring as it is drawn now.
    /// </summary>
    void Heights(PlanetWorld w)
    {
        var s = _main.Streamer;
        Terrain.ClipGround clip = s.Clip;
        for (int l = clip.Floor; l < clip.Levels; l++)
        {
            Vector4 outer = clip.Ring(l), inner = l > clip.Floor ? clip.Ring(l - 1) : Vector4.Zero;
            for (int n = 0; n < 500; n++)
            {
                float x = Mathf.Lerp(outer.X, outer.Z, (float)_rand.NextDouble()), z = Mathf.Lerp(outer.Y, outer.W, (float)_rand.NextDouble());
                if (inner != Vector4.Zero && x > inner.X && x < inner.Z && z > inner.Y && z < inner.W) continue;
                if (clip.Height(x, z) is not float drawn) continue;
                float truth = w.GroundHeight(x, z);
                _tally.HeightError.Add((l, MathF.Abs(drawn - truth)));
            }
        }
    }

    // ---- The rays -----------------------------------------------------------------------------------

    /// <summary>Everything aimed from one camera position.</summary>
    void Look(PlanetWorld w, Vector3 eye3, string where)
    {
        Drawn drawn = Snapshot(eye3);
        _tally.Views++;
        _tally.Voxel += drawn.Voxel.Count;
        _tally.Far += drawn.Far.Count;
        _tally.Horizon += drawn.Horizon.Count;
        _tally.Samples++;
        var eye = new NV(eye3.X, eye3.Y, eye3.Z);
        // Any height over the same spot sees the same ground: the levels go by where in plan the camera is.
        NV[] eyes = { eye, eye + new NV(0f, 8f, 0f), eye + new NV(0f, 30f, 0f), eye + new NV(0f, 100f, 0f) };

        // The boundaries, in plan: voxels to far rings, the far rings' levels, the far rings to the horizon.
        var seams = new List<(string Name, Vector4 Rect)> { ("voxels to far", drawn.VoxelHole), ("far to horizon", drawn.FarHole) };
        var clip = _main.Streamer.Clip;
        for (int l = clip.Floor; l + 1 < clip.Levels; l++) seams.Add(($"far level {l} to {l + 1}", clip.Ring(l)));
        foreach (var (name, r) in seams)
        {
            if (r == Vector4.Zero) continue;
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float from = alongX ? r.X : r.Y, to = alongX ? r.Z : r.W, fixedAt = side switch { 0 => r.Y, 1 => r.W, 2 => r.X, _ => r.Z };
                var normal = alongX ? new Vector2(0, 1) : new Vector2(1, 0);
                float step = name == "far to horizon" ? 8f : 4f;
                for (float t = from + (float)_rand.NextDouble() * step; t < to; t += step)
                {
                    var p = alongX ? new Vector2(t, fixedAt) : new Vector2(fixedAt, t);
                    float a = w.GroundHeight(p.X - normal.X * 3f, p.Y - normal.Y * 3f), b = w.GroundHeight(p.X + normal.X * 3f, p.Y + normal.Y * 3f), c = w.GroundHeight(p.X, p.Y);
                    float lo = MathF.Min(a, MathF.Min(b, c)), hi = MathF.Max(a, MathF.Max(b, c));
                    if (hi - lo < SteepRelief) continue;
                    // The drawn tops either side of the boundary, a hair off it. Only a step up going outward can be looked through.
                    var mid = new Vector2((r.X + r.Z) * 0.5f, (r.Y + r.W) * 0.5f);
                    float sign = (p - mid).Dot(normal) > 0f ? 1f : -1f;
                    float inner = Top(drawn, p - normal * sign * 0.05f), outer = Top(drawn, p + normal * sign * 0.05f);
                    bool stepped = !float.IsInfinity(inner) && !float.IsInfinity(outer) && outer > inner + 0.05f;
                    if (stepped && outer - inner > _tally.WorstStep.GetValueOrDefault(name)) _tally.WorstStep[name] = outer - inner;
                    foreach (NV camera in eyes)
                    {
                        if (stepped)
                            for (int k = 0; k < 3; k++)
                                Fire(w, drawn, camera, new NV(p.X, inner + (outer - inner) * (float)_rand.NextDouble(), p.Y), name, where);
                        Fire(w, drawn, camera, new NV(p.X, lo - 3f + (hi - lo + 6f) * (float)_rand.NextDouble(), p.Y), name, where);
                    }
                }
            }
        }
        Cliffs(drawn, eyes[0]);
    }

    /// <summary>Rays anywhere over the camera's view: whatever they hit that is sheer, the shader must draw as rock, lit as a wall.</summary>
    void Cliffs(Drawn drawn, NV eye)
    {
        for (int i = 0; i < 6000; i++)
        {
            float a = (float)_rand.NextDouble() * Mathf.Tau, e = (float)(_rand.NextDouble() * 0.35 - 0.2);
            var d = NV.Normalize(new NV(MathF.Cos(a), e, MathF.Sin(a)));
            float t = drawn.Hit(eye, d, 2500f, out Soup soup);
            if (float.IsInfinity(t)) continue;
            var (geom, bare, facing, seen, rise) = soup.Paint(d);
            if (geom > 0.34f || rise < 6f) continue; // steeper than 70 degrees, and a real face
            NV p = eye + d * t;
            // A wall going down from a hole's edge, and the top of a curtain, are the ground closing a small step: they stay the ground they hang from.
            if (rise > 500f) continue;
            if (geom < 1e-4f)
            {
                var side = new NV(d.X, 0f, d.Z) * 0.1f;
                if (p.Y > MathF.Max(Top(drawn, new Vector2(p.X - side.X, p.Z - side.Z)), Top(drawn, new Vector2(p.X + side.X, p.Z + side.Z))) - 4f) continue;
            }
            _tally.Cliffs++;
            if (bare >= 0.5f && (facing > 0.02f || seen < 0.2f)) continue; // a wall seen edge-on is edge-on whatever its normals
            _tally.Painted++;
            if (_tally.FirstPaint.Count < 6) _tally.FirstPaint.Add($"{(soup == drawn.Voxel ? "voxels" : soup == drawn.Far ? "far" : "horizon")} at {p.X:F0}, {p.Y:F0}, {p.Z:F0}, {t:F0} m from {eye.X:F0}, {eye.Y:F0}, {eye.Z:F0}: bare {bare:F2}, its normal to the eye {facing:F2}");
        }
    }

    static float Top(Drawn drawn, Vector2 p)
    {
        float t = drawn.Hit(new NV(p.X, 4000f, p.Y), -NV.UnitY, 5000f);
        return 4000f - t;
    }

    /// <summary>One ray from the eye through a point at a steep crossing, on until it is under the drawn ground.</summary>
    void Fire(PlanetWorld w, Drawn drawn, NV eye, NV through, string seam, string where)
    {
        if (eye.Y < w.GroundHeight(eye.X, eye.Z) + 0.5f) return; // a camera in flight never goes under the ground
        NV d = through - eye;
        float toSeam = d.Length();
        if (toSeam < 1f) return;
        d /= toSeam;
        // Somewhere past the crossing, under the ground as it is drawn, and solid ground in truth (caves are air).
        float end = -1f;
        for (float t = toSeam + 1f; t < toSeam + PastSeam; t += 2f)
        {
            NV p = eye + d * t;
            if (p.Y > w.GroundHeight(p.X, p.Z) - 0.5f) continue;
            if (!drawn.Under(p + new NV(0f, 1f, 0f))) continue;
            end = t;
            break;
        }
        if (end < 0f) return;
        _tally.Rays++;
        _tally.AimedAt[seam] = _tally.AimedAt.GetValueOrDefault(seam) + 1;
        float hit = drawn.Hit(eye, d, end);
        if (hit >= toSeam - 8f) _tally.Open++;
        if (!float.IsInfinity(hit)) return;
        NV q = eye + d * end;
        if (w.Source.CellDensity((int)MathF.Floor(q.X), (int)MathF.Floor(q.Y), (int)MathF.Floor(q.Z)) <= 0f) return; // a cave or a room: open to the eye
        float heading = Mathf.RadToDeg(MathF.Atan2(d.X, -d.Z)), pitch = Mathf.RadToDeg(MathF.Asin(d.Y));
        var clip = _main.Streamer.Clip;
        string chunks = $"[far ring {Enumerable.Range(clip.Floor, Math.Max(0, clip.Levels - clip.Floor)).FirstOrDefault(l => clip.Ring(l) is var r && through.X >= r.X && through.X <= r.Z && through.Z >= r.Y && through.Z <= r.W, -1)}]";
        _tally.Saw(seam, $"{chunks} tops {Top(drawn, new Vector2(through.X + d.X * 0.1f, through.Z + d.Z * 0.1f)):F1} past, {Top(drawn, new Vector2(through.X - d.X * 0.1f, through.Z - d.Z * 0.1f)):F1} before, hit nothing to {end:F0} m (seam at {toSeam:F0}); far complete {_main.Streamer.FarComplete}, voxels {_main.Streamer.CompleteRadius}; {where}, eye {eye.X:F1}, {eye.Y:F1}, {eye.Z:F1} up {eye.Y - w.GroundHeight(eye.X, eye.Z):F1} through {through.X:F1}, {through.Y:F1}, {through.Z:F1} heading {heading:F1} pitch {pitch:F1}");
    }

    /// <summary>Near the camera the walker's ground is the drawn ground: a ray that hits one hits the other, at the same place.</summary>
    async Task Solid(PlanetWorld w, Vector3 eye)
    {
        // Collision is built for walking: put the walker there, and take the rays in a physics tick.
        _main.Fly.Active = false;
        _main.Player.ProcessMode = ProcessModeEnum.Inherit;
        _main.Player.GlobalPosition = eye;
        await Frames(2);
        bool ready = await Until(() => _main.Streamer.ReadyAround(eye, 1, needCollision: true), 30);
        _main.Player.ProcessMode = ProcessModeEnum.Disabled;
        if (ready)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Drawn drawn = Snapshot(eye);
            PhysicsDirectSpaceState3D space = _main.GetWorld3D().DirectSpaceState;
            var from = new NV(eye.X, eye.Y, eye.Z);
            for (int i = 0; i < 400; i++)
            {
                NV d = NV.Normalize(new NV((float)_rand.NextDouble() - 0.5f, (float)_rand.NextDouble() * 0.5f - 0.3f, (float)_rand.NextDouble() - 0.5f));
                float t = drawn.Voxel.Hit(from, d, 15f); // collision is sure out to the next column
                if (float.IsInfinity(t)) continue;
                _tally.Touched++;
                NV to = from + d * (t + 1.5f);
                var query = PhysicsRayQueryParameters3D.Create(eye, new Vector3(to.X, to.Y, to.Z), Layers.World);
                Godot.Collections.Dictionary r = space.IntersectRay(query);
                if (r.Count != 0 && MathF.Abs(eye.DistanceTo((Vector3)r["position"]) - t) <= 0.05f) continue;
                _tally.Untouched++;
                if (_tally.FirstSolid.Count < 4) _tally.FirstSolid.Add($"from {eye} along {d}: drawn at {t:F2} m, solid at {(r.Count == 0 ? "none" : eye.DistanceTo((Vector3)r["position"]).ToString("F2"))}");
            }
        }
        _main.Fly.Begin(eye, 0f, 0f);
        await Until(() => _main.Streamer.Settled, 30);
    }
}
