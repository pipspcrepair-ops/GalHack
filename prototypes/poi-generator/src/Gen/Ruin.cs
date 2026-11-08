#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// How long ago and how hard the world fell, as the decay of its places reads it, from the world's roll:
/// <see cref="Wear"/> 0 (the fall is going on now) to 1 (two centuries of weather); <see cref="Falling"/>,
/// the world is falling right now (fresh damage, the lights still on); <see cref="Violence"/> 0 to 1, how much
/// of the fall was blast and fire (wars and strikes from the sky) rather than neglect; <see cref="Water"/> 0 to
/// 1, how wet the world is (a drowned or drowning world near 1, a dry one near 0): how far damp and mould take the walls.
/// </summary>
public sealed record RuinAge(float Wear, bool Falling, float Violence, float Water)
{
    /// <summary>A place off any world (the viewer, the tests' mass runs): a century after a fall that was mostly neglect.</summary>
    public static readonly RuinAge Default = new(0.65f, false, 0.2f, 0.35f);

    /// <summary>What a place spawned off any world is read as: <see cref="Default"/>, unless a viewer or proof frame asks for another age.</summary>
    public static RuinAge Fallback = Default;

    /// <summary>The roll's age, phase and falls, read as decay.</summary>
    public static RuinAge Of(WorldRoll roll)
    {
        bool falling = roll.One(LayerKind.Phase)?.Id == "falling";
        float wear = roll.One(LayerKind.Age)?.Id switch { "now" => 0.04f, "recent" => 0.3f, "century" => 0.65f, "long" => 0.92f, _ => 0.65f };
        if (falling) wear = Math.Min(wear, 0.06f);
        float violence = 0.1f, water = 0.35f;
        foreach (WorldLayer f in roll.All(LayerKind.Fall))
        {
            float v = f.Id is "nuclear_war" or "orbital_bombardment" or "kinetic_strikes" or "mirror_burn" or "meteor_swarm" ? 1f
                : f.Group == "war" ? 0.65f : f.Speed == "instant" ? 0.4f : 0.1f;
            violence = Math.Max(violence, v);
            if (f.Is.Contains("flood") || f.Sets.Any(s => s.Fact == "water" && s.Value == "sea")) water = Math.Max(water, 0.95f);
            if (f.Sets.Any(s => s.Fact == "water" && s.Value == "dry")) water = Math.Min(water, 0.1f);
        }
        if (roll.Fact("water") == "dry") water = Math.Min(water, 0.1f);
        // What every rolled value leans (WorldReading, from the data's bias lines): burned country and war are violence too, and a dry world keeps its water low.
        WorldReading lean = WorldReading.Of(roll);
        violence = Math.Max(violence, Math.Max(lean.Burnt, lean.Strife * 0.65f));
        if (lean.Dry > 0.6f) water = Math.Min(water, 0.1f);
        return new RuinAge(wear, falling, violence, water);
    }

    public override string ToString() => $"wear {Wear:0.00}{(Falling ? ", falling now" : "")}, violence {Violence:0.00}, water {Water:0.00}";
}

public static partial class StateTransforms
{
    static readonly Rgb Mould = new(0x2e, 0x34, 0x22), Stain = new(0x4a, 0x3a, 0x26), Plaster = new(0xc8, 0xc0, 0xb0), Leaf = new(0x7a, 0x5a, 0x2e), Wire = new(0x22, 0x22, 0x24);

