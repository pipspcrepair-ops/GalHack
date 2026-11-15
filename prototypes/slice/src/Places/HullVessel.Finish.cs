using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Structures;
using PoiGenerator.Gen;
using Block = GalHack.Structures.Block;
using BlockType = GalHack.Structures.BlockType;
using Dirs = GalHack.Structures.Dirs;
using DecoTech = GalHack.Items.DecoTech;
using DecoPiece = GalHack.Items.DecoPiece;
using DecoMount = GalHack.Items.DecoMount;
using NV3 = System.Numerics.Vector3;
using Int3 = GalHack.Structures.Int3;
using Plan = PoiGenerator.Gen.CapitalHull.HullPlan;
using Dir = GalHack.Structures.Dir;

namespace Slice.Places;

/// <summary>Each room's own finish, what the crash and the years did, the solid nobody reaches, and the dressing.</summary>
public sealed partial class HullVessel
{
    /// <summary>Every face looking into a space takes the space's finish: its floor's paint underfoot, its walls' on the rest.</summary>
    void Finish()
    {
        foreach (Space s in Out.Spaces)
        {
            var (wall, floor) = FinishOf(s);
            foreach (var (u, v) in s.Floor)
            {
                if (s.Taken.Contains((u, v))) continue;
                for (int y = s.Y; y < s.Y + s.Height; y++)
                {
                    if (Has(u, y, v)) continue;
                    foreach (Dir f in Dirs.All)
                    {
                        Int3 n = At(u, y, v) + f.Vec();
                        if (!S.TryGet(n, out Block b) || b.IsFiller || !S.TypeOf(b).IsArmor || S.TypeOf(b) == Glass) continue;
                        if (s.Taken.Contains((n.X, n.Z + Pc.V0)) && f != Dir.NegY) continue; // a cargo stack keeps its colours
                        b.Paint[f.Opposite()] = f == Dir.NegY ? floor : wall;
                        S.Set(n, b);
                    }
                }
            }
        }
    }

