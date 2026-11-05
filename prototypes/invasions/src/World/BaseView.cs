using System.Collections.Generic;
using System.Linq;
using Godot;
using Invasions.Combat;
using Invasions.Model;

namespace Invasions.World;

/// <summary>
/// Draws the player base from its <see cref="BaseModel"/> and keeps the picture in step with it:
/// blocks scorch as they take damage and burst into debris when they break, wrecked containers leave
/// spill crates, and losing the core (or the generator) kills the lights, screens and turrets.
/// Every block is chunky and a full grid cell: 2 m square, after the player's own build blocks.
/// </summary>
public partial class BaseView : Node3D
{
    public BaseModel Model;
    public CombatWorld World;
    public Node3D Player;

    public readonly List<Turret> Turrets = new();
    public readonly List<ConsoleScreen> Consoles = new();
    public SirenSystem Sirens { get; private set; }

    readonly Dictionary<int, BlockNode> _blocks = new();
    readonly Dictionary<int, SpillCrateNode> _crates = new();
    readonly List<Light3D> _lights = new();
    readonly List<(Block block, Node3D panel, HitBox box)> _doors = new();
    readonly List<Node3D> _spinners = new();
    CpuParticles3D _coreSmoke;
    double _t;

    sealed class BlockNode
    {
        public Node3D Root;
        public List<(MeshInstance3D mesh, Color color)> Paint = new();
        public int Stage;
    }

    BaseGrid Grid => Model.Grid;
    float WallHeight => (float)Model.Data.WallHeight;

    public override void _Ready()
    {
        BuildPad();
        foreach (Block b in Grid.Blocks) BuildBlock(b);
        BuildLights();

        Sirens = new SirenSystem { Name = "Sirens", Listener = Player };
        AddChild(Sirens);
        foreach (int[] c in Model.Data.Sirens) Sirens.AddBeacon(World.CellCentre(Grid.FromLayout(c[0], c[1])));

        Model.BlockDamaged += OnDamaged;
        Model.BlockDestroyed += OnDestroyed;
        Model.BlockPlaced += OnPlaced;
        Model.CrateSpilled += OnSpilled;
        Model.CrateRecovered += OnRecovered;
        Model.OnlineChanged += OnOnlineChanged;
        ApplyPower();
    }

    public override void _ExitTree()
    {
        Model.BlockDamaged -= OnDamaged;
        Model.BlockDestroyed -= OnDestroyed;
        Model.BlockPlaced -= OnPlaced;
        Model.CrateSpilled -= OnSpilled;
        Model.CrateRecovered -= OnRecovered;
        Model.OnlineChanged -= OnOnlineChanged;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _t += delta;
        bool powered = Model.Powered;
        foreach (Node3D spinner in _spinners)
            if (IsInstanceValid(spinner) && powered) spinner.RotateY(dt * 2.5f);

        // Doors open for the player and nobody else; invaders have to breach them.
        foreach (var (block, panel, box) in _doors)
        {
            if (!block.Alive || !IsInstanceValid(panel)) continue;
            bool open = Player != null && Player.GlobalPosition.DistanceTo(panel.GlobalPosition with { Y = Player.GlobalPosition.Y }) < 3.2f;
            float y = Mathf.MoveToward(panel.Position.Y, open ? WallHeight - 0.2f : 1.2f, 6f * dt);
            panel.Position = panel.Position with { Y = y };
            box.CollisionLayer = y > 2.2f ? 0u : Layers.Blocks;
        }
    }

    public Vector3 SpawnPoint => World.CellCentre(Grid.FromLayout(Model.Data.PlayerSpawn[0], Model.Data.PlayerSpawn[1])) + Vector3.Up * 0.1f;
    public int StorageTotal => Model.Loot.Storage.Total;
    public IEnumerable<SpillCrateNode> CrateNodes => _crates.Values;

    // ---- Building ---------------------------------------------------------------------

