using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Material = Godot.Material;

namespace PoiGenerator;

/// <summary>
/// One place, built from a POI instance: chunked block meshes, collision for walking, doors that
/// slide open as you come close (and collide while shut), lift
/// cars that ride their shafts, ladders to climb, the lights that still work, figures where
/// enemies wait, and the terminals, caches, core and hidden walls you can interact with.
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

    public record Interactable(Vector3 Position, string Title, string Text, Action Use = null);
    public readonly List<Interactable> Interactables = new();

    readonly List<Door> _doors = new();
    readonly List<LiftCar> _lifts = new();
    readonly List<LampSpot> _spots = new();
    readonly List<OmniLight3D> _pool = new();
    readonly List<FlickerLight> _lights = new();
    BlockMaterials _materials;
    float _glowBase;
    Node3D _blocks;
    Options _opt;
    Biome _biome;
    Func<float, float, Color> _groundAt;
    Vector2I _offset;

    sealed class Door
    {
        public AnimatableBody3D Body;
        public Vector3 Closed;
        public float Height, Open, Stuck; // Stuck: how far a jammed door hangs open
        public bool Jammed;
        public int Lift = -1, Stop = -1;   // a lift's shaft door at one of its stops
        public Vector3 Center;
    }

    sealed class LiftCar
    {
        public LiftShaft Shaft;
        public AnimatableBody3D Car;
        public float Y;                    // the car floor's walk level now
        public int Stop, Target;
        public Vector3 Center;
    }

    sealed class LampSpot
    {
        public LightSpot Spot;
        public float Phase, Speed;
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
        var view = new PoiView { Poi = poi, Walkable = opt.Collision, Name = poi.Family.Id, _opt = opt, _biome = biome, _groundAt = groundAt, _offset = offset };
        view.BuildBlocks();
        if (MeshBuild.Water(poi) is { } water) view._blocks.AddChild(water);
        view.BuildDoors();
        view.BuildLifts();
        view.BuildFittings();
        if (opt.Lights) view.BuildLights();
        if (opt.Lights) view.BuildLitFaces();
        if (opt.Figures) view.BuildFigures();
        view.BuildInteractables();
        view.DressFromDecoLibrary();
        return view;
    }

    // ---- blocks -----------------------------------------------------------------------

    static int FloorTo(int v, int step) => (int)Math.Floor(v / (double)step) * step;

    void BuildBlocks()
    {
        _blocks?.QueueFree();
        _blocks = new Node3D { Name = "Blocks" };
        AddChild(_blocks);
        var g = Poi.Blocks;
        // The plug covers whole ground tiles, so the terrain around it can simply leave them out.
        PlugMin = new Vector2I(FloorTo(g.Min.X, Tile), FloorTo(g.Min.Z, Tile));
        PlugMax = new Vector2I(FloorTo(g.Max.X, Tile) + Tile - 1, FloorTo(g.Max.Z, Tile) + Tile - 1);

        _glowBase = Poi.State switch { PoiState.Active => 2.4f, PoiState.Infested => 2.0f, _ => 1.6f };
        _materials ??= new BlockMaterials(_glowBase);
        bool space = Poi.Extras.InSpace;
        var options = new MeshOptions
        {
            Doors = false,
            Collision = _opt.Collision,
            Detail = _opt.Detail,
            Rock = !space,
            RockMin = new Int3(PlugMin.X, 0, PlugMin.Y),
            RockMax = new Int3(PlugMax.X, 0, PlugMax.Y),
            RockBottom = Math.Min(g.Min.Y, -3),
            Mottle = 0.3f,
            GroundColor = Look.Ground(_biome),
            GroundColorAt = _groundAt == null ? null : (x, z) => Look.ToRgb(Terrain.Shaded(_groundAt(x + _offset.X, z + _offset.Y), x + _offset.X, z + _offset.Y)),
            RockColor = Look.Rock(_biome),
        };
        StaticBody3D body = null;
        if (_opt.Collision)
        {
            body = new StaticBody3D { Name = "Collision", CollisionLayer = 1, CollisionMask = 0 };
            _blocks.AddChild(body);
        }
        foreach (var chunk in Mesher.Build(g, Poi.Layout.Occupied, options))
        {
            _blocks.AddChild(new MeshInstance3D { Mesh = MeshBuild.ToMesh(chunk, _materials.BySurface), Name = $"Chunk {chunk.Chunk}" });
            if (body != null && chunk.Collision.Count > 0)
                body.AddChild(new CollisionShape3D { Shape = MeshBuild.ToShape(chunk) });
        }
    }

    // ---- doors ------------------------------------------------------------------------

    void BuildDoors()
    {
        var layout = Poi.Layout;
        var liftDoor = new Dictionary<int, (int lift, int stop)>();
        for (int li = 0; li < layout.Extras.Lifts.Count; li++)
            for (int s = 0; s < layout.Extras.Lifts[li].StopDoors.Count; s++) liftDoor[layout.Extras.Lifts[li].StopDoors[s]] = (li, s);
        var groups = layout.Doorways.Select((d, i) => (d.Cells, d.Facing, i)).ToList();
        groups.Add((layout.ExitCells, layout.EntranceFacing, -1));
        foreach (var (cells, facing, index) in groups)
        {
            if (cells.Count == 0) continue;
            var blocks = cells.Select(c => Poi.Blocks.Get(c)).ToList();
            if (blocks.All(b => b.Type != BlockType.Door)) continue; // blown out of a ruin
            var b0 = blocks.First(b => b.Type == BlockType.Door);
            bool lift = index >= 0 && liftDoor.ContainsKey(index);
            if (b0.Variant == DoorState.Open && !lift) continue; // standing open: nothing to draw
            Int3 min = cells.Aggregate(Int3.Min), max = cells.Aggregate(Int3.Max);
            var lo = new Vector3(min.X, min.Y, min.Z);
            var hi = new Vector3(max.X + 1, max.Y + 1, max.Z + 1);
            bool alongX = facing is Dir.North or Dir.South;
            bool blast = b0.Variant == DoorState.Blast;
            float thick = blast ? 0.5f : 0.22f;
            var size = new Vector3(alongX ? hi.X - lo.X : thick, hi.Y - lo.Y, alongX ? thick : hi.Z - lo.Z);
            var center = (lo + hi) / 2f;

            var body = new AnimatableBody3D { Name = "Door", CollisionLayer = 1, CollisionMask = 0, SyncToPhysics = true };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            var color = Look.ToColor(b0.Color);
            body.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.5f, Metallic = 0.4f },
            });
            // A hazard band across the middle (not on a house's plain timber door), and a dark seam down it.
            if (b0.Material != PoiGenerator.Gen.Material.Timber)
            body.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(alongX ? size.X : thick + 0.04f, blast ? 0.5f : 0.22f, alongX ? thick + 0.04f : size.Z) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("e0a800"), Roughness = 0.6f },
                Position = new Vector3(0, -0.4f, 0),
            });
            body.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(alongX ? 0.06f : thick + 0.03f, size.Y, alongX ? thick + 0.03f : 0.06f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color.Darkened(0.6f) },
            });
            AddChild(body);
            body.Position = center;
            var door = new Door
            {
                Body = body, Closed = center, Height = size.Y, Center = center,
                Jammed = b0.Variant == DoorState.Jammed,
                Stuck = b0.Variant == DoorState.Jammed ? 0.72f : 0f,
            };
            if (lift) (door.Lift, door.Stop) = liftDoor[index];
            door.Open = door.Stuck;
            _doors.Add(door);
        }
    }

    // ---- lifts ------------------------------------------------------------------------

    void BuildLifts()
    {
        foreach (var shaft in Poi.Extras.Lifts)
        {
            Int3 min = shaft.Footprint.Aggregate(Int3.Min), max = shaft.Footprint.Aggregate(Int3.Max);
            var size = new Vector3(max.X - min.X + 1, 0.2f, max.Z - min.Z + 1);
            var car = new AnimatableBody3D { Name = "Lift", CollisionLayer = 1, CollisionMask = 0, SyncToPhysics = true };
            car.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            car.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("5a5e64"), Metallic = 0.5f, Roughness = 0.5f } });
            car.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(size.X, 0.08f, size.Z) }, Position = new Vector3(0, 2.8f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("3a3e44") } });
            car.AddChild(new OmniLight3D { LightColor = new Color("ffe0b0"), LightEnergy = Poi.State == PoiState.Active ? 1.2f : 0.5f, OmniRange = 4f, Position = new Vector3(0, 2.4f, 0) });
            AddChild(car);
            var center = new Vector3((min.X + max.X + 1) / 2f, 0, (min.Z + max.Z + 1) / 2f);
            // The car waits at the top stop (the way down starts there).
            int start = shaft.StopWalkY.Count - 1;
            var l = new LiftCar { Shaft = shaft, Car = car, Stop = start, Target = start, Y = shaft.StopWalkY[start], Center = center };
            car.Position = center + Vector3.Up * (l.Y - 0.1f);
            _lifts.Add(l);
        }
    }

    /// <summary>Sends a lift to one of its stops. The car travels at walking pace.</summary>
    public void RequestLift(int lift, int stop)
    {
        if (lift < 0 || lift >= _lifts.Count) return;
        _lifts[lift].Target = Math.Clamp(stop, 0, _lifts[lift].Shaft.StopWalkY.Count - 1);
    }

    /// <summary>The lift (and stop) whose shaft column holds this local cell, if any.</summary>
    public (int lift, int stop)? LiftAt(Int3 cell)
    {
        for (int i = 0; i < _lifts.Count; i++)
        {
            var s = _lifts[i].Shaft;
            if (!s.Footprint.Any(f => f.X == cell.X && f.Z == cell.Z)) continue;
            int stop = s.StopWalkY.IndexOf(cell.Y);
            return (i, stop);
        }
        return null;
    }

    public bool LiftArrived(int lift, int stop) =>
        lift >= 0 && lift < _lifts.Count && stop >= 0 && _lifts[lift].Stop == stop && Mathf.Abs(_lifts[lift].Y - _lifts[lift].Shaft.StopWalkY[stop]) < 0.01f;

    /// <summary>Standing on a car: send it to the next stop down, or back to the top from the bottom.</summary>
    public bool RideLift(Vector3 global)
    {
        Vector3 p = ToLocal(global);
        for (int i = 0; i < _lifts.Count; i++)
        {
            var l = _lifts[i];
            Vector3 d = p - (l.Center + Vector3.Up * l.Y);
            if (Mathf.Abs(d.X) > 1.2f || Mathf.Abs(d.Z) > 1.2f || Mathf.Abs(d.Y) > 1.2f) continue;
            int next = l.Stop - 1 < 0 ? l.Shaft.StopWalkY.Count - 1 : l.Stop - 1;
            RequestLift(i, next);
            return true;
        }
        return false;
    }

    // ---- lights -----------------------------------------------------------------------

    void BuildLights()
    {
        var rng = new RandomNumberGenerator { Seed = Poi.SpawnSeed };
        if (Poi.Lights.Count > 0)
        {
            // Architect places: the lights that survived the state and the depth. A small pool of
            // real lights follows the visitor; the glowing blocks carry the rest.
            foreach (var s in Poi.Lights) _spots.Add(new LampSpot { Spot = s, Phase = rng.RandfRange(0f, 100f), Speed = rng.RandfRange(0.7f, 1.4f) });
            for (int i = 0; i < 28; i++)
            {
                var l = new OmniLight3D { ShadowEnabled = false, OmniAttenuation = 1.4f, Visible = false };
                AddChild(l);
                _pool.Add(l);
            }
            AssignPool(force: true);
            return;
        }
        var layout = Poi.Layout;
        var light = Look.ToColor(Poi.Kit.Light);
        switch (Poi.State)
        {
            case PoiState.Active:
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
                AddLight(DoorCenter(layout.VaultModule) + Vector3.Up * 0.6f, new Color("ff2a1a"), 1.4f, 9f, 2, rng);
                break;
            case PoiState.Infested:
                var spots = new List<Int3>();
                foreach (Int3 gc in Poi.Growths.Where(c => layout.Occupied[c]).OrderBy(_ => rng.Randf()))
                    if (spots.All(s => s.Manhattan(gc) > 7)) spots.Add(gc);
                foreach (var (s, i) in spots.Take(10).Select((s, i) => (s, i)))
                    AddLight(new Vector3(s.X + 0.5f, s.Y + 1.2f, s.Z + 0.5f), i % 3 == 2 ? new Color("e0408a") : new Color("b8f060"), 1.5f, 8f, 1, rng);
                break;
        }
        if (!Poi.HasCore) return;
        var core = Poi.Blocks.Get(Poi.CoreCell);
        float coreEnergy = core.Variant == 1 ? 0.35f : 1.4f;
        AddLight(new Vector3(Poi.CoreCell.X + 0.5f, Poi.CoreCell.Y + 1.6f, Poi.CoreCell.Z + 0.5f), new Color("3de8ff"), coreEnergy, 6f,
            Poi.State == PoiState.Active ? 0 : 1, rng);
    }

    int _poolTimer;
    readonly Dictionary<OmniLight3D, int> _poolSpot = new();

    /// <summary>Gives the pool's lights to the spots nearest the visitor (or the whole place from afar).</summary>
    void AssignPool(bool force = false)
    {
        if (!force && --_poolTimer > 0) return;
        _poolTimer = 8;
        Vector3 at = Watcher != null && IsInstanceValid(Watcher) ? ToLocal(Watcher.GlobalPosition) : ModuleBounds().GetCenter();
        var nearest = _spots.Select((s, i) => (s, i)).OrderBy(t => SpotPos(t.s.Spot).DistanceSquaredTo(at)).Take(_pool.Count).ToList();
        for (int i = 0; i < _pool.Count; i++)
        {
            var l = _pool[i];
            if (i >= nearest.Count) { l.Visible = false; _poolSpot[l] = -1; continue; }
            var (s, index) = nearest[i];
            l.Visible = true;
            l.Position = SpotPos(s.Spot);
            l.LightColor = Look.ToColor(s.Spot.Color);
            l.OmniRange = s.Spot.Range;
            _poolSpot[l] = index;
        }
    }

    static Vector3 SpotPos(LightSpot s) => new(s.Cell.X + 0.5f, s.Cell.Y + 0.4f, s.Cell.Z + 0.5f);

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
        Interactables.Clear();
        foreach (var t in Poi.Terminals)
            Interactables.Add(new Interactable(new Vector3(t.Cell.X + 0.5f, t.Cell.Y + 1.1f, t.Cell.Z + 0.5f), "TERMINAL", t.Text));
        string core = Poi.State == PoiState.Active
            ? "BIG CORE // ONLINE\n\nThis core runs the base. Hijack it (hold E, in the full game) and the base is yours.\nOr destroy it and place your own."
            : "BIG CORE // DORMANT\n\nThe core still holds a charge. Restart it to claim the base,\nor tear it out and place your own.";
        if (Poi.HasCore) Interactables.Add(new Interactable(new Vector3(Poi.CoreCell.X + 0.5f, Poi.CoreCell.Y + 1f, Poi.CoreCell.Z + 0.5f), "CORE", core));
        foreach (var c in Poi.Caches)
        {
            string items = c.Items.Count == 0 ? "Empty. Someone got here first." : string.Join("\n", c.Items.Select(i => $"  {i.Name}  ({i.Rarity.ToString().ToLowerInvariant()})"));
            string title = c.Secret ? "HIDDEN STASH" : c.InVault ? "VAULT CACHE" : "CACHE";
            Interactables.Add(new Interactable(new Vector3(c.Cell.X + 0.5f, c.Cell.Y + 0.6f, c.Cell.Z + 0.5f),
                title, $"{items}\n\nWorth {c.Value} cr  ·  tier {c.Tier}" + (c.PickedOver ? "  (picked over)" : "")));
        }
        foreach (var s in Poi.Story)
            Interactables.Add(new Interactable(new Vector3(s.Cell.X + 0.5f, s.Cell.Y + 0.5f, s.Cell.Z + 0.5f), s.Kind.ToUpperInvariant(), s.Note));
        foreach (var s in Poi.Extras.Secrets)
        {
            var secret = s;
            Int3 w = s.WallCells[0];
            Interactables.Add(new Interactable(new Vector3(w.X + 0.5f, w.Y + 1f, w.Z + 0.5f), "CRACKED WALL",
                "This panel doesn't match the rest. Cracks, and a draft through them.\n\nYou break it open (in the full game: shoot it, dig it, or set a charge).",
                () => Break(secret)));
        }
    }

    /// <summary>Breaks a hidden room's wall: its blocks go, the meshes are rebuilt, the room behind opens.</summary>
    public void Break(SecretRoom s)
    {
        foreach (Int3 c in s.WallCells) Poi.Blocks.Set(c, Block.Air);
        BuildBlocks();
        Int3 w = s.WallCells[0];
        var at = new Vector3(w.X + 0.5f, w.Y + 1f, w.Z + 0.5f);
        Interactables.RemoveAll(i => i.Title == "CRACKED WALL" && i.Position == at);
    }

    /// <summary>The nearest thing to use within reach, roughly where you are looking.</summary>
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

    // ---- what the visitor is standing in ------------------------------------------------

    public Int3 CellAt(Vector3 global)
    {
        Vector3 p = ToLocal(global);
        return new Int3(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z));
    }

    /// <summary>A ladder in the body's cell or the one above it: the body can climb.</summary>
    public bool LadderAt(Vector3 global)
    {
        Int3 c = CellAt(global);
        return Poi.Blocks.Get(c).Type == BlockType.Ladder || Poi.Blocks.Get(c + Int3.Up).Type == BlockType.Ladder;
    }

    public LevelInfo LevelAt(Vector3 global) => Decay.LevelAt(Poi.Layout, CellAt(global).Y);

    /// <summary>Inside the place: in its rooms, under a roof.</summary>
    public bool Inside(Vector3 global)
    {
        Int3 c = CellAt(global) + Int3.Up;
        if (!Poi.Layout.Occupied[c]) return false;
        for (int y = c.Y + 1; y <= Poi.Blocks.Max.Y; y++)
            if (!Poi.Blocks.Get(c with { Y = y }).IsAir) return true;
        return false;
    }

    /// <summary>The warning for any hazard you're in or close to.</summary>
    public string HazardAt(Vector3 global)
    {
        Vector3 p = ToLocal(global);
        foreach (var h in Poi.Extras.Hazards)
        {
            float d = new Vector3(h.Center.X + 0.5f - p.X, (h.Center.Y - p.Y) * 0.5f, h.Center.Z + 0.5f - p.Z).Length();
            if (d < h.Radius + 6f) return d < h.Radius ? h.Warning : "Warning ahead: " + h.Warning;
        }
        return null;
    }

    // ---- frame update -----------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3? watcher = Watcher != null && IsInstanceValid(Watcher) ? ToLocal(Watcher.GlobalPosition) : null;
        foreach (var l in _lifts)
        {
            // Called from a stop: the car comes to whoever waits at a shaft door.
            if (watcher is Vector3 w && l.Target == l.Stop)
            {
                bool onCar = Mathf.Abs(w.Y - l.Y) < 1.2f && new Vector2(w.X - l.Center.X, w.Z - l.Center.Z).Length() < 1.6f;
                for (int s = 0; s < l.Shaft.StopWalkY.Count && !onCar; s++)
                {
                    float sy = l.Shaft.StopWalkY[s];
                    bool waiting = Mathf.Abs(w.Y - sy) < 1.2f && new Vector2(w.X - l.Center.X, w.Z - l.Center.Z).Length() < 3.6f;
                    if (waiting && s != l.Stop) l.Target = s;
                }
            }
            float target = l.Shaft.StopWalkY[l.Target];
            l.Y = Mathf.MoveToward(l.Y, target, dt * 2.5f);
            if (Mathf.Abs(l.Y - target) < 0.001f) l.Stop = l.Target;
            l.Car.Position = l.Center + Vector3.Up * (l.Y - 0.1f);
        }
        foreach (var d in _doors)
        {
            bool near = watcher is Vector3 w && new Vector2(w.X - d.Center.X, w.Z - d.Center.Z).Length() < 3.6f && Math.Abs(w.Y - (d.Center.Y - 1.5f)) < 2.5f;
            bool allowed = d.Lift < 0 || LiftArrived(d.Lift, d.Stop);
            float target = d.Jammed ? d.Stuck : near && allowed ? 1f : 0f;
            d.Open = Mathf.MoveToward(d.Open, target, dt * 2.6f);
            d.Body.Position = d.Closed + Vector3.Up * d.Open * (d.Height - 0.15f);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Time += dt;
        float glow = _glowBase;
        foreach (var l in _lights)
        {
            float t = Time * l.Speed + l.Phase;
            l.Light.LightEnergy = l.Energy * Mathf.Max(0f, Wave(l.Mode, t));
        }
        if (_pool.Count > 0)
        {
            AssignPool();
            foreach (var l in _pool)
            {
                if (!l.Visible || !_poolSpot.TryGetValue(l, out int i) || i < 0) continue;
                var s = _spots[i];
                float t = Time * s.Speed + s.Phase;
                int mode = s.Spot.Mode switch
                {
                    LightMode.Flicker => 3, LightMode.Emergency => 4, LightMode.Bloom or LightMode.Pulse => 1, LightMode.Beacon => 2, _ => 0,
                };
                l.LightEnergy = s.Spot.Energy * Mathf.Max(0f, Wave(mode, t));
                if (s.Spot.Style is GalHack.Structures.LightSettings st)
                {
                    l.LightEnergy = s.Spot.Energy * st.Intensity(Time);
                    uint c = st.ColorAt(Time);
                    l.LightColor = new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);
                }
            }
        }
        if (Poi.State == PoiState.Active) glow *= 0.94f + 0.06f * Mathf.Sin(Time * 9.3f) - (Mathf.Sin(Time * 0.83f) > 0.95f ? 0.5f : 0f);
        else if (Poi.State == PoiState.Infested) glow *= 0.75f + 0.25f * Mathf.Sin(Time * 1.1f);
        _materials.Glow.SetShaderParameter("energy", glow);
    }

    static float Wave(int mode, float t) => mode switch
    {
        // Mains power: mostly steady, with brownouts and the odd stutter.
        0 => 0.92f + 0.08f * Mathf.Sin(t * 7.1f) * Mathf.Sin(t * 2.3f) - (Mathf.Sin(t * 0.9f) > 0.93f ? 0.7f * Mathf.Abs(Mathf.Sin(t * 41f)) : 0f),
        1 => 0.6f + 0.4f * Mathf.Sin(t * 1.3f),
        2 => Mathf.Sin(t * 2.2f) > 0.2f ? 1f : 0.05f,
        // A failing tube: on, off, stuttering.
        3 => Mathf.Sin(t * 3.1f) + Mathf.Sin(t * 11.7f) * 0.6f > 0.2f ? 0.9f : 0.04f,
        // A battery emergency lamp: dim and red, slowly breathing.
        _ => 0.65f + 0.2f * Mathf.Sin(t * 0.8f),
    };

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
