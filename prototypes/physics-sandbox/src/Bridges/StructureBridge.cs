using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using GalHack.Physics;
using GalHack.Structures;
using ShipHandling.Handling;
using GVector3 = Godot.Vector3;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace PhysicsSandbox.Bridges;

/// <summary>
/// The bridge between building's structure core and the shared physics module: a structure's
/// mass and centre of mass, its per-chunk colliders, the room a body needs, its interior (for
/// boarding), and the handling model's view of its components. Pure translation, no rules.
/// </summary>
public static class StructureBridge
{
    /// <summary>
    /// Loads building's catalogue from where it lives, with the sandbox's vessel parts (gyros,
    /// hover pads, landing legs) appended to its components. Nothing is copied.
    /// </summary>
    public static Catalogue LoadCatalogue(string buildingData, string vesselParts)
    {
        return Catalogue.Load(name =>
        {
            string text = File.ReadAllText(Path.Combine(buildingData, name));
            if (name != Catalogue.ComponentsFile) return text;
            var options = new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            JsonNode root = JsonNode.Parse(text, documentOptions: options);
            JsonNode extra = JsonNode.Parse(File.ReadAllText(vesselParts), documentOptions: options);
            JsonArray list = root["components"].AsArray();
            foreach (JsonNode c in extra["components"].AsArray().ToList())
                list.Add(c.DeepClone());
            return root.ToJsonString();
        });
    }

    public static Structure LoadBlueprint(string path, Catalogue catalogue)
    {
        Structure s = Blueprint.Load(File.ReadAllText(path), catalogue, out List<string> errors);
        if (errors.Count > 0 || s == null)
            throw new InvalidDataException($"{Path.GetFileName(path)}: {string.Join("; ", errors)}");
        s.DirtyChunks.Clear();
        return s;
    }

    public static float CellSize(Structure s) => s.Grid.CellSize;

    /// <summary>A cell's centre in the structure's frame, metres. Cell (0,0,0) is centred on the origin.</summary>
    public static N.Vector3 CellCenter(Structure s, Int3 p) => p.ToVector3() * s.Grid.CellSize;

    /// <summary>The cell a structure-frame point falls in.</summary>
    public static Int3 CellAt(Structure s, N.Vector3 local) => Int3.Round(local / s.Grid.CellSize);

    /// <summary>Total mass (kg) and centre of mass, from every block's own mass at its cell, plus cargo at the containers.</summary>
    public static MassProperties Mass(Structure s)
    {
        var points = new List<(N.Vector3, float)>();
        var containers = new List<N.Vector3>();
        foreach (var (p, b) in s.Blocks())
        {
            points.Add((MassPoint(s, p, b), Units.Kilograms(s.StatsOf(b).Mass)));
            if (s.TypeOf(b).IsContainer) containers.Add(CellCenter(s, p));
        }
        float cargo = Units.Kilograms((float)(s.CargoSu * s.Catalogue.CargoMassPerSu));
        if (cargo > 0f && containers.Count > 0)
            foreach (N.Vector3 c in containers) points.Add((c, cargo / containers.Count));
        return MassProperties.Of(points);
    }

    /// <summary>
    /// Where a block's own mass sits: a full cell's middle, but a fitting (a lamp plate on a wall's face, an open door's frame) is
    /// where its parts are, so a lamp cut off lies as the thin plate it is and its centre of mass is inside it, not a cell's width away.
    /// </summary>
    static N.Vector3 MassPoint(Structure s, Int3 p, Block b)
    {
        BlockType t = s.TypeOf(b);
        N.Vector3 centre = CellCenter(s, p);
        if (t.IsArmor || (s.IsSolid(b) && t.Collide != Collide.Parts) || t.Parts == null || t.Parts.Count == 0) return centre;
        float volume = 0f;
        N.Vector3 sum = N.Vector3.Zero;
        foreach (Part part in t.Parts)
        {
            N.Vector3 size = part.Max - part.Min;
            float v = MathF.Max(1e-6f, MathF.Abs(size.X * size.Y * size.Z));
            sum += (part.Min + part.Max) * 0.5f * v;
            volume += v;
        }
        N.Vector3 local = sum / volume;
        return centre + Orientations.Matrix(b.Rot).Apply(local) * s.Grid.CellSize;
    }

    /// <summary>Colliders for some of a structure's chunks (all of them when <paramref name="chunks"/> is null).</summary>
    public static List<ChunkColliders> Colliders(Structure s, IEnumerable<Int3> chunks = null, bool fittings = false)
    {
        var list = new List<ChunkColliders>();
        foreach (Int3 c in chunks ?? s.Chunks.Keys)
            list.Add(ChunkColliders(s, c, fittings));
        return list;
    }

