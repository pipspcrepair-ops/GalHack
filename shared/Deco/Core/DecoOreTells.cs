using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GalHack.Deco;

/// <summary>
/// The surface tells of a buried ore body, matching shared/Items/Deposits TellKind: an outcrop where
/// the body breaks the surface, a fracture over a deep stringer, and scree where the deposit's
/// footprint once only stained the ground.
/// </summary>
public enum DecoTellKind { Outcrop, Fracture, Scree }

/// <summary>
/// Rock formations that hint at ore, never just a discoloured patch: weathered slabs, knuckles of
/// bedrock and boulder clusters with veins of the ore's colour wrapping through them (and a faint
/// glint on metallic and crystal ores); cracked slabs with a hairline of ore in the cracks; scree,
/// pebbles and mineral crust with a slight tint, and the odd stunted plant. Plain C#: give it the
/// ore's colour and glow (planet-generator's DepositLook), a seed and a size.
/// </summary>
public static class DecoOreTells
{
    static readonly DecoRgb DefaultRock = DecoRgb.Hex("6e665c");

    /// <summary>Above this size a formation is solid to walk against; smaller ones are only seen.</summary>
    public const float SolidSize = 0.8f;

    /// <summary>
    /// A tell's mesh, centred on its tell position, standing on y = 0 within <paramref name="radius"/>
    /// metres. <paramref name="glints"/> for metallic and crystal ores. <paramref name="shape"/> picks
    /// an outcrop's form (slabs, a bedrock knuckle or a boulder cluster); -1 lets the seed choose.
    /// <paramref name="growth"/> is the colour of stunted growth on scree: none without one.
    /// </summary>
    public static DecoMeshData Build(DecoTellKind kind, DecoRgb ore, float glow, bool glints, ulong seed, float radius, DecoRgb? rock = null, int shape = -1, DecoRgb? growth = null)
    {
        var rng = new DecoRng(DecoHash.Combine(seed, (ulong)kind * 977UL + 13));
        DecoRgb stone = rock ?? DefaultRock;
        var parts = new List<(DecoPart part, bool solid)>();
        var b = new DecoBuilder(radius * 2, radius * 2, radius * 2, seed);
        var veins = new List<(Vector3 centre, Vector3 normal, float reach, float width)>();
        var hairlines = new List<DecoPart>();
        var crust = new List<DecoPart>();
        var plants = new List<DecoPart>();
        DecoRgb tinted = stone.Lerp(ore, 0.12f);

        DecoPart Boulder(Vector3 c, Vector3 size, float yaw, float jitter)
        {
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                var u = new Vector3((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f);
                bool top = (i & 2) != 0;
                u.X *= top ? rng.Range(0.55f, 0.85f) : 1f;
                u.Z *= top ? rng.Range(0.55f, 0.85f) : 1f;
                u += new Vector3(rng.Range(-jitter, jitter), top ? rng.Range(-jitter, 0) : 0, rng.Range(-jitter, jitter));
                corners[i] = Vector3.Transform(u * size, DecoGeo.RotY(yaw)) + c + new Vector3(0, size.Y / 2, 0);
            }
            return b.Hull(corners, DecoSlot.Stone);
        }

        switch (kind)
        {
            case DecoTellKind.Outcrop:
            {
                int form = shape >= 0 ? shape % 3 : rng.Int(3);
                float s = radius;
                if (form == 0)
                {
                    // Jutting slabs, tipped up out of the ground, leaning together.
                    int n = 2 + rng.Int(3);
                    for (int i = 0; i < n; i++)
                    {
                        float x = rng.Range(-0.4f, 0.4f) * s, z = (i - (n - 1) / 2f) * s * 0.45f;
                        float h = s * rng.Range(0.7f, 1.3f), w = s * rng.Range(0.45f, 0.7f), t = s * rng.Range(0.18f, 0.3f);
                        float lean = rng.Range(-28, 28), yaw = rng.Range(-20, 20);
                        using (b.Turn(new Vector3(x, 0, z), 0, yaw, 0))
                        using (b.Turn(new Vector3(x, 0, z), lean * 0.4f, 0, lean))
                            parts.Add((Boulder(new Vector3(x, -h * 0.08f, z), new Vector3(w, h, t), 0, 0.08f), true));
                        veins.Add((new Vector3(x, h * 0.45f, z), Vector3.Normalize(new Vector3(rng.Range(-0.3f, 0.3f), 1, rng.Range(-0.5f, 0.5f))), s * 1.4f, s * 0.035f + 0.02f));
                    }
                }
                else if (form == 1)
                {
                    // A knuckle of bedrock: one broad mass with lumps, a vein through its shoulder.
                    parts.Add((Boulder(new Vector3(0, -s * 0.1f, 0), new Vector3(s * 1.7f, s * 0.75f, s * 1.3f), rng.Range(0, 90), 0.12f), true));
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * MathF.Tau / 3 + rng.Range(0, 1f);
                        parts.Add((Boulder(new Vector3(MathF.Cos(a) * s * 0.5f, s * 0.3f, MathF.Sin(a) * s * 0.4f), new Vector3(s * 0.6f, s * 0.45f, s * 0.5f), rng.Range(0, 90), 0.15f), true));
                    }
                    veins.Add((new Vector3(0, s * 0.35f, 0), Vector3.Normalize(new Vector3(0.5f, 1, 0.2f)), s * 1.6f, s * 0.04f + 0.03f));
                    veins.Add((new Vector3(s * 0.2f, s * 0.25f, 0), Vector3.Normalize(new Vector3(-0.3f, 0.8f, 0.9f)), s * 1.6f, s * 0.025f + 0.02f));
                }
                else
                {
                    // A cluster of angular boulders.
                    int n = 4 + rng.Int(4);
                    for (int i = 0; i < n; i++)
                    {
                        float a = rng.Range(0, MathF.Tau), d = rng.Range(0, 0.65f) * s;
                        float size = s * rng.Range(0.3f, 0.65f) * (i == 0 ? 1.4f : 1f);
                        var c = new Vector3(MathF.Cos(a) * d, -size * 0.1f, MathF.Sin(a) * d);
                        parts.Add((Boulder(c, new Vector3(size, size * rng.Range(0.6f, 1.0f), size * rng.Range(0.7f, 1f)), rng.Range(0, 180), 0.15f), size >= SolidSize));
                        if (i < 4) veins.Add((c + new Vector3(0, size * 0.4f, 0), Vector3.Normalize(new Vector3(rng.Range(-1, 1f), 1.2f, rng.Range(-1, 1f))), size * 1.4f, size * 0.05f + 0.015f));
                    }
                }
                Pebbles(b, rng, crust, radius * 0.95f, 10, 0.03f, 0.09f, DecoSlot.Debris);
                break;
            }
            case DecoTellKind.Fracture:
            {
                // Split slabs with ore in the hairline cracks, and a small spill of rubble.
                float s = radius;
                int n = 3;
                float x0 = -s * 0.75f, w = s * 1.5f / n;
                for (int i = 0; i < n; i++)
                {
                    float x = x0 + w * (i + 0.5f), gap = s * 0.03f + 0.02f;
                    float h = s * rng.Range(0.12f, 0.22f);
                    using (b.Turn(new Vector3(x, 0, 0), rng.Range(-4, 4), rng.Range(-6, 6), rng.Range(-5, 5)))
                        parts.Add((Boulder(new Vector3(x, -h * 0.25f, 0), new Vector3(w - gap * 2, h, s * rng.Range(0.9f, 1.3f)), 0, 0.05f), s >= SolidSize));
                    if (i < n - 1)
                    {
                        float cx = x0 + w * (i + 1);
                        var crack = new DecoPart
                        {
                            Kind = DecoPrim.Hex, Corners = DecoPart.UnitCube, Slot = DecoSlot.Node,
                            M = Matrix4x4.CreateScale(gap * 0.6f, 0.02f, s * 1.1f) * DecoGeo.RotY(rng.Range(-6, 6)) * DecoGeo.Move(cx, 0.01f, 0),
                        };
                        hairlines.Add(crack);
                    }
                }
                for (int i = 0; i < 6; i++)
                {
                    float size = s * rng.Range(0.08f, 0.18f);
                    parts.Add((Boulder(new Vector3(s * 0.7f + rng.Range(0, s * 0.25f), 0, rng.Range(-s * 0.5f, s * 0.5f)), new Vector3(size, size * 0.7f, size), rng.Range(0, 180), 0.2f), false));
                }
                break;
            }
            default:
            {
                // Scree: gravel, pebbles and flakes of mineral crust, and a few stunted plants.
                Pebbles(b, rng, crust, radius, 30 + (int)(radius * 12), 0.02f, 0.08f, DecoSlot.Debris);
                for (int i = 0; i < 6 + (int)(radius * 3); i++)
                {
                    float a = rng.Range(0, MathF.Tau), d = MathF.Sqrt(rng.Float()) * radius * 0.9f, sz = rng.Range(0.08f, 0.22f);
                    var c = new Vector3(MathF.Cos(a) * d, 0, MathF.Sin(a) * d);
                    crust.Add(b.Frustum(0, c.X - sz, c.X + sz * 0.8f, c.Z - sz * 0.7f, c.Z + sz * 0.9f, 0.025f, c.X - sz * 0.8f, c.X + sz * 0.6f, c.Z - sz * 0.5f, c.Z + sz * 0.7f, DecoSlot.Node));
                }
                for (int i = 0; growth != null && i < 2 + rng.Int(3); i++)
                {
                    float a = rng.Range(0, MathF.Tau), d = rng.Range(0.2f, 0.85f) * radius;
                    var c = new Vector3(MathF.Cos(a) * d, 0, MathF.Sin(a) * d);
                    for (int k = 0; k < 4; k++)
                    {
                        float ka = k * MathF.PI / 2 + rng.Range(0, 0.6f), hgt = rng.Range(0.08f, 0.2f);
                        plants.Add(new DecoPart
                        {
                            Kind = DecoPrim.Cylinder, Segments = 4, Taper = 0f, Slot = DecoSlot.Leaf,
                            M = DecoGeo.Along(c, c + new Vector3(MathF.Cos(ka) * 0.05f, hgt, MathF.Sin(ka) * 0.05f), 0.025f, 0.025f),
                        });
                    }
                }
                break;
            }
        }

