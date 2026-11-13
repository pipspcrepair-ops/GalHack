#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// One build, four sites. Each state rewrites the same layout's blocks: abandoned turns the
/// power off and leaves a mess, infested lets the Hollow Bloom grow through it, ruined knocks
/// it apart. None of them touch the protected cells, so the way to the loot stays open.
/// </summary>
public static partial class StateTransforms
{
    public static readonly Rgb BloomFlesh = new(0xc8, 0x38, 0x7a);
    public static readonly Rgb BloomBruise = new(0x6a, 0x24, 0x70);
    public static readonly Rgb BloomGlow = new(0xc8, 0xf0, 0x60);
    public static readonly Rgb BloomBone = new(0xe6, 0xdc, 0xcb);
    public static readonly Rgb BloomDark = new(0x2a, 0x0f, 0x2e);
    static readonly Rgb Soot = new(0x1a, 0x14, 0x12);
    static readonly Rgb AshGrey = new(0x9a, 0x94, 0x8c);
    static readonly Rgb Moss = new(0x4f, 0x6a, 0x2a);
    static readonly Rgb Paper = new(0xc8, 0xc0, 0xa8);
    static readonly Rgb DeadGlass = new(0x24, 0x22, 0x28);

    public static void Apply(PoiInstance inst, Rng rng)
    {
        switch (inst.State)
        {
            case PoiState.Active:
                break;
            case PoiState.Abandoned:
                if (!inst.Ruin.Falling) PowerOff(inst);
                SetDoors(inst, rng, DoorState.Jammed, DoorState.Open);
                if (inst.Extras.Plain.Count > 0) Weathered(inst, rng, 0.015f + 0.06f * inst.Ruin.Wear, walls: false);
                else Abandon(inst, rng);
                break;
            case PoiState.Infested:
                PowerOff(inst);
                SetDoors(inst, rng, DoorState.Open, DoorState.Jammed);
                Infest(inst, rng);
                break;
            case PoiState.Ruined:
                // Mid-collapse the power still runs; the older the fall, the more of it is down, and blast takes more.
                if (!inst.Ruin.Falling) PowerOff(inst);
                if (inst.Extras.Plain.Count > 0)
                {
                    // Doors hang on or have gone, one by one.
                    foreach (var d in inst.Layout.Doorways)
                    {
                        bool gone = rng.Chance(0.5f);
                        foreach (Int3 c in d.Cells) { if (gone) inst.Blocks.Set(c, Block.Air); else inst.Blocks.At(c).Variant = DoorState.Jammed; }
                    }
                    Weathered(inst, rng, 0.05f + 0.24f * inst.Ruin.Wear + 0.08f * inst.Ruin.Violence, walls: true);
                    break;
                }
                BlowDoors(inst);
                Ruin(inst, rng, 0.08f + 0.3f * inst.Ruin.Wear + 0.1f * inst.Ruin.Violence);
                break;
        }
    }

    // ---- shared helpers ---------------------------------------------------------------

    static void PowerOff(PoiInstance inst)
    {
        var g = inst.Blocks;
        foreach (var (p, _) in g.Blocks())
        {
            ref Block b = ref g.At(p);
            if (b.Material != Material.Lumen || b.Type == BlockType.Core) continue;
            b.Material = Material.DeadLumen;
            b.Color = b.Color.Lerp(DeadGlass, 0.78f);
        }
        ref Block core = ref g.At(inst.CoreCell);
        core.Variant = 1; // dormant
    }

    static void SetDoors(PoiInstance inst, Rng rng, byte usual, byte other)
    {
        foreach (var d in inst.Layout.Doorways)
        {
            if (d.ModuleB == inst.Layout.VaultModule) continue; // vault doors hold
            if (d.Cells.Count > 0 && inst.Blocks.Get(d.Cells[0]).Variant == DoorState.Blast) continue; // so do blast doors
            if (inst.Layout.Extras.Lifts.Any(l => l.StopDoors.Contains(inst.Layout.Doorways.IndexOf(d)))) continue; // lift doors follow the car
            byte state = rng.Chance(0.7f) ? usual : other;
            foreach (Int3 c in d.Cells) inst.Blocks.At(c).Variant = state;
        }
        foreach (Int3 c in inst.Layout.ExitCells) inst.Blocks.At(c).Variant = DoorState.Open;
    }

    static void BlowDoors(PoiInstance inst)
    {
        foreach (var d in inst.Layout.Doorways)
        {
            bool lift = inst.Layout.Extras.Lifts.Any(l => l.StopDoors.Contains(inst.Layout.Doorways.IndexOf(d)));
            bool blast = d.Cells.Count > 0 && inst.Blocks.Get(d.Cells[0]).Variant == DoorState.Blast;
            foreach (Int3 c in d.Cells)
                if (d.ModuleB == inst.Layout.VaultModule || blast) continue;           // vault and blast doors hold
                else if (lift) inst.Blocks.At(c).Variant = DoorState.Closed;
                else inst.Blocks.Set(c, Block.Air);
        }
        foreach (Int3 c in inst.Layout.ExitCells) inst.Blocks.Set(c, Block.Air);
    }

    /// <summary>Floor cells inside a module you could stand in right now.</summary>
    public static List<Int3> FloorCells(PoiInstance inst, PlacedModule m, NavGrid nav) =>
        m.Cells.Where(c => (inst.Blocks.Get(c).IsAir || inst.Blocks.Get(c).Type == BlockType.Plate) && nav.Standable(c) && !nav.LiftAt(c) && !nav.LadderAt(c)).ToList();

