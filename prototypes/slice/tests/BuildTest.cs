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

    public override void _Ready()
    {
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

        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        _play = new Playthrough(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Wildlife.Frozen = true;
        GD.Print("  (systems test: items are handed over and the castaway teleported where a playthrough would spend minutes)");
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
        foreach (int x in Founding()) yield return x;
        foreach (int x in Sources()) yield return x;
        foreach (int x in BackIntoInventories()) yield return x;
        foreach (int x in Areas()) yield return x;
        foreach (int x in Machines()) yield return x;
        foreach (int x in MenuChecks()) yield return x;
        foreach (int x in Creative()) yield return x;
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

    Vector3 Ground(Vector3 at) => _game.Zone.GroundAt(at.X, at.Z) + Vector3.Up * 0.1f;

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
        _site = _play.FlatSpot(_game.Start, 70f, 0.6f, 0.4f);
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

        foreach (int x in Carry("base-core", 1, 4)) yield return x;
        foreach (int x in _play.PlaceOnGround(4, _site)) yield return x;
        _base = _game.Zone.Vessels.FirstOrDefault(v => v.IsBase && !v.IsRuin);
        Check(_base != null && StructureArea.ClassOf(_base) == StructureClass.Base && _base.Structure.Grid.Size == GridSize.Large && Pack["base-core"] == 0,
            "a base core founds a base (large grid, anchored), and is used up");
        if (_base == null) yield break;

        // A small-grid block is refused on a base, with a clear message.
        foreach (int x in _play.Select(3)) yield return x;
        foreach (int x in _drive.LookAt(_base.ToWorld(new N.Vector3(1f, 0, 0)), 8)) yield return x;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_base.Structure.Count == 1 && Pack[steelS] == 12 && _game.Builder.Verdict.Contains("blocks only"), $"a small-grid block is refused on a base: \"{_game.Builder.Verdict}\"");

        // Building onto the founded structure works.
        bool on = false;
        foreach (int x in StandByBase(7, 1)) yield return x;
        foreach (int x in _play.PlaceBlock(2, _base, Int3.Zero, Dir.PosX, p => on = p)) yield return x;
        Check(on && Pack[steelL] == 11, "a large block goes onto the base's core, one item used");

        // A hover core, a small-vessel core and a capital core found their own classes.
        foreach (var (core, cls, slot, offset) in new[] { ("vessel-core", StructureClass.Small, 5, new Vector3(20, 0, 0)), ("hover-core", StructureClass.Hover, 6, new Vector3(-20, 0, 0)), ("capital-core", StructureClass.Capital, 7, new Vector3(0, 0, 40)) })
        {
            Vector3 spot = _play.FlatSpot(_site + offset, 4f, 1.4f, 0.5f);
            foreach (int x in StandAt(spot + new Vector3(5, 0, 0))) yield return x;
            var before = _game.Zone.Vessels.ToList();
            foreach (int x in Carry(core, 1, slot)) yield return x;
            foreach (int x in _play.PlaceOnGround(slot, spot)) yield return x;
            Vessel v = _game.Zone.Vessels.FirstOrDefault(n => !before.Contains(n));
            Check(v != null && StructureArea.ClassOf(v) == cls && (v.Structure.Grid.Size == GridSize.Small) == StructureClasses.IsSmallGrid(cls),
                $"{Items.Item(core).Name} founds a {StructureClasses.Name(cls)}: {v?.Name}");
            if (cls == StructureClass.Small) _small = v;
            else if (cls == StructureClass.Hover) _hover = v;
            else _capital = v;
        }
        if (_small == null || _capital == null) yield break;

        // Devices per class.
        foreach (int x in StandAt(_small.Body.Position.ToGodot() + new Vector3(3, 0, 0))) yield return x;
        foreach (int x in Carry(Item("concrete", false), 1, 8)) yield return x;
        foreach (int x in _play.Select(8)) yield return x;
        foreach (int x in _drive.LookAt(_small.Body.Position.ToGodot(), 8)) yield return x;
        Check(_game.Builder.TargetVessel == _small && _game.Builder.Verdict.Contains("base blocks only"), $"a base-only block is refused on a small vessel: \"{_game.Builder.Verdict}\"");
        ItemDef pad = Items.BlockItem("hover", true), decon = Items.BlockItem("decon", false);
        Check(!pad.PlacesOn(StructureClass.Small) && pad.PlacesOn(StructureClass.Hover) && decon.PlacesOn(StructureClass.Base) && !decon.PlacesOn(StructureClass.Capital),
            "a hover pad only goes on a hover vessel, a large deconstructor only on a base");
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

        // The structure toolbar: set a slot by dragging the stack from the base's storage.
        Check(Pack.Linked == _base.Storage && _game.LinkedStructure == _base, "standing in its area, you are linked to the base");
        _game.Open(new OpenContainer { Title = "Base storage", Grid = _base.StorageGrid, Vessel = _base, Where = () => _base.Body.Position.ToGodot() });
        foreach (int x in _drive.Wait(2)) yield return x;
        int stack = _base.StorageGrid.IndexOf(steel);
        foreach (int x in _drive.DragWidget($"box:{stack}", "sbar:2")) yield return x;
        Check(Pack.StructureBar[2] == steel && _base.Storage.Count(steel) == 5, "dragging a stack onto the structure toolbar sets the slot: nothing moves");
        foreach (int x in _play.CloseMenu()) yield return x;
        foreach (int x in _drive.Tap(Key.Y)) yield return x;
        foreach (int x in _drive.Wait(1)) yield return x;
        Check(Pack.OnStructureBar && Pack.Hand == HandKind.Block && Pack.HandSource == _base.Storage, "Y switches to the structure toolbar: the source is the base's storage");
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
        Check(Pack.OnStructureBar, "the selected number tapped twice switches toolbars too");
        foreach (int x in _drive.Tap(Key.Y)) yield return x;
        foreach (int x in _drive.Wait(1)) yield return x;

        // A core goes down from your own stack only: off a structure, its storage doesn't count.
        Pack.Bar = BarSource.Structure;
        _base.Storage.Add("vessel-core", 1);
        Pack.SetStructureSlot(6, "vessel-core");
        foreach (int x in _drive.Tap(Key.Key7)) yield return x;
        foreach (int x in StandByBase(0, 13)) yield return x;
        foreach (int x in _drive.LookAt(_base.ToWorld(new N.Vector3(4, -1f, 16)), 6)) yield return x;
        int all = _game.Zone.Vessels.Count;
        foreach (int x in _drive.Click()) yield return x;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_game.Zone.Vessels.Count == all && _base.Storage.Count("vessel-core") == 1, $"a block or core away from any structure can't draw on the base's storage: \"{_game.Builder.Verdict}\"");
        Pack.Bar = BarSource.Personal;
        _base.Storage.Remove("vessel-core", 1);
    }

    IEnumerable<int> BackIntoInventories()
    {
        GD.Print("-- taking down: nothing is lost --");
        string steel = Item("steel", false);
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

    IEnumerable<int> Areas()
    {
        GD.Print("-- a structure's storage works inside its area --");
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
        Pack.SetStructureSlot(4, fab);
        Pack.SetStructureSlot(5, gen);
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
        Check(block.Contains("Steel block (large)") && block.Contains("8 Hull plate") && block.Contains("large grid") && block.Contains("base") && block.Contains("capital vessel") && block.Contains("Stacks to 250") && block.Contains("stack of "),
            "a block's tooltip: name, recipe, stack, the grid and classes it fits, the whole stack's mass");
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
        Check(_base.Structure.Count == count + 1 && _game.Builder.Verdict.Contains("blocks only"), "and the same class rules apply: a small-grid block is refused on a base");
        _game.Mode = GameMode.Survival;
    }
}