    /// <summary>
    /// What the years leave inside a dead place, after its state and its dressing, by its world's age: ceilings
    /// come down in patches with their wires left hanging, mould and water climb the walls from the floor and the
    /// paint lifts, moss takes the damp corners, dead leaves blow in at the openings, and bent bars show at every broken
    /// concrete edge. No room is ever left standing in water.
    /// </summary>
    public static void Aftermath(PoiInstance inst, Rng rng)
    {
        if (inst.State == PoiState.Active || inst.Layout.Extras.InSpace) return;
        RuinAge age = inst.Ruin;
        bool ruined = inst.State == PoiState.Ruined;
        float wear = age.Wear * (ruined ? 1f : 0.7f);
        if (inst.Age != Age.OldWorld) wear *= 0.4f; // a sealed or living place keeps better
        var g = inst.Blocks;
        var nav = inst.Nav();
        // Nothing hangs in a doorway: a wire there would be drawn across the way with nothing to stop you.
        var doorways = new HashSet<Int3>(inst.Layout.Doorways.SelectMany(d => d.Cells).Concat(inst.Layout.ExitCells));
        foreach (PlacedModule m in inst.Layout.Modules)
        {
            if (m.Kind is not (ModuleKind.Room or ModuleKind.Hall or ModuleKind.Corridor or ModuleKind.Stair)) continue;
            bool damp = m.Underground || m.Min.Y < 0;
            var floor = m.Cells.Where(c => c.Y == m.Min.Y && g.Get(c).IsAir && nav.Standable(c) && !inst.Protected[c] && (BlockInfo.Supports(g.Get(c + Int3.Down).Type) || nav.IsRock(c + Int3.Down))).ToList();
            if (floor.Count == 0) continue;
            // The ceiling lets go in a few places, more the older it is: tiles and plaster under each, the wires hanging.
            int falls = (int)(floor.Count / 40f * wear * (ruined ? 1.6f : 1f) + rng.Float(0f, 0.9f) * wear);
            for (int k = 0; k < falls; k++)
            {
                Int3 at = rng.Pick(floor);
                if (Ceiling(g, at) is not int top) continue;
                foreach (Int3 c in floor)
                    if (c.FlatDistance(at) < 1.7f && g.Get(c).IsAir && rng.Chance(0.45f)) g.Set(c, Debris.Of(Material.Ceramic, Plaster, Debris.Tiles, Dirs.Horizontal[rng.Range(0, 4)]));
                for (int n = 0; n < 3; n++)
                {
                    Int3 hang = new(at.X + rng.Range(-1, 2), top - 1, at.Z + rng.Range(-1, 2));
                    if (!g.Get(hang).IsAir || !BlockInfo.Collides(g.Get(hang + Int3.Up).Type) || inst.Protected[hang] || doorways.Contains(hang)) continue;
                    g.Set(hang, new Block(BlockType.Cable, Material.Steel, Wire, Role.Debris));
                    if (rng.Chance(0.5f) && g.Get(hang + Int3.Down).IsAir && g.Get(hang + Int3.Down * 2).IsAir && !inst.Protected[hang + Int3.Down] && !doorways.Contains(hang + Int3.Down))
                        g.Set(hang + Int3.Down, new Block(BlockType.Cable, Material.Steel, Wire, Role.Debris));
                }
            }
            // Moss in the damp corners, and dead leaves in from the openings.
            foreach (Int3 c in floor)
            {
                if (!g.Get(c).IsAir) continue;
                int walls = Dirs.Horizontal.Count(d => g.Get(c + d.Vec()) is var w && !w.IsAir && w.Role is Role.Wall);
                if (walls >= 2 && rng.Chance((damp ? 0.5f : 0.2f) * wear)) g.Set(c, new Block(BlockType.Scatter, Material.Plant, Moss.Lerp(new Rgb(0x5a, 0x7a, 0x30), rng.Float()), Role.Debris));
                else if (Opening(g, inst.Layout.Occupied, c) && rng.Chance(0.35f * wear)) g.Set(c, new Block(BlockType.Scatter, Material.Plant, Leaf.Lerp(new Rgb(0x9a, 0x6a, 0x30), rng.Float()).Scale(0.8f + rng.Float(0f, 0.3f)), Role.Debris));
            }
            Walls(inst, m, wear, damp);
        }
        if (!age.Falling || ruined) Bars(inst, rng, wear);
    }