    static bool NextToWall(PoiInstance inst, Int3 c)
    {
        foreach (Dir d in Dirs.Horizontal)
        {
            var b = inst.Blocks.Get(c + d.Vec());
            if (!b.IsAir && b.Role is Role.Wall or Role.Window) return true;
        }
        return false;
    }

    static void Put(PoiInstance inst, Int3 c, BlockType type, Material mat, Rgb color, Role role, Dir facing = Dir.North, byte variant = 0)
    {
        if (!inst.Blocks.InBounds(c)) return;
        if (BlockInfo.Collides(type) && inst.Protected[c]) return;
        inst.Blocks.Set(c, new Block(type, mat, color, role, facing, variant));
    }

    // ---- abandoned --------------------------------------------------------------------

    static void Abandon(PoiInstance inst, Rng rng)
    {
        var nav = inst.Nav();
        foreach (var m in inst.Layout.Modules)
        {
            var floor = FloorCells(inst, m, nav);
            foreach (Int3 c in floor)
                if (rng.Chance(0.07f)) Put(inst, c, BlockType.Scatter, Material.Cloth, rng.Chance(0.5f) ? Paper : inst.Kit.Palette[0].Colors[0], Role.Debris, Dirs.Horizontal[rng.Range(0, 4)]);
            int crates = m.Kind is ModuleKind.Room or ModuleKind.Hall ? rng.Between(0, 2) : 0;
            foreach (Int3 c in floor.Where(c => !inst.Protected[c] && NextToWall(inst, c)).OrderBy(_ => rng.Float()).Take(crates))
                Put(inst, c, BlockType.Crate, Material.Scrap, new Rgb(0x6a, 0x5a, 0x48), Role.Debris, Dirs.Horizontal[rng.Range(0, 4)], 1);
        }
        // Nature starts at the door.
        foreach (Int3 c in FloorCells(inst, inst.Layout.Modules[0], nav))
            if (inst.Blocks.Get(c).IsAir && rng.Chance(0.12f)) Put(inst, c, BlockType.Plant, Material.Plant, Moss, Role.Debris);

        // Outside, whatever they dropped on the way out: crates, a pulled-down barricade, weeds.
        Litter(inst, rng, rng.Between(7, 12));
    }

    /// <summary>Junk on open ground near the way in, never on the path.</summary>
    static void Litter(PoiInstance inst, Rng rng, int count)
    {
        var g = inst.Blocks;
        var nav = inst.Nav();
        Int3 e = inst.Layout.EntranceCell;
        var spots = new List<Int3>();
        for (int z = -9; z <= 9; z++)
            for (int x = -9; x <= 9; x++)
            {
                var c = new Int3(e.X + x, 0, e.Z + z);
                int d = Math.Abs(x) + Math.Abs(z);
                if (d < 3 || d > 12 || inst.Layout.Occupied[c] || inst.Protected[c] || !g.Get(c).IsAir || !nav.Standable(c)) continue;
                spots.Add(c);
            }
        rng.Shuffle(spots);
        var wood = new Rgb(0x6a, 0x54, 0x3c);
        foreach (Int3 c in spots.Take(count))
        {
            switch (rng.Range(0, 5))
            {
                case 0: case 1: Put(inst, c, BlockType.Crate, Material.Scrap, wood.Lerp(inst.Kit.Palette[0].Colors[0], 0.4f), Role.Debris, Dirs.Horizontal[rng.Range(0, 4)], 1); break;
                case 2: if (!inst.Protected[c]) inst.Blocks.Set(c, Debris.Of(Material.Concrete, inst.Kit.Palette[0].Colors[0].Scale(0.8f), Debris.Chunks)); break;
                case 3: Put(inst, c, BlockType.Barricade, Material.Timber, wood.Scale(0.7f), Role.Debris, Dirs.Horizontal[rng.Range(0, 4)]); break;
                default: Put(inst, c, BlockType.Plant, Material.Plant, Moss, Role.Debris); break;
            }
        }
    }

    // ---- infested ---------------------------------------------------------------------

    static void Infest(PoiInstance inst, Rng rng)
    {
        var g = inst.Blocks;
        var layout = inst.Layout;
        int seeds = Math.Clamp(4 + g.CountBlocks() / 300, 6, 28);

        // Bloom takes root on inside surfaces: air cells against a solid, never on the path.
        var roots = new List<Int3>();
        foreach (var m in layout.Modules)
            foreach (Int3 c in m.Cells)
                if (g.Get(c).IsAir && !inst.Protected[c] && TouchesSolid(g, c)) roots.Add(c);
        rng.Shuffle(roots);

        var seedCells = new List<Int3>();
        foreach (Int3 seed in roots.Take(seeds))
        {
            seedCells.Add(seed);
            Grow(inst, seed, rng.Between(5, 16), rng, insideOnly: true);
        }

        // Corruption creeps through the walls around each mass.
        foreach (Int3 s in seedCells)
            foreach (Int3 p in Sphere(s, 3.6f))
            {
                if (!g.InBounds(p)) continue;
                ref Block b = ref g.At(p);
                if (b.IsAir || b.Role is not (Role.Wall or Role.Roof or Role.Floor or Role.Decor)) continue;
                float t = 0.75f - p.FlatDistance(s) * 0.12f;
                if (t > 0f) b.Color = b.Color.Lerp(BloomDark, t);
            }

        // Tendrils hang from ceilings near the growths.
        foreach (var m in layout.Modules)
            foreach (Int3 c in m.Cells)
            {
                var above = g.Get(c + Int3.Up);
                if (!g.Get(c).IsAir || above.IsAir || !BlockInfo.IsStructure(above.Role)) continue;
                if (!seedCells.Any(s => s.Manhattan(c) < 8) || !rng.Chance(0.3f)) continue;
                Put(inst, c, BlockType.Tendril, Material.Bloom, rng.Chance(0.5f) ? BloomFlesh : BloomBruise, Role.Growth);
                if (rng.Chance(0.5f) && g.Get(c + Int3.Down).IsAir && g.Get(c + Int3.Down * 2).IsAir)
                    Put(inst, c + Int3.Down, BlockType.Tendril, Material.Bloom, BloomFlesh, Role.Growth, Dir.North, 1);
            }

        // Pods sit at the foot of the growths.
        var nav = inst.Nav();
        foreach (Int3 grown in inst.Growths.ToList())
            foreach (Dir d in Dirs.Horizontal)
            {
                Int3 c = grown + d.Vec();
                if (g.Get(c).IsAir && layout.Occupied[c] && !inst.Protected[c] && nav.Standable(c) && rng.Chance(0.12f))
                    Put(inst, c, BlockType.Pod, Material.Bloom, BloomFlesh, Role.Growth);
            }

        // Outside: it spills over the roofs and raises a spire that shouldn't stand up.
        var tops = RoofTops(inst).ToList();
        rng.Shuffle(tops);
        foreach (Int3 t in tops.Take(Math.Clamp(layout.Modules.Count * 2 / 3, 3, 10)))
            Grow(inst, t, rng.Between(10, 24), rng, insideOnly: false);
        var spireHost = layout.Modules[layout.CoreModule >= 0 ? layout.CoreModule : layout.KeyModules.Count > 0 ? layout.KeyModules[0] : layout.VaultModule];
        Int3? top = TopOf(inst, spireHost);
        if (top is Int3 st) Spire(inst, st, rng.Between(9, 15), rng);
    }

