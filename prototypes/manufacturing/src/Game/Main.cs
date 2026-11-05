using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using DiegeticUi.Screens;
using Godot;
using Manufacturing.Panels;
using Manufacturing.Screens;

namespace Manufacturing.Game;

/// <summary>
/// The manufacturing workshop: a base on a desert pad with a small constructor, a deconstructor
/// and a blueprint factory along its back wall, all working from the base's one shared storage;
/// a wreck on a salvage pad; a launch pad where the factory's blueprints appear. Build on the
/// base with the multi-tool, paying in survival and free in creative (F2).
///
/// Each machine's screen hangs at eye height on its face. Look at one from within reach and a
/// cursor appears on the glass where you aim; click what's under it.
/// </summary>
public partial class Main : Node3D
{
    const float Reach = 9f, ScreenReach = 3.4f;
    public static readonly uint CrateLayer = 1u << 4;
    static readonly Vector3 PadSpot = new(17, -0.25f, 0), WreckSpot = new(-16, -0.75f, 2);

    // The multi-tool's palette for the base (large grid).
    static readonly string[] Palette = { "steel", "glass", "concrete", "container", "door", "light", "turret", "hardened", "ladder" };
    static readonly string[] Shapes = { "cube", "slope", "corner", "half", "plate" };

    public Workshop Workshop { get; private set; }
    public StructureView BaseView { get; private set; }
    public StructureView WreckView { get; private set; }
    public Player Player { get; private set; }
    public Hud Hud { get; private set; }
    public List<MachineScreen> Screens { get; } = new();
    public MachineScreen ConstructorScreen, DeconstructorScreen, FactoryScreen, StorageScreen;
    /// <summary>Puts the cursor on whichever screen you aim at, within reach, and sends it your clicks.</summary>
    public ScreenPointer Pointer { get; private set; }
    /// <summary>Redraws the screens, only when their pages change and they can be seen.</summary>
    public ScreenManager ScreenManager { get; private set; }
    /// <summary>The machine screen the cursor is on, or null (its page takes the keyboard extras).</summary>
    public MachineScreen Focus => Pointer.Target as MachineScreen;
    /// <summary>The base console on the back wall: a plain WorldScreen showing the structure's settings panel.</summary>
    public WorldScreen ConsoleScreen { get; private set; }
    public StructurePanel StructurePanel { get; private set; }
    public List<StructureView> Spawned { get; } = new();

    GhostPreview _ghost;
    readonly Dictionary<int, Node3D> _crates = new();
    int _slot, _shape, _cratesSeen = -1;
    (bool ok, Int3 place, Int3 remove)? _aim;

    public override void _Ready()
    {
        Kit.UseDiegeticFonts();
        BuildEnvironment();

        Catalogue blocks = DataPaths.BlockCatalogue;
        Structure baseStructure = DataPaths.LoadBlueprint("workshop.gbp");
        Structure wreck = DataPaths.LoadBlueprint("wreck.gbp");
        wreck.Clear(wreck.Core.Value); // shot out: an unowned wreck now
        wreck.Owner = null;
        Workshop = new Workshop(DataPaths.ItemCatalogue, blocks, baseStructure, wreck, LoadBlueprints());
        Workshop.Stock();

        BaseView = StructureView.Create(baseStructure);
        AddChild(BaseView);
        WreckView = StructureView.Create(wreck);
        WreckView.Position = WreckSpot;
        AddChild(WreckView);
        AddChild(_ghost = new GhostPreview());
        Pad(PadSpot + new Vector3(0, -0.74f, 0), new Vector2(9, 9), new Color("f4a100"));
        Pad(WreckSpot + new Vector3(0, -0.24f, 0), new Vector2(6, 6), new Color("c1121f"));

        foreach (var (cell, machine) in BuildingBridge.MachinesOn(Workshop.Items, baseStructure))
        {
            MachinePage page = machine.Kind switch
            {
                MachineKind.Constructor => new ConstructorPage(Workshop),
                MachineKind.Deconstructor => new DeconstructorPage(Workshop),
                _ => new FactoryPage(Workshop),
            };
            MachineScreen screen = Mount(cell, page);
            if (page is ConstructorPage) ConstructorScreen = screen;
            else if (page is DeconstructorPage) DeconstructorScreen = screen;
            else FactoryScreen = screen;
        }
        Int3 container = baseStructure.Blocks().Where(b => baseStructure.TypeOf(b.block).IsContainer).Select(b => b.pos).OrderBy(p => p.X).First();
        StorageScreen = Mount(container, new StoragePage(Workshop));
        MountConsole(container + new Int3(1, 0, 0));

        Player = new Player();
        AddChild(Player);
        Player.StandAndLook(new Vector3(0, 1.05f, 4.5f), new Vector3(0, 1 + Player.EyeHeight, -6));
        AddChild(Hud = new Hud());

        // The pointer aims before this node's own _Process reads it (priority 0), so the
        // multi-tool's ghost and the HUD never lag a frame behind the cursor.
        AddChild(Pointer = new ScreenPointer
        {
            Name = "Pointer", Camera = Player.Camera, Reach = ScreenReach, PickMask = MachineScreen.Layer,
            BlockMask = PhysicsLayers.World | PhysicsLayers.Structures, ProcessPriority = -10,
        });
        AddChild(ScreenManager = new ScreenManager { Name = "ScreenManager", Camera = Player.Camera });

        Workshop.Toast += line => Hud.Toast(line);
        Workshop.Built += Spawn;
    }