    void BuildPad()
    {
        int w = Model.Data.Layout.Max(r => r.Length), d = Model.Data.Layout.Count;
        float cs = (float)Grid.CellSize;
        Vector3 centre = World.BaseCentre;
        Palette.Box(this, centre + new Vector3(0, 0.02f, 0), new Vector3(w * cs + 1.5f, 0.06f, d * cs + 1.5f), Palette.Mat(Palette.Deck, 0.95f));
        Palette.Box(this, centre + new Vector3(0, 0.055f, 0), new Vector3(w * cs + 2.1f, 0.02f, 0.35f), Palette.Mat(Palette.Yellow));

        // Painted keep-clear lines around the core room, and hazard aprons at the doors.
        if (Grid.CoreRoom.Count > 0)
        {
            float minX = Grid.CoreRoom.Min(c => c.X), maxX = Grid.CoreRoom.Max(c => c.X);
            float minZ = Grid.CoreRoom.Min(c => c.Z), maxZ = Grid.CoreRoom.Max(c => c.Z);
            Vector3 a = World.CellCentre(new Cell((int)minX, (int)minZ)), b = World.CellCentre(new Cell((int)maxX, (int)maxZ));
            Vector3 mid = (a + b) / 2;
            Vector3 size = new(b.X - a.X + cs, 0.02f, b.Z - a.Z + cs);
            Palette.Box(this, mid + new Vector3(0, 0.06f, 0), size - new Vector3(0.6f, 0, 0.6f), Palette.Mat(Palette.TealDeep, 0.9f));
        }
        foreach (Block door in Grid.Blocks.Where(b => b.Kind == BlockKind.Door))
        {
            Vector3 p = World.CellCentre(door.Cell);
            bool alongX = IsWallish(door.Cell + new Cell(1, 0)) || IsWallish(door.Cell + new Cell(-1, 0));
            Vector3 apron = alongX ? new Vector3(2.2f, 0.02f, 5.6f) : new Vector3(5.6f, 0.02f, 2.2f);
            Palette.Box(this, p + new Vector3(0, 0.065f, 0), apron, Palette.Textured(Palette.Hazard, new Vector3(4f, 4f, 1f)));
        }
    }

    bool IsWallish(Cell c) => Grid.At(c) is { Kind: BlockKind.Wall or BlockKind.Turret or BlockKind.Door };

    /// <summary>True for the outer ring of the layout, so outside walls can wear the base livery.</summary>
    bool IsOuter(Cell c)
    {
        Cell l = Grid.ToLayout(c);
        return l.X == 0 || l.Z == 0 || l.Z == Model.Data.Layout.Count - 1 || l.X == Model.Data.Layout[l.Z].Length - 1;
    }

    void BuildBlock(Block b)
    {
        var node = new BlockNode { Root = new Node3D { Name = $"{b.Kind}{b.Id}", Position = World.CellCentre(b.Cell) } };
        AddChild(node.Root);
        _blocks[b.Id] = node;

        float h = b.Kind switch
        {
            BlockKind.Wall or BlockKind.Door => WallHeight,
            BlockKind.Turret => Turret.TowerHeight,
            BlockKind.Container => 1.8f,
            BlockKind.Generator => 2.1f,
            BlockKind.Core => 2.8f,
            _ => 2.4f,
        };
        HitBox box = HitBox.Create(new BoxShape3D { Size = new Vector3(2f, h, 2f) }, new Vector3(0, h / 2, 0),
            Layers.Blocks, Team.Defenders, () => b.Alive, (amount, from, by) => Model.Damage(b, amount));
        box.Name = "Body";
        box.AimOffset = Vector3.Zero;
        node.Root.AddChild(box);

        switch (b.Kind)
        {
            case BlockKind.Wall: BuildWall(node, b); break;
            case BlockKind.Door: BuildDoor(node, b, box); break;
            case BlockKind.Container: BuildContainer(node, b); break;
            case BlockKind.Generator: BuildGenerator(node); break;
            case BlockKind.Core: BuildCore(node); break;
            case BlockKind.Turret: BuildTurret(node, b); break;
            case BlockKind.Console: BuildConsole(node); break;
        }
    }