    /// <summary>
    /// One chunk: full cells (cubes, cell-colliding components, closed doors) merged into boxes;
    /// one convex hull per sloped block; part boxes for components that collide by their parts.
    /// Open doors, ladders and lights don't collide, while they hang on something you walk through: a body that is loose
    /// (a wreck, cut off and free) asks for <paramref name="fittings"/>, and every part of them then collides in its real shape,
    /// so a lamp cut off a wall lies on the ground instead of falling through it.
    /// </summary>
    public static ChunkColliders ChunkColliders(Structure s, Int3 chunk, bool fittings = false)
    {
        var result = new ChunkColliders { Chunk = new ChunkKey(chunk.X, chunk.Y, chunk.Z) };
        float cell = s.Grid.CellSize;
        if (s.Chunks.TryGetValue(chunk, out Chunk data))
        {
            var solid = new bool[StructureCollision.ChunkSize * StructureCollision.ChunkSize * StructureCollision.ChunkSize];
            var merged = new HashSet<Int3>();
            Int3 origin = Structure.ChunkOrigin(chunk);
            for (int i = 0; i < Chunk.Volume; i++)
            {
                Block b = data.Cells[i];
                if (b.IsEmpty) continue;
                var local = new Int3(i & Chunk.Mask, (i >> Chunk.Bits) & Chunk.Mask, i >> (2 * Chunk.Bits));
                Int3 p = origin + local;
                BlockType t = s.TypeOf(b);
                Mat3 rot = Orientations.Matrix(b.Rot);
                if (!s.IsSolid(b))
                {
                    if (!fittings || t.IsArmor || t.Parts == null) continue;
                    foreach (Part part in t.Parts)
                    {
                        if (part.ClosedOnly && b.Open) continue;
                        N.Vector3 a = rot.Apply(part.Min), z = rot.Apply(part.Max);
                        N.Vector3 min = N.Vector3.Min(a, z), max = N.Vector3.Max(a, z);
                        result.Boxes.Add(new BoxCollider((p.ToVector3() + (min + max) * 0.5f) * cell, (max - min) * cell));
                    }
                    continue;
                }
                if (t.IsArmor && !s.ShapeOf(b).IsCube)
                {
                    // Sloped blocks: one convex hull per straight run of the same shape and turn,
                    // along the axis the shape is a prism on (a roof edge of slopes is one hull,
                    // not sixteen). Convex hulls cost far more than boxes to sweep.
                    if (!merged.Add(p)) continue;
                    ShapeDef shape = s.ShapeOf(b);
                    Int3 last = p;
                    if (ExtrusionAxis(shape) is int axis)
                    {
                        Int3 step = rot.Apply(Int3.Zero.With(axis, 1));
                        if (step.X + step.Y + step.Z < 0) step = -step;
                        for (Int3 q = p + step; Structure.ChunkOf(q) == chunk && s.TryGet(q, out Block n) && n.Type == b.Type
                             && n.Shape == b.Shape && n.Rot == b.Rot && !merged.Contains(q); q += step)
                        {
                            merged.Add(q);
                            last = q;
                        }
                    }
                    N.Vector3[] points = shape.HullPoints.Select(q => (p.ToVector3() + rot.Apply(q)) * cell)
                        .Concat(last == p ? Enumerable.Empty<N.Vector3>() : shape.HullPoints.Select(q => (last.ToVector3() + rot.Apply(q)) * cell))
                        .ToArray();
                    result.Hulls.Add(new HullCollider(points));
                }
                else if (!t.IsArmor && t.Collide == Collide.Parts)
                {
                    foreach (Part part in t.Parts)
                    {
                        if (part.ClosedOnly && b.Open) continue;
                        N.Vector3 a = rot.Apply(part.Min), z = rot.Apply(part.Max);
                        N.Vector3 min = N.Vector3.Min(a, z), max = N.Vector3.Max(a, z);
                        result.Boxes.Add(new BoxCollider((p.ToVector3() + (min + max) * 0.5f) * cell, (max - min) * cell));
                    }
                }
                else solid[local.X + 16 * (local.Y + 16 * local.Z)] = true;
            }
            StructureCollision.AddBoxes(result, result.Chunk, solid, cell);
        }
        result.Hash = StructureCollision.Hash(result);
        return result;
    }

    static readonly Dictionary<ShapeDef, int?> Extrusions = new();

