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
    BlockType _steel, _hardened, _combat, _glass, _core, _thruster, _generator, _container, _door, _ladder, _extender;

    public override void _Ready()
    {
        _cat = GameData.Catalogue;
        _steel = _cat.Type("steel");
        _hardened = _cat.Type("hardened");
        _combat = _cat.Type("combat");
        _glass = _cat.Type("glass");
        _core = _cat.CoreType;
        _thruster = _cat.Type("thruster");
        _generator = _cat.Type("generator");
        _container = _cat.Type("container");
        _door = _cat.Type("door");
        _ladder = _cat.Type("ladder");
        _extender = _cat.Type("cpuext");
        GD.Print($"Catalogue: {_cat}");

        Run("Placement rules", TestPlacement);
        Run("Symmetry", TestSymmetry);
        Run("Removal", TestRemoval);
        Run("Severing", TestSevering);
        Run("Mass, CPU and tiers", TestStats);
        Run("Shapes", TestShapes);
        Run("Upgrading in place", TestUpgrade);
        Run("Paint", TestPaint);
        Run("Sealed rooms", TestRooms);
        Run("Shared storage", TestStorage);
        Run("Blueprints", TestBlueprints);
        Run("Validator", TestValidator);
        Run("Core mode", TestCoreMode);
        Run("Core mode never touches blocks", TestModeKeepsEveryBlock);
        Run("Early-game recipes", TestSmallRecipes);
        Run("Navigation", TestNav);
        Run("Meshing performance", TestMeshing);

        GD.Print($"\n{_passes} passed, {_failures} failed");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    // ---- Placement ------------------------------------------------------------------------

    void TestPlacement()
    {
        var empty = new Structure(_cat, _cat.Grid(GridSize.Large));
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
        var cost = _cat.CostOf(s.Get(new Int3(1, 0, 0)), s.Grid);
        RemoveResult r = Building.Remove(s, new Int3(1, 0, 0), inventory);
        Check(r.Status == RemoveStatus.Removed && !s.Has(new Int3(1, 0, 0)), "the multi-tool removes the block");
        Check(cost.All(kv => inventory[kv.Key] == kv.Value) && inventory["iron"] == 7 && inventory["tungsten"] == 2,
            $"its materials go to the inventory, scaled by shape volume ({string.Join(", ", inventory.Items.Select(kv => $"{kv.Value} {kv.Key}"))})");

        Place(s, 1, 0, 0, _steel);
        Check(Building.Remove(s, Int3.Zero, inventory).Status == RemoveStatus.CoreHoldsStructure && s.Core != null,
            "the core cannot be taken down while blocks hang off it");
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
        Check(StructureStats.Compute(wreck).CpuLimit == 0, "the wreck's components stop working (no core, no CPU)");

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

        double mass = 4 + 10 * 1 + 2 * 2 * 0.5 + 3 + 6 + 2;
        Check(Near(st.Mass, mass), $"mass is the sum of the blocks ({st.Mass:0.###} t, expected {mass} t)");
        s.CargoSu = 2000;
        Check(Near(StructureStats.Compute(s).Mass, mass + 2), "cargo adds to the mass (2000 SU = 2 t)");
        s.CargoSu = 0;
        double cpu = 0 + 10 * 20 + 2 * 45 + 2500 + 4000 + 800;
        Check(Near(st.CpuUsed, cpu) && st.Tier == 1 && st.CpuLimit == 200_000 && st.Efficiency == 1,
            $"CPU is summed ({st.CpuUsed}) and the core alone gives tier 1 (200,000)");
        Check(Near(st.Thrust[(int)Dir.NegZ], _thruster.Thrust) && Near(st.ThrustToWeightForward, _thruster.Thrust / (mass * 9.81)),
            $"thrust-to-mass is right ({st.ThrustToWeightForward:0.00} g forward)");

        Check(Near(StructureStats.EfficiencyFor(400_000, 200_000), 0.5), "100% over the limit gives 50% efficiency");
        Check(Near(StructureStats.EfficiencyFor(250_000, 200_000), 0.8), "25% over gives 80% efficiency");

        // A real overshoot: generators until the core's 200,000 is exceeded.
        Structure big = NewShip();
        for (int x = 1; x <= 60; x++) Place(big, x, 0, 0, _generator);
        StructureStats over = StructureStats.Compute(big);
        Check(over.CpuUsed > over.CpuLimit && Near(over.Efficiency, 200_000 / over.CpuUsed)
              && Near(over.PowerOut, 60 * 12 * over.Efficiency),
            $"over the limit, components run at limit/used ({over.Efficiency:P1} of {60 * 12} MW = {over.PowerOut:0.#} MW)");

        Structure ext = NewShip();
        var tiers = new List<(int extenders, int tier, long limit)>();
        for (int n = 0; n <= 4; n++)
        {
            if (n > 0) Place(ext, -n, 0, 0, _extender);
            StructureStats e = StructureStats.Compute(ext);
            tiers.Add((n, e.Tier, e.CpuLimit));
        }
        Check(tiers[1].tier == 2 && tiers[1].limit == 500_000 && tiers[2].tier == 3 && tiers[2].limit == 1_500_000
              && tiers[3].tier == 3 && tiers[4].tier == 4 && tiers[4].limit == 10_000_000,
            "extenders unlock tiers: 1 → T2 500k, 2 → T3 1.5M, 3 stays T3, 4 → T4 10M");
        GridDef small = _cat.Grid(GridSize.Small);
        long[] smallLimits = new[] { 0, 1, 2, 4 }.Select(n => StructureStats.TierFor(_cat, small, n, true).limit).ToArray();
        Check(smallLimits.SequenceEqual(new long[] { 6000, 15000, 40000, 100000 }), "small-grid tiers are 6k / 15k / 40k / 100k");

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
        Check(cyl > 0.76f && cyl < 0.786f, $"cylinder volume is about π/4 ({cyl:0.###})");

        bool scaled = true, same = true;
        foreach (ShapeDef shape in _cat.Shapes)
        {
            BlockStats st = s.StatsOf(Block(s, _combat, shape.Id));
            scaled &= Near(st.MaxHp, cube.MaxHp * shape.Volume, 1e-3) && Near(st.Mass, cube.Mass * shape.Volume, 1e-4);
            same &= st.Armor == cube.Armor && st.Resist == cube.Resist && st.Cpu == cube.Cpu;
        }
        Check(scaled, "every shape's hit points and mass scale with its volume");
        Check(same, "every shape keeps the material's armour and resistances");
        Check(_cat.Shape("slope").Face(Dir.NegY) == Coverage.Full && _cat.Shape("slope").Face(Dir.PosZ) == Coverage.Full
              && _cat.Shape("slope").Face(Dir.PosX) == Coverage.Partial && _cat.Shape("slope").Face(Dir.PosY) == Coverage.None,
            "a slope has a full bottom and back, triangular sides and no top");

        var smallShip = new Structure(_cat, _cat.Grid(GridSize.Small));
        Check(Near(smallShip.StatsOf(Block(smallShip, _steel)).MaxHp, 50), "a small-grid steel cube has a tenth of the hit points (50)");
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

        Check(Building.Upgrade(s, p) == UpgradeStatus.Upgraded, "steel upgrades to hardened");
        Block after = s.Get(p);
        Check(s.TypeOf(after) == _hardened && after.Shape == before.Shape && after.Rot == rot && after.Paint.Equals(before.Paint),
            "the upgrade keeps the shape, the orientation and the paint");
        Check(Near(after.Hp, 1000 * 0.5), $"and comes back at the new material's full health ({after.Hp})");
        Check(Building.Upgrade(s, p) == UpgradeStatus.Upgraded && s.TypeOf(s.Get(p)) == _combat, "hardened upgrades to combat");
        Check(Building.Upgrade(s, p) == UpgradeStatus.AlreadyBest, "combat is the top");
        Check(Building.Upgrade(s, Int3.Zero) == UpgradeStatus.NotArmor, "components are not armour");

        var hit = new DamageReport();
        Damage.Hit(s, p, DamageType.Energy, 600, hit);
        Check(Building.IsDamaged(s, p) && Building.Repair(s, p) && !Building.IsDamaged(s, p), "the multi-tool repairs a damaged block");
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
            $"a closed 5×5×5 box holds one sealed 27-cell room ({r.Sealed.Count()} sealed)");

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
        Check(!r.Sealed.Any() && r.LeakFaces.SequenceEqual(new[] { new Face(doorAt, Dir.PosX) }), "an open door leaks, at the doorway");

        Building.Toggle(s, doorAt);
        s.Set(new Int3(2, 2, 4), Block(s, _glass));
        Check(Rooms.Detect(s).Sealed.Count() == 1, "a glass window is airtight");
    }

    /// <summary>A hollow 5×5×5 steel box from (0,0,0) to (4,4,4) with its core in the floor.</summary>
    Structure HollowBox()
    {
        var s = new Structure(_cat, _cat.Grid(GridSize.Large));
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
        Check(st.StorageCapacity == 24_000 && st.Containers == 3, $"three large containers make one 24,000 SU storage ({st.StorageCapacity})");
        for (int x = 4; x <= 50; x++) Place(s, x, 0, 0, _container);
        st = StructureStats.Compute(s);
        Check(st.StorageRaw == 400_000 && st.StorageCapacity == 320_000, "50 containers (400,000 SU) are capped at 320,000 SU on the large grid");

        var small = new Structure(_cat, _cat.Grid(GridSize.Small));
        Building.Place(small, Int3.Zero, Block(small, _core));
        for (int x = 1; x <= 10; x++) Building.Place(small, new Int3(x, 0, 0), Block(small, _container));
        Check(StructureStats.Compute(small).StorageCapacity == 1250, "small containers hold 125 SU each");
        for (int x = 11; x <= 300; x++) Building.Place(small, new Int3(x, 0, 0), Block(small, _container));
        st = StructureStats.Compute(small);
        Check(st.StorageRaw == 37_500 && st.StorageCapacity == 32_000, "the small grid caps storage at 32,000 SU");
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
        GD.Print($"      {ship.Name}: {ship.Count} blocks, {st.Mass:0} t, CPU {st.CpuUsed:0}/{st.CpuLimit} (T{st.Tier}), " +
                 $"{st.Thrusters} thrusters, {st.Generators} generators, {st.Containers} containers, " +
                 $"{rooms.Sealed.Count()} sealed rooms, {rooms.Leaking.Count()} leaking");
        Check(rooms.Sealed.Any() && !rooms.Leaking.Any(), "the example ship's cabin is sealed");
        Check(ship.Blocks().Any(b => ship.TypeOf(b.block).IsCockpit) && st.Thrusters > 0 && st.Generators > 0 && st.Containers > 0,
            "it has a cockpit, thrusters, a generator and containers");

        string text1 = Blueprint.Save(ship);
        string text2 = Blueprint.Save(Blueprint.Load(text1, _cat, out _));
        Check(text1 == text2, $"a blueprint round-trips byte for byte ({text1.Length} bytes)");

        // Damage, doors, lights and cargo round-trip too.
        Structure s = NewShip();
        for (int x = 1; x <= 4; x++) Place(s, x, 0, 0, _steel);
        Place(s, 1, 1, 0, _door);
        Building.Toggle(s, new Int3(1, 1, 0));
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
        const string head = "schema galhack.blueprint\nversion 1\nname \"t\"\ngrid large\n";
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
            (head + "block 0 0 0 core\nblock 1 0 0 thruster slope\n", "takes no shape", "a shape on a component"),
            (head + "block 0 0 0 core\nblock 1 0 0 steel\nblock 5 5 5 steel\n", "not attached", "a detached block marked as attached"),
            (head + "block 0 0 0 core\nblock 1 0 0 steel half\nblock 1 1 0 steel\n", "not attached", "a block touching only a half block's open top"),
            ("schema galhack.blueprint\nversion 9\ngrid large\nblock 0 0 0 core\n", "version", "an unknown version"),
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
        Check(s.Mode == CoreMode.Capital && StructureStats.Compute(s).Flies, "a Big Core in Capital Mode flies");
        Check(Building.ToggleCoreMode(s) && s.Mode == CoreMode.Base, "B switches the core to base");
        StructureStats st = StructureStats.Compute(s);
        Check(!st.Flies && st.Thrust.Sum() == 0 && st.Blocks == 3, "a base keeps every block but does not fly");
        Structure loaded = Blueprint.Load(Blueprint.Save(s), _cat, out _);
        Check(loaded?.Mode == CoreMode.Base, "the mode is saved in the blueprint");
        Check(Building.ToggleCoreMode(s) && s.Mode == CoreMode.Capital, "and switches back to capital");

        var small = new Structure(_cat, _cat.Grid(GridSize.Small));
        Building.Place(small, Int3.Zero, Block(small, _core));
        Check(small.Mode == CoreMode.Vessel && Building.ToggleCoreMode(small) && small.Mode == CoreMode.Hover, "a Small Core switches between Vessel Mode and Hover Mode");
        Check(Blueprint.Load(Blueprint.Save(small), _cat, out _)?.Mode == CoreMode.Hover, "the Small Core's mode is saved in the blueprint");
        Check(Building.ToggleCoreMode(small) && small.Mode == CoreMode.Vessel, "and switches back");
        Check(Blueprint.Load(Blueprint.Save(small).Replace("mode vessel", "mode capital"), _cat, out _)?.Mode == CoreMode.Vessel, "an old blueprint's \"mode capital\" on a small grid is Vessel Mode");
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
        for (int x = -1; x <= 60; x++)
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
                if (Building.Place(s, new Int3(x0++, 1, 0), Building.Make(s, t, shape, rot)) == PlaceError.None) placedOk++;
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
            allSame &= st.Blocks == statsBefore.Blocks && Near(st.Mass, statsBefore.Mass) && Near(st.CpuUsed, statsBefore.CpuUsed)
                && st.StorageCapacity == statsBefore.StorageCapacity && Near(st.PowerOut, statsBefore.PowerOut);
        }
        Check(allSame, $"switching base → capital → base → capital keeps all {before.Count} blocks exactly as they were");
        Check(allValid, "and the structure validates in both modes");
        Check(s.Mode == CoreMode.Capital && StructureStats.Compute(s).Thrust.Sum() > 0,
            "the same thrusters push once it is a capital ship (only flying depends on the mode)");
    }

    /// <summary>A small vessel is the early-game goal: its blocks must be cheap and made of common materials.</summary>
    void TestSmallRecipes()
    {
        GridDef small = _cat.Grid(GridSize.Small);
        var scratch = new Structure(_cat, small);
        var common = new HashSet<string>(_cat.CommonMaterials);
        var rare = new List<string>();
        foreach (BlockType t in _cat.Types.Skip(1))
            foreach (ShapeDef shape in t.IsArmor ? _cat.Shapes : new ShapeDef[] { null })
                rare.AddRange(_cat.CostOf(Building.Make(scratch, t, shape), small).Keys.Where(i => !common.Contains(i)).Select(i => $"{t.Id}:{i}"));
        Check(common.Count > 0 && rare.Count == 0,
            $"every small-grid recipe uses only common materials ({string.Join(", ", common)})" +
            (rare.Count > 0 ? ": not " + string.Join(", ", rare.Distinct()) : ""));

        // A starter hopper: core, seat, four thrusters, generator, tank, container, glass canopy, steel hull.
        var bill = new Dictionary<string, int>();
        void Add(string id, int n, string shape = null)
        {
            foreach (var (item, count) in _cat.CostOf(Building.Make(scratch, _cat.Type(id), shape == null ? null : _cat.Shape(shape)), small))
                bill[item] = (bill.TryGetValue(item, out int had) ? had : 0) + count * n;
        }
        Add("core", 1); Add("chair", 1); Add("thruster", 4); Add("generator", 1); Add("fueltank", 1); Add("container", 1);
        Add("door", 1); Add("light", 2); Add("glass", 8); Add("glass", 4, "slope"); Add("steel", 70); Add("steel", 20, "slope");
        int total = bill.Values.Sum();
        Check(total <= 300, $"a 115-block starter small vessel costs {total} units of common materials: " +
            string.Join(", ", bill.OrderBy(kv => kv.Key).Select(kv => $"{kv.Value} {kv.Key}")));
        int smallSteel = _cat.CostOf(Building.Make(scratch, _steel), small)["iron"];
        int largeSteel = _cat.CostOf(Block(NewShip(), _steel), _cat.Grid(GridSize.Large))["iron"];
        Check(smallSteel * 5 <= largeSteel, $"a small steel block costs a fifth or less of a large one ({smallSteel} vs {largeSteel} iron)");
    }

    // ---- Navigation -----------------------------------------------------------------------

    void TestNav()
    {
        var s = new Structure(_cat, _cat.Grid(GridSize.Large));
        for (int x = -2; x <= 2; x++)
            for (int z = -2; z <= 2; z++)
                s.Set(new Int3(x, 0, z), Block(s, x == 0 && z == 0 ? _core : _steel));
        NavGrid nav = NavGrid.Build(s);
        bool floors = true;
        for (int x = -2; x <= 2; x++)
            for (int z = -2; z <= 2; z++)
                floors &= nav.Nodes.TryGetValue(new Int3(x, 1, z), out NavKind k) && k == NavKind.Floor;
        Check(floors && nav.Nodes.Count == 25, $"the 25 cells above a 5×5 floor are walkable, nothing else ({nav.Nodes.Count} nodes)");

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

    Structure NewShip(GridSize grid = GridSize.Large)
    {
        var s = new Structure(_cat, _cat.Grid(grid));
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
