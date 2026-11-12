using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;

namespace BlockBuilding;

/// <summary>
/// Blocks that span several cells (docs/one-metre-grid.md, section 2), on real parts given a size
/// (<see cref="MultiCellParts"/>): a 2×2×1 door, a 2×2×2 container and a 1×1×2 thruster.
/// </summary>
public partial class BuildTest
{
    Catalogue _mc;
    BlockType _door2, _box2, _jet2;

    void MultiCellTests()
    {
        _mc = MultiCellParts.Load(name => GameData.ReadText($"{GameData.DataDir}/{name}"));
        _door2 = _mc.Type(MultiCellParts.Door);
        _box2 = _mc.Type(MultiCellParts.Container);
        _jet2 = _mc.Type(MultiCellParts.Thruster);
        Run("Multi-cell: placement in all 24 orientations", TestMultiPlacement);
        Run("Multi-cell: refused on any blocked cell", TestMultiBlocked);
        Run("Multi-cell: mirror symmetry", TestMultiSymmetry);
        Run("Multi-cell: removal from a filler", TestMultiRemoval);
        Run("Multi-cell: damage from a filler", TestMultiDamage);
        Run("Multi-cell: faces, outer and inner", TestMultiFaces);
        Run("Multi-cell: the 2x2x1 door seals its room", TestMultiDoorRoom);
        Run("Multi-cell: connectivity and stats", TestMultiConnectivity);
        Run("Multi-cell: meshing and colliders", TestMultiMesh);
        Run("Multi-cell: blueprints", TestMultiBlueprints);
        Run("Multi-cell: navigation", TestMultiNav);
    }

    /// <summary>A core on a steel deck (-6..6, 0, -6..6), large grid: room to build on top in any direction.</summary>
    Structure MultiDeck(GridSize grid = GridSize.Large)
    {
        var s = new Structure(_mc, _mc.Grid(grid)) { Owner = Building.DefaultOwner };
        BlockType steel = _mc.Type("steel");
        for (int x = -6; x <= 6; x++)
            for (int z = -6; z <= 6; z++)
                s.Set(new Int3(x, 0, z), Building.Make(s, x == 0 && z == 0 ? _mc.CoreType : steel));
        return s;
    }

    /// <summary>
    /// Every turn of a 2×2×1 door, a 2×2×2 container and a 1×1×2 thruster set down on a deck: every
    /// cell of its turned size is filled, the anchor holds the block, each filler points back at it,
    /// and it counts as one block.
    /// </summary>
    void TestMultiPlacement()
    {
        foreach (BlockType t in new[] { _door2, _box2, _jet2 })
        {
            int good = 0;
            for (int rot = 0; rot < Orientations.Count; rot++)
            {
                Structure s = MultiDeck();
                int blocks = s.BlockCount, cells = s.Count;
                Block b = Building.Make(s, t, null, rot);
                // Grown up from the deck, from the cell over (1, 0, 1).
                Int3 anchor = Footprint.AnchorFor(t, rot, new Int3(1, 1, 1), Dir.PosY);
                PlaceError e = Building.Place(s, anchor, b);
                // A thruster turned with its nozzle (+z, not a mounting face) to the deck has nothing to attach by.
                if (!Stands(t, rot))
                {
                    if (e == PlaceError.NoSharedFace && s.Count == cells) good++;
                    else Fail($"{t.Id} in orientation {Orientations.Name(rot)}, nozzle down: {e}");
                    continue;
                }
                var want = new HashSet<Int3>(Footprint.Cells(anchor, t, rot));
                var (lo, hi) = (want.Aggregate(Int3.Min), want.Aggregate(Int3.Max));
                Int3 world = Footprint.WorldSize(t, rot);
                bool ok = e == PlaceError.None
                    && want.Count == (t.Size.X * t.Size.Y * t.Size.Z)
                    && lo == new Int3(1, 1, 1) && hi - lo + Footprint.One == world
                    && s.Count == cells + want.Count && s.BlockCount == blocks + 1
                    && want.All(c => s.TryGet(c, out Block f) && f.Type == t.Index && c + f.ToAnchor == anchor && f.IsFiller == (c != anchor))
                    && s.AnchorOf(want.Last()) == anchor
                    && s.Blocks().Count(x => x.block.Type == t.Index) == 1
                    && SortedSize(world) == SortedSize(t.Size);
                if (ok) good++;
                else Fail($"{t.Id} in orientation {Orientations.Name(rot)}: {e}, {want.Count} cells from {lo} to {hi}, {s.Count - cells} cells set");
            }
            Check(good == Orientations.Count, $"{t.Id} places in all 24 orientations ({good})");
        }
    }

