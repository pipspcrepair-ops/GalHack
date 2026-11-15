using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Surface;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;
using FieldTier = Slice.Game.SliceRules.FieldTier;

namespace Slice.Vessels;

/// <summary>What a shot met on a force field.</summary>
public sealed record FieldHit(Vessel Vessel, ForceField Field);

/// <summary>
/// Force fields at work (design section 12), on every structure: each placed field's strength and
/// regrowth (building's <see cref="ForceField"/> rule, its tier's numbers from slice.json), and the
/// field across every open outer door while the structure is powered. Both hold the air (building's
/// rooms read them) and neither has a collider: people, vessels and objects pass through. Shots and
/// blasts meet a placed field (<see cref="Along"/>, <see cref="Blast"/>). Each is drawn as one sheet
/// with a generated shimmer, blue; shields, a different tech, are purple.
/// </summary>
public partial class ForceFields : Node3D
{
    /// <summary>The zone you are in (the planet's surface or its orbit), and your feet: door fields are drawn near you.</summary>
    public Func<SurfaceZone> ZoneOf;
    public Func<Vector3> Feet;
    /// <summary>The structures' doors: only one with a door open has its outer doors' fields worked out.</summary>
    public BlockDoors Doors;
    SurfaceZone Zone => ZoneOf?.Invoke();

    /// <summary>A force field's colour (shields, later, are purple).</summary>
    public static readonly Color Blue = new(0.3f, 0.66f, 1f);

    sealed class Look
    {
        public MeshInstance3D Mesh;
        public ShaderMaterial Material;
        public float Shown;
        public bool Seen;
    }

    readonly Dictionary<ForceField, Look> _fields = new();
    readonly Dictionary<(Vessel, Int3), Look> _doors = new();
    /// <summary>Each structure's open outer doors holding a field, as last worked out.</summary>
    readonly Dictionary<Vessel, int> _doorFields = new();

    /// <summary>Shots and blasts fields have taken (tests).</summary>
    public static int Hits { get; private set; }

    /// <summary>STAGING: fields' waits and regrowth at this many times play speed (tests), 1 in play.</summary>
    public float TimeScale = 1f;

    /// <summary>Open outer doors whose field is drawn now (tests).</summary>
    public int DoorsShown => System.Linq.Enumerable.Count(_doors.Values, l => IsInstanceValid(l.Mesh) && l.Mesh.Visible);

    public override void _PhysicsProcess(double delta)
    {
        if (Zone?.Planet == null) return;
        float dt = (float)delta * TimeScale;
        Vector3 feet = Feet?.Invoke() ?? Vector3.Zero;
        foreach (Look l in _fields.Values) l.Seen = false;
        foreach (Look l in _doors.Values) l.Seen = false;
        _doorFields.Clear();
        foreach (Vessel v in Zone.Vessels)
        {
            if (!IsInstanceValid(v.Node)) continue;
            Structure s = v.Structure;
            bool powered = PoweredFor(v);
            if (s.DoorFields != powered)
            {
                s.DoorFields = powered;
                s.AirChanged();
            }
            for (int i = s.Fields.Count - 1; i >= 0; i--) Run(v, s.Fields[i], dt, powered);
            int held = 0;
            if (powered && Doors != null && Doors.AnyOpen(v) && v.Body.WorldPosition.ToGodot().DistanceTo(feet) < Reach(v))
                foreach (Int3 door in v.Rooms.FieldDoors) held += ShowDoor(v, door, dt) ? 1 : 0;
            _doorFields[v] = held;
        }
        Drop(_fields);
        Drop(_doors);
    }

    /// <summary>
    /// Is the power its fields draw there? Today: a generator on it with its power on. When the
    /// capacitors come (proto/power), whether they cover <see cref="Draw"/>.
    /// </summary>
    public static bool PoweredFor(Vessel v) => !v.IsWreck && v.Powered;

    /// <summary>MW a structure's fields draw from the capacitors now: every tile of each placed field that holds, and each open outer door's field.</summary>
    public double Draw(Vessel v)
    {
        var rules = v.Zone.Data.Rules.ForceFields;
        double mw = _doorFields.GetValueOrDefault(v) * rules.DoorDraw;
        foreach (ForceField f in v.Structure.Fields) mw += f.Draw(rules.DrawPerTile);
        return mw;
    }

