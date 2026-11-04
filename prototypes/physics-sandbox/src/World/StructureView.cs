using System;
using System.Collections.Generic;
using GalHack.Structures;
using Godot;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace PhysicsSandbox.World;

/// <summary>
/// Draws a building structure from its blocks, one mesh per 16³ chunk, rebuilt only for dirty
/// chunks. Armour faces hidden behind a neighbour are culled; sloped faces and component parts
/// are drawn from the shapes' own polygons. Paint is the block's per-face livery: base colour,
/// accent and pattern (stripes, chevrons, checker, hazard) drawn by the shader, toon-lit.
/// A presentation helper only: it never moves anything.
/// </summary>
public partial class StructureView : Node3D
{
    public Structure Structure { get; private set; }
    readonly Dictionary<Int3, MeshInstance3D> _chunks = new();

    static ShaderMaterial _paint;
    static StandardMaterial3D _glass;

    public void Setup(Structure structure)
    {
        Structure = structure;
        _paint ??= new ShaderMaterial { Shader = new Shader { Code = PaintShader } };
        _glass ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.82f, 0.9f, 0.32f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.05f,
            Metallic = 0.3f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = false,
        };
        RebuildAll();
    }

    public void RebuildAll()
    {
        foreach (MeshInstance3D m in _chunks.Values) m.QueueFree();
        _chunks.Clear();
        foreach (Int3 c in Structure.Chunks.Keys) Rebuild(c);
    }

    public void Rebuild(IEnumerable<Int3> chunks)
    {
        foreach (Int3 c in chunks) Rebuild(c);
    }

    public void Rebuild(Int3 chunk)
    {
        var solid = new Buffers();
        var glass = new Buffers();
        if (Structure.Chunks.TryGetValue(chunk, out Chunk data))
        {
            Int3 origin = Structure.ChunkOrigin(chunk);
            for (int i = 0; i < Chunk.Volume; i++)
            {
                if (data.Cells[i].IsEmpty) continue;
                Int3 p = origin + new Int3(i & Chunk.Mask, (i >> Chunk.Bits) & Chunk.Mask, i >> (2 * Chunk.Bits));
                EmitBlock(solid, glass, p, data.Cells[i]);
            }
        }

        if (!_chunks.TryGetValue(chunk, out MeshInstance3D mi))
        {
            mi = new MeshInstance3D { Name = $"Chunk {chunk}" };
            AddChild(mi);
            _chunks[chunk] = mi;
        }
        var mesh = new ArrayMesh();
        if (solid.Count > 0) solid.AddTo(mesh, _paint);
        if (glass.Count > 0) glass.AddTo(mesh, _glass);
        mi.Mesh = mesh.GetSurfaceCount() > 0 ? mesh : null;
    }

    void EmitBlock(Buffers solid, Buffers glass, Int3 p, in Block b)
    {
        Structure s = Structure;
        BlockType t = s.TypeOf(b);
        Mat3 rot = Orientations.Matrix(b.Rot);
        float cell = s.Grid.CellSize;
        N.Vector3 centre = p.ToVector3();

        if (t.IsArmor)
        {
            foreach (Polygon poly in s.ShapeOf(b).Polygons)
            {
                Dir? world = poly.Boundary is Dir local ? Orientations.ToWorld(b.Rot, local) : null;
                if (world is Dir w && Hidden(p, w)) continue;
                N.Vector3 normal = rot.Apply(poly.Normal);
                Dir slot = world ?? Dirs.Closest(normal);
                if (t.Transparent) glass.Add(poly, rot, centre, cell, normal, Look.Glass);
                else solid.Add(poly, rot, centre, cell, normal, PaintLook(b, slot, t));
            }
            return;
        }

        foreach (Part part in t.Parts)
        {
            if (part.ClosedOnly && b.Open) continue;
            foreach (Polygon poly in part.Polygons)
            {
                if (poly.Boundary is Dir local && Hidden(p, Orientations.ToWorld(b.Rot, local))) continue;
                N.Vector3 normal = rot.Apply(poly.Normal);
                Look look = part.Style switch
                {
                    PartStyle.Body => PaintLook(b, Dirs.Closest(normal), t),
                    PartStyle.Dark => Look.Plain(Palette.Dark),
                    PartStyle.Accent => Look.Plain(Palette.Hex(t.Accent)),
                    PartStyle.Glow => Look.Glowing(Palette.Hex(t.GlowColor)),
                    _ => Look.Glass,
                };
                if (part.Style == PartStyle.Glass) glass.Add(poly, rot, centre, cell, normal, look);
                else solid.Add(poly, rot, centre, cell, normal, look);
            }
        }
    }

    /// <summary>A face is hidden when the neighbour across it fully and opaquely covers it.</summary>
    bool Hidden(Int3 p, Dir face) =>
        Structure.TryGet(p + face.Vec(), out Block n) && Structure.Occludes(n, face.Opposite());

    Look PaintLook(in Block b, Dir face, BlockType t)
    {
        ushort slot = b.Paint[face];
        if (slot == 0) return Look.Plain(Palette.Hex(t.Color));
        Paint paint = Structure.Palette[slot];
        return new Look(Palette.Hex(paint.Base), Palette.Hex(paint.Accent), (int)paint.Pattern, false);
    }

    /// <summary>How a face is drawn: base colour, accent and pattern for the shader, or glowing.</summary>
    readonly record struct Look(Color Base, Color Accent, int Pattern, bool Glow)
    {
        public static Look Plain(Color c) => new(c, c, 0, false);
        public static Look Glowing(Color c) => new(c, c, 0, true);
        public static readonly Look Glass = new(Colors.White, Colors.White, 0, false);
    }

    /// <summary>Vertex lists for one surface. Accent rides in UV and UV2.x; the pattern (and a glow flag) in UV2.y.</summary>
    sealed class Buffers
    {
        readonly List<Vector3> _v = new(), _n = new();
        readonly List<Color> _c = new();
        readonly List<Vector2> _uv = new(), _uv2 = new();
        readonly List<int> _i = new();
        readonly List<Vector3> _points = new();

        public int Count => _i.Count;

        /// <summary>Adds a convex polygon (cell units, counter-clockwise from outside). Godot's front faces are clockwise.</summary>
        public void Add(Polygon poly, Mat3 rot, N.Vector3 centre, float cell, N.Vector3 normal, Look look)
        {
            _points.Clear();
            foreach (N.Vector3 q in poly.Points)
            {
                N.Vector3 w = (centre + rot.Apply(q)) * cell;
                _points.Add(new Vector3(w.X, w.Y, w.Z));
            }
            if (_points.Count < 3) return;
            var n = new Vector3(normal.X, normal.Y, normal.Z);
            Vector3 cross = (_points[1] - _points[0]).Cross(_points[2] - _points[0]);
            bool ccw = cross.Dot(n) >= 0f;
            int start = _v.Count;
            Color baseLinear = look.Base.SrgbToLinear(), accent = look.Accent.SrgbToLinear();
            foreach (Vector3 p in _points)
            {
                _v.Add(p);
                _n.Add(n);
                _c.Add(baseLinear);
                _uv.Add(new Vector2(accent.R, accent.G));
                _uv2.Add(new Vector2(accent.B, look.Pattern + (look.Glow ? 8 : 0)));
            }
            for (int k = 1; k + 1 < _points.Count; k++)
            {
                _i.Add(start);
                _i.Add(start + (ccw ? k + 1 : k));
                _i.Add(start + (ccw ? k : k + 1));
            }
        }

        public void AddTo(ArrayMesh mesh, Material material)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = _n.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = _c.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV2] = _uv2.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = _i.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
    }

    /// <summary>Paperback liveries: flat toon colour, the accent drawn in a pattern across each face, glowing parts lit.</summary>
    const string PaintShader = @"