    /// <summary>
    /// The block-local axis a shape is a prism along (every point sits on one of the two cell
    /// faces of that axis, with its twin on the other), or null. Runs along it stay convex.
    /// </summary>
    static int? ExtrusionAxis(ShapeDef shape)
    {
        lock (Extrusions)
        {
            if (Extrusions.TryGetValue(shape, out int? known)) return known;
            int? found = null;
            for (int axis = 0; axis < 3 && found == null; axis++)
            {
                bool prism = true;
                foreach (N.Vector3 q in shape.HullPoints)
                {
                    float c = Geometry.Get(q, axis);
                    N.Vector3 twin = Geometry.Set(q, axis, -c);
                    prism &= MathF.Abs(MathF.Abs(c) - 0.5f) < 1e-4f && shape.HullPoints.Any(o => N.Vector3.DistanceSquared(o, twin) < 1e-8f);
                }
                if (prism) found = axis;
            }
            Extrusions[shape] = found;
            return found;
        }
    }

    /// <summary>
    /// The room a structure body needs, for stuck checks: feet at its keel, as tall as it is,
    /// as wide as half its larger horizontal extent.
    /// </summary>
    public static StuckShape ShapeOf(Structure s)
    {
        var (min, max) = s.Bounds();
        float cell = s.Grid.CellSize;
        float bottom = (min.Y - 0.5f) * cell, top = (max.Y + 0.5f) * cell;
        float radius = MathF.Max(max.X - min.X + 1, max.Z - min.Z + 1) * cell * 0.5f;
        return new StuckShape(top - bottom, radius, -bottom);
    }

    /// <summary>A rough drag area: frontal area times a bluff-body drag coefficient.</summary>
    public static float DragArea(Structure s)
    {
        var (min, max) = s.Bounds();
        float cell = s.Grid.CellSize;
        return (max.X - min.X + 1) * (max.Y - min.Y + 1) * cell * cell * 0.8f;
    }

    /// <summary>The shared module's class for a vessel: the handling model's own (hover, small, capital).</summary>
    public static StructureClass ClassOf(StructureVessel vessel) => vessel.Class switch
    {
        VesselClass.Hover => StructureClass.Hover,
        VesselClass.Capital => StructureClass.Capital,
        _ => StructureClass.Small,
    };

    /// <summary>Where the command chair is (structure frame, metres), or the origin if there is none.</summary>
    public static N.Vector3 SeatOf(Structure s)
    {
        foreach (var (p, b) in s.Blocks())
            if (s.TypeOf(b).IsCommandChair) return CellCenter(s, p);
        return N.Vector3.Zero;
    }
}

/// <summary>
/// The handling model's view of a structure: its mass, and its thrusters, gyros, hover pads and
/// landing legs as component data. Thrust per thruster is the building catalogue's (after the
/// grid's scale and the CPU efficiency); torque, hover lift, ride height and leg length come from
/// the handling model's parts table by grid size. Mass is the structure's whole mass, so the
/// handling model's thrust / mass is exactly the build's.
/// </summary>
public sealed class StructureVessel : IVesselStructure
{
    readonly Structure _s;
    readonly Tunables _t;
    readonly CoreMode? _as;
    int _version = -1;
    CoreMode _mode;

    readonly List<ThrusterSpec> _thrusters = new();
    readonly List<GyroSpec> _gyros = new();
    readonly List<HoverEngineSpec> _hover = new();
    readonly List<GearSpec> _gear = new();
    float _mass;
    VesselClass _class;
    GVector3 _cockpit;

    /// <param name="asMode">Reads the structure as if its core were in this mode, to ask what that mode would do; null: its own mode.</param>
    public StructureVessel(Structure structure, Tunables tunables, CoreMode? asMode = null)
    {
        _s = structure;
        _t = tunables;
        _as = asMode;
        Refresh();
    }

    public Structure Structure => _s;
    public string Name => _s.Name;

    public VesselClass Class { get { Refresh(); return _class; } }
    public float HullMass { get { Refresh(); return _mass; } }
    public float CargoMass => 0f;
    public IReadOnlyList<ThrusterSpec> Thrusters { get { Refresh(); return _thrusters; } }
    public IReadOnlyList<GyroSpec> Gyros { get { Refresh(); return _gyros; } }
    public IReadOnlyList<HoverEngineSpec> HoverEngines { get { Refresh(); return _hover; } }
    public IReadOnlyList<GearSpec> LandingGear { get { Refresh(); return _gear; } }
    public IReadOnlyList<HullBlock> Blocks => Array.Empty<HullBlock>();
    public GVector3 Cockpit { get { Refresh(); return _cockpit; } }
    public int Revision => _s.Version;

