using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Items;
using Slice.Player;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using StructureClass = GalHack.Items.StructureClass;

namespace Slice.Construction;

/// <summary>What placing something new starts.</summary>
public enum NewThing { None, Base, Vessel, Hover, Device, Capital }

/// <summary>
/// Building with what is on the toolbar, on building's structure model and rules (attach through
/// a shared face, one core, nothing lost when something is cut loose), its ghosts and mesher,
/// and shared/Items' rule that blocks are craftable items (Empyrion's way).
///
/// A block family item in hand places that block, in any of its shapes (wheel: shape, R / T
/// rotate, X mirror), on the face under the crosshair, and only on a structure: every structure
/// starts with its core. It goes only on the grid and the classes of structure its family allows
/// (Empyrion's base, capital, small and hover). In Survival each placement uses one item from the
/// source you chose: your own toolbar's stack, or, on the structure toolbar, the shared storage of
/// the structure you are building on while you are in its area. An empty stack can't place.
/// Taking a block down (the survival tool's deconstruct mode, or right click with a block in
/// hand) gives its item back, whole: to your bag (the structure's storage when you build from the
/// structure toolbar), then the structure's storage, then a container on the ground. Creative is
/// free and unlimited, with every block available, and follows the same core-first and class rules.
///
/// A core item in hand founds a new structure of its class on open ground (a base core a base,
/// fixed where it stands; a small-vessel, hover or capital core a vessel on a build stand). A
/// device item (the survival constructor) is set down as itself. The blueprint projector is not
/// on the toolbar: the CORE tab starts it (<see cref="Project"/>), and it builds the Skiff block by
/// block while the button is held, each block one item from your bag.
/// </summary>
public sealed class BuildTool
{
    readonly SurfaceZone _zone;
    readonly Survivor _survivor;
    readonly PlayerInventory _pack;
    readonly SliceRules.BuildRules _rules;
    readonly GhostPreview _ghosts;
    readonly MeshInstance3D _planGhost, _deviceGhost;
    readonly Catalogue _catalogue;
    readonly BlockPlacer _placer;
    public Progress Progress;

    /// <summary>The blueprint projector is running (started from the CORE tab, not the toolbar).</summary>
    public bool Projecting { get; private set; }

    public int ShapeIndex;
    public int Rot = Orientations.Identity;
    public bool MirrorX;

    /// <summary>The blueprint being followed and the vessel growing from it.</summary>
    public Structure Plan { get; private set; }
    public Vessel Underway { get; private set; }

    /// <summary>Blocks of the plan still to place, nearest the core first.</summary>
    public IReadOnlyList<(Int3 cell, Block block)> Remaining => _remaining;

    /// <summary>Blocks placed, taken down, and block items used since the start (tests, stats).</summary>
    public int Placed { get; private set; }
    public int TakenDown { get; private set; }
    public readonly SortedDictionary<string, int> Spent = new(StringComparer.Ordinal);

    /// <summary>What the tool says: "No Steel block (large) left", "the Skiff is complete".</summary>
    public event Action<string, Color> Said;

    /// <summary>A structure came into being (a core placed), or was finished from its blueprint.</summary>
    public event Action<Vessel> Started, Finished;

    /// <summary>A block was placed (on a large or small grid), for experience.</summary>
    public event Action<Vessel, bool> BlockPlaced;

    /// <summary>A device was set down.</summary>
    public event Action<Device> DeviceSet;

    /// <summary>Things that had nowhere to go, set down at a place.</summary>
    public event Action<Bill, Vector3> Spilled;

    // Aim, each tick.
    public bool HasTarget { get; private set; }
    public Vessel TargetVessel { get; private set; }
    public Int3 TargetCell { get; private set; }
    public Int3 PlaceCell { get; private set; }
    public string Verdict { get; private set; } = "";
    public Vector3 GroundPoint => _groundPoint;
    Vector3 _groundPoint;
    bool _onGround;

    readonly List<(Int3 cell, Block block)> _remaining = new();
    float _placeTimer;
    bool _planDirty = true;
    int _structures;

