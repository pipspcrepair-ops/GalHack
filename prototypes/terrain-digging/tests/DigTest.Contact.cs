using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Godot;
using TerrainDigging.Terrain;
using NQuaternion = System.Numerics.Quaternion;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace TerrainDigging;

/// <summary>
/// Rule 4, headless: blocks set into the ground clear what is inside them and meet the ground
/// around them flush, at any size and angle, on slopes and flat ground, with nothing changed
/// outside them, no sag ring and no gap under their edges. Taking one down leaves its hole as
/// dug ground (remove-only); seams, replays and saves all agree to the bit.
/// </summary>
public partial class DigTest
{
    /// <summary>A block (or a run of them) set into the ground for one check.</summary>
    sealed record ContactCase(string Name, NVector2 At, NVector3 Size, float Yaw, float Pitch = 0f, float Roll = 0f, float Sink = 0.5f, int Run = 1, NVector2 Nudge = default);

    const float Degrees = MathF.PI / 180f;

    // How close a cut point must sit to the face it was cut on, and to the original surface (m).
    const float OnFace = 1e-3f, OnSurface = 2e-3f;

    // How far the ground next to a block may move (m): it doesn't, so this is float noise only.
    const float SagTolerance = 1e-4f;

    NVector2 _flat, _slope;
    float _slopeDegrees;

    void FindContactSpots(ChunkStore store)
    {
        float bestFlat = float.MaxValue, bestSlope = float.MaxValue;
        for (int z = -64; z <= 64; z += 3)
            for (int x = -64; x <= 64; x += 3)
            {
                if (store.IsWaterColumn(x, z) || NVector2.Distance(new NVector2(x, z), SimpleTerrainSource.BeaconSite) < 16f) continue;
                float grade = Grade(store, x, z, out float spread);
                // Flat: the least grade. Slope: closest to 30 degrees, and an even one (no mesa lip).
                if (grade + spread < bestFlat)
                {
                    bestFlat = grade + spread;
                    _flat = new NVector2(x + 0.5f, z + 0.5f);
                }
                float off = MathF.Abs(MathF.Atan(grade) / Degrees - 30f) + spread * 60f;
                if (off < bestSlope)
                {
                    bestSlope = off;
                    _slope = new NVector2(x + 0.5f, z + 0.5f);
                    _slopeDegrees = MathF.Atan(grade) / Degrees;
                }
            }
    }

    /// <summary>The ground's grade at a point (rise per metre), and how much it varies within 2.5 m.</summary>
    static float Grade(ChunkStore store, float x, float z, out float spread)
    {
        float G(float px, float pz)
        {
            float gx = (store.Source.SurfaceHeight(px + 1f, pz) - store.Source.SurfaceHeight(px - 1f, pz)) * 0.5f;
            float gz = (store.Source.SurfaceHeight(px, pz + 1f) - store.Source.SurfaceHeight(px, pz - 1f)) * 0.5f;
            return MathF.Sqrt(gx * gx + gz * gz);
        }
        float g = G(x, z);
        spread = 0f;
        foreach ((float dx, float dz) in new[] { (2.5f, 0f), (-2.5f, 0f), (0f, 2.5f), (0f, -2.5f) })
            spread = MathF.Max(spread, MathF.Abs(G(x + dx, z + dz) - g));
        return g;
    }

    static IEnumerable<ContactCase> ContactCases(NVector2 at, string where)
    {
        yield return new ContactCase($"2 m block, square, {where}", at, new NVector3(2f), 0f);
        yield return new ContactCase($"2 m block at 17°, {where}", at, new NVector3(2f), 17f);
        yield return new ContactCase($"2 m block at 45°, {where}", at, new NVector3(2f), 45f);
        yield return new ContactCase($"2 m block at 90°, {where}", at, new NVector3(2f), 90f);
        yield return new ContactCase($"2 m block off the cell grid, {where}", at, new NVector3(2f), 0f, Nudge: new NVector2(0.37f, 0.21f));
        yield return new ContactCase($"2 m block tilted 6° and 4° at 25°, {where}", at, new NVector3(2f), 25f, 6f, 4f);
        yield return new ContactCase($"0.5 m block, square, {where}", at, new NVector3(0.5f), 0f);
        yield return new ContactCase($"0.5 m block at 33°, {where}", at, new NVector3(0.5f), 33f);
        yield return new ContactCase($"0.5 m block at 33° tilted 8° and -5°, {where}", at, new NVector3(0.5f), 33f, 8f, -5f);
        yield return new ContactCase($"2 m block barely in the ground at 9°, {where}", at, new NVector3(2f), 9f, Sink: 0.06f);
        yield return new ContactCase($"2 m block buried nine tenths at 71°, {where}", at, new NVector3(2f), 71f, Sink: 0.9f);
        yield return new ContactCase($"4 x 1 x 2 m slab at 12°, {where}", at, new NVector3(4f, 1f, 2f), 12f, Sink: 0.6f);
        yield return new ContactCase($"a row of three 2 m blocks at 20°, {where}", at, new NVector3(2f), 20f, Run: 3);
        yield return new ContactCase($"a row of four 0.5 m blocks at 61°, {where}", at, new NVector3(0.5f), 61f, Run: 4);
    }

    static NQuaternion RotationOf(ContactCase c) =>
        NQuaternion.CreateFromYawPitchRoll(c.Yaw * Degrees, c.Pitch * Degrees, c.Roll * Degrees);

    /// <summary>The case's blocks, sunk into the visible ground by the given fraction of their height.</summary>
    static List<(VolumeKey key, StructureVolume volume)> VolumesFor(ContactCase c, Surface ground)
    {
        NQuaternion q = RotationOf(c);
        NVector3 along = NVector3.Transform(NVector3.UnitX, q) * c.Size.X;
        NVector2 at = c.At + c.Nudge;
        float top = ground.Down(at.X, at.Y, 500f);
        var centre = new NVector3(at.X, top + c.Size.Y * (0.5f - c.Sink), at.Y);
        var list = new List<(VolumeKey, StructureVolume)>();
        for (int i = 0; i < c.Run; i++)
        {
            NVector3 p = centre + along * (i - (c.Run - 1) * 0.5f);
            // Each block of a run is sunk into the ground under it by the same fraction (on a slope
            // a row of blocks steps down it), so none of them hangs over the ground.
            if (c.Run > 1) p.Y = ground.Down(p.X, p.Z, 500f) + c.Size.Y * (0.5f - c.Sink);
            list.Add((new VolumeKey(7, i, 0, 0), new StructureVolume(p, q, c.Size)));
        }
        return list;
    }

