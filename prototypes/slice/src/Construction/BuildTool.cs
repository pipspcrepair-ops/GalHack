using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Player;
using Slice.Surface;
using Slice.Vessels;
using Inventory = GalHack.Structures.Inventory;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Construction;

/// <summary>How the build tool is being used: any block anywhere, or a blueprint piece by piece.</summary>
public enum BuildStyle { Free, Blueprint }

/// <summary>One block the free-build palette offers.</summary>
public readonly record struct BlockChoice(string Type, string Shape, string Label);

/// <summary>
/// The multi-tool in survival: building's structure model and rules (attach through a shared
/// face, one core, nothing lost when something is cut loose), its ghosts and mesher, and a cost
/// for every block from <see cref="IBuildCosts"/> (the pack first, then the target's storage).
///
/// Free building places the palette's block on the face under the crosshair, or starts a new
/// structure with a core on the ground. Blueprint building sets a blueprint's ghost down on the
/// ground; holding the button then places its blocks one at a time, nearest the core first,
/// each one paid for as it goes, until the vessel is whole.
/// </summary>
public sealed class BuildTool
{
    public static readonly BlockChoice[] Palette =
    {
        new("steel", "cube", "Steel block"), new("steel", "slope", "Steel slope"), new("glass", "cube", "Glass"),
        new("thruster", null, "Thruster"), new("gyro", null, "Gyro"), new("generator", null, "Generator"),
        new("cockpit", null, "Cockpit seat"), new("gear", null, "Landing leg"), new("container", null, "Container"),
        new("light", null, "Light"), new("core", null, "Core (starts a new vessel)"),
    };

    readonly SurfaceZone _zone;
    readonly Survivor _survivor;
    readonly Inventory _pack;
    readonly SliceRules.BuildRules _rules;
    readonly GhostPreview _ghosts;
    readonly MeshInstance3D _planGhost;
    readonly ShaderMaterial _planMaterial;
    readonly Catalogue _catalogue;
    readonly GridDef _small;

    public IBuildCosts Costs;
    public BuildStyle Style = BuildStyle.Free;
    public int Slot;
    public int Rot = Orientations.Identity;
    public bool MirrorX;

    /// <summary>The blueprint being followed and the vessel growing from it.</summary>
    public Structure Plan { get; private set; }
    public Vessel Underway { get; private set; }

    /// <summary>Blocks of the plan still to place, nearest the core first.</summary>
    public IReadOnlyList<(Int3 cell, Block block)> Remaining => _remaining;

    /// <summary>Blocks placed and materials spent since the start (tests).</summary>
    public int Placed { get; private set; }
    public readonly SortedDictionary<string, int> Spent = new(StringComparer.Ordinal);

    /// <summary>What the tool says: "missing 12 iron", "the Skiff is complete".</summary>
    public event Action<string, Color> Said;

    /// <summary>A vessel came into being (a core placed) or was finished from its blueprint.</summary>
    public event Action<Vessel> Started, Finished;

    // Aim, each tick.
    public bool HasTarget { get; private set; }
    public Vessel TargetVessel { get; private set; }
    public Int3 TargetCell { get; private set; }
    public Int3 PlaceCell { get; private set; }
    public string Verdict { get; private set; } = "";
    Vector3 _groundPoint;
    bool _onGround;

    readonly List<(Int3 cell, Block block)> _remaining = new();
    float _placeTimer;
    bool _planDirty = true;

    public BuildTool(SurfaceZone zone, Survivor survivor, Inventory pack, IBuildCosts costs)
    {
        _zone = zone;
        _survivor = survivor;
        _pack = pack;
        Costs = costs;
        _rules = zone.Data.Rules.Build;
        _catalogue = zone.Data.Catalogue;
        _small = _catalogue.Grid(GridSize.Small);
        _ghosts = new GhostPreview { Name = "BuildGhosts" };
        zone.AddChild(_ghosts);
        _planMaterial = BlockMaterials.Ghost(GhostPreview.Valid, 0.18f);
        _planGhost = new MeshInstance3D
        {
            Name = "BlueprintGhost", MaterialOverride = _planMaterial, Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off,
        };
        zone.AddChild(_planGhost);
    }

    public BlockChoice Choice => Palette[Slot];

    /// <summary>The stores a build near this vessel draws on: the pack, then its storage.</summary>
    IReadOnlyList<Inventory> Stores(Vessel near) => near == null ? new[] { _pack } : new[] { _pack, near.Cargo };

    // ---- Each tick ------------------------------------------------------------------------------