    /// <summary>Hover plate positions for the shared hover controller: the bottom face of each plate (none outside Hover Mode).</summary>
    public N.Vector3[] HoverPoints => HoverEngines.Select(h => new N.Vector3(h.Position.X, h.Position.Y, h.Position.Z)).ToArray();

    /// <summary>Landing legs for the shared gear, each hanging from the bottom face of its housing. They stay folded in Hover Mode.</summary>
    public GearLeg[] Legs => Class == VesselClass.Hover ? Array.Empty<GearLeg>() : LandingGear.Select(g => new GearLeg(new N.Vector3(g.Position.X, g.Position.Y, g.Position.Z), g.Length)).ToArray();

    void Refresh()
    {
        CoreMode mode = _as ?? _s.Mode;
        if (_version == _s.Version && _mode == mode) return;
        _version = _s.Version;
        _mode = mode;
        _thrusters.Clear();
        _gyros.Clear();
        _hover.Clear();
        _gear.Clear();

        StructureStats stats = StructureStats.Compute(_s);
        _mass = (float)stats.Mass;
        float cell = _s.Grid.CellSize;
        string size = _s.Grid.Size == GridSize.Small ? "small" : "large";
        // Base Mode leaves its thrusters idle; hover plates lift in Hover Mode and do nothing in any other.
        float efficiency = mode != CoreMode.Base && stats.HasCore && stats.Powered ? (float)stats.Efficiency : 0f;
        bool hovering = mode == CoreMode.Hover && _s.Grid.Size == GridSize.Small;
        _cockpit = GVector3.Zero;

        foreach (var (p, b) in _s.Blocks())
        {
            BlockType t = _s.TypeOf(b);
            GVector3 at = new GVector3(p.X, p.Y, p.Z) * cell;
            GVector3 underside = at + GVector3.Down * (cell * 0.5f);
            if (t.IsThruster)
            {
                // A thruster's exhaust leaves through its back (+Z), pushing the structure toward its local -Z.
                N.Vector3 push = Orientations.ToWorld(b.Rot, Dir.NegZ).Normal();
                _thrusters.Add(new ThrusterSpec(size, at, new GVector3(push.X, push.Y, push.Z),
                    t.Thrust * _s.Grid.FunctionScale * efficiency, 0f));
            }
            else if (t.Function == "gyro") _gyros.Add(new GyroSpec(size, at, _t.Parts.Gyros[size].Torque, 0f));
            else if (t.Function == "hover" && hovering)
            {
                var part = _t.Parts.HoverEngines[size];
                _hover.Add(new HoverEngineSpec(size, underside, part.Lift, part.RideHeight, 0f));
            }
            else if (t.Function == "gear") _gear.Add(new GearSpec(size, underside, _t.Parts.LandingGear[size].Length, 0f));
            else if (t.IsCommandChair) _cockpit = at + GVector3.Up * 0.8f;
        }

        // A structure's class is its core and that core's mode, never the blocks on it.
        _class = hovering ? VesselClass.Hover : _s.Grid.Size == GridSize.Small ? VesselClass.Small : VesselClass.Capital;
    }
}

/// <summary>
/// Which air cells are inside a structure: covered above and floored below in the same column,
/// and walled on at least three of the four sides. A doorway counts as inside (it has a wall
/// either side and the cabin behind it), the deck outside does not. Inside, a rider's down is the
/// core's; outside it is on the outer hull.
/// </summary>
public sealed class InteriorMap
{
    readonly Structure _s;
    int _version = -1;
    readonly HashSet<Int3> _inside = new();

    public InteriorMap(Structure structure) => _s = structure;

    public int Count { get { Refresh(); return _inside.Count; } }

    public bool IsInterior(Int3 cell)
    {
        Refresh();
        return _inside.Contains(cell);
    }

    /// <summary>A structure-frame point is in an interior cell (solid blocks count as inside, so walls don't break it).</summary>
    public bool Contains(N.Vector3 local)
    {
        Int3 c = StructureBridge.CellAt(_s, local);
        return IsInterior(c) || _s.TryGet(c, out Block b) && _s.IsSolid(b);
    }

