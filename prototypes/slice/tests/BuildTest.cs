using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Items.Production;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using Slice.Construction;
using Slice.Game;
using Slice.Items;
using Slice.Places;
using Slice.Player;
using Slice.Surface;
using Slice.Ui;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using StructureClass = GalHack.Items.StructureClass;
using Survivor = Slice.Player.Survivor;

namespace Slice.Tests;

/// <summary>
/// Blocks as items in the slice, and the storage model round them (a systems test: it hands over
/// items and teleports where a playthrough would take minutes, and says so). Pure checks first:
/// the shared grid's filter, sort and speed, the HUD's layout at every size, every device in all
/// 24 orientations. Then in a world: every structure starts with its core and takes only the
/// blocks its class allows; the toolbar you choose is the source you draw from, your bag or the
/// structure's one storage, inside its area only, and the two never merge; nothing is lost on the
/// way back; constructors stay on their own storage; filters, typing and tooltips through the menu.
/// </summary>
public partial class BuildTest : Node
{
    Game.Game _game;
    Driver _drive;
    Playthrough _play;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    ItemCatalogue Items => _game?.Zone.Data.Items ?? GameData.Shared.Items;
    Catalogue Blocks => GameData.Shared.Catalogue;
    PlayerInventory Pack => _game.Pack;
    string Item(string block, bool small) => Items.BlockItem(block, small).Id;

    const int WatchdogMinutes = 20;