    /// <summary>One tick of the tool in hand: aim, show the ghost, and act on the buttons.</summary>
    public void Tick(float dt, bool primaryHeld, bool primaryPressed, bool secondaryPressed)
    {
        if (Style == BuildStyle.Blueprint) TickBlueprint(dt, primaryHeld, primaryPressed, secondaryPressed);
        else TickFree(primaryPressed, secondaryPressed);
    }

    public void Hide()
    {
        _ghosts.HideGhosts();
        _ghosts.HideOutline();
        _planGhost.Visible = false;
    }

    public void NextSlot(int step) => Slot = (Slot + step + Palette.Length) % Palette.Length;

    public void Rotate(int axis) => Rot = Orientations.Rotate(Rot, axis, 1);

    // ---- Free building ---------------------------------------------------------------------------

    void TickFree(bool place, bool remove)
    {
        _planGhost.Visible = false;
        Aim();
        if (!HasTarget)
        {
            _ghosts.HideGhosts();
            _ghosts.HideOutline();
            return;
        }
        Structure s = TargetVessel?.Structure;
        BlockType type = _catalogue.Type(Choice.Type);
        if (TargetVessel != null && !type.IsCore)
        {
            Block b = Make(s, Choice, AutoOrient(type, Rot));
            var copies = Copies(s, PlaceCell, b);
            bool ok = true;
            var tinted = new List<(Int3, Block, Color)>();
            foreach (var (cell, block) in copies)
            {
                string why = Problem(TargetVessel, cell, block);
                if (why != null && cell == PlaceCell) { ok = false; Verdict = why; }
                tinted.Add((cell, block, why == null ? GhostPreview.Valid : GhostPreview.Invalid));
            }
            if (ok) Verdict = $"{Choice.Label}: {CostText(s, b)}";
            _ghosts.ShowGhosts(TargetVessel.DrawTransform, s, tinted);
            _ghosts.ShowOutline(TargetVessel.DrawTransform, s, TargetCell, new Color(1, 1, 1, 0.8f));
            if (place && ok) foreach (var (cell, block) in copies) TryPlace(TargetVessel, cell, block, quiet: cell != PlaceCell);
            if (remove) Remove(TargetVessel, TargetCell);
            return;
        }
        _ghosts.HideOutline();
        if (type.IsCore && _onGround)
        {
            // A new structure: its core sits on the ground where you look, turned to face away from you.
            var scratch = new Structure(_catalogue, _small);
            Block core = GalHack.Structures.Building.Make(scratch, type);
            Transform3D frame = GroundFrame(_groundPoint, 0.25f);
            _ghosts.ShowGhosts(frame, scratch, new List<(Int3, Block, Color)> { (Int3.Zero, core, GhostPreview.Valid) });
            Verdict = $"Core: starts a new vessel. {CostText(scratch, core)}";
            if (place) StartVessel(frame, null);
            return;
        }
        _ghosts.HideGhosts();
        Verdict = TargetVessel != null ? "A structure has one core" : "Aim at a vessel's block, or pick the core (wheel) to start one";
        if (remove && TargetVessel != null) Remove(TargetVessel, TargetCell);
    }

    /// <summary>What the crosshair is on: a structure's block face, or open ground.</summary>
    void Aim()
    {
        HasTarget = false;
        TargetVessel = null;
        _onGround = false;
        Vector3 eye = _survivor.WorldEye(), dir = _survivor.WorldLook();
        var query = PhysicsRayQueryParameters3D.Create(eye, eye + dir * _rules.Reach, Layers.World | Layers.Hull);
        Godot.Collections.Dictionary hit = _zone.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return;
        Vector3 point = (Vector3)hit["position"], normal = (Vector3)hit["normal"];
        Body body = _zone.Host.BodyOf((GodotObject)hit["collider"]);
        if (_zone.VesselOf(body) is Vessel v && !v.IsWreck)
        {
            Structure s = v.Structure;
            N.Vector3 local = v.Body.ToLocal((point - normal * 0.02f).ToNumerics());
            TargetCell = StructureBridge.CellAt(s, local);
            if (!s.Has(TargetCell)) return;
            N.Vector3 n = MathX.Unrotate(v.Body.Rotation, normal.ToNumerics());
            PlaceCell = TargetCell + Dirs.Closest(n).Vec();
            TargetVessel = v;
            HasTarget = true;
            return;
        }
        if (body == null)
        {
            _groundPoint = point;
            _onGround = true;
            HasTarget = true;
        }
    }

    /// <summary>The frame of a new structure standing on the ground at a point, nose away from the survivor.</summary>
    Transform3D GroundFrame(Vector3 ground, float lift) =>
        new(new Basis(Vector3.Up, _survivor.Yaw + YawOfFrame()), ground + Vector3.Up * lift);

    float YawOfFrame() => _survivor.Body.Frame == null ? 0f : MathX.Heading(_survivor.Body.Frame.Rotation);

