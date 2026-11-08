using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// The decay pass on fresh worlds: rubble is never a plain box (every hard pile is broken pieces, tilted and
    /// inside its cell, resting on a floor), bars stand out of a concrete face that is still there, standing water
    /// lies over a floor and under its room's ceiling, and the older and harder the fall, the further decay went.
    /// </summary>
    void DecayPass()
    {
        var rand = new Random();
        string first = "";
        void Note(string s) { if (first.Length == 0) first = " (first: " + s + ")"; }
        int piles = 0, lumps = 0, outside = 0, unheld = 0, bars = 0, loose = 0, pools = 0, wet = 0, badWater = 0;
        var kinds = new int[Debris.Count];
        var spawns = new List<PoiInstance>();
        foreach (FamilyDef f in Arch.Where(f => !f.Space).OrderBy(_ => rand.Next()).Take(24))
            spawns.Add(PoiFactory.Generate(_lib, f, 0, Rarity.Common, PoiState.Ruined, (ulong)rand.NextInt64()));
        // City lots, the owner's terrace among them, fallen some way.
        foreach (string kind in new[] { "terrace", "tower", "midrise", "house", "works" })
        {
            FamilyDef f = _lib.Families.First(x => x.Id == "city_" + kind);
            var rng = new Rng((ulong)rand.NextInt64());
            var lot = new CityLot(kind, kind == "terrace" ? 17 : 18, 17, kind is "tower" or "midrise" ? rng.Range(3, 9) : rng.Range(1, 4), rng.Range(0, 1000), rng.Float());
            Layout layout = PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, rng.NextU64(), new LayoutHints(Dirs.Horizontal[rng.Range(0, 4)], TechLevel.Mid, lot));
            foreach (float wear in new[] { 0.3f, 0.92f })
                spawns.Add(PoiFactory.Spawn(_lib, layout, PoiState.Ruined, rng.NextU64(), new WorldProfile { Ruin = new RuinAge(wear, false, rng.Float(), rng.Float()) }));
        }
        foreach (PoiInstance p in spawns)
        {
            var g = p.Blocks;
            var nav = p.Nav();
            string name = p.Family.Id;
            foreach (var (c, b) in g.Blocks())
            {
                if (b.Type == BlockType.Rubble && b.Role == Role.Debris)
                {
                    piles++;
                    byte look = Debris.Look(b, c);
                    kinds[look]++;
                    if (Debris.IsLumps(b) || look == Debris.Lumps) { lumps++; Note($"{name}: plain rubble lumps of {b.Material} at {c}"); }
                    if (!nav.Solid(c + Int3.Down)) { unheld++; Note($"{name}: {b} hangs at {c}"); }
                    var shape = Shapes.For(b, c, g);
                    float low = float.MaxValue;
                    foreach (ShapeBox s in shape)
                    {
                        var (lo, hi) = Corners(s);
                        low = MathF.Min(low, lo.Y);
                        if (lo.X < -0.01f || lo.Z < -0.01f || hi.X > 1.01f || hi.Z > 1.01f || lo.Y < -0.04f || hi.Y > 1.2f) { outside++; Note($"{name}: a {look} piece leaves its cell at {c}: {lo} to {hi}"); break; }
                    }
                    if (low > 0.03f) { outside++; Note($"{name}: a {look} pile stands off its floor at {c} (lowest {low:0.000})"); }
                }
                else if (b.Type == BlockType.Rebar)
                {
                    bars++;
                    if (!BlockInfo.IsFullCube(g.Get(c + b.Facing.Vec()).Type)) { loose++; Note($"{name}: bars at {c} with no face toward {b.Facing}"); }
                    foreach (ShapeBox s in Shapes.For(b, c, g))
                    {
                        var (lo, hi) = Corners(s);
                        if (lo.X < -0.03f || lo.Y < -0.03f || lo.Z < -0.03f || hi.X > 1.03f || hi.Y > 1.03f || hi.Z > 1.03f) { outside++; Note($"{name}: a bar leaves its cell at {c}: {lo} to {hi}"); break; }
                    }
                }
            }
            foreach (WaterPool w in p.Water)
            {
                pools++;
                PlacedModule m = p.Layout.Modules[w.Module];
                foreach (Int3 c in w.Cells)
                {
                    wet++;
                    // Over a floor and under the room's ceiling, never above the world's water table.
                    if (w.Level <= c.Y || w.Level > m.Max.Y || w.Level > p.Ruin.WaterTable + 0.01f || BlockInfo.IsFullCube(g.Get(c).Type)) { badWater++; Note($"{name}: water at {w.Level:0.0} over {c} in a room {m.Min.Y} to {m.Max.Y}"); break; }
                }
            }
        }
        Check(lumps == 0 && outside == 0 && unheld == 0 && piles > 200 && kinds[Debris.Heap] > 0 && kinds[Debris.Slab] > 0,
            $"no plain rubble boxes: {piles} piles in {spawns.Count} ruins ({string.Join(", ", Enumerable.Range(1, Debris.Count - 1).Select(k => $"{kinds[k]} {Kind(k)}"))}), {lumps} plain lumps, {outside} pieces out of their cell or off the floor, {unheld} on nothing{first}");
        Check(loose == 0 && bars > 20, $"bars stand out of torn concrete: {bars} cells of bars, {loose} with no face to hold them{first}");
        Check(badWater == 0, $"flooding lies below the waterline: {pools} pools over {wet} cells, {badWater} above their room or the water table{first}");

        // The same places, young and old, gentle and violent: decay scales with the fall's age and force.
        int older = 0, greener = 0, wetter = 0, sootier = 0, lit = 0, pairs = 0;
        foreach (FamilyDef f in Arch.Where(f => !f.Space && f.StateWeights[(int)PoiState.Ruined] > 0f).OrderBy(_ => rand.Next()).Take(10))
        {
            ulong seed = (ulong)rand.NextInt64();
            Layout layout = PoiFactory.BuildLayout(_lib, f, 0, Rarity.Common, seed);
            PoiInstance Make(RuinAge a) => PoiFactory.Spawn(_lib, layout, PoiState.Ruined, seed + 1, new WorldProfile { Ruin = a });
            PoiInstance young = Make(new RuinAge(0.04f, true, 0.1f, 0.3f)), old = Make(new RuinAge(0.92f, false, 0.1f, 0.95f)), blasted = Make(new RuinAge(0.92f, false, 1f, 0.95f));
            pairs++;
            if (old.RemovedFraction > young.RemovedFraction) older++;
            int Green(PoiInstance p) => p.Blocks.Blocks().Count(t => t.Block.Material == Gen.Material.Plant) + p.GrowthWanted;
            if (Green(old) > Green(young)) greener++;
            if (old.Water.Sum(w => w.Cells.Count) >= young.Water.Sum(w => w.Cells.Count)) wetter++;
            int Soot(PoiInstance p) => p.Blocks.Blocks().Count(t => t.Block.Role is Role.Wall or Role.Roof or Role.Floor && t.Block.Color.R + t.Block.Color.G + t.Block.Color.B < 140) + p.Blocks.Blocks().Count(t => t.Block.Type == BlockType.Ash);
            if (Soot(blasted) >= Soot(old)) sootier++;
            // Mid-collapse the power still runs.
            if (young.Blocks.Blocks().Count(t => t.Block.Material == Gen.Material.DeadLumen) <= layout.Blocks.Blocks().Count(t => t.Block.Material == Gen.Material.DeadLumen)) lit++; // no lamp put out that wasn't out already
        }
        Check(older >= pairs * 0.8f && greener >= pairs * 0.7f && wetter == pairs && sootier >= pairs * 0.7f && lit == pairs,
            $"decay scales with the fall: of {pairs} places, {older} lost more structure old than mid-collapse, {greener} greener, {wetter} as wet or wetter on a drowned world, {sootier} as scorched or more after a violent fall, {lit} with the lights on mid-collapse");
    }

    static string Kind(int k) => k switch { Debris.Slab => "slabs", Debris.Chunks => "chunk piles", Debris.Heap => "heaps", Debris.Tiles => "tile falls", Debris.Timber => "timbers", Debris.Glass => "glass", _ => "lumps" };

    /// <summary>A shape box's bounds as drawn, turned by its tilt about its middle.</summary>
    static (V3 Lo, V3 Hi) Corners(ShapeBox s)
    {
        if (s.Tilt == default) return (s.Lo, s.Hi);
        V3 mid = (s.Lo + s.Hi) * 0.5f, half = (s.Hi - s.Lo) * 0.5f;
        V3 lo = new(float.MaxValue, float.MaxValue, float.MaxValue), hi = new(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            V3 v = mid + Shapes.Rotate(new V3((i & 1) == 0 ? -half.X : half.X, (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z), s.Tilt);
            lo = new V3(MathF.Min(lo.X, v.X), MathF.Min(lo.Y, v.Y), MathF.Min(lo.Z, v.Z));
            hi = new V3(MathF.Max(hi.X, v.X), MathF.Max(hi.Y, v.Y), MathF.Max(hi.Z, v.Z));
        }
        return (lo, hi);
    }
}
