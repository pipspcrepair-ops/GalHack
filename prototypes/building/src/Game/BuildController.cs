using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using GDict = Godot.Collections.Dictionary;

namespace BlockBuilding;

/// <summary>
/// Turns input into building: aims at a structure face, shows the ghost (and its mirror copies),
/// and places, removes, upgrades, repairs, paints, toggles and blasts. Every rule lives in the
/// structure core; this class only decides what the player meant and gives feedback.
/// </summary>
public partial class BuildController : Node
{
    public enum Tool { Build, Paint }

    /// <summary>Hotbar slots 1-9. Pressing a slot's key again cycles its variants.</summary>
    public static readonly string[][] Slots =
    {
        new[] { "steel" }, new[] { "hardened" }, new[] { "combat" }, new[] { "glass", "concrete" },
        new[] { "thruster", "generator", "fueltank" }, new[] { "chair", "light", "turret" },
        new[] { "container", "cpuext" }, new[] { "door", "ladder" }, new[] { "core" },
    };

    /// <summary>The sprayer's colours: a 1970s paperback palette.</summary>
    public static readonly uint[] PaintColors =
    {
        0xece3c8, 0xe85d04, 0xf4a100, 0xc1121f, 0x0a9396, 0x1d3557, 0x2b2d42, 0x94d2bd, 0x6b705c,
    };

    public static readonly string[] PaintColorNames = { "Cream", "Orange", "Saffron", "Red", "Teal", "Navy", "Charcoal", "Mint", "Olive" };

    public Main Main;
    public Player Player;
    public Sfx Sfx;
    public Effects Effects;
    public Hud Hud;

    public Tool Mode = Tool.Build;
    public int Slot;
    public readonly int[] Variant = new int[9];
    public int ShapeIndex;
    public int Rot;
    public Symmetry Symmetry;
    public readonly int[] SymmetryState = new int[3]; // 0 off, 1 through the core's cell, 2 between cells
    public GridSize NewCoreGrid = GridSize.Large;
    public bool OverlayOn;

    /// <summary>Ghost, outline, planes and hints. Off for clean screenshots.</summary>
    public bool PreviewEnabled = true;

    /// <summary>The first click only captures the mouse. Headless tests have no window to capture, so they turn this off.</summary>
    public bool RequireMouseCapture = true;
    public StructureView Active { get; private set; }
    public readonly Inventory Inventory = new() { Creative = true };

    public int PaintBase, PaintAccent = 6, Grime, Stencil = -1;
    public Pattern PaintPattern = Pattern.None;
    public FaceTexture PaintTexture = FaceTexture.Panel;

    /// <summary>What the crosshair is on. Updated every physics tick.</summary>
    public struct AimInfo
    {
        public StructureView View;
        public bool OnBlock, OnGround;
        public Int3 Cell;
        public Dir Face;
        public Vector3 Point, Normal;
    }

    public AimInfo Aim { get; private set; }
    public string Hint { get; private set; } = "";
    public Color HintColor { get; private set; } = Colors.White;

    GhostPreview _ghost;
    SymmetryPlanes _planes;
    AirtightOverlay _overlay;
    int _overlayVersion = -1;
    double _overlayAt;
    Int3? _armedCut;
    StructureView _armedView;
    double _armedUntil;
    Face? _lastSprayed;
    bool _ignoreNextClick;

    public BlockType SelectedType => Main.Catalogue.Type(Slots[Slot][Variant[Slot] % Slots[Slot].Length]);
    public ShapeDef SelectedShape => SelectedType.IsArmor ? Main.Catalogue.Shapes[ShapeIndex] : null;
    public Paint CurrentPaint => new(PaintColors[PaintBase], PaintColors[PaintAccent], PaintPattern, PaintTexture,
        Stencil < 0 ? (byte)0 : (byte)('0' + Stencil), (byte)Grime);

    public override void _Ready()
    {
        _ghost = new GhostPreview { Name = "Ghost" };
        _planes = new SymmetryPlanes { Name = "SymmetryPlanes" };
        Main.AddChild(_ghost);
        Main.AddChild(_planes);
    }

    public void SetActive(StructureView view)
    {
        if (Active == view) return;
        Active = view;
        _overlayVersion = -1;
        if (_overlay != null && IsInstanceValid(_overlay)) _overlay.QueueFree();
        _overlay = null;
        if (view != null && view.Structure.Core is Int3 core) CentreSymmetry(core);
    }

    void CentreSymmetry(Int3 core)
    {
        for (int a = 0; a < 3; a++)
            Symmetry.Set(a, SymmetryState[a] != 0, core[a] * 2 + (SymmetryState[a] == 2 ? 1 : 0));
    }