    public override void _Ready()
    {
        // Its own time limit: a hung run quits itself.
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenarios().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"  FAIL  {what}");
        if (_game?.Survivor == null) return;
        bool seen = WorldRay.Cast(_game.Zone, _game.Survivor, 30f, Layers.World | Layers.Hull, out LookHit look);
        GD.Print($"    (feet {_game.Survivor.WorldFeet}, eye {_game.Survivor.WorldEye()}, frame {_game.Survivor.Body.Frame?.Label}, selected {Pack.Selected} {Pack.InHand} hand {Pack.Hand}; look {(seen ? $"meets {look.Vessel?.Name ?? "ground"} at {look.Distance:F1} m" : "nothing")}; verdict \"{_game.Builder.Verdict}\"; target {_game.Builder.TargetVessel?.Name}, menu {_game.Menu.IsOpen})");
    }

    bool Check(bool ok, string what)
    {
        if (ok) { _passes++; GD.Print($"  ok    {what}"); }
        else Fail(what);
        return ok;
    }

    IEnumerable<int> Scenarios()
    {
        GD.Print("-- the HUD's layout --");
        HudChecks();
        GD.Print("-- filters and sorts on the shared grid --");
        FilterChecks();
        GD.Print("-- a big storage stays fast --");
        SpeedChecks();
        GD.Print("-- devices work whichever way they face --");
        OrientationChecks();
        GD.Print("-- lights: colour, animation and glowing paint --");
        LightChecks();
        GD.Print("-- the starter blueprints, and saving one --");
        BlueprintChecks();
        IconSourceChecks();

        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        _play = new Playthrough(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        GD.Print("  (systems test: items are handed over and the castaway teleported where a playthrough would spend minutes)");
        CompassAtStart();
        Check(Pack.Hand == HandKind.Empty && _game.ToolHint != null && Items.Item("survival-tool").Recipe.Inputs.Count == 0, "empty-handed, and the survival tool's recipe is free");
        Check(_game.Controls.Primary == false && Pack.Storage.IsEmpty, "nothing carried at the start");
        foreach (int x in _play.MakeTool()) yield return x;
        Check(Pack.Hand == HandKind.Tool && _game.ToolHint == null, "made in the suit fabricator and put in hand: the hint goes");
        // Lost: the free recipe stays.
        Pack.Remove("survival-tool", 1);
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(Pack.Hand == HandKind.Empty, "a lost tool leaves your hands empty");
        foreach (int x in _play.MakeTool()) yield return x;
        Check(Pack.Hand == HandKind.Tool, "and the free recipe replaces it");
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--only=ui") >= 0)
        {
            // -- --only=ui: the UI round's checks alone.
            foreach (int x in UiRound()) yield return x;
            yield break;
        }
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--only=blueprints") >= 0)
        {
            // -- --only=blueprints: the BLUEPRINTS tab's scenario alone.
            foreach (int x in Blueprints()) yield return x;
            yield break;
        }
        foreach (int x in Founding()) yield return x;
        foreach (int x in Sources()) yield return x;
        foreach (int x in BackIntoInventories()) yield return x;
        foreach (int x in Areas()) yield return x;
        foreach (int x in Machines()) yield return x;
        TechTabs();
        foreach (int x in CpuExtender()) yield return x;
        LatticeGate();
        foreach (int x in MenuChecks()) yield return x;
        foreach (int x in UiRound()) yield return x;
        foreach (int x in Creative()) yield return x;
        foreach (int x in Blueprints()) yield return x;
    }

    // ---- Pure checks ---------------------------------------------------------------------------

    void HudChecks()
    {
        var sizes = new[] { new Vector2(1280, 720), new Vector2(1600, 900), new Vector2(1920, 1080), new Vector2(2560, 1440), new Vector2(1024, 768), new Vector2(3440, 1440) };
        const string longest = "BASE 1 TOOLBAR · Hardened steel block (large), inverted corner · 1200 in your stack · 34000 in Base 12";
        int checks = 0;
        foreach (Vector2 size in sizes)
            foreach (float scale in new[] { 0.75f, 1f, 1.25f, 1.5f })
            {
                HudLayout l = HudLayout.Compute(size, scale);
                string problem = l.Problem(size);
                if (problem != null) Fail($"HUD at {size} scale {scale}: {problem}");
                int font = (int)(16 * l.Scale);
                string fitted = HudLayout.Fit(DiegeticUi.Theme.UiTheme.Label, font, longest, l.ItemLine.Size.X);
                if (DiegeticUi.Theme.Paint.TextWidth(DiegeticUi.Theme.UiTheme.Label, font, fitted) > l.ItemLine.Size.X + 0.5f) Fail($"HUD at {size} scale {scale}: the long line overflows its box");
                checks++;
            }
        Check(true, $"vitals, toolbar, item line, hints, shapes and lines never overlap or leave the window ({checks} sizes and scales), and the longest line fits its box");
    }

    ItemGrid BigGrid(int perItem, out SharedStorage storage)
    {
        storage = SharedStorage.Unbounded(Items, "test");
        foreach (ItemDef i in Items.Items) storage.Add(i.Id, i.StackSize * perItem);
        var grid = new ItemGrid(storage, 8, 6, 0, growable: true);
        grid.Reconcile();
        return grid;
    }

    void FilterChecks()
    {
        ItemGrid grid = BigGrid(1, out SharedStorage storage);
        Bill before = storage.Contents;
        var view = new ItemView();
        bool Shows(Func<ItemDef, bool> test) => view.Visible(grid).Select(i => Items.Item(grid[i].Item)).All(test);
        Check(view.Visible(grid).Count == grid.Count, "unfiltered and as arranged, every slot shows");
        view.Text = "StEeL";
        Check(view.Visible(grid).Count > 0 && Shows(i => i.Name.Contains("steel", StringComparison.OrdinalIgnoreCase) || ItemView.CategoryWords(i).Contains("steel")), $"text filter, case-insensitive: \"StEeL\" shows {view.Visible(grid).Count} stacks, all steel");
        view.Text = "raw";
        Check(view.Visible(grid).Count > 0 && Shows(i => i.Category == ItemCategory.Raw), "text filter on a category name: \"raw\" shows raw items");
        view.Text = "";
        foreach (ItemGroup g in ItemView.Groups)
        {
            view.Show = g;
            int n = view.Visible(grid).Count;
            Check(Shows(i => ItemView.GroupOf(i) == g) && (n > 0 || g == ItemGroup.Other), $"category {ItemView.GroupName(g)}: {n} stacks, all of it");
        }
        view.Show = ItemGroup.Ores | ItemGroup.Ammo;
        Check(Shows(i => i.Category is ItemCategory.Raw or ItemCategory.Ammo), "two categories together");
        view.Show = ItemGroup.Deco;
        view.Text = "office";
        Check(view.Visible(grid).Count > 0 && Shows(i => i.Family == BlockFamily.Deco), "a category and text together: deco, \"office\"");
        view.Clear();

        var names = new Func<int, ItemDef>(i => Items.Item(grid[i].Item));
        bool Sorted<T>(Func<ItemDef, int, T> key, IComparer<T> cmp)
        {
            var list = view.Visible(grid).ToList();
            for (int k = 1; k < list.Count; k++)
                if (cmp.Compare(key(names(list[k - 1]), grid[list[k - 1]].Count), key(names(list[k]), grid[list[k]].Count)) > 0) return false;
            return list.Count > 0;
        }
        view.Sort = ItemSort.Name;
        Check(Sorted((i, _) => i.Name, StringComparer.OrdinalIgnoreCase), "sort by name");
        view.Sort = ItemSort.Category;
        Check(Sorted((i, _) => (int)i.Category, Comparer<int>.Default), "sort by category");
        view.Sort = ItemSort.Quantity;
        Check(Sorted((_, n) => -n, Comparer<int>.Default), "sort by quantity, most first");
        view.Sort = ItemSort.Mass;
        Check(Sorted((i, n) => -i.MassKg * n, Comparer<double>.Default), "sort by the stack's mass, heaviest first");
        view.Sort = ItemSort.Rarity;
        Check(Sorted((i, _) => -(int)i.Rarity, Comparer<int>.Default), "sort by rarity, rarest first");
        view.Sort = ItemSort.Recent;
        storage.Add("iron-ore", 1);
        grid.Reconcile();
        Check(names(view.Visible(grid)[0]).Id == "iron-ore", "sort by recently added: what just came in is first");
        storage.Remove("iron-ore", 1);
        grid.Reconcile();
        view.NextSort();
        Check(view.Sort == ItemSort.Slot, "one click steps through the sorts, back round to as arranged");
        Check(storage.Contents.Equals(before), "filtering and sorting never change what is stored");

        // Moves under a filter act on what is shown.
        SharedStorage to = SharedStorage.Unbounded(Items, "other");
        var other = new ItemGrid(to, 8, 6, 0, growable: true);
        view.Text = "iron";
        var shown = new HashSet<int>(view.Visible(grid));
        Bill moved = ItemGrid.MoveAll(grid, other, true, shown.Contains);
        Check(!moved.IsEmpty && moved.Ids.All(id => Items.Item(id).Name.Contains("iron", StringComparison.OrdinalIgnoreCase) || ItemView.CategoryWords(Items.Item(id)).Contains("iron")) && to.Contents.Equals(moved),
            $"take-all under a filter takes only what is shown ({shown.Count} stacks)");
        view.Clear();
    }

    void SpeedChecks()
    {
        ItemGrid grid = BigGrid(8, out SharedStorage storage);
        var view = new ItemView { Text = "steel", Sort = ItemSort.Name };
        double Best(Action a)
        {
            double best = double.MaxValue;
            for (int r = 0; r < 5; r++)
            {
                var w = Stopwatch.StartNew();
                a();
                best = Math.Min(best, w.Elapsed.TotalMilliseconds);
            }
            return best;
        }
        double reconcile = Best(() => { storage.Remove("iron-ore", 1); grid.Reconcile(); });
        double filter = Best(() => { storage.Add("iron-ore", 1); view.Visible(grid); });
        double cached = Best(() => view.Visible(grid));
        double typing = Best(() => { view.Text = view.Text == "steel" ? "iron" : "steel"; view.Visible(grid); });
        GD.Print($"  {grid.Used} stacks of {Items.Items.Count} kinds: a change reconciles in {reconcile:F2} ms, filters and sorts in {filter:F2} ms, redraws cached in {cached:F4} ms, retypes in {typing:F2} ms");
        Check(grid.Used > 2000, $"a storage of thousands of stacks ({grid.Used})");
        Check(reconcile < 8 && filter < 12 && typing < 12 && cached < 0.2, "no hitch: every step well inside a frame, and a redraw with nothing changed is nearly free");
    }

    /// <summary>Every device, in all 24 orientations, on a small and a large grid: the same stats; a thruster only changes axis.</summary>
    void OrientationChecks()
    {
        foreach (bool small in new[] { true, false })
        {
            int devices = 0;
            foreach (BlockType type in Blocks.Types.Where(t => t != null && !t.IsArmor && !t.IsCore))
            {
                var seen = new HashSet<string>();
                var axes = new HashSet<Dir>();
                int placed = 0;
                for (int rot = 0; rot < Orientations.Count; rot++)
                {
                    var s = new Structure(Blocks, Blocks.Grid(small ? GridSize.Small : GridSize.Large)) { Mode = CoreMode.Capital };
                    Building.Place(s, Int3.Zero, Building.Make(s, Blocks.CoreType));
                    Block b = Building.Make(s, type, null, rot);
                    Int3? cell = null;
                    foreach (Dir d in Dirs.All)
                        if (Building.Check(s, d.Vec(), b) == PlaceError.None) { cell = d.Vec(); break; }
                    if (cell == null) { Fail($"{type.Id} in orientation {rot} attaches to no face of the core"); continue; }
                    Building.Place(s, cell.Value, b);
                    placed++;
                    StructureStats st = StructureStats.Compute(s);
                    var spec = new StructureVessel(s, GameData.Shared.Handling);
                    double thrust = st.Thrust.Sum();
                    seen.Add(string.Join("|", st.PowerOut, st.PowerUse, st.CpuUsed, st.CpuLimit, st.StorageCapacity, st.Fuel, st.Mass, thrust,
                        st.Containers, st.Generators, st.Lights, st.LifeSupport, BuildingBridge.MachinesOn(Items, s).Count(),
                        spec.Gyros.Sum(g => g.Torque), spec.HoverEngines.Count, spec.LandingGear.Count, s.Blocks().Count(x => s.TypeOf(x.block).IsCommandChair)));
                    if (type.IsThruster) axes.Add(Orientations.ToWorld(rot, Dir.NegZ));
                }
                devices++;
                if (placed == Orientations.Count && seen.Count != 1) Fail($"{type.Id} ({(small ? "small" : "large")}): its stats change with its facing: {string.Join(" / ", seen.Take(3))}");
                if (type.IsThruster && axes.Count != 6) Fail($"a thruster's axis should follow its facing: {axes.Count} axes");
            }
            Check(true, $"{devices} device types on the {(small ? "small" : "large")} grid in all {Orientations.Count} orientations: the same power, CPU, storage, crafting, control and torque; a thruster changes only its axis");
        }
        // A chair gives no air: only a sealed room does.
        var room = new Structure(Blocks, Blocks.Grid(GridSize.Small)) { Mode = CoreMode.Capital };
        Building.Place(room, new Int3(0, -1, 0), Building.Make(room, Blocks.CoreType));
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 2; y++)
                for (int z = -1; z <= 1; z++)
                    if ((x != 0 || z != 0 || y is -1 or 2) && !(x == 0 && y == -1 && z == 0) && !room.Has(new Int3(x, y, z)))
                        Building.Place(room, new Int3(x, y, z), Building.Make(room, Blocks.Type("steel")));
        Int3 chair = new(0, 0, 0);
        Building.Place(room, chair, Building.Make(room, Blocks.Type("chair")));
        Check(Rooms.Detect(room).Sealed.Any(r => r.Cells.Contains(chair)), "a command chair inside a sealed shell sits in a sealed room");
        Building.Remove(room, new Int3(0, 1, 1), null);
        Check(!Rooms.Detect(room).Sealed.Any(r => r.Cells.Contains(chair)), "open a hole and the room leaks: the chair is no help");
        Check(Blocks.TryGetType("cockpit", out BlockType old) && old.IsCommandChair && old.IsCockpit, "old blueprints' cockpit id reads as the command chair");
    }

    // ---- In the world ----------------------------------------------------------------------------

    void Teleport(Vector3 feet) => _game.Survivor.PlaceOutside(feet.ToNumerics());

    /// <summary>The ground as it stands (the natural height, or lower where the site was levelled).</summary>
    Vector3 Ground(Vector3 at)
    {
        Vector3 natural = _game.Zone.GroundAt(at.X, at.Z);
        var hit = _game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(natural + Vector3.Up * 1f, natural + Vector3.Down * 30f, GalHack.Physics.Layers.World));
        return (hit.Count > 0 ? (Vector3)hit["position"] : natural) + Vector3.Up * 0.1f;
    }

    /// <summary>Digs the ground down to the site's height, as a builder would cut a pad into a slope.</summary>
    void LevelSite(float radius)
    {
        int y0 = (int)MathF.Floor(_site.Y), r = (int)radius;
        var cells = new List<TerrainDigging.Terrain.Int3>();
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            int x = (int)MathF.Floor(_site.X) + dx, z = (int)MathF.Floor(_site.Z) + dz;
            int top = (int)MathF.Ceiling(_game.Zone.GroundAt(x + 0.5f, z + 0.5f).Y);
            for (int y = y0; y <= top; y++) cells.Add(new TerrainDigging.Terrain.Int3(x, y, z));
        }
        _game.Zone.Edits.DigCells(cells);
    }

    IEnumerable<int> StandAt(Vector3 at)
    {
        Teleport(Ground(at));
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>Stands at a point in the base's own frame (metres), on the ground.</summary>
    IEnumerable<int> StandByBase(float x, float z)
    {
        foreach (int t in StandAt(_base.ToWorld(new N.Vector3(x, 0, z)))) yield return t;
    }

    /// <summary>Gets a stack of an item into the bag and onto a toolbar slot through the menu.</summary>
    IEnumerable<int> Carry(string item, int n, int slot)
    {
        Pack.Add(item, n);
        foreach (int x in _play.ToToolbar(item, slot)) yield return x;
        foreach (int x in _play.CloseMenu()) yield return x;
    }

    Vessel _base, _small, _hover, _capital;
    Vector3 _site;

    IEnumerable<int> Founding()
    {
        GD.Print("-- every structure starts with its core --");
        // Flat right across the base and the spots to stand at round it: on a slope the ground
        // buries its lowest blocks and stands between you and them. Any slope left is cut away.
        // Ground that won't be dug (a road's bed, a pad) or anything standing there (a ruin's stump, a wreck) spoils the site: then on to another.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            _site = _play.FlatSpot(_game.Start, 70f + attempt * 30f, 0.6f + attempt * 1.7f, 0.4f, span: 14f);
            foreach (int x in StandAt(_site + new Vector3(6, 0, 0))) yield return x;
            LevelSite(14f);
            foreach (int x in _drive.UntilSettled()) yield return x;
            _site = Ground(_site);
            float ridge = 0f;
            bool standing = false;
            for (int k = 0; k < 16; k++)
            {
                var way = new Vector3(MathF.Cos(k * 0.3927f), 0f, MathF.Sin(k * 0.3927f));
                standing |= _game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(_site + Vector3.Up * 1.2f, _site + Vector3.Up * 1.2f + way * 12f, GalHack.Physics.Layers.World)).Count > 0;
                for (float r = 2f; r <= 12f; r += 2f)
                    ridge = MathF.Max(ridge, MathF.Abs(_game.Zone.GroundAt(_site.X + way.X * r, _site.Z + way.Z * r).Y - _site.Y));
            }
            if (ridge < 0.6f && !standing) break;
            GD.Print($"  (the site at {_site} keeps {ridge:F1} m of ground{(standing ? " and something standing on it" : "")}: on to another)");
        }
        foreach (int x in StandAt(_site + new Vector3(6, 0, 0))) yield return x;
        string steelL = Item("steel", false), steelS = Item("steel", true);
        foreach (int x in Carry(steelL, 12, 2)) yield return x;
        foreach (int x in Carry(steelS, 12, 3)) yield return x;
        int structures = _game.Zone.Vessels.Count;
        foreach (int x in _play.Select(2)) yield return x;
        foreach (int x in _drive.LookAt(_site, 8)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_game.Zone.Vessels.Count == structures && Pack[steelL] == 12 && _game.Builder.Verdict.Contains("set a core down"),
            $"a block can't be placed in the open without a structure: \"{_game.Builder.Verdict}\"");

        foreach (int x in Carry("big-core", 1, 4)) yield return x;
        foreach (int x in _play.PlaceOnGround(4, _site)) yield return x;
        _base = _game.Zone.Vessels.FirstOrDefault(v => v.IsBase && !v.IsRuin && !v.IsWreck);
        Check(_base != null && StructureArea.ClassOf(_base) == StructureClass.Base && _base.Structure.Grid.Size == GridSize.Large && Pack["big-core"] == 0,
            "the Big Core founds a base (large grid, anchored), and is used up");
        if (_base == null) yield break;

        // A small-grid block is refused on a base, with a clear message.
        foreach (int x in _play.Select(3)) yield return x;
        foreach (int x in _drive.LookAt(_base.ToWorld(new N.Vector3(1f, 0, 0)), 8)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_base.Structure.Count == 1 && Pack[steelS] == 12 && _game.Builder.Verdict.Contains("small-grid block"), $"a small-grid block is refused on a base: \"{_game.Builder.Verdict}\" (the eye {_game.Survivor.WorldEye().DistanceTo(_base.ToWorld(new N.Vector3(1f, 0, 0))):F1} m off it, feet {_game.Survivor.WorldFeet}, core at {_base.Body.Position}, site {_site}, ground there {_game.Zone.GroundAt(_site.X, _site.Z).Y:F2})");

        // Building onto the founded structure works.
        bool on = false;
        foreach (int x in StandByBase(7, 1)) yield return x;
        foreach (int x in _play.PlaceBlock(2, _base, Int3.Zero, Dir.PosX, p => on = p)) yield return x;
        Check(on && Pack[steelL] == 11, "a large block goes onto the base's core, one item used");

        // A structure's class is its core and that core's mode: the two cores make all four.
        foreach (var (core, mode, cls, slot, offset) in new[]
                 {
                     ("small-core", CoreMode.Vessel, StructureClass.Small, 5, new Vector3(20, 0, 0)), ("small-core", CoreMode.Hover, StructureClass.Hover, 6, new Vector3(-20, 0, 0)),
                     ("big-core", CoreMode.Capital, StructureClass.Capital, 7, new Vector3(0, 0, 20)),
                 })
        {
            Vector3 spot = _play.FlatSpot(_site + offset, 4f, 1.4f, 0.5f);
            foreach (int x in StandAt(spot + new Vector3(5, 0, 0))) yield return x;
            var before = _game.Zone.Vessels.ToList();
            foreach (int x in Carry(core, 1, slot)) yield return x;
            foreach (int x in _play.PlaceOnGround(slot, spot)) yield return x;
            Vessel v = _game.Zone.Vessels.FirstOrDefault(n => !before.Contains(n));
            v?.SwitchMode(mode);
            Check(v != null && StructureArea.ClassOf(v) == cls && (v.Structure.Grid.Size == GridSize.Small) == StructureClasses.IsSmallGrid(cls),
                $"{Items.Item(core).Name} in {mode} Mode is a {StructureClasses.Name(cls)}: {v?.Name}");
            if (cls == StructureClass.Small) _small = v;
            else if (cls == StructureClass.Hover) _hover = v;
            else _capital = v;
        }
        if (_small == null || _capital == null) yield break;

        // No block is limited to a mode: what used to be for one class goes on the other.
        foreach (int x in StandAt(_small.Body.Position.ToGodot() + new Vector3(3, 0, 0))) yield return x;
        foreach (int x in Carry(Item("hover", true), 1, 8)) yield return x;
        foreach (int x in _play.Select(8)) yield return x;
        foreach (int x in _drive.LookAt(_small.Body.Position.ToGodot(), 8)) yield return x;
        Check(_game.Builder.TargetVessel == _small && !_game.Builder.Verdict.Contains("only"), $"a hover plate goes on a craft in Vessel Mode: \"{_game.Builder.Verdict}\"");
    }

    IEnumerable<int> Sources()
    {
        GD.Print("-- you choose the source: your stack, or the structure's storage --");
        string steel = Item("steel", false);
        // A container, so the base has a storage; its stock is the base's, not yours.
        foreach (int x in StandByBase(0, 8)) yield return x;
        Pack.Remove(steel, Pack[steel]);
        foreach (int x in Carry(Item("container", false), 1, 2)) yield return x;
        bool on = false;
        foreach (int x in _play.PlaceBlock(2, _base, Int3.Zero, Dir.PosZ, p => on = p)) yield return x;
        foreach (int x in StandByBase(0, 11)) yield return x;
        Check(on && _base.Storage.CapacitySu > 0, "a container gives the base its one shared storage");
        _base.Storage.Add(steel, 5);
        _base.StorageGrid.Reconcile();

        // Your own toolbar uses your stack, and only that.
        foreach (int x in Carry(steel, 2, 2)) yield return x;
        foreach (int x in _play.PlaceBlock(2, _base, new Int3(0, 0, 1), Dir.PosZ, p => on = p)) yield return x;
        Check(on && Pack[steel] == 1 && _base.Storage.Count(steel) == 5, "placed from your toolbar: one from your stack, the base's storage untouched");
        Pack.Remove(steel, Pack[steel]);
        Pack.Grid.Reconcile();
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(Pack.Grid[2].IsEmpty && Pack.Hand == HandKind.Empty, "the stack used up, the slot is empty");
        int count = _base.Structure.Count;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_base.Structure.Count == count && _base.Storage.Count(steel) == 5, "an empty stack can't place, and nothing silently comes from the base's storage");

        // The remote toolbar: set a slot by dragging the stack from the base's storage tab.
        Check(Pack.Linked == _base.Storage && _game.LinkedStructure == _base, "standing in its area, you are linked to the base");
        _game.Open(new OpenContainer { Title = "Base storage", Grid = _base.StorageGrid, Vessel = _base, Where = () => _base.Body.Position.ToGodot() });
        foreach (int x in _drive.Wait(2)) yield return x;
        int stack = _base.StorageGrid.IndexOf(steel);
        foreach (int x in _drive.DragWidget($"box:{stack}", "rbar:2")) yield return x;
        Check(Pack.Remote[2]?.ItemId == steel && Pack.Remote[2].Source == _base.Storage && _base.Storage.Count(steel) == 5, "dragging a stack onto the remote toolbar puts a reference there: nothing moves out of the storage");
        foreach (int x in _play.CloseMenu()) yield return x;
        foreach (int x in _drive.Tap(Key.Y)) yield return x;
        foreach (int x in _drive.Wait(1)) yield return x;
        Check(Pack.OnRemoteBar && Pack.Hand == HandKind.Block && Pack.HandSource == _base.Storage, "Y switches to the remote toolbar: the source is the base's storage");
        foreach (int x in _play.PlaceBlock(2, _base, new Int3(0, 0, 2), Dir.PosZ, p => on = p)) yield return x;
        Check(on && _base.Storage.Count(steel) == 4 && Pack[steel] == 0, "placed from the structure toolbar: one from the base's storage, your bag untouched");

        // No merging the other way: bag holds them, the structure toolbar still says none.
        _base.Storage.Remove(steel, 4);
        Pack.Add(steel, 3);
        foreach (int x in _drive.LookAt(_base.ToWorld(new N.Vector3(0, 0, 7f)), 6)) yield return x;
        Check(_game.Builder.Verdict.Contains("storage"), $"with none in the base's storage the structure toolbar refuses, though the bag holds some: \"{_game.Builder.Verdict}\"");
        count = _base.Structure.Count;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_base.Structure.Count == count && Pack[steel] == 3, "and nothing comes from your bag");
        _base.Storage.Add(steel, 4);
        foreach (int x in _drive.Tap(Key.Y)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(Pack.Bar == BarSource.Personal, "Y again: back on your own toolbar");
        // A number tapped twice switches too.
        foreach (int x in _drive.Wait(40)) yield return x;
        foreach (int x in _drive.Tap(Key.Key3)) yield return x;
        Check(Pack.Bar == BarSource.Personal, "the selected number tapped once stays on your toolbar");
        foreach (int x in _drive.Tap(Key.Key3)) yield return x;
        foreach (int x in _drive.Wait(1)) yield return x;
        Check(Pack.OnRemoteBar, "the selected number tapped twice switches toolbars too");
        foreach (int x in _drive.Tap(Key.Y)) yield return x;
        foreach (int x in _drive.Wait(1)) yield return x;

        // A remote core goes down only while its origin is in reach: a slot from a core you are not near is out of range.
        var far = new SharedStorage(Items, "Far core", 200);
        far.Add("small-core", 1);
        Pack.Bar = BarSource.Remote;
        Pack.SetRemote(6, far, "Far core", "small-core");
        Check(!Pack.RemoteReady(6), "a slot whose core you are not in the area of is out of range");
        foreach (int x in _drive.Tap(Key.Key7)) yield return x;
        foreach (int x in StandByBase(0, 13)) yield return x;
        foreach (int x in _drive.LookAt(_base.ToWorld(new N.Vector3(4, -1f, 16)), 6)) yield return x;
        int all = _game.Zone.Vessels.Count;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_game.Zone.Vessels.Count == all && far.Count("small-core") == 1, $"a core from a storage out of reach can't be set down: \"{_game.Builder.Verdict}\"");
        Pack.Bar = BarSource.Personal;
        Pack.ClearRemote(6);
        foreach (int x in RemoteRules()) yield return x;
        foreach (int x in StorageMenu()) yield return x;
        foreach (int x in DecoBlock()) yield return x;
    }


    /// <summary>The remote toolbar's own rules: empty it can't be opened, it closes with its last item, one slot per item and core, live counts, range, ammo order.</summary>
    IEnumerable<int> RemoteRules()
    {
        GD.Print("-- the remote toolbar's rules --");
        string steel = Item("steel", false), rounds = "pistol-rounds";
        for (int i = 0; i < PlayerInventory.ToolbarSlots; i++) Pack.ClearRemote(i);
        Pack.Bar = BarSource.Personal;
        string refused = Pack.ToggleBar();
        Check(refused != null && Pack.Bar == BarSource.Personal, $"an empty remote toolbar can't be opened: \"{refused}\"");
        var other = new SharedStorage(Items, "Other core", 400);
        Pack.SetRemote(0, _base.Storage, _base.Name, steel);
        Pack.SetRemote(1, other, "Other core", steel);
        Pack.SetRemote(2, _base.Storage, _base.Name, steel);
        Check(Pack.Remote[0] == null && Pack.Remote[1].Source == other && Pack.Remote[2].Source == _base.Storage, "the same item from two cores takes two slots; from one core only one");
        Check(Pack.ColourOf(_base.Storage) != Pack.ColourOf(other), "each core has its own pip colour");
        int had = Pack.Remote[2].Count;
        _base.Storage.Add(steel, 3);
        Check(Pack.Remote[2].Count == had + 3, "a remote slot shows the storage's live count and refills with it");
        _base.Storage.Remove(steel, Pack.Remote[2].Count);
        Check(Pack.Remote[2].Count == 0 && Pack.Remote[2].ItemId == steel, "and falls to zero without losing its place");
        Check(Pack.ToggleBar() == null && Pack.OnRemoteBar, "with items on it the remote toolbar opens");
        Pack.Selected = 1;
        Pack.InRange = s => s == _base.Storage;
        Check(!Pack.HandUsable && !Pack.RemoteReady(1) && Pack.RemoteReady(2), "a slot whose core is out of the area greys out and can't be used");
        Pack.InRange = s => true;
        Pack.ClearRemote(1);
        Pack.ClearRemote(2);
        Check(Pack.Tick() && Pack.Bar == BarSource.Personal, "the remote toolbar closes by itself when its last item goes");
        // Ammo comes from the player first, then the gun's origin, then the nearest container in range.
        Pack.Remove(rounds, Pack[rounds]);
        Pack.Add(rounds, 2);
        _base.Storage.Remove(rounds, _base.Storage.Count(rounds));
        _base.Storage.Add(rounds, 5);
        other.Add(rounds, 5);
        Pack.SetRemote(3, _base.Storage, _base.Name, "survival-tool");
        Pack.Bar = BarSource.Remote;
        Pack.Selected = 3;
        int got = Pack.TakeAmmo(rounds, 6, new[] { other });
        Check(got == 6 && Pack[rounds] == 0 && _base.Storage.Count(rounds) == 1 && other.Count(rounds) == 5, "ammo: the player's two first, then four from the gun's origin");
        got = Pack.TakeAmmo(rounds, 4, new[] { other });
        Check(got == 4 && _base.Storage.Count(rounds) == 0 && other.Count(rounds) == 2, "then the rest from the nearest container in range");
        Pack.ClearRemote(3);
        Pack.Tick();
        Pack.Bar = BarSource.Personal;
        yield return 0;
    }

    /// <summary>The storage menu: a tab per container, drag or hover to send, partial moves say so, selection, double-click, right-drag amounts.</summary>
    IEnumerable<int> StorageMenu()
    {
        GD.Print("-- the storage menu --");
        string steel = Item("steel", false), conc = Item("concrete", false);
        string tab = $"stab:{YouSection.TabKey(_base)}";
        string said = "";
        void Hear(string t, Godot.Color c) => said = t;
        _game.Said += Hear;
        foreach (int x in StandByBase(0, 11)) yield return x;
        Pack.Remove(steel, Pack[steel]);
        Pack.Remove(conc, Pack[conc]);
        _base.Storage.Remove(steel, _base.Storage.Count(steel));
        _base.Storage.Remove(conc, _base.Storage.Count(conc));
        Pack.Add(steel, 20);
        Pack.Add(conc, 6);
        Selection.Clear();
        ItemViews.Get("bag").Clear();
        _game.Menu.Open(MenuTab.You);
        foreach (int x in _drive.ClickWidget("you:fab")) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(_game.Menu.RectOf(tab) != null, "the base's storage has a tab in the menu");
        int steelAt = Pack.Grid.IndexOf(steel), concAt = Pack.Grid.IndexOf(conc);

        // Hold something over a tab and it opens; let go on a slot to place it there.
        _base.StorageGrid.Reconcile();
        int free = Enumerable.Range(0, _base.StorageGrid.Count).First(i => _base.StorageGrid[i].IsEmpty);
        foreach (int x in _drive.DragWidgetVia($"bag:{steelAt}", tab, 40, $"box:{free}")) yield return x;
        Check(_game.Menu.Root.You.CurrentTab == YouSection.TabKey(_base), "hovering a held stack over a storage tab opens it");
        Check(_base.Storage.Count(steel) == 20 && Pack[steel] == 0, "and letting go on its slot places the stack in the base's storage");

        // Drop straight on a tab; right-drag takes half, Ctrl one, the wheel changes the amount.
        steelAt = Pack.Grid.IndexOf(steel);
        foreach (int x in _drive.DragWidget($"bag:{concAt}", tab)) yield return x;
        Check(_base.Storage.Count(conc) == 6 && Pack[conc] == 0, "dropping a stack on a tab sends it there");
        _base.Storage.Remove(steel, 20);
        Pack.Add(steel, 20);
        foreach (int x in _drive.Wait(2)) yield return x;
        steelAt = Pack.Grid.IndexOf(steel);
        foreach (int x in _drive.RightDragWidget($"bag:{steelAt}", tab)) yield return x;
        Check(_base.Storage.Count(steel) == 10 && Pack[steel] == 10, "right-drag takes half a stack");
        foreach (int x in _drive.RightDragWidget($"bag:{Pack.Grid.IndexOf(steel)}", tab, ctrl: true)) yield return x;
        Check(_base.Storage.Count(steel) == 11 && Pack[steel] == 9, "Ctrl + right-drag takes one");
        foreach (int x in _drive.RightDragWidget($"bag:{Pack.Grid.IndexOf(steel)}", tab, wheel: -3)) yield return x;
        Check(_base.Storage.Count(steel) == 11 + 2 && Pack[steel] == 7, $"the wheel changes the amount while holding it (half of 9 is 5, wheel -3 leaves 2; base has {_base.Storage.Count(steel)})");

        // A container that fills takes what fits and says so.
        var tinyStorage = new SharedStorage(Items, "Tiny", 2);
        var tiny = new ItemGrid(tinyStorage, 4, 1, 0, growable: false);
        Pack.Add(steel, 30);
        said = "";
        int moved = Slots.SendStacks(_game, Pack.Grid, new[] { Pack.Grid.IndexOf(steel) }, tiny, int.MaxValue);
        Check(moved < 37 && moved == tinyStorage.Count(steel) && Pack[steel] == 37 - moved && said.Contains("full"), $"a full container takes what fits, the rest stays, and a message says so: moved {moved}, \"{said}\"");

        // Selection: Ctrl+click adds, Shift+click ranges, Ctrl+A takes all that the filter shows.
        Pack.Add(conc, 4);
        Pack.Add("iron-bar", 5);
        Pack.Add("copper-bar", 5);
        foreach (int x in _drive.Wait(2)) yield return x;
        var order = new[] { steel, conc, "iron-bar", "copper-bar" }.Select(i => Pack.Grid.IndexOf(i)).OrderBy(i => i).ToList();
        foreach (int x in _drive.ClickWidgetWith($"bag:{order[0]}")) yield return x;
        foreach (int x in _drive.ClickWidgetWith($"bag:{order[2]}", ctrl: true)) yield return x;
        Check(Selection.Slots.SetEquals(new[] { order[0], order[2] }), "Ctrl+click adds a stack to the selection");
        foreach (int x in _drive.ClickWidgetWith($"bag:{order[1]}", shift: true)) yield return x;
        Check(Selection.Slots.Contains(order[1]) && Selection.Slots.Contains(order[0]), "Shift+click selects the range from the last click");
        ItemViews.Get("bag").Text = "bar";
        foreach (int x in _drive.Wait(2)) yield return x;
        Selection.Clear();
        Check(Slots.SelectAll() && Selection.Slots.Count == 2 && Selection.Slots.All(i => Pack.Grid[i].Item is "iron-bar" or "copper-bar"), $"Ctrl+A selects everything the text filter shows ({Selection.Slots.Count})");
        foreach (int x in _drive.DragWidget($"bag:{Selection.Slots.Min()}", tab)) yield return x;
        Check(_base.Storage.Count("iron-bar") >= 5 && _base.Storage.Count("copper-bar") >= 5 && Pack["iron-bar"] == 0 && Pack["copper-bar"] == 0, "dragging one selected stack sends them all");
        ItemViews.Get("bag").Clear();

        // Double-click sends a stack across to the open container.
        foreach (int x in _drive.ClickWidget(tab)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        int before = _base.Storage.Count(conc), carried = Pack[conc];
        foreach (int x in _drive.DoubleClickWidget($"bag:{Pack.Grid.IndexOf(conc)}")) yield return x;
        Check(carried > 0 && Pack[conc] == 0 && _base.Storage.Count(conc) == before + carried, $"double-click sends the stack across ({carried} moved, {Pack[conc]} left)");
        _game.Said -= Hear;
        Selection.Clear();
        foreach (int x in _play.CloseMenu()) yield return x;
        _base.Storage.Remove(steel, _base.Storage.Count(steel));
        Pack.Remove(steel, Pack[steel]);
    }

    /// <summary>The universal deco block: one item for every piece, a gallery to pick from, pieces on the grid, flat cost, real lights.</summary>
    IEnumerable<int> DecoBlock()
    {
        GD.Print("-- the universal deco block --");
        ItemDef block = Items.Item(DecoCatalogue.UniversalItem);
        Check(block.IsBlock && block.Family == BlockFamily.Deco && block.Recipe.Tier >= 1, $"one deco block item goes on every class of structure and needs a small constructor or better (tier {block.Recipe.Tier})");
        var survival = new Constructor(Items, Items.Machines.Get("survival"), SharedStorage.Unbounded(Items, "t"), new WorldRules());
        var small = new Constructor(Items, Items.Machines.Get("small"), SharedStorage.Unbounded(Items, "t2"), new WorldRules());
        Check(!survival.Knows(block) && small.Knows(block), "the survival tier doesn't make the deco block; the small constructor does");
        var pieces = Items.Items.Where(i => i.IsDecoPiece).ToList();
        Check(pieces.Count > 100 && !block.IsDecoPiece && pieces.All(p => p.Group != null), $"individual deco items ({pieces.Count}) are marked as pieces, kept off the constructor lists");
        Check(Items.Has("ceiling-light@large") && Items.Has("strip-light@small"), "ceiling lights and strip lights are in the deco catalogue");

        // The gallery.
        _game.Progress.Creative = true;
        var tool = _game.Builder;
        int all = tool.DecoChoices(false).Count;
        tool.DecoTheme = "industrial";
        int industrial = tool.DecoChoices(false).Count;
        Check(all > industrial && industrial >= 10 && tool.DecoThemes(false).Count > 5, $"the gallery filters by theme ({all} pieces, {industrial} industrial, {tool.DecoThemes(false).Count} themes)");
        tool.DecoTheme = null;

        // Pieces go on the grid: floor, wall and ceiling, and nothing overlaps.
        var deco = Decor.Shared.Catalogue;
        float cell = _base.Structure.Grid.CellSize;
        var crate = deco.Piece("cargo-crate");
        var onFloor = tool.PlanDeco(_base, crate, Int3.Zero, new Int3(0, 1, 0), 0);
        Check(onFloor.Why == null && onFloor.Cells.Count == 1 && Math.Abs(onFloor.Local.Origin.Y - cell * 0.5f) < 0.01f, $"a floor piece stands on the top face, one grid cell: {onFloor.Why} (y {onFloor.Local.Origin.Y})");
        var light = deco.Piece("ceiling-light");
        var up = tool.PlanDeco(_base, light, new Int3(0, 3, 0), new Int3(0, 2, 0), 0);
        Check(up.Why == null && Math.Abs(up.Local.Origin.Y - (2 * cell + cell * 0.5f - light.Height)) < 0.01f, $"a ceiling light hangs from the underside of the block above ({up.Why})");
        Check(tool.PlanDeco(_base, light, Int3.Zero, new Int3(0, 1, 0), 0).Why?.Contains("ceiling") == true, "a ceiling piece is refused on a floor face");
        Check(tool.PlanDeco(_base, crate, new Int3(0, 3, 0), new Int3(0, 2, 0), 0).Why?.Contains("floor") == true, "a floor piece is refused under a ceiling");
        var panel = deco.Piece("junction-box");
        var wall = tool.PlanDeco(_base, panel, new Int3(5, 1, 0), new Int3(4, 1, 0), 0);
        Check(wall.Why == null && wall.Cells.Count >= 1, $"a wall piece mounts against a side face ({wall.Why})");

        // Placing through the game: one block each, flat, whatever the piece.
        foreach (int x in StandByBase(0, 8)) yield return x;
        Pack.Remove(block.Id, Pack[block.Id]);
        foreach (int x in Carry(block.Id, 2, 5)) yield return x;
        tool.DecoPieceId = "junction-box";
        int zmax = Enumerable.Range(0, 12).Where(z => _base.Structure.Has(new Int3(0, 0, z))).Max();
        tool.DecoYaw = 0;
        int decos = _base.Decos.Count;
        foreach (int x in _play.PlaceBlock(5, _base, new Int3(0, 0, zmax), Dir.PosZ)) yield return x;
        Check(_base.Decos.Count == decos + 1 && Pack[block.Id] == 1, $"a click sets the chosen piece on the grid and uses one deco block ({_base.Decos.Count - decos} placed, {Pack[block.Id]} blocks left; \"{tool.Verdict}\")");
        foreach (int x in _play.PlaceBlock(5, _base, new Int3(0, 0, zmax), Dir.PosZ)) yield return x;
        Check(_base.Decos.Count == decos + 1 && Pack[block.Id] == 1, "the same cell can't take a second piece, and no block is spent");
        DecoPlaced placed = _base.Decos.Last();
        _base.RemoveDeco(placed);
        Check(_base.Decos.Count == decos && !_base.DecoAt(placed.Cells[0], out _), "a piece taken down frees its cells");

        // Lights are real, and follow the structure's light switch.
        var lamp = tool.PlanDeco(_base, light, new Int3(0, 3, 0), new Int3(0, 2, 0), 0);
        DecoPlaced lit = _base.AddDeco("ceiling-light", new Int3(0, 2, 0), 0, lamp.Cells, lamp.Local);
        var lights = lit.Node.GetChildren().OfType<Light3D>().ToList();
        Check(lights.Count > 0 && lights.All(l => l.Visible), $"a ceiling light gives real light ({lights.Count} lamp)");
        _base.LightsOn = false;
        Check(lights.All(l => !l.Visible), "and goes dark with the structure's lights");
        _base.LightsOn = true;
        _base.RemoveDeco(lit);
        _game.Progress.Creative = false;
        Pack.Remove(block.Id, Pack[block.Id]);
    }
    IEnumerable<int> BackIntoInventories()
    {
        GD.Print("-- taking down: nothing is lost --");
        string steel = Item("steel", false);
        // The core stays, however little is left on it.
        var (coreCell, coreBlock) = _base.Structure.Blocks().First(c => _base.Structure.TypeOf(c.block).IsCore);
        int vessels = _game.Zone.Vessels.Count, coreItems = PlayerInventory.CoreItems.Sum(id => Pack[id]);
        _game.Builder.Remove(_base, coreCell);
        Check(_base.Structure.Has(coreCell) && _game.Zone.Vessels.Count == vessels && PlayerInventory.CoreItems.Sum(id => Pack[id]) == coreItems,
            $"deconstruct refuses a core ({_base.Structure.TypeOf(coreBlock).Name}): it can only be destroyed or hijacked");
        foreach (int x in _play.Tool(ToolMode.Deconstruct)) yield return x;
        N.Vector3 Face(Int3 c) => StructureBridge.CellCenter(_base.Structure, c) + new N.Vector3(1f, 0, 0);
        foreach (int x in StandByBase(6, 4)) yield return x;

        // To the bag.
        int bag = Pack[steel];
        foreach (int x in _drive.LookAt(_base.ToWorld(Face(new Int3(0, 0, 3))), 8)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(!_base.Structure.Has(new Int3(0, 0, 3)) && Pack[steel] == bag + 1, "deconstruct gives the block's item back to your bag");

        // A full bag: to the base's storage.
        Pack.Storage.Add("stone", Pack.Storage.RoomFor("stone"));
        Pack.Grid.Reconcile();
        int stored = _base.Storage.Count(steel);
        foreach (int x in _drive.LookAt(_base.ToWorld(Face(new Int3(0, 0, 2))), 8)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(!_base.Structure.Has(new Int3(0, 0, 2)) && _base.Storage.Count(steel) == stored + 1 && Pack[steel] == bag + 1, "with a full bag, it goes to the structure's storage");

        // Both full: a container on the ground.
        double room = _base.Storage.FreeSu;
        _base.Storage.Add("stone", (int)(room / Items.Item("stone").VolumeSu));
        int spills = _game.Zone.Loot.All.Count();
        foreach (int x in _drive.LookAt(_base.ToWorld(StructureBridge.CellCenter(_base.Structure, new Int3(1, 0, 0)) + new N.Vector3(0, 0, 1f)), 8)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_game.Zone.Loot.All.Count() == spills + 1 && _game.Zone.Loot.All.Any(l => l.Grid.Storage.Count(steel) == 1), "with both full, it goes into a spill container on the ground: nothing lost");
        Pack.Storage.Remove("stone", Pack.Storage.Count("stone"));
        _base.Storage.Remove("stone", _base.Storage.Count("stone"));
        Pack.Grid.Reconcile();
        foreach (int x in _play.Tool(ToolMode.Gather)) yield return x;
    }

    /// <summary>No creature spawns within the clearance of a base, from its footprint's edge; a vessel keeps nothing away.</summary>
    void NoSpawnsByBases()
    {
        if (_base == null) return;
        GD.Print("-- no creature spawns near a base --");
        var patch = _game.Zone.Wildlife;
        patch.SyncBases();
        Wildlife.WildSim sim = patch.Sim;
        float clearance = sim.Data.Spawning.BaseClearance;
        Check(clearance > 0 && sim.Structures.Count(s => s.IsBase) == _game.Zone.Vessels.Count(v => v.IsBase && v.Structure.Core != null),
            $"the wildlife sim knows every base ({sim.Structures.Count}), and keeps {clearance:F0} m clear of each");
        var (min, max) = _base.Structure.Bounds();
        Vector3 Edge(float past) => patch.PatchLocal(_base.ToWorld(StructureBridge.CellCenter(_base.Structure, max) + new N.Vector3(_base.Structure.Grid.CellSize * 0.5f + past, 0, 0)));
        Check(sim.InBaseZone(patch.PatchLocal(_base.ToWorld(StructureBridge.CellCenter(_base.Structure, min)))) && sim.InBaseZone(Edge(clearance - 2f)) && !sim.InBaseZone(Edge(clearance + 2f)),
            "the zone runs from the footprint's edge, so a bigger base has a bigger zone");
        Wildlife.Species species = patch.Fauna.Species.First(s => s.Domestic == Wildlife.DomesticKind.None);
        int creatures = sim.Creatures.Count;
        var spawned = sim.SpawnGroup(species, Edge(4f), 6, null);
        Check(spawned.Count == 0 && sim.Creatures.Count == creatures, "a group set down by the base doesn't spawn");
        if (_capital != null)
            Check(!sim.Structures.Any(s => s.Name == _capital.Name), "a capital vessel keeps nothing away");
    }

    IEnumerable<int> Areas()
    {
        GD.Print("-- a structure's storage works inside its area --");
        NoSpawnsByBases();
        var rules = _game.Zone.Data.Rules.Build.Area;
        foreach (Vessel v in new[] { _base, _capital, _small, _hover })
        {
            if (v == null) continue;
            float margin = StructureArea.Margin(v, rules);
            var (min, max) = v.Structure.Bounds();
            N.Vector3 edge = StructureBridge.CellCenter(v.Structure, max) + new N.Vector3(v.Structure.Grid.CellSize * 0.5f, 0, 0);
            Vector3 inside = v.ToWorld(edge + new N.Vector3(margin - 0.25f, 0, 0)), outside = v.ToWorld(edge + new N.Vector3(margin + 0.25f, 0, 0));
            Check(_game.Builder.CanDrawFrom(v, inside) && !_game.Builder.CanDrawFrom(v, outside),
                $"{StructureClasses.Name(StructureArea.ClassOf(v))}: its storage can be drawn on {margin - 0.25f:F2} m from its blocks, refused at {margin + 0.25f:F2} m");
        }
        Check(rules.Margin(StructureClass.Base) > rules.Margin(StructureClass.Small), "a base reaches further than a small vessel");

        // Out of every area: nothing linked, the CORE tab says so.
        if (_base != null)
        {
            Vector3 far = _base.Body.Position.ToGodot() + new Vector3(StructureArea.Margin(_base, rules) + 80f, 0, 0);
            foreach (int x in StandAt(far)) yield return x;
            var out_ = StructureArea.Around(_game.Zone, _game.Survivor.WorldFeet, rules);
            foreach (int x in _play.OpenMenu(MenuTab.Core)) yield return x;
            foreach (int x in _drive.Wait(2)) yield return x;
            Check(!_game.Menu.Root.Core.Near().Contains(_base), $"outside its area the CORE tab doesn't list the base ({out_.Count} structures in range)");
            foreach (int x in _play.CloseMenu()) yield return x;
            if (out_.Count == 0) Check(Pack.Linked == null, "outside every area nothing is linked");
        }

        // A vessel parked on a base: the structure you build on wins.
        if (_base != null)
        {
            // The small vessel's core started 30 m off: inside the base's area, and the base inside neither.
            Vector3 point = _small.Body.Position.ToGodot() + new Vector3(0, -1, 3);
            Check(StructureArea.Contains(_base, point, rules) && StructureArea.Contains(_small, point, rules), "beside a vessel parked by a base you are in both their areas");
            Check(StructureArea.Linked(_game.Zone, point, rules, _small) == _small && StructureArea.Linked(_game.Zone, point, rules, _base) == _base, "the one you are building on is the one you link to");
            foreach (int x in StandAt(point)) yield return x;
            foreach (int x in _play.OpenMenu(MenuTab.Core)) yield return x;
            foreach (int x in _drive.Wait(2)) yield return x;
            List<object> near = _game.Menu.Root.Core.Near();
            Check(near.Contains(_base) && near.Contains(_small), "and the CORE tab lets you pick either");
            foreach (int x in _play.CloseMenu()) yield return x;
        }
    }

    IEnumerable<int> Machines()
    {
        GD.Print("-- constructors keep to their own storage --");
        _game.CraftTimeScale = 40f;
        foreach (int x in StandByBase(0, 11)) yield return x;
        // A small constructor and a generator on the base, from its own storage, on the structure toolbar.
        string fab = Item("smallfab", false), gen = Item("generator", false);
        _game.Progress.Creative = true;
        _base.Storage.Add(fab, 1);
        _base.Storage.Add(gen, 1);
        Pack.SetRemote(4, _base.Storage, _base.Name, fab);
        Pack.SetRemote(5, _base.Storage, _base.Name, gen);
        foreach (int x in _drive.Tap(Key.Y)) yield return x;
        bool on = false;
        foreach (int x in _play.PlaceBlock(4, _base, new Int3(0, 0, 1), Dir.PosZ, p => on = p)) yield return x;
        foreach (int x in StandByBase(7, 1)) yield return x;
        foreach (int x in _play.PlaceBlock(5, _base, Int3.Zero, Dir.PosX, p => on &= p)) yield return x;
        foreach (int x in _drive.Tap(Key.Y)) yield return x;
        Check(on && _base.Machines.Count == 1 && _base.Powered, "a constructor and a generator built on the base from its own storage");
        _game.Progress.Creative = false;
        Constructor c = _base.Machines.Values.First();
        Bill ores = Bill.Of(("iron-ore", 60), ("carbon-ore", 60));
        _base.Storage.TryAdd(ores);
        Pack.Add("iron-ore", 40);
        Pack.Add("biomass", 4);
        Bill bag = Pack.Storage.Contents;
        string steel = Item("steel", false);
        int before = _base.Storage.Count(steel);
        Check(c.Order(steel, 1).Ok, "the base's constructor takes an order for a steel block");
        foreach (int x in _play.WaitFor(() => !c.Busy, 60)) yield return x;
        Check(_base.Storage.Count(steel) == before + 1, "it lands in the base's one shared storage");
        Check(Pack.Storage.Contents.Equals(bag), "and your bag is untouched: constructors never reach into it");
        Bill baseStock = _base.Storage.Contents;
        Check(_game.Fabricator.Order("trail-ration", 1).Ok, "the suit fabricator takes an order");
        foreach (int x in _play.WaitFor(() => !_game.Fabricator.Busy, 30)) yield return x;
        Check(Pack["trail-ration"] >= 1 && _base.Storage.Contents.Equals(baseStock), "it works from your bag only: the base's storage is untouched");
        // Auto sub-parts in the fabricator: ask for what needs a sub-part, with only raw finds.
        Check(_game.Fabricator.Blockers.Count == 0, "and has nothing blocking it");
        Constructor f = _game.Fabricator;
        f.Lock = i => i.Id == "patch-kit" ? "locked: level 9" : null;
        Pack.Remove("biomass", Pack["biomass"]);
        _game.CraftTimeScale = 1f;
        OrderResult r = f.Order("air-cell", 1);
        Check(r.Ok && f.Blockers.Any(b => b.Contains("Biomass") && b.Contains("not in storage")), $"a missing raw material in the fabricator is reported: {string.Join("; ", f.Blockers)}");
        f.Cancel(r.Job);
        f.Lock = null;
        _game.CraftTimeScale = 40f;
    }

    /// <summary>The tech tree's tabs (Base &amp; Capital, Small &amp; Hover, tools, weapons, deco, misc) and the hover gate, on a fresh character.</summary>
    void TechTabs()
    {
        GD.Print("-- the tech tree: one tab for Large (Big Core), one for Small (Small Core) --");
        var data = _game.Zone.Data;
        var fresh = new Progress(data.Progress, data.Items, data.Catalogue);
        Check(Progress.Tabs.SequenceEqual(new[] { "large", "small", "tools", "weapons", "misc" }), $"five tabs: {string.Join(", ", Progress.Tabs)}");
        var stray = fresh.Nodes.Where(n => !Progress.Tabs.Contains(n.Tab)).Select(n => $"{n.Id}:{n.Tab}").ToList();
        Check(stray.Count == 0, $"every node sits on one of them{(stray.Count > 0 ? ": " + string.Join(", ", stray) : "")}");
        Check(fresh.Nodes.Select(n => n.Id).Distinct().Count() == fresh.Nodes.Count, "no node is listed twice (a block shared by a base and a capital vessel is one node)");
        Check(fresh.Node("thruster@large").Tab == "large" && fresh.Node("big-core").Tab == "large" && fresh.Node("thruster@large").Unlock.Parent == "big-core", "the Big Core heads the Large tab, the large thruster under it");
        Check(fresh.Node("hover@small").Tab == "small" && fresh.Node("small-core").Tab == "small" && fresh.Node("hover@small").Unlock.Parent == "small-core", "the Small Core heads the Small tab, the hover plate under it");
        Check(fresh.Node("thruster@small").Unlock.Parent == "hover@small", "hover plates are the thruster's prerequisite on Small");
        Check(fresh.Level == 1 && fresh.Points == 3, $"a new character starts at level 1 with 3 unlock points ({fresh.Points})");
        Check(fresh.Unlock("small-core") && fresh.Unlock("hover@small") && fresh.Points == 1, "the Small Core and the hover plate both open at level 1, with a point to spare");
        Check(fresh.StateOf(fresh.Node("thruster@small")) == UnlockState.NeedsLevel, "and the thruster behind them waits for its level");
        fresh.Earn(fresh.Rules.XpFor(5), "test");
        Check(fresh.StateOf(fresh.Node("thruster@small")) == UnlockState.Affordable && fresh.Unlock("thruster@small"), $"at level {fresh.Level} the thruster unlocks with its hover plate in hand");
    }

    /// <summary>In Survival: the CPU extender can be unlocked, crafted in the base's own constructor, placed, and it raises the structure's CPU tier.</summary>
    IEnumerable<int> CpuExtender()
    {
        GD.Print("-- the CPU extender: unlock, craft, place, a higher CPU tier --");
        Check(_game.Mode == GameMode.Survival && !_game.Progress.Creative, "in Survival");
        TechNode node = _game.Progress.NodeOfBlock("cpuext", false);
        Check(node != null, "the CPU extender is a node on the tech tree");
        if (node == null) yield break;
        // Level it up by playing (XP), then buy it and its parents with the points that brings.
        _game.Progress.Earn(1_000_000, "test");
        var chain = new List<TechNode>();
        for (TechNode n = node; n != null; n = _game.Progress.Node(n.Unlock.Parent)) chain.Insert(0, n);
        foreach (TechNode n in chain)
            if (!_game.Progress.IsUnlocked(n.Id)) _game.Progress.Unlock(n.Id);
        Check(_game.Progress.IsUnlocked(node.Id), $"unlocked on the tech tree through {string.Join(" > ", chain.Select(n => n.Id))}");
        string ext = Item("cpuext", false);
        ItemDef def = Items.Item(ext);
        Constructor c = _base.Machines.Values.First();
        Check(c.Knows(def) && _game.Progress.ItemAllowed(def), $"the base's {c.Def.Name} (tier {c.Def.Tier}) knows the recipe (tier {def.Recipe.Tier}) and it is allowed");
        // Its parts come out of the constructor's own lane; stock them as the earlier steps would have made them.
        foreach (var (id, n) in def.Recipe.Inputs) _base.Storage.Add(id.Id, n);
        int before = _base.Storage.Count(ext);
        Check(c.Order(ext, 1).Ok, "the constructor takes an order for the CPU extender");
        foreach (int x in _play.WaitFor(() => !c.Busy, 60)) yield return x;
        Check(_base.Storage.Count(ext) == before + 1, "and makes it into the base's storage");
        int tier = _base.StructureStats.Tier, extenders = _base.StructureStats.Extenders;
        Pack.SetRemote(6, _base.Storage, _base.Name, ext);
        _base.Storage.Add(ext, 0);
        foreach (int x in _drive.Tap(Key.Y)) yield return x;
        bool on = false;
        foreach (int x in StandByBase(7, 1)) yield return x;
        foreach (int x in _play.PlaceBlock(6, _base, new Int3(1, 0, 0), Dir.PosX, p => on = p)) yield return x;
        foreach (int x in _drive.Tap(Key.Y)) yield return x;
        Check(on && _base.StructureStats.Extenders == extenders + 1, $"placed on the base: {_base.StructureStats.Extenders} extender(s)");
        // The tiers climb with the count (one extender is the second tier); add the rest until it does.
        Check(_base.StructureStats.Tier > tier || _base.StructureStats.Extenders < _base.Structure.Catalogue.ExtendersPerTier[Math.Min(tier, _base.Structure.Catalogue.ExtendersPerTier.Length - 1)],
            $"CPU tier {tier} -> {_base.StructureStats.Tier}, limit {_base.StructureStats.CpuLimit}");
    }

    /// <summary>The lattice extenders are real blocks, but no amount of ore pays for one: the parts are found in places.</summary>
    void LatticeGate()
    {
        foreach (string block in new[] { "cpuext3", "cpuext4" })
        {
            ItemDef def = Items.Item(Item(block, false));
            Check(def.Recipe.InputsFor(1).Ids.Any(id => Items.Item(id).IsUnique && Items.Item(id).Recipe == null), $"{def.Name} needs a part found in places");
            Check(_base.Structure.Catalogue.Type(block).IsCpuExtender, $"{def.Name} is a CPU extender on the structure");
        }
    }

    IEnumerable<int> MenuChecks()
    {
        GD.Print("-- filters, typing and tooltips on the menu --");
        Pack.Remove("biomass", Pack["biomass"]);
        Pack.Add("iron-ore", 20);
        Pack.Add("hull-plate", 3);
        Pack.Add(Item("steel", false), 2);
        foreach (int x in _play.OpenMenu(MenuTab.You)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(_game.Menu.RectOf("bag:filter") != null && _game.Menu.RectOf("bag:sort") != null && _game.Menu.RectOf("bag:cat:blocks") != null
              && _game.Menu.RectOf("recipes:fab:filter") != null, "the bag and the fabricator's list have a text box, a sort and category toggles");
        foreach (int x in _drive.ClickWidget("bag:filter")) yield return x;
        Check(ItemViews.Focused == "bag", "a click gives the box the keyboard");
        Vector3 feet = _game.Survivor.WorldFeet;
        _game.Controls.Hold(Key.W);
        _game.Controls.Type("iron ore");
        foreach (int x in _drive.Wait(30)) yield return x;
        _game.Controls.Hold(Key.W, false);
        ItemView bag = ItemViews.Get("bag");
        Check(bag.Text == "iron ore", "typing goes to the box");
        Check(_game.Survivor.WorldFeet.DistanceTo(feet) < 0.1f, "and doesn't move you");
        IReadOnlyList<int> shown = bag.Visible(Pack.Grid, PlayerInventory.ToolbarSlots);
        Check(shown.Count > 0 && shown.All(i => Pack.Grid[i].Item == "iron-ore"), "the bag shows only iron ore");
        _game.Controls.Tap(Key.Escape);
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(bag.Text == "" && _game.Menu.IsOpen, "Escape clears the box");
        _game.Controls.Tap(Key.Escape);
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(ItemViews.Focused == null && _game.Menu.IsOpen, "again, it lets go of the keyboard");
        _game.Controls.Tap(Key.Escape);
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(!_game.Menu.IsOpen, "again, the menu closes");

        // Tooltips: hover over a stack through the game's mouse.
        foreach (int x in _play.OpenMenu(MenuTab.You)) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        foreach (int x in _drive.Tap(Key.Slash)) yield return x;
        foreach (int x in _drive.Wait(1)) yield return x;
        Check(ItemViews.Focused == "bag", "the / key gives the page's first filter box the keyboard");
        _game.Controls.Tap(Key.Escape);
        foreach (int x in _drive.Wait(2)) yield return x;
        List<(string Text, Color Colour)> Tip() => _game.Menu.Root.Gui.LastTip;
        string All() => Tip() == null ? "" : string.Join(" / ", Tip().Select(t => t.Text));
        IEnumerable<int> Hover(string itemId)
        {
            int slot = _play.BagSlotOf(itemId);
            Rect2 r = _game.Menu.RectOf($"bag:{slot}") ?? new Rect2();
            _game.Controls.Mouse = r.GetCenter();
            foreach (int x in _drive.Wait(3)) yield return x;
        }
        foreach (int x in Hover(Item("steel", false))) yield return x;
        string block = All();
        GD.Print($"  (a block's tooltip: {block})");
        Check(block.Contains("Steel block (large)") && block.Contains("8 Hull plate") && block.Contains("large grid") && block.Contains("Stacks to 250") && block.Contains("stack of "),
            "a block's tooltip: name, recipe, stack, the grid it fits, the whole stack's mass");
        foreach (int x in Hover("iron-ore")) yield return x;
        string ore = All();
        GD.Print($"  (an ore's tooltip: {ore})");
        Check(ore.Contains("Iron ore") && !ore.Contains("Made from") && (ore.Contains("Found") || ore.Contains("Mined")), "an ore's tooltip says where it is found, not how it is made");
        foreach (int x in Hover("hull-plate")) yield return x;
        string part = All();
        GD.Print($"  (a part's tooltip: {part})");
        Check(part.Contains("Hull plate") && part.Contains("Made from") && part.Contains("constructor") && part.Contains(" s a run"), "a crafted part's tooltip: its inputs, its constructor and its time");
        ItemDef locked = Items.BlockItem("combat", false);
        var lines = Tooltips.For(_game, locked, 0);
        Check(lines.Any(l => l.Text.Contains("Locked: level 12")), "a locked item's tooltip says at which level");
        Rect2 box = Gui.TipBox(new Rect2(0, 0, 1600, 900), new Rect2(1560, 860, 40, 40), lines);
        Check(new Rect2(0, 0, 1600, 900).Encloses(box), "a tooltip at the corner of the screen stays on it");
        _game.Controls.Mouse = Vector2.Zero;
        foreach (int x in _play.CloseMenu()) yield return x;
    }

    IEnumerable<int> Creative()
    {
        GD.Print("-- Creative: free, unlimited, every block; the same rules of structure --");
        _game.Mode = GameMode.Creative;
        foreach (int x in StandByBase(0, 12)) yield return x;
        string steelS = Item("steel", true);
        Pack.Grid.SetBlock(2, Item("steel", false));
        Pack.Grid.SetBlock(3, steelS);
        Bill bag = Pack.Storage.Contents;
        bool on = false;
        int count = _base.Structure.Count;
        foreach (int x in _play.PlaceBlock(2, _base, new Int3(0, 0, 2), Dir.PosZ, p => on = p)) yield return x;
        Check(on && Pack.Storage.Contents.Equals(bag), "a block from the palette places free: nothing used");
        foreach (int x in _play.Select(3)) yield return x;
        foreach (int x in _drive.LookAt(_base.ToWorld(new N.Vector3(0, 0, 7f)), 8)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_base.Structure.Count == count + 1 && _game.Builder.Verdict.Contains("small-grid block"), "and the same grid rule applies: a small-grid block is refused on a base");
        _game.Mode = GameMode.Survival;
    }
}