    public BuildTool(SurfaceZone zone, Survivor survivor, PlayerInventory pack, Progress progress)
    {
        _zone = zone;
        _survivor = survivor;
        _pack = pack;
        Progress = progress;
        _rules = zone.Data.Rules.Build;
        _catalogue = zone.Data.Catalogue;
        _placer = new BlockPlacer(zone.Data.Items, zone.WorldRules, new CrateYard(zone.Data.Items));
        _ghosts = new GhostPreview { Name = "BuildGhosts" };
        zone.AddChild(_ghosts);
        _planGhost = new MeshInstance3D
        {
            Name = "BlueprintGhost", MaterialOverride = BlockMaterials.Ghost(GhostPreview.Valid, 0.18f), Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off,
        };
        zone.AddChild(_planGhost);
        _deviceGhost = new MeshInstance3D
        {
            Name = "DeviceGhost", Mesh = new BoxMesh { Size = new Vector3(1.1f, 1.2f, 0.8f) }, Visible = false,
            MaterialOverride = BlockMaterials.Ghost(GhostPreview.Valid, 0.3f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        zone.AddChild(_deviceGhost);
    }

    bool Creative => _zone.WorldRules.Creative;
    ItemCatalogue Items => _zone.Data.Items;

    /// <summary>How high a new vessel's core is set up above the ground it is placed on, m.</summary>
    public const float BuildHeight = 2.75f;

    /// <summary>The armour shapes the wheel steps through.</summary>
    public IReadOnlyList<ShapeDef> Shapes => _catalogue.Shapes;
    public ShapeDef Shape => _catalogue.Shapes[Math.Clamp(ShapeIndex, 0, _catalogue.Shapes.Count - 1)];

    public void NextShape(int step) => ShapeIndex = (ShapeIndex + step + _catalogue.Shapes.Count) % _catalogue.Shapes.Count;
    public void Rotate(int axis) => Rot = Orientations.Rotate(Rot, axis, 1);

    public void Hide()
    {
        _ghosts.HideGhosts();
        _ghosts.HideOutline();
        _planGhost.Visible = false;
        _deviceGhost.Visible = false;
    }

    /// <summary>The ghost and the label for what is in hand, and acting on the buttons.</summary>
    public void Tick(float dt, HandKind hand, Slot slot, int slotIndex, bool held, bool pressed, bool removePressed)
    {
        if (Projecting) hand = HandKind.Blueprint;
        string item = slot.Block != null && slot.Block != PlayerInventory.BlueprintBlock ? slot.Block : slot.Item;
        switch (hand)
        {
            case HandKind.Block: TickBlock(item, slotIndex, pressed, removePressed); break;
            case HandKind.Core: TickNew(NewThingOf(item), item, slotIndex, pressed); break;
            case HandKind.Device: TickNew(NewThing.Device, item, slotIndex, pressed); break;
            case HandKind.Blueprint:
                if (Plan == null && Underway == null) Follow(_zone.Data.Blueprint(_rules.StarterBlueprint));
                TickBlueprint(dt, held, pressed, removePressed);
                break;
            default: Hide(); break;
        }
    }

    static NewThing NewThingOf(string coreItem) => StructureArea.ClassOfCore(coreItem) switch
    {
        StructureClass.Base => NewThing.Base,
        StructureClass.Capital => NewThing.Capital,
        StructureClass.Hover => NewThing.Hover,
        _ => NewThing.Vessel,
    };

    /// <summary>
    /// The survival tool's deconstruct mode: the crosshair on a block outlines it and says what
    /// comes back; a click takes it down and gives its item back (Empyrion's multi-tool pick-up).
    /// </summary>
    public void TickDeconstruct(bool pressed)
    {
        _planGhost.Visible = false;
        _deviceGhost.Visible = false;
        _ghosts.HideGhosts();
        Aim();
        if (!HasTarget || TargetVessel == null || !TargetVessel.Structure.TryGet(TargetCell, out Block b))
        {
            _ghosts.HideOutline();
            Verdict = "Deconstruct: aim at a block of a structure";
            return;
        }
        Structure s = TargetVessel.Structure;
        _ghosts.ShowOutline(TargetVessel.DrawTransform, s, TargetCell, new Color(1f, 0.55f, 0.2f, 0.9f));
        ItemDef back = s.TypeOf(b).IsCore && s.Count == 1 ? Items.Item(StructureArea.CoreItem(StructureArea.ClassOf(TargetVessel))) : BuildingBridge.ItemOf(Items, s, b);
        Verdict = Creative ? $"Deconstruct {s.TypeOf(b).Name}" : $"Deconstruct {s.TypeOf(b).Name}: +1 {back?.Name ?? "nothing"}";
        if (pressed) Remove(TargetVessel, TargetCell);
    }

    /// <summary>Starts the blueprint projector (from the CORE tab): the Skiff's ghost follows the crosshair until a click sets it down.</summary>
    public void Project(Structure blueprint = null)
    {
        Follow(blueprint ?? _zone.Data.Blueprint(_rules.StarterBlueprint));
        Projecting = true;
    }

    /// <summary>Puts the projector away.</summary>
    public void StopProjecting()
    {
        Projecting = false;
        Plan = null;
        Underway = null;
        _planGhost.Visible = false;
    }

    // ---- Blocks ----------------------------------------------------------------------------------

    void TickBlock(string itemId, int slotIndex, bool place, bool remove)
    {
        _planGhost.Visible = false;
        _deviceGhost.Visible = false;
        Aim();
        if (itemId == null || !Items.TryGet(itemId, out ItemDef item) || !item.IsBlock || !_catalogue.TryGetType(item.BlockId, out BlockType type))
        {
            Hide();
            Verdict = itemId != null && Items.TryGet(itemId, out ItemDef d) && d.Family == BlockFamily.Deco ? $"{d.Name}: deco pieces aren't placed by hand in the slice yet" : "";
            return;
        }
        if (!HasTarget || TargetVessel == null)
        {
            Hide();
            Verdict = HasTarget ? "Blocks go onto a structure: set a core down first" : "";
            if (remove && HasTarget) Said?.Invoke(Verdict, Tones.Caution);
            return;
        }
        Structure s = TargetVessel.Structure;
        Block b = Make(s, type, AutoOrient(type, Rot));
        var copies = Copies(s, PlaceCell, b);
        // The family in hand must go on this grid and class of structure (Creative too).
        string why = !item.PlacesOn(StructureArea.ClassOf(TargetVessel)) ? $"{item.Name}: {StructureClasses.Only(item.Places)}"
            : Problem(TargetVessel, PlaceCell, b) ?? SourceProblem(TargetVessel, item, slotIndex, copies.Count);
        var tinted = copies.Select(c => (c.cell, c.block, Problem(TargetVessel, c.cell, c.block) == null ? GhostPreview.Valid : GhostPreview.Invalid)).ToList();
        if (why != null) tinted = tinted.Select(t => (t.cell, t.block, GhostPreview.Invalid)).ToList();
        Verdict = why ?? $"{item.Name}{(type.IsArmor ? $", {Shape.Name}" : "")} · {Available(TargetVessel, item, slotIndex)}";
        _ghosts.ShowGhosts(TargetVessel.DrawTransform, s, tinted);
        _ghosts.ShowOutline(TargetVessel.DrawTransform, s, TargetCell, new Color(1, 1, 1, 0.8f));
        if (place)
        {
            if (why != null) Said?.Invoke(why, Tones.Caution);
            else foreach (var (cell, block) in copies) TryPlace(TargetVessel, cell, block, quiet: cell != PlaceCell, slotIndex);
        }
        if (remove) Remove(TargetVessel, TargetCell);
    }

    /// <summary>The structure toolbar is in use: blocks come from the linked structure's storage, never your bag.</summary>
    bool FromStructure => _pack.OnStructureBar;

    /// <summary>Can a structure's storage be drawn on from a point: is it a linkable structure, and the point in its area?</summary>
    public bool CanDrawFrom(Vessel v, Vector3 at) => StructureArea.Linkable(v) && StructureArea.Contains(v, at, _rules.Area);

    /// <summary>How many of the block item the chosen source holds: "12 in your stack", "340 in Base 1".</summary>
    public string Available(Vessel v, ItemDef item, int slotIndex)
    {
        if (Creative) return "free";
        if (FromStructure) return $"{_pack.Linked.Count(item.Id)} in {_pack.LinkedName}";
        Slot slot = _pack.Grid[slotIndex];
        return $"{(slot.Item == item.Id ? slot.Count : 0)} in your stack";
    }

    /// <summary>Why the chosen source can't pay for <paramref name="n"/> placements on this structure, or null.</summary>
    string SourceProblem(Vessel v, ItemDef item, int slotIndex, int n = 1)
    {
        if (Creative) return null;
        if (FromStructure)
        {
            if (_pack.Linked != v.Storage) return $"The structure toolbar draws on {_pack.LinkedName}: build on it, or use your own toolbar (Y)";
            if (!StructureArea.Contains(v, _survivor.WorldFeet, _rules.Area)) return $"Out of {v.Name}'s area: its storage is out of reach";
            return _pack.Linked.Count(item.Id) > 0 ? null : $"No {item.Name} in {v.Name}'s storage: craft some there";
        }
        Slot slot = _pack.Grid[slotIndex];
        if (slot.Block != null) return $"{item.Name}: a Creative shortcut, not a stack";
        return slot.Item == item.Id && slot.Count > 0 ? null : $"No {item.Name} left in your stack";
    }

    /// <summary>Uses one block item from the chosen source (nothing in Creative). False if it has none.</summary>
    bool TakeOne(Vessel v, ItemDef item, int slotIndex)
    {
        if (Creative) return true;
        if (FromStructure) return _pack.Linked == v.Storage && _pack.Linked.Remove(item.Id, 1) == 1;
        Slot slot = _pack.Grid[slotIndex];
        return slot.Item == item.Id && _pack.Grid.TakeOne(slotIndex);
    }

    /// <summary>What the crosshair is on, in the world you are in: a structure's block face, or open ground.</summary>
    void Aim()
    {
        HasTarget = false;
        TargetVessel = null;
        _onGround = false;
        if (!WorldRay.Cast(_zone, _survivor, _rules.Reach, Layers.World | Layers.Hull, out LookHit hit)) return;
        if (hit.Vessel is Vessel v && !v.IsWreck)
        {
            if (!v.Structure.Has(hit.Cell)) return;
            TargetCell = hit.Cell;
            N.Vector3 n = MathX.Unrotate(v.Body.Rotation, hit.Normal.ToNumerics());
            PlaceCell = TargetCell + Dirs.Closest(n).Vec();
            TargetVessel = v;
            HasTarget = true;
            return;
        }
        if (hit.Vessel == null && _zone.Host.BodyOf(hit.Collider) == null)
        {
            _groundPoint = hit.Point;
            _onGround = true;
            HasTarget = true;
        }
    }

    // ---- New structures and devices -------------------------------------------------------------

    void TickNew(NewThing what, string itemId, int slotIndex, bool place)
    {
        _planGhost.Visible = false;
        _ghosts.HideOutline();
        Aim();
        if (!_onGround)
        {
            Hide();
            Verdict = "Look at open ground to set it down";
            return;
        }
        // A core or a device founds something new off any structure: only your own stack counts.
        string from = FromStructure ? $"Set it down from your own toolbar (Y): {_pack.LinkedName}'s storage only builds onto {_pack.LinkedName}" : null;
        if (what == NewThing.Device)
        {
            _ghosts.HideGhosts();
            Transform3D at = GroundFrame(_groundPoint, 0f);
            _deviceGhost.GlobalTransform = at.TranslatedLocal(Vector3.Up * 0.6f);
            _deviceGhost.Visible = true;
            Verdict = from ?? $"{Items.Item(itemId).Name}: click to set it down";
            if (place && from == null && _pack.Grid.TakeOne(slotIndex))
            {
                MachineDef machine = Items.Machines.All.First(m => m.ItemId == itemId);
                Device d = Device.Place(_zone, machine, machine.ItemId, at.Origin, _survivor.Yaw + YawOfFrame());
                _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Place, at.Origin, 0.8f, -2f);
                Said?.Invoke($"{machine.Name} set down: its panel is in the Tab menu (Core) when you stand by it", Tones.Good);
                DeviceSet?.Invoke(d);
            }
            else if (place && from != null) Said?.Invoke(from, Tones.Caution);
            return;
        }
        _deviceGhost.Visible = false;
        bool large = what is NewThing.Base or NewThing.Capital;
        var scratch = new Structure(_catalogue, _catalogue.Grid(large ? GridSize.Large : GridSize.Small));
        Block core = Building.Make(scratch, _catalogue.CoreType);
        // A base sits on the ground; a vessel's core is set up on a build stand, 2 m up, so you can
        // build under it as well as round it (Empyrion's way: a vessel holds where it is built
        // until it is first flown, then lands on its own legs).
        Transform3D frame = GroundFrame(_groundPoint, scratch.Grid.CellSize * 0.5f + (what == NewThing.Base ? 0f : BuildHeight));
        _ghosts.ShowGhosts(frame, scratch, new List<(Int3, Block, Color)> { (Int3.Zero, core, from == null ? GhostPreview.Valid : GhostPreview.Invalid) });
        Verdict = from ?? what switch
        {
            NewThing.Base => "Base core: click to found a base here",
            NewThing.Capital => "Capital core: click to start a capital vessel here",
            NewThing.Hover => "Hover core: click to start a hover vessel here",
            _ => "Small-vessel core: click to start a small vessel here",
        };
        if (!place) return;
        if (from != null)
        {
            Said?.Invoke(from, Tones.Caution);
            return;
        }
        if (!Creative && !_pack.Grid.TakeOne(slotIndex)) return;
        Vessel v = StartStructure(frame, what, itemId);
        if (v == null && !Creative) _pack.Add(itemId, 1);
    }

    /// <summary>A new structure of the core's class where a core item was set down. The item is its price.</summary>
    Vessel StartStructure(Transform3D frame, NewThing what, string coreItem)
    {
        _structures++;
        StructureClass cls = StructureArea.ClassOfCore(coreItem);
        bool large = cls is StructureClass.Base or StructureClass.Capital;
        var s = new Structure(_catalogue, _catalogue.Grid(large ? GridSize.Large : GridSize.Small))
        {
            Name = cls switch
            {
                StructureClass.Base => $"Base {_structures}",
                StructureClass.Capital => $"Capital {_structures}",
                StructureClass.Hover => $"Hover {_structures}",
                _ => $"Vessel {_structures}",
            },
            Mode = cls == StructureClass.Base ? CoreMode.Base : CoreMode.Capital,
            GravityOn = true,
        };
        Building.Place(s, Int3.Zero, Building.Make(s, _catalogue.CoreType));
        s.DirtyChunks.Clear();
        N.Vector3 origin = frame.Origin.ToNumerics();
        float heading = MathX.Heading(frame.Basis.GetRotationQuaternion().ToNumerics());
        Vessel v = cls == StructureClass.Base ? Vessel.SpawnBase(_zone, s, origin, heading) : Vessel.Spawn(_zone, s, origin, heading, landed: true);
        StructureArea.SetClass(v, cls);
        if (cls == StructureClass.Base) ClearGround(v, Int3.Zero);
        else v.Stand = v.Body.Position;
        Placed++;
        _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Place, frame.Origin, 0.7f, -2f);
        Said?.Invoke(cls == StructureClass.Base ? $"{s.Name} founded: build onto its core with blocks from your toolbar" : $"{s.Name} begun: build it up from its core", Tones.Good);
        Started?.Invoke(v);
        BlockPlaced?.Invoke(v, !large);
        return v;
    }