    /// <summary>True if the block's face turned to the deck below is one it mounts by.</summary>
    static bool Stands(BlockType t, int rot) => BlockType.Has(t.AttachFaces, Orientations.ToLocal(rot, Dir.NegY));

    static string SortedSize(Int3 v) => string.Join("x", new[] { v.X, v.Y, v.Z }.OrderBy(n => n));

    /// <summary>A block spanning several cells needs every one of them: any single blocked cell refuses it, and the ghost marks that cell.</summary>
    void TestMultiBlocked()
    {
        BlockType steel = _mc.Type("steel");
        foreach (BlockType t in new[] { _door2, _box2 })
        {
            int refused = 0, total = 0;
            Structure s = MultiDeck();
            var anchor = new Int3(2, 1, 2);
            Block b = Building.Make(s, t);
            foreach (Int3 cell in Footprint.Cells(anchor, t, 0))
            {
                total++;
                s.Set(cell, Building.Make(s, steel));
                List<Int3> blocked = Building.Blocked(s, anchor, b);
                if (Building.Check(s, anchor, b) == PlaceError.Occupied && blocked.Count == 1 && blocked[0] == cell) refused++;
                s.Clear(cell);
            }
            Check(refused == total, $"{t.Id}: a block in any one of its {total} cells refuses it and is the one cell shown blocked ({refused})");
            Check(Building.Check(s, anchor, b) == PlaceError.None, $"{t.Id}: with every cell clear it goes");
        }

        // Attached through any outer face: a container with only its far top corner touching a block above.
        Structure air = MultiDeck();
        Block box = Building.Make(air, _box2);
        var high = new Int3(-3, 3, -3);
        Check(Building.Check(air, high, box) == PlaceError.NotAttached, "floating, it attaches to nothing");
        for (int y = 1; y <= 5; y++) air.Set(new Int3(-1, y, -2), Building.Make(air, steel));
        Check(Building.Check(air, high, box) == PlaceError.None, "a pillar beside one of its outer faces is enough to attach it");
    }

    /// <summary>Mirror symmetry mirrors the size too: the copy fills exactly the mirrored cells, and a block across the plane is placed once.</summary>
    void TestMultiSymmetry()
    {
        foreach (int rot in Enumerable.Range(0, Orientations.Count).Where(o => Stands(_jet2, o)))
        {
            Structure s = MultiDeck();
            var sym = Symmetry.Through(Int3.Zero);
            sym.Set(0, true, 0);
            Block b = Building.Make(s, _jet2, null, rot);
            Int3 anchor = Footprint.AnchorFor(_jet2, rot, new Int3(3, 1, 2), Dir.PosY);
            var results = Building.PlaceSymmetric(s, anchor, b, sym);
            var cells = new HashSet<Int3>(Footprint.Cells(anchor, _jet2, rot));
            var mirrored = new HashSet<Int3>(cells.Select(c => sym.Mirror(c, 0)));
            var copyCells = results.Count == 2 ? new HashSet<Int3>(s.CellsOf(results[1].copy.Pos, s.Get(results[1].copy.Pos))) : new HashSet<Int3>();
            Check(results.Count == 2 && results.All(r => r.error == PlaceError.None) && copyCells.SetEquals(mirrored),
                $"turn {Orientations.Name(rot)}: the mirror copy fills the mirrored cells ({results.Count} placed)");
        }

        // A 2×2×1 door straddling an even-width plane mirrors onto itself: one door, not two.
        Structure st = MultiDeck();
        var between = Symmetry.Through(Int3.Zero);
        between.Set(0, true, 1); // the plane between x = 0 and x = 1
        Block door = Building.Make(st, _door2);
        var placed = Building.PlaceSymmetric(st, new Int3(0, 1, 3), door, between);
        Check(placed.Count == 1 && placed[0].error == PlaceError.None && st.Blocks().Count(x => x.block.Type == _door2.Index) == 1,
            $"a door across the plane is placed once ({placed.Count} copies)");
    }

