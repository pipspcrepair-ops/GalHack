using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Material = Godot.Material;

namespace PoiGenerator;

/// <summary>
/// One place, built from a POI instance: chunked block meshes, collision for walking, lights
/// that suit its state, doors that slide open as you come close, figures where enemies wait,
/// and the terminals and core you can interact with.
///
/// The node's local space is the generator's grid: one unit per cell, cell (x, y, z) spans
/// [x, x+1] and so on. Place the node at a multiple of <see cref="Tile"/> so its rock plug lines
/// up with the ground tiles around it.
/// </summary>
public partial class PoiView : Node3D
{
    public const int Tile = 8;

    public PoiInstance Poi { get; private set; }
    public bool Walkable { get; private set; }
    /// <summary>The ground tiles this place's rock plug replaces, in local cells (inclusive).</summary>
    public Vector2I PlugMin, PlugMax;
    public Node3D Watcher;                 // the player or camera that opens doors
    public float Time;

    public record Interactable(Vector3 Position, string Title, string Text);
    public readonly List<Interactable> Interactables = new();

    readonly List<Door> _doors = new();
    readonly List<FlickerLight> _lights = new();
    BlockMaterials _materials;
    float _glowBase;

    sealed class Door
    {
        public Node3D Panel;
        public Vector3 Closed;
        public float Height, Open, Stuck; // Stuck: how far a jammed door hangs open
        public bool Jammed;
        public Vector3 Center;
    }

    sealed class FlickerLight
    {
        public OmniLight3D Light;
        public float Energy, Phase, Speed;
        public int Mode; // 0 steady flicker (power), 1 slow pulse (Bloom), 2 emergency blink
    }

    public record Options(bool Collision = true, bool Detail = true, bool Lights = true, bool Figures = true);

    /// <param name="groundAt">the terrain's colour at a world position, so the plug matches it</param>
    /// <param name="offset">where the view will sit in the world (a multiple of the tile size)</param>
    public static PoiView Build(PoiInstance poi, Biome biome, Options opt, Func<float, float, Color> groundAt = null, Vector2I offset = default)
    {
        var view = new PoiView { Poi = poi, Walkable = opt.Collision, Name = poi.Family.Id };
        view.BuildBlocks(biome, opt, groundAt, offset);
        view.BuildDoors();
        if (opt.Lights) view.BuildLights();
        if (opt.Figures) view.BuildFigures();
        view.BuildInteractables();
        return view;
    }

    // ---- blocks -----------------------------------------------------------------------

    static int FloorTo(int v, int step) => (int)Math.Floor(v / (double)step) * step;

    void BuildBlocks(Biome biome, Options opt, Func<float, float, Color> groundAt, Vector2I offset)
    {
        var g = Poi.Blocks;
        // The plug covers whole ground tiles, so the terrain around it can simply leave them out.
        PlugMin = new Vector2I(FloorTo(g.Min.X, Tile), FloorTo(g.Min.Z, Tile));
        PlugMax = new Vector2I(FloorTo(g.Max.X, Tile) + Tile - 1, FloorTo(g.Max.Z, Tile) + Tile - 1);

        _glowBase = Poi.State switch { PoiState.Active => 2.4f, PoiState.Infested => 2.0f, _ => 1.6f };
        _materials = new BlockMaterials(_glowBase);
        var options = new MeshOptions
        {
            Doors = false,
            Collision = opt.Collision,
            Detail = opt.Detail,
            RockMin = new Int3(PlugMin.X, 0, PlugMin.Y),
            RockMax = new Int3(PlugMax.X, 0, PlugMax.Y),
            RockBottom = Math.Min(g.Min.Y, -3),
            GroundColor = Look.Ground(biome),
            GroundColorAt = groundAt == null ? null : (x, z) => Look.ToRgb(Terrain.Shaded(groundAt(x + offset.X, z + offset.Y), x + offset.X, z + offset.Y)),
            RockColor = Look.Rock(biome),
        };
        StaticBody3D body = null;
        if (opt.Collision)
        {
            body = new StaticBody3D { Name = "Collision", CollisionLayer = 1, CollisionMask = 0 };
            AddChild(body);
        }
        foreach (var chunk in Mesher.Build(g, Poi.Layout.Occupied, options))
        {
            AddChild(new MeshInstance3D { Mesh = MeshBuild.ToMesh(chunk, _materials.BySurface), Name = $"Chunk {chunk.Chunk}" });
            if (body != null && chunk.Collision.Count > 0)
                body.AddChild(new CollisionShape3D { Shape = MeshBuild.ToShape(chunk) });
        }
    }