    MeshInstance3D Paint(BlockNode node, Vector3 centre, Vector3 size, Color color, float roughness = 0.8f)
    {
        MeshInstance3D mesh = Palette.Box(node.Root, centre, size, Palette.Mat(color, roughness));
        node.Paint.Add((mesh, color));
        return mesh;
    }

    void BuildWall(BlockNode node, Block b)
    {
        bool outer = IsOuter(b.Cell);
        float h = WallHeight;
        // Outer walls in the base livery; the core room is a teal vault so you can read the layout at a glance.
        Color body = outer ? Palette.Cream : Palette.TealDeep;
        Color band = outer ? Palette.Orange : Palette.Yellow;
        Paint(node, new Vector3(0, h / 2, 0), new Vector3(2f, h, 2f), body);
        Paint(node, new Vector3(0, 0.22f, 0), new Vector3(2.04f, 0.44f, 2.04f), Palette.Dark);
        Paint(node, new Vector3(0, h * 0.72f, 0), new Vector3(2.03f, 0.34f, 2.03f), band);
        Paint(node, new Vector3(0, h + 0.06f, 0), new Vector3(2.08f, 0.12f, 2.08f), outer ? Palette.CreamShade : Palette.Teal);

        // Foss-style stencilled numbers on some outer faces.
        Cell l = Grid.ToLayout(b.Cell);
        if (outer && (l.X + l.Z) % 5 == 2)
        {
            Vector3 outward = OutwardNormal(b.Cell);
            var label = new Label3D
            {
                Text = $"{(l.X * 7 + l.Z * 3) % 90 + 10:00}",
                Font = Palette.Font,
                FontSize = 120,
                PixelSize = 0.006f,
                Modulate = Palette.Dark,
                OutlineSize = 0,
                Position = outward * 1.03f + new Vector3(0, 1.25f, 0),
                Rotation = new Vector3(0, Mathf.Atan2(outward.X, outward.Z), 0),
            };
            node.Root.AddChild(label);
        }
    }

    Vector3 OutwardNormal(Cell c)
    {
        Cell l = Grid.ToLayout(c);
        int w = Model.Data.Layout[0].Length, d = Model.Data.Layout.Count;
        if (l.Z == 0) return Vector3.Forward;
        if (l.Z == d - 1) return Vector3.Back;
        if (l.X == 0) return Vector3.Left;
        if (l.X == w - 1) return Vector3.Right;
        return Vector3.Back;
    }

    void BuildDoor(BlockNode node, Block b, HitBox box)
    {
        float h = WallHeight;
        bool alongX = IsWallish(b.Cell + new Cell(1, 0)) || IsWallish(b.Cell + new Cell(-1, 0));
        // Frame posts either side of the opening, a lintel, and a sliding hazard-striped shutter.
        Vector3 side = alongX ? new Vector3(0.85f, 0, 0) : new Vector3(0, 0, 0.85f);
        Vector3 postSize = alongX ? new Vector3(0.3f, h, 2f) : new Vector3(2f, h, 0.3f);
        Paint(node, side + new Vector3(0, h / 2, 0), postSize, Palette.Cream);
        Paint(node, -side + new Vector3(0, h / 2, 0), postSize, Palette.Cream);
        Paint(node, new Vector3(0, h - 0.25f, 0), new Vector3(2.02f, 0.5f, 2.02f), Palette.Dark);
        var panel = new Node3D { Name = "Shutter", Position = new Vector3(0, 1.2f, 0) };
        node.Root.AddChild(panel);
        Vector3 panelSize = alongX ? new Vector3(1.45f, 2.4f, 0.3f) : new Vector3(0.3f, 2.4f, 1.45f);
        Palette.Box(panel, Vector3.Zero, panelSize, Palette.Textured(Palette.Hazard, new Vector3(2f, 3f, 1f)));
        Palette.Box(panel, new Vector3(0, 0.9f, 0), panelSize * new Vector3(1.02f, 0.08f, 1.05f), Palette.Glow(Palette.Acid, 2f));
        _doors.Add((b, panel, box));
    }