    /// <summary>Taking down any cell takes down the whole block and gives back one block's worth.</summary>
    void TestMultiRemoval()
    {
        Structure s = MultiDeck();
        var anchor = new Int3(2, 1, 2);
        Building.Place(s, anchor, Building.Make(s, _box2));
        int cells = s.Count, blocks = s.BlockCount;
        Int3 filler = anchor + new Int3(1, 1, 1);
        var inv = new Inventory { Creative = false };
        RemoveResult r = Building.Remove(s, filler, inv);
        Dictionary<string, int> one = _mc.CostOf(Building.Make(s, _box2), s.Grid);
        Check(r.Status == RemoveStatus.Removed && r.Block.Type == _box2.Index && !r.Block.IsFiller, "removing a filler removes the block, reported by its anchor");
        Check(s.Count == cells - 8 && s.BlockCount == blocks - 1 && Footprint.Cells(anchor, _box2, 0).All(c => !s.Has(c)), "every one of its 8 cells is cleared");
        Check(one.All(kv => inv[kv.Key] == kv.Value) && inv.Total == one.Values.Sum(), $"one container's materials come back, not eight ({inv.Total})");

        // Clearing any cell by hand clears all of it too: no half block is ever left.
        Building.Place(s, anchor, Building.Make(s, _box2));
        s.Clear(anchor + new Int3(0, 1, 0));
        Check(s.Count == cells - 8, "clearing one of its cells clears the whole block");
        // So does writing over one of its cells.
        Building.Place(s, anchor, Building.Make(s, _box2));
        s.Set(anchor + Int3.UnitX, Building.Make(s, _mc.Type("steel")));
        Check(s.Count == cells - 8 + 1 && !s.Has(anchor) && s.Has(anchor + Int3.UnitX), "a block written over one of its cells replaces the whole block");
    }