    void Refresh()
    {
        if (_version == _s.Version) return;
        _version = _s.Version;
        _inside.Clear();
        if (_s.Count == 0) return;
        var (min, max) = _s.Bounds();
        for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
                for (int x = min.X; x <= max.X; x++)
                {
                    var p = new Int3(x, y, z);
                    if (_s.Has(p)) continue;
                    if (!Hits(p, Dir.PosY, min, max) || !Hits(p, Dir.NegY, min, max)) continue;
                    int walls = (Hits(p, Dir.PosX, min, max) ? 1 : 0) + (Hits(p, Dir.NegX, min, max) ? 1 : 0)
                        + (Hits(p, Dir.PosZ, min, max) ? 1 : 0) + (Hits(p, Dir.NegZ, min, max) ? 1 : 0);
                    if (walls >= 3) _inside.Add(p);
                }
    }

    /// <summary>Is there a block somewhere along this axis ray within the structure's bounds?</summary>
    bool Hits(Int3 p, Dir d, Int3 lo, Int3 hi)
    {
        Int3 step = d.Vec();
        for (Int3 q = p + step; q.X >= lo.X && q.X <= hi.X && q.Y >= lo.Y && q.Y <= hi.Y && q.Z >= lo.Z && q.Z <= hi.Z; q += step)
            if (_s.Has(q)) return true;
        return false;
    }
}

/// <summary>
/// Keeps bodies out of a structure's blocks (stuck-safety after structure edits): a point is
/// inside if it falls in a solid block's cell. It keeps out everything in the zone's world and
/// its own riders (inside or on its hull: a block placed where someone stands); a rider rescued
/// from its own ship's blocks stays aboard. A structure never rescues itself, and riders of other
/// ships are their ships' business.
/// </summary>
public sealed class StructureField : IStuckField
{
    public readonly Body Body;
    public readonly Structure Structure;

    public StructureField(Body body, Structure structure)
    {
        Body = body;
        Structure = structure;
    }

    public string Name => $"structure {Structure.Name}";

    public Body Owner => Body;

    public bool Applies(Body body) =>
        body.Kind is not (BodyKind.Vessel or BodyKind.Wreck) && (body.Frame == null ? body != Body : body.Frame == Body);

    public bool IsSolid(Body body, N.Vector3 point)
    {
        N.Vector3 local = Body.ToLocal(point);
        return Structure.TryGet(StructureBridge.CellAt(Structure, local), out Block b) && Structure.IsSolid(b);
    }

    /// <summary>Inside: any point on the body's core axis (a radius in from each end) is in a solid block.</summary>
    public bool IsInside(Body body, N.Vector3 feet, StuckShape shape)
    {
        float bottom = shape.Radius, top = MathF.Max(shape.Height - shape.Radius, shape.Radius);
        int steps = Math.Max(1, (int)MathF.Ceiling((top - bottom) / 0.25f));
        for (int i = 0; i <= steps; i++)
            if (IsSolid(body, feet + N.Vector3.UnitY * (bottom + (top - bottom) * i / steps))) return true;
        return false;
    }

    public bool Fits(Body body, N.Vector3 feet, StuckShape shape)
    {
        float ring = shape.Radius + 0.1f;
        for (float y = 0.1f; y <= shape.Height; y += 0.25f)
        {
            N.Vector3 a = feet + N.Vector3.UnitY * y;
            if (IsSolid(body, a) || IsSolid(body, a + new N.Vector3(ring, 0, 0)) || IsSolid(body, a - new N.Vector3(ring, 0, 0))
                || IsSolid(body, a + new N.Vector3(0, 0, ring)) || IsSolid(body, a - new N.Vector3(0, 0, ring)))
                return false;
        }
        return true;
    }

    public bool Supports(Body body, N.Vector3 feet) => IsSolid(body, feet - N.Vector3.UnitY * 0.3f);

    public float TopAt(Body body, float x, float z)
    {
        // Straight down through the structure from well above it.
        var (min, max) = Structure.Bounds();
        float cell = Structure.Grid.CellSize;
        float above = (max.Y + 2) * cell, below = (min.Y - 1) * cell;
        N.Vector3 top = Body.ToParent(new N.Vector3(0, above, 0)), bottom = Body.ToParent(new N.Vector3(0, below, 0));
        for (float y = MathF.Max(top.Y, bottom.Y); y >= MathF.Min(top.Y, bottom.Y); y -= cell * 0.5f)
            if (IsSolid(body, new N.Vector3(x, y, z))) return y + cell * 0.5f;
        return float.NegativeInfinity;
    }

    public bool TryFindOpen(Body body, N.Vector3 feet, StuckShape shape, out N.Vector3 openFeet)
    {
        openFeet = default;
        return false;
    }
}