    /// <summary>The four checks of the brief, for every case on flat ground and on a slope.</summary>
    void TestFlushContact()
    {
        var reference = NewStore();
        FindContactSpots(reference);
        Info($"flat spot {_flat}, slope spot {_slope} ({_slopeDegrees:F0}°)");
        long fallbacks = StructureClipper.Fallbacks;
        var cases = ContactCases(_flat, "flat ground").Concat(ContactCases(_slope, $"{_slopeDegrees:F0}° slope")).ToList();
        int failedCases = 0;
        double worstSag = 0, worstLegacySag = 0;
        int legacyOutside = 0, cutPoints = 0, samples = 0, edgeSamples = 0, buried = 0;
        foreach (ContactCase c in cases)
        {
            var before = new ChunkStore(reference.Base);
            CellBox region = RegionAround(c.At, 6, before);
            Surface ground = Surface.Of(before, region);
            List<(VolumeKey key, StructureVolume volume)> parts = VolumesFor(c, ground);

            var after = new ChunkStore(reference.Base);
            EditResult cleared = after.ClearForStructure(parts);
            Surface cut = Surface.Of(after, region);
            ContactReport r = Measure(before, after, ground, cut, parts.Select(p => p.volume).ToList(), region);
            worstSag = Math.Max(worstSag, r.MaxSag);
            cutPoints += r.CutPoints;
            samples += r.Samples;

            // The same block cleared the old way (the slice's BuildTool.ClearGround before this change), for comparison.
            var legacy = new ChunkStore(reference.Base);
            int outside = LegacyClear(legacy, parts.Select(p => p.volume));
            legacyOutside += outside;
            worstLegacySag = Math.Max(worstLegacySag, SagOf(ground, Surface.Of(legacy, region), parts.Select(p => p.volume).ToList(), region));

            edgeSamples += r.EdgeSamples;
            buried += r.Buried;
            // Exactly the solid cells whose centres lie inside (a 0.5 m block can hold none at all).
            bool clears = cleared.Removed.Count == parts.SelectMany(p => p.volume.CellsInside()).Distinct().Count(before.IsSolid);
            bool ok = clears && r.SolidInside == 0 && r.ChangedOutside == 0 && r.Moved == 0 && r.OffSurface == 0 &&
                      r.InsideBlock == 0 && r.MaxSag <= SagTolerance && r.Gaps == 0 && r.EdgeSamples > 0;
            if (!ok)
            {
                failedCases++;
                Fail($"flush: {c.Name}: cleared {cleared.Removed.Count}, solid inside {r.SolidInside}, cells changed outside {r.ChangedOutside}, " +
                     $"vertices moved {r.Moved}, cut points off the old surface {r.OffSurface}, terrain inside {r.InsideBlock}, " +
                     $"max sag {r.MaxSag:F5} m, gaps under edges {r.Gaps} of {r.EdgeSamples}");
            }
        }
        Check(failedCases == 0,
            $"flush contact: {cases.Count} blocks (2 m and 0.5 m, square, turned, tilted, off the grid, slabs and rows) set into flat ground and a " +
            $"{_slopeDegrees:F0}° slope: no solid cell left inside, no cell outside changed, the ground outside bit-identical " +
            $"({cutPoints:N0} new points, all on a block face and on the original surface), nothing drawn inside a block");
        Check(failedCases == 0 && worstSag <= SagTolerance,
            $"no sag ring: {samples:N0} rays down within 1.5 m of the blocks find the ground where it was (worst {worstSag * 1000:F3} mm); " +
            $"no gap: at {edgeSamples:N0} points 3 cm outside the blocks' bottom edges ({buried:N0} of them below ground) the ground is untouched, " +
            $"so each face runs down into it. The old clearing sagged up to {worstLegacySag:F2} m and cut {legacyOutside} cells outside the blocks");
        Check(StructureClipper.Fallbacks == fallbacks, $"every cut triangle re-triangulated cleanly ({StructureClipper.Fallbacks - fallbacks} fallbacks)");
    }

    sealed class ContactReport
    {
        public int SolidInside, ChangedOutside, Moved, OffSurface, InsideBlock, Gaps, EdgeSamples, Buried, CutPoints, Samples;
        public double MaxSag;
    }

    static CellBox RegionAround(NVector2 at, int radius, ChunkStore store)
    {
        int x = (int)MathF.Floor(at.X), z = (int)MathF.Floor(at.Y);
        int y = (int)MathF.Floor(store.Source.SurfaceHeight(at.X, at.Y));
        return new CellBox(new Int3(x - radius, y - radius, z - radius), new Int3(x + radius + 1, y + radius + 1, z + radius + 1));
    }