    /// <summary>A hit on a filler goes to the anchor: one pool of hit points, and the block breaks as a whole.</summary>
    void TestMultiDamage()
    {
        Structure s = MultiDeck();
        var anchor = new Int3(2, 1, 2);
        Building.Place(s, anchor, Building.Make(s, _box2));
        float max = s.MaxHp(s.Get(anchor));
        var report = new DamageReport();
        Int3 filler = anchor + new Int3(1, 0, 1);
        Damage.Hit(s, filler, DamageType.Kinetic, max * 0.3f + _box2.Armor, report);
        float after = s.Get(anchor).Hp;
        Check(after < max - 1f && after > 0f && s.Count == 169 + 8, $"a hit on a filler wears the anchor's hit points ({after:0}/{max:0})");
        Check(s.Get(filler).Hp == 0f && !Building.IsDamaged(s, Int3.Zero) && Building.IsDamaged(s, filler), "the filler holds none of its own; damage reads from the block");
        Check(s.RecentHits.Count > 0 && s.RecentHits[^1].Cell == anchor, "the hit is noted at the anchor");

        Damage.Hit(s, anchor + new Int3(0, 1, 1), DamageType.Kinetic, max * 2f, report);
        Check(Footprint.Cells(anchor, _box2, 0).All(c => !s.Has(c)) && report.Destroyed.Count == 1 && report.Destroyed[0].pos == anchor,
            "a hit through its hit points breaks the whole block, once");

        // A blast over all eight cells hits the block once, not eight times.
        Structure a = MultiDeck(), b = MultiDeck();
        Building.Place(a, anchor, Building.Make(a, _box2));
        Building.Place(b, anchor, Building.Make(b, _mc.Type("container")));
        var centre = new System.Numerics.Vector3(2.5f, 1.5f, 2.5f);
        DamageReport ra = new(), rb = new();
        Damage.BlastInto(a, centre, 1.0f, DamageType.Explosive, 400f, ra);
        Damage.BlastInto(b, centre, 1.0f, DamageType.Explosive, 400f, rb);
        float lostA = max - a.Get(anchor).Hp, lostB = s.MaxHp(b.Get(anchor)) - b.Get(anchor).Hp;
        Check(lostA > 0f && lostA <= lostB + 0.01f, $"a blast across all its cells hits it once ({lostA:0} lost, a one-cell container nearer the edge lost {lostB:0})");

        // A crash spread over it lands once, at the anchor.
        Structure c = MultiDeck();
        Building.Place(c, anchor, Building.Make(c, _box2));
        var crash = new DamageReport();
        Damage.Crash(c, new System.Numerics.Vector3(2.5f, 3f, 2.5f), -System.Numerics.Vector3.UnitY, 50f, crash);
        Check(c.RecentHits.Count(h => Footprint.Cells(anchor, _box2, 0).Contains(h.Cell)) == 1, "a crash over its top hits it once");
    }

    /// <summary>Faces inside the size don't exist; outer faces take the component's face lists for that whole side.</summary>
    void TestMultiFaces()
    {
        Structure s = MultiDeck();
        var anchor = new Int3(2, 1, 2);
        Building.Place(s, anchor, Building.Make(s, _box2));
        int good = 0;
        foreach (Int3 c in Footprint.Cells(anchor, _box2, 0))
        {
            Block f = s.Get(c);
            byte outer = 0;
            foreach (Dir d in Dirs.All)
                if (!Footprint.Cells(anchor, _box2, 0).Contains(c + d.Vec())) outer |= (byte)(1 << (int)d);
            bool ok = Footprint.OuterMask(_box2, f) == outer && _mc.TouchMask(f) == outer && _mc.FullMask(f) == outer
                && _mc.OccludeMask(f) == outer && _mc.SealMask(f) == 0x3f; // airtight all round: solid to air, inner faces too
            foreach (Dir d in Dirs.All)
                if ((outer & (1 << (int)d)) == 0) ok &= s.Joined(c, d);
            if (ok) good++;
        }
        Check(good == 8, $"each of the container's 8 cells has 3 outer faces and 3 inner ones that join, cover and seal nothing apart ({good})");

        // An open door's cells pass air everywhere; a shut one seals all round.
        Structure t = MultiDeck();
        Building.Place(t, anchor, Building.Make(t, _door2));
        Check(Footprint.Cells(anchor, _door2, 0).All(c => _mc.SealMask(t.Get(c)) == 0x3f), "a shut 2x2x1 door is sealed in every cell");
        Building.Toggle(t, anchor + new Int3(1, 1, 0));
        Check(Footprint.Cells(anchor, _door2, 0).All(c => t.Get(c).Open && _mc.SealMask(t.Get(c)) == 0), "toggling it from a filler opens the whole door, every cell passing air");
    }