    static float Reach(Vessel v)
    {
        var (min, max) = v.Structure.Bounds();
        return (max - min).ToVector3().Length() * v.Structure.Grid.CellSize * 0.6f + 60f;
    }

    static Catalogue _emitterOf;
    static BlockType _emitter;

    /// <summary>The bare emitter block a placed field's corners get: the catalogue's field emitter under no plating.</summary>
    public static BlockType Emitter(Catalogue c)
    {
        if (_emitterOf != c) (_emitter, _emitterOf) = (System.Linq.Enumerable.First(c.Types, t => t != null && t.IsFieldEmitter && !t.IsPlated), c);
        return _emitter;
    }

    static FieldTier TierOf(Vessel v, ForceField f) => v.Zone.Data.Rules.ForceFields.TierOf(f.Tier) ?? new FieldTier();

    /// <summary>A tick of one placed field: its emitters counted, its strength run, its sheet drawn. With every emitter gone it is lost.</summary>
    void Run(Vessel v, ForceField f, float dt, bool powered)
    {
        Structure s = v.Structure;
        int emitters = Emitters(v, f);
        if (emitters == 0)
        {
            s.Fields.Remove(f);
            if (f.Holding) s.AirChanged();
            return;
        }
        FieldTier tier = TierOf(v, f);
        f.Powered = powered;
        if (f.Tick(dt, emitters == 4, tier.Strength, tier.RegrowDelay, tier.RegrowSeconds)) s.AirChanged();
        Look l = LookFor(_fields, f, v);
        l.Mesh.Visible = f.Holding;
        if (!f.Holding) return;
        l.Mesh.Transform = Frame(s.Grid.CellSize, f.Min, f.Max, f.Axis, out Vector2 size);
        ((QuadMesh)l.Mesh.Mesh).Size = size;
        l.Material.SetShaderParameter("size", size);
        l.Material.SetShaderParameter("strength", f.Strength / tier.Strength);
        l.Material.SetShaderParameter("flash", f.Flash);
        if (l.Mesh.GetChildCount() == 0) Strips(l.Mesh, size, s.Grid.CellSize, Emitter(s.Catalogue));
    }

    static StandardMaterial3D _strip;