shader_type spatial;
render_mode diffuse_toon, specular_toon;

varying vec3 local_pos;
varying vec3 local_normal;

void vertex() {
    local_pos = VERTEX;
    local_normal = NORMAL;
}

void fragment() {
    vec3 base = COLOR.rgb;
    vec3 accent = vec3(UV.x, UV.y, UV2.x);
    float code = UV2.y;
    float glow = step(7.5, code);
    float pattern = code - glow * 8.0;
    vec3 an = abs(local_normal);
    vec2 uv = an.y > 0.57 ? local_pos.xz : (an.x > 0.57 ? local_pos.zy : local_pos.xy);
    float a = 0.0;
    if (pattern > 0.5 && pattern < 1.5) a = step(abs(fract(uv.y * 1.6) - 0.5), 0.17);
    else if (pattern > 1.5 && pattern < 2.5) a = step(0.5, fract(uv.x * 1.2 + abs(fract(uv.y * 1.2) - 0.5)));
    else if (pattern > 2.5 && pattern < 3.5) a = mod(floor(uv.x * 2.0) + floor(uv.y * 2.0), 2.0);
    else if (pattern > 3.5) a = step(0.5, fract((uv.x + uv.y) * 1.8));
    vec3 c = mix(base, accent, a);
    ALBEDO = c;
    EMISSION = c * glow * 2.5;
    ROUGHNESS = 0.62;
    METALLIC = 0.08;
}
";
}
