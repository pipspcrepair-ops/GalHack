using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Items;
using Godot;
using Slice.Places;
using NV3 = System.Numerics.Vector3;

namespace Slice.Tests;

/// <summary>
/// The owner's two from a farm's barn (8 Nov): one piece repeated in an even row across a hall, and the
/// small bits it laid hovering just off the floor. Over the fresh places rolled: no room holds more of
/// one piece than the planner's cap, nothing stands in an even line of four or more alike (same piece,
/// same turn, same spacing), and every small bit a piece lays near the floor rests on it or on the piece.
/// </summary>
public partial class DressTest
{
    void Repeats(Decor decor, List<Rolled> rolled)
    {
        var over = new List<string>();
        var lined = new List<string>();
        int rooms = 0;
        foreach (Rolled r in rolled)
            foreach (PlaceRoom room in r.Memory.Rooms.Values)
            {
                rooms++;
                HashSet<int> taken = r.Memory.DecoTaken.GetValueOrDefault(room.Module) ?? new HashSet<int>();
                var kept = room.Plan.Placements.Where((_, i) => !taken.Contains(i)).ToList();
                foreach (var g in kept.GroupBy(p => p.Piece))
                {
                    int cap = DecoPlanner.CapOf(decor.Catalogue.Piece(g.Key));
                    if (g.Count() > cap) over.Add($"{r.Name}, {room.Room.RoomId}: {g.Count()} x {g.Key} (cap {cap})");
                    if (Lined(g.ToList()) is int n && n >= 4) lined.Add($"{r.Name}, {room.Room.RoomId}: {n} x {g.Key} in an even line");
                }
            }
        foreach (string s in over.Take(4).Concat(lined.Take(6))) GD.Print("  e.g. " + s);
        Check(over.Count == 0, $"no room holds more of one piece than its cap ({over.Count} of {rooms} rooms do)");
        Check(lined.Count == 0, $"no piece repeats in an even line across a room, four or more alike in turn and spacing ({lined.Count} lines)");
    }

    /// <summary>The longest run of placements on one line, turned alike, evenly spaced (null for fewer than three).</summary>
    static int? Lined(List<DecoPlacement> ps)
    {
        if (ps.Count < 3) return null;
        int best = 0;
        for (int a = 0; a < ps.Count; a++)
            for (int b = a + 1; b < ps.Count; b++)
            {
                var pa = new System.Numerics.Vector2(ps[a].X, ps[a].Z);
                var d = new System.Numerics.Vector2(ps[b].X, ps[b].Z) - pa;
                if (d.Length() < 0.05f) continue;
                d = System.Numerics.Vector2.Normalize(d);
                var n = new System.Numerics.Vector2(-d.Y, d.X);
                var on = ps.Where(p => MathF.Abs(System.Numerics.Vector2.Dot(new System.Numerics.Vector2(p.X, p.Z) - pa, n)) < 0.05f && MathF.Abs(Turn(p.Yaw - ps[a].Yaw)) < 1.5f)
                    .Select(p => System.Numerics.Vector2.Dot(new System.Numerics.Vector2(p.X, p.Z) - pa, d)).OrderBy(t => t).ToList();
                // The longest run of equal gaps.
                for (int i = 0; i + 1 < on.Count; i++)
                {
                    float gap = on[i + 1] - on[i];
                    int run = 2;
                    for (int j = i + 2; j < on.Count && MathF.Abs(on[j] - on[j - 1] - gap) < 0.05f; j++) run++;
                    best = Math.Max(best, run);
                }
            }
        return best;
    }

    static float Turn(float deg) => ((deg % 360) + 540) % 360 - 180;