    /// <summary>The frame of a new structure standing on the ground at a point, facing away from the survivor.</summary>
    Transform3D GroundFrame(Vector3 ground, float lift) =>
        new(new Basis(Vector3.Up, _survivor.Yaw + YawOfFrame()), ground + Vector3.Up * lift);

    float YawOfFrame() => _survivor.Body.Frame == null ? 0f : MathX.Heading(_survivor.Body.Frame.Rotation);

    // ---- Blueprints (the projector) ---------------------------------------------------------------

    /// <summary>The blueprint's ghost is standing where you look: a click sets it down there.</summary>
    public bool PlanShown => Plan != null && Underway == null && _planGhost.Visible;

    /// <summary>Starts following a blueprint: its ghost goes down where you look; the first click sets it there.</summary>
    public void Follow(Structure blueprint)
    {
        Plan = blueprint;
        Underway = null;
        _remaining.Clear();
        _planDirty = true;
    }

    void TickBlueprint(float dt, bool held, bool pressed, bool cancel)
    {
        _ghosts.HideGhosts();
        _ghosts.HideOutline();
        _deviceGhost.Visible = false;
        if (Plan == null)
        {
            _planGhost.Visible = false;
            Verdict = "No blueprint chosen";
            return;
        }
        if (cancel)
        {
            Said?.Invoke(Underway == null ? "Blueprint put away" : $"Stopped following the blueprint: the {Underway.Name} stays as it is", Tones.Text);
            StopProjecting();
            return;
        }
        if (Underway == null)
        {
            Aim();
            if (!_onGround)
            {
                _planGhost.Visible = false;
                Verdict = "Look at open ground to set the blueprint down";
                if (pressed) Said?.Invoke(Verdict, Tones.Caution); // a click that can't place never goes unanswered
                return;
            }
            Transform3D frame = AnchorFrame(_groundPoint);
            ShowPlan(frame, all: true);
            Verdict = $"{Plan.Name}: {Plan.Count} blocks, {Items.Describe(TotalCost(Plan), 4)} from your bag. Click to set it down";
            if (pressed) StartFromPlan(frame);
            return;
        }
        if (Underway.IsWreck || Underway.Structure.Core == null)
        {
            Said?.Invoke("The blueprint's vessel has no core any more", Tones.Warning);
            StopProjecting();
            return;
        }
        if (_planDirty) RefreshRemaining();
        ShowPlan(Underway.DrawTransform, all: false);
        Verdict = $"{Plan.Name}: {Plan.Count - _remaining.Count}/{Plan.Count} blocks. Hold to build";
        if (!held)
        {
            _placeTimer = 0f;
            return;
        }
        _placeTimer += dt;
        float interval = 1f / Math.Max(1f, _rules.BlueprintPlacesPerSecond);
        while (_placeTimer >= 0f)
        {
            _placeTimer -= interval;
            if (!PlaceNext()) { _placeTimer = 0f; break; }
        }
    }