    void BuildContainer(BlockNode node, Block b)
    {
        Paint(node, new Vector3(0, 0.82f, 0), new Vector3(1.8f, 1.6f, 1.8f), Palette.Teal, 0.6f);
        Paint(node, new Vector3(0, 1.66f, 0), new Vector3(1.86f, 0.12f, 1.86f), Palette.Orange);
        Paint(node, new Vector3(0, 0.06f, 0), new Vector3(1.86f, 0.12f, 1.86f), Palette.Dark);
        for (int i = -2; i <= 2; i++)
        {
            Paint(node, new Vector3(i * 0.36f, 0.85f, 0.92f), new Vector3(0.08f, 1.4f, 0.06f), Palette.TealDeep);
            Paint(node, new Vector3(i * 0.36f, 0.85f, -0.92f), new Vector3(0.08f, 1.4f, 0.06f), Palette.TealDeep);
        }
        Paint(node, new Vector3(0, 1.05f, 0.95f), new Vector3(0.9f, 0.35f, 0.02f), Palette.Cream);
        var label = new Label3D
        {
            Name = "StorageLabel",
            Font = Palette.Font,
            FontSize = 30,
            PixelSize = 0.004f,
            Modulate = Palette.Dark,
            OutlineSize = 0,
            Position = new Vector3(0, 1.05f, 0.97f),
            Text = "NET",
        };
        node.Root.AddChild(label);
    }

    void BuildGenerator(BlockNode node)
    {
        Paint(node, new Vector3(0, 0.15f, 0), new Vector3(1.9f, 0.3f, 1.9f), Palette.Dark);
        Paint(node, new Vector3(0, 1.0f, 0), new Vector3(1.6f, 1.4f, 1.6f), Palette.Orange, 0.6f);
        Paint(node, new Vector3(0, 1.0f, 0.81f), new Vector3(1.1f, 0.9f, 0.04f), Palette.Dark);
        Paint(node, new Vector3(0.81f, 1.0f, 0), new Vector3(0.04f, 0.9f, 1.1f), Palette.Dark);
        Palette.Cylinder(node.Root, new Vector3(0.55f, 2.0f, -0.55f), 0.16f, 0.9f, Palette.Mat(Palette.Dark), 8);
        Palette.Cylinder(node.Root, new Vector3(-0.55f, 1.9f, -0.55f), 0.16f, 0.7f, Palette.Mat(Palette.Dark), 8);
        var fan = new Node3D { Position = new Vector3(0, 1.78f, 0.2f) };
        node.Root.AddChild(fan);
        Palette.Cylinder(fan, Vector3.Zero, 0.55f, 0.1f, Palette.Mat(Palette.Cream), 16);
        Palette.Box(fan, new Vector3(0, 0.08f, 0), new Vector3(1.0f, 0.05f, 0.16f), Palette.Mat(Palette.Dark));
        Palette.Box(fan, new Vector3(0, 0.08f, 0), new Vector3(0.16f, 0.05f, 1.0f), Palette.Mat(Palette.Dark));
        _spinners.Add(fan);
        Palette.Box(node.Root, new Vector3(0, 1.45f, 0.83f), new Vector3(0.6f, 0.12f, 0.03f), Palette.Glow(Palette.Yellow, 3f));
    }