        // Emit: rock, then veins that wrap its surface, then glints, cracks, crust and plants.
        var mesh = new DecoMeshData();
        float veinGlow = Math.Clamp(glow, 0, 1);
        DecoRgb veinCol = ore.Lerp(stone, 0.6f);
        foreach (DecoPart p in b.Model.Parts.Where(p => !crust.Contains(p)))
            DecoEmit.Part(mesh, p, (at, n, _) => (crust.Contains(p) ? tinted : stone).Scale(0.82f + 0.3f * DecoHash.At(at, seed) + (n.Y > 0.6f ? 0.08f : 0)).Lerp(ore, 0.03f), _ => DecoSurface.Matte, v => v.Y < 0 ? v with { Y = 0 } : v);
        var tris = mesh.AllTriangles().ToList();
        DecoSurface veinSurface = veinGlow > 0.45f ? DecoSurface.Glow : DecoSurface.Matte;
        var glintAt = new List<Vector3>();
        foreach (var (c, normal, reach, width) in veins)
        {
            List<Vector3> path = WrapPath(tris, c, normal, reach);
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var strip = new DecoPart { Kind = DecoPrim.Cylinder, Segments = 4, Slot = DecoSlot.Node, M = DecoGeo.Along(path[i], path[i + 1], width * 0.8f, width * 0.35f) };
                DecoEmit.Part(mesh, strip, (at, n, _) => veinSurface == DecoSurface.Glow ? veinCol.Scale(0.45f + 0.4f * veinGlow) : veinCol.Scale(0.9f + 0.2f * DecoHash.At(at, seed + 3)), _ => veinSurface, v => v.Y < 0 ? v with { Y = 0 } : v);
                if (glints && rng.Chance(0.12f)) glintAt.Add(path[i]);
            }
        }
        foreach (Vector3 g in glintAt.Take(4))
        {
            float s = rng.Range(0.012f, 0.026f);
            var spark = new DecoPart { Kind = DecoPrim.Sphere, Segments = 4, Slot = DecoSlot.Node, M = Matrix4x4.CreateScale(s * 2) * DecoGeo.Move(g) };
            DecoEmit.Part(mesh, spark, (_, _, _) => veinCol.Lerp(new DecoRgb(1, 1, 1), 0.15f), _ => DecoSurface.Metal);
        }
        foreach (DecoPart crack in hairlines)
            DecoEmit.Part(mesh, crack, (_, _, _) => veinCol.Scale(0.35f + 0.35f * veinGlow), _ => veinGlow > 0.45f ? DecoSurface.Glow : DecoSurface.Matte);
        foreach (DecoPart p in crust)
            DecoEmit.Part(mesh, p, (at, n, _) => (p.Slot == DecoSlot.Node ? stone.Lerp(ore, 0.2f).Lerp(new DecoRgb(0.9f, 0.88f, 0.84f), 0.06f) : tinted).Scale(0.85f + 0.3f * DecoHash.At(at, seed + 5)),
                _ => p.Slot == DecoSlot.Node && glints ? DecoSurface.Metal : DecoSurface.Matte, v => v.Y < 0 ? v with { Y = 0 } : v);
        foreach (DecoPart p in plants)
            DecoEmit.Part(mesh, p, (at, _, _) => growth.Value.Scale(0.8f + 0.3f * DecoHash.At(at, seed + 9)), _ => DecoSurface.Leaf);
        mesh.RecomputeBounds();
        DecoEmit.Collision(mesh, parts.Where(x => x.solid).Select(x => x.part), 8);
        if (!parts.Any(x => x.solid)) mesh.Boxes.Clear();
        return mesh;
    }

    /// <summary>Small stones scattered in a disc, each a little low-poly lump on the ground.</summary>
    static void Pebbles(DecoBuilder b, DecoRng rng, List<DecoPart> into, float radius, int count, float min, float max, DecoSlot slot)
    {
        for (int i = 0; i < count; i++)
        {
            float a = rng.Range(0, MathF.Tau), d = MathF.Sqrt(rng.Float()) * radius;
            float s = rng.Range(min, max);
            into.Add(b.Dome(MathF.Cos(a) * d, 0, MathF.Sin(a) * d, s, s * rng.Range(0.4f, 0.8f), s * rng.Range(0.7f, 1.1f), slot, 6));
        }
    }

    /// <summary>
    /// A band around whatever the rays find: the plane through <paramref name="c"/> with
    /// <paramref name="normal"/> is swept by rays from outside towards c, and each hit, lifted a
    /// hair off the surface, is a point of the vein.
    /// </summary>
    static List<Vector3> WrapPath(List<(Vector3 A, Vector3 B, Vector3 C)> tris, Vector3 c, Vector3 normal, float reach)
    {
        Vector3 t1 = Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
        Vector3 t2 = Vector3.Cross(normal, t1);
        var path = new List<Vector3>();
        for (int i = 0; i <= 24; i++)
        {
            float a = i * MathF.Tau / 24;
            Vector3 dir = t1 * MathF.Cos(a) + t2 * MathF.Sin(a);
            Vector3 o = c + dir * reach;
            float best = float.MaxValue;
            Vector3 n = Vector3.Zero;
            foreach (var (A, B, C) in tris)
            {
                float d = DecoGeo.Ray(o, -dir, A, B, C);
                if (d > 0 && d < best) { best = d; n = Vector3.Normalize(Vector3.Cross(B - A, C - A)); }
            }
            if (best == float.MaxValue) { if (path.Count > 0) break; continue; }
            if (Vector3.Dot(n, dir) < 0) n = -n;
            Vector3 hit = o - dir * best + n * 0.012f;
            if (hit.Y < 0.01f) { if (path.Count > 1) break; continue; }
            path.Add(hit);
        }
        return path;
    }
}