    /// <summary>
    /// Places the next block of the plan that can go on now: attached to what is built, clear of
    /// the survivor, and paid for. False if none can (and says why).
    /// </summary>
    public bool PlaceNext()
    {
        if (Underway == null || Plan == null) return false;
        if (_planDirty) RefreshRemaining();
        Structure s = Underway.Structure;
        string blocked = null;
        for (int i = 0; i < _remaining.Count; i++)
        {
            var (cell, block) = _remaining[i];
            if (Building.Check(s, cell, block) != PlaceError.None) continue;
            string why = Problem(Underway, cell, block);
            if (why != null)
            {
                blocked ??= why;
                continue;
            }
            if (!TryPlace(Underway, cell, block, quiet: true, -1)) continue;
            _remaining.RemoveAt(i);
            ShowPlanMesh();
            if (_remaining.Count == 0)
            {
                Said?.Invoke($"The {Plan.Name} is complete: take the seat (F) to fly", Tones.Good);
                Vessel done = Underway;
                StopProjecting();
                Finished?.Invoke(done);
            }
            return true;
        }
        if (blocked != null) Said?.Invoke(blocked, Tones.Caution);
        return false;
    }

    /// <summary>Where a blueprint stands on the ground: its lowest point (the gear's feet) on the highest ground under it.</summary>
    Transform3D AnchorFrame(Vector3 ground)
    {
        var (min, max) = Plan.Bounds();
        float cell = Plan.Grid.CellSize;
        float feet = StructureBridge.ShapeOf(Plan).FeetBelowOrigin;
        var spec = new StructureVessel(Plan, _zone.Data.Handling);
        foreach (ShipHandling.Handling.GearSpec g in spec.LandingGear) feet = MathF.Max(feet, -g.Position.Y + g.Length);
        var basis = new Basis(Vector3.Up, _survivor.Yaw + YawOfFrame());
        float top = float.MinValue;
        for (int i = 0; i < 9; i++)
        {
            var corner = new Vector3(i % 3 == 0 ? min.X : i % 3 == 1 ? 0 : max.X, 0, i / 3 == 0 ? min.Z : i / 3 == 1 ? 0 : max.Z) * cell;
            Vector3 p = ground + basis * corner;
            top = MathF.Max(top, _zone.GroundAt(p.X, p.Z).Y);
        }
        return new Transform3D(basis, new Vector3(ground.X, MathF.Max(top, ground.Y) + feet + 0.02f, ground.Z));
    }