    void BuildCore(BlockNode node)
    {
        Paint(node, new Vector3(0, 0.15f, 0), new Vector3(1.9f, 0.3f, 1.9f), Palette.Dark);
        Paint(node, new Vector3(0, 2.68f, 0), new Vector3(1.9f, 0.24f, 1.9f), Palette.Dark);
        foreach (float x in new[] { -0.82f, 0.82f })
            foreach (float z in new[] { -0.82f, 0.82f })
                Paint(node, new Vector3(x, 1.4f, z), new Vector3(0.22f, 2.3f, 0.22f), Palette.Cream);
        var crystal = new Node3D { Position = new Vector3(0, 1.42f, 0) };
        node.Root.AddChild(crystal);
        var shard = Palette.Box(crystal, Vector3.Zero, new Vector3(0.62f, 2.0f, 0.62f), Palette.Glow(new Color("4cc9f0"), 3.5f));
        shard.Rotation = new Vector3(0, Mathf.Pi / 4, 0);
        Palette.Box(crystal, new Vector3(0, 0, 0), new Vector3(0.9f, 0.18f, 0.9f), Palette.Mat(Palette.Yellow));
        _spinners.Add(crystal);
        var light = new OmniLight3D { LightColor = new Color("4cc9f0"), OmniRange = 9f, LightEnergy = 2.2f, Position = new Vector3(0, 1.6f, 0) };
        node.Root.AddChild(light);
        _lights.Add(light);
    }

    void BuildTurret(BlockNode node, Block b)
    {
        float h = Turret.TowerHeight;
        Paint(node, new Vector3(0, h / 2, 0), new Vector3(1.9f, h, 1.9f), Palette.Cream);
        Paint(node, new Vector3(0, 0.25f, 0), new Vector3(2.04f, 0.5f, 2.04f), Palette.Dark);
        Paint(node, new Vector3(0, h * 0.55f, 0), new Vector3(1.94f, 0.5f, 1.94f), Palette.Teal);
        Paint(node, new Vector3(0, h * 0.55f + 0.32f, 0), new Vector3(1.95f, 0.1f, 1.95f), Palette.Orange);
        var turret = new Turret { Name = "Turret", World = World, Model = Model, Block = b };
        node.Root.AddChild(turret);
        Turrets.Add(turret);
    }

    void BuildConsole(BlockNode node)
    {
        var console = new ConsoleScreen { Name = "Console", Position = new Vector3(0, 0, 0.1f) };
        node.Root.AddChild(console);
        Consoles.Add(console);
    }

    void BuildLights()
    {
        foreach (int[] c in Model.Data.Lights)
        {
            Vector3 p = World.CellCentre(Grid.FromLayout(c[0], c[1]));
            var light = new OmniLight3D { LightColor = new Color("ffe2b0"), OmniRange = 11f, LightEnergy = 1.3f, Position = p + Vector3.Up * 3.6f };
            AddChild(light);
            _lights.Add(light);
            Palette.Box(this, p + Vector3.Up * 3.7f, new Vector3(0.7f, 0.12f, 0.7f), Palette.Glow(new Color("fff1d6"), 2f)).Name = "Lamp";
        }
    }

    // ---- Reacting to the model --------------------------------------------------------

    void OnDamaged(Block b)
    {
        if (!b.Alive || !_blocks.TryGetValue(b.Id, out BlockNode node)) return;
        int stage = b.Health01 > 0.66 ? 0 : b.Health01 > 0.33 ? 1 : 2;
        if (stage == node.Stage) return;
        node.Stage = stage;
        // Scorch: darken the paint as the block weakens.
        float darken = stage == 1 ? 0.3f : 0.55f;
        foreach (var (mesh, color) in node.Paint)
            if (IsInstanceValid(mesh)) mesh.MaterialOverride = Palette.Mat(color.Darkened(darken), 0.95f);
        Fx.Puff(node.Root.GlobalPosition + Vector3.Up * 0.8f, 0.3f);
    }