    static bool TouchesSolid(BlockGrid g, Int3 c)
    {
        foreach (Dir d in Dirs.All)
        {
            var b = g.Get(c + d.Vec());
            if (!b.IsAir && BlockInfo.Collides(b.Type)) return true;
        }
        return false;
    }

    /// <summary>A Bloom mass inside a place, for the depth pass: it creeps in wherever it is deep enough.</summary>
    public static void GrowAt(PoiInstance inst, Int3 seed, int size, Rng rng) => Grow(inst, seed, size, rng, insideOnly: true);

    /// <summary>A lumpy mass that creeps over surfaces from a seed cell.</summary>
    static void Grow(PoiInstance inst, Int3 seed, int size, Rng rng, bool insideOnly)
    {
        var g = inst.Blocks;
        var frontier = new List<Int3> { seed };
        int grown = 0;
        while (frontier.Count > 0 && grown < size)
        {
            int i = rng.Range(0, frontier.Count);
            Int3 c = frontier[i];
            frontier.RemoveAt(i);
            if (!g.InBounds(c) || !g.Get(c).IsAir || inst.Protected[c] || !TouchesSolid(g, c)) continue;
            if (insideOnly && !inst.Layout.Occupied[c]) continue;
            if (!insideOnly && (inst.Layout.Occupied[c] || c.Y <= Assembler.GroundY)) continue; // not into the rock
            var color = rng.Float() switch { < 0.5f => BloomFlesh, < 0.92f => BloomBruise, _ => BloomBone };
            Put(inst, c, BlockType.Growth, Material.Bloom, color, Role.Growth, Dir.North, (byte)rng.Range(0, 4));
            inst.Growths.Add(c);
            grown++;
            foreach (Dir d in Dirs.All) frontier.Add(c + d.Vec());
        }
    }

    /// <summary>Air cells sitting on top of roofs.</summary>
    static IEnumerable<Int3> RoofTops(PoiInstance inst)
    {
        var g = inst.Blocks;
        foreach (var m in inst.Layout.Modules)
        {
            if (m.Underground) continue;
            if (TopOf(inst, m) is Int3 t) yield return t;
        }
    }

    /// <summary>The air cell above the highest block in the middle of a module.</summary>
    public static Int3? TopOf(PoiInstance inst, PlacedModule m)
    {
        var g = inst.Blocks;
        int x = (m.Min.X + m.Max.X) / 2, z = (m.Min.Z + m.Max.Z) / 2;
        for (int y = g.Max.Y - 1; y >= m.Min.Y; y--)
        {
            var p = new Int3(x, y, z);
            if (!g.Get(p).IsAir) return y + 1 <= g.Max.Y && y > Assembler.CoverY ? p + Int3.Up : null;
        }
        return null;
    }

    /// <summary>A twisting column of growth: each cell touches the last, so it stands.</summary>
    static void Spire(PoiInstance inst, Int3 baseCell, int height, Rng rng)
    {
        var g = inst.Blocks;
        Int3 prev = baseCell;
        float phase = rng.Float(0f, 6.28f);
        for (int k = 0; k < height && prev.Y < g.Max.Y; k++)
        {
            float r = 1.6f * (1f - k / (float)height) + 0.4f;
            var next = new Int3(baseCell.X + (int)MathF.Round(MathF.Cos(phase + k * 0.75f) * r), baseCell.Y + k,
                baseCell.Z + (int)MathF.Round(MathF.Sin(phase + k * 0.75f) * r));
            // Step along x, then z, then up, so the column stays in one piece.
            Int3 at = prev;
            foreach (Int3 step in Walk(prev, next))
            {
                at = step;
                if (g.InBounds(at) && g.Get(at).IsAir)
                {
                    Put(inst, at, BlockType.Growth, Material.Bloom, k % 3 == 2 ? BloomBone : BloomFlesh, Role.Growth, Dir.North, (byte)(k % 4));
                    inst.Growths.Add(at);
                }
            }
            prev = next;
        }
        var tip = prev + Int3.Up;
        if (g.InBounds(tip) && g.Get(tip).IsAir) Put(inst, tip, BlockType.Pod, Material.Bloom, BloomGlow, Role.Growth, Dir.North, 1);
    }

