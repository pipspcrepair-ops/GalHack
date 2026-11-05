#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>
/// The deeper you go, the worse it gets. At plan time each storey gets a wrongness from the
/// family's ominousness and its depth (and a deliberately wrong spawn starts wrong near the top).
/// At spawn time, after the state, wrongness decides which lights still work (steady, flickering,
/// emergency red, dead), how far the Bloom has crept in, how thick the fog is, what you hear, and
/// the danger tier of what waits and what's left to find.
/// </summary>
public static class Decay
{
    static readonly Rgb Red = new(0xff, 0x30, 0x20), BloomGlow = new(0xb8, 0xf0, 0x60);

    public static void Plan(Canvas c, FamilyDef family, Rarity rarity, Rng rng)
    {
        var levels = c.X.Levels;
        if (levels.Count == 0) levels.Add(new LevelInfo { Storey = 0, WalkY = 0, Name = "ground" });
        int maxDepth = Math.Max(1, levels.Max(l => l.Depth));
        float baseWrong = c.X.Wrong ? 0.3f : 0f;
        foreach (var l in levels)
        {
            float d = l.Depth / (float)maxDepth;
            l.Wrongness = Math.Clamp(family.Ominous * (l.Depth > 0 ? 0.25f + 0.75f * d : 0.05f) + baseWrong, 0f, 1f);
            if (l.Depth > 0 && l.Storey == -maxDepth) l.Wrongness = Math.Max(l.Wrongness, family.Ominous);
            l.Fog = 0.008f + 0.05f * l.Wrongness + (l.Depth > 0 ? 0.006f : 0f);
            l.Tier = (int)rarity + (int)MathF.Round(l.Wrongness * 2f);
        }
        // Wrong on purpose, deep down: doors that open onto rock, set in the far ends of corridors.
        foreach (var m in c.Modules.Where(m => m.Kind == ModuleKind.Corridor))
        {
            var lvl = levels.FirstOrDefault(l => l.Storey == m.Storey);
            if (lvl == null || lvl.Wrongness < 0.45f || !rng.Chance(0.6f)) continue;
            // Find a stretch of the corridor's side wall with rock behind it.
            foreach (Int3 cell in m.Cells.Where(p => p.Y == lvl.WalkY).OrderBy(p => Hash.Cell(p, 7)))
            {
                foreach (Dir h in Dirs.Horizontal)
                {
                    Int3 w = cell + h.Vec(), behind = w + h.Vec();
                    if (c.Get(w).Role != Role.Wall || c.Occ.Contains(behind) || behind.Y > Assembler.GroundY) continue;
                    if (c.Get(w + Int3.Up).Role != Role.Wall) continue;
                    var color = c.Style.AccentColor.Scale(0.5f);
                    c.Set(w, new Block(BlockType.SealedDoor, Material.Steel, color, Role.Story, h.Opposite(), 1));
                    c.Set(w + Int3.Up, new Block(BlockType.SealedDoor, Material.Steel, color, Role.Story, h.Opposite(), 1));
                    c.X.Story.Add(new StoryProp("door to nowhere", w, "A welded door in a wall with nothing but rock behind it. Scratched from the other side."));
                    goto nextCorridor;
                }
            }
            nextCorridor:;
        }
        // The deepest walls bruise where the Bloom has been feeding on them.
        foreach (var (p, b) in c.Blocks.ToList())
        {
            var lvl = levels.FirstOrDefault(l => p.Y >= l.WalkY - 1 && p.Y <= l.WalkY + 3);
            if (lvl == null || lvl.Wrongness < 0.7f || b.Role != Role.Wall) continue;
            if (Hash.Cell(p, 13) % 100 > (lvl.Wrongness - 0.6f) * 120f) continue;
            var nb = b;
            nb.Color = b.Color.Lerp(StateTransforms.BloomDark, 0.45f + 0.3f * lvl.Wrongness);
            c.Blocks[p] = nb;
        }
    }

    public static LevelInfo LevelAt(Layout layout, int y)
    {
        LevelInfo? best = null;
        foreach (var l in layout.Extras.Levels)
            if (y >= l.WalkY - 1 && (best == null || l.WalkY > best.WalkY)) best = l;
        return best ?? layout.Extras.Levels.FirstOrDefault() ?? new LevelInfo();
    }