    IEnumerable<BlueprintEntry> LoadBlueprints()
    {
        yield return new BlueprintEntry("Gadfly", "starter small vessel", DataPaths.LoadBlueprint("gadfly.gbp"));
        // The physics sandbox's ships, read where they live, if they're there.
        string dir = Path.Combine(DataPaths.Repo, "prototypes", "physics-sandbox", "data", "blueprints");
        foreach (var (file, note) in new[] { ("sandflea.gbp", "hover rover"), ("corsair.gbp", "walk-in shuttle") })
        {
            string path = Path.Combine(dir, file);
            if (!File.Exists(path)) continue;
            Structure s = Blueprint.Load(File.ReadAllText(path), DataPaths.BlockCatalogue, out List<string> errors);
            if (s != null && errors.Count == 0) yield return new BlueprintEntry(s.Name, note, s);
        }
    }

    /// <summary>
    /// Hangs a page's screen on a machine's front face with its centre at standing eye height
    /// above the floor the machine stands on, facing straight out, so you look at it rather than
    /// down at it.
    /// </summary>
    MachineScreen Mount(Int3 cell, MachinePage page)
    {
        var screen = new MachineScreen { Name = page.GetType().Name.Replace("Page", "Screen"), Page = page, Transform = OnFrontFace(cell) };
        AddChild(screen);
        Screens.Add(screen);
        page.Said += line => Hud.Toast(line, IsBad(line));
        return screen;
    }

    /// <summary>
    /// The base console: a wall block's screen showing the structure's settings. The panel is the
    /// kind the Tab menu shows; the screen hosts it as it is.
    /// </summary>
    void MountConsole(Int3 cell)
    {
        ConsoleScreen = new WorldScreen
        {
            Name = "ConsoleScreen", Prompt = "BASE CONSOLE", Pixels = new Vector2I(MachinePage.W, MachinePage.H), SizeMeters = new Vector2(1.6f, 0.9f),
            PickLayer = MachineScreen.Layer, SurfaceShader = Kit.SurfaceShader, BezelLit = Kit.Accent2, Transform = OnFrontFace(cell),
        };
        AddChild(ConsoleScreen);
        ConsoleScreen.SetContent(StructurePanel = new StructurePanel(Workshop));
    }

    /// <summary>A screen on a back-wall block's front face, at eye height above the floor the block stands on.</summary>
    Transform3D OnFrontFace(Int3 cell)
    {
        float size = Workshop.Base.Grid.CellSize;
        var face = new Vector3(cell.X, 0, cell.Z + 0.5f) * size;
        return ScreenMount.OnFace(face, Vector3.Back, floorY: (cell.Y - 0.5f) * size);
    }

    static bool IsBad(string line) => line.StartsWith("REFUSED") || line.Contains("NEEDS") || line.Contains("ONLY") || line.Contains("CAN'T");

    void Spawn(BlueprintEntry e)
    {
        foreach (StructureView old in Spawned) old.QueueFree();
        Spawned.Clear();
        Structure copy = Blueprint.Load(Blueprint.Save(e.Structure), DataPaths.BlockCatalogue, out _);
        copy.DirtyChunks.Clear();
        var view = StructureView.Create(copy);
        // Stand it on the pad: its lowest cell's bottom on the ground.
        (Int3 min, _) = copy.Bounds();
        float cell = copy.Grid.CellSize;
        view.Position = new Vector3(PadSpot.X, -1 + (-min.Y + 0.5f) * cell, PadSpot.Z);
        AddChild(view);
        Spawned.Add(view);
    }

