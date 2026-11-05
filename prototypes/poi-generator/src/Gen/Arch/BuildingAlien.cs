#nullable enable
using System;
using System.Linq;

namespace PoiGenerator.Gen;

public sealed partial class Building
{
    /// <summary>
    /// What makes an alien place more than a re-skin: forms nobody human would build, laid over the
    /// plan without touching its rooms or ways. A cathedral grows a tapering spire as tall as itself,
    /// leaning fins down its flanks and a pointed portal; a library sprouts leaning crystal columns
    /// and a monolith; a hive swells into domes and breathing vents. The doorways still connect:
    /// frames stand beside and above them, never in front.
    /// </summary>
    void AlienForm()
    {
        string style = Fam.Style ?? "";
        if (!style.StartsWith("alien_", StringComparison.Ordinal)) return;
        Rgb shell = S.Wall.Colors[0];
        Rgb glow = S.Light.Colors[0];
        Int3 entrance = Out.EntranceCell;

        bool Free(Int3 p) => C.Get(p).IsAir && !C.Occ.Contains(p);
        void Put(Int3 p, Block b) { if (Free(p)) C.Set(p, b); }
        Block Bone(float shade) => new(BlockType.Cube, Material.Bone, shell.Scale(shade), Role.Wall);
        Block Crystal() => new(BlockType.Cube, Material.Crystal, glow.Lerp(new Rgb(0x8a, 0x9a, 0xff), 0.5f), Role.Decor);
        bool NearDoor(Int3 p) => Math.Abs(p.X - entrance.X) + Math.Abs(p.Z - entrance.Z) < 7;
        Int3 Middle() => new((Out.Min.X + Out.Max.X) / 2, 0, (Out.Min.Z + Out.Max.Z) / 2);
        int HalfSpan() => Math.Max(Out.Max.X - Out.Min.X, Out.Max.Z - Out.Min.Z) / 2;

        Portal();
        switch (style)
        {
            case "alien_cathedral": Spire(); Fins(); break;
            case "alien_library": Crystals(5, 6, 16); Monolith(); break;
            case "alien_hive": Domes(); Vents(); break;
            default: Crystals(3, 4, 10); break;
        }

        // A pointed portal round the way in: jambs beside the door, an arch meeting over it, a glowing keystone.
        void Portal()
        {
            var cells = Out.ExitCells;
            if (cells.Count == 0) return;
            Int3 lo = cells.Aggregate(Int3.Min), hi = cells.Aggregate(Int3.Max);
            Int3 outV = Out.EntranceFacing.Vec();
            Int3 across = Out.EntranceFacing is Dir.North or Dir.South ? new Int3(1, 0, 0) : new Int3(0, 0, 1);
            Int3 a = lo with { Y = 0 } - across + outV, b = hi with { Y = 0 } + across + outV;
            // Jambs, a lintel row joining them, and the point over it: every piece rests on the last.
            for (int y = 0; y <= 6; y++) { Put(a + Int3.Up * y, Bone(0.9f)); Put(b + Int3.Up * y, Bone(0.9f)); }
            for (Int3 c = a + Int3.Up * 6; c != b + Int3.Up * 6; c += across) Put(c, Bone(0.95f));
            Int3 key = lo with { Y = 7 } + outV;
            Put(key, new Block(BlockType.Cube, Material.Glyph, glow, Role.Light));
            Put(hi with { Y = 7 } + outV, Bone(1f));
            C.X.Lights.Add(new LightSpot { Cell = key, Color = glow, Mode = LightMode.Pulse, Energy = 1.4f, Range = 9f });
        }

        // A tapering spire on the roof, hollow rings stepping in as they climb, as tall again as the building.
        void Spire()
        {
            if (Hut) return;
            int y0 = WalkY(Top) + 4;
            int cx = Cx > Wm / 2 ? Math.Max(5, Cx / 2) : Math.Min(Wm - 6, (CoreEnd + Wm) / 2), cz = Zm + Dm / 2 + 2;
            // The spire stands on roof all round its base (a corner the tower shed is no footing).
            bool Footed(int r)
            {
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if ((Math.Abs(dx) == r || Math.Abs(dz) == r) && (!InFoot(Top, cx + dx, cz + dz) || C.Get(L(cx + dx, y0 - 1, cz + dz)).IsAir || !Free(L(cx + dx, y0, cz + dz)))) return false;
                return true;
            }
            // On the crossing if it can stand there, else the widest footing on the roof.
            int side = 0;
            (int x, int z) best = (cx, cz);
            foreach (var (tx, tz) in Enumerable.Range(0, W).SelectMany(x => Enumerable.Range(0, D).Select(z => (x, z))).OrderBy(p => Math.Abs(p.x - Wm / 2) + Math.Abs(p.z - (Zm + Dm / 2))))
            {
                cx = tx; cz = tz;
                int r = 4;
                while (r > 0 && !Footed(r)) r--;
                if (Footed(r) && r > side) { side = r; best = (tx, tz); }
                if (side >= 3) break;
            }
            (cx, cz) = best;
            if (!Footed(side)) return;
            int tall = Math.Max(18, Floors * 4);
            int y = y0;
            for (int r = side; r >= 0; r--)
            {
                int ring = r == 0 ? 6 : Math.Max(3, tall / (side + 1));
                for (int k = 0; k < ring; k++, y++)
                    for (int dz = -r; dz <= r; dz++)
                        for (int dx = -r; dx <= r; dx++)
                            if (Math.Abs(dx) == r || Math.Abs(dz) == r || k == ring - 1)   // the last course of each ring is a ledge the next rests on
                                Put(L(cx + dx, y, cz + dz), (k + dx + dz) % 5 == 0 && r > 0 ? new Block(BlockType.Cube, Material.Glyph, glow, Role.Light) : Bone(0.92f + 0.04f * (k % 2)));
            }
            Put(L(cx, y, cz), new Block(BlockType.Cube, Material.Crystal, glow, Role.Light));
            C.X.Lights.Add(new LightSpot { Cell = L(cx, y, cz), Color = glow, Mode = LightMode.Beacon, Energy = 2.4f, Range = 18f });
        }

        // Fins down the flanks: stepped buttresses leaning out from the walls, every few metres.
        void Fins()
        {
            int h = Math.Min(16, Floors * 4 - 2);
            foreach (Dir side in Dirs.Horizontal)
            {
                Int3 o = side.Vec();
                for (int z = 0; z < D; z++)
                    for (int x = 0; x < W; x++)
                    {
                        if (!InFoot(0, x, z) || InFoot(0, x + o.X, z + o.Z)) continue;   // an outer wall facing this way
                        if ((side is Dir.North or Dir.South ? x : z) % 7 != 3) continue;
                        Int3 wall = L(x, 0, z), outward = L(x + o.X, 0, z + o.Z) - wall;
                        if (NearDoor(wall)) continue;
                        for (int d = 1; d <= 4; d++)
                            for (int y = 0; y < h - (d - 1) * (h / 4); y++)
                                Put(wall + outward * d + Int3.Up * y, Bone(0.85f));
                    }
            }
        }

        // Leaning crystal columns round the place, glowing faintly: they climb in steps, drifting as they go.
        void Crystals(int count, int min, int max)
        {
            for (int k = 0; k < count; k++)
            {
                double ang = Rng.Float(0f, MathF.Tau);
                int dist = Rng.Between(4, 12);
                Int3 mid = Middle();
                Int3 at = new(mid.X + (int)(Math.Cos(ang) * (HalfSpan() + dist)), 0, mid.Z + (int)(Math.Sin(ang) * (HalfSpan() + dist)));
                if (NearDoor(at) || !Free(at)) continue;
                int tall = Rng.Between(min, max);
                Int3 lean = Rng.Chance(0.5f) ? new Int3(Rng.Chance(0.5f) ? 1 : -1, 0, 0) : new Int3(0, 0, Rng.Chance(0.5f) ? 1 : -1);
                Int3 c = at;
                for (int y = 0; y < tall; y++)
                {
                    Put(c + Int3.Up * y, Crystal());
                    if (y % 3 == 2) { c += lean; Put(c + Int3.Up * y, Crystal()); }
                }
                C.X.Lights.Add(new LightSpot { Cell = c + Int3.Up * (tall - 1), Color = glow, Mode = LightMode.Pulse, Energy = 1.2f, Range = 10f });
            }
        }

        // A monolith: a slab of crystal far taller than anything round it.
        void Monolith()
        {
            Int3 at = new(Out.Min.X - 7, 0, Middle().Z);
            if (NearDoor(at)) at = new Int3(Out.Max.X + 6, 0, Middle().Z);
            for (int y = 0; y < 22; y++)
                for (int dx = 0; dx < 2; dx++)
                    for (int dz = 0; dz < (y < 18 ? 3 : 2); dz++)
                        Put(at + new Int3(dx, y, dz), Crystal());
            C.X.Lights.Add(new LightSpot { Cell = at + new Int3(0, 22, 0), Color = glow, Mode = LightMode.Beacon, Energy = 2f, Range = 16f });
        }

        // Domes swelling out of the ground round the hive: mounds of chitin.
        void Domes()
        {
            for (int k = 0; k < 4; k++)
            {
                double ang = k * Math.PI / 2 + Rng.Float(-0.4f, 0.4f);
                int r = Rng.Between(3, 6);
                Int3 mid = Middle();
                Int3 at = new(mid.X + (int)(Math.Cos(ang) * (HalfSpan() + r + 3)), 0, mid.Z + (int)(Math.Sin(ang) * (HalfSpan() + r + 3)));
                if (NearDoor(at)) continue;
                for (int y = 0; y <= r; y++)
                    for (int dz = -r; dz <= r; dz++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            float d = MathF.Sqrt(dx * dx + dz * dz + y * y * 1.3f);
                            if (d > r) continue;   // solid mounds: every cell rests on the one below
                            Put(at + new Int3(dx, y, dz), new Block(BlockType.Cube, Material.Chitin, shell.Scale(0.8f + 0.05f * ((dx + dz + y) & 3)), Role.Wall));
                        }
            }
        }

        // Vents breathing out of the ground, each with a faint glow at its mouth.
        void Vents()
        {
            for (int k = 0; k < 6; k++)
            {
                Int3 at = Middle() + new Int3(Rng.Between(-14, 14), 0, Rng.Between(-14, 14));
                if (NearDoor(at) || !Free(at)) continue;
                int tall = Rng.Between(4, 9);
                for (int y = 0; y < tall; y++) Put(at + Int3.Up * y, new Block(BlockType.Pillar, Material.Chitin, shell.Scale(0.7f), Role.Decor));
                Put(at + Int3.Up * tall, new Block(BlockType.Cube, Material.Lumen, glow, Role.Light));
                C.X.Lights.Add(new LightSpot { Cell = at + Int3.Up * tall, Color = glow, Mode = LightMode.Pulse, Energy = 1f, Range = 8f });
            }
        }
    }
}