    ContactReport Measure(ChunkStore before, ChunkStore after, Surface ground, Surface cut, List<StructureVolume> volumes, CellBox region)
    {
        var r = new ContactReport();

        // 1. No solid terrain cell inside any block.
        foreach (StructureVolume v in volumes)
            r.SolidInside += v.CellsInside().Count(after.IsSolid);

        // 2. Cells outside the blocks untouched: material and density exactly as generated.
        foreach (Int3 cell in ChunkStore.BoxCells(region))
        {
            if (volumes.Any(v => v.ContainsCell(cell))) continue;
            if (before.MaterialAt(cell) != after.MaterialAt(cell) || before.DensityAt(cell) != after.DensityAt(cell)) r.ChangedOutside++;
        }

        // 3. Every vertex of the new surface is either an old one, to the bit, or a cut point on a
        //    block face that lies on the old surface. Nothing is drawn inside a block.
        foreach (NVector3 p in cut.Vertices)
        {
            if (ground.HasVertex(p)) continue;
            r.CutPoints++;
            if (!volumes.Any(v => OnSurfaceOf(v, p, OnFace))) r.Moved++;
            else if (ground.DistanceTo(p) > OnSurface) r.OffSurface++;
        }
        for (int t = 0; t < cut.Triangles; t++)
        {
            NVector3 centroid = cut.Centroid(t);
            if (volumes.Any(v => InsideBy(v, centroid, OnFace)))
            {
                if (r.InsideBlock++ < 3)
                    GD.Print($"      inside: triangle {cut.Corner(t, 0)} {cut.Corner(t, 1)} {cut.Corner(t, 2)}, local {string.Join(" ", volumes.Select(v => v.Local(centroid)))}");
            }
        }

        // The ground round the blocks, by rays straight down: where it was, and nothing inside a block.
        NVector3 lo = volumes.Aggregate(new NVector3(float.MaxValue), (m, v) => NVector3.Min(m, v.Min));
        NVector3 hi = volumes.Aggregate(new NVector3(float.MinValue), (m, v) => NVector3.Max(m, v.Max));
        for (float x = lo.X - 1.5f; x <= hi.X + 1.5f; x += 0.07f)
            for (float z = lo.Z - 1.5f; z <= hi.Z + 1.5f; z += 0.07f)
            {
                foreach (float y in cut.AllDown(x, z))
                    if (volumes.Any(v => InsideBy(v, new NVector3(x, y, z), OnFace)))
                    {
                        if (r.InsideBlock++ < 3)
                            GD.Print($"      inside: ray at ({x:F3}, {z:F3}) meets the ground at {y:F4}; local {string.Join(" ", volumes.Select(v => v.Local(new NVector3(x, y, z))))}; " +
                                     $"triangle {string.Join(" ", cut.TrianglesAt(x, z).Select(t => $"[{cut.Corner(t, 0)} {cut.Corner(t, 1)} {cut.Corner(t, 2)}]"))}");
                    }
                if (volumes.Any(v => VerticalHits(v, x, z, 0.01f))) continue;
                float a = ground.Down(x, z, 500f), b = cut.Down(x, z, 500f);
                if (float.IsNaN(a) || float.IsNaN(b)) continue;
                r.Samples++;
                r.MaxSag = Math.Max(r.MaxSag, MathF.Abs(a - b));
            }

        // 4. No gap under a block's bottom edges: just outside each one the ground is where it was,
        //    and where it was above the edge it still is, so the face runs down into it.
        foreach (StructureVolume v in volumes)
            foreach ((NVector3 s, NVector3 outward) in BottomEdgeSamples(v))
            {
                NVector3 p = s + outward * 0.03f;
                if (volumes.Any(o => VerticalHits(o, p.X, p.Z, 0f))) continue; // an edge shared with the next block
                r.EdgeSamples++;
                float a = ground.Down(p.X, p.Z, 500f), b = cut.Down(p.X, p.Z, 500f);
                if (a >= s.Y) r.Buried++;
                if (float.IsNaN(b) || MathF.Abs(a - b) > SagTolerance || (a >= s.Y && b < s.Y - 1e-3f)) r.Gaps++;
            }
        return r;
    }

    /// <summary>The biggest drop of the ground within 1.5 m of the blocks, outside them (the old sag ring).</summary>
    static double SagOf(Surface ground, Surface cut, List<StructureVolume> volumes, CellBox region)
    {
        double worst = 0;
        NVector3 lo = volumes.Aggregate(new NVector3(float.MaxValue), (m, v) => NVector3.Min(m, v.Min));
        NVector3 hi = volumes.Aggregate(new NVector3(float.MinValue), (m, v) => NVector3.Max(m, v.Max));
        for (float x = lo.X - 1.5f; x <= hi.X + 1.5f; x += 0.1f)
            for (float z = lo.Z - 1.5f; z <= hi.Z + 1.5f; z += 0.1f)
            {
                if (volumes.Any(v => VerticalHits(v, x, z, 0.01f))) continue;
                float a = ground.Down(x, z, 500f), b = cut.Down(x, z, 500f);
                if (float.IsNaN(a)) continue;
                if (float.IsNaN(b)) b = a - 2f;
                worst = Math.Max(worst, a - b);
            }
        return worst;
    }

    /// <summary>
    /// The slice's BuildTool.ClearGround as it was: the block's volume plus 0.1 m, sampled every
    /// 0.5 m, each sample floored to a cell, every solid cell dug. Returns how many cells it cut
    /// whose centres lie outside the block.
    /// </summary>
    internal static int LegacyClear(ChunkStore store, IEnumerable<StructureVolume> volumes)
    {
        int outside = 0;
        var all = volumes.ToList();
        foreach (StructureVolume v in all)
        {
            var cells = new HashSet<Int3>();
            NVector3 half = v.Half + new NVector3(0.1f);
            for (float z = -half.Z; z <= half.Z; z += 0.5f)
                for (float y = -half.Y; y <= half.Y; y += 0.5f)
                    for (float x = -half.X; x <= half.X; x += 0.5f)
                        cells.Add(Int3.Floor(v.Center + v.AxisX * x + v.AxisY * y + v.AxisZ * z));
            EditResult r = store.DigCells(cells.Where(store.IsSolid).OrderBy(c => c).ToList());
            outside += r.Removed.Count(c => !all.Any(o => o.ContainsCell(c.Cell)));
        }
        return outside;
    }

    static bool OnSurfaceOf(StructureVolume v, NVector3 p, float eps)
    {
        NVector3 l = v.Local(p);
        NVector3 h = v.Half;
        bool within = MathF.Abs(l.X) <= h.X + eps && MathF.Abs(l.Y) <= h.Y + eps && MathF.Abs(l.Z) <= h.Z + eps;
        bool onFace = MathF.Abs(MathF.Abs(l.X) - h.X) <= eps || MathF.Abs(MathF.Abs(l.Y) - h.Y) <= eps || MathF.Abs(MathF.Abs(l.Z) - h.Z) <= eps;
        return within && onFace;
    }