    /// <summary>A 2×2×1 door in a 2×2 opening seals the room while it's shut; open, the leaks are the whole doorway.</summary>
    void TestMultiDoorRoom()
    {
        // A hollow box 6 wide, 5 high, 5 deep (0..5, 0..4, 0..4), core in the floor.
        var s = new Structure(_mc, _mc.Grid(GridSize.Large));
        BlockType steel = _mc.Type("steel");
        for (int x = 0; x <= 5; x++)
            for (int y = 0; y <= 4; y++)
                for (int z = 0; z <= 4; z++)
                    if (x == 0 || y == 0 || z == 0 || x == 5 || y == 4 || z == 4)
                        s.Set(new Int3(x, y, z), Building.Make(s, x == 2 && y == 0 && z == 2 ? _mc.CoreType : steel));
        Check(Rooms.Detect(s).Sealed.Count() == 1, "the box is sealed");
        // A 2×2 opening in the z = 4 wall: x 2..3, y 1..2.
        var opening = new[] { new Int3(2, 1, 4), new Int3(3, 1, 4), new Int3(2, 2, 4), new Int3(3, 2, 4) };
        foreach (Int3 c in opening) s.Clear(c);
        RoomReport r = Rooms.Detect(s);
        Check(!r.Sealed.Any() && r.LeakFaces.Count() == 4, $"the opening leaks at four faces ({r.LeakFaces.Count()})");

        Check(Building.Place(s, new Int3(2, 1, 4), Building.Make(s, _door2)) == PlaceError.None, "the 2x2x1 door fits the opening");
        r = Rooms.Detect(s);
        Check(r.Sealed.Count() == 1 && !r.Leaking.Any() && r.Sealed.First().Cells.Count == 4 * 3 * 3,
            $"shut, it seals the room ({r.Sealed.Count()} sealed, {r.Leaking.Count()} leaking)");
        Building.Toggle(s, new Int3(3, 2, 4));
        r = Rooms.Detect(s);
        var faces = r.LeakFaces.Select(f => f.Cell).ToHashSet();
        Check(!r.Sealed.Any() && r.LeakFaces.Count() == 4 && (faces.SetEquals(opening.Select(c => c - Int3.UnitZ)) || faces.SetEquals(opening)),
            $"open, the leaks are the doorway's four faces ({string.Join(", ", r.LeakFaces.Select(f => $"{f.Cell} {f.Dir.Name()}"))})");
        Building.Toggle(s, new Int3(2, 1, 4));
        Check(Rooms.Detect(s).Sealed.Count() == 1, "shut again, sealed again");
    }

    /// <summary>A block spanning several cells holds together, counts once, and goes whole into a wreck.</summary>
    void TestMultiConnectivity()
    {
        Structure s = MultiDeck();
        BlockType steel = _mc.Type("steel");
        var anchor = new Int3(2, 1, 2);
        Building.Place(s, anchor, Building.Make(s, _box2));
        StructureStats st = StructureStats.Compute(s);
        Check(st.Containers == 1 && Near(st.StorageRaw, _box2.Storage) && st.Blocks == s.BlockCount && st.Blocks == 169 + 1,
            $"stats count it once: one container, {st.StorageRaw} SU, {st.Blocks} blocks");
        Check(Near(st.BlockMass, 168 * s.StatsOf(Building.Make(s, steel)).Mass + s.StatsOf(Building.Make(s, _mc.CoreType)).Mass + s.StatsOf(s.Get(anchor)).Mass, 1e-4),
            "its mass counts once");

        // A tower on its far top corner hangs on by the container.
        Int3 top = anchor + new Int3(1, 2, 1);
        for (int y = 0; y < 3; y++) s.Set(top + new Int3(0, y, 0), Building.Make(s, steel));
        Check(Connectivity.Reachable(s, Int3.Zero).Contains(top + new Int3(0, 2, 0)), "a tower on the container's top is joined through it");
        Check(Connectivity.CutOffWithout(s, anchor + new Int3(0, 1, 1)) == 3, $"taking down the container (from a filler) would cut the tower's 3 blocks off ({Connectivity.CutOffWithout(s, anchor + new Int3(0, 1, 1))})");

        // Take the four deck cells under it: the container and its tower break away together, whole.
        foreach (Int3 c in new[] { anchor - Int3.UnitY, anchor + new Int3(1, -1, 0), anchor + new Int3(0, -1, 1), anchor + new Int3(1, -1, 1) }) s.Clear(c);
        List<Structure> wrecks = Connectivity.SplitOff(s);
        Structure w = wrecks.FirstOrDefault(x => x.Has(anchor));
        Check(w != null && w.BlockCount == 4 && w.Count == 8 + 3 && w.Get(anchor).Type == _box2.Index
              && Footprint.Cells(anchor, _box2, 0).All(c => w.TryGet(c, out Block f) && c + f.ToAnchor == anchor) && !s.Has(anchor + new Int3(1, 1, 1)),
            $"cut loose, the container goes whole into a wreck with its tower ({wrecks.Count} wrecks, {w?.BlockCount} blocks, {w?.Count} cells)");
    }

