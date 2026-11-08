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
                    // The standard door's height (DoorSize), its head the wall over it.
                    if (Enumerable.Range(1, DoorSize.Tall - 1).Any(k => c.Get(w + Int3.Up * k).Role != Role.Wall) || !BlockInfo.Collides(c.Get(w + Int3.Up * DoorSize.Tall).Type)) continue;
                    var color = c.Style.AccentColor.Scale(0.5f);
                    for (int k = 0; k < DoorSize.Tall; k++) c.Set(w + Int3.Up * k, new Block(BlockType.SealedDoor, Material.Steel, color, Role.Story, h.Opposite(), 1));
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
        bool powered = inst.State == PoiState.Active || inst.Ruin.Falling && inst.State != PoiState.Infested, plain = x.Plain.Count > 0; // mid-collapse the lights are still on
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
            // Abandoned but whole: the lights still mostly burn on whatever feeds them, and the place's feel
            // (PlaceLights) sets a few flickering or failed. Deep wrongness still eats them.
            if (inst.State == PoiState.Abandoned && !rng.Chance(0.1f + w * 0.6f))
            {
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
        // One wrong detail from afar: in a dead place, a single window high up is still lit.
        if (!powered && !plain && (rng.Chance(0.75f) || !inst.Lights.Any(l => l.Cell.Y >= 0)))
        {
            var windows = new List<Int3>();
            foreach (var (p, b) in inst.Blocks.Blocks())
                if (b.Role == Role.Window && p.Y >= 1) windows.Add(p);
            if (windows.Count > 0)
            {
                int top = windows.Max(w => w.Y);
                var high = windows.Where(w => w.Y >= top - 4).ToList();
                Int3 lit = high[rng.Range(0, high.Count)];
                // The whole window, pane by pane: one lit tile reads as a fault, not a window.
                var window = new HashSet<Int3> { lit };
                var next = new Queue<Int3>(window);
                while (next.Count > 0 && window.Count < 6)
                {
                    Int3 pane = next.Dequeue();
                    foreach (Dir d in Dirs.All)
                        if (windows.Contains(pane + d.Vec()) && window.Add(pane + d.Vec())) next.Enqueue(pane + d.Vec());
                }
                Rgb glow = new(0xff, 0xc8, 0x78);
                foreach (Int3 c in window)
                {
                    var wb = inst.Blocks.Get(c);
                    wb.Material = Material.Lumen;
                    wb.Color = glow;
                    inst.Blocks.Set(c, wb);
                }
                inst.Lights.Add(new LightSpot { Cell = lit, Color = glow, Mode = LightMode.Steady, Energy = 0.9f, Range = 6f, Storey = (lit.Y + 1) / Building.Pitch });
            }
        }
        // No window to leave lit (a mine mouth, a hatch): a work lamp still burns over the way in.
        if (!inst.Lights.Any(l => l.Cell.Y >= 0))
        {
            Int3 out1 = layout.EntranceFacing.Vec();
            Int3 door = layout.EntranceCell - out1 * 2;
            bool done = plain; // a house keeps no lamp in its walls: only the light itself, for a lived-in later
            for (int back = 0; back <= 2 && !done; back++)
                for (int up = 1; up <= 5 && !done; up++)
                {
                    Int3 c = door + out1 * back + Int3.Up * up;
                    var lb = inst.Blocks.Get(c);
                    if (lb.IsAir || !BlockInfo.Collides(lb.Type)) continue;
                    lb.Material = Material.Lumen;
                    lb.Color = new Rgb(0xff, 0xa0, 0x50);
                    inst.Blocks.Set(c, lb);
                    inst.Lights.Add(new LightSpot { Cell = c + out1, Color = lb.Color, Mode = LightMode.Flicker, Energy = 1.1f, Range = 7f });
                    done = true;
                }
            if (!done || plain)   // nothing over the door at all: a lamp left by it
                inst.Lights.Add(new LightSpot { Cell = layout.EntranceCell + Int3.Up * 2, Color = new Rgb(0xff, 0xa0, 0x50), Mode = LightMode.Flicker, Energy = 1.1f, Range = 7f });
        }
        // A thin column of smoke where something still burns or vents.
        if (inst.State is PoiState.Ruined or PoiState.Infested || rng.Chance(0.3f))
        {
            var roofs = layout.Modules.Where(m => !m.Underground && m.Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Corridor)
                .Select(m => StateTransforms.TopOf(inst, m)).Where(t => t != null).Select(t => t!.Value).ToList();
            if (roofs.Count > 0) inst.Markers.Add(new Marker("smoke", roofs[rng.Range(0, roofs.Count)], inst.State == PoiState.Infested ? "spores" : "smoke"));
        }
        inst.Markers.AddRange(x.Markers);

        // Damp and disorder deep down whatever the state; the Bloom creeps in (thickest deepest) only where it has taken the place.
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
            foreach (Int3 r in inst.State == PoiState.Infested ? roots.Take(seeds) : Enumerable.Empty<Int3>())
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
        PoiLights.ApplyKitMood(inst);
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