    static bool InsideBy(StructureVolume v, NVector3 p, float eps)
    {
        NVector3 l = v.Local(p);
        return MathF.Abs(l.X) < v.Half.X - eps && MathF.Abs(l.Y) < v.Half.Y - eps && MathF.Abs(l.Z) < v.Half.Z - eps;
    }

    /// <summary>Does the vertical line through (x, z) pass through the box (grown by a margin)?</summary>
    static bool VerticalHits(StructureVolume v, float x, float z, float margin)
    {
        // The line p(t) = (x, t, z): in the box frame it is l0 + t·d. Slab-test t over the three axes.
        NVector3 l0 = v.Local(new NVector3(x, 0f, z));
        var d = new NVector3(v.AxisX.Y, v.AxisY.Y, v.AxisZ.Y);
        float t0 = float.NegativeInfinity, t1 = float.PositiveInfinity;
        for (int k = 0; k < 3; k++)
        {
            float a = k == 0 ? l0.X : k == 1 ? l0.Y : l0.Z, dk = k == 0 ? d.X : k == 1 ? d.Y : d.Z;
            float h = (k == 0 ? v.Half.X : k == 1 ? v.Half.Y : v.Half.Z) + margin;
            if (MathF.Abs(dk) < 1e-9f)
            {
                if (MathF.Abs(a) > h) return false;
                continue;
            }
            float e0 = (-h - a) / dk, e1 = (h - a) / dk;
            if (e0 > e1) (e0, e1) = (e1, e0);
            t0 = MathF.Max(t0, e0);
            t1 = MathF.Min(t1, e1);
        }
        return t0 <= t1;
    }

    /// <summary>Points along the four edges of a block's lowest face, each with the outward direction there.</summary>
    static IEnumerable<(NVector3 point, NVector3 outward)> BottomEdgeSamples(StructureVolume v)
    {
        NVector3[] axes = { v.AxisX, v.AxisY, v.AxisZ };
        float[] half = { v.Half.X, v.Half.Y, v.Half.Z };
        int down = 0;
        float lowest = float.MaxValue;
        float sign = 1f;
        for (int k = 0; k < 3; k++)
            foreach (float s in new[] { 1f, -1f })
                if (axes[k].Y * s < lowest)
                {
                    lowest = axes[k].Y * s;
                    down = k;
                    sign = s;
                }
        NVector3 face = v.Center + axes[down] * (half[down] * sign);
        int u = (down + 1) % 3, w = (down + 2) % 3;
        foreach ((int a, int b) in new[] { (u, w), (w, u) })
            foreach (float s in new[] { 1f, -1f })
                for (int i = 0; i <= 8; i++)
                {
                    float t = -0.9f + 1.8f * i / 8f;
                    NVector3 p = face + axes[a] * (half[a] * s) + axes[b] * (half[b] * t);
                    NVector3 outward = axes[a] * s;
                    outward.Y = 0f;
                    if (outward.LengthSquared() < 1e-6f) continue;
                    yield return (p, NVector3.Normalize(outward));
                }
    }

    /// <summary>A block straddling the corner where eight chunks meet: the cut agrees across every seam.</summary>
    void TestFlushSeams()
    {
        var store = NewStore();
        const int X = 16, Z = 16;
        float h = store.Source.SurfaceHeight(X, Z);
        var big = new StructureVolume(new NVector3(X + 0.13f, h, Z - 0.27f), NQuaternion.CreateFromYawPitchRoll(30f * Degrees, 4f * Degrees, 0f), new NVector3(2f));
        var small = new StructureVolume(new NVector3(X + 2.9f, store.Source.SurfaceHeight(X + 2.9f, Z + 0.1f), Z + 0.1f), NQuaternion.CreateFromYawPitchRoll(-21f * Degrees, 0f, 0f), new NVector3(0.5f));
        var flat = new StructureVolume(new NVector3(X - 2.5f, store.Source.SurfaceHeight(X - 2.5f, Z + 2f), Z + 2f), NQuaternion.Identity, new NVector3(2f, 1f, 2f));
        var volumes = new List<StructureVolume> { big, small, flat };
        store.ClearForStructure(volumes.Select((v, i) => (new VolumeKey(3, i, 0, 0), v)));

        Int3 chunk = ChunkStore.ChunkOf(new Int3(X, (int)MathF.Floor(h), Z));
        var meshes = new List<ChunkMesh>();
        var mesher = new SurfaceNetsMesher();
        for (int y = -1; y <= 0; y++)
            for (int z = -1; z <= 0; z++)
                for (int x = -1; x <= 0; x++)
                    meshes.Add(mesher.Build(store.CaptureNeighborhood(chunk + new Int3(x, y, z))));
        // Above and below too, in case the surface crosses a vertical seam here.
        for (int z = -1; z <= 0; z++)
            for (int x = -1; x <= 0; x++)
            {
                meshes.Add(mesher.Build(store.CaptureNeighborhood(chunk + new Int3(x, 1, z))));
                meshes.Add(mesher.Build(store.CaptureNeighborhood(chunk + new Int3(x, -2, z))));
            }

        var edges = new Dictionary<(NVector3, NVector3), int>();
        int triangles = 0, inward = 0, faces = 0;
        foreach (ChunkMesh m in meshes)
            for (int t = 0; t < m.Indices.Length; t += 3)
            {
                triangles++;
                for (int e = 0; e < 3; e++)
                {
                    NVector3 p = m.Positions[m.Indices[t + e]], q = m.Positions[m.Indices[t + (e + 1) % 3]];
                    var key = Less(p, q) ? (p, q) : (q, p);
                    edges[key] = edges.GetValueOrDefault(key) + 1;
                }
                NVector3 a = m.Positions[m.Indices[t]], b = m.Positions[m.Indices[t + 1]], c = m.Positions[m.Indices[t + 2]];
                NVector3 n = m.Normals[m.Indices[t]] + m.Normals[m.Indices[t + 1]] + m.Normals[m.Indices[t + 2]];
                NVector3 cross = NVector3.Cross(b - a, c - a);
                if (cross.LengthSquared() < 1e-10f) continue;
                faces++;
                if (NVector3.Dot(cross, n) > 0f) inward++;
            }
        NVector3 lo = ChunkStore.ChunkOrigin(chunk - Int3.One).ToVector() + NVector3.One * 1.5f;
        NVector3 hi = ChunkStore.ChunkOrigin(chunk + Int3.One).ToVector() - NVector3.One * 1.5f;
        bool Inside(NVector3 v) => v.X > lo.X && v.Z > lo.Z && v.X < hi.X && v.Z < hi.Z;
        int interior = 0, open = 0, onFaces = 0;
        foreach (((NVector3 p, NVector3 q), int count) in edges)
        {
            if (!Inside(p) || !Inside(q)) continue;
            interior++;
            if (count != 1) continue;
            if (volumes.Any(v => OnSurfaceOf(v, p, OnFace) && OnSurfaceOf(v, q, OnFace))) onFaces++;
            else open++;
        }
        Check(interior > 0 && open == 0 && onFaces > 0,
            $"flush seams: three blocks across an 8-chunk corner, {interior:N0} interior edges of {triangles:N0} triangles: every open edge " +
            $"({onFaces}) runs along a block face, none elsewhere (no cracks, no T-junctions across seams)");
        Check(inward * 100 < faces, $"cut triangles keep their winding ({faces - inward:N0} of {faces:N0} outward)");
    }