    void ShowPlan(Transform3D frame, bool all)
    {
        if (all && _planDirty)
        {
            _remaining.Clear();
            foreach (var (p, b) in Plan.Blocks()) _remaining.Add((p, b));
            ShowPlanMesh();
            _planDirty = false;
        }
        _planGhost.GlobalTransform = frame;
        _planGhost.Visible = true;
    }

    /// <summary>The remaining blocks as one translucent mesh (building's mesher on a scratch structure).</summary>
    void ShowPlanMesh()
    {
        Structure ghost = Like(Plan);
        foreach (var (cell, block) in _remaining) ghost.Set(cell, block);
        var data = new ChunkMeshData();
        var meshes = new ArrayMesh();
        foreach (Int3 chunk in ghost.Chunks.Keys)
        {
            ChunkMesher.Build(ghost, chunk, data);
            if (data.Opaque.VertexCount > 0) meshes.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, data.Opaque.ToArrays());
            if (data.Glass.VertexCount > 0) meshes.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, data.Glass.ToArrays());
        }
        _planGhost.Mesh = meshes.GetSurfaceCount() > 0 ? meshes : null;
    }

    /// <summary>The plan's blocks not yet built, ordered outward from the core through shared faces.</summary>
    void RefreshRemaining()
    {
        _planDirty = false;
        Structure s = Underway.Structure;
        var order = new List<(Int3, Block)>();
        var seen = new HashSet<Int3> { Plan.Core.Value };
        var queue = new Queue<Int3>();
        queue.Enqueue(Plan.Core.Value);
        while (queue.Count > 0)
        {
            Int3 p = queue.Dequeue();
            Block pb = Plan.Get(p);
            if (!s.Has(p)) order.Add((p, pb));
            foreach (Dir d in Dirs.All)
            {
                Int3 q = p + d.Vec();
                if (seen.Contains(q) || !Plan.TryGet(q, out _)) continue;
                seen.Add(q);
                queue.Enqueue(q);
            }
        }
        foreach (var (p, b) in Plan.Blocks())
            if (!seen.Contains(p) && !s.Has(p)) order.Add((p, b));
        _remaining.Clear();
        _remaining.AddRange(order);
        ShowPlanMesh();
    }

    /// <summary>The blueprint's core goes down where its ghost stands, paid for by its bill; the rest is built from there.</summary>
    void StartFromPlan(Transform3D frame)
    {
        Structure s = Like(Plan);
        Int3 coreCell = Plan.Core.Value;
        Block core = Plan.Get(coreCell);
        Bill bill = _placer.CostOf(s, core);
        if (!_pack.Storage.TryTake(bill))
        {
            Said?.Invoke($"The blueprint starts from its core: {Items.Describe(bill)} in your bag", Tones.Caution);
            return;
        }
        _pack.Grid.Reconcile();
        Count(bill);
        Building.Place(s, coreCell, core);
        s.DirtyChunks.Clear();
        Vessel v = Vessel.Spawn(_zone, s, frame.Origin.ToNumerics(), MathX.Heading(frame.Basis.GetRotationQuaternion().ToNumerics()), landed: true);
        StructureArea.SetClass(v, BuildingBridge.IsSmall(s) ? StructureClass.Small : StructureClass.Capital);
        Placed++;
        Said?.Invoke($"{Plan.Name} begun: hold the button to build it", Tones.Good);
        Started?.Invoke(v);
        Underway = v;
        _planDirty = true;
    }

    /// <summary>An empty structure with a plan's grid, mode, gravity and paints (so its blocks' paint slots mean the same).</summary>
    Structure Like(Structure plan)
    {
        var s = new Structure(_catalogue, plan.Grid) { Name = plan.Name, Mode = plan.Mode, GravityOn = plan.GravityOn };
        for (int i = 1; i < plan.Palette.Count; i++) s.Palette.Intern(plan.Palette[(ushort)i], plan.Palette.NameOf((ushort)i));
        return s;
    }

    // ---- Placing and removing ------------------------------------------------------------------

    /// <summary>Why a block can't go here, or null: the rules, the grid and class, the survivor in the way, the ground.</summary>
    string Problem(Vessel v, Int3 cell, in Block block)
    {
        Structure s = v.Structure;
        PlaceError e = Building.Check(s, cell, block);
        if (e != PlaceError.None) return e switch
        {
            PlaceError.Occupied => "Something is already there",
            PlaceError.NotAttached or PlaceError.NoSharedFace => "Blocks attach through a shared face",
            PlaceError.OneCoreOnly => "A structure has one core",
            _ => e.ToString(),
        };
        BlockType type = s.TypeOf(block);
        bool small = BuildingBridge.IsSmall(s);
        ItemDef item = Items.BlockItem(type.Id, small);
        if (item == null)
        {
            ItemDef other = Items.BlockItem(type.Id, !small);
            if (other != null) return $"{other.Name} is a {(small ? "large" : "small")}-grid block: this is a {(small ? "small" : "large")} grid";
            if (!Creative) return $"{type.Name} has no item: it can't be built in Survival";
        }
        if (item != null && !item.PlacesOn(StructureArea.ClassOf(v))) return $"{item.Name}: {StructureClasses.Only(item.Places)}";
        if (InSurvivor(v, cell)) return "Step out of the way to build there";
        if (!v.IsBase && InGround(v, cell)) return "Ground in the way: only a base reaches into the ground";
        return null;
    }

    /// <summary>Places one block, using one of its item from the chosen source (<paramref name="slotIndex"/> -1: the projector, from your bag).</summary>
    bool TryPlace(Vessel v, Int3 cell, Block block, bool quiet, int slotIndex)
    {
        Structure s = v.Structure;
        if (Problem(v, cell, block) is string why)
        {
            if (!quiet) Said?.Invoke(why, Tones.Caution);
            return false;
        }
        ItemDef item = BuildingBridge.ItemOf(Items, s, block);
        if (!Creative && item != null)
        {
            bool took = slotIndex < 0 ? _pack.Storage.Remove(item.Id, 1) == 1 : TakeOne(v, item, slotIndex);
            if (!took)
            {
                if (!quiet) Said?.Invoke(slotIndex < 0 ? $"No {item.Name} in your bag" : SourceProblem(v, item, slotIndex) ?? $"No {item.Name} left", Tones.Caution);
                return false;
            }
            if (slotIndex < 0) _pack.Grid.Reconcile();
            Count(Bill.Of((item.Id, 1)));
        }
        Building.Place(s, cell, block);
        _placer.Sync(s, v.Storage, 0, 0, 0, spill: false); // placing only ever adds room
        if (v.IsBase) ClearGround(v, cell);
        v.ApplyEdits();
        if (v == Underway) v.Settle(); // a blueprint's vessel stands on the ground as it grows
        Placed++;
        _planDirty |= Underway == v;
        _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Place, v.ToWorld(StructureBridge.CellCenter(s, cell)), 1.3f, -6f);
        BlockPlaced?.Invoke(v, BuildingBridge.IsSmall(s));
        return true;
    }

    /// <summary>
    /// A base's block reaching into the ground: the cells whose centres lie inside it are cleared
    /// and its volume registered, so the ground meets its faces flush (terrain-digging's rule 4).
    /// </summary>
    void ClearGround(Vessel v, Int3 cell)
    {
        var volume = new TerrainDigging.Terrain.StructureVolume(
            v.Body.ToParent(StructureBridge.CellCenter(v.Structure, cell)), v.Body.Rotation, new N.Vector3(v.Structure.Grid.CellSize));
        _zone.Edits.ClearForStructure(BlockKey(v, cell), volume);
    }

    static TerrainDigging.Terrain.VolumeKey BlockKey(Vessel v, Int3 cell) => new(v.Body.Id, cell.X, cell.Y, cell.Z);

    /// <summary>
    /// Takes a block down: Survival gives its item back, whole, and sets down whatever has nowhere
    /// to go; sections it cuts loose become wrecks. The last block, the core, comes back as the
    /// core item of the structure's class, and the structure's storage comes with it.
    /// </summary>
    public void Remove(Vessel v, Int3 cell)
    {
        Structure s = v.Structure;
        if (!s.TryGet(cell, out Block b)) return;
        // A base's blocks hold the ground off their faces: the ones that leave it let go.
        var before = v.IsBase ? s.Blocks().Select(x => x.pos).ToList() : null;
        ItemDef item = BuildingBridge.ItemOf(Items, s, b);
        Bill back = Creative || item == null ? new Bill() : Bill.Of((item.Id, 1));
        StructureClass cls = StructureArea.ClassOf(v);
        RemoveResult r = Building.Remove(s, cell, null);
        if (r.Status == RemoveStatus.CoreHoldsStructure)
        {
            Said?.Invoke("The core holds the structure together: take the rest down first", Tones.Caution);
            return;
        }
        if (r.Status != RemoveStatus.Removed) return;
        if (before != null)
            foreach (Int3 p in before)
                if (!s.Has(p)) _zone.Edits.ReleaseStructure(BlockKey(v, p));
        TakenDown++;
        Vector3 at = v.ToWorld(StructureBridge.CellCenter(s, cell));
        if (s.TypeOf(b).IsCore && s.Count == 0)
        {
            // The last block, its core: it goes back as the core item it was founded with.
            back = Creative ? new Bill() : Bill.Of((StructureArea.CoreItem(cls), 1));
            GiveBack(back, null, at);
            Bill held = v.Storage.Contents;
            v.Storage.TryTake(held);
            if (!held.IsEmpty) Spilled?.Invoke(held, at);
            if (_pack.Linked == v.Storage) _pack.Link(null, null);
            _zone.Physics.Unregister(v.Body);
            _zone.Physics.StuckFields.Remove(v.Field);
            _zone.Untrack(v);
            v.Node.QueueFree();
            Said?.Invoke($"{v.Name} taken down", Tones.Text);
            return;
        }
        GiveBack(back, v, at);
        foreach (Structure piece in r.Wrecks) v.Sever(piece);
        // A container gone shrinks storage: what no longer fits is set down, never lost.
        Bill overflow = _placer.Sync(s, v.Storage, 0, 0, 0, spill: false);
        if (!overflow.IsEmpty) Spilled?.Invoke(overflow, at);
        v.ApplyEdits();
        if (v == Underway) v.Settle();
        _planDirty |= Underway == v;
        _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Clank, at, 0.9f, -6f);
        if (!back.IsEmpty) Said?.Invoke($"Taken down: +{Items.Describe(back, 3)}", Tones.Text);
    }

    /// <summary>
    /// Where a block taken down goes: building from the structure toolbar, back into that
    /// structure's storage; otherwise into your bag, then (in its area) the structure's storage;
    /// then a container on the ground. Nothing is ever lost.
    /// </summary>
    void GiveBack(Bill bill, Vessel v, Vector3 at)
    {
        if (bill.IsEmpty) return;
        bool inArea = v != null && StructureArea.Contains(v, _survivor.WorldFeet, _rules.Area);
        Bill left;
        if (v != null && FromStructure && _pack.Linked == v.Storage && inArea) left = v.Storage.AddAll(bill);
        else
        {
            left = _pack.Storage.AddAll(bill);
            _pack.Grid.Reconcile();
            if (inArea) left = v.Storage.AddAll(left);
        }
        if (!left.IsEmpty) Spilled?.Invoke(left, at);
    }

    void Count(Bill bill)
    {
        foreach (var (item, n) in bill) Spent[item] = Spent.GetValueOrDefault(item) + n;
    }

    /// <summary>Would a block at this cell overlap the survivor's body?</summary>
    bool InSurvivor(Vessel v, Int3 cell)
    {
        N.Vector3 feet = _survivor.Body.WorldPosition;
        N.Vector3 up = _survivor.Body.Frame == null ? _survivor.Body.Up : MathX.Rotate(_survivor.Body.Frame.Rotation, _survivor.Body.Up);
        float cellSize = v.Structure.Grid.CellSize, half = cellSize * 0.5f;
        N.Vector3 c = StructureBridge.CellCenter(v.Structure, cell);
        for (float y = 0.2f; y <= Survivor.Height - 0.1f; y += 0.4f)
        {
            N.Vector3 local = v.Body.ToLocal(feet + up * y);
            N.Vector3 d = N.Vector3.Abs(local - c);
            if (d.X < half + Survivor.Radius && d.Z < half + Survivor.Radius && d.Y < half + 0.25f) return true;
        }
        return false;
    }

    /// <summary>Would a block at this cell sit mostly in the ground (natural or not)?</summary>
    bool InGround(Vessel v, Int3 cell)
    {
        N.Vector3 c = v.Body.ToParent(StructureBridge.CellCenter(v.Structure, cell));
        return _zone.Planet.Terrain.Store.SampleDensity(c) > 0.25f;
    }

    // ---- Helpers --------------------------------------------------------------------------------

    Block Make(Structure s, BlockType type, int rot)
    {
        ShapeDef shape = type.IsArmor ? Shape : null;
        return Building.Make(s, type, shape, rot);
    }

    List<(Int3 cell, Block block)> Copies(Structure s, Int3 cell, Block block)
    {
        var sym = Symmetry.Through(s.Core ?? Int3.Zero);
        sym.Set(0, MirrorX, (s.Core ?? Int3.Zero).X * 2);
        return Building.MirrorCopies(s, cell, block, sym).Select(c => (c.Pos, c.Block)).ToList();
    }

    /// <summary>Components that mount by one face turn it toward the face clicked (as building's tool does).</summary>
    int AutoOrient(BlockType type, int preferred)
    {
        if (type.IsArmor) return preferred;
        int mask = type.AttachFaces;
        if (mask == 0 || (mask & (mask - 1)) != 0) return preferred;
        var mount = (Dir)System.Numerics.BitOperations.TrailingZeroCount(mask);
        Dir wanted = Dirs.Closest((TargetCell - PlaceCell).ToVector3());
        if (Orientations.ToWorld(preferred, mount) == wanted) return preferred;
        Mat3 pref = Orientations.Matrix(preferred);
        int best = preferred, bestScore = int.MinValue;
        for (int o = 0; o < Orientations.Count; o++)
        {
            if (Orientations.ToWorld(o, mount) != wanted) continue;
            Mat3 m = Orientations.Matrix(o);
            int score = Int3.Dot(m.X, pref.X) + Int3.Dot(m.Y, pref.Y) + Int3.Dot(m.Z, pref.Z);
            if (score > bestScore) { bestScore = score; best = o; }
        }
        return best;
    }

    /// <summary>What placing a block uses on a grid: one of its item (nothing in Creative).</summary>
    public Bill CostOf(string blockId, bool small) =>
        Creative || Items.BlockItem(blockId, small) is not ItemDef item ? new Bill() : Bill.Of((item.Id, 1));

    /// <summary>A whole blueprint's block items, one per block (what the projector uses from your bag).</summary>
    public Bill TotalCost(Structure plan)
    {
        var total = new Bill();
        foreach (var (_, b) in plan.Blocks()) total.Add(_placer.CostOf(plan, b));
        return total;
    }
}