    /// <summary>Only the anchor draws, its parts stretched over the size; colliders cover the size; a block across a chunk border dirties every chunk it touches.</summary>
    void TestMultiMesh()
    {
        foreach (BlockType t in new[] { _box2, _door2, _jet2 })
        {
            int good = 0;
            for (int rot = 0; rot < Orientations.Count; rot++)
            {
                var s = new Structure(_mc, _mc.Grid(GridSize.Large));
                Block b = Building.Make(s, t, null, rot);
                b.Hp = s.MaxHp(b);
                ChunkMeshData data = ChunkMesher.BuildSingle(s, b);
                var (lo, hi) = Footprint.Box(t, rot);
                float cell = s.Grid.CellSize;
                Vector3 min = new Vector3(lo.X - 0.5f, lo.Y - 0.5f, lo.Z - 0.5f) * cell, max = new Vector3(hi.X + 0.5f, hi.Y + 0.5f, hi.Z + 0.5f) * cell;
                Vector3 vmin = Vector3.One * 1e9f, vmax = -Vector3.One * 1e9f;
                foreach (Vector3 v in data.Opaque.Vertices.Concat(data.Glass.Vertices))
                {
                    vmin = vmin.Min(v);
                    vmax = vmax.Max(v);
                }
                // Detail (handles, bolts) stands a little proud of a face, stretched with the block like its parts.
                Vector3 span = vmax - vmin, want = max - min, eps = want * 0.09f;
                bool inside = vmin.X >= min.X - eps.X && vmin.Y >= min.Y - eps.Y && vmin.Z >= min.Z - eps.Z
                    && vmax.X <= max.X + eps.X && vmax.Y <= max.Y + eps.Y && vmax.Z <= max.Z + eps.Z;
                // Every part of these three spans the block's two longest sides.
                int spanning = 0;
                for (int a = 0; a < 3; a++) if (span[a] > want[a] * 0.85f) spanning++;
                if (inside && spanning >= 2) good++;
                else Fail($"{t.Id} turned {Orientations.Name(rot)} draws from {vmin} to {vmax}, its cells run {min} to {max}");
            }
            Check(good == 24, $"{t.Id}: drawn whole, stretched over its turned size, in all 24 turns ({good})");
        }

        // Colliders: a shut door and a container fill every cell of their size, one box each after merging.
        foreach (BlockType t in new[] { _box2, _door2 })
        {
            var s = new Structure(_mc, _mc.Grid(GridSize.Large));
            Block b = Building.Make(s, t);
            s.Set(new Int3(4, 4, 4), b);
            ChunkMeshData d = ChunkMesher.Build(s, Int3.Zero);
            float vol = d.Boxes.Sum(x => x.size.X * x.size.Y * x.size.Z), cell = s.Grid.CellSize;
            Check(Mathf.Abs(vol - (t.Size.X * t.Size.Y * t.Size.Z) * cell * cell * cell) < 0.01f && d.Hulls.Count == 0,
                $"{t.Id}: colliders fill its {(t.Size.X * t.Size.Y * t.Size.Z)} cells exactly ({vol / (cell * cell * cell):0.##} cells in {d.Boxes.Count} boxes)");
        }
        var open = new Structure(_mc, _mc.Grid(GridSize.Large));
        Block od = Building.Make(open, _door2);
        od.Flags = BlockFlags.Open;
        open.Set(new Int3(4, 4, 4), od);
        ChunkMeshData od2 = ChunkMesher.Build(open, Int3.Zero);
        float frame = od2.Boxes.Sum(x => x.size.X * x.size.Y * x.size.Z) / Mathf.Pow(open.Grid.CellSize, 3);
        Check(od2.Boxes.Count == 3 && frame < 1f, $"open, only its frame collides: 3 boxes, stretched over the doorway ({frame:0.##} cells)");

        // Across a chunk border: drawn once, from the anchor's chunk; the other chunk draws none of it.
        var x = new Structure(_mc, _mc.Grid(GridSize.Large));
        var anchor = new Int3(15, 4, 4);
        x.Set(anchor, Building.Make(x, _box2));
        Check(x.DirtyChunks.Contains(Int3.Zero) && x.DirtyChunks.Contains(new Int3(1, 0, 0)), "set across a chunk border, it dirties both chunks");
        ChunkMeshData own = ChunkMesher.Build(x, Int3.Zero), other = ChunkMesher.Build(x, new Int3(1, 0, 0));
        Check(own.Opaque.VertexCount > 0 && other.Opaque.VertexCount == 0 && other.Boxes.Count == 1,
            $"the anchor's chunk draws it, the other chunk only holds its half's collider ({own.Opaque.VertexCount} and {other.Opaque.VertexCount} vertices)");
        x.DirtyChunks.Clear();
        x.Set(anchor + new Int3(2, 0, 0), Building.Make(x, _mc.Type("steel")));
        Check(x.DirtyChunks.Contains(Int3.Zero), "a block set beside its far half redraws the anchor's chunk (the faces it hides are drawn there)");
        foreach (Int3 c in new[] { new Int3(2, 1, 0), new Int3(2, 0, 1), new Int3(2, 1, 1) }) x.Set(anchor + c, Building.Make(x, _mc.Type("steel")));
        int before = own.Opaque.VertexCount;
        int after = ChunkMesher.Build(x, Int3.Zero).Opaque.VertexCount;
        Check(after < before, $"walled on that whole side, that side's faces are culled ({before} → {after} vertices)");
    }