    static IEnumerable<Int3> Walk(Int3 a, Int3 b)
    {
        Int3 c = a;
        yield return c;
        while (c.X != b.X) { c = c with { X = c.X + Math.Sign(b.X - c.X) }; yield return c; }
        while (c.Z != b.Z) { c = c with { Z = c.Z + Math.Sign(b.Z - c.Z) }; yield return c; }
        while (c.Y != b.Y) { c = c with { Y = c.Y + Math.Sign(b.Y - c.Y) }; yield return c; }
    }

    static IEnumerable<Int3> Sphere(Int3 c, float r)
    {
        int ri = (int)MathF.Ceiling(r);
        for (int y = -ri; y <= ri; y++)
            for (int z = -ri; z <= ri; z++)
                for (int x = -ri; x <= ri; x++)
                    if (x * x + y * y + z * z <= r * r) yield return c + new Int3(x, y, z);
    }

    // ---- ruined -----------------------------------------------------------------------

    static bool Removable(PoiInstance inst, Int3 p)
    {
        var b = inst.Blocks.Get(p);
        return !b.IsAir && !inst.Protected[p] && b.Role is Role.Wall or Role.Roof or Role.Window or Role.Light or Role.Decor;
    }

    /// <summary>
    /// Punches holes and drops roofs until about <paramref name="share"/> of the structure is
    /// gone, then lets go of anything left hanging and covers the floors in ash and weeds.
    /// </summary>
    static void Ruin(PoiInstance inst, Rng rng, float share)
    {
        var g = inst.Blocks;
        // Even glyphs fade in a ruin; they never quite go out.
        foreach (var (p, _) in g.Blocks())
        {
            ref Block b = ref g.At(p);
            if (b.Material is Material.Glyph or Material.Crystal) b.Color = b.Color.Scale(0.6f);
        }
        int before = PoiFactory.CountStructure(g);
        var fallen = new List<(Int3 Cell, Block Block)>();
        Carve(inst, rng, (int)(before * share), 1.6f, 3.8f, fallen);
        // Big places (towers) protect long paths: keep carving until a fair share is really gone.
        for (int pass = 0; pass < 4 && before - PoiFactory.CountStructure(g) < before * share * 0.6f; pass++)
            Carve(inst, rng, (int)(before * share * 0.3f), 1.6f, 3.8f, fallen);

        // A whole roof comes down on one side room.
        var branches = inst.Layout.Modules.Where(m => !m.OnPath && !m.Underground).ToList();
        if (branches.Count > 0)
        {
            var m = rng.Pick(branches);
            foreach (Int3 c in m.Cells.Where(c => g.Get(c).Role == Role.Roof && Removable(inst, c)).ToList())
            {
                fallen.Add((c, g.Get(c)));
                g.Set(c, Block.Air);
            }
        }

        Collapse(inst, rng, fallen);
        // What came down lies under where it came from, heaped where a lot fell.
        Debris.Lay(inst, fallen, rng, 0.45f, 160);
        inst.Fallen.AddRange(fallen.Select(f => f.Cell));
        float wear = inst.Ruin.Wear, fire = inst.Ruin.Violence;

        var nav = inst.Nav();
        foreach (var m in inst.Layout.Modules)
        {
            var floor = FloorCells(inst, m, nav);
            foreach (Int3 c in floor)
            {
                bool sky = OpenToSky(g, c);
                if (sky && rng.Chance(0.15f + 0.5f * fire)) Put(inst, c, BlockType.Ash, Material.Ash, AshGrey, Role.Debris);
                else if (sky && rng.Chance(0.5f * wear)) Put(inst, c, BlockType.Plant, Material.Plant, Moss.Lerp(new Rgb(0x7a, 0x8a, 0x3a), rng.Float()), Role.Debris);
            }
        }
        // Moss on what's left of the roofs.
        foreach (var (p, _) in g.Blocks())
        {
            ref Block b = ref g.At(p);
            if (b.Role is Role.Roof or Role.Wall && g.Get(p + Int3.Up).IsAir && Hash.Cell(new Int3(p.X >> 2, p.Y >> 2, p.Z >> 2), 103 + (uint)inst.SpawnSeed % 7) % 100 < 45 * wear)
                b.Color = b.Color.Lerp(Moss, 0.55f);
        }
    }