    // ---- Blueprints -----------------------------------------------------------------------------

    /// <summary>Starts following a blueprint: its ghost goes down where you look; the first click sets it there.</summary>
    public void Follow(Structure blueprint)
    {
        Plan = blueprint;
        Underway = null;
        Style = BuildStyle.Blueprint;
        _remaining.Clear();
        _planDirty = true;
    }

    void TickBlueprint(float dt, bool held, bool pressed, bool cancel)
    {
        _ghosts.HideGhosts();
        _ghosts.HideOutline();
        if (Plan == null)
        {
            _planGhost.Visible = false;
            Verdict = "No blueprint chosen";
            return;
        }
        if (cancel)
        {
            Said?.Invoke(Underway == null ? "Blueprint put away" : $"Stopped following the blueprint: the {Underway.Name} stays as it is", Tones.Text);
            Plan = null;
            Underway = null;
            Style = BuildStyle.Free;
            _planGhost.Visible = false;
            return;
        }

        if (Underway == null)
        {
            // Placing: the whole blueprint's ghost stands on the ground under the crosshair.
            Aim();
            if (!_onGround)
            {
                _planGhost.Visible = false;
                Verdict = "Look at open ground to set the blueprint down";
                return;
            }
            Transform3D frame = AnchorFrame(_groundPoint);
            ShowPlan(frame, all: true);
            var plan = Plan;
            Verdict = $"{plan.Name}: {plan.Count} blocks, {TotalText(plan)}. Click to set it down";
            if (pressed) StartVessel(frame, plan);
            return;
        }

        if (Underway.IsWreck || Underway.Structure.Core == null)
        {
            Said?.Invoke("The blueprint's vessel has no core any more", Tones.Warning);
            Plan = null;
            Underway = null;
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
            if (GalHack.Structures.Building.Check(s, cell, block) != PlaceError.None) continue;
            string why = Problem(Underway, cell, block);
            if (why != null)
            {
                blocked ??= why;
                continue;
            }
            if (!TryPlace(Underway, cell, block, quiet: true)) continue;
            _remaining.RemoveAt(i);
            ShowPlanMesh();
            if (_remaining.Count == 0)
            {
                Said?.Invoke($"The {Plan.Name} is complete: take the seat (F) to fly", Tones.Good);
                Vessel done = Underway;
                Plan = null;
                Underway = null;
                Style = BuildStyle.Free;
                _planGhost.Visible = false;
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
        // Anything the walk didn't reach (attached through a partial face only) goes last.
        foreach (var (p, b) in Plan.Blocks())
            if (!seen.Contains(p) && !s.Has(p)) order.Add((p, b));
        _remaining.Clear();
        _remaining.AddRange(order);
        ShowPlanMesh();
    }

    // ---- Placing and removing ------------------------------------------------------------------

    /// <summary>Starts a new vessel with a core at a frame (paid for). With a plan, follows it from there.</summary>
    Vessel StartVessel(Transform3D frame, Structure plan)
    {
        Structure s = plan != null ? Like(plan) : new Structure(_catalogue, _small) { Name = "New Vessel" };
        Int3 coreCell = plan?.Core ?? Int3.Zero;
        Block core = plan != null ? plan.Get(coreCell) : GalHack.Structures.Building.Make(s, _catalogue.CoreType);
        if (!Costs.TryPay(s, core, Stores(null)))
        {
            Said?.Invoke($"A core needs {MissingText(s, core, null)}", Tones.Caution);
            return null;
        }
        Pay(s, core);
        GalHack.Structures.Building.Place(s, coreCell, core);
        s.DirtyChunks.Clear();
        N.Vector3 origin = frame.Origin.ToNumerics();
        float heading = MathX.Heading(frame.Basis.GetRotationQuaternion().ToNumerics());
        Vessel v = Vessel.Spawn(_zone, s, origin, heading, landed: true);
        Placed++;
        Said?.Invoke(plan != null ? $"{plan.Name} begun: hold the button to build it" : "Core placed: a new vessel", Tones.Good);
        Started?.Invoke(v);
        if (plan != null)
        {
            Underway = v;
            _planDirty = true;
        }
        return v;
    }

    /// <summary>An empty structure with a plan's grid, mode, gravity and paints (so its blocks' paint slots mean the same).</summary>
    Structure Like(Structure plan)
    {
        var s = new Structure(_catalogue, plan.Grid) { Name = plan.Name, Mode = plan.Mode, GravityOn = plan.GravityOn };
        for (int i = 1; i < plan.Palette.Count; i++) s.Palette.Intern(plan.Palette[(ushort)i], plan.Palette.NameOf((ushort)i));
        return s;
    }

    /// <summary>Why a block can't go here, or null: the rules, the survivor in the way, the cost.</summary>
    string Problem(Vessel v, Int3 cell, in Block block)
    {
        Structure s = v.Structure;
        PlaceError e = GalHack.Structures.Building.Check(s, cell, block);
        if (e != PlaceError.None) return e switch
        {
            PlaceError.Occupied => "Something is already there",
            PlaceError.NotAttached or PlaceError.NoSharedFace => "Blocks attach through a shared face",
            PlaceError.OneCoreOnly => "A structure has one core",
            _ => e.ToString(),
        };
        if (InSurvivor(v, cell)) return "Step out of the way to build there";
        IReadOnlyDictionary<string, int> missing = Costs.Missing(s, block, Stores(v));
        return missing.Count > 0 ? $"Missing {string.Join(", ", missing.Select(kv => $"{kv.Value} {kv.Key}"))}" : null;
    }

    bool TryPlace(Vessel v, Int3 cell, Block block, bool quiet)
    {
        Structure s = v.Structure;
        if (Problem(v, cell, block) is string why)
        {
            if (!quiet) Said?.Invoke(why, Tones.Caution);
            return false;
        }
        if (!Costs.TryPay(s, block, Stores(v))) return false;
        Pay(s, block);
        GalHack.Structures.Building.Place(s, cell, block);
        v.ApplyEdits();
        Placed++;
        _planDirty |= Underway == v;
        _zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Place, v.Body.ToParent(StructureBridge.CellCenter(s, cell)).ToGodot(), 1.3f, -6f);
        return true;
    }

    void Pay(Structure s, in Block block)
    {
        foreach (var (item, n) in Costs.CostOf(s, block)) Spent[item] = Spent.GetValueOrDefault(item) + n;
    }

    void Remove(Vessel v, Int3 cell)
    {
        Structure s = v.Structure;
        if (!s.TryGet(cell, out Block b)) return;
        RemoveResult r = GalHack.Structures.Building.Remove(s, cell, null);
        if (r.Status == RemoveStatus.CoreHoldsStructure)
        {
            Said?.Invoke("The core holds the vessel together: take the rest down first", Tones.Caution);
            return;
        }
        if (r.Status != RemoveStatus.Removed) return;
        Costs.Refund(s, b, _pack);
        foreach (Structure piece in r.Wrecks) v.Sever(piece);
        if (s.Count == 0)
        {
            // The last block (its core) came off: the vessel is gone, its materials are in the pack.
            _zone.Physics.Unregister(v.Body);
            _zone.Physics.StuckFields.Remove(v.Field);
            _zone.Untrack(v);
            v.Node.QueueFree();
            return;
        }
        v.ApplyEdits();
        _planDirty |= Underway == v;
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

    // ---- Helpers --------------------------------------------------------------------------------

    Block Make(Structure s, BlockChoice choice, int rot)
    {
        BlockType type = _catalogue.Type(choice.Type);
        ShapeDef shape = choice.Shape != null && type.IsArmor ? _catalogue.Shape(choice.Shape) : null;
        return GalHack.Structures.Building.Make(s, type, shape, rot);
    }

    List<(Int3 cell, Block block)> Copies(Structure s, Int3 cell, Block block)
    {
        var sym = Symmetry.Through(s.Core ?? Int3.Zero);
        sym.Set(0, MirrorX, (s.Core ?? Int3.Zero).X * 2);
        return GalHack.Structures.Building.MirrorCopies(s, cell, block, sym).Select(c => (c.Pos, c.Block)).ToList();
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

    string CostText(Structure s, in Block b)
    {
        IReadOnlyDictionary<string, int> cost = Costs.CostOf(s, b);
        return cost.Count == 0 ? "free" : string.Join(", ", cost.Select(kv => $"{kv.Value} {kv.Key}"));
    }

    string MissingText(Structure s, in Block b, Vessel near) =>
        string.Join(", ", Costs.Missing(s, b, Stores(near)).Select(kv => $"{kv.Value} {kv.Key}"));

    /// <summary>A whole blueprint's cost, from the same costs the tool charges.</summary>
    public IReadOnlyDictionary<string, int> TotalCost(Structure plan)
    {
        var total = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, b) in plan.Blocks())
            foreach (var (item, n) in Costs.CostOf(plan, b)) total[item] = total.GetValueOrDefault(item) + n;
        return total;
    }

    string TotalText(Structure plan)
    {
        IReadOnlyDictionary<string, int> total = TotalCost(plan);
        return total.Count == 0 ? "free" : string.Join(", ", total.Select(kv => $"{kv.Value} {kv.Key}"));
    }
}