    /// <summary>
    /// The emitters' light: in each corner one continuous strip along the whole inner side of its L
    /// (both arms, the faces toward the field), on the sheet so it is lit only while the field holds.
    /// </summary>
    static void Strips(MeshInstance3D sheet, Vector2 size, float cell, BlockType emitter)
    {
        _strip ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Blue };
        // The arms' inner faces stand this far in from the field's outer edge; they run one cell from it.
        float arm = (System.Linq.Enumerable.Max(emitter.Parts, p => MathF.Min(p.Max.X, p.Max.Y)) + 0.5f) * cell;
        float t = 0.01f * cell, depth = 0.04f * cell, run = cell - arm;
        foreach (float su in new[] { -1f, 1f })
        foreach (float sv in new[] { -1f, 1f })
        {
            // The L's inner corner; one strip along u on the arm's inner face, one along v, overlapping at the corner.
            var inner = new Vector3(su * (size.X * 0.5f - arm), sv * (size.Y * 0.5f - arm), 0f);
            foreach (var (box, at) in new[] { (new Vector3(run, t, depth), new Vector3(su * run, sv * t, 0f)), (new Vector3(t, run, depth), new Vector3(su * t, sv * run, 0f)) })
                sheet.AddChild(new MeshInstance3D { Name = "Emitter light", Mesh = new BoxMesh { Size = box }, MaterialOverride = _strip,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = inner - at * 0.5f });
        }
    }

    /// <summary>How many of a field's corners hold an emitter. One put back by hand is turned to face its corner.</summary>
    static int Emitters(Vessel v, ForceField f)
    {
        Structure s = v.Structure;
        int n = 0;
        bool turned = false;
        for (int i = 0; i < 4; i++)
        {
            Int3 c = f.Corner(i);
            if (!s.TryGet(c, out Block b) || !s.TypeOf(b).IsFieldEmitter) continue;
            n++;
            int rot = f.CornerRot(i);
            if (b.Rot == rot) continue;
            b.Rot = (byte)rot;
            s.Set(c, b);
            turned = true;
        }
        if (turned) v.ApplyEdits();
        return n;
    }

    /// <summary>The field across an open outer door: in its leaves' plane, filling the doorway, fading in as the leaves draw back.</summary>
    bool ShowDoor(Vessel v, Int3 anchor, float dt)
    {
        Structure s = v.Structure;
        if (!s.TryGet(anchor, out Block b) || !s.TypeOf(b).IsDoor) return false;
        Look l = LookFor(_doors, (v, anchor), v);
        l.Shown = MathF.Min(1f, l.Shown + dt * 2.5f);
        l.Mesh.Transform = DoorSheet(s, anchor, b, out Vector2 size);
        ((QuadMesh)l.Mesh.Mesh).Size = size;
        l.Material.SetShaderParameter("size", size);
        l.Material.SetShaderParameter("strength", l.Shown);
        l.Mesh.Visible = true;
        return true;
    }

    Look LookFor<TKey>(Dictionary<TKey, Look> looks, TKey key, Vessel v)
    {
        if (!looks.TryGetValue(key, out Look l) || !IsInstanceValid(l.Mesh))
        {
            ShaderMaterial material = Material(Blue);
            var mesh = new MeshInstance3D { Name = "Force field", Mesh = new QuadMesh(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            v.Node.AddChild(mesh);
            looks[key] = l = new Look { Mesh = mesh, Material = material };
        }
        l.Seen = true;
        return l;
    }

    /// <summary>Sheets of fields gone this tick (taken down, lost, a door shut or its power out).</summary>
    static void Drop<TKey>(Dictionary<TKey, Look> looks)
    {
        List<TKey> gone = null;
        foreach (var (key, l) in looks)
            if (!l.Seen || !IsInstanceValid(l.Mesh)) (gone ??= new List<TKey>()).Add(key);
        if (gone == null) return;
        foreach (TKey key in gone)
        {
            if (IsInstanceValid(looks[key].Mesh)) looks[key].Mesh.QueueFree();
            looks.Remove(key);
        }
    }

    // ---- What meets a field ---------------------------------------------------------------------

    /// <summary>Harm to a placed field: it fizzles at zero (its emitters untouched) and the air it held goes at once.</summary>
    public static void Hit(Vessel v, ForceField f, float amount)
    {
        if (!f.Holding) return;
        Hits++;
        if (f.Take(amount, TierOf(v, f).RegrowDelay)) v.Structure.AirChanged();
    }

    /// <summary>An explosion at a world point: each field within its radius takes it, less with distance.</summary>
    public static void Blast(Vessel v, N.Vector3 at, float radius, float amount)
    {
        Structure s = v.Structure;
        if (s.Fields.Count == 0) return;
        float cell = s.Grid.CellSize;
        N.Vector3 local = v.Body.ToLocal(at) / cell;
        foreach (ForceField f in s.Fields.ToArray())
        {
            float d = f.DistanceTo(local) * cell;
            if (d < radius) Hit(v, f, amount * (1f - d / radius));
        }
    }

    /// <summary>The nearest placed field that holds along a ray (zone frame, unit direction) within reach, its distance and the face it was met on.</summary>
    public static FieldHit Along(SurfaceZone zone, Vector3 from, Vector3 dir, float reach, out float distance, out Vector3 normal)
    {
        distance = reach;
        normal = -dir;
        FieldHit best = null;
        foreach (Vessel v in zone.Vessels)
        {
            Structure s = v.Structure;
            if (s.Fields.Count == 0) continue;
            float cell = s.Grid.CellSize;
            N.Vector3 o = v.Body.ToLocal(from.ToNumerics()), d = MathX.Unrotate(v.Body.Rotation, dir.ToNumerics());
            foreach (ForceField f in s.Fields)
            {
                float along = Geometry.Get(d, f.Axis);
                if (!f.Holding || MathF.Abs(along) < 1e-6f) continue;
                float t = (f.Min[f.Axis] * cell - Geometry.Get(o, f.Axis)) / along;
                if (t < 0f || t >= distance) continue;
                N.Vector3 p = (o + d * t) / cell;
                if (MathF.Abs(Geometry.Get(p, f.U) - (f.Min[f.U] + f.Max[f.U]) * 0.5f) > f.Width * 0.5f
                    || MathF.Abs(Geometry.Get(p, f.V) - (f.Min[f.V] + f.Max[f.V]) * 0.5f) > f.Height * 0.5f) continue;
                distance = t;
                best = new FieldHit(v, f);
                normal = MathX.Rotate(v.Body.Rotation, Geometry.Set(N.Vector3.Zero, f.Axis, -MathF.Sign(along))).ToGodot();
            }
        }
        return best;
    }

    // ---- The look -------------------------------------------------------------------------------

    /// <summary>
    /// A sheet across cells <paramref name="min"/> to <paramref name="max"/> (inclusive, structure
    /// frame), through their middle along <paramref name="axis"/>, its edges on the cells' outer
    /// faces, flush with its emitters' outer edges: the quad's frame and size.
    /// </summary>
    public static Transform3D Frame(float cell, Int3 min, Int3 max, int axis, out Vector2 size)
    {
        int u = (axis + 1) % 3, v = (axis + 2) % 3;
        size = new Vector2((max[u] - min[u] + 1) * cell, (max[v] - min[v] + 1) * cell);
        Vector3 Unit(int a) => Geometry.Set(N.Vector3.Zero, a, 1f).ToGodot();
        return new Transform3D(new Basis(Unit(u), Unit(v), Unit(axis)), ((min.ToVector3() + max.ToVector3()) * 0.5f * cell).ToGodot());
    }

    /// <summary>An open door's sheet: exactly the shut leaf's span, so it meets the door's frame and sill on every side.</summary>
    public static Transform3D DoorSheet(Structure s, Int3 anchor, in Block b, out Vector2 size)
    {
        BlockType t = s.TypeOf(b);
        Part leaf = System.Linq.Enumerable.First(t.Parts, p => p.ClosedOnly && p.Style == PartStyle.Body);
        N.Vector3 n = t.Size.ToVector3(), lo = leaf.Min * n, hi = leaf.Max * n, mid = (lo + hi) * 0.5f;
        float cell = s.Grid.CellSize;
        size = new Vector2(hi.X - lo.X, hi.Y - lo.Y) * cell;
        Mat3 m = Orientations.Matrix(b.Rot);
        return new Transform3D(new Basis(m.X.ToVector3().ToGodot(), m.Y.ToVector3().ToGodot(), m.Z.ToVector3().ToGodot()),
            (StructureBridge.BlockCenter(s, anchor, b) + m.Apply(new N.Vector3(mid.X, mid.Y, 0f)) * cell).ToGodot());
    }

    static Shader _shader;

    /// <summary>A field's generated shimmer in a colour: blue for a field (and a valid silhouette), red for one that can't go.</summary>
    public static ShaderMaterial Material(Color tint)
    {
        var m = new ShaderMaterial { Shader = _shader ??= new Shader { Code = Code } };
        m.SetShaderParameter("tint", tint);
        return m;
    }

    // A hexagonal lattice drifting under slow bands of light, bright at its edges, fainter as it weakens, flaring where it is hit.
    const string Code = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec4 tint : source_color = vec4(0.3, 0.66, 1.0, 1.0);
uniform vec2 size = vec2(4.0);
uniform float strength = 1.0;
uniform float flash = 0.0;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}
float hex(vec2 p) {
    const vec2 s = vec2(1.0, 1.7320508);
    vec4 c = floor(vec4(p, p - vec2(0.5, 1.0)) / s.xyxy) + 0.5;
    vec4 h = vec4(p - c.xy * s, p - (c.zw + 0.5) * s);
    vec2 g = abs(dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw);
    return smoothstep(0.40, 0.49, max(dot(g, s * 0.5), g.x));
}
void fragment() {
    vec2 m = UV * size;
    float drift = noise(m * 0.6 + vec2(TIME * 0.15, -TIME * 0.1));
    float lattice = hex(m * 2.4 + vec2(0.0, TIME * 0.05));
    float band = pow(0.5 + 0.5 * sin(m.y * 2.2 - TIME * 1.8 + drift * 5.0), 6.0);
    vec2 e = min(UV, 1.0 - UV) * size;
    float edge = 1.0 - smoothstep(0.0, 0.3, min(e.x, e.y));
    float a = (0.07 + 0.22 * lattice + 0.12 * band + 0.08 * drift) * mix(0.3, 1.0, strength) + 0.45 * edge * strength;
    ALBEDO = tint.rgb * (1.0 + 1.5 * flash);
    ALPHA = clamp(a + flash * 0.5, 0.0, 1.0);
}
";
}