    static void Carve(PoiInstance inst, Rng rng, int target, float rMin, float rMax, List<(Int3 Cell, Block Block)>? fallen = null)
    {
        var g = inst.Blocks;
        var candidates = new List<Int3>();
        foreach (var (p, _) in g.Blocks())
            if (Removable(inst, p)) candidates.Add(p);
        if (candidates.Count == 0) return;

        int removed = 0;
        for (int tries = 0; tries < 300 && removed < target; tries++)
        {
            // Two picks, keep the higher: roofs and wall tops go first.
            Int3 a = rng.Pick(candidates), b = rng.Pick(candidates);
            Int3 c = a.Y >= b.Y ? a : b;
            if (g.Get(c).IsAir) continue;
            float r = rng.Float(rMin, rMax);
            foreach (Int3 p in Sphere(c, r))
            {
                if (!Removable(inst, p)) continue;
                float d = MathF.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y) + (p.Z - c.Z) * (p.Z - c.Z));
                if (!rng.Chance(0.95f - d / r * 0.35f)) continue;
                fallen?.Add((p, g.Get(p)));
                g.Set(p, Block.Air);
                removed++;
            }
            // Scorch around the edge of each hole.
            foreach (Int3 p in Sphere(c, r + 1.2f))
            {
                if (!g.InBounds(p)) continue;
                ref Block s = ref g.At(p);
                if (s.IsAir || s.Role is not (Role.Wall or Role.Roof or Role.Floor)) continue;
                float away = MathF.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y) + (p.Z - c.Z) * (p.Z - c.Z));
                s.Color = s.Color.Lerp(Soot, (0.12f + 0.6f * inst.Ruin.Violence) * Math.Clamp((r + 1.2f - away) / 2.4f, 0f, 1f)); // a burn is a ring round the hole that fades outward, not speckle
            }
        }
    }

    /// <summary>Anything no longer joined to the ground falls and becomes rubble (or, given <paramref name="fallen"/>, joins what fell, to be laid with it).</summary>
    static void Collapse(PoiInstance inst, Rng rng, List<(Int3 Cell, Block Block)>? fallen = null)
    {
        var g = inst.Blocks;
        var anchors = inst.Layout.Extras.InSpace ? LayoutCheck.StationAnchors(inst.Layout) : null;
        bool solid = inst.Extras.Plain.Count > 0;
        var floating = Structure.Floating(g, inst.Layout.Occupied, anchors, solid);
        var nav = new NavGrid(g, inst.Layout.Occupied, null, inst.Layout.Extras.InSpace);
        var was = floating.Select(p => g.Get(p)).ToList();
        foreach (Int3 p in floating) g.Set(p, Block.Air);
        for (int i = 0; i < floating.Count; i++)
        {
            Int3 p = floating[i];
            if (fallen != null) { if (was[i].Role != Role.Debris) fallen.Add((p, was[i])); continue; }
            if (!rng.Chance(0.3f) || was[i].Role == Role.Debris) continue;
            Int3 c = p;
            while (c.Y > g.Min.Y && g.Get(c + Int3.Down).IsAir && !nav.IsRock(c + Int3.Down)) c += Int3.Down;
            if (g.Get(c).IsAir && !inst.Protected[c] && inst.Layout.Occupied[c] && !g.Get(c + Int3.Down).IsAir && (!solid || BlockInfo.Supports(g.Get(c + Int3.Down).Type)))
                g.Set(c, Debris.Of(was[i], Debris.KindOf(was[i])));
        }
        // Rubble can't hang either.
        foreach (Int3 p in Structure.Floating(g, inst.Layout.Occupied, anchors, solid)) g.Set(p, Block.Air);
    }

    static bool OpenToSky(BlockGrid g, Int3 c)
    {
        for (int y = c.Y + 1; y <= g.Max.Y; y++)
            if (!g.Get(c with { Y = y }).IsAir) return false;
        return true;
    }

    // ---- a century or two after the fall ----------------------------------------------

    /// <summary>
    /// Old-world places, long after the fall: timber that wasn't sheltered has rotted and fallen in
    /// (concrete, stone and steel stand on as shells), and the spots trees have grown up through: courtyards,
    /// yards and anything open to the sky, some of them mature. Never on the protected way to the
    /// vault, the core or a cache.
    /// </summary>
    public static void Weather(PoiInstance inst, Rng rng)
    {
        if (inst.Age != Age.OldWorld || inst.State == PoiState.Active || inst.Extras.Plain.Count > 0) return; // a plain ruin has its own weather
        var g = inst.Blocks;
        // Timber rots unless something sturdier roofs it over.
        var rotten = new List<Int3>();
        foreach (var (p, b) in g.Blocks())
        {
            if (b.Material != Material.Timber || b.Role is not (Role.Wall or Role.Roof) || p.Y < 0 || inst.Protected[p]) continue;
            bool sheltered = false;
            for (int y = p.Y + 1; y <= Math.Min(g.Max.Y, p.Y + 8) && !sheltered; y++)
            {
                var above = g.Get(p with { Y = y });
                sheltered = !above.IsAir && above.Material != Material.Timber && BlockInfo.Collides(above.Type);
            }
            if (!sheltered && rng.Chance(b.Role == Role.Roof ? 0.85f : 0.55f)) rotten.Add(p);
        }
        foreach (Int3 p in rotten) g.Set(p, Block.Air);
        if (rotten.Count > 0) Collapse(inst, rng);

        // Trees through the gaps: floor open to the sky, off the protected paths, with room to grow.
        // Only the spots: the planet plants its own flora on them, or none (see Growth).
        var nav = inst.Nav();
        var spots = new List<Int3>();
        foreach (var m in inst.Layout.Modules)
            foreach (Int3 c in m.Cells)
            {
                // Through the ground floor's gaps; on an old enough ruin, on any storey open to the sky (a planter, a crack in a slab).
                if (c.Y != m.Min.Y || Math.Abs(c.Y) > 1 && inst.Ruin.Wear < 0.6f || inst.Protected[c] || !g.Get(c).IsAir || !nav.Standable(c) || !OpenToSky(g, c)) continue;
                bool clear = true;
                foreach (Dir d in Dirs.Horizontal) clear &= !inst.Protected[c + d.Vec()];
                if (clear) spots.Add(c);
            }
        inst.GrowthWanted = Math.Min((int)MathF.Round((spots.Count / 30 + (inst.State == PoiState.Ruined ? 3 : 1)) * (0.2f + inst.Ruin.Wear)), 8);
        rng.Shuffle(spots);
        foreach (Int3 root in spots)
        {
            // More spots than trees: a tree that doesn't fit one tries the next.
            if (inst.Growth.Count >= inst.GrowthWanted * 4) break;
            if (!inst.Growth.Exists(s => Math.Abs(s.Cell.X - root.X) + Math.Abs(s.Cell.Z - root.Z) < 6)) inst.Growth.Add(new GrowthSpot(root));
        }
    }

    /// <summary>
    /// The "blasted" decay for a place inside a blast's radius: roofs gone, the walls that faced
    /// ground zero stripped, interiors burnt, more of it the stronger the blast was here
    /// (<paramref name="strength"/> 0..1). <paramref name="towardX"/>, <paramref name="towardZ"/>
    /// point from the place toward ground zero. The protected way in survives; whatever is left
    /// hanging comes down.
    /// </summary>
    public static void Blast(PoiInstance inst, float towardX, float towardZ, float strength, Rng rng)
    {
        if (strength <= 0f) return;
        var g = inst.Blocks;
        var occ = inst.Layout.Occupied;
        var gone = new List<Int3>();
        foreach (var (p, b) in g.Blocks())
        {
            if (p.Y < 0 || inst.Protected[p] || b.Role is Role.Floor or Role.Core) continue;
            if (b.Role == Role.Roof && rng.Chance(0.4f + 0.6f * strength)) { gone.Add(p); continue; }
            if (b.Role is not (Role.Wall or Role.Window)) continue;
            // An outer wall whose open side looks toward ground zero takes the blast face on.
            foreach (Dir d in Dirs.Horizontal)
            {
                Int3 v = d.Vec();
                if (v.X * towardX + v.Z * towardZ < 0.5f) continue;
                Int3 n = p + v;
                if (!g.Get(n).IsAir || occ[n]) continue;
                if (rng.Chance(0.3f + 0.7f * strength)) gone.Add(p);
                break;
            }
        }
        foreach (Int3 p in gone) g.Set(p, Block.Air);
        // Burnt inside: scorch and ash on the floors left open.
        foreach (var m in inst.Layout.Modules)
            foreach (Int3 c in m.Cells.Where(c => c.Y == m.Min.Y))
            {
                if (!g.Get(c).IsAir || inst.Protected[c] || g.Get(c + Int3.Down).IsAir) continue;
                uint h = Hash.Cell(c, 211);
                if (h % 100 < 25 * strength) g.Set(c, new Block(BlockType.Scorch, Material.Ash, new Rgb(0x1e, 0x1a, 0x18), Role.Debris));
                else if (h % 100 < 40 * strength) g.Set(c, new Block(BlockType.Ash, Material.Ash, new Rgb(0x3a, 0x34, 0x30), Role.Debris));
            }
        Collapse(inst, rng);
        Structure.DropUntethered(g);
        inst.Blasted = strength;
    }

    /// <summary>
    /// A big blast's grading of what stood round it, column by column, by how hard it came down there
    /// (<paramref name="strength"/> at a local cell column, 0..1; <see cref="Strike.Strength"/>): by the rim
    /// only foundations, slabs and the stumps and bases of former towers; then stumps a storey or two tall;
    /// then gutted shells, their roofs, upper floors and windows gone; past that what the plain blast
    /// leaves standing. Run before <see cref="Blast"/>, which strips and burns what is left.
    /// </summary>
    public static void Flattened(PoiInstance inst, Func<int, int, float> strength)
    {
        var g = inst.Blocks;
        var gone = new List<Int3>();
        var columns = new Dictionary<(int, int), (float S, int Cap)>();
        foreach (var (p, b) in g.Blocks())
        {
            if (p.Y <= 0 || b.IsAir) continue;
            if (!columns.TryGetValue((p.X, p.Z), out var col))
            {
                float s = strength(p.X, p.Z);
                columns[(p.X, p.Z)] = col = (s, Cap(s, Hash.Cell(new Int3(p.X, 0, p.Z), 233)));
            }
            if (col.S < ShellFrom) continue;
            if (p.Y > col.Cap) { gone.Add(p); continue; }
            // A gutted shell: open to the sky, its upper floors fallen through, not a pane left.
            uint h = Hash.Cell(p, 239);
            if (b.Role is Role.Roof or Role.Window
                || b.Role == Role.Floor && h % 100 < 100 * Math.Clamp((col.S - ShellFrom) / (StumpFrom - ShellFrom), 0f, 1f)) gone.Add(p);
        }
        foreach (Int3 p in gone) g.Set(p, Block.Air);
        Structure.DropUntethered(g);
    }

    /// <summary>The strength from which a blast guts what it leaves standing, and from which it leaves only stumps.</summary>
    public const float ShellFrom = 0.45f, StumpFrom = 0.6f;

    /// <summary>The strength from which only slabs and the bases of towers are left: the ground by a big crater's rim.</summary>
    public const float Rim = 0.85f;

    /// <summary>
    /// The tallest a column stands (cells above the ground floor) where a blast came down this hard: two
    /// storeys or so where stumps begin, down to the slab by the rim; ragged column to column, and the odd
    /// one a tower's base standing a little proud. No cap short of stumps.
    /// </summary>
    public static int Cap(float strength, uint hash)
    {
        if (strength < StumpFrom) return int.MaxValue;
        float f = Math.Clamp((strength - StumpFrom) / (Rim - StumpFrom), 0f, 1f);
        int cap = (int)MathF.Round(9f * (1f - f)) + (int)(hash % 2);
        if (f >= 0.9f && hash % 100 < 12) cap += 2;
        return cap;
    }

    // ---- crash sites ------------------------------------------------------------------

    /// <summary>Baseline wreckage for crash sites: torn hull, scorch, and pieces strewn outside.</summary>
    public static void Wreck(PoiInstance inst, Rng rng, float damage)
    {
        var g = inst.Blocks;
        int before = PoiFactory.CountStructure(g);
        Carve(inst, rng, (int)(before * damage), 1.4f, 3f);
        Collapse(inst, rng);

        // Hull plates thrown clear, lying on the ground outside.
        var hull = inst.Kit.Palette[0];
        int pieces = rng.Between(8, 18);
        for (int i = 0; i < pieces * 6 && pieces > 0; i++)
        {
            var p = new Int3(rng.Range(g.Min.X + 1, g.Max.X), 0, rng.Range(g.Min.Z + 1, g.Max.Z));
            if (inst.Layout.Occupied[p] || inst.Protected[p] || !g.Get(p).IsAir) continue;
            if (p.FlatDistance(inst.Layout.EntranceCell) < 4f) continue;
            g.Set(p, hull.Make(p, Dir.North));
            g.At(p).Color = g.At(p).Color.Lerp(Soot, rng.Float(0.2f, 0.7f));
            g.At(p).Role = Role.Debris;
            pieces--;
        }
    }
}