    /// <summary>Taking a block down: what it cleared stays cleared, drawn as the plain dug hole it is.</summary>
    void TestFlushRemoval()
    {
        var reference = NewStore();
        if (_flat == default) FindContactSpots(reference);
        int ok = 0, total = 0;
        foreach (ContactCase c in new[]
                 {
                     new ContactCase("2 m at 17° on the slope", _slope, new NVector3(2f), 17f),
                     new ContactCase("0.5 m at 33° on flat ground", _flat, new NVector3(0.5f), 33f),
                     new ContactCase("row of three at 20° on the slope", _slope, new NVector3(2f), 20f, Run: 3),
                 })
        {
            total++;
            var before = new ChunkStore(reference.Base);
            CellBox region = RegionAround(c.At, 6, before);
            List<(VolumeKey key, StructureVolume volume)> parts = VolumesFor(c, Surface.Of(before, region));
            var store = new ChunkStore(reference.Base);
            EditResult cleared = store.ClearForStructure(parts);
            int removed = store.RemovedCount;
            // Take the middle block of a row down first: its neighbours still stand.
            var dirty = new HashSet<Int3>();
            foreach ((VolumeKey key, _) in parts.OrderBy(p => Math.Abs(p.key.X - 1))) dirty.UnionWith(store.RemoveVolume(key));

            // The same cells dug with no structure at all: the mesh must be exactly that.
            var dug = new ChunkStore(reference.Base);
            dug.DigCells(cleared.Removed.Select(r => r.Cell));
            var mesher = new SurfaceNetsMesher();
            int differ = 0, chunks = 0;
            foreach (Int3 chunk in dirty)
            {
                chunks++;
                if (mesher.Build(store.CaptureNeighborhood(chunk)).ComputeHash() != mesher.Build(dug.CaptureNeighborhood(chunk)).ComputeHash()) differ++;
            }
            bool stillAir = cleared.Removed.All(r => !store.IsSolid(r.Cell)) && store.RemovedCount == removed && store.Volumes.Count == 0;
            bool closed = OpenEdges(store, region) == 0;
            int floating = FloatingCells(store, region) - FloatingCells(before, region);
            if (stillAir && differ == 0 && closed && floating <= 0) ok++;
            else Fail($"removal: {c.Name}: cells still air {stillAir}, {differ} of {chunks} chunks differ from the plain dug hole, closed {closed}, {floating} floating cells");
        }
        Check(ok == total, $"taking blocks down ({total} cases): the cells they cleared stay cleared (remove-only), the ground is drawn as the plain dug hole, " +
                           "closed, with nothing left floating");
    }

    /// <summary>Open edges of the mesh in a region's middle (the surface is closed everywhere without blocks).</summary>
    static int OpenEdges(ChunkStore store, CellBox region)
    {
        var mesher = new SurfaceNetsMesher();
        var edges = new Dictionary<(NVector3, NVector3), int>();
        Int3 lo = ChunkStore.ChunkOf(region.Min), hi = ChunkStore.ChunkOf(region.Max - Int3.One);
        for (int y = lo.Y - 1; y <= hi.Y + 1; y++)
            for (int z = lo.Z - 1; z <= hi.Z + 1; z++)
                for (int x = lo.X - 1; x <= hi.X + 1; x++)
                {
                    ChunkMesh m = mesher.Build(store.CaptureNeighborhood(new Int3(x, y, z)));
                    for (int t = 0; t < m.Indices.Length; t += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            NVector3 p = m.Positions[m.Indices[t + e]], q = m.Positions[m.Indices[t + (e + 1) % 3]];
                            var key = Less(p, q) ? (p, q) : (q, p);
                            edges[key] = edges.GetValueOrDefault(key) + 1;
                        }
                }
        NVector3 a = region.Min.ToVector(), b = region.Max.ToVector();
        bool In(NVector3 v) => v.X > a.X && v.Y > a.Y && v.Z > a.Z && v.X < b.X && v.Y < b.Y && v.Z < b.Z;
        return edges.Count(kv => kv.Value == 1 && In(kv.Key.Item1) && In(kv.Key.Item2));
    }

