using System.Collections.Generic;
using Godot;
using GalHack.Structures;

namespace GalHack.Structures.Presentation;

/// <summary>
/// The hologram of what a click would place: one ghost per mirror copy, coloured by whether it
/// can go there, plus an outline round the block under the crosshair. Ghosts glide to a new
/// cell in a few milliseconds and pop slightly, so the preview feels snappy without jumping.
/// </summary>
public partial class GhostPreview : Node3D
{
    public static readonly Color Valid = new(0.45f, 1f, 0.88f);
    public static readonly Color Invalid = new(1f, 0.28f, 0.22f);
    public static readonly Color Warning = new(1f, 0.74f, 0.22f);

    const float Glide = 38f, PopScale = 0.1f;

    readonly List<Ghost> _ghosts = new();
    readonly List<MeshInstance3D> _blocked = new();
    readonly Dictionary<string, ArrayMesh> _meshes = new();
    readonly Dictionary<Color, ShaderMaterial> _materials = new();
    MeshInstance3D _outline;
    ShaderMaterial _outlineMaterial;

    sealed class Ghost
    {
        public MeshInstance3D Node;
        public Transform3D Target;
        public Int3 Cell;
        public string MeshKey;
        public float Pop;
    }

    public override void _Ready()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off; // moved every frame, not every tick
        _outlineMaterial = BlockMaterials.Ghost(Colors.White, fill: 0f);
        _outline = new MeshInstance3D { Name = "Outline", MaterialOverride = _outlineMaterial, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_outline);
    }

    /// <summary>Shows a ghost for each copy, in the frame of the structure it would join.</summary>
    public void ShowGhosts(Transform3D frame, Structure s, IReadOnlyList<(Int3 cell, Block block, Color tint)> copies)
    {
        while (_ghosts.Count < copies.Count)
        {
            var node = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            AddChild(node);
            _ghosts.Add(new Ghost { Node = node });
        }
        float size = s.Grid.CellSize;
        for (int i = 0; i < _ghosts.Count; i++)
        {
            Ghost g = _ghosts[i];
            if (i >= copies.Count)
            {
                g.Node.Visible = false;
                continue;
            }
            var (cell, block, tint) = copies[i];
            string key = $"{s.Grid.Id}:{block.Type}:{block.Shape}:{block.Rot}:{(int)block.Flags}";
            if (g.MeshKey != key)
            {
                g.Node.Mesh = MeshFor(s, block, key);
                g.MeshKey = key;
                g.Pop = 1f;
            }
            g.Node.MaterialOverride = MaterialFor(tint);
            g.Target = frame * new Transform3D(Basis.Identity, new Vector3(cell.X, cell.Y, cell.Z) * size);
            if (!g.Node.Visible || g.Target.Origin.DistanceTo(g.Node.GlobalTransform.Origin) > size * 6f)
            {
                g.Node.GlobalTransform = g.Target; // appear in place, don't fly in from far away
                g.Pop = 1f;
            }
            else if (g.Cell != cell) g.Pop = 1f;
            g.Cell = cell;
            g.Node.Visible = true;
        }
    }

    public void HideGhosts()
    {
        foreach (Ghost g in _ghosts) g.Node.Visible = false;
        foreach (MeshInstance3D m in _blocked) m.Visible = false;
    }

    /// <summary>A red cell wherever a block spanning several cells is blocked by something already there.</summary>
    public void ShowBlocked(Transform3D frame, Structure s, IReadOnlyList<Int3> cells)
    {
        ArrayMesh cube = OutlineCube(s);
        while (_blocked.Count < cells.Count)
        {
            var node = new MeshInstance3D { MaterialOverride = MaterialFor(Invalid), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            AddChild(node);
            _blocked.Add(node);
        }
        float size = s.Grid.CellSize;
        for (int i = 0; i < _blocked.Count; i++)
        {
            _blocked[i].Visible = i < cells.Count;
            if (i >= cells.Count) continue;
            _blocked[i].Mesh = cube;
            _blocked[i].GlobalTransform = frame * new Transform3D(Basis.Identity.Scaled(Vector3.One * 1.04f), new Vector3(cells[i].X, cells[i].Y, cells[i].Z) * size);
        }
    }

    /// <summary>A wire outline round the block under the crosshair: all of it, when it spans several cells.</summary>
    public void ShowOutline(Transform3D frame, Structure s, Int3 cell, Color color)
    {
        _outline.Mesh = OutlineCube(s);
        _outlineMaterial.SetShaderParameter("tint", color);
        float size = s.Grid.CellSize;
        Vector3 centre = new(cell.X, cell.Y, cell.Z), extent = Vector3.One;
        if (s.TryGetBlock(cell, out Int3 anchor, out Block b))
        {
            System.Numerics.Vector3 c = s.CentreOf(anchor, b);
            Int3 w = Footprint.WorldSize(s.TypeOf(b), b.Rot);
            (centre, extent) = (new Vector3(c.X, c.Y, c.Z), new Vector3(w.X, w.Y, w.Z));
        }
        _outline.GlobalTransform = frame * new Transform3D(Basis.Identity.Scaled(extent * 1.03f), centre * size);
        _outline.Visible = true;
    }

    /// <summary>A cube from the mesher, so its UVs are in block units and the shader can find the edges.</summary>
    ArrayMesh OutlineCube(Structure s)
    {
        string key = $"outline:{s.Grid.Id}";
        if (!_meshes.TryGetValue(key, out ArrayMesh cube))
        {
            BlockType armour = null;
            foreach (BlockType t in s.Catalogue.Types)
                if (t != null && t.IsArmor && !t.Transparent) { armour = t; break; }
            _meshes[key] = cube = ChunkMesher.ToMesh(ChunkMesher.BuildSingle(s, Building.Make(s, armour)));
        }
        return cube;
    }

    public void HideOutline() => _outline.Visible = false;

    public override void _Process(double delta)
    {
        float t = 1f - Mathf.Exp(-Glide * (float)delta);
        foreach (Ghost g in _ghosts)
        {
            if (!g.Node.Visible) continue;
            g.Pop = Mathf.MoveToward(g.Pop, 0f, (float)delta * 9f);
            Vector3 origin = g.Node.GlobalTransform.Origin.Lerp(g.Target.Origin, t);
            float scale = 1f + PopScale * g.Pop * g.Pop;
            g.Node.GlobalTransform = new Transform3D(g.Target.Basis.Scaled(Vector3.One * scale), origin);
        }
    }

    ArrayMesh MeshFor(Structure s, Block block, string key)
    {
        if (_meshes.TryGetValue(key, out ArrayMesh mesh)) return mesh;
        block.Hp = s.MaxHp(block);
        mesh = ChunkMesher.ToMesh(ChunkMesher.BuildSingle(s, block));
        _meshes[key] = mesh;
        return mesh;
    }

    ShaderMaterial MaterialFor(Color tint)
    {
        if (!_materials.TryGetValue(tint, out ShaderMaterial m))
            _materials[tint] = m = BlockMaterials.Ghost(tint);
        return m;
    }
}