    void OnDestroyed(Block b)
    {
        if (!_blocks.Remove(b.Id, out BlockNode node)) return;
        Vector3 at = node.Root.GlobalPosition + Vector3.Up * 1.2f;
        Color color = node.Paint.Count > 0 ? node.Paint[0].color : Palette.Cream;
        Fx.Debris(at, color.Darkened(0.2f), 14, 7f, 0.45f);
        Fx.Puff(at, 0.8f);
        Turrets.RemoveAll(t => !IsInstanceValid(t) || t.Block == b);
        Consoles.RemoveAll(c => !IsInstanceValid(c) || c.GetParent() == node.Root);
        _doors.RemoveAll(d => d.block == b);
        node.Root.QueueFree();

        if (b.Kind == BlockKind.Core)
        {
            Fx.Explosion(at, 4f);
            _coreSmoke = Fx.SmokeColumn(this, World.CellCentre(b.Cell) + Vector3.Up * 0.5f, 1.2f);
        }
        if (b.Kind == BlockKind.Generator) ApplyPower();
    }

    void OnPlaced(Block b)
    {
        BuildBlock(b);
        if (b.Kind == BlockKind.Core && _coreSmoke != null && IsInstanceValid(_coreSmoke))
        {
            _coreSmoke.QueueFree();
            _coreSmoke = null;
        }
        Fx.Flash(World.CellCentre(b.Cell) + Vector3.Up * 2f, new Color("4cc9f0"), 14f, 6f, 0.4f);
    }

    void OnSpilled(SpillCrate crate)
    {
        var node = new SpillCrateNode { Crate = crate, Name = $"SpillCrate{crate.Id}" };
        AddChild(node);
        node.GlobalPosition = new Vector3((float)crate.Position.X, 0f, (float)crate.Position.Z);
        _crates[crate.Id] = node;
        World.RegisterObstacle(node.GlobalPosition, SpillCrateSize, 0f, SpillCrateInflate); // invaders path round it
    }

    void OnRecovered(SpillCrate crate)
    {
        if (!_crates.TryGetValue(crate.Id, out SpillCrateNode node)) return;
        if (crate.Total > 0)
        {
            node.Refresh();
            return;
        }
        _crates.Remove(crate.Id);
        World.UnregisterObstacle(node.GlobalPosition, SpillCrateSize, 0f, SpillCrateInflate);
        node.QueueFree();
    }

    static readonly Vector3 SpillCrateSize = new(1.3f, 1f, 1.3f);

    /// <summary>
    /// A spill crate sits in the middle of its own grid cell, which the planner already keeps a body
    /// clear of. Growing it by an agent's radius as well would close the cells round it too, and with
    /// them the narrow aisles beside the storage, where raiders break containers open.
    /// </summary>
    const float SpillCrateInflate = 0f;

    void OnOnlineChanged(bool online) => ApplyPower();

    /// <summary>Lights, screens and turrets all need power: a core and a working generator.</summary>
    public void ApplyPower()
    {
        bool powered = Model.Powered;
        foreach (Light3D light in _lights)
            if (IsInstanceValid(light)) light.Visible = powered;
        foreach (Node lamp in GetChildren())
            if (lamp is MeshInstance3D { Name: var n } mesh && n.ToString().StartsWith("Lamp"))
                mesh.MaterialOverride = powered ? Palette.Glow(new Color("fff1d6"), 2f) : Palette.Mat(Palette.Dark);
        foreach (ConsoleScreen console in Consoles)
            if (IsInstanceValid(console)) console.SetPowered(powered);
    }

    /// <summary>Every container shows the shared inventory total: it's one network.</summary>
    public void RefreshStorageLabels()
    {
        string text = Model.Powered ? $"STORAGE NET\n{Model.Loot.Storage.Total} ITEMS" : "NET OFFLINE";
        foreach (BlockNode node in _blocks.Values)
            if (node.Root.GetNodeOrNull<Label3D>("StorageLabel") is Label3D label) label.Text = text;
    }

    /// <summary>For tests: the world position of a block's node.</summary>
    public Vector3? BlockPosition(int id) => _blocks.TryGetValue(id, out BlockNode n) ? n.Root.GlobalPosition : null;
}