    /// <summary>
    /// Every look the rolled rooms use, meshed as the forge makes it: each separate bit of it near the floor
    /// (a chunk of debris, a shard, a fragment, a foot) lies on the floor or touches the rest of the piece.
    /// </summary>
    void Hovering(Decor decor, List<Rolled> rolled)
    {
        var looks = rolled.SelectMany(r => r.Memory.Rooms.Values).SelectMany(room => room.Plan.Placements).Select(p => p.Look).Distinct().ToList();
        var hovering = new List<string>();
        int bits = 0;
        foreach (DecoLook look in looks)
        {
            DecoPiece piece = decor.Catalogue.Piece(look.Piece);
            // Standing pieces: one knocked over lies as its fall settles it, faces and all.
            if (piece.Mount is not (DecoMount.Floor or DecoMount.Top) || look.Fall != DecoFall.None) continue;
            var comps = Components(decor.Forge.Mesh(look));
            // The piece's body seen from above: what stands taller than a bit. A bit inside it is the piece's own (a specimen in its tank).
            var body = comps.Where(c => c.Hi.Y - c.Lo.Y > 0.15f || c.Hi.X - c.Lo.X > 0.4f || c.Hi.Z - c.Lo.Z > 0.4f).ToList();
            for (int i = 0; i < comps.Count; i++)
            {
                var (lo, hi) = comps[i];
                // Near the floor and small: what the eye takes for a bit lying about. Flat marks lie on it by their lift.
                if (lo.Y > 0.15f || hi.X - lo.X > 0.4f || hi.Z - lo.Z > 0.4f) continue;
                bits++;
                if (lo.Y <= 0.004f || hi.Y - lo.Y < 0.003f && lo.Y < 0.012f) continue;
                if (body.Any(c => lo.X >= c.Lo.X - 0.01f && hi.X <= c.Hi.X + 0.01f && lo.Z >= c.Lo.Z - 0.01f && hi.Z <= c.Hi.Z + 0.01f && lo.Y >= c.Lo.Y - 0.01f && hi.Y <= c.Hi.Y + 0.01f)) continue;
                bool touches = false;
                for (int j = 0; j < comps.Count && !touches; j++)
                    // Touching, or a mark laid on its face (a glyph, a plate) a hair off it.
                    touches = j != i && comps[j].Lo.X <= hi.X + 0.012f && comps[j].Hi.X >= lo.X - 0.012f && comps[j].Lo.Y <= hi.Y + 0.012f && comps[j].Hi.Y >= lo.Y - 0.012f
                        && comps[j].Lo.Z <= hi.Z + 0.012f && comps[j].Hi.Z >= lo.Z - 0.012f;
                if (!touches) hovering.Add($"{look.Piece} {look.State}{(look.Fall != DecoFall.None ? "/" + look.Fall : "")}: a bit {(hi - lo).Length():F2} m across, {lo.Y * 100:F1} cm up");
            }
        }
        foreach (string s in hovering.Take(8)) GD.Print("  e.g. " + s);
        Check(hovering.Count == 0, $"every small bit a piece lays near the floor rests on it or on the piece ({looks.Count} looks, {bits} bits, {hovering.Count} hovering)");
    }

    /// <summary>The mesh's separate bits: triangles joined through shared corners, each with its box.</summary>
    static List<(NV3 Lo, NV3 Hi)> Components(DecoMeshData mesh)
    {
        var verts = new List<NV3>();
        var index = new Dictionary<(int, int, int), int>();
        var tris = new List<int>();
        foreach (DecoSurfaceData s in mesh.Surfaces)
            foreach (NV3 v in s.Positions)
            {
                var key = ((int)MathF.Round(v.X * 2000), (int)MathF.Round(v.Y * 2000), (int)MathF.Round(v.Z * 2000));
                if (!index.TryGetValue(key, out int k)) { k = verts.Count; index[key] = k; verts.Add(v); }
                tris.Add(k);
            }
        var parent = Enumerable.Range(0, verts.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        for (int t = 0; t + 2 < tris.Count; t += 3)
        {
            int a = Find(tris[t]);
            parent[Find(tris[t + 1])] = a;
            parent[Find(tris[t + 2])] = a;
        }
        var boxes = new Dictionary<int, (NV3 Lo, NV3 Hi)>();
        for (int i = 0; i < verts.Count; i++)
        {
            int root = Find(i);
            boxes[root] = boxes.TryGetValue(root, out var b) ? (NV3.Min(b.Lo, verts[i]), NV3.Max(b.Hi, verts[i])) : (verts[i], verts[i]);
        }
        return boxes.Values.ToList();
    }
}