    /// <summary>Solid cells in a region not joined to its bottom layer (cell faces, six ways): floating terrain.</summary>
    static int FloatingCells(ChunkStore store, CellBox region)
    {
        var reached = new HashSet<Int3>();
        var queue = new Queue<Int3>();
        for (int z = region.Min.Z; z < region.Max.Z; z++)
            for (int x = region.Min.X; x < region.Max.X; x++)
            {
                var c = new Int3(x, region.Min.Y, z);
                if (store.IsSolid(c) && reached.Add(c)) queue.Enqueue(c);
            }
        Int3[] steps = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };
        while (queue.Count > 0)
        {
            Int3 c = queue.Dequeue();
            foreach (Int3 s in steps)
            {
                Int3 n = c + s;
                if (region.Contains(n) && store.IsSolid(n) && reached.Add(n)) queue.Enqueue(n);
            }
        }
        return store.CountSolid(region) - reached.Count;
    }

    /// <summary>Same seed, same blocks, in any order: the same meshes. Saves and loads keep them.</summary>
    void TestFlushDeterminism()
    {
        if (_flat == default) FindContactSpots(NewStore());
        var probe = NewStore();
        CellBox region = RegionAround(_slope, 7, probe);
        Surface ground = Surface.Of(probe, region);
        List<(VolumeKey key, StructureVolume volume)> parts = VolumesFor(new ContactCase("row", _slope, new NVector3(2f), 20f, Run: 3), ground);
        parts.AddRange(VolumesFor(new ContactCase("small", _slope + new NVector2(-2.4f, 2.6f), new NVector3(0.5f), 33f, 8f, -5f), ground)
            .Select(p => (key: new VolumeKey(8, 0, 0, 0), volume: p.volume)));

        // Two separate worlds (their own generators and caches), the blocks placed in opposite orders.
        var a = NewStore();
        var b = NewStore();
        foreach (var p in parts) a.ClearForStructure(p.key, p.volume);
        foreach (var p in Enumerable.Reverse(parts)) b.ClearForStructure(p.key, p.volume);
        var chunks = new HashSet<Int3>();
        foreach (var p in parts) chunks.UnionWith(ChunkStore.ChunksNear(p.volume));
        var mesher = new SurfaceNetsMesher();
        int differ = 0;
        var hashes = new Dictionary<Int3, ulong>();
        foreach (Int3 c in chunks)
        {
            ulong ha = mesher.Build(a.CaptureNeighborhood(c)).ComputeHash();
            hashes[c] = ha;
            if (ha != mesher.Build(b.CaptureNeighborhood(c)).ComputeHash()) differ++;
        }
        Check(differ == 0 && a.EditStateHash() == b.EditStateHash(),
            $"same seed, same blocks in either order, two separate worlds: all {chunks.Count} chunk meshes identical to the bit");

        // A save and a load: the store's own blocks come back with their volumes; a caller's
        // volumes (a base the terrain doesn't save) stay registered across the load.
        string path = Path.Combine(_saveDir, "flush.ghs");
        PlaceResult block = a.PlaceStructure(CellBox.FromSize(SurfaceCell(a, (int)_flat.X, (int)_flat.Y), new Int3(2, 2, 2)));
        var blockChunks = new HashSet<Int3>(ChunkStore.ChunksNear(StructureVolume.Box(block.Structure.Box)));
        foreach (Int3 c in blockChunks) hashes[c] = mesher.Build(a.CaptureNeighborhood(c)).ComputeHash();
        SaveReport saved = TerrainSave.Save(a, path, 1);
        a.DigCells(ChunkStore.SphereCells(SurfaceCell(a, 40, -30), 2f)); // something to undo
        LoadResult loaded = TerrainSave.Load(path, a.Base);
        HashSet<Int3> dirty = loaded.Ok ? a.ReplaceStateWith(loaded.Store) : new HashSet<Int3>();
        int after = 0;
        foreach ((Int3 c, ulong h) in hashes)
            if (mesher.Build(a.CaptureNeighborhood(c)).ComputeHash() != h) after++;
        bool volumesBack = a.Volumes.ContainsKey(VolumeKey.ForBlock(block.Structure.Id)) && parts.All(p => a.Volumes.ContainsKey(p.key));
        Check(saved.Ok && loaded.Ok && after == 0 && volumesBack && dirty.Count > 0,
            $"save and load: the blocks' meshes come back identical ({hashes.Count} chunks), the saved block re-registers its volume, a caller's volumes stay");
    }

    /// <summary>The same at planet scale: kilometres from the origin, where a float step is millimetres.</summary>
    void TestFlushAtPlanetScale()
    {
        CellBox oldBounds = ChunkStore.WorldChunks;
        try
        {
            ChunkStore.SetWorldBounds(new CellBox(new Int3(-8, -64, -8), new Int3(2008, 160, 2008)));
            var reference = NewStore();
            var at = new NVector2(20031.4f, 18877.7f);
            float worst = 0f;
            int bad = 0, cases = 0;
            foreach (ContactCase c in new[]
                     {
                         new ContactCase("2 m at 23° at 20 km", at, new NVector3(2f), 23f),
                         new ContactCase("0.5 m at 51° at 20 km", at + new NVector2(5f, 3f), new NVector3(0.5f), 51f),
                         new ContactCase("row of three at 8° at 20 km", at + new NVector2(-6f, 4f), new NVector3(2f), 8f, Run: 3),
                     })
            {
                cases++;
                var before = new ChunkStore(reference.Base);
                CellBox region = RegionAround(c.At, 6, before);
                Surface ground = Surface.Of(before, region);
                var parts = VolumesFor(c, ground);
                var after = new ChunkStore(reference.Base);
                after.ClearForStructure(parts);
                Surface cut = Surface.Of(after, region);
                var volumes = parts.Select(p => p.volume).ToList();
                // At 20 km a float step is 2 mm: the cut points sit on the face to within a centimetre.
                int moved = cut.Vertices.Count(p => !ground.HasVertex(p) && !volumes.Any(v => OnSurfaceOf(v, p, 0.01f)));
                int inside = Enumerable.Range(0, cut.Triangles).Count(t => volumes.Any(v => InsideBy(v, cut.Centroid(t), 0.01f)));
                double sag = SagOf(ground, cut, volumes, region);
                worst = MathF.Max(worst, (float)sag);
                if (moved > 0 || inside > 0 || sag > 0.002) bad++;
            }
            Check(bad == 0, $"at planet scale ({cases} blocks 20 km out): no vertex moved off the old surface or a face, nothing inside a block, worst sag {worst * 1000:F2} mm");
        }
        finally
        {
            ChunkStore.SetWorldBounds(oldBounds);
        }
    }

    /// <summary>What the cut costs: a base footprint of 30 blocks over a few chunks, meshed with and without it.</summary>
    void TestClipTiming()
    {
        if (_flat == default) FindContactSpots(NewStore());
        var store = NewStore();
        var plain = new ChunkStore(store.Base);
        Surface ground = Surface.Of(store, RegionAround(_slope, 10, store));
        NQuaternion q = NQuaternion.CreateFromYawPitchRoll(20f * Degrees, 0f, 0f);
        NVector3 ax = NVector3.Transform(NVector3.UnitX, q) * 2f, az = NVector3.Transform(NVector3.UnitZ, q) * 2f;
        float y = ground.Down(_slope.X, _slope.Y, 500f);
        var parts = new List<(VolumeKey, StructureVolume)>();
        for (int i = 0; i < 6; i++)
            for (int k = 0; k < 5; k++)
            {
                NVector3 c = new NVector3(_slope.X, y, _slope.Y) + ax * (i - 2.5f) + az * (k - 2f);
                parts.Add((new VolumeKey(9, i, 0, k), new StructureVolume(c, q, new NVector3(2f))));
            }
        store.ClearForStructure(parts);
        plain.DigCells(parts.SelectMany(p => p.Item2.CellsInside()));
        var chunks = new HashSet<Int3>(parts.SelectMany(p => ChunkStore.ChunksNear(p.Item2)));
        var mesher = new SurfaceNetsMesher();
        foreach (Int3 c in chunks) mesher.Build(store.CaptureNeighborhood(c)); // warm up
        var with = new Stats();
        var without = new Stats();
        for (int pass = 0; pass < 3; pass++)
            foreach (Int3 c in chunks)
            {
                long t0 = Stopwatch.GetTimestamp();
                mesher.Build(store.CaptureNeighborhood(c));
                with.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                t0 = Stopwatch.GetTimestamp();
                mesher.Build(plain.CaptureNeighborhood(c));
                without.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            }
        Info($"remesh with a 30-block base cut in: avg {with.Average:F3} ms, max {with.Max:F3} ms per chunk ({chunks.Count} chunks); " +
             $"the same holes without it avg {without.Average:F3} ms");
        Check(with.Average < 5.0, $"cutting the ground at block faces keeps a remesh well under 5 ms ({with.Average:F3} ms)");
    }

    /// <summary>
    /// In the running scene, with the real collision: a turned block set into the slope leaves the
    /// ground's collision exactly where it was outside it and none inside it; then the ground is
    /// tunnelled out from under it with a player standing on it, and neither moves.
    /// </summary>
    async System.Threading.Tasks.Task TestFlushInScene()
    {
        if (_slope == default) FindContactSpots(_main.Store);
        ChunkStore store = _main.Store;
        var probe = new ChunkStore(store.Base);
        Surface ground = Surface.Of(probe, RegionAround(_slope, 6, probe));
        var c = new ContactCase("scene", _slope + new NVector2(1.3f, -0.6f), new NVector3(2f), 28f);
        StructureVolume v = VolumesFor(c, ground)[0].volume;

        var outside = BottomEdgeSamples(v).Select(s => s.point + s.outward * 0.03f).ToList();
        var inside = new List<NVector3>();
        for (float a = -0.85f; a <= 0.85f; a += 0.17f)
            for (float b = -0.85f; b <= 0.85f; b += 0.17f)
                inside.Add(v.Center + v.AxisX * a + v.AxisZ * b);
        await NextPhysics();
        var before = outside.Select(p => TerrainHit(p)).ToList();

        var key = new VolumeKey(99, 0, 0, 0);
        _main.PlaceOriented(key, v, Palette.Block);
        while (!_main.View.IsIdle) await NextFrame();
        for (int i = 0; i < 3; i++) await NextPhysics();
        var after = outside.Select(p => TerrainHit(p)).ToList();
        int moved = 0, poking = 0;
        for (int i = 0; i < outside.Count; i++)
            if (float.IsNaN(after[i]) || MathF.Abs(after[i] - before[i]) > 1e-3f) moved++;
        float bottom = v.Center.Y - v.Half.Y;
        foreach (NVector3 p in inside)
        {
            float hit = TerrainHit(p);
            if (!float.IsNaN(hit) && hit > bottom + 0.01f) poking++;
        }
        Check(moved == 0 && poking == 0,
            $"in the scene, a block turned 28° into the slope: the ground's collision 3 cm outside its edges is where it was ({outside.Count} rays), " +
            $"and there is none inside it above its base ({inside.Count} rays, {poking} hit)");

        // Stand on it, then tunnel right under it: the block stays put, and so does the player on it.
        StaticBody3D body = _main.Structures.GetChildren().OfType<StaticBody3D>().Last();
        Transform3D pose = body.GlobalTransform;
        Player player = _main.Player;
        player.Teleport(new Vector3(v.Center.X, v.Center.Y + v.Half.Y + 0.3f, v.Center.Z));
        for (int i = 0; i < 40; i++) await NextPhysics();
        Vector3 standing = player.GlobalPosition;
        var tunnel = new List<Int3>();
        Int3 under = Int3.Floor(v.Center - new NVector3(0f, v.Half.Y + 1.5f, 0f));
        for (int dx = -3; dx <= 3; dx++)
            for (int dy = -1; dy <= 0; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    tunnel.Add(under + new Int3(dx, dy, dz));
        EditResult dug = store.DigCells(tunnel);
        _main.View.MarkDirty(dug.DirtyChunks);
        while (!_main.View.IsIdle) await NextFrame();
        for (int i = 0; i < 60; i++) await NextPhysics();
        Check(dug.Removed.Count > 0 && body.GlobalTransform.IsEqualApprox(pose) && store.Volumes.ContainsKey(key) &&
              player.GlobalPosition.DistanceTo(standing) < 0.05f,
            $"a tunnel dug under it ({dug.Removed.Count} cells) with the player standing on it: the block hasn't moved and neither has the player");
        _main.RemoveOriented(key);
        while (!_main.View.IsIdle) await NextFrame();
        player.Teleport(new Vector3(v.Center.X + 6f, store.Source.SurfaceHeight(v.Center.X + 6f, v.Center.Z) + 1f, v.Center.Z));
        for (int i = 0; i < 20; i++) await NextPhysics();
    }

    /// <summary>Where a ray straight down onto the terrain's collision (not blocks) hits, or NaN.</summary>
    float TerrainHit(NVector3 p)
    {
        var from = new Vector3(p.X, p.Y + 12f, p.Z);
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 40f, Layers.Terrain);
        var hit = _main.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? ((Vector3)hit["position"]).Y : float.NaN;
    }

    /// <summary>
    /// The terrain surface around a region as world triangles, with an xz bucket grid for rays
    /// straight down and nearest-point queries.
    /// </summary>
    sealed class Surface
    {
        readonly List<NVector3> _p = new();
        readonly HashSet<(int, int, int)> _vertices = new();
        public readonly List<NVector3> Vertices = new();
        readonly Dictionary<(int, int), List<int>> _grid = new();

        public int Triangles => _p.Count / 3;

        public static Surface Of(ChunkStore store, CellBox region)
        {
            var s = new Surface();
            var mesher = new SurfaceNetsMesher();
            Int3 lo = ChunkStore.ChunkOf(region.Min), hi = ChunkStore.ChunkOf(region.Max - Int3.One);
            for (int y = lo.Y; y <= hi.Y; y++)
                for (int z = lo.Z; z <= hi.Z; z++)
                    for (int x = lo.X; x <= hi.X; x++)
                    {
                        ChunkMesh m = mesher.Build(store.CaptureNeighborhood(new Int3(x, y, z)));
                        var used = new HashSet<int>(m.Indices);
                        foreach (int i in used)
                        {
                            NVector3 v = m.Positions[i];
                            if (s._vertices.Add(Bits(v))) s.Vertices.Add(v);
                        }
                        for (int t = 0; t < m.Indices.Length; t += 3) s.Add(m.Positions[m.Indices[t]], m.Positions[m.Indices[t + 1]], m.Positions[m.Indices[t + 2]]);
                    }
            return s;
        }

        static (int, int, int) Bits(NVector3 v) => (BitConverter.SingleToInt32Bits(v.X), BitConverter.SingleToInt32Bits(v.Y), BitConverter.SingleToInt32Bits(v.Z));

        public bool HasVertex(NVector3 v) => _vertices.Contains(Bits(v));

        void Add(NVector3 a, NVector3 b, NVector3 c)
        {
            int t = _p.Count / 3;
            _p.Add(a);
            _p.Add(b);
            _p.Add(c);
            NVector3 lo = NVector3.Min(a, NVector3.Min(b, c)), hi = NVector3.Max(a, NVector3.Max(b, c));
            for (int x = (int)MathF.Floor(lo.X); x <= (int)MathF.Floor(hi.X); x++)
                for (int z = (int)MathF.Floor(lo.Z); z <= (int)MathF.Floor(hi.Z); z++)
                {
                    if (!_grid.TryGetValue((x, z), out List<int> list)) _grid[(x, z)] = list = new List<int>();
                    list.Add(t);
                }
        }

        public NVector3 Centroid(int t) => (_p[t * 3] + _p[t * 3 + 1] + _p[t * 3 + 2]) / 3f;
        public NVector3 Corner(int t, int i) => _p[t * 3 + i];

        public IEnumerable<int> TrianglesAt(float x, float z)
        {
            if (!_grid.TryGetValue(((int)MathF.Floor(x), (int)MathF.Floor(z)), out List<int> list)) yield break;
            foreach (int t in list)
                if (HeightIn(t, x, z, out _)) yield return t;
        }

        /// <summary>Heights where the vertical line through (x, z) meets the surface.</summary>
        public IEnumerable<float> AllDown(float x, float z)
        {
            if (!_grid.TryGetValue(((int)MathF.Floor(x), (int)MathF.Floor(z)), out List<int> list)) yield break;
            foreach (int t in list)
                if (HeightIn(t, x, z, out float y)) yield return y;
        }

        /// <summary>The highest point of the surface straight down from (x, from, z), or NaN.</summary>
        public float Down(float x, float z, float from)
        {
            float best = float.NaN;
            foreach (float y in AllDown(x, z))
                if (y <= from && (float.IsNaN(best) || y > best)) best = y;
            return best;
        }

        bool HeightIn(int t, float x, float z, out float y)
        {
            NVector3 a = _p[t * 3], b = _p[t * 3 + 1], c = _p[t * 3 + 2];
            float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
            y = 0f;
            if (MathF.Abs(d) < 1e-12f) return false;
            float wa = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / d;
            float wb = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / d;
            float wc = 1f - wa - wb;
            const float E = -1e-6f;
            if (wa < E || wb < E || wc < E) return false;
            y = wa * a.Y + wb * b.Y + wc * c.Y;
            return true;
        }

        /// <summary>Distance from a point to the nearest triangle near it.</summary>
        public float DistanceTo(NVector3 p)
        {
            float best = float.MaxValue;
            for (int x = (int)MathF.Floor(p.X) - 1; x <= (int)MathF.Floor(p.X) + 1; x++)
                for (int z = (int)MathF.Floor(p.Z) - 1; z <= (int)MathF.Floor(p.Z) + 1; z++)
                {
                    if (!_grid.TryGetValue((x, z), out List<int> list)) continue;
                    foreach (int t in list) best = MathF.Min(best, PointTriangle(p, _p[t * 3], _p[t * 3 + 1], _p[t * 3 + 2]));
                }
            return best;
        }

        static float PointTriangle(NVector3 p, NVector3 a, NVector3 b, NVector3 c)
        {
            // Ericson, Real-Time Collision Detection 5.1.5: the closest point by Voronoi regions.
            NVector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = NVector3.Dot(ab, ap), d2 = NVector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return NVector3.Distance(p, a);
            NVector3 bp = p - b;
            float d3 = NVector3.Dot(ab, bp), d4 = NVector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return NVector3.Distance(p, b);
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return NVector3.Distance(p, a + ab * (d1 / (d1 - d3)));
            NVector3 cp = p - c;
            float d5 = NVector3.Dot(ab, cp), d6 = NVector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return NVector3.Distance(p, c);
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return NVector3.Distance(p, a + ac * (d2 / (d2 - d6)));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return NVector3.Distance(p, b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))));
            float denom = 1f / (va + vb + vc);
            return NVector3.Distance(p, a + ab * (vb * denom) + ac * (vc * denom));
        }
    }
}