/// <summary>Structural checks shared by the ruin pass and the validator.</summary>
public static class Structure
{
    /// <summary>Whether a block hangs: cables, wires and Bloom tendrils, which must always hang from something.</summary>
    public static bool Hangs(BlockType t) => t is BlockType.Cable or BlockType.Tendril;

    /// <summary>
    /// Hanging things with nothing holding them: a cable, wire or tendril hangs straight down from
    /// its anchor (through any more of itself above), so the cell at the top of its run must be
    /// under a solid block. One whose run ends in air is floating in mid-air.
    /// </summary>
    public static List<Int3> Untethered(BlockGrid g)
    {
        var loose = new List<Int3>();
        foreach (var (p, b) in g.Blocks())
        {
            // Bars stand out of the face they are cast in: with the face gone, they go.
            if (b.Type == BlockType.Rebar) { if (!BlockInfo.IsFullCube(g.Get(p + b.Facing.Vec()).Type)) loose.Add(p); continue; }
            if (!Hangs(b.Type)) continue;
            Int3 up = p + Int3.Up;
            while (Hangs(g.Get(up).Type)) up += Int3.Up;
            var anchor = g.Get(up);
            if (anchor.IsAir || !BlockInfo.Collides(anchor.Type)) loose.Add(p);
        }
        return loose;
    }