    /// <summary>
    /// What the crash did (the plan's tears, the top deck crushed to 2 m where it struck) and what the years did since:
    /// a share of the devices wrecked, the more so the worse its state, and holes knocked in the shell. Never all of
    /// anything that matters: a third of the thrusters, generators and chairs come through, so it is worth the work.
    /// </summary>
    void Damage()
    {
        foreach (var (p, _) in S.Blocks().ToList())
            if (H.Torn(p.X, p.Y, p.Z + Pc.V0)) S.Clear(p);
        int top = H.Decks - 1;
        // The decks by the tears are the crash's too: what stands of them is ragged, low and open to the sky.
        for (int v = Pc.V0; v < Math.Min(VEnd, H.L); v++)
            if (H.Break >= 0 && Math.Abs(v - H.Break) < 9 || H.Cut >= 0 && v >= H.Cut - 6 && v < H.Cut + 8)
                for (int d = 0; d < H.Decks; d++)
                    for (int u = -H.Half; u <= H.Half; u++) Out.Crushed.Add(At(u, Plan.Walk(d), v));
        for (int v = Pc.V0; v < Math.Min(VEnd, H.L); v++)
        {
            if (H.Crushed(v) == 0) continue;
            for (int u = -H.Half; u <= H.Half; u++)
            {
                if (InTower(u, v) || ZoneAt(top, u, v) is not (int z and >= 0)) continue;
                for (int y = H.Top - H.Crushed(v); y < H.Top; y++)
                    if (Interior(u, y, v) && !H.Torn(u, y, v)) Solid(u, y, v, Paint["scorch"]);
                Out.Spaces[z].Height = Math.Min(Out.Spaces[z].Height, 4 - H.Crushed(v));
                Out.Crushed.Add(At(u, Plan.Walk(top), v));
            }
        }
        float devices = State switch { PoiState.Active => 0.1f, PoiState.Abandoned => 0.3f, PoiState.Infested => 0.4f, _ => 0.55f };
        float shell = State switch { PoiState.Active => 0.004f, PoiState.Abandoned => 0.012f, PoiState.Infested => 0.016f, _ => 0.04f };
        foreach (var kind in S.Blocks().Where(b => !b.block.IsFiller && S.TypeOf(b.block) is var t && !t.IsArmor && t.Function is not ("stairs" or "core" or "container") && b.pos != Out.Door)
                     .GroupBy(b => S.TypeOf(b.block).Function).ToList())
        {
            var all = kind.Select(b => b.pos).OrderBy(_ => Rng.Next()).ToList();
            int keep = kind.Key is "thruster" or "generator" or "chair" ? Math.Max(1, all.Count / 3) : 0;
            foreach (Int3 p in all.Take(Math.Max(0, Math.Min(all.Count - keep, (int)MathF.Round(all.Count * devices))))) S.Clear(p);
        }
        // Holes in the shell, a few blocks round each.
        var skin = S.Blocks().Where(b => S.TypeOf(b.block) == Plating && !Interior(b.pos.X, b.pos.Y, b.pos.Z + Pc.V0) && b.pos.Y > 1).Select(b => b.pos).ToList();
        int holes = (int)(skin.Count * shell / 6f);
        for (int h = 0; h < holes && skin.Count > 0; h++)
        {
            Int3 at = skin[Rng.Next(skin.Count)];
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        Int3 c = at + new Int3(dx, dy, dz);
                        if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) <= 1 && S.TryGet(c, out Block b) && S.TypeOf(b) == Plating && !Interior(c.X, c.Y, c.Z + Pc.V0)) S.Clear(c);
                    }
        }
    }

    /// <summary>
    /// A Husk ship (DESIGN: hijacked vessels of any faction, keeping its hull, overrun with worm flesh): flesh grown in
    /// rounded masses out of its rooms' walls and corners, the holds and the engine room the worst, now and then through
    /// the deck overhead into the room above; veins through the walls round it; every lamp that still works flickering a
    /// light that looks wrong. Never in a doorway's way or a corridor: what lives here still gets about.
    /// </summary>
    void Infest()
    {
        var lamps = S.Blocks().Where(b => S.TypeOf(b.block).IsLight).Select(b => b.pos).ToList();
        foreach (Int3 l in lamps) S.SetLight(l, LightSettings.Make(0x9ad040, LightAnim.Flicker, 0.35f, l.X * 5 + l.Z));
        foreach (Space s in Out.Spaces.Where(s => !s.Corridor && s.Floor.Count >= 6))
        {
            var near = new HashSet<(int, int)>(s.Doors.SelectMany(d => Around(d.Inside, 2)));
            var walls = s.Floor.Where(c => !near.Contains(c) && new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(o => !s.Floor.Contains((c.U + o.Item1, c.V + o.Item2)))).ToList();
            int blobs = (int)MathF.Ceiling(s.Area / (s.Purpose is "hold" or "ore-hold" or "engine-room" ? 10f : 18f));
            for (int i = 0; i < blobs && walls.Count > 0; i++)
            {
                var (cu, cv) = walls[Rng.Next(walls.Count)];
                float r = 1.4f + (float)Rng.NextDouble() * (s.Hall ? 3f : 1.8f);
                // On the floor against the wall, or up through the deck overhead.
                bool through = Rng.Next(4) == 0;
                float cy = through ? s.Y + s.Height - 0.5f : s.Y - 0.5f + r * 0.6f;
                Blob(s, near, cu + 0.5f, cy, cv + 0.5f, r, through);
            }
        }
        // Veins through the walls of the rooms it has taken.
        foreach (var (p, b) in S.Blocks().ToList())
            if (S.TypeOf(b) == Plating && Interior(p.X, p.Y, p.Z + Pc.V0) && Rng.Next(7) == 0 && Dirs.All.Any(f => !S.Has(p + f.Vec())))
                S.Set(p, Armour(Plating, S.ShapeOf(b), b.Rot, Paint["vein"]));
    }

    /// <summary>A rounded mass of flesh about a point (hull frame, metres), shaped to its sphere a cell at a time; through a deck it takes the slab's cells too.</summary>
    void Blob(Space s, HashSet<(int, int)> near, float cu, float cy, float cv, float r, bool through)
    {
        bool In(float u, float y, float v) => (u - cu) * (u - cu) + (y - cy) * (y - cy) * 1.4f + (v - cv) * (v - cv) <= r * r;
        int R = (int)MathF.Ceiling(r) + 1;
        for (int v = (int)cv - R; v <= (int)cv + R; v++)
            for (int u = (int)cu - R; u <= (int)cu + R; u++)
            {
                if (!s.Floor.Contains((u, v)) || near.Contains((u, v))) continue;
                for (int y = (int)cy - R; y <= (int)cy + R; y++)
                {
                    if (y < s.Y || !through && y >= s.Y + s.Height || y > s.Y + s.Height + 1) continue;
                    bool slab = y >= s.Y + s.Height;
                    if (Has(u, y, v) && !(slab && S.TryGet(At(u, y, v), out Block b) && S.TypeOf(b) == Plating)) continue;
                    if (slab && y == s.Y + s.Height + 1 && Has(u, y, v)) continue;
                    int m = 0;
                    for (int c = 0; c < 8; c++)
                        if (In(u - 0.5f + (c & 1), y - 0.5f + (c >> 1 & 1), v - 0.5f + (c >> 2 & 1))) m |= 1 << c;
                    // Through the deck the slab stays whole (flesh over it); in the room, the mass's own shape.
                    if (slab && y == s.Y + s.Height) m = In(u, y, v) ? 255 : 0;
                    if (m == 0 || Fit.For(m) is not var (shape, rot)) continue;
                    Put(u, y, v, Armour(Plating, shape, rot, Paint["flesh"]));
                    s.Taken.Add((u, v));
                }
            }
    }

    /// <summary>Solid that nobody sees or reaches (a cube of plating with plating on all six sides: the middle of a cargo stack, the fill behind the bow's rooms) is left out, as a builder would.</summary>
    void Hollow()
    {
        bool Plain(Int3 p) => S.TryGet(p, out Block b) && !b.IsFiller && S.TypeOf(b) == Plating && S.ShapeOf(b).IsCube;
        var shut = S.Blocks().Where(b => Plain(b.pos) && Dirs.All.All(f => Plain(b.pos + f.Vec()))).Select(b => b.pos).ToList();
        foreach (Int3 p in shut) S.Clear(p);
    }

    // ---- dressing -------------------------------------------------------------------------------------

    /// <summary>
    /// Each room dressed by the decorations library from its purpose (the recipes in deco-arrangements.json: the
    /// bridge's consoles, the quarters' bunks and lockers, the galley's counters and tables, the medbay's beds), the
    /// focal point first on the wall you face coming in and the ways between its doors kept clear, as the interiors
    /// proof dresses a house; then left as the hulk's state has it. Nothing stands in a block, a doorway or a stair.
    /// </summary>
    void Dress(DecoPoi deco)
    {
        DecoTech tech = H.Tech switch { TechLevel.Low => DecoTech.Low, TechLevel.High => DecoTech.High, _ => DecoTech.Mid };
        string state = State.ToString().ToLowerInvariant();
        for (int i = 0; i < Out.Spaces.Count; i++)
        {
            Space s = Out.Spaces[i];
            if (s.Corridor || s.Floor.Count < 6) continue;
            int x0 = s.U0, x1 = s.U1, z0 = s.V0, z1 = s.V1;
            var room = new DecoRoom
            {
                Width = x1 - x0 + 1, Depth = z1 - z0 + 1, Height = s.Height, RoomId = "ship-" + s.Purpose, Tags = new List<string> { "ship", "wreck" },
                State = state, Tech = tech, Seed = Hash.Combine(H.Seed, (ulong)(Pc.V0 * 977 + i)),
            };
            foreach (var (wall, _, width) in s.Doors)
            {
                if (wall.V < z0) room.Doors.Add(new DecoDoor(DecoWall.North, wall.U - x0 + width / 2f, width));
                else if (wall.V > z1) room.Doors.Add(new DecoDoor(DecoWall.South, wall.U - x0 + width / 2f, width));
                else if (wall.U < x0) room.Doors.Add(new DecoDoor(DecoWall.West, wall.V - z0 + width / 2f, width));
                else if (wall.U > x1) room.Doors.Add(new DecoDoor(DecoWall.East, wall.V - z0 + width / 2f, width));
            }
            for (int v = z0; v <= z1; v++)
                for (int u = x0; u <= x1; u++)
                    if (!s.Floor.Contains((u, v)) || s.Taken.Contains((u, v)) || !Has(u, s.Y - 1, v) || Has(u, s.Y, v) || Has(u, s.Y + 1, v))
                        room.Blocked.Add(new DecoRect(new System.Numerics.Vector2(u - x0 + 0.5f, v - z0 + 0.5f), new System.Numerics.Vector2(0.5f, 0), new System.Numerics.Vector2(0, 0.5f)));
            DecoPlan plan;
            try { plan = deco.Dress(room); }
            catch (Exception) { continue; }
            var corner = new PoiGenerator.Gen.Int3(x0, s.Y, z0);
            var dropped = new HashSet<int>();
            for (int k = 0; k < plan.Placements.Count; k++)
            {
                DecoPlacement pl = plan.Placements[k];
                if (pl.Support >= 0 && dropped.Contains(pl.Support)) { dropped.Add(k); continue; }
                DecoPiece piece = deco.Forge.Catalogue.Piece(pl.Piece);
                int y0 = s.Y + (int)MathF.Floor(pl.Y + 0.01f), y1 = s.Y + (int)MathF.Ceiling(pl.Y + piece.Height - 0.05f) - 1;
                if (piece.Mount == DecoMount.Ceiling) { y1 = s.Y + s.Height - 1; y0 = Math.Max(s.Y, y1 - (int)MathF.Ceiling(piece.Height) + 1); }
                var cells = new List<Int3>();
                foreach (PoiGenerator.Gen.Int3 c in RoomDecor.CellsOf(DecoLayout.Footprint(deco.Forge, pl), corner, 0.05f))
                    for (int y = y0; y <= Math.Max(y0, y1); y++) cells.Add(At(c.X, y, c.Z));
                if (cells.Count == 0 || cells.Any(c => S.Has(c) || !s.Floor.Contains((c.X, c.Z + Pc.V0)))) { dropped.Add(k); continue; }
                var at = new NV3(x0 - 0.5f + pl.X, s.Y - 0.5f + pl.Y + 0.008f, z0 - Pc.V0 - 0.5f + pl.Z);
                Out.Decor.Add(new Dressing(pl.Look, cells[0], cells, at, pl.Yaw));
            }
        }
    }
}