    /// <summary>Blueprints save the anchor only and rebuild the fillers; a block landing in another's cells is a load error.</summary>
    void TestMultiBlueprints()
    {
        Structure s = MultiDeck();
        Orientations.TryParse("+x-z", out int side);
        Building.Place(s, new Int3(2, 1, 2), Building.Make(s, _box2));
        int jetRot = Enumerable.Range(0, Orientations.Count).First(o =>
            Building.Check(s, Footprint.AnchorFor(_jet2, o, new Int3(-2, 1, 3), Dir.PosY), Building.Make(s, _jet2, null, o)) == PlaceError.None && Footprint.WorldSize(_jet2, o).X == 2);
        Check(Building.Place(s, Footprint.AnchorFor(_jet2, jetRot, new Int3(-2, 1, 3), Dir.PosY), Building.Make(s, _jet2, null, jetRot)) == PlaceError.None
              && Building.Place(s, Footprint.AnchorFor(_door2, side, new Int3(-4, 1, -4), Dir.PosY), Building.Make(s, _door2, null, side)) == PlaceError.None,
            $"a door on its side and a thruster lying along x ({Orientations.Name(jetRot)}) go down");
        Building.Toggle(s, s.Blocks().First(b => b.block.Type == _door2.Index).pos);
        string text = Blueprint.Save(s);
        Structure back = Blueprint.Load(text, _mc, out List<string> errors);
        int lines = text.Split('\n').Count(l => l.Contains(MultiCellParts.Container) || l.Contains(MultiCellParts.Door) || l.Contains(MultiCellParts.Thruster));
        Check(errors.Count == 0 && lines == 3, $"saved, each block is one line, at its anchor ({lines} lines; {string.Join("; ", errors)})");
        bool same = back != null && back.Count == s.Count && back.BlockCount == s.BlockCount
            && s.Cells().All(c => back.TryGet(c.pos, out Block o) && o.Type == c.block.Type && o.Rot == c.block.Rot && o.ToAnchor == c.block.ToAnchor && o.Flags == c.block.Flags);
        Check(same, "loaded, every filler is rebuilt where it was, the door still open");
        Check(back != null && Blueprint.Save(back) == text, "save, load, save is byte-identical");
        RoomReport ra = Rooms.Detect(s), rb = Rooms.Detect(back);
        Check(back != null && ra.Rooms.Count == rb.Rooms.Count && StructureStats.Compute(back).Containers == 1
              && NavGrid.Build(back).Nodes.Count == NavGrid.Build(s).Nodes.Count,
            "rooms, stats and navigation come out the same after the round trip");

        string clash = text.Replace("block 2 1 2 container-2x2x2", "block 2 1 2 container-2x2x2\nblock 3 2 3 steel");
        Check(Blueprint.Load(clash, _mc, out errors) == null && errors.Any(e => e.Contains("overlaps")),
            $"a block in one of its cells is a load error ({string.Join("; ", errors)})");
        string clash2 = text + "block 3 1 1 container-2x2x2\n";
        Check(Blueprint.Load(clash2, _mc, out errors) == null && errors.Any(e => e.Contains("overlaps") && e.Contains("2x2x2")),
            $"two of them overlapping is a load error ({string.Join("; ", errors)})");
    }