    /// <summary>Takes down every hanging thing left with nothing to hang from (a ruin lost its ceiling).</summary>
    public static int DropUntethered(BlockGrid g)
    {
        var loose = Untethered(g);
        foreach (Int3 p in loose) g.Set(p, Block.Air);
        return loose.Count;
    }

    /// <summary>
    /// The head of every stair or ramp cut down into the ground is a floor the place holds: where the
    /// cell past a flight's top, at its top step's level, is the ground, it becomes a floor block (the
    /// flight's own stuff) that the place draws and stands on. Left to the ground, the head is the cut's
    /// edge, and the ground's surface rounds off into the cut there, falling away from the top step: the
    /// step's edge stood proud of it, a lip at the head of every flight dug into the ground that stopped
    /// a walker. Now floor and step meet flush, and the ground holds back inside the block as at any
    /// outer wall (<see cref="OuterWall"/>). Returns how many were laid.
    /// </summary>
    public static int HeadFloors(Layout layout)
    {
        BlockGrid g = layout.Blocks;
        CellMask occupied = layout.Occupied;
        if (layout.Extras.InSpace) return 0;
        var heads = new Dictionary<Int3, Block>();
        foreach (var (c, b) in g.Blocks())
        {
            if (!BlockInfo.IsSlope(b.Type) || b.Facing is Dir.Up or Dir.Down) continue;
            Int3 head = c + b.Facing.Vec();
            // Ground there (below the ground's level, nothing built, not the place's open space), and the place's way on over it.
            if (head.Y > Assembler.GroundY || !g.InBounds(head) || occupied[head] || !g.Get(head).IsAir) continue;
            if (!occupied[head + Int3.Up] || BlockInfo.Collides(g.Get(head + Int3.Up).Type)) continue;
            heads.TryAdd(head, new Block(BlockType.Cube, b.Material, b.Color.Scale(0.93f), Role.Floor));
        }
        foreach (var (head, floor) in heads)
        {
            g.Set(head, floor);
            occupied[head] = true;
        }
        return heads.Count;
    }

    /// <summary>
    /// Every ladder lies flat against a wall: each run of ladder up a column turns, as one, to the side
    /// with the most wall along it (its back to the wall, Facing out), keeping the way it was drawn
    /// when that side is as good as any. A ladder with nothing beside it (one standing free on a
    /// gantry) stays as it was. However the place's plan turned it, it never stands side-on to its
    /// wall or hangs out in the shaft. Returns how many runs turned.
    /// </summary>
    public static int LadderWalls(Layout layout)
    {
        BlockGrid g = layout.Blocks;
        CellMask occupied = layout.Occupied;
        bool Wall(Int3 c)
        {
            Block b = g.Get(c);
            if (b.IsAir) return !layout.Extras.InSpace && c.Y <= Assembler.GroundY && !occupied[c];
            return BlockInfo.IsFullCube(b.Type) || b.Type is BlockType.Lattice or BlockType.Tank or BlockType.Machine or BlockType.SealedDoor or BlockType.Shelf or BlockType.Growth;
        }
        int turned = 0;
        foreach (var (c, b) in g.Blocks().ToList())
        {
            if (b.Type != BlockType.Ladder || g.Get(c + Int3.Down).Type == BlockType.Ladder) continue; // each run from its foot
            if (b.Role == Role.Decor) continue; // railings and gate leaves: bars along their line, not a ladder up a wall
            var run = new List<Int3>();
            for (Int3 p = c; g.Get(p).Type == BlockType.Ladder; p += Int3.Up) run.Add(p);
            Dir back = (b.Facing is Dir.Up or Dir.Down ? Dir.South : b.Facing).Opposite();
            int Along(Dir d) => run.Count(p => Wall(p + d.Vec()));
            Dir best = back;
            int most = Along(back);
            foreach (Dir d in new[] { back.Opposite(), back.RotateCw(1), back.RotateCw(3) })
                if (Along(d) > most) { best = d; most = Along(d); }
            if (best == back && b.Facing is not (Dir.Up or Dir.Down)) continue;
            foreach (Int3 p in run)
            {
                Block l = g.Get(p);
                l.Facing = best.Opposite();
                g.Set(p, l);
            }
            turned++;
        }
        return turned;
    }

