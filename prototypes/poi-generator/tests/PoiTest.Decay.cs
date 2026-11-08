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
    /// inside its cell, resting on a floor), bars stand out of a concrete face that is still there, nothing is ever
    /// flooded and no cell is water (a place's own water is a surface over a floor it was built with), a floor's
    /// finish lies under everything set on it and only a full cube hides a face, and the older and harder the fall,
    /// the further decay went.
    /// </summary>
    void DecayPass()
    {
        var rand = new Random();
        string first = "";
        void Note(string s) { if (first.Length == 0) first = " (first: " + s + ")"; }
        int piles = 0, lumps = 0, outside = 0, unheld = 0, bars = 0, loose = 0, waterBlocks = 0, wet = 0, badWater = 0;
        int finished = 0, holed = 0;
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
        // Furnished and plumbed places as they were left: tables on tiles, the water a works was built with.
        foreach (string id in new[] { "flavour_farmstead", "water_works", "sewage_works" })
        {
            FamilyDef f = _lib.Families.First(x => x.Id == id);
            spawns.Add(PoiFactory.Generate(_lib, f, rand.Next(f.Variants.Count), f.Rarity, PoiState.Abandoned, (ulong)rand.NextInt64()));
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
            // No cell is water: what water a place holds is a surface over a floor it was built with.
            waterBlocks += g.Blocks().Count(t => t.Block.Material == Gen.Material.Water);
            foreach (Int3 c in p.Layout.Extras.Water)
            {
                wet++;
                Block below = p.Layout.Blocks.Get(c + Int3.Down);
                if (BlockInfo.IsFullCube(p.Layout.Blocks.Get(c).Type) || !BlockInfo.Collides(below.Type) && !(below.IsAir && c.Y - 1 <= Assembler.GroundY && !p.Layout.Occupied[c + Int3.Down])) { badWater++; Note($"{name}: water at {c} over no floor"); }
            }
            var (under, holes) = FinishesKept(p, Note);
            finished += under;
            holed += holes;
        }
        var (faces, unfaced, tops, untopped) = MeshedFaces(spawns.Where(s => s.State == PoiState.Abandoned).Concat(spawns.OrderBy(_ => rand.Next()).Take(4)), Note);
        Check(lumps == 0 && outside == 0 && unheld == 0 && piles > 200 && kinds[Debris.Heap] > 0 && kinds[Debris.Slab] > 0,
            $"no plain rubble boxes: {piles} piles in {spawns.Count} ruins ({string.Join(", ", Enumerable.Range(1, Debris.Count - 1).Select(k => $"{kinds[k]} {Kind(k)}"))}), {lumps} plain lumps, {outside} pieces out of their cell or off the floor, {unheld} on nothing{first}");
        Check(loose == 0 && bars > 20, $"bars stand out of torn concrete: {bars} cells of bars, {loose} with no face to hold them{first}");
        Check(waterBlocks == 0 && badWater == 0, $"no flooding, no water blocks: {waterBlocks} cells of water, {wet} cells under a place's own water, {badWater} of them over no floor{first}");
        Check(holed == 0 && finished > 20, $"a floor's finish lies under everything set on it: {finished} things stand on a finish kept under them, {holed} left a hole in it{first}");
        Check(unfaced == 0 && untopped == 0 && faces > 1000, $"only a full cube hides a face: {faces} faces of floors, walls and ceilings beside things drawn, {unfaced} missing; {tops} finishes under things drawn, {untopped} missing{first}");

        // The same places, young and old, gentle and violent: decay scales with the fall's age and force.
        int older = 0, greener = 0, sootier = 0, lit = 0, pairs = 0;
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
            int Soot(PoiInstance p) => p.Blocks.Blocks().Count(t => t.Block.Role is Role.Wall or Role.Roof or Role.Floor && t.Block.Color.R + t.Block.Color.G + t.Block.Color.B < 140) + p.Blocks.Blocks().Count(t => t.Block.Type == BlockType.Ash);
            if (Soot(blasted) >= Soot(old)) sootier++;
            // Mid-collapse the power still runs.
            if (young.Blocks.Blocks().Count(t => t.Block.Material == Gen.Material.DeadLumen) <= layout.Blocks.Blocks().Count(t => t.Block.Material == Gen.Material.DeadLumen)) lit++; // no lamp put out that wasn't out already
        }
        Check(older >= pairs * 0.8f && greener >= pairs * 0.7f && sootier >= pairs * 0.7f && lit == pairs,
            $"decay scales with the fall: of {pairs} places, {older} lost more structure old than mid-collapse, {greener} greener, {sootier} as scorched or more after a violent fall, {lit} with the lights on mid-collapse");
    }

    /// <summary>
    /// A floor's finish (the tiles, the boards) lies under everything set on it: nothing the spawn put on a finish
    /// the layout laid took it away, and in a paved ground-floor room everything standing on the floor has the
    /// finish under it. Returns the things standing on a kept finish, and the holes.
    /// </summary>
    static (int Under, int Holes) FinishesKept(PoiInstance p, Action<string> note)
    {
        var g = p.Blocks;
        var laid = p.Layout.Blocks;
        bool Finished(BlockGrid grid, Int3 c) => BlockInfo.IsFinish(grid.Get(c).Type) || BlockInfo.IsFinish(grid.Under(c).Type);
        // What grows roots where the paving broke; it never stands on it.
        static bool Rooted(Block b) => b.Type is BlockType.Plant or BlockType.Vine or BlockType.Growth;
        int under = 0, holes = 0;
        // What the spawn's passes set down (fixtures, dressing, decay, whoever lives there) keeps the finish under it,
        // wherever the paving round it is still whole (not weathered or torn away there).
        foreach (var (c, b) in g.Blocks())
        {
            if (BlockInfo.IsFinish(b.Type) || Rooted(b) || !Finished(laid, c) || !Dirs.Horizontal.All(d => Finished(g, c + d.Vec()))) continue;
            if (BlockInfo.IsFinish(g.Under(c).Type)) under++;
            else { holes++; note($"{p.Family.Id}: {b.Type} set at {c} took the {laid.Get(c).Type} finish under it away"); }
        }
        // What the plan set down in a paved room: furniture on the tiles, never in a hole in them.
        foreach (PlacedModule m in p.Layout.Modules)
        {
            if (m.Kind is not (ModuleKind.Room or ModuleKind.Hall or ModuleKind.Corridor) || m.Min.Y != 0) continue;
            var floor = m.Cells.Where(c => c.Y == 0).ToList();
            if (floor.Count(c => Finished(laid, c)) < floor.Count * 0.6f) continue;
            foreach (Int3 c in floor)
            {
                Block b = laid.Get(c);
                if (b.IsAir || BlockInfo.IsFinish(b.Type) || BlockInfo.IsFullCube(b.Type) || b.Role is Role.Traverse or Role.Door || Rooted(b)) continue;
                if (BlockInfo.IsFinish(laid.Under(c).Type)) under++;
                else { holes++; note($"{p.Family.Id}: a {b.Type} in its {m.Purpose} stands in a hole in the floor at {c}"); }
            }
        }
        return (under, holes);
    }

    /// <summary>
    /// Only a full cube hides a face: meshed as a planet draws them, every floor, wall and ceiling face of a full cube
    /// beside anything else (furniture, props, debris, bars) is drawn, and so is every finish under a thing.
    /// </summary>
    static (int Faces, int Missing, int Tops, int MissingTops) MeshedFaces(IEnumerable<PoiInstance> places, Action<string> note)
    {
        int faces = 0, missing = 0, tops = 0, missingTops = 0;
        foreach (PoiInstance p in places)
        {
            var g = p.Blocks;
            var drawn = new HashSet<(Int3 Cell, Int3 Out)>();
            var finishTops = new HashSet<Int3>();
            foreach (ChunkMesh chunk in Mesher.Build(g, p.Layout.Occupied, MeshOptions.OnPlanet(g.Min)))
                foreach (MeshBuffer buf in chunk.Surfaces)
                    for (int t = 0; t + 2 < buf.Indices.Count; t += 3)
                    {
                        int i0 = buf.Indices[t];
                        V3 P(int i) => new(buf.Positions[i * 3], buf.Positions[i * 3 + 1], buf.Positions[i * 3 + 2]);
                        V3 a = P(i0), b = P(buf.Indices[t + 1]), c = P(buf.Indices[t + 2]);
                        var n = new Int3((int)MathF.Round(buf.Normals[i0 * 3]), (int)MathF.Round(buf.Normals[i0 * 3 + 1]), (int)MathF.Round(buf.Normals[i0 * 3 + 2]));
                        if (Math.Abs(n.X) + Math.Abs(n.Y) + Math.Abs(n.Z) != 1) continue;
                        float Along(V3 v) => n.X != 0 ? v.X : n.Y != 0 ? v.Y : v.Z;
                        float plane = Along(a);
                        if (MathF.Abs(Along(b) - plane) > 1e-3f || MathF.Abs(Along(c) - plane) > 1e-3f) continue;
                        V3 mid = (a + b + c) * (1f / 3f);
                        if (MathF.Abs(plane - MathF.Round(plane)) < 1e-3f)
                        {
                            V3 inside = mid - new V3(n.X, n.Y, n.Z) * 0.5f;
                            drawn.Add((new Int3((int)MathF.Floor(inside.X), (int)MathF.Floor(inside.Y), (int)MathF.Floor(inside.Z)), n));
                        }
                        else if (n.Y == 1 && MathF.Abs(plane - MathF.Floor(plane) - BlockInfo.FinishTop) < 1e-3f)
                            finishTops.Add(new Int3((int)MathF.Floor(mid.X), (int)MathF.Floor(plane), (int)MathF.Floor(mid.Z)));
                    }
            foreach (var (c, b) in g.Blocks())
            {
                if (BlockInfo.IsFullCube(b.Type)) continue;
                foreach (Dir d in Dirs.All)
                {
                    Int3 n = c + d.Vec();
                    if (!BlockInfo.IsFullCube(g.Get(n).Type)) continue;
                    faces++;
                    if (!drawn.Contains((n, c - n))) { missing++; note($"{p.Family.Id}: the face of the {g.Get(n).Role} at {n} toward the {b.Type} at {c} is not drawn"); }
                }
                if (!BlockInfo.IsFinish(g.Under(c).Type)) continue;
                tops++;
                if (!finishTops.Contains(c)) { missingTops++; note($"{p.Family.Id}: no finish drawn under the {b.Type} at {c}"); }
            }
        }
        return (faces, missing, tops, missingTops);
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
