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
            case "alien_cathedral": Spire(); Vault(); Fins(); break;
            case "alien_library": CrystalDome(); Crystals(5, 6, 16); Monolith(); break;
            case "alien_hive": Mound(); Domes(); Vents(); break;
            default: Crystals(3, 4, 10); break;
        }

        // ---- shells: curved massing from a height field, stepped with ramps so it reads as curves ----

        // Lays a shell over plan cells: each column rises to its height above the base, filled down to
        // its lowest neighbour so the surface is closed, with a slope on top wherever the next column
        // is one higher. Never into a room, a stair or the way in (only free cells take blocks).
        void HeightShell(Func<int, int, int> height, int baseY, Func<int, int, int, Block> block, bool slopes)
        {
            for (int z = -8; z < D + 8; z++)
                for (int x = -8; x < W + 8; x++)
                {
                    int h = height(x, z);
                    if (h <= 0) continue;
                    int low = h;
                    foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) low = Math.Min(low, Math.Max(0, height(x + dx, z + dz)));
                    for (int y = Math.Max(0, low - 1); y < h; y++) Put(L(x, baseY + y, z), block(x, y, z));
                    if (!slopes) continue;
                    foreach (Dir d in Dirs.Horizontal)
                    {
                        Int3 v = d.Vec();
                        if (height(x + v.X, z + v.Z) == h + 1)
                        {
                            Int3 top = L(x, baseY + h, z);
                            Put(top, new Block(BlockType.Ramp, block(x, h, z).Material, block(x, h, z).Color, Role.Roof, R(d)));
                            break;
                        }
                    }
                }
        }

        // A ribbed vault down the nave: a pointed barrel over the roof, a rib standing proud every few metres.
        void Vault()
        {
            if (Hut) return;
            int y0 = WalkY(Top) + 4;
            bool alongX = Wm >= Dm;
            float half = (alongX ? Dm : Wm) / 2f, mid = alongX ? Zm + Dm / 2f : Wm / 2f;
            int Height(int x, int z)
            {
                if (!InFoot(Top, x, z) || C.Get(L(x, y0 - 1, z)).IsAir) return 0;
                float t = Math.Abs((alongX ? z : x) + 0.5f - mid) / half;
                if (t >= 0.92f) return 0;   // a walk left round the edge
                int h = (int)MathF.Round(half * 0.9f * MathF.Pow(1f - t * t, 0.8f));
                if ((alongX ? x : z) % 4 == 0) h += 1;   // ribs
                return h;
            }
            HeightShell(Height, y0, (x, y, z) => (alongX ? x : z) % 4 == 0 ? Bone(0.82f) : Bone(0.95f + 0.03f * (y % 2)), slopes: true);
        }

        // A mound swelling over and round the hive's way down, chitin lumped and pitted; the way in left open.
        void Mound()
        {
            var hut = HutRect();
            float cx = (hut.x0 + hut.x1) / 2f, cz = (hut.z0 + hut.z1) / 2f;
            float r = Math.Max(hut.x1 - hut.x0, hut.z1 - hut.z0) / 2f + 7f;
            Int3 outV = Out.EntranceFacing.Vec();
            int Height(int x, int z)
            {
                // Keep the way in clear: a lane straight out from the door.
                Int3 c = L(x, 0, z);
                Int3 rel = c - entrance;
                int along = rel.X * outV.X + rel.Z * outV.Z, across = Math.Abs(rel.X * outV.Z) + Math.Abs(rel.Z * outV.X);
                if (along > -6 && across <= 3) return 0;
                float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (z + 0.5f - cz) * (z + 0.5f - cz)) / r;
                if (d >= 1f) return 0;
                float lump = (Hash.Cell(new Int3(x / 3, 7, z / 3), 911) % 100) / 100f;
                return (int)MathF.Round((9f + lump * 3f) * (1f - d * d));
            }
            HeightShell(Height, 0, (x, y, z) => new Block(BlockType.Cube, Material.Chitin, shell.Scale(0.65f + 0.08f * ((x * 3 + z * 5 + y) & 3)), Role.Wall), slopes: true);
        }

        // A crystal dome: a faceted, stepped crown of crystal over the library's way down.
        void CrystalDome()
        {
            var hut = HutRect();
            float cx = (hut.x0 + hut.x1) / 2f, cz = (hut.z0 + hut.z1) / 2f;
            float r = Math.Max(hut.x1 - hut.x0, hut.z1 - hut.z0) / 2f + 4f;
            Int3 outV = Out.EntranceFacing.Vec();
            int Height(int x, int z)
            {
                Int3 rel = L(x, 0, z) - entrance;
                int along = rel.X * outV.X + rel.Z * outV.Z, across = Math.Abs(rel.X * outV.Z) + Math.Abs(rel.Z * outV.X);
                if (along > -6 && across <= 3) return 0;
                float d = MathF.Max(Math.Abs(x + 0.5f - cx), Math.Abs(z + 0.5f - cz)) / r;   // square: facets, not a ball
                if (d >= 1f) return 0;
                return (int)(10f * (1f - d));
            }
            HeightShell(Height, 0, (x, y, z) => Crystal(), slopes: false);
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
            int tall = Math.Max(30, Floors * 7);   // it has to clear the vault and own the skyline
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