    /// <summary>
    /// Whether the ground must be opened at a cell a place occupies. An outer wall is not, at any height:
    /// open ground beside a carved cell slumps toward it, leaving a curved dip along the wall. Left solid
    /// (<see cref="OuterWall"/>), the dip falls inside the wall and the ground runs flush up to it.
    /// </summary>
    public static bool Carved(BlockGrid g, CellMask occupied, Int3 c) => occupied[c] && !OuterWall(g, occupied, c);

    /// <summary>
    /// A solid block on the edge of a place, to whatever height: the ground comes right up to it and into it,
    /// a sill, a floor's edge or the top course of a wall as much as the wall under it. Only an opening
    /// (a doorway, a window, a gap decay left) is carved, as glass is: the ground in it would show.
    /// </summary>
    public static bool OuterWall(BlockGrid g, CellMask occupied, Int3 c)
    {
        Block b = g.Get(c);
        if (!occupied[c] || !BlockInfo.IsFullCube(b.Type) || BlockInfo.IsTransparent(b.Material)) return false;
        // Round it, corners too; and where a wall stands on it, round that: where a storey steps in, the ground comes to the wall above. (A roof under the ground is no edge: held back, the ground would fillet the room's corners.)
        Block over = g.Get(c + Int3.Up);
        bool walled = BlockInfo.IsFullCube(over.Type) && !BlockInfo.IsTransparent(over.Material);
        for (int dy = 0; dy <= (walled ? 1 : 0); dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (!occupied[c + new Int3(dx, dy, dz)] && (dx != 0 || dz != 0)) return true;
        return false;
    }

    /// <summary>
    /// Every block not joined (face to face, through other blocks) to the ground. Blocks below
    /// ground level that touch rock count as grounded.
    /// </summary>
    /// <summary>Building fabric, which holds up what joins it. Rubble, furniture and weeds only rest on what is under them.</summary>
    static bool Holds(BlockType t) => t is BlockType.Cube or BlockType.Breakable or BlockType.Ramp or BlockType.Corner or BlockType.InvCorner or BlockType.Stair or BlockType.Pillar or BlockType.Rib or BlockType.Lattice or BlockType.Pane or BlockType.Rail or BlockType.Pipe
        || BlockInfo.IsShaped(t);

    /// <param name="solidOnly">Only fabric holds others up, and a thin thing (a floor plate, a weed) only rests on what is under it; a wall stub standing on a floor plate is still floating.</param>
    public static List<Int3> Floating(BlockGrid g, CellMask occupied, IReadOnlyCollection<Int3>? anchors = null, bool solidOnly = false)
    {
        bool space = anchors != null;
        var nav = new NavGrid(g, occupied, null, space);
        var grounded = new CellMask(g.Min, g.Size);
        var queue = new Queue<Int3>();
        var anchorSet = anchors != null ? new HashSet<Int3>(anchors) : null;
        foreach (var (p, _) in g.Blocks())
        {
            bool anchor = space ? anchorSet!.Contains(p) : p.Y == g.Min.Y;
            if (!anchor)
                foreach (Dir d in Dirs.All)
                    if (nav.IsRock(p + d.Vec())) { anchor = true; break; }
            if (!anchor || solidOnly && !Holds(g.Get(p).Type)) continue;
            grounded[p] = true;
            queue.Enqueue(p);
        }
        while (queue.Count > 0)
        {
            Int3 p = queue.Dequeue();
            foreach (Dir d in Dirs.All)
            {
                Int3 q = p + d.Vec();
                if (!g.InBounds(q) || grounded[q] || g.Get(q).IsAir || solidOnly && !Holds(g.Get(q).Type)) continue;
                grounded[q] = true;
                queue.Enqueue(q);
            }
        }
        var floating = new List<Int3>();
        foreach (var (p, b) in g.Blocks())
        {
            if (grounded[p]) continue;
            // A thin thing rests on what is under it, a door hangs in the wall beside it, bars stand out of their face, a ladder is fixed to its wall or stands on its foot.
            if (solidOnly && !Holds(b.Type) && (nav.IsRock(p + Int3.Down) || grounded[p + Int3.Down] || b.Type == BlockType.Door && Dirs.Horizontal.Any(d => grounded[p + d.Vec()])
                || b.Type == BlockType.Rebar && grounded[p + b.Facing.Vec()] || b.Type == BlockType.Ladder && LadderHeld(p))) continue;
            floating.Add(p);
        }
        return floating;

        // Anywhere down its run: the wall behind it, or the floor under its foot.
        bool LadderHeld(Int3 p)
        {
            for (Int3 q = p; g.Get(q).Type == BlockType.Ladder; q += Int3.Down)
            {
                Int3 back = q - g.Get(q).Facing.Vec();
                if (grounded[back] || nav.IsRock(back) || grounded[q + Int3.Down] || nav.IsRock(q + Int3.Down)) return true;
            }
            return false;
        }
    }
}