/// <summary>Translucent planes showing where the mirror symmetry reflects, one colour per axis.</summary>
public partial class SymmetryPlanes : Node3D
{
    static readonly Color[] AxisColors = { new("ff6b2b"), new("ffd23f"), new("2ee6d6") };
    readonly MeshInstance3D[] _planes = new MeshInstance3D[3];

    public override void _Ready()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        for (int a = 0; a < 3; a++)
        {
            var material = new ShaderMaterial { Shader = _shader ??= new Shader { Code = PlaneCode } };
            material.SetShaderParameter("tint", AxisColors[a]);
            _planes[a] = new MeshInstance3D
            {
                Mesh = new QuadMesh(),
                MaterialOverride = material,
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_planes[a]);
        }
    }

    static Shader _shader;

    /// <summary>Shows the active planes across the structure's bounds (plus a margin).</summary>
    public void Show(Transform3D frame, float cellSize, Symmetry sym, Int3 min, Int3 max)
    {
        for (int a = 0; a < 3; a++)
        {
            MeshInstance3D plane = _planes[a];
            plane.Visible = sym.On(a);
            if (!plane.Visible) continue;
            int u = (a + 1) % 3, v = (a + 2) % 3;
            const int margin = 3;
            float u0 = min[u] - margin - 0.5f, u1 = max[u] + margin + 0.5f, v0 = min[v] - margin - 0.5f, v1 = max[v] + margin + 0.5f;
            var centre = new Vector3();
            centre[a] = sym.Plane2(a) * 0.5f;
            centre[u] = (u0 + u1) * 0.5f;
            centre[v] = (v0 + v1) * 0.5f;
            var eu = new Vector3();
            eu[u] = 1;
            var ev = new Vector3();
            ev[v] = 1;
            var ea = new Vector3();
            ea[a] = 1;
            ((QuadMesh)plane.Mesh).Size = new Vector2(u1 - u0, v1 - v0) * cellSize;
            ((ShaderMaterial)plane.MaterialOverride).SetShaderParameter("cells", new Vector2(u1 - u0, v1 - v0));
            plane.GlobalTransform = frame * new Transform3D(new Basis(eu, ev, ea), centre * cellSize);
        }
    }

    public void HideAll()
    {
        foreach (MeshInstance3D p in _planes) p.Visible = false;
    }

    const string PlaneCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec4 tint : source_color = vec4(1.0);
uniform vec2 cells = vec2(10.0);
void fragment() {
    vec2 c = UV * cells;
    vec2 g = abs(fract(c + 0.5) - 0.5) / fwidth(c);
    float line = 1.0 - clamp(min(g.x, g.y) - 0.5, 0.0, 1.0);
    vec2 e = min(UV, 1.0 - UV) * cells;
    float fade = clamp(min(e.x, e.y) / 2.5, 0.0, 1.0);
    float shimmer = 0.5 + 0.5 * sin(TIME * 2.0 + (c.x + c.y) * 0.4);
    ALBEDO = tint.rgb;
    ALPHA = (0.13 + line * 0.55 + shimmer * 0.04) * fade;
}
";
}
