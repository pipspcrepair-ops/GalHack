using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;

namespace BlockBuilding;

/// <summary>
/// Headless test of the structure core and the chunk mesher. Run with:
///   godot --headless --path prototypes/building res://tests/BuildTest.tscn
/// Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class BuildTest : Node
{
    int _passes, _failures;
    Catalogue _cat;
    BlockType _base, _steel, _hardened, _combat, _window, _glass, _core, _thruster, _generator, _container, _door, _ladder;

    public override void _Ready()
    {
        _cat = GameData.Catalogue;
        _base = _cat.Type("base-block");
        _steel = _cat.Type("iron-plated");
        _hardened = _cat.Type("hardened-plated");
        _combat = _cat.Type("combat-plated");
        _window = _cat.Type("window-block");
        _glass = _cat.Type("glass-plated");
        _core = _cat.CoreType;
        _thruster = _cat.Type("thruster-t1");
        _generator = _cat.Type("generator-t1");
        _container = _cat.Type("container-t1");
        _door = _cat.Type("door-1x2x1");
        _ladder = _cat.Type("ladder");
        GD.Print($"Catalogue: {_cat}");

        Run("Placement rules", TestPlacement);
        Run("Symmetry", TestSymmetry);
        Run("Removal", TestRemoval);
        Run("Severing", TestSevering);
        Run("One grid", TestOneGrid);
        Run("Mass, power and thrust", TestStats);
        Run("Shapes", TestShapes);
        Run("Upgrading in place", TestUpgrade);
        Run("Plating", TestPlating);
        Run("Device ladders", TestLadders);
        Run("Paint", TestPaint);
        Run("Sealed rooms", TestRooms);
        Run("Old ids and old grids", TestOldBlueprints);
        Run("Shared storage", TestStorage);
        Run("Blueprints", TestBlueprints);
        Run("Validator", TestValidator);
        Run("Core mode", TestCoreMode);
        Run("Core mode never touches blocks", TestModeKeepsEveryBlock);
        Run("Navigation", TestNav);
        Run("Meshing performance", TestMeshing);
        Run("Device detail", TestDeviceDetail);
        MultiCellTests();

        GD.Print($"\n{_passes} passed, {_failures} failed");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---- Device detail --------------------------------------------------------------------

    /// <summary>
    /// Every device gets detail from its function's rule, looks only (no solids, no cover); a screen's
    /// symbol is placed on its own face; detail on a covered face is left out; the detail costs a few
    /// hundred triangles a device at most.
    /// </summary>
    void TestDeviceDetail()
    {
        foreach (BlockType t in _cat.Types.Skip(1).Where(t => !t.IsArmor && !t.IsLadder))
            Check(t.Detail.Count > 0, $"{t.Id} has detail from its rule ({t.Detail.Count} parts)");

        // A screen's symbol: pattern 7, the symbol in the stencil bits, and the accent's centre inside the polygon.
        BlockType chair = _cat.Type("command-chair");
        var s = new Structure(_cat);
        s.Set(Int3.Zero, Building.Make(s, chair));
        MeshBuffers m = ChunkMesher.Build(s, Int3.Zero).Opaque;
        int screens = 0, centred = 0;
        for (int i = 0; i < m.Indices.Count; i += 3)
        {
            int v0 = m.Indices[i];
            int flags = (int)m.UV2s[v0].Y, accent = (int)m.UV2s[v0].X;
            if ((flags & 7) != DetailPattern.Symbol || ((flags >> BlockMaterials.StencilShift) & 15) != (int)DeviceSymbol.Chevron) continue;
            screens++;
            float cu = ((accent >> 16) & 255) / 255f, cv = ((accent >> 8) & 255) / 255f;
            Vector2 a = Frac(m.UVs[v0]), b = Frac(m.UVs[m.Indices[i + 1]]), c = Frac(m.UVs[m.Indices[i + 2]]);
            float u0 = Mathf.Min(a.X, Mathf.Min(b.X, c.X)) - 0.01f, u1 = Mathf.Max(a.X, Mathf.Max(b.X, c.X)) + 0.01f;
            float w0 = Mathf.Min(a.Y, Mathf.Min(b.Y, c.Y)) - 0.01f, w1 = Mathf.Max(a.Y, Mathf.Max(b.Y, c.Y)) + 0.01f;
            if (cu >= u0 && cu <= u1 && cv >= w0 && cv <= w1) centred++;
        }
        Check(screens > 0 && centred == screens, $"the chair's console shows its symbol on its own faces ({centred} of {screens} triangles)");

        // Detail on a face a neighbour covers is left out.
        BlockType generator = _generator;
        var lone = new Structure(_cat);
        lone.Set(Int3.Zero, Building.Make(lone, generator));
        int alone = Triangles(lone);
        lone.Set(new Int3(1, 0, 0), Building.Make(lone, _steel));
        lone.Set(new Int3(-1, 0, 0), Building.Make(lone, _steel));
        int flanked = Triangles(lone) - 2 * 10;
        Check(flanked < alone - 100, $"a generator's fins go when steel covers its flanks ({alone} triangles alone, {flanked} flanked)");

        foreach (BlockType t in _cat.Types.Skip(1).Where(t => !t.IsArmor))
        {
            var one = new Structure(_cat);
            one.Set(Int3.Zero, Building.Make(one, t));
            int n = Triangles(one);
            Check(n <= 1000, $"{t.Id} stays cheap: {n} triangles");
        }
    }

    static Vector2 Frac(Vector2 v) => new(v.X - Mathf.Floor(v.X), v.Y - Mathf.Floor(v.Y));

    static int Triangles(Structure s)
    {
        int n = 0;
        foreach (Int3 c in s.Chunks.Keys)
        {
            ChunkMeshData d = ChunkMesher.Build(s, c);
            n += (d.Opaque.Indices.Count + d.Glass.Indices.Count) / 3;
        }
        return n;
    }

    // ---- Placement ------------------------------------------------------------------------

    void TestPlacement()
    {
        var empty = new Structure(_cat);
        Check(Building.Check(empty, Int3.Zero, Block(empty, _steel)) == PlaceError.StartWithCore,
            "an empty structure only takes a core (the starter block)");

        Structure s = NewShip();
        Check(s.Owner == Building.DefaultOwner && s.Core == Int3.Zero, "placing the core starts the structure and sets its owner");
        Check(Place(s, 1, 0, 0, _steel) == PlaceError.None, "a block attaches to the core through a shared face");
        Check(Place(s, 1, 0, 0, _steel) == PlaceError.Occupied, "a second block in an occupied cell is rejected (no overlaps)");
        Check(Place(s, 5, 5, 5, _steel) == PlaceError.NotAttached, "a floating block is rejected (must attach)");
        Check(Place(s, -1, 0, 0, _core) == PlaceError.OneCoreOnly, "a second core is rejected");

        Check(Place(s, 2, 0, 0, _steel, "half") == PlaceError.None, "a half block attaches by its partial side face");
        Check(Place(s, 2, 1, 0, _steel) == PlaceError.NoSharedFace,
            "a block on a half block's open top is rejected: no shared face");
        Check(Place(s, 1, 0, -1, _steel, "slope", "+y+z") == PlaceError.NoSharedFace, "a slope can't attach by its open front");
        Check(Place(s, 1, 0, -1, _steel, "slope") == PlaceError.None, "a slope attaches by its full back face");
        Check(s.Count == 4, $"rejected placements left no trace ({s.Count} blocks)");
    }

    // ---- Symmetry -------------------------------------------------------------------------

    void TestSymmetry()
    {
        Structure s = NewShip();
        var sym = Symmetry.Through(Int3.Zero);
        sym.X = true;

        var placed = Building.PlaceSymmetric(s, new Int3(1, 0, 0), Block(s, _steel), sym);
        Check(placed.Count == 2 && placed.All(p => p.error == PlaceError.None) && s.Has(new Int3(-1, 0, 0)),
            "an X mirror places the copy on the far side");

        Orientations.TryParse("+y+x", out int facingRight);
        var slope = Block(s, _steel, "slope", facingRight);
        slope.Paint[Dir.PosX] = s.Palette.Intern(Paint.Solid(0xe85d04));
        Building.PlaceSymmetric(s, new Int3(2, 0, 0), slope, sym);
        Block a = s.Get(new Int3(2, 0, 0)), b = s.Get(new Int3(-2, 0, 0));
        Check(Orientations.Name(b.Rot) == "+y-x", $"a slope facing +x mirrors to one facing -x (got {Orientations.Name(b.Rot)})");
        bool facesMirror = Dirs.All.All(d => s.AttachCoverage(a, d) == s.AttachCoverage(b, d.Mirror(0)));
        Check(facesMirror, "every face of the mirrored slope matches the original's mirror image");
        Check(b.Paint[Dir.NegX] == a.Paint[Dir.PosX] && b.Paint[Dir.PosX] == 0, "face paint mirrors with the block");

        // Corners have no left-right symmetry of their own axis, only a diagonal one: still exact.
        Orientations.TryParse("+y-z", out int fwd);
        var corner = Block(s, _steel, "corner", fwd);
        Block cm = Building.MirrorBlock(s, corner, 0);
        Check(Dirs.All.All(d => s.AttachCoverage(corner, d) == s.AttachCoverage(cm, d.Mirror(0))),
            "a corner mirrors exactly through its diagonal symmetry");

        var all = Symmetry.Through(Int3.Zero);
        all.X = all.Y = all.Z = true;
        var copies = Building.MirrorCopies(s, new Int3(2, 3, 4), Block(s, _steel), all);
        var expected = new HashSet<Int3>();
        foreach (int x in new[] { 2, -2 }) foreach (int y in new[] { 3, -3 }) foreach (int z in new[] { 4, -4 }) expected.Add(new Int3(x, y, z));
        Check(copies.Count == 8 && copies.Select(c => c.Pos).ToHashSet().SetEquals(expected), "three planes make eight copies");
        Check(Building.MirrorCopies(s, new Int3(0, 3, 4), Block(s, _steel), all).Count == 4, "a block on a plane is not doubled");

        var between = new Symmetry { X = true, X2 = 1 };
        Check(between.Mirror(new Int3(3, 0, 0), 0) == new Int3(-2, 0, 0), "a plane between cells mirrors 3 to -2 (even-width hulls)");
    }

    // ---- Removal --------------------------------------------------------------------------

    void TestRemoval()
    {
        Structure s = NewShip();
        Place(s, 1, 0, 0, _hardened, "slope");
        var inventory = new Inventory();
        var cost = _cat.CostOf(s.Get(new Int3(1, 0, 0)));
        RemoveResult r = Building.Remove(s, new Int3(1, 0, 0), inventory);
        Check(r.Status == RemoveStatus.Removed && !s.Has(new Int3(1, 0, 0)), "the multi-tool removes the block");
        Check(cost.All(kv => inventory[kv.Key] == kv.Value) && inventory["hardened-plate"] == 2 && inventory["polymer"] == 1,
            $"its materials go to the inventory, scaled by shape volume ({string.Join(", ", inventory.Items.Select(kv => $"{kv.Value} {kv.Key}"))})");

        Place(s, 1, 0, 0, _steel);
        Check(Building.Remove(s, Int3.Zero, inventory).Status == RemoveStatus.CoreHoldsStructure && s.Core != null,
            "the core cannot be taken down while blocks hang off it");
        Building.Remove(s, new Int3(1, 0, 0), inventory);
        Check(s.Count == 1 && Building.Remove(s, Int3.Zero, inventory).Status == RemoveStatus.CoreHoldsStructure && s.Core != null,
            "nor as the last block: a core is only ever destroyed or hijacked");
    }

    // ---- Severing -------------------------------------------------------------------------

    void TestSevering()
    {
        Structure s = NewShip();
        for (int x = 1; x <= 4; x++) Place(s, x, 0, 0, _steel);
        Place(s, 4, 1, 0, _container);
        Place(s, 3, 0, 1, _thruster);
        Building.PaintFace(s, new Int3(4, 0, 0), Dir.PosX, Paint.Solid(0xc1121f).WithPattern(Pattern.Hazard, 0x2b2d42));
        s.CargoSu = 5000;
        int before = s.Count;

        var report = new DamageReport();
        Damage.Hit(s, new Int3(2, 0, 0), DamageType.Kinetic, 100_000, report);
        report.Wrecks.AddRange(Connectivity.SplitOff(s));
        Structure wreck = report.Wrecks.SingleOrDefault();
        Check(report.Destroyed.Count == 1 && wreck != null, "destroying the only link breaks the far section off as one wreck");
        if (wreck == null) return;
        Check(s.Count + wreck.Count + report.Destroyed.Count == before,
            $"total block count is conserved ({s.Count} + {wreck.Count} + 1 destroyed = {before})");
        Check(s.Owner == Building.DefaultOwner && s.Core == Int3.Zero, "the core side keeps its owner");
        Check(wreck.Owner == null && wreck.Core == null && wreck.IsWreck, "the wreck is unowned and has no core");
        Check(Math.Abs(s.CargoSu + wreck.CargoSu - 5000) < 1e-9 && wreck.CargoSu == 5000,
            "cargo is conserved: it went with the container that broke away");
        Check(Building.PaintOf(wreck, new Int3(4, 0, 0), Dir.PosX)?.Pattern == Pattern.Hazard, "the wreck keeps its paint");
        Check(!StructureStats.Compute(wreck).Flies, "the wreck's components stop working (no core)");

        Check(Building.Place(wreck, new Int3(5, 0, 0), Block(wreck, _core), owner: "salvager") == PlaceError.None
              && wreck.Owner == "salvager" && !wreck.IsWreck, "placing a core claims the wreck");

        // The multi-tool can cut too: the cut-off part becomes a wreck rather than vanishing.
        Structure t = NewShip();
        for (int x = 1; x <= 3; x++) Place(t, x, 0, 0, _steel);
        Check(Building.WouldSever(t, new Int3(1, 0, 0), out int cut) && cut == 2, "the tool knows a removal would cut 2 blocks off");
        RemoveResult r = Building.Remove(t, new Int3(1, 0, 0), new Inventory());
        Check(r.Wrecks.Count == 1 && r.Wrecks[0].Count == 2 && t.Count == 1, "removing a link with the tool also splits, losing nothing");
    }

    // ---- Stats ----------------------------------------------------------------------------

    void TestStats()
    {
        Structure s = NewShip();
        for (int x = 1; x <= 10; x++) Place(s, x, 0, 0, _steel);
        Place(s, 1, 1, 0, _hardened, "slope");
        Place(s, 2, 1, 0, _hardened, "slope");
        Place(s, 0, 0, 1, _thruster);
        Place(s, 0, 1, 0, _generator);
        Place(s, -1, 0, 0, _container);
        StructureStats st = StructureStats.Compute(s);

        double mass = _core.Mass + 10 * _steel.Mass + 2 * _hardened.Mass * 0.5 + _thruster.Mass + _generator.Mass + _container.Mass;
        Check(Near(st.Mass, mass, 1e-5), $"mass is the sum of the blocks ({st.Mass:0.###} t, expected {mass:0.###} t)");
        s.CargoSu = 2000;
        Check(Near(StructureStats.Compute(s).Mass, mass + 2, 1e-5), "cargo adds to the mass (2000 SU = 2 t)");
        s.CargoSu = 0;
        Check(Near(st.Thrust[(int)Dir.NegZ], _thruster.Thrust) && Near(st.ThrustToWeightForward, _thruster.Thrust / (mass * 9.81)),
            $"thrust-to-mass is right ({st.ThrustToWeightForward:0.00} g forward)");

        // No CPU: however many devices, each runs in full; power is the only limit.
        Structure big = NewShip();
        for (int x = 1; x <= 60; x++) Place(big, x, 0, 0, _generator);
        StructureStats many = StructureStats.Compute(big);
        Check(Near(many.PowerOut, 60 * _generator.Power, 1e-5), $"60 generators make all their power ({many.PowerOut:0.#} MW)");


        var hit = new DamageReport();
        Damage.Hit(s, new Int3(0, 0, 1), DamageType.Explosive, 100_000, hit);
        Check(hit.Destroyed.Count == 1 && StructureStats.Compute(s).Thrust.Sum() == 0, "a destroyed thruster stops pushing");
    }

    // ---- Shapes ---------------------------------------------------------------------------

    void TestShapes()
    {
        Structure s = NewShip();
        BlockStats cube = s.StatsOf(Block(s, _combat));
        var volumes = new Dictionary<string, float>
        {
            ["cube"] = 1f, ["slope"] = 0.5f, ["corner"] = 1f / 6f, ["invcorner"] = 5f / 6f,
            ["wedge"] = 0.25f, ["half"] = 0.5f, ["plate"] = 0.125f,
        };
        foreach (var (id, v) in volumes)
            Check(Near(_cat.Shape(id).Volume, v, 1e-4), $"{id} has volume {v:0.###}");
        float cyl = _cat.Shape("cylinder").Volume;
        Check(cyl > 0.76f && cyl < 0.786f, $"cylinder volume is about Ï€/4 ({cyl:0.###})");

        bool scaled = true, same = true;
        foreach (ShapeDef shape in _cat.Shapes)
        {
            BlockStats st = s.StatsOf(Block(s, _combat, shape.Id));
            scaled &= Near(st.MaxHp, cube.MaxHp * shape.Volume, 1e-3) && Near(st.Mass, cube.Mass * shape.Volume, 1e-4);
            same &= st.Armor == cube.Armor && st.Resist == cube.Resist;
        }
        Check(scaled, "every shape's hit points and mass scale with its volume");
        Check(same, "every shape keeps the material's armour and resistances");
        Check(_cat.Shape("slope").Face(Dir.NegY) == Coverage.Full && _cat.Shape("slope").Face(Dir.PosZ) == Coverage.Full
              && _cat.Shape("slope").Face(Dir.PosX) == Coverage.Partial && _cat.Shape("slope").Face(Dir.PosY) == Coverage.None,
            "a slope has a full bottom and back, triangular sides and no top");

        Check(Near(s.StatsOf(Block(s, _steel)).MaxHp, 158) && Near(s.StatsOf(Block(s, _base)).MaxHp, 63),
            "a 1 m iron-plated cube has 158 hit points, a bare base block 63");
        Check(Near(s.StatsOf(Block(s, _hardened)).Resist.Kinetic, 0.45) && Near(s.StatsOf(Block(s, _combat)).Resist.Explosive, 0.55),
            "hardened leans to kinetic, combat to explosive");
    }

    // ---- Upgrade --------------------------------------------------------------------------

    void TestUpgrade()
    {
        Structure s = NewShip();
        Orientations.TryParse("+z+x", out int rot);
        Place(s, 1, 0, 0, _steel, "slope", "+z+x");
        var p = new Int3(1, 0, 0);
        Building.PaintFace(s, p, Dir.PosX, Paint.Solid(0xe85d04).WithPattern(Pattern.Chevrons, 0xf4a100));
        Building.PaintFace(s, p, Dir.PosY, Paint.Solid(0xe9dfc4).WithStencil('7', 0x2b2d42));
        Block before = s.Get(p);

        Check(Building.Upgrade(s, p) == UpgradeStatus.Upgraded, "iron plating upgrades to hardened");
        Block after = s.Get(p);
        Check(s.TypeOf(after) == _hardened && after.Shape == before.Shape && after.Rot == rot && after.Paint.Equals(before.Paint),
            "the upgrade keeps the shape, the orientation and the paint");
        Check(Near(after.Hp, _hardened.Hp * 0.5), $"and comes back at the new plating's full health ({after.Hp})");
        Check(Building.Upgrade(s, p) == UpgradeStatus.Upgraded && s.TypeOf(s.Get(p)) == _combat, "hardened upgrades to combat");
        Check(Building.Upgrade(s, p) == UpgradeStatus.AlreadyBest, "combat is the top");
        Check(Building.Upgrade(s, Int3.Zero) == UpgradeStatus.NotArmor, "components are not armour");

        var hit = new DamageReport();
        Damage.Hit(s, p, DamageType.Energy, 200, hit);
        Check(Building.IsDamaged(s, p) && Building.Repair(s, p) && !Building.IsDamaged(s, p), "the multi-tool repairs a damaged block");
    }

    // ---- One grid, plating, ladders --------------------------------------------------------

    void TestOneGrid()
    {
        GridDef g = _cat.Grid;
        Check(g.Id == "1m" && g.CellSize == 1f && g.StorageCap == 320_000, $"one grid: 1 m cells, 320,000 SU of storage ({g.Id}, {g.CellSize} m)");
        Check(NewShip().Grid == g && Blueprint.Save(NewShip()).Contains("grid 1m"), "every structure is on it, and its blueprints say so");
    }

    /// <summary>
    /// Plating: plates sprayed onto a base block make it a plated block; spraying another plating
    /// or stripping it gives the old plates back by the block's hit-point share, rounded down; a
    /// repair takes plates in proportion to the damage; glass plates go on windows only.
    /// </summary>
    void TestPlating()
    {
        Structure s = NewShip();
        var p = new Int3(1, 0, 0);
        Place(s, 1, 0, 0, _base);
        var bag = new Inventory { Creative = false };
        Check(Building.Plate(s, p, _steel, bag) == UpgradeStatus.NotEnoughMaterials && s.TypeOf(s.Get(p)) == _base, "plating takes plates");
        bag.Add(new() { ["iron-plate"] = 4, ["hardened-plate"] = 4 });
        Check(Building.Plate(s, p, _steel, bag) == UpgradeStatus.Upgraded && s.TypeOf(s.Get(p)) == _steel && bag["iron-plate"] == 0 && Near(s.Get(p).Hp, 158),
            "four iron plates make a base block an iron-plated one, whole");
        Check(_cat.CostOf(s.Get(p)).OrderBy(kv => kv.Key).SequenceEqual(new Dictionary<string, int> { ["iron-plate"] = 4, ["polymer"] = 1 }.OrderBy(kv => kv.Key)),
            "its bill is the base block's plus its plates");

        var hit = new DamageReport();
        Damage.Hit(s, p, DamageType.Kinetic, s.Get(p).Hp * 0.6f + _steel.Armor / (1 - _steel.Resist.Kinetic), hit);
        float share = s.Get(p).Hp / 158f;
        Check(Building.Plate(s, p, _hardened, bag) == UpgradeStatus.Upgraded && s.TypeOf(s.Get(p)) == _hardened
              && bag["iron-plate"] == (int)MathF.Floor(4 * share) && bag["hardened-plate"] == 0,
            $"spraying hardened over damaged iron gives back the iron's worth rounded down ({bag["iron-plate"]} plates at {share:P0})");

        Damage.Hit(s, p, DamageType.Kinetic, s.Get(p).Hp * 0.5f + _hardened.Armor / (1 - _hardened.Resist.Kinetic), hit);
        Dictionary<string, int> repair = Building.RepairCost(s, p);
        float damage = 1 - s.Get(p).Hp / _hardened.Hp;
        Check(repair.Count == 1 && repair["hardened-plate"] == (int)MathF.Ceiling(4 * damage), $"a repair takes plates in proportion to the damage ({repair["hardened-plate"]} for {damage:P0})");

        share = s.Get(p).Hp / _hardened.Hp;
        var back = new Inventory();
        Check(Building.Strip(s, p, back) == UpgradeStatus.Upgraded && s.TypeOf(s.Get(p)) == _base && back["hardened-plate"] == (int)MathF.Floor(4 * share)
              && Near(s.Get(p).Hp, _base.Hp * share, 1e-4),
            $"stripping leaves the base block at its share, and the plates' worth rounded down ({back["hardened-plate"]})");
        Check(Building.Strip(s, p) == UpgradeStatus.AlreadyBest, "a bare block has nothing to strip");

        Place(s, 2, 0, 0, _base, "plate");
        var slab = new Int3(2, 0, 0);
        Building.Plate(s, slab, _steel);
        Check(_cat.PlatesOf(s.Get(slab)) == 1 && _cat.PlatesOf(Block(s, _steel, "half")) == 2, "a shape takes plates by its volume, never fewer than one (plate 1, half 2)");
        Check(Building.Plate(s, slab, _glass) == UpgradeStatus.WrongBase, "glass plates go on windows only");
        Place(s, 3, 0, 0, _window);
        Check(Building.Upgrade(s, new Int3(3, 0, 0)) == UpgradeStatus.Upgraded && s.TypeOf(s.Get(new Int3(3, 0, 0))) == _glass, "a window upgrades to glass plating");
        Check(Building.Plate(s, Int3.Zero, _steel) == UpgradeStatus.NotArmor, "devices aren't plated yet");
    }

    /// <summary>Every device rung loads with its footprint; each family is one ladder, named by tier, and a door family is one item.</summary>
    void TestLadders()
    {
        var sizes = new Dictionary<string, Int3>
        {
            ["thruster-t1"] = new(1, 1, 1), ["thruster-t2"] = new(1, 1, 2), ["thruster-t3"] = new(1, 1, 2), ["thruster-t4"] = new(2, 2, 4), ["thruster-t5"] = new(3, 3, 6),
            ["generator-t1"] = new(1, 1, 1), ["generator-t2"] = new(1, 2, 1), ["generator-t3"] = new(3, 1, 1), ["generator-t4"] = new(6, 2, 2),
            ["fuel-tank-t1"] = new(1, 1, 1), ["fuel-tank-t2"] = new(1, 1, 2), ["fuel-tank-t3"] = new(1, 2, 1), ["fuel-tank-t4"] = new(2, 2, 1),
            ["container-t1"] = new(1, 1, 1), ["container-t2"] = new(1, 1, 1), ["container-t3"] = new(1, 1, 1), ["container-t4"] = new(2, 1, 1),
            ["command-chair"] = new(1, 2, 3), ["cockpit-t1"] = new(3, 2, 4), ["cockpit-t2"] = new(3, 3, 5), ["core"] = new(1, 1, 1),
            ["door-1x2x1"] = new(1, 2, 1), ["door-2x2x1"] = new(2, 2, 1), ["door-3x3x1"] = new(3, 3, 1), ["door-4x4x1"] = new(4, 4, 1), ["door-5x5x1"] = new(5, 5, 1),
        };
        foreach (var (id, size) in sizes)
        {
            bool ok = _cat.TryGetType(id, out BlockType t) && t.Id == id && t.Size == size;
            Structure s = NewShip();
            if (ok) s.Set(new Int3(0, 1, 0), Building.Make(s, t));
            Check(ok && s.Count == 1 + t.Cells && s.BlockCount == 2, $"{id} loads as {size.X}x{size.Y}x{size.Z} cells and sets whole");
        }
        foreach (string family in new[] { "thruster", "generator", "fuel-tank", "container" })
        {
            var rungs = _cat.Types.Skip(1).Where(t => t.Id.StartsWith(family + "-t")).ToList();
            Check(rungs.Count >= 4 && rungs.Select(t => t.Name).Distinct().Count() == rungs.Count && rungs.All(t => t.Name.Contains($"T{t.Id[^1]}")),
                $"{family}: {rungs.Count} rungs, named by tier");
        }
        Check(_cat.Types.Skip(1).All(t => !t.Name.Contains("small", StringComparison.OrdinalIgnoreCase) && !t.Name.Contains("large", StringComparison.OrdinalIgnoreCase)),
            "no block is named small or large");
        IReadOnlyList<BlockType> doors = _cat.VariantsOf(_door);
        Check(doors.Count == 5 && doors.All(d => d.Head == _door && d.IsDoor) && doors.Select(d => d.Units).SequenceEqual(new[] { 1, 2, 5, 8, 13 }),
            "the doors are one family under the 1x2x1 door, a bigger one taking more of its item (1, 2, 5, 8, 13)");
        BlockType chair = _cat.Type("command-chair");
        Check(_cat.Types.Skip(1).Where(t => t.IsCommandChair).All(t => t.Eye.Y > t.Seat.Y && t.Seat.Y > -0.5f && t.Eye.Y < 0.5f)
              && Near((chair.Eye.Y - chair.Seat.Y) * chair.Size.Y, 0.8, 1e-3),
            "every chair and cockpit seats a person: eyes 0.8 m over the seat, inside the block");
    }

    // ---- Paint ----------------------------------------------------------------------------

    void TestPaint()
    {
        Structure s = NewShip();
        for (int x = 1; x <= 5; x++) Place(s, x, 0, 0, _steel);
        var paints = new[]
        {
            Paint.Solid(0xe9dfc4).WithPattern(Pattern.Stripes, 0xe85d04),
            Paint.Solid(0x0a9396).WithPattern(Pattern.Chevrons, 0xf4a100) with { Grime = 2 },
            Paint.Solid(0x2b2d42).WithPattern(Pattern.Checker, 0xe9dfc4) with { Texture = FaceTexture.Plates },
            Paint.Solid(0xf4a100).WithPattern(Pattern.Hazard, 0x1b1b1b) with { Texture = FaceTexture.Ribbed },
            Paint.Solid(0xe9dfc4).WithStencil('4', 0xc1121f) with { Grime = 1, Texture = FaceTexture.Smooth },
        };
        for (int x = 1; x <= 5; x++)
            foreach (Dir d in Dirs.All)
                Building.PaintFace(s, new Int3(x, 0, 0), d, paints[(x + (int)d) % paints.Length]);
        Building.PaintFace(s, new Int3(3, 0, 0), Dir.NegY, null);

        Structure loaded = Blueprint.Load(Blueprint.Save(s), _cat, out var errors);
        bool same = loaded != null && s.Blocks().All(x => Dirs.All.All(d =>
            Building.PaintOf(s, x.pos, d) == Building.PaintOf(loaded, x.pos, d)));
        Check(errors.Count == 0 && same, "per-face colour, pattern, texture, stencil and grime survive a save and load");
    }

    // ---- Rooms ----------------------------------------------------------------------------

    void TestRooms()
    {
        Structure s = HollowBox();
        RoomReport r = Rooms.Detect(s);
        Check(r.Sealed.Count() == 1 && r.Sealed.First().Cells.Count == 27 && !r.Leaking.Any(),
            $"a closed 5Ã—5Ã—5 box holds one sealed 27-cell room ({r.Sealed.Count()} sealed)");

        var hole = new Int3(4, 2, 2);
        Block wall = s.Get(hole);
        s.Clear(hole);
        r = Rooms.Detect(s);
        List<Face> leaks = r.LeakFaces.ToList();
        Check(!r.Sealed.Any() && r.Leaking.Count() == 1 && leaks.Count == 1 && leaks[0] == new Face(hole, Dir.PosX),
            $"removing one wall block reports the leak at exactly that face ({string.Join(", ", leaks.Select(f => $"{f.Cell} {f.Dir.Name()}"))})");

        var holes = new[] { new Int3(0, 2, 2), new Int3(2, 4, 1) };
        foreach (Int3 h in holes) s.Clear(h);
        r = Rooms.Detect(s);
        Check(r.LeakFaces.Count() == 3, $"three holes report three leaking faces ({r.LeakFaces.Count()})");
        foreach (Int3 h in holes) s.Set(h, wall);
        s.Set(hole, wall);

        s.Set(new Int3(2, 1, 3), Block(s, _ladder));
        s.Set(new Int3(1, 1, 1), Block(s, _container));
        r = Rooms.Detect(s);
        Check(r.Sealed.Count() == 1 && !r.Leaking.Any() && r.Sealed.First().Cells.Contains(new Int3(2, 1, 3)),
            "a ladder and a container inside don't break the seal (only the boundary matters)");

        Orientations.TryParse("+y+x", out int facingOut);
        var doorAt = new Int3(4, 1, 2);
        s.Set(doorAt, Block(s, _door, rot: facingOut));
        r = Rooms.Detect(s);
        Check(r.Sealed.Count() == 1 && !r.Leaking.Any(), "a closed door seals");
        Building.Toggle(s, doorAt);
        r = Rooms.Detect(s);
        Check(!r.Sealed.Any() && r.LeakFaces.OrderBy(f => f.Cell.Y).SequenceEqual(new[] { new Face(doorAt, Dir.PosX), new Face(doorAt + Int3.UnitY, Dir.PosX) }),
            "an open 1x2x1 door leaks, at its doorway");

        Building.Toggle(s, doorAt);
        s.Set(new Int3(2, 2, 4), Block(s, _glass));
        s.Set(new Int3(2, 3, 4), Block(s, _window));
        Check(Rooms.Detect(s).Sealed.Count() == 1, "windows, bare or glass plated, are airtight");
    }

    /// <summary>
    /// Old ids read as the blocks that replaced them, and blueprints from the 2 m and 0.5 m grids load on
    /// the 1 m grid as they are, cell for cell: a door is 1x2x1 now, so the wall over an old one-cell door
    /// gives way to it, and a block running into a device is left out.
    /// </summary>
    void TestOldBlueprints()
    {
        var was = new (string old, string now)[]
        {
            ("steel", "iron-plated"), ("hardened", "hardened-plated"), ("combat", "combat-plated"), ("glass", "glass-plated"),
            ("concrete", "concrete-plated"), ("cpuext", "iron-plated"), ("cpuext3", "iron-plated"), ("cpuext4", "iron-plated"),
            ("thruster", "thruster-t1"), ("generator", "generator-t1"), ("fueltank", "fuel-tank-t1"), ("container", "container-t1"),
            ("door", "door-1x2x1"), ("chair", "command-chair"), ("cockpit", "command-chair"),
        };
        foreach (var (old, now) in was)
            Check(_cat.TryGetType(old, out BlockType t) && t.Id == now, $"old id '{old}' reads as {now}");

        string Text(string grid, params string[] blocks) => string.Join('\n', new[] { "schema galhack.blueprint", "version 1", "name \"Door\"", $"grid {grid}", "mode base",
            "block 0 0 0 core", "block 1 0 0 steel", "block 2 0 0 steel", "block 0 1 0 steel", "block 2 1 0 steel" }.Concat(blocks).Append(""));
        foreach (string grid in Blueprint.OldGrids)
        {
            Structure old = Blueprint.Load(Text(grid, "block 1 1 0 door", "block 1 2 0 glass", "block 0 2 0 thruster", "block 0 2 0 steel"), _cat, out List<string> errors);
            Check(errors.Count == 0 && old.TypeOf(old.Get(new Int3(1, 1, 0))) == _door && old.AnchorOf(new Int3(1, 2, 0)) == new Int3(1, 1, 0)
                  && old.TypeOf(old.Get(new Int3(0, 2, 0))) == _thruster,
                $"a 'grid {grid}' blueprint loads on 1 m cells: its door grows to 1x2x1 over the glass, the steel after the thruster is left out ({string.Join("; ", errors)})");
            Check(Blueprint.Save(old).Contains("grid 1m"), "and saves as a 1 m blueprint");
        }
        Check(Blueprint.Load(Text("1m", "block 1 1 0 door", "block 1 2 0 glass"), _cat, out List<string> overlap) == null && overlap.Any(e => e.Contains("overlaps")),
            "on the 1 m grid the same overlap is an error");
    }

    /// <summary>A hollow 5Ã—5Ã—5 steel box from (0,0,0) to (4,4,4) with its core in the floor.</summary>
    Structure HollowBox()
    {
        var s = new Structure(_cat);
        for (int x = 0; x <= 4; x++)
            for (int y = 0; y <= 4; y++)
                for (int z = 0; z <= 4; z++)
                    if (x == 0 || y == 0 || z == 0 || x == 4 || y == 4 || z == 4)
                        s.Set(new Int3(x, y, z), Block(s, x == 2 && y == 0 && z == 2 ? _core : _steel));
        return s;
    }

    // ---- Storage --------------------------------------------------------------------------

    void TestStorage()
    {
        Structure s = NewShip();
        for (int x = 1; x <= 3; x++) Place(s, x, 0, 0, _container);
        StructureStats st = StructureStats.Compute(s);
        Check(st.StorageCapacity == 3 * _container.Storage && st.Containers == 3, $"three containers make one shared storage ({st.StorageCapacity} SU)");
        BlockType big = _cat.Type("container-t4");
        for (int x = 4; x <= 4 + 2 * 130; x += 2) Place(s, x, 0, 0, big);
        st = StructureStats.Compute(s);
        Check(st.StorageRaw > 320_000 && st.StorageCapacity == 320_000, $"{st.Containers} containers ({st.StorageRaw:N0} SU) are capped at 320,000 SU");
    }

    // ---- Blueprints -----------------------------------------------------------------------

    void TestBlueprints()
    {
        string example = GameData.ReadText(GameData.ExampleBlueprint);
        Structure ship = Blueprint.Load(example, _cat, out var errors);
        Check(ship != null, $"the hand-written example ship loads ({(errors.Count > 0 ? errors[0] : "no errors")})");
        if (ship == null) return;
        StructureStats st = StructureStats.Compute(ship);
        RoomReport rooms = Rooms.Detect(ship);
        GD.Print($"      {ship.Name}: {ship.Count} blocks, {st.Mass:0} t, " +
                 $"{st.Thrusters} thrusters, {st.Generators} generators, {st.Containers} containers, " +
                 $"{rooms.Sealed.Count()} sealed rooms, {rooms.Leaking.Count()} leaking");
        Check(rooms.Sealed.Any() && !rooms.Leaking.Any(), "the example ship's cabin is sealed");
        Check(ship.Blocks().Any(b => ship.TypeOf(b.block).IsCommandChair) && st.Thrusters > 0 && st.Generators > 0 && st.Containers > 0,
            "it has a cockpit, thrusters, a generator and containers");

        string text1 = Blueprint.Save(ship);
        string text2 = Blueprint.Save(Blueprint.Load(text1, _cat, out _));
        Check(text1 == text2, $"a blueprint round-trips byte for byte ({text1.Length} bytes)");

        // Damage, doors, lights and cargo round-trip too.
        Structure s = NewShip();
        for (int x = 1; x <= 4; x++) Place(s, x, 0, 0, _steel);
        Place(s, 1, 1, 0, _door);
        Building.Toggle(s, new Int3(1, 2, 0));
        var hit = new DamageReport();
        Damage.Hit(s, new Int3(2, 0, 0), DamageType.Kinetic, 123.456f, hit);
        Damage.Hit(s, new Int3(3, 0, 0), DamageType.Kinetic, 123.43f, hit);
        s.CargoSu = 1234.5;
        string a = Blueprint.Save(s), b = Blueprint.Save(Blueprint.Load(a, _cat, out _));
        Check(a == b && a.Contains("open") && a.Contains("hp=") && a.Contains("cargo 1234.5"),
            "damage, open doors and cargo round-trip byte for byte");
    }

    void TestValidator()
    {
        const string head = "schema galhack.blueprint\nversion 1\nname \"t\"\ngrid 1m\n";
        Check(Blueprint.Validate(Blueprint.Parse(head + "block 0 0 0 core\nblock 1 0 0 steel slope rot=+y+x\n"), _cat).Count == 0,
            "a good blueprint passes");

        var cases = new (string text, string expect, string what)[]
        {
            (head + "block 0 0 0 steel\n", "no core", "no core"),
            (head + "block 0 0 0 core\nblock 1 0 0 core\n", "cores", "two cores"),
            (head + "block 0 0 0 core\nblock 1..3 0 0 steel\nblock 2 0 0 hardened\n", "overlaps", "overlapping blocks"),
            (head + "block 0 0 0 core\nblock 1 0 0 unobtanium\n", "unknown block id", "an unknown block id"),
            (head + "block 0 0 0 core\nblock 1 0 0 steel blob\n", "unknown shape", "an unknown shape"),
            (head + "block 0 0 0 core\nblock 1 0 0 steel paint=nope\n", "unknown paint", "an unknown paint"),
            (head + "block 0 0 0 core\nblock 1 0 0 steel rot=+y+y\n", "bad rot", "an impossible rotation"),
            (head + "block 0 0 0 core\nblock 1 0 0 thruster-t1 slope\n", "takes no shape", "a shape on a component"),
            (head + "block 0 0 0 core\nblock 1 0 0 steel\nblock 5 5 5 steel\n", "not attached", "a detached block marked as attached"),
            (head + "block 0 0 0 core\nblock 1 0 0 steel half\nblock 1 1 0 steel\n", "not attached", "a block touching only a half block's open top"),
            ("schema galhack.blueprint\nversion 9\ngrid 1m\nblock 0 0 0 core\n", "version", "an unknown version"),
            ("schema galhack.blueprint\nversion 1\ngrid 3m\nblock 0 0 0 core\n", "unknown grid", "an unknown grid"),
            (head + "block 0 0 0 core\nblock 1 0 0 steel hp=99999\n", "outside", "hit points above the maximum"),
            (head + "block 0 0 0 core\nwibble\n", "unknown statement", "a junk line"),
        };
        foreach (var (text, expect, what) in cases)
        {
            List<string> errors = Blueprint.Validate(Blueprint.Parse(text), _cat);
            Check(errors.Any(e => e.Contains(expect)), $"the validator rejects {what} ({(errors.Count > 0 ? errors[0] : "accepted!")})");
        }
    }

    // ---- Core mode ------------------------------------------------------------------------

    void TestCoreMode()
    {
        Structure s = NewShip();
        Place(s, 0, 0, 1, _thruster);
        Place(s, 0, 1, 0, _generator);
        Check(s.Mode == CoreMode.Capital && StructureStats.Compute(s).Flies, "a core in Capital Mode flies");
        Check(Building.ToggleCoreMode(s) && s.Mode == CoreMode.Base, "B switches the core to base");
        StructureStats st = StructureStats.Compute(s);
        Check(!st.Flies && st.Thrust.Sum() == 0 && st.Blocks == 3, "a base keeps every block but does not fly");
        Structure loaded = Blueprint.Load(Blueprint.Save(s), _cat, out _);
        Check(loaded?.Mode == CoreMode.Base, "the mode is saved in the blueprint");
        Check(Building.ToggleCoreMode(s) && s.Mode == CoreMode.Capital, "and switches back to capital");

        Check(Building.ToggleCoreMode(s) && s.Mode == CoreMode.Base, "with no hover plates aboard it never steps to Hover Mode");
        s.Mode = CoreMode.Vessel;
        Check(s.Mode == CoreMode.Capital && Blueprint.Load(Blueprint.Save(s).Replace("mode capital", "mode vessel"), _cat, out _)?.Mode == CoreMode.Capital,
            "old data's Vessel Mode flies as Capital Mode");
        s.Mode = CoreMode.Hover;
        Check(Blueprint.Load(Blueprint.Save(s), _cat, out _)?.Mode == CoreMode.Hover, "Hover Mode is saved in the blueprint");
    }

    /// <summary>
    /// Bases and capital ships share every block. Build one of everything (every material in every
    /// shape, every component) while in base mode, then flip the core both ways: nothing may be
    /// removed, changed or become invalid.
    /// </summary>
    void TestModeKeepsEveryBlock()
    {
        Structure s = NewShip();
        s.Mode = CoreMode.Base;
        for (int x = -1; x <= 200; x++)
            for (int z = -1; z <= 1; z++)
                if (x != 0 || z != 0) s.Set(new Int3(x, 0, z), Block(s, _steel));

        Orientations.TryParse("+z+y", out int backDown); // wall-mounted parts (light, ladder) hang from their back
        int x0 = 1, placedOk = 0, tried = 0;
        foreach (BlockType t in _cat.Types.Skip(1))
        {
            if (t.IsCore) continue; // the one core is already in the floor
            foreach (ShapeDef shape in t.IsArmor ? _cat.Shapes : new ShapeDef[] { null })
            {
                int rot = t.IsArmor || BlockType.Has(t.AttachFaces, Dir.NegY) ? 0 : backDown;
                tried++;
                var (lo, hi) = Footprint.Box(t, rot);
                if (Building.Place(s, new Int3(x0 - lo.X, 1 - lo.Y, -lo.Z), Building.Make(s, t, shape, rot)) == PlaceError.None) placedOk++;
                x0 += hi.X - lo.X + 1;
            }
        }
        Check(placedOk == tried, $"every block type and shape can be placed on a base ({placedOk}/{tried})");

        var before = s.Blocks().ToDictionary(x => x.pos, x => x.block);
        StructureStats statsBefore = StructureStats.Compute(s);
        bool allSame = true, allValid = true;
        foreach (CoreMode expected in new[] { CoreMode.Capital, CoreMode.Base, CoreMode.Capital })
        {
            Building.ToggleCoreMode(s);
            allSame &= s.Mode == expected && s.Count == before.Count
                && before.All(kv => s.TryGet(kv.Key, out Block b) && b.SameAs(kv.Value));
            allValid &= Blueprint.Validate(Blueprint.Parse(Blueprint.Save(s)), _cat).Count == 0;
            StructureStats st = StructureStats.Compute(s);
            allSame &= st.Blocks == statsBefore.Blocks && Near(st.Mass, statsBefore.Mass)
                && st.StorageCapacity == statsBefore.StorageCapacity && Near(st.PowerOut, statsBefore.PowerOut);
        }
        Check(allSame, $"switching base â†’ capital â†’ base â†’ capital keeps all {before.Count} blocks exactly as they were");
        Check(allValid, "and the structure validates in both modes");
        Check(s.Mode == CoreMode.Capital && StructureStats.Compute(s).Thrust.Sum() > 0,
            "the same thrusters push once it is a capital ship (only flying depends on the mode)");
    }

    // ---- Navigation -----------------------------------------------------------------------

    void TestNav()
    {
        var s = new Structure(_cat);
        for (int x = -2; x <= 2; x++)
            for (int z = -2; z <= 2; z++)
                s.Set(new Int3(x, 0, z), Block(s, x == 0 && z == 0 ? _core : _steel));
        NavGrid nav = NavGrid.Build(s);
        bool floors = true;
        for (int x = -2; x <= 2; x++)
            for (int z = -2; z <= 2; z++)
                floors &= nav.Nodes.TryGetValue(new Int3(x, 1, z), out NavKind k) && k == NavKind.Floor;
        Check(floors && nav.Nodes.Count == 25, $"the 25 cells above a 5Ã—5 floor are walkable, nothing else ({nav.Nodes.Count} nodes)");

        s.Set(new Int3(0, 1, 0), Block(s, _steel));
        nav = NavGrid.Build(s);
        Check(!nav.Nodes.ContainsKey(new Int3(0, 1, 0)) && nav.Nodes.ContainsKey(new Int3(0, 2, 0)),
            "a block on the floor is not walkable, but its top is");

        // A wall across z = 1 with a door, and a ladder up a pillar.
        for (int x = -2; x <= 2; x++) s.Set(new Int3(x, 1, 1), Block(s, x == 1 ? _door : _steel));
        s.Set(new Int3(-2, 1, -2), Block(s, _steel));
        s.Set(new Int3(-2, 2, -2), Block(s, _steel));
        Orientations.TryParse("+y+z", out int backToPillar); // the ladder's back (+Z) faces the pillar at -z
        s.Set(new Int3(-2, 1, -1), Block(s, _ladder, rot: backToPillar));
        s.Set(new Int3(-2, 2, -1), Block(s, _ladder, rot: backToPillar));
        nav = NavGrid.Build(s);
        Check(nav.Nodes.TryGetValue(new Int3(1, 1, 1), out NavKind door) && door == NavKind.Door
              && nav.LinksFrom(new Int3(1, 1, 0)).Any(l => l.To == new Int3(1, 1, 1) && l.Kind == NavKind.Door),
            "a door is a door node with door links");
        Check(nav.LinksFrom(new Int3(-2, 2, -1)).Any(l => l.Kind == NavKind.Ladder && l.To == new Int3(-2, 3, -2))
              && nav.LinksFrom(new Int3(-2, 1, -1)).Any(l => l.Kind == NavKind.Ladder && l.To == new Int3(-2, 2, -1)),
            "a ladder links its rungs and the floor on top");
        List<Int3> path = nav.FindPath(new Int3(-1, 1, -2), new Int3(2, 1, 2));
        Check(path != null && path.Contains(new Int3(1, 1, 1)), $"A* finds a way through the door ({path?.Count ?? 0} steps)");
    }

    // ---- Meshing --------------------------------------------------------------------------

    void TestMeshing() => MeshBenchmark.Run(_cat, Check);

    // ---- Helpers --------------------------------------------------------------------------

    Structure NewShip()
    {
        var s = new Structure(_cat);
        Building.Place(s, Int3.Zero, Block(s, _core));
        return s;
    }

    Block Block(Structure s, BlockType t, string shape = null, int rot = 0) =>
        Building.Make(s, t, shape == null ? null : _cat.Shape(shape), rot);

    PlaceError Place(Structure s, int x, int y, int z, BlockType t, string shape = null, string rot = null)
    {
        int o = 0;
        if (rot != null) Orientations.TryParse(rot, out o);
        return Building.Place(s, new Int3(x, y, z), Block(s, t, shape, o));
    }

    static bool Near(double a, double b, double eps = 1e-6) => Math.Abs(a - b) <= eps * Math.Max(1, Math.Abs(b));

    void Run(string name, Action test)
    {
        GD.Print($"\n== {name}");
        try { test(); }
        catch (Exception e) { Fail($"{name} threw {e}"); }
    }

    void Check(bool ok, string what)
    {
        if (ok) { _passes++; GD.Print($"PASS  {what}"); }
        else { _failures++; GD.PrintErr($"FAIL  {what}"); }
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