    // ---- World --------------------------------------------------------------------------

    void BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("3d6fb6"), SkyHorizonColor = new Color("e9c99a"),
            GroundBottomColor = new Color("5a4632"), GroundHorizonColor = new Color("d8b98c"), SunAngleMax = 20,
        };
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = sky },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.7f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                GlowEnabled = true, GlowIntensity = 0.35f, GlowHdrThreshold = 1.4f,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 1.3f, ShadowEnabled = true, LightColor = new Color("fff1dc") };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-52, -38, 0);

        var ground = new StaticBody3D { CollisionLayer = PhysicsLayers.World, CollisionMask = 0, Position = new Vector3(0, -2, 0) };
        ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(400, 2, 400) } });
        ground.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(400, 2, 400) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("c9a77a"), Roughness = 0.95f },
        });
        AddChild(ground);
    }

    void Pad(Vector3 at, Vector2 size, Color stripe)
    {
        var pad = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size.X, 0.04f, size.Y) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("5b5f66"), Roughness = 0.8f },
            Position = at,
        };
        AddChild(pad);
        var edge = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size.X + 0.4f, 0.03f, size.Y + 0.4f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = stripe, Roughness = 0.7f },
            Position = at + new Vector3(0, -0.005f, 0),
        };
        AddChild(edge);
    }

    // ---- Every frame -----------------------------------------------------------------------

    public override void _PhysicsProcess(double delta) => Workshop.Tick(delta);

    public override void _Process(double delta)
    {
        Hud.Crosshair = Pointer.Target == null;
        UpdateAim();
        SyncCrates();
        if (Workshop.Wreck.Count == 0 && WreckView.Visible)
        {
            WreckView.Visible = false; // salvaged to the last block
            WreckView.ProcessMode = ProcessModeEnum.Disabled;
        }
        Hud.Mode = $"{Workshop.Rules.Mode.ToString().ToUpperInvariant()}  -  {Workshop.Storage.UsedSu:0}/{Workshop.Storage.CapacitySu:0} SU";
    }

    RayHit RayFrom(uint mask, float reach)
    {
        Camera3D cam = Player.Camera;
        Vector3 from = cam.GlobalPosition, to = from - cam.GlobalBasis.Z * reach;
        var query = PhysicsRayQueryParameters3D.Create(from, to, mask);
        query.CollideWithAreas = false;
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? null : new RayHit(hit);
    }

    /// <summary>A small wrapper so a ray hit reads plainly.</summary>
    sealed class RayHit
    {
        public readonly GodotObject Collider;
        public readonly Vector3 Position, Normal;
        public RayHit(Godot.Collections.Dictionary d)
        {
            Collider = d["collider"].AsGodotObject();
            Position = d["position"].AsVector3();
            Normal = d["normal"].AsVector3();
        }
    }

    void UpdateAim()
    {
        _aim = null;
        Structure s = Workshop.Base;
        if (Pointer.Target != null)
        {
            _ghost.HideGhosts();
            _ghost.HideOutline();
            Hud.Prompt = ""; // the screen shows its own keys
            Hud.Tool = "";
            return;
        }
        RayHit crate = RayFrom(CrateLayer, 3.5f);
        Hud.Prompt = crate != null ? "E: EMPTY THE CRATE INTO STORAGE" : "";

        BlockType type = Workshop.Blocks.Type(Palette[_slot]);
        ShapeDef shape = Workshop.Blocks.Shape(type.IsArmor ? Shapes[_shape] : "cube");
        Block block = Building.Make(s, type, shape);
        Bill cost = Workshop.Placer.CostOf(s, block);
        Bill missing = Workshop.Storage.Missing(cost);
        string name = type.IsArmor && shape.Id != "cube" ? $"{type.Name} {shape.Name}" : type.Name;
        Hud.Tool = $"[{_slot + 1}] {name.ToUpperInvariant()}  -  " + (Workshop.Rules.Creative ? "FREE" : missing.IsEmpty ? Workshop.Items.Describe(cost).ToUpperInvariant() : "MISSING " + Workshop.Items.Describe(missing).ToUpperInvariant());
        Hud.ToolColour = missing.IsEmpty || Workshop.Rules.Creative ? Kit.Cream : Kit.Danger;

        RayHit hit = RayFrom(PhysicsLayers.Structures, Reach);
        if (hit?.Collider != BaseView)
        {
            _ghost.HideGhosts();
            _ghost.HideOutline();
            return;
        }
        Vector3 local = BaseView.ToLocal(hit.Position);
        Vector3 n = BaseView.GlobalBasis.Inverse() * hit.Normal;
        Int3 remove = BaseView.LocalToCell(local - n * 0.05f);
        var step = new Int3(Mathf.RoundToInt(n.X), Mathf.RoundToInt(n.Y), Mathf.RoundToInt(n.Z));
        Int3 place = remove + step;
        bool ok = Building.Check(s, place, block) == PlaceError.None && (missing.IsEmpty || Workshop.Rules.Creative);
        _aim = (ok, place, remove);
        _ghost.ShowGhosts(BaseView.GlobalTransform, s, new[] { (place, block, ok ? GhostPreview.Valid : GhostPreview.Invalid) });
        _ghost.ShowOutline(BaseView.GlobalTransform, s, remove, new Color(1, 1, 1, 0.7f));
    }

    void SyncCrates()
    {
        if (Workshop.Crates.Version == _cratesSeen) return;
        _cratesSeen = Workshop.Crates.Version;
        var alive = Workshop.Crates.Crates.ToDictionary(c => c.Id);
        foreach (int id in _crates.Keys.Where(id => !alive.ContainsKey(id)).ToList())
        {
            _crates[id].QueueFree();
            _crates.Remove(id);
        }
        foreach (SpillCrate c in alive.Values)
        {
            if (!_crates.TryGetValue(c.Id, out Node3D node))
            {
                node = new StaticBody3D { CollisionLayer = CrateLayer | PhysicsLayers.World, CollisionMask = 0, Position = new Vector3((float)c.X, (float)c.Y, (float)c.Z) };
                node.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.9f, 0.7f, 0.9f) }, Position = new Vector3(0, 0.35f, 0) });
                node.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.7f, 0.9f) },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("f4a100"), Roughness = 0.6f },
                    Position = new Vector3(0, 0.35f, 0),
                });
                node.AddChild(new Label3D { Name = "Label", Position = new Vector3(0, 1.05f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = 0.006f, Font = Kit.Label, OutlineSize = 10 });
                node.SetMeta("crate", c.Id);
                AddChild(node);
                _crates[c.Id] = node;
            }
            node.GetNode<Label3D>("Label").Text = $"SPILL CRATE\n{c.Contents.Contents.Total} ITEMS";
        }
    }

    // ---- Input --------------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        bool pressed = e is InputEventMouseButton { Pressed: true };
        // Mouse buttons and the wheel go to the screen under the cursor, if there is one.
        if (Pointer.HandleInput(e))
        {
            if (pressed) Input.MouseMode = Input.MouseModeEnum.Captured; // a click on a screen also takes the mouse back
            GetViewport().SetInputAsHandled();
            return;
        }
        if (pressed && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            return;
        }
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            return;
        }
        // Keys stay as extras while the cursor is on a screen.
        if (Focus != null && ScreenKeyFor(e) is ScreenKey key)
        {
            string toast = Focus.Page.Press(key);
            if (toast != null) Hud.Toast(toast, IsBad(toast));
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventMouseButton { Pressed: true } click && Pointer.Target == null)
        {
            if (click.ButtonIndex == MouseButton.Left) PlaceAimed();
            else if (click.ButtonIndex == MouseButton.Right) RemoveAimed();
            else if (click.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) _shape = (_shape + (click.ButtonIndex == MouseButton.WheelUp ? 1 : Shapes.Length - 1)) % Shapes.Length;
            return;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.PhysicalKeycode)
        {
            case >= Key.Key1 and <= Key.Key9: _slot = (int)(k.PhysicalKeycode - Key.Key1); break;
            case Key.Q: _shape = (_shape + Shapes.Length - 1) % Shapes.Length; break;
            case Key.E: TryCollectCrate(); break;
            case Key.U: UpgradeAimed(); break;
            case Key.G: Workshop.Gather(); break;
            case Key.F: Workshop.FireGuns(); break;
            case Key.F1: Hud.ShowHelp = !Hud.ShowHelp; break;
            case Key.F2: Workshop.ToggleMode(); break;
        }
    }

    static ScreenKey? ScreenKeyFor(InputEvent e) => e switch
    {
        InputEventKey { Pressed: true, PhysicalKeycode: Key.Up } => ScreenKey.Up,
        InputEventKey { Pressed: true, PhysicalKeycode: Key.Down } => ScreenKey.Down,
        InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Tab } => ScreenKey.Tab,
        InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Enter, ShiftPressed: true } => ScreenKey.ActMany,
        InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Enter } => ScreenKey.Act,
        InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Backspace } => ScreenKey.Cancel,
        InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.R } => ScreenKey.Toggle,
        InputEventKey { Pressed: true, PhysicalKeycode: Key.Bracketleft } => ScreenKey.Less,
        InputEventKey { Pressed: true, PhysicalKeycode: Key.Bracketright } => ScreenKey.More,
        _ => null,
    };

    /// <summary>Places the selected block where the multi-tool points. Public so the probe can drive it.</summary>
    public PlaceResult? PlaceAimed()
    {
        if (_aim is not { } aim) return null;
        Int3 place = aim.place;
        Structure s = Workshop.Base;
        BlockType type = Workshop.Blocks.Type(Palette[_slot]);
        Block block = Building.Make(s, type, Workshop.Blocks.Shape(type.IsArmor ? Shapes[_shape] : "cube"));
        PlaceResult r = Workshop.Placer.Place(s, place, block, Workshop.Storage);
        if (r.Status == PlaceStatus.NotEnoughMaterials) Hud.Toast("MISSING " + Workshop.Items.Describe(r.Missing).ToUpperInvariant(), bad: true);
        BaseView.Sync();
        return r;
    }

    public TakeDownResult? RemoveAimed()
    {
        if (_aim is not { } aim) return null;
        Int3 remove = aim.remove;
        Vector3 at = BaseView.CellToGlobal(remove);
        TakeDownResult r = Workshop.Placer.Remove(Workshop.Base, remove, Workshop.Storage, at.X, at.Y - 1, at.Z + 1.5f);
        if (r.Removal.Status == RemoveStatus.CoreHoldsStructure) Hud.Toast("THE CORE HOLDS THE BASE TOGETHER", bad: true);
        else if (r.Ok && !r.Spilled.IsEmpty) Hud.Toast($"STORAGE FULL: {r.Spilled.Total} PARTS WENT INTO A CRATE", bad: true);
        else if (r.Ok && !r.Refunded.IsEmpty) Hud.Toast("REFUNDED " + Workshop.Items.Describe(r.Refunded).ToUpperInvariant());
        foreach (Structure wreck in r.Removal.Wrecks)
        {
            var view = StructureView.Create(wreck);
            view.Transform = BaseView.Transform;
            AddChild(view);
        }
        BaseView.Sync();
        return r;
    }

    void UpgradeAimed()
    {
        if (_aim is not { } aim) return;
        Int3 remove = aim.remove;
        PlaceResult r = Workshop.Placer.Upgrade(Workshop.Base, remove, Workshop.Storage);
        if (r.Status == PlaceStatus.NotEnoughMaterials) Hud.Toast("UPGRADE NEEDS " + Workshop.Items.Describe(r.Missing).ToUpperInvariant(), bad: true);
        else if (r.Ok) Hud.Toast("UPGRADED" + (r.Paid.IsEmpty ? "" : ": " + Workshop.Items.Describe(r.Paid).ToUpperInvariant()));
        BaseView.Sync();
    }

    bool TryCollectCrate()
    {
        RayHit hit = RayFrom(CrateLayer, 3.5f);
        if (hit?.Collider is not Node n || !n.HasMeta("crate")) return false;
        int id = n.GetMeta("crate").AsInt32();
        SpillCrate crate = Workshop.Crates.Crates.FirstOrDefault(c => c.Id == id);
        if (crate == null) return false;
        Bill moved = Workshop.Crates.Collect(crate, Workshop.Storage);
        Hud.Toast(moved.IsEmpty ? "NO ROOM IN STORAGE" : $"MOVED {moved.Total} ITEMS TO STORAGE", moved.IsEmpty);
        return true;
    }

    /// <summary>For the probe and screenshots: aim the multi-tool at a cell face of the base, then place or remove.</summary>
    public void AimAt(Int3 remove, Int3 place) => _aim = (true, place, remove);
    public void SelectSlot(int slot) => _slot = slot;
}