    // ---- Input ----------------------------------------------------------------------------

    public void HandleInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (RequireMouseCapture && Input.MouseMode != Input.MouseModeEnum.Captured)
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
                return; // this click only grabs the mouse
            }
            switch (mb.ButtonIndex)
            {
                case MouseButton.Left: if (Mode == Tool.Build) Place(); else Spray(Input.IsPhysicalKeyPressed(Key.Shift)); break;
                case MouseButton.Right: if (Mode == Tool.Build) Remove(); else Strip(); break;
                case MouseButton.Middle: if (Mode == Tool.Build) PickBlock(); else Eyedropper(); break;
                case MouseButton.WheelUp: Cycle(-1); break;
                case MouseButton.WheelDown: Cycle(+1); break;
            }
            return;
        }
        if (e is not InputEventKey { Pressed: true } key) return;
        bool shift = key.ShiftPressed;
        if (key.Echo && key.PhysicalKeycode is not (Key.Bracketleft or Key.Bracketright)) return;

        if (key.PhysicalKeycode >= Key.Key1 && key.PhysicalKeycode <= Key.Key9)
        {
            int n = (int)(key.PhysicalKeycode - Key.Key1);
            if (Mode == Tool.Paint)
            {
                if (shift) PaintAccent = n;
                else PaintBase = n;
                Sfx.PlayUi("click", 1f + n * 0.04f);
            }
            else SelectSlot(n);
            return;
        }

        switch (key.PhysicalKeycode)
        {
            case Key.Tab: Cycle(shift ? -1 : 1); break;
            case Key.R when Mode == Tool.Build: Turn(1, shift); break;
            case Key.T when Mode == Tool.Build: Turn(0, shift); break;
            case Key.Y when Mode == Tool.Build: Turn(2, shift); break;
            case Key.R when Mode == Tool.Paint:
                PaintTexture = (FaceTexture)(((int)PaintTexture + 1) % 4);
                Sfx.PlayUi("click");
                break;
            case Key.X: ToggleSymmetry(0); break;
            case Key.V: ToggleSymmetry(1); break;
            case Key.Z: ToggleSymmetry(2); break;
            case Key.Bracketleft: Stencil = Stencil <= -1 ? 9 : Stencil - 1; Sfx.PlayUi("click"); break;
            case Key.Bracketright: Stencil = Stencil >= 9 ? -1 : Stencil + 1; Sfx.PlayUi("click"); break;
            case Key.Minus: Grime = Math.Max(0, Grime - 1); Sfx.PlayUi("click"); break;
            case Key.Equal: Grime = Math.Min(3, Grime + 1); Sfx.PlayUi("click"); break;
            case Key.U: UpgradeOrRepair(); break;
            case Key.E: Interact(); break;
            case Key.O: ToggleOverlay(); break;
            case Key.K: Blast(); break;
            case Key.G:
                NewCoreGrid = NewCoreGrid == GridSize.Large ? GridSize.Small : GridSize.Large;
                Hud.Toast($"New cores start a {(NewCoreGrid == GridSize.Large ? "large-grid (2 m) base or capital ship" : "small-grid (0.5 m) vessel")}", Hud.Info);
                Sfx.PlayUi("toggle");
                break;
            case Key.B: ToggleCoreMode(); break;
            case Key.P:
                Mode = Mode == Tool.Build ? Tool.Paint : Tool.Build;
                Hud.Toast(Mode == Tool.Paint ? "Paint sprayer" : "Multi-tool", Hud.Info);
                Sfx.PlayUi("toggle");
                break;
            case Key.F5: Save(); break;
            case Key.F9: Load(); break;
            case Key.Escape: Input.MouseMode = Input.MouseModeEnum.Visible; break;
        }
    }

    void SelectSlot(int n)
    {
        if (Slot == n) Variant[n] = (Variant[n] + 1) % Slots[n].Length;
        Slot = n;
        Sfx.PlayUi("click", 1f + n * 0.03f);
    }

    void Cycle(int step)
    {
        if (Mode == Tool.Paint)
        {
            PaintPattern = (Pattern)(((int)PaintPattern + step + 5) % 5);
            Sfx.PlayUi("click");
            return;
        }
        if (!SelectedType.IsArmor)
        {
            Hud.Toast($"{SelectedType.Name} has a fixed shape", Hud.Muted);
            return;
        }
        int count = Main.Catalogue.Shapes.Count;
        ShapeIndex = (ShapeIndex + step + count) % count;
        Sfx.PlayUi("click", 1.1f);
    }

    void Turn(int axis, bool back)
    {
        Rot = Orientations.Rotate(Rot, axis, back ? -1 : 1);
        Sfx.PlayUi("click", 0.9f);
    }

    void ToggleSymmetry(int axis)
    {
        SymmetryState[axis] = (SymmetryState[axis] + 1) % 3;
        Int3 centre = Active?.Structure.Core ?? Int3.Zero;
        CentreSymmetry(centre);
        string[] names = { "X (left/right)", "Y (up/down)", "Z (front/back)" };
        Hud.Toast(SymmetryState[axis] switch
        {
            0 => $"Mirror {names[axis]} off",
            1 => $"Mirror {names[axis]} through the core",
            _ => $"Mirror {names[axis]} between cells",
        }, Hud.Info);
        Sfx.PlayUi("toggle");
    }

    // ---- Aiming ---------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        UpdateAim();
        // Hold the trigger to keep spraying as the crosshair sweeps across faces.
        if (Mode == Tool.Paint && Input.IsMouseButtonPressed(MouseButton.Left) && Input.MouseMode == Input.MouseModeEnum.Captured)
            Spray(Input.IsPhysicalKeyPressed(Key.Shift), dragging: true);
        else _lastSprayed = null;
    }

    float Reach => Player.Flying ? 70f : 18f;

    void UpdateAim()
    {
        Camera3D cam = Player.Camera;
        Vector3 from = cam.GlobalPosition, dir = -cam.GlobalBasis.Z;
        var aim = new AimInfo();
        GDict hit = Raycast(from, from + dir * Reach);
        if (hit.Count > 0)
        {
            aim.Point = (Vector3)hit["position"];
            aim.Normal = (Vector3)hit["normal"];
            if (hit["collider"].As<GodotObject>() is StructureView view)
            {
                Vector3 local = view.GlobalTransform.AffineInverse() * aim.Point;
                Vector3 n = (view.GlobalBasis.Inverse() * aim.Normal).Normalized();
                float size = view.CellSize;
                Int3 cell = view.LocalToCell(local - n * size * 0.02f);
                if (!view.Structure.Has(cell)) cell = view.LocalToCell(local - n * size * 0.3f);
                if (view.Structure.Has(cell))
                {
                    aim.View = view;
                    aim.OnBlock = true;
                    aim.Cell = cell;
                    aim.Face = Dirs.Closest(new System.Numerics.Vector3(n.X, n.Y, n.Z));
                    SetActive(view);
                }
            }
            else aim.OnGround = true;
        }
        Aim = aim;
    }

    GDict Raycast(Vector3 from, Vector3 to)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to, PhysicsLayers.World | PhysicsLayers.Structures, new Godot.Collections.Array<Rid> { Player.GetRid() });
        return Player.GetWorld3D().DirectSpaceState.IntersectRay(query);
    }

    // ---- Preview --------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        UpdatePreview();
        UpdateOverlay();
    }

    void UpdatePreview()
    {
        AimInfo aim = Aim;
        Hint = "";
        _ghost.HideGhosts();
        _ghost.HideOutline();
        if (!PreviewEnabled)
        {
            _planes.HideAll();
            return;
        }

        StructureView view = aim.View ?? Active;
        if (view != null && IsInstanceValid(view) && Symmetry.Any && view.Structure.Count > 0)
        {
            var (min, max) = view.Structure.Bounds();
            _planes.Show(view.GlobalTransform, view.CellSize, Symmetry, min, max);
        }
        else _planes.HideAll();

        if (Mode == Tool.Paint)
        {
            if (aim.OnBlock) _ghost.ShowOutline(aim.View.GlobalTransform, aim.View.Structure, aim.Cell, ChunkMesher.Rgb(PaintColors[PaintBase]));
            return;
        }

        if (aim.OnBlock)
        {
            bool armed = _armedCut == aim.Cell && _armedView == aim.View && Time.GetTicksMsec() / 1000.0 < _armedUntil;
            _ghost.ShowOutline(aim.View.GlobalTransform, aim.View.Structure, aim.Cell, armed ? GhostPreview.Warning : new Color(1, 1, 1, 0.9f));
            PreviewOnStructure(aim);
        }
        else if (aim.OnGround) PreviewOnGround(aim);
    }

    /// <summary>The block (and its mirror images) that a click would add, with what's wrong if anything.</summary>
    List<(Int3 cell, Block block, PlaceError error)> Candidates(Structure s, Int3 cell, Dir face)
    {
        BlockType type = SelectedType;
        int rot = AutoOrient(type, face, Rot);
        Block block = Building.Make(s, type, SelectedShape, rot);
        var copies = Building.MirrorCopies(s, cell, block, Symmetry);
        var result = new List<(Int3, Block, PlaceError)>();
        foreach (Placement c in copies)
            result.Add((c.Pos, c.Block, Building.Check(s, c.Pos, c.Block)));
        return result;
    }

    void PreviewOnStructure(AimInfo aim)
    {
        Structure s = aim.View.Structure;
        Int3 cell = aim.Cell + aim.Face.Vec();
        var candidates = Candidates(s, cell, aim.Face);
        bool blocked = InPlayer(aim.View, cell);
        PlaceError first = candidates[0].error;

        double addCpu = candidates.Where(c => c.error == PlaceError.None).Sum(c => (double)s.StatsOf(c.block).Cpu);
        StructureStats st = aim.View.Stats;
        string warning = null;
        if (st != null && st.CpuLimit > 0 && first == PlaceError.None && st.CpuUsed + addCpu > st.CpuLimit)
        {
            double after = StructureStats.EfficiencyFor(st.CpuUsed + addCpu, st.CpuLimit);
            warning = st.CpuUsed <= st.CpuLimit
                ? $"Crosses the T{st.Tier} CPU limit ({st.CpuLimit:N0}): components drop to {after:P0}"
                : $"Over CPU: efficiency {st.Efficiency:P0} → {after:P0}. Add a CPU extender (7).";
        }

        Color tint = blocked || first != PlaceError.None ? GhostPreview.Invalid : warning != null ? GhostPreview.Warning : GhostPreview.Valid;
        var ghosts = candidates.Select(c => (c.cell, c.block, c.error == PlaceError.None && !blocked ? tint : GhostPreview.Invalid)).ToList();
        _ghost.ShowGhosts(aim.View.GlobalTransform, s, ghosts);

        if (blocked) SetHint("You're standing there", GhostPreview.Invalid);
        else if (first != PlaceError.None) SetHint(Explain(first, s), GhostPreview.Invalid);
        else if (warning != null) SetHint(warning, GhostPreview.Warning);
        else if (Building.IsDamaged(s, aim.Cell)) SetHint("Damaged block: U to repair", GhostPreview.Warning);
    }

    void PreviewOnGround(AimInfo aim)
    {
        if (!SelectedType.IsCore)
        {
            SetHint("Blocks attach to a structure. Start one with a core (9).", new Color(1f, 0.85f, 0.6f));
            return;
        }
        GridDef grid = Main.Catalogue.Grid(NewCoreGrid);
        var scratch = new Structure(Main.Catalogue, grid);
        _ghost.ShowGhosts(GroundFrame(aim.Point, grid), scratch,
            new[] { (Int3.Zero, Building.Make(scratch, SelectedType), GhostPreview.Valid) });
        SetHint($"Place a {(grid.Size == GridSize.Large ? "large" : "small")}-grid starter core (G switches grid)", GhostPreview.Valid);
    }

    /// <summary>Where a starter core sits on the ground: snapped to the grid, resting on the surface.</summary>
    static Transform3D GroundFrame(Vector3 point, GridDef grid)
    {
        float s = grid.CellSize;
        var p = new Vector3(Mathf.Round(point.X / s) * s, point.Y + s * 0.5f, Mathf.Round(point.Z / s) * s);
        return new Transform3D(Basis.Identity, p);
    }

    /// <summary>
    /// Components that mount by one face (lights, ladders, seats, turrets) turn that face to the
    /// surface you clicked, keeping as much of the chosen rotation as possible. No fiddling.
    /// </summary>
    static int AutoOrient(BlockType type, Dir face, int preferred)
    {
        if (type.IsArmor) return preferred;
        int mask = type.AttachFaces;
        if (mask == 0 || (mask & (mask - 1)) != 0) return preferred; // not exactly one mount face
        var mount = (Dir)System.Numerics.BitOperations.TrailingZeroCount(mask);
        Dir wanted = face.Opposite();
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

    bool InPlayer(StructureView view, Int3 cell)
    {
        Aabb body = Player.Bounds();
        Vector3 local = view.GlobalTransform.AffineInverse() * body.GetCenter();
        Vector3 c = view.CellCenter(cell);
        float half = view.CellSize * 0.5f;
        return Mathf.Abs(local.X - c.X) < half + 0.33f && Mathf.Abs(local.Z - c.Z) < half + 0.33f
            && Mathf.Abs(local.Y - c.Y) < half + body.Size.Y * 0.5f - 0.02f;
    }

    static string Explain(PlaceError e, Structure s) => e switch
    {
        PlaceError.Occupied => "Something is already there",
        PlaceError.NotAttached => "Blocks must attach to the structure",
        PlaceError.NoSharedFace => "No shared face: that side of the neighbour is open",
        PlaceError.StartWithCore => "Start with a core (9)",
        PlaceError.OneCoreOnly => s.IsWreck ? "" : "This structure already has its core",
        PlaceError.NotEnoughMaterials => "Not enough materials",
        _ => e.ToString(),
    };

    void SetHint(string text, Color color)
    {
        Hint = text;
        HintColor = color;
    }

    // ---- Actions --------------------------------------------------------------------------

    void Place()
    {
        AimInfo aim = Aim;
        if (aim.OnGround && SelectedType.IsCore)
        {
            GridDef grid = Main.Catalogue.Grid(NewCoreGrid);
            var s = new Structure(Main.Catalogue, grid) { Name = NewCoreGrid == GridSize.Large ? "New base" : "New vessel", Mode = CoreMode.Base };
            if (NewCoreGrid == GridSize.Small) s.Mode = CoreMode.Capital;
            Building.Place(s, Int3.Zero, Building.Make(s, SelectedType));
            Transform3D frame = GroundFrame(aim.Point, grid);
            StructureView view = Main.AddStructure(s, frame);
            SetActive(view);
            Feedback(view, Int3.Zero, "place");
            Hud.Toast($"Started a {(grid.Size == GridSize.Large ? "large" : "small")}-grid structure. Build onto the core.", Hud.Good);
            return;
        }
        if (!aim.OnBlock)
        {
            Sfx.PlayUi("error");
            return;
        }

        Structure st = aim.View.Structure;
        Int3 cell = aim.Cell + aim.Face.Vec();
        if (InPlayer(aim.View, cell))
        {
            Refuse("You're standing there");
            return;
        }
        BlockType type = SelectedType;
        Block block = Building.Make(st, type, SelectedShape, AutoOrient(type, aim.Face, Rot));
        bool claiming = type.IsCore && st.IsWreck;
        var results = Building.PlaceSymmetric(st, cell, block, Symmetry, Inventory);
        if (results[0].error != PlaceError.None)
        {
            Refuse(Explain(results[0].error, st));
            return;
        }
        aim.View.Sync();
        int placed = 0;
        foreach (var (copy, error) in results)
        {
            if (error != PlaceError.None) continue;
            placed++;
            Feedback(aim.View, copy.Pos, "place", 1f - placed * 0.03f);
        }
        if (claiming)
        {
            st.Name = st.Name.Replace(" wreck", " (salvaged)");
            Hud.Toast("Wreck claimed: the core makes it yours", Hud.Good);
        }
        else if (placed > 1) Hud.Toast($"{Describe(type, SelectedShape)} ×{placed}", Hud.Muted);
    }

    void Remove()
    {
        AimInfo aim = Aim;
        if (!aim.OnBlock) return;
        StructureView view = aim.View;
        Structure s = view.Structure;

        var cells = new List<Int3> { aim.Cell };
        if (Symmetry.Any)
            foreach (Placement c in Building.MirrorCopies(s, aim.Cell, s.Get(aim.Cell), Symmetry).Skip(1))
                if (s.Has(c.Pos)) cells.Add(c.Pos);

        if (s.Core == aim.Cell && s.Count > 1)
        {
            Refuse("The core holds this structure together");
            return;
        }

        // Cutting a section off is allowed (it becomes a wreck), but never by accident.
        double now = Time.GetTicksMsec() / 1000.0;
        bool confirmed = _armedCut == aim.Cell && _armedView == view && now < _armedUntil;
        if (!confirmed && Building.WouldSever(s, aim.Cell, out int cut))
        {
            _armedCut = aim.Cell;
            _armedView = view;
            _armedUntil = now + 2.5;
            Refuse($"That cuts {cut} blocks loose. Right-click again to cut them off.", warn: true);
            return;
        }
        _armedCut = null;

        var wrecks = new List<Structure>();
        int removed = 0;
        foreach (Int3 c in cells)
        {
            if (!s.TryGet(c, out Block b)) continue;
            RemoveResult r = Building.Remove(s, c, Inventory);
            if (r.Status == RemoveStatus.CoreHoldsStructure)
            {
                if (c == aim.Cell) Refuse("The core holds this structure together");
                continue;
            }
            if (r.Status != RemoveStatus.Removed) continue;
            removed++;
            wrecks.AddRange(r.Wrecks);
            Vector3 at = view.CellToGlobal(c);
            Effects.Debris(at, view.CellSize, BlockColor(s, r.Block), 10, 3f);
            Sfx.Play("remove", at, view.CellSize < 1f ? 1.5f : 1f);
        }
        view.Sync();
        foreach (Structure w in wrecks) Main.AddWreck(w, view, Vector3.Zero);
        if (wrecks.Count > 0)
        {
            Sfx.Play("creak", view.GlobalPosition);
            Hud.Toast($"{wrecks.Sum(w => w.Count)} blocks broke away as a wreck", Hud.Warn);
        }
        if (s.Count == 0) Main.RemoveStructure(view);
    }

    void PickBlock()
    {
        if (!Aim.OnBlock) return;
        Structure s = Aim.View.Structure;
        Block b = s.Get(Aim.Cell);
        BlockType t = s.TypeOf(b);
        for (int i = 0; i < Slots.Length; i++)
        {
            int v = Array.IndexOf(Slots[i], t.Id);
            if (v < 0) continue;
            Slot = i;
            Variant[i] = v;
            if (t.IsArmor) ShapeIndex = b.Shape;
            Rot = b.Rot;
            Sfx.PlayUi("click", 1.2f);
            Hud.Toast($"Picked {Describe(t, t.IsArmor ? s.ShapeOf(b) : null)}", Hud.Muted);
            return;
        }
    }

    /// <summary>U: fix a damaged block, otherwise upgrade its armour a step (steel → hardened → combat), shape and paint kept.</summary>
    void UpgradeOrRepair()
    {
        if (!Aim.OnBlock) return;
        StructureView view = Aim.View;
        Structure s = view.Structure;
        var cells = new List<Int3> { Aim.Cell };
        if (Symmetry.Any)
            foreach (Placement c in Building.MirrorCopies(s, Aim.Cell, s.Get(Aim.Cell), Symmetry).Skip(1))
                if (s.Has(c.Pos)) cells.Add(c.Pos);

        if (Building.IsDamaged(s, Aim.Cell))
        {
            // Repairs consume materials: a share of the block's recipe, for the health it gets back.
            if (Building.Repair(s, Aim.Cell, Inventory) == RepairStatus.NotEnoughMaterials)
            {
                Refuse("Not enough materials to repair that");
                return;
            }
            foreach (Int3 c in cells.Skip(1)) Building.Repair(s, c, Inventory);
            view.Sync();
            foreach (Int3 c in cells) Feedback(view, c, "upgrade", 0.8f);
            Hud.Toast("Repaired", Hud.Good);
            return;
        }
        UpgradeStatus status = Building.Upgrade(s, Aim.Cell, Inventory);
        if (status != UpgradeStatus.Upgraded)
        {
            Refuse(status switch
            {
                UpgradeStatus.AlreadyBest => "Already the toughest armour",
                UpgradeStatus.NotArmor => "Only armour can be upgraded",
                _ => "Can't upgrade that",
            });
            return;
        }
        foreach (Int3 c in cells.Skip(1)) Building.Upgrade(s, c, Inventory);
        view.Sync();
        foreach (Int3 c in cells) Feedback(view, c, "upgrade");
        Hud.Toast($"Upgraded to {s.TypeOf(s.Get(Aim.Cell)).Name}", Hud.Good);
    }

    void Interact()
    {
        if (!Aim.OnBlock) return;
        Structure s = Aim.View.Structure;
        BlockType t = s.TypeOf(s.Get(Aim.Cell));
        if (!Building.Toggle(s, Aim.Cell))
        {
            Sfx.PlayUi("error");
            return;
        }
        Aim.View.Sync();
        Sfx.Play("toggle", Aim.View.CellToGlobal(Aim.Cell));
        if (t.IsCore) Hud.Toast($"Core gravity {(s.GravityOn ? "on" : "off")}", Hud.Info);
        else if (t.IsDoor) Hud.Toast(s.Get(Aim.Cell).Open ? "Door open" : "Door closed", Hud.Muted);
    }

    void ToggleCoreMode()
    {
        StructureView view = Active;
        if (view == null || !IsInstanceValid(view)) return;
        Structure s = view.Structure;
        if (s.IsWreck)
        {
            Refuse("A wreck has no core. Place one (9) to claim it.");
            return;
        }
        if (!Building.ToggleCoreMode(s)) return;
        view.Sync();
        Sfx.Play("upgrade", view.CellToGlobal(s.Core.Value), 0.7f);
        Hud.Toast(s.Mode switch
        {
            CoreMode.Base => "Core set to BASE MODE: every block stays, it just won't fly",
            CoreMode.Capital => "Core set to CAPITAL MODE: same blocks, thrusters live",
            CoreMode.Hover => "Core set to HOVER MODE: hover plates lift, thrusters only push it along",
            _ => "Core set to VESSEL MODE: thrusters fly it, hover plates idle",
        }, Hud.Good);
    }

    // ---- Paint ----------------------------------------------------------------------------

    void Spray(bool wholeBlock, bool dragging = false)
    {
        if (!Aim.OnBlock) return;
        var face = new Face(Aim.Cell, Aim.Face);
        if (dragging && _lastSprayed == face) return;
        _lastSprayed = face;
        Structure s = Aim.View.Structure;
        Paint paint = CurrentPaint;
        bool changed = false;
        foreach (var (cell, dir) in MirroredFaces(s, Aim.Cell, Aim.Face))
            foreach (Dir d in wholeBlock ? Dirs.All : new[] { dir })
                changed |= Building.PaintFace(s, cell, d, paint);
        if (!changed) return;
        Aim.View.Sync();
        Sfx.Play("paint", Aim.Point, 1f + (float)GD.RandRange(-0.1, 0.1), -4f);
    }

    void Strip()
    {
        if (!Aim.OnBlock) return;
        Structure s = Aim.View.Structure;
        bool changed = false;
        foreach (var (cell, dir) in MirroredFaces(s, Aim.Cell, Aim.Face))
            changed |= Building.PaintFace(s, cell, dir, null);
        if (!changed) return;
        Aim.View.Sync();
        Sfx.Play("remove", Aim.Point, 2.2f, -10f);
    }

    void Eyedropper()
    {
        if (!Aim.OnBlock) return;
        if (Building.PaintOf(Aim.View.Structure, Aim.Cell, Aim.Face) is not Paint p)
        {
            Hud.Toast("Bare metal", Hud.Muted);
            return;
        }
        PaintBase = Nearest(p.Base);
        PaintAccent = Nearest(p.Accent);
        PaintPattern = p.Pattern;
        PaintTexture = p.Texture;
        Stencil = p.Stencil == 0 ? -1 : p.Stencil - '0';
        Grime = p.Grime;
        Sfx.PlayUi("click", 1.3f);
        Hud.Toast("Picked paint", Hud.Muted);
    }

    static int Nearest(uint color)
    {
        Color c = ChunkMesher.Rgb(color);
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < PaintColors.Length; i++)
        {
            Color p = ChunkMesher.Rgb(PaintColors[i]);
            float d = (p.R - c.R) * (p.R - c.R) + (p.G - c.G) * (p.G - c.G) + (p.B - c.B) * (p.B - c.B);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>The face under the crosshair and its mirror images (the face direction mirrors too).</summary>
    IEnumerable<(Int3 cell, Dir dir)> MirroredFaces(Structure s, Int3 cell, Dir face)
    {
        var faces = new List<(Int3, Dir)> { (cell, face) };
        for (int a = 0; a < 3; a++)
        {
            if (!Symmetry.On(a)) continue;
            int n = faces.Count;
            for (int i = 0; i < n; i++)
            {
                var (c, d) = faces[i];
                var m = (Symmetry.Mirror(c, a), d.Mirror(a));
                if (!faces.Contains(m)) faces.Add(m);
            }
        }
        return faces.Where(f => s.Has(f.Item1));
    }

    // ---- Overlay, blaster, blueprints -----------------------------------------------------

    void ToggleOverlay()
    {
        OverlayOn = !OverlayOn;
        _overlayVersion = -1;
        if (!OverlayOn && _overlay != null && IsInstanceValid(_overlay))
        {
            _overlay.QueueFree();
            _overlay = null;
        }
        Sfx.PlayUi("toggle", OverlayOn ? 1.2f : 0.8f);
        Hud.Toast(OverlayOn ? "Visor: air overlay on" : "Visor: air overlay off", Hud.Info);
    }

    public RoomReport Rooms => _overlay?.Report;

    void UpdateOverlay()
    {
        if (!OverlayOn || Active == null || !IsInstanceValid(Active)) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (_overlayVersion == Active.Structure.Version || now - _overlayAt < 0.2) return;
        if (_overlay == null || !IsInstanceValid(_overlay) || _overlay.GetParent() != Active)
        {
            _overlay = new AirtightOverlay { Name = "AirOverlay" };
            Active.AddChild(_overlay);
        }
        _overlayVersion = Active.Structure.Version;
        _overlayAt = now;
        _overlay.Refresh(Active.Structure, GalHack.Structures.Rooms.Detect(Active.Structure));
    }

    /// <summary>K: a debug blaster that blows chunks out of whatever it hits, to test damage and severing.</summary>
    public void Blast()
    {
        Camera3D cam = Player.Camera;
        Vector3 from = cam.GlobalPosition, dir = -cam.GlobalBasis.Z;
        GDict hit = Raycast(from, from + dir * 400f);
        Vector3 muzzle = from + cam.GlobalBasis * new Vector3(0.3f, -0.25f, -0.6f);
        Sfx.Play("zap", muzzle);
        if (hit.Count == 0)
        {
            Effects.Tracer(muzzle, from + dir * 120f);
            return;
        }
        var point = (Vector3)hit["position"];
        Effects.Tracer(muzzle, point);
        Effects.Explosion(point, 2.6f);
        Sfx.Play("boom", point);
        if (hit["collider"].As<GodotObject>() is not StructureView view) return;
        BlastAt(view, point, dir);
    }

    /// <summary>Explosive damage around a world point on a structure; anything cut off falls away as a wreck.</summary>
    public DamageReport BlastAt(StructureView view, Vector3 point, Vector3 dir)
    {
        Structure s = view.Structure;
        Vector3 local = view.GlobalTransform.AffineInverse() * point / view.CellSize;
        float radius = view.CellSize < 1f ? 3.5f : 1.7f;
        DamageReport report = Damage.Blast(s, new System.Numerics.Vector3(local.X, local.Y, local.Z), radius, DamageType.Explosive, 1800f * s.Grid.HpScale);
        foreach (var (pos, block) in report.Destroyed)
            Effects.Debris(view.CellToGlobal(pos), view.CellSize, BlockColor(s, block), 8, 7f);
        view.Sync();
        // Blasted sections are thrown clear and roll, so the break reads at a glance.
        foreach (Structure w in report.Wrecks) Main.AddWreck(w, view, dir * 4.5f + Vector3.Up * 2.5f, dir.Cross(Vector3.Up) * -1.6f);
        if (report.Wrecks.Count > 0)
        {
            Sfx.Play("creak", point);
            Hud.Toast($"{report.Wrecks.Sum(w => w.Count)} blocks severed: now an unowned wreck", Hud.Warn);
        }
        if (s.Count == 0) Main.RemoveStructure(view);
        return report;
    }

    const string SaveDir = "res://saves", SavePath = "res://saves/quicksave.gbp";

    void Save()
    {
        StructureView view = Active;
        if (view == null || !IsInstanceValid(view)) return;
        DirAccess.MakeDirRecursiveAbsolute(SaveDir);
        string text = Blueprint.Save(view.Structure);
        using (var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write))
        {
            if (file == null)
            {
                Refuse($"Could not save: {FileAccess.GetOpenError()}");
                return;
            }
            file.StoreString(text);
        }
        Sfx.PlayUi("upgrade", 1.2f);
        Hud.Toast($"Saved {view.Structure.Name}: {view.Structure.Count} blocks, {text.Count(c => c == '\n')} lines → {SavePath}", Hud.Good);
    }

    void Load()
    {
        if (!FileAccess.FileExists(SavePath))
        {
            Refuse("No blueprint saved yet (F5 saves the structure you're looking at)");
            return;
        }
        Structure s = Blueprint.Load(GameData.ReadText(SavePath), Main.Catalogue, out List<string> errors);
        if (s == null)
        {
            Refuse($"Blueprint rejected: {errors[0]}");
            return;
        }
        StructureView old = Active;
        Transform3D frame = old != null && IsInstanceValid(old) ? old.GlobalTransform : Main.PadFrame(s);
        if (old != null && IsInstanceValid(old)) Main.RemoveStructure(old);
        StructureView view = Main.AddStructure(s, frame);
        SetActive(view);
        Sfx.PlayUi("upgrade", 0.9f);
        Hud.Toast($"Loaded {s.Name}: {s.Count} blocks", Hud.Good);
    }

    // ---- Feedback helpers -----------------------------------------------------------------

    void Feedback(StructureView view, Int3 cell, string sound, float pitch = 1f)
    {
        Effects.PlaceFlash(view.CellTransform(cell), view.CellSize);
        Sfx.Play(sound, view.CellToGlobal(cell), pitch * (view.CellSize < 1f ? 1.6f : 1f));
    }

    void Refuse(string why, bool warn = false)
    {
        if (!string.IsNullOrEmpty(why)) Hud.Toast(why, warn ? Hud.Warn : Hud.Bad);
        Sfx.PlayUi("error");
    }

    static Color BlockColor(Structure s, Block b)
    {
        foreach (Dir d in Dirs.All)
            if (b.Paint[d] != 0) return ChunkMesher.Rgb(s.Palette[b.Paint[d]].Base);
        return ChunkMesher.Rgb(s.TypeOf(b).Color);
    }

    public static string Describe(BlockType t, ShapeDef shape) =>
        t.IsArmor && shape != null && !shape.IsCube ? $"{t.Name} {shape.Name.ToLowerInvariant()}" : t.Name;

    // ---- Screenshot hooks -----------------------------------------------------------------

    public void DebugForceAim() => UpdateAim();
}
