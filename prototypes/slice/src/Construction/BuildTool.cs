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

namespace Slice.Construction;

/// <summary>What placing something new starts.</summary>
public enum NewThing { None, Base, Device }

/// <summary>
/// Building with what is on the toolbar, on building's structure model and rules (attach through
/// a shared face, one core, nothing lost when something is cut loose), its ghosts and mesher,
/// and shared/Items' rule that blocks are craftable items (Empyrion's way).
///
/// A block item in hand places that block, in any of its shapes or sizes (wheel: shape or door
/// size, R / T rotate, X mirror), on the face under the crosshair, and only on a structure: every
/// structure starts with its core. Any block goes on any structure. In Survival each placement uses one item from the
/// source you chose: your own toolbar's stack, or, on the structure toolbar, the shared storage of
/// the structure you are building on while you are in its area. An empty stack can't place.
/// Taking a block down (the survival tool's deconstruct mode, or right click with a block in
/// hand) gives its item back, whole: to your bag (the structure's storage when you build from the
/// structure toolbar), then the structure's storage, then a container on the ground. Creative is
/// free and unlimited, with every block available, and follows the same core-first rule.
///
/// A core item in hand founds a new structure on open ground, a base fixed where it stands, and
/// the CORE tab sets its mode. A device
/// item (the survival constructor) is set down as itself. The blueprint projector is not
/// on the toolbar: the BLUEPRINTS tab starts it (<see cref="Project"/>), and it builds a blueprint block by
/// block while the button is held, each block one item from your bag.
/// </summary>
public sealed partial class BuildTool
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

    /// <summary>The blueprint projector is running (started from the BLUEPRINTS tab, not the toolbar).</summary>
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

    /// <summary>A wreck was claimed with a core: its name (news, for the notices).</summary>
    public event Action<string> Claimed;

    /// <summary>A structure came into being (a core placed), or was finished from its blueprint.</summary>
    public event Action<Vessel> Started, Finished;

    /// <summary>A block was placed, and the experience it is worth.</summary>
    public event Action<Vessel, int> BlockPlaced;

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

    /// <summary>The armour shapes the wheel steps through.</summary>
    public IReadOnlyList<ShapeDef> Shapes => _catalogue.Shapes;
    public ShapeDef Shape => _catalogue.Shapes[Math.Clamp(ShapeIndex, 0, _catalogue.Shapes.Count - 1)];

    public void NextShape(int step) => ShapeIndex = (ShapeIndex + step + _catalogue.Shapes.Count) % _catalogue.Shapes.Count;

    /// <summary>The size of a variant family the wheel has picked (a door's); any other type is itself.</summary>
    BlockType Variant(BlockType t)
    {
        IReadOnlyList<BlockType> all = _catalogue.VariantsOf(t);
        return all[ShapeIndex % all.Count];
    }
    public void Rotate(int axis) => Rot = Orientations.Rotate(Rot, axis, 1);

    public void Hide()
    {
        if (_decoGhost != null) _decoGhost.Visible = false;
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
            case HandKind.Core: TickNew(NewThing.Base, item, slotIndex, pressed); break;
            case HandKind.Device: TickNew(NewThing.Device, item, slotIndex, pressed); break;
            case HandKind.Blueprint:
                if (Plan == null && Underway == null) Follow(_zone.Data.Blueprint(_rules.StarterBlueprint));
                TickBlueprint(dt, held, pressed, removePressed);
                break;
            default: Hide(); break;
        }
    }

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
        if (DecoToDeconstruct(out string decoVerdict))
        {
            _ghosts.HideOutline();
            Verdict = decoVerdict;
            if (pressed) RemoveDecoNear(TargetVessel);
            return;
        }
        if (!HasTarget || TargetVessel == null || !TargetVessel.Structure.TryGet(TargetCell, out Block b))
        {
            _ghosts.HideOutline();
            Verdict = "DECONSTRUCT · NO BLOCK";
            return;
        }
        Structure s = TargetVessel.Structure;
        _ghosts.ShowOutline(TargetVessel.DrawTransform, s, TargetCell, new Color(1f, 0.55f, 0.2f, 0.9f));
        ItemDef back = BuildingBridge.ItemOf(Items, s, b);
        if (s.TypeOf(b).IsCore) Verdict = CoreStays;
        else Verdict = Creative ? $"Deconstruct {s.TypeOf(b).Name}" : $"Deconstruct {s.TypeOf(b).Name} · +{s.TypeOf(b).Units} {back?.Name ?? "nothing"}";
        if (pressed) Remove(TargetVessel, TargetCell);
    }

    /// <summary>Starts the blueprint projector (from the BLUEPRINTS tab): the blueprint's ghost (the Skiff's if none is given) follows the crosshair until a click sets it down.</summary>
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
        if (itemId != null && Items.TryGet(itemId, out ItemDef deco) && deco.IsUniversalDeco)
        {
            TickDeco(deco, slotIndex, place, remove);
            return;
        }
        if (_decoGhost != null) _decoGhost.Visible = false;
        _planGhost.Visible = false;
        _deviceGhost.Visible = false;
        Aim();
        if (itemId == null || !Items.TryGet(itemId, out ItemDef item) || !item.IsBlock || !_catalogue.TryGetType(item.BlockId, out BlockType type))
        {
            Hide();
            Verdict = itemId != null && Items.TryGet(itemId, out ItemDef d) && d.Family == BlockFamily.Deco ? $"{d.Name} · NOT PLACEABLE" : "";
            return;
        }
        type = Variant(type);
        if (!HasTarget || TargetVessel == null)
        {
            Hide();
            Verdict = HasTarget ? "NO STRUCTURE · CORE FIRST" : "";
            if (remove && HasTarget) Said?.Invoke(Verdict, Tones.Caution);
            return;
        }
        Structure s = TargetVessel.Structure;
        Block b = Make(s, type, AutoOrient(type, Rot));
        // A block spanning several cells grows out from the face it's built on; its anchor may sit away from the aimed cell.
        Int3 anchor = Footprint.AnchorFor(type, b.Rot, PlaceCell, Dirs.Closest((PlaceCell - TargetCell).ToVector3()));
        var copies = Copies(s, anchor, b);
        string why = Problem(TargetVessel, anchor, b) ?? SourceProblem(TargetVessel, item, slotIndex, copies.Count * type.Units);
        var tinted = copies.Select(c => (c.cell, c.block, Problem(TargetVessel, c.cell, c.block) == null ? GhostPreview.Valid : GhostPreview.Invalid)).ToList();
        if (why != null) tinted = tinted.Select(t => (t.cell, t.block, GhostPreview.Invalid)).ToList();
        Verdict = why ?? $"{item.Name}{(type.IsArmor ? $", {Shape.Name}" : type.Group != null ? $", {Footprint.Label(type, _catalogue.Grid.CellSize)}" : "")} · {Available(TargetVessel, item, slotIndex)}";
        _ghosts.ShowGhosts(TargetVessel.DrawTransform, s, tinted);
        if (type.Multi) _ghosts.ShowBlocked(TargetVessel.DrawTransform, s, copies.SelectMany(c => Building.Blocked(s, c.cell, c.block)).ToList());
        _ghosts.ShowOutline(TargetVessel.DrawTransform, s, TargetCell, new Color(1, 1, 1, 0.8f));
        if (place)
        {
            if (why != null) Said?.Invoke(why, Tones.Caution);
            else foreach (var (cell, block) in copies) TryPlace(TargetVessel, cell, block, quiet: cell != anchor, slotIndex);
        }
        if (remove) Remove(TargetVessel, TargetCell);
    }

    /// <summary>The remote toolbar is in use: blocks come from the origin core's storage, never your bag.</summary>
    bool FromStructure => _pack.OnRemoteBar;

    /// <summary>Can a structure's storage be drawn on from a point: is it a linkable structure, and the point in its area?</summary>
    public bool CanDrawFrom(Vessel v, Vector3 at) => StructureArea.Linkable(v) && StructureArea.Contains(v, at, _rules.Area);

    /// <summary>How many of the block item the chosen source holds: "12 in your stack", "340 in Base 1".</summary>
    public string Available(Vessel v, ItemDef item, int slotIndex)
    {
        if (Creative) return "free";
        if (FromStructure) return $"{_pack.HandSource.Count(item.Id)} in {_pack.HandRemote?.Origin}";
        Slot slot = _pack.Grid[slotIndex];
        return $"{(slot.Item == item.Id ? slot.Count : 0)} in your stack";
    }

    /// <summary>Why the chosen source can't pay for <paramref name="n"/> placements on this structure, or null.</summary>
    string SourceProblem(Vessel v, ItemDef item, int slotIndex, int n = 1)
    {
        if (Creative) return null;
        if (FromStructure)
        {
            RemoteSlot slotRef = _pack.HandRemote;
            if (slotRef == null || slotRef.ItemId != item.Id) return $"REMOTE SLOT · NO {item.Name}";
            if (!_pack.HandUsable) return $"{slotRef.Origin.ToUpperInvariant()} STORAGE · OUT OF RANGE";
            return slotRef.Count >= n ? null : $"{slotRef.Origin.ToUpperInvariant()} STORAGE · {slotRef.Count} {item.Name}";
        }
        Slot slot = _pack.Grid[slotIndex];
        if (slot.Block != null) return $"{item.Name} · CREATIVE";
        return slot.Item == item.Id && slot.Count >= n ? null : $"{item.Name} · STACK {(slot.Item == item.Id ? slot.Count : 0)}";
    }

    /// <summary>Uses one block item from the chosen source (nothing in Creative). False if it has none.</summary>
    bool Take(Vessel v, ItemDef item, int slotIndex, int n)
    {
        if (Creative) return true;
        if (FromStructure) return _pack.HandUsable && _pack.HandSource.Count(item.Id) >= n && _pack.HandSource.Remove(item.Id, n) == n;
        Slot slot = _pack.Grid[slotIndex];
        if (slot.Item != item.Id || slot.Count < n) return false;
        for (int i = 0; i < n; i++) _pack.Grid.TakeOne(slotIndex);
        return true;
    }

    /// <summary>Uses one of the item in hand: from your stack, or from the remote slot's storage.</summary>
    bool TakeHand(string itemId, int slotIndex) => FromStructure ? _pack.HandUsable && _pack.HandSource.Remove(itemId, 1) == 1 : _pack.Grid.TakeOne(slotIndex);

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
        if (what != NewThing.Device && ClaimAim(what, itemId, slotIndex, place)) return;
        if (!_onGround)
        {
            Hide();
            Verdict = "NEEDS OPEN GROUND";
            return;
        }
        // A core or a device is set down from your own stack, or from the remote slot's storage while its core is in reach.
        string from = FromStructure && !_pack.HandUsable ? $"{_pack.HandRemote?.Origin.ToUpperInvariant()} STORAGE · OUT OF RANGE" : FromStructure && _pack.HandSource.Count(itemId) == 0 ? $"{_pack.HandRemote?.Origin.ToUpperInvariant()} STORAGE · 0 {Items.Item(itemId).Name}" : null;
        if (what == NewThing.Device)
        {
            _ghosts.HideGhosts();
            Transform3D at = GroundFrame(_groundPoint, 0f);
            _deviceGhost.GlobalTransform = at.TranslatedLocal(Vector3.Up * 0.6f);
            _deviceGhost.Visible = true;
            Verdict = from ?? $"[LMB] SET DOWN · {Items.Item(itemId).Name}";
            if (place && from == null && TakeHand(itemId, slotIndex))
            {
                bool climate = itemId == Device.ClimateItem;
                MachineDef machine = climate ? null : Items.Machines.All.First(m => m.ItemId == itemId);
                Device d = climate ? Device.PlaceClimate(_zone, at.Origin, _survivor.Yaw + YawOfFrame()) : Device.Place(_zone, machine, machine.ItemId, at.Origin, _survivor.Yaw + YawOfFrame());
                _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Place, at.Origin, 0.8f, -2f);
                DeviceSet?.Invoke(d);
            }
            else if (place && from != null) Said?.Invoke(from, Tones.Caution);
            return;
        }
        _deviceGhost.Visible = false;
        var scratch = new Structure(_catalogue);
        Block core = Building.Make(scratch, _catalogue.CoreType);
        // A new structure sits on the ground as a base; the CORE tab lifts it off once it can fly.
        Transform3D frame = GroundFrame(_groundPoint, scratch.Grid.CellSize * 0.5f);
        _ghosts.ShowGhosts(frame, scratch, new List<(Int3, Block, Color)> { (Int3.Zero, core, from == null ? GhostPreview.Valid : GhostPreview.Invalid) });
        Verdict = from ?? "[LMB] FOUND BASE · Core";
        if (!place) return;
        if (from != null)
        {
            Said?.Invoke(from, Tones.Caution);
            return;
        }
        if (!Creative && !TakeHand(itemId, slotIndex)) return;
        Vessel v = StartStructure(frame);
        if (v == null && !Creative) { if (FromStructure) _pack.HandSource.Add(itemId, 1); else _pack.Add(itemId, 1); }
    }

    /// <summary>
    /// A core in hand aimed at a wreck with no core (a crashed hulk, a severed section): set into it, it claims it
    /// (design: "Any wreck with no core ... can be claimed by placing a core in it"): any wreck takes the core.
    /// True while a wreck is under the crosshair.
    /// </summary>
    bool ClaimAim(NewThing what, string itemId, int slotIndex, bool place)
    {
        if (!WorldRay.Cast(_zone, _survivor, Slice.Game.Game.UseReach + 1f, Layers.Hull, out LookHit look) || look.Vessel is not { } wreck || wreck.Structure.Core != null)
            return false;
        Hide();
        Verdict = $"[LMB] CLAIM · {wreck.Name}";
        if (!place) return true;
        if (!Creative && !TakeHand(itemId, slotIndex)) return true;
        N.Vector3 n = MathX.Unrotate(wreck.Body.Rotation, look.Normal.ToNumerics());
        N.Vector3 a = N.Vector3.Abs(n);
        Int3 face = a.X >= a.Y && a.X >= a.Z ? new Int3(Math.Sign(n.X), 0, 0) : a.Y >= a.Z ? new Int3(0, Math.Sign(n.Y), 0) : new Int3(0, 0, Math.Sign(n.Z));
        string why = wreck.ClaimWith(new Int3(look.Cell.X + face.X, look.Cell.Y + face.Y, look.Cell.Z + face.Z));
        if (why != null)
        {
            if (!Creative) { if (FromStructure) _pack.HandSource.Add(itemId, 1); else _pack.Add(itemId, 1); }
            Said?.Invoke(why, Tones.Caution);
            return true;
        }
        _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Place, look.Point, 0.7f, -2f);
        Claimed?.Invoke(wreck.Name);
        Started?.Invoke(wreck);
        return true;
    }

    /// <summary>A new structure where a core item was set down. The item is its price.</summary>
    Vessel StartStructure(Transform3D frame)
    {
        _structures++;
        var s = new Structure(_catalogue) { Name = $"Base {_structures}", Mode = CoreMode.Base, GravityOn = true };
        Building.Place(s, Int3.Zero, Building.Make(s, _catalogue.CoreType));
        s.DirtyChunks.Clear();
        N.Vector3 origin = frame.Origin.ToNumerics();
        float heading = MathX.Heading(frame.Basis.GetRotationQuaternion().ToNumerics());
        Vessel v = Vessel.SpawnBase(_zone, s, origin, heading);
        v.ClearGround(Int3.Zero);
        Placed++;
        _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Place, frame.Origin, 0.7f, -2f);
        Started?.Invoke(v);
        BlockPlaced?.Invoke(v, XpOf(s, s.Get(Int3.Zero)));
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
            Said?.Invoke(Underway == null ? "BLUEPRINT PUT AWAY" : $"BLUEPRINT STOPPED · {Underway.Name}", Tones.Text);
            StopProjecting();
            return;
        }
        if (Underway == null)
        {
            Aim();
            if (!_onGround)
            {
                _planGhost.Visible = false;
                Verdict = "NEEDS OPEN GROUND";
                if (pressed) Said?.Invoke(Verdict, Tones.Caution); // a click that can't place never goes unanswered
                return;
            }
            Transform3D frame = AnchorFrame(_groundPoint);
            ShowPlan(frame, all: true);
            Verdict = $"{Plan.Name} · {Plan.Count} BLOCKS · {Items.Describe(TotalCost(Plan), 4)}   [LMB] SET DOWN";
            if (pressed) StartFromPlan(frame);
            return;
        }
        if (Underway.IsWreck || Underway.Structure.Core == null)
        {
            Said?.Invoke("BLUEPRINT · CORE LOST", Tones.Warning);
            StopProjecting();
            return;
        }
        if (_planDirty) RefreshRemaining();
        ShowPlan(Underway.DrawTransform, all: false);
        Verdict = $"{Plan.Name} · {Plan.Count - _remaining.Count}/{Plan.Count} BLOCKS   [LMB] HOLD";
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
            Said?.Invoke($"NEEDS · {Items.Describe(bill)}", Tones.Caution);
            return;
        }
        _pack.Grid.Reconcile();
        Count(bill);
        Building.Place(s, coreCell, core);
        s.DirtyChunks.Clear();
        N.Vector3 origin = frame.Origin.ToNumerics();
        float heading = MathX.Heading(frame.Basis.GetRotationQuaternion().ToNumerics());
        Vessel v = s.Mode == CoreMode.Base ? Vessel.SpawnBase(_zone, s, origin, heading) : Vessel.Spawn(_zone, s, origin, heading, landed: true);
        Placed++;
        Started?.Invoke(v);
        Underway = v;
        _planDirty = true;
    }

    /// <summary>An empty structure with a plan's mode, gravity and paints (so its blocks' paint slots mean the same).</summary>
    Structure Like(Structure plan)
    {
        var s = new Structure(_catalogue) { Name = plan.Name, Mode = plan.Mode, GravityOn = plan.GravityOn };
        for (int i = 1; i < plan.Palette.Count; i++) s.Palette.Intern(plan.Palette[(ushort)i], plan.Palette.NameOf((ushort)i));
        return s;
    }

    // ---- Placing and removing ------------------------------------------------------------------

    /// <summary>Why a block can't go here, or null: the rules, the survivor in the way, the ground.</summary>
    string Problem(Vessel v, Int3 cell, in Block block)
    {
        Structure s = v.Structure;
        PlaceError e = Building.Check(s, cell, block);
        if (e != PlaceError.None) return e switch
        {
            PlaceError.Occupied => "OCCUPIED",
            PlaceError.NotAttached or PlaceError.NoSharedFace => "NO SHARED FACE",
            PlaceError.OneCoreOnly => "ONE CORE PER STRUCTURE",
            _ => e.ToString(),
        };
        if (!Creative && BuildingBridge.ItemOf(Items, s, block) == null) return $"{s.TypeOf(block).Name} · NO ITEM";
        foreach (Int3 c in s.CellsOf(cell, block))
        {
            if (InSurvivor(v, c)) return "YOU · IN THE WAY";
            if (!v.IsBase && InGround(v, c)) return "GROUND · IN THE WAY";
        }
        return null;
    }

    /// <summary>Places one block, using its item from the chosen source (<paramref name="slotIndex"/> -1: the projector, from your bag): one, or a door's units.</summary>
    bool TryPlace(Vessel v, Int3 cell, Block block, bool quiet, int slotIndex)
    {
        Structure s = v.Structure;
        if (Problem(v, cell, block) is string why)
        {
            if (!quiet) Said?.Invoke(why, Tones.Caution);
            return false;
        }
        ItemDef item = BuildingBridge.ItemOf(Items, s, block);
        int units = s.TypeOf(block).Units;
        if (!Creative && item != null)
        {
            bool took = slotIndex < 0 ? _pack.Storage.Count(item.Id) >= units && _pack.Storage.Remove(item.Id, units) == units : Take(v, item, slotIndex, units);
            if (!took)
            {
                if (!quiet) Said?.Invoke(slotIndex < 0 ? $"{item.Name} · BAG {_pack.Storage.Count(item.Id)}" : SourceProblem(v, item, slotIndex, units) ?? $"{item.Name} · 0", Tones.Caution);
                return false;
            }
            if (slotIndex < 0) _pack.Grid.Reconcile();
            Count(Bill.Of((item.Id, units)));
        }
        Building.Place(s, cell, block);
        _placer.Sync(s, v.Storage, 0, 0, 0, spill: false); // placing only ever adds room
        if (v.IsBase)
            foreach (Int3 c in s.CellsOf(cell, block)) v.ClearGround(c);
        v.ApplyEdits();
        if (v == Underway) v.Settle(); // a blueprint's vessel stands on the ground as it grows
        Placed++;
        _planDirty |= Underway == v;
        _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Place, v.ToWorld(StructureBridge.BlockCenter(s, cell, block)), 1.3f, -6f);
        BlockPlaced?.Invoke(v, XpOf(s, block));
        return true;
    }

    /// <summary>
    /// What the tool says on a core: design 693, with no exception for the last block.
    /// </summary>
    public const string CoreStays = "CORE · CAN'T REMOVE";

    /// <summary>
    /// Takes a block down: Survival gives its item back, whole, and sets down whatever has nowhere
    /// to go; sections it cuts loose become wrecks. The core stays: it can only be destroyed or
    /// hijacked, even when it is the last block.
    /// </summary>
    public void Remove(Vessel v, Int3 cell)
    {
        Structure s = v.Structure;
        // Any cell of a block spanning several takes the whole block down.
        if (!s.TryGetBlock(cell, out cell, out Block b)) return;
        N.Vector3 centre = StructureBridge.BlockCenter(s, cell, b);
        // A base's blocks hold the ground off their faces: the ones that leave it let go.
        var before = v.IsBase ? s.Cells().Select(x => x.pos).ToList() : null;
        ItemDef item = BuildingBridge.ItemOf(Items, s, b);
        Bill back = Creative || item == null ? new Bill() : Bill.Of((item.Id, 1));
        RemoveResult r = Building.Remove(s, cell, null);
        if (r.Status == RemoveStatus.CoreHoldsStructure)
        {
            Said?.Invoke(CoreStays, Tones.Caution);
            return;
        }
        if (r.Status != RemoveStatus.Removed) return;
        if (before != null)
            foreach (Int3 p in before)
                if (!s.Has(p)) _zone.Edits.ReleaseStructure(v.GroundKey(p));
        TakenDown++;
        Vector3 at = v.ToWorld(centre);
        GiveBack(back, v, at);
        foreach (Structure piece in r.Wrecks) v.Sever(piece);
        // A container gone shrinks storage: what no longer fits is set down, never lost.
        Bill overflow = _placer.Sync(s, v.Storage, 0, 0, 0, spill: false);
        if (!overflow.IsEmpty) Spilled?.Invoke(overflow, at);
        v.ApplyEdits();
        if (v == Underway) v.Settle();
        _planDirty |= Underway == v;
        _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Clank, at, 0.9f, -6f);
        if (!back.IsEmpty) Said?.Invoke($"+{Items.Describe(back, 3)}", Tones.Text);
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
        if (v != null && FromStructure && _pack.HandSource == v.Storage && inArea) left = v.Storage.AddAll(bill);
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

    /// <summary>
    /// Components that mount by one face turn it toward the face clicked (as building's tool does). A
    /// ladder on a wall also stands upright, its rails up the wall and its rungs level, however the
    /// tool was turned.
    /// </summary>
    int AutoOrient(BlockType type, int preferred)
    {
        if (type.IsArmor) return preferred;
        int mask = type.AttachFaces;
        if (mask == 0 || (mask & (mask - 1)) != 0) return preferred;
        var mount = (Dir)System.Numerics.BitOperations.TrailingZeroCount(mask);
        Dir wanted = Dirs.Closest((TargetCell - PlaceCell).ToVector3());
        bool upright = type.IsLadder && wanted.Axis() != Dir.PosY.Axis();
        bool Stands(int o) => !upright || Orientations.ToWorld(o, Dir.PosY) == Dir.PosY;
        if (Orientations.ToWorld(preferred, mount) == wanted && Stands(preferred)) return preferred;
        Mat3 pref = Orientations.Matrix(preferred);
        int best = preferred, bestScore = int.MinValue;
        for (int o = 0; o < Orientations.Count; o++)
        {
            if (Orientations.ToWorld(o, mount) != wanted) continue;
            Mat3 m = Orientations.Matrix(o);
            int score = Int3.Dot(m.X, pref.X) + Int3.Dot(m.Y, pref.Y) + Int3.Dot(m.Z, pref.Z) + (Stands(o) ? 100 : 0);
            if (score > bestScore) { bestScore = score; best = o; }
        }
        return best;
    }

    /// <summary>Experience for placing a block: its item's (shared/Items blocks.json), a door's for each unit.</summary>
    int XpOf(Structure s, in Block b) =>
        Items.Blocks.TryGet(BuildingBridge.ItemBlockOf(s.TypeOf(b)), out BlockBill bill) ? bill.Xp * s.TypeOf(b).Units : 1;

    /// <summary>What placing a block uses: one of its item (nothing in Creative).</summary>
    public Bill CostOf(string blockId) =>
        Creative || Items.BlockItem(blockId) is not ItemDef item ? new Bill() : Bill.Of((item.Id, 1));

    /// <summary>A whole blueprint's block items, one per block (what the projector uses from your bag).</summary>
    public Bill TotalCost(Structure plan)
    {
        var total = new Bill();
        foreach (var (_, b) in plan.Blocks()) total.Add(_placer.CostOf(plan, b));
        return total;
    }
}