    /// <summary>Its cells are solid to walkers; a 2×2×1 door is a door for every cell of its opening.</summary>
    void TestMultiNav()
    {
        var s = new Structure(_mc, _mc.Grid(GridSize.Large));
        BlockType steel = _mc.Type("steel");
        for (int x = -3; x <= 3; x++)
            for (int z = -3; z <= 3; z++)
                s.Set(new Int3(x, 0, z), Building.Make(s, x == 0 && z == 0 ? _mc.CoreType : steel));
        // A wall across z = 1, 3 high, with a 2×2×1 door at x 0..1.
        for (int x = -3; x <= 3; x++)
            for (int y = 1; y <= 3; y++)
                if (!(x is 0 or 1 && y <= 2)) s.Set(new Int3(x, y, 1), Building.Make(s, steel));
        s.Set(new Int3(0, 1, 1), Building.Make(s, _door2));
        NavGrid nav = NavGrid.Build(s, 0.9f * s.Grid.CellSize);
        Check(nav.Nodes.TryGetValue(new Int3(0, 1, 1), out NavKind a) && a == NavKind.Door && nav.Nodes.TryGetValue(new Int3(1, 1, 1), out NavKind b) && b == NavKind.Door,
            "both cells of its doorway at the floor are door nodes");
        List<Int3> path = nav.FindPath(new Int3(-2, 1, -2), new Int3(2, 1, 3));
        Check(path != null && path.Any(p => p.Z == 1 && p.X is 0 or 1), $"A* walks through it ({path?.Count ?? 0} steps)");

        s.Set(new Int3(-2, 1, -1), Building.Make(s, _box2));
        nav = NavGrid.Build(s, 0.9f * s.Grid.CellSize);
        Check(!nav.Nodes.ContainsKey(new Int3(-2, 1, -1)) && !nav.Nodes.ContainsKey(new Int3(-1, 1, 0)) && nav.Nodes.ContainsKey(new Int3(-1, 3, 0)),
            "a container's cells are solid, and its top is floor");
    }
}