    /// <summary>The ceiling over a floor cell: the y of the first solid block above, within a storey or two.</summary>
    static int? Ceiling(BlockGrid g, Int3 c)
    {
        for (int y = c.Y + 2; y <= c.Y + 6; y++)
            if (BlockInfo.Collides(g.Get(c with { Y = y }).Type)) return BlockInfo.IsFullCube(g.Get(c with { Y = y }).Type) ? y : null;
        return null;
    }

    /// <summary>Beside a way the weather comes in: a cell outside the place, or open to the sky, next to it.</summary>
    static bool Opening(BlockGrid g, CellMask occ, Int3 c)
    {
        if (OpenToSky(g, c)) return true;
        foreach (Dir d in Dirs.Horizontal)
        {
            Int3 n = c + d.Vec();
            if (g.Get(n).IsAir && (!occ[n] || OpenToSky(g, n))) return true;
        }
        return false;
    }

    /// <summary>
    /// The walls of a room: damp climbs from the floor (higher and darker below ground and the older it is), the
    /// paint lifts off in patches higher up, and both are thickest in the corners.
    /// </summary>
    static void Walls(PoiInstance inst, PlacedModule m, float wear, bool damp)
    {
        var g = inst.Blocks;
        float rise = (damp ? 2.2f : 1.2f) * wear;
        foreach (Int3 c in m.Cells.Where(c => c.Y == m.Min.Y && g.Get(c).Type is not BlockType.Stair))
            foreach (Dir d in Dirs.Horizontal)
            {
                Int3 w = c + d.Vec();
                for (int h = 0; h < 4; h++)
                {
                    Int3 p = w + Int3.Up * h;
                    ref Block b = ref g.At(p);
                    if (b.IsAir || b.Role != Role.Wall || !BlockInfo.IsFullCube(b.Type) || BlockInfo.IsTransparent(b.Material) || b.Material is Material.Lumen or Material.Glyph) break;
                    uint n = Hash.Cell(new Int3(p.X >> 1, p.Y, p.Z >> 1), 211 + (uint)inst.SpawnSeed % 5);
                    float creep = rise - h + (n % 100) / 100f * 0.8f;
                    if (creep > 0f) b.Color = b.Color.Lerp(damp && n % 3 == 0 ? Mould : Stain, Math.Clamp(creep * 0.28f, 0.08f, 0.55f));
                    else if (h >= 1 && n % 100 < 30 * wear) b.Color = b.Color.Lerp(Plaster, 0.35f); // paint gone, the plaster under it
                }
            }
    }

    /// <summary>
    /// Bent, rusted bars out of every broken concrete edge: beside each concrete slab or wall a ruin tore (a cell
    /// the decay took from beside it), a few bars stand out of the torn face, for as long as the face holds them;
    /// out of a slab's edge sideways, out of a broken wall's side or its top.
    /// </summary>
    static void Bars(PoiInstance inst, Rng rng, float wear)
    {
        var g = inst.Blocks;
        int most = 120 + inst.Fallen.Count / 6, laid = 0;
        var spots = new List<(Int3 Cell, Dir Toward)>();
        foreach (Int3 c in inst.Fallen.Distinct())
        {
            if (!g.Get(c).IsAir || inst.Protected[c]) continue;
            foreach (Dir d in Dirs.All)
            {
                if (d == Dir.Up) continue;
                Block b = g.Get(c + d.Vec());
                if (!BlockInfo.IsFullCube(b.Type) || !Reinforced(b.Material)) continue;
                bool fits = b.Role switch
                {
                    Role.Floor or Role.Roof => d != Dir.Down,
                    Role.Wall => true,
                    _ => false,
                };
                if (fits) { spots.Add((c, d)); break; }
            }
        }
        rng.Shuffle(spots);
        foreach (var (c, toward) in spots)
        {
            if (laid >= most) break;
            if (!rng.Chance(0.3f + 0.5f * wear)) continue;
            g.Set(c, new Block(BlockType.Rebar, Material.Rust, Debris.Rust, Role.Debris, toward));
            laid++;
        }
    }

    static bool Reinforced(Material m) => m is Material.Concrete or Material.Fused or Material.Armour;
}