    // ---- doors ------------------------------------------------------------------------

    void BuildDoors()
    {
        var groups = Poi.Layout.Doorways.Select(d => (d.Cells, d.Facing)).ToList();
        groups.Add((Poi.Layout.ExitCells, Poi.Layout.EntranceFacing));
        foreach (var (cells, facing) in groups)
        {
            var blocks = cells.Select(c => Poi.Blocks.Get(c)).ToList();
            if (blocks.All(b => b.Type != BlockType.Door)) continue; // blown out of a ruin
            var b0 = blocks.First(b => b.Type == BlockType.Door);
            if (b0.Variant == DoorState.Open) continue; // standing open: nothing to draw
            Int3 min = cells.Aggregate(Int3.Min), max = cells.Aggregate(Int3.Max);
            var lo = new Vector3(min.X, min.Y, min.Z);
            var hi = new Vector3(max.X + 1, max.Y + 1, max.Z + 1);
            bool alongX = facing is Dir.North or Dir.South;
            var size = new Vector3(alongX ? hi.X - lo.X : 0.22f, hi.Y - lo.Y, alongX ? 0.22f : hi.Z - lo.Z);
            var center = (lo + hi) / 2f;

            var panel = new Node3D { Name = "Door" };
            var color = Look.ToColor(b0.Color);
            panel.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.5f, Metallic = 0.4f },
            });
            // A hazard band across the middle, and a dark seam down it.
            panel.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(alongX ? size.X : 0.26f, 0.22f, alongX ? 0.26f : size.Z) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("e0a800"), Roughness = 0.6f },
                Position = new Vector3(0, -0.4f, 0),
            });
            panel.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(alongX ? 0.06f : 0.25f, size.Y, alongX ? 0.25f : 0.06f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color.Darkened(0.6f) },
            });
            AddChild(panel);
            panel.Position = center;
            var door = new Door
            {
                Panel = panel, Closed = center, Height = size.Y, Center = center,
                Jammed = b0.Variant == DoorState.Jammed,
                Stuck = b0.Variant == DoorState.Jammed ? 0.55f : 0f,
            };
            door.Open = door.Stuck;
            _doors.Add(door);
        }
    }

    // ---- lights -----------------------------------------------------------------------

    void BuildLights()
    {
        var rng = new RandomNumberGenerator { Seed = Poi.SpawnSeed };
        var layout = Poi.Layout;
        var light = Look.ToColor(Poi.Kit.Light);

        switch (Poi.State)
        {
            case PoiState.Active:
                // A lamp per room, under its lit panels. The power is never quite steady.
                foreach (var m in layout.Modules)
                {
                    var lit = m.Cells.Where(c => Poi.Blocks.Get(c).Role == Role.Light && Poi.Blocks.Get(c).Material == Gen.Material.Lumen).ToList();
                    Vector3 at = lit.Count > 0
                        ? new Vector3((float)lit.Average(c => c.X) + 0.5f, lit.Min(c => c.Y) - 0.6f, (float)lit.Average(c => c.Z) + 0.5f)
                        : new Vector3((m.Min.X + m.Max.X + 1) / 2f, m.Min.Y + 3f, (m.Min.Z + m.Max.Z + 1) / 2f);
                    float range = Math.Max(m.Shape.W, m.Shape.D) * 0.85f + 2f;
                    AddLight(at, light, 1.5f, range, 0, rng);
                }
                break;
            case PoiState.Abandoned:
                // Only an emergency lamp by the vault, still blinking on its battery.
                AddLight(DoorCenter(layout.VaultModule) + Vector3.Up * 0.6f, new Color("ff2a1a"), 1.4f, 9f, 2, rng);
                break;
            case PoiState.Infested:
                // The Bloom lights itself: sickly lamps among the growths, breathing slowly.
                var spots = new List<Int3>();
                foreach (Int3 gc in Poi.Growths.Where(c => layout.Occupied[c]).OrderBy(_ => rng.Randf()))
                    if (spots.All(s => s.Manhattan(gc) > 7)) spots.Add(gc);
                foreach (var (s, i) in spots.Take(10).Select((s, i) => (s, i)))
                    AddLight(new Vector3(s.X + 0.5f, s.Y + 1.2f, s.Z + 0.5f), i % 3 == 2 ? new Color("e0408a") : new Color("b8f060"), 1.5f, 8f, 1, rng);
                break;
        }

        // The core always gives off a little light; less when dormant.
        var core = Poi.Blocks.Get(Poi.CoreCell);
        float coreEnergy = core.Variant == 1 ? 0.35f : 1.4f;
        AddLight(new Vector3(Poi.CoreCell.X + 0.5f, Poi.CoreCell.Y + 1.6f, Poi.CoreCell.Z + 0.5f), new Color("3de8ff"), coreEnergy, 6f,
            Poi.State == PoiState.Active ? 0 : 1, rng);
    }

    Vector3 DoorCenter(int module)
    {
        var d = Poi.Layout.Doorways.FirstOrDefault(d => d.ModuleB == module);
        if (d == null) return Center(Poi.Layout.Modules[module]);
        Int3 min = d.Cells.Aggregate(Int3.Min), max = d.Cells.Aggregate(Int3.Max);
        return new Vector3((min.X + max.X + 1) / 2f, max.Y + 0.5f, (min.Z + max.Z + 1) / 2f) - Look.ToVector(d.Facing) * 1.2f;
    }

    static Vector3 Center(PlacedModule m) => new((m.Min.X + m.Max.X + 1) / 2f, m.Min.Y + 2f, (m.Min.Z + m.Max.Z + 1) / 2f);

    void AddLight(Vector3 at, Color color, float energy, float range, int mode, RandomNumberGenerator rng)
    {
        var l = new OmniLight3D { Position = at, LightColor = color, LightEnergy = energy, OmniRange = range, OmniAttenuation = 1.4f, ShadowEnabled = false };
        AddChild(l);
        _lights.Add(new FlickerLight { Light = l, Energy = energy, Phase = rng.RandfRange(0f, 100f), Speed = rng.RandfRange(0.7f, 1.4f), Mode = mode });
    }

    // ---- figures ----------------------------------------------------------------------

    void BuildFigures()
    {
        foreach (var e in Poi.Enemies)
        {
            var fig = Figures.Make(e, Poi.Family.Faction, Poi.Family.Roster);
            fig.Position = new Vector3(e.Cell.X + 0.5f, e.Cell.Y, e.Cell.Z + 0.5f);
            fig.RotationDegrees = new Vector3(0, (Hash.Cell(e.Cell) % 360), 0);
            AddChild(fig);
        }
    }

    // ---- interaction ------------------------------------------------------------------

    void BuildInteractables()
    {
        foreach (var t in Poi.Terminals)
            Interactables.Add(new Interactable(new Vector3(t.Cell.X + 0.5f, t.Cell.Y + 1.1f, t.Cell.Z + 0.5f), "TERMINAL", t.Text));
        string core = Poi.State == PoiState.Active
            ? "BASE CORE // ONLINE\n\nThis core runs the base. Hijack it (hold E, in the full game) and the base is yours.\nOr destroy it and place your own."
            : "BASE CORE // DORMANT\n\nThe core still holds a charge. Restart it to claim the base,\nor tear it out and place your own.";
        Interactables.Add(new Interactable(new Vector3(Poi.CoreCell.X + 0.5f, Poi.CoreCell.Y + 1f, Poi.CoreCell.Z + 0.5f), "CORE", core));
        foreach (var c in Poi.Caches)
        {
            string items = c.Items.Count == 0 ? "Empty. Someone got here first." : string.Join("\n", c.Items.Select(i => $"  {i.Name}  ({i.Value} cr)"));
            Interactables.Add(new Interactable(new Vector3(c.Cell.X + 0.5f, c.Cell.Y + 0.6f, c.Cell.Z + 0.5f),
                c.InVault ? "VAULT CACHE" : "CACHE", $"{items}\n\nWorth {c.Value} cr" + (c.PickedOver ? "  (picked over)" : "")));
        }
        foreach (var s in Poi.Story)
            Interactables.Add(new Interactable(new Vector3(s.Cell.X + 0.5f, s.Cell.Y + 0.5f, s.Cell.Z + 0.5f), s.Kind.ToUpperInvariant(), s.Note));
    }

    /// <summary>The nearest thing to read within reach, roughly where you are looking.</summary>
    public Interactable Nearest(Vector3 eyeWorld, Vector3 lookWorld, float reach = 2.6f)
    {
        Vector3 eye = ToLocal(eyeWorld);
        Vector3 look = (GlobalBasis.Inverse() * lookWorld).Normalized();
        Interactable best = null;
        float bestScore = float.MaxValue;
        foreach (var i in Interactables)
        {
            Vector3 d = i.Position - eye;
            float dist = d.Length();
            if (dist > reach) continue;
            float facing = d.Normalized().Dot(look);
            if (facing < 0.35f) continue;
            float score = dist - facing;
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }

    // ---- frame update -----------------------------------------------------------------

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Time += dt;

        Vector3? watcher = Watcher != null && IsInstanceValid(Watcher) ? ToLocal(Watcher.GlobalPosition) : null;
        foreach (var d in _doors)
        {
            bool near = watcher is Vector3 w && new Vector2(w.X - d.Center.X, w.Z - d.Center.Z).Length() < 3.6f && Math.Abs(w.Y - (d.Center.Y - 1f)) < 3f;
            float target = d.Jammed ? d.Stuck : near ? 1f : 0f;
            d.Open = Mathf.MoveToward(d.Open, target, dt * 2.6f);
            d.Panel.Position = d.Closed + Vector3.Up * d.Open * (d.Height - 0.15f);
        }

        float glow = _glowBase;
        foreach (var l in _lights)
        {
            float t = Time * l.Speed + l.Phase;
            float k = l.Mode switch
            {
                // Mains power: mostly steady, with brownouts and the odd stutter.
                0 => 0.92f + 0.08f * Mathf.Sin(t * 7.1f) * Mathf.Sin(t * 2.3f) - (Mathf.Sin(t * 0.9f) > 0.93f ? 0.7f * Mathf.Abs(Mathf.Sin(t * 41f)) : 0f),
                1 => 0.6f + 0.4f * Mathf.Sin(t * 1.3f),
                _ => Mathf.Sin(t * 2.2f) > 0.2f ? 1f : 0.05f,
            };
            l.Light.LightEnergy = l.Energy * Mathf.Max(0f, k);
        }
        if (Poi.State == PoiState.Active) glow *= 0.94f + 0.06f * Mathf.Sin(Time * 9.3f) - (Mathf.Sin(Time * 0.83f) > 0.95f ? 0.5f : 0f);
        else if (Poi.State == PoiState.Infested) glow *= 0.75f + 0.25f * Mathf.Sin(Time * 1.1f);
        _materials.Glow.SetShaderParameter("energy", glow);
    }

    /// <summary>Where to stand to walk in: outside the way in, facing the door.</summary>
    public (Vector3 position, float yaw) EntranceStand()
    {
        var e = Poi.Layout.EntranceCell;
        Vector3 outward = Look.ToVector(Poi.Layout.EntranceFacing);
        Vector3 local = new Vector3(e.X + 1f, e.Y + 0.05f, e.Z + 0.5f) + outward * 1.5f;
        if (Poi.Layout.EntranceFacing is Dir.East or Dir.West) local = new Vector3(e.X + 0.5f, e.Y + 0.05f, e.Z + 1f) + outward * 1.5f;
        float yaw = Mathf.Atan2(outward.X, outward.Z); // looking back in, along -outward
        return (ToGlobal(local), yaw);
    }

    /// <summary>The places' bounds in local space: every module, without the margin.</summary>
    public Aabb ModuleBounds() => Bounds(Poi.Layout.Modules);

    public static Aabb ModuleBoundsOf(PoiInstance poi) => Bounds(poi.Layout.Modules);

    /// <summary>
    /// What you can see from outside: the modules that reach above ground. A mine is a shaft
    /// house over a long buried maze, and a camera should frame the shaft house.
    /// </summary>
    public Aabb SurfaceBounds()
    {
        var surface = Poi.Layout.Modules.Where(m => !m.Underground).ToList();
        return surface.Count > 0 ? Bounds(surface) : ModuleBounds();
    }

    static Aabb Bounds(IEnumerable<PlacedModule> mods)
    {
        Int3 min = mods.Select(m => m.Min).Aggregate(Int3.Min), max = mods.Select(m => m.Max).Aggregate(Int3.Max);
        // Clip at ground level: what's buried doesn't count toward the view.
        int bottom = Math.Max(min.Y, -1);
        return new Aabb(new Vector3(min.X, bottom, min.Z), new Vector3(max.X - min.X + 1, Math.Max(1, max.Y - bottom + 1), max.Z - min.Z + 1));
    }
}