    /// <summary>Spawn time: lights, Bloom creep, debris and sounds, by state and wrongness.</summary>
    public static void Apply(PoiInstance inst, Rng rng)
    {
        var layout = inst.Layout;
        var x = layout.Extras;
        bool powered = inst.State == PoiState.Active;
        foreach (var spot in x.Lights)
        {
            var lvl = LevelAt(layout, spot.Cell.Y);
            float w = lvl.Wrongness;
            var l = new LightSpot { Cell = spot.Cell, Color = spot.Color, Energy = spot.Energy, Range = spot.Range, Module = spot.Module, Storey = spot.Storey, Mode = spot.Mode };
            if (spot.Mode is LightMode.Bloom or LightMode.Pulse) { inst.Lights.Add(l); continue; }
            if (spot.Mode == LightMode.Beacon)
            {
                if (powered || rng.Chance(0.6f)) inst.Lights.Add(l);
                continue;
            }
            if (powered)
            {
                // Even running places flicker deep down.
                if (w > 0.5f && rng.Chance(w * 0.5f)) l.Mode = LightMode.Flicker;
                else if (w > 0.8f && rng.Chance(0.3f)) continue;
                inst.Lights.Add(l);
                continue;
            }
            // Without power: most lights are dead. Battery emergency lamps hang on near the top,
            // fewer and redder the deeper you go; the deepest levels are black.
            float emergency = Math.Clamp(0.42f - 0.38f * w, 0.02f, 0.5f);
            if (inst.State == PoiState.Ruined) emergency *= 0.4f;
            if (rng.Chance(emergency))
            {
                l.Mode = rng.Chance(0.35f + w * 0.4f) ? LightMode.Flicker : LightMode.Emergency;
                l.Color = Red;
                l.Energy = 0.9f;
                l.Range = Math.Min(l.Range, 8f);
                inst.Lights.Add(l);
            }
        }
        // The Bloom creeps into the deep levels whatever the state (it's thickest deepest).
        var nav = inst.Nav();
        foreach (var m in layout.Modules)
        {
            var lvl = LevelAt(layout, m.Min.Y);
            float w = lvl.Wrongness;
            if (w < 0.35f || inst.State == PoiState.Ruined && w < 0.6f) continue;
            if (m.Kind is ModuleKind.Stair or ModuleKind.Lift) continue;
            int seeds = (int)MathF.Round((w - 0.3f) * (m.Kind == ModuleKind.Cave ? 6f : 2.5f) + rng.Float(0f, 0.8f));
            var roots = m.Cells.Where(c => inst.Blocks.Get(c).IsAir && !inst.Protected[c] && Touches(inst.Blocks, c)).ToList();
            rng.Shuffle(roots);
            foreach (Int3 r in roots.Take(seeds))
            {
                StateTransforms.GrowAt(inst, r, rng.Between(4, 6 + (int)(w * 10)), rng);
                if (rng.Chance(0.5f)) inst.Lights.Add(new LightSpot { Cell = r + Int3.Up, Color = BloomGlow, Mode = LightMode.Bloom, Energy = 0.9f + w, Range = 6f, Module = m.Index, Storey = m.Storey });
            }
            // Puddles and drips, things knocked over.
            foreach (Int3 c in StateTransforms.FloorCells(inst, m, nav))
            {
                if (inst.Protected[c] && !rng.Chance(0.5f)) continue;
                if (rng.Chance(0.05f * w)) inst.Blocks.Set(c, new Block(BlockType.Puddle, Material.Water, new Rgb(0x20, 0x26, 0x28), Role.Debris));
            }
        }
        // What you hear.
        foreach (var lvl in x.Levels)
        {
            float w = lvl.Wrongness;
            var floor = layout.Modules.Where(m => m.Storey == lvl.Storey && m.Kind is ModuleKind.Corridor or ModuleKind.Cave or ModuleKind.Hall).ToList();
            if (floor.Count == 0) continue;
            Int3 at(PlacedModule m) => new((m.Min.X + m.Max.X) / 2, lvl.WalkY + 1, (m.Min.Z + m.Max.Z) / 2);
            if (lvl.Depth > 0) inst.Sounds.Add(new SoundHook("drip", at(rng.Pick(floor)), 0.4f + w * 0.4f, lvl.Storey));
            if (w > 0.4f) inst.Sounds.Add(new SoundHook("distant-knock", at(rng.Pick(floor)), 0.3f + w * 0.5f, lvl.Storey));
            if (w > 0.75f) inst.Sounds.Add(new SoundHook("breathing", at(rng.Pick(floor)), 0.5f, lvl.Storey));
            if (powered) inst.Sounds.Add(new SoundHook("hum", at(floor[0]), 0.5f, lvl.Storey));
        }
        inst.Sounds.AddRange(x.Sounds);
    }

    static bool Touches(BlockGrid g, Int3 c)
    {
        foreach (Dir d in Dirs.All)
        {
            var b = g.Get(c + d.Vec());
            if (!b.IsAir && BlockInfo.Collides(b.Type)) return true;
        }
        return false;
    }
}
