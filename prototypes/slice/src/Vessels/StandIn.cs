using System.Collections.Generic;
using GalHack.Structures;
using Godot;
using Structure = GalHack.Structures.Structure;

namespace Slice.Vessels;

/// <summary>
/// A structure far off as one cheap mesh: its cells merged into runs along x, each a box in the colour its top face is
/// painted (or its device's own), one draw call, no shadow. Its lights are a second mesh of bright boxes, a little
/// bigger than the lamps, shown while they are on. It still reads as the ship it stands for: the same blocks, the same coats.
/// </summary>
public partial class StandIn : Node3D
{
    MeshInstance3D _body, _lights;
    int _version = -1;

    /// <summary>Builds (again, if the structure changed) the stand-in's meshes from the structure.</summary>
    public void Build(Structure s)
    {
        if (s.Version == _version) return;
        _version = s.Version;
        float cell = s.Grid.CellSize;
        var solid = new Dictionary<Int3, Color>();
        var lamps = new List<(Int3, Color)>();
        foreach (var (p, b) in s.Cells())
        {
            Int3 anchor = p + b.ToAnchor;
            Block a = b.IsFiller ? s.Get(anchor) : b;
            BlockType t = s.TypeOf(a);
            if (t.IsLight) { if (!a.Off) lamps.Add((p, ChunkColor(s.LightAt(anchor).Color))); continue; }
            uint c = t.IsArmor ? s.Palette[a.Paint.PY].Base : t.Color;
            solid[p] = ChunkColor(c);
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var done = new HashSet<Int3>();
        foreach (var (p, colour) in solid)
        {
            if (done.Contains(p)) continue;
            int x0 = p.X, x1 = p.X;
            while (solid.TryGetValue(new Int3(x0 - 1, p.Y, p.Z), out Color c) && c == colour && !done.Contains(new Int3(x0 - 1, p.Y, p.Z))) x0--;
            while (solid.TryGetValue(new Int3(x1 + 1, p.Y, p.Z), out Color c) && c == colour && !done.Contains(new Int3(x1 + 1, p.Y, p.Z))) x1++;
            for (int x = x0; x <= x1; x++) done.Add(new Int3(x, p.Y, p.Z));
            Box(st, new Vector3((x0 + x1) * 0.5f, p.Y, p.Z) * cell, new Vector3(x1 - x0 + 1, 1, 1) * cell, colour);
        }
        _body ??= Add("Body", new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
        _body.Mesh = st.Commit();
        st.Clear();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (p, colour) in lamps) Box(st, new Vector3(p.X, p.Y, p.Z) * cell, Vector3.One * 1.6f * cell, colour);
        _lights ??= Add("Lights", new StandardMaterial3D { VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
        _lights.Mesh = lamps.Count > 0 ? st.Commit() : null;
    }

    MeshInstance3D Add(string name, Material material)
    {
        var m = new MeshInstance3D { Name = name, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(m);
        return m;
    }

    static Color ChunkColor(uint rgb) => Color.FromHtml(rgb.ToString("x6"));

    static readonly (Vector3 N, Vector3 U, Vector3 V)[] Faces =
    {
        (Vector3.Right, Vector3.Up, Vector3.Back), (Vector3.Left, Vector3.Back, Vector3.Up),
        (Vector3.Up, Vector3.Back, Vector3.Right), (Vector3.Down, Vector3.Right, Vector3.Back),
        (Vector3.Back, Vector3.Right, Vector3.Up), (Vector3.Forward, Vector3.Up, Vector3.Right),
    };

    static void Box(SurfaceTool st, Vector3 centre, Vector3 size, Color colour)
    {
        Vector3 half = size * 0.5f;
        foreach (var (n, u, v) in Faces)
        {
            Vector3 mid = centre + n * half, su = u * half, sv = v * half;
            foreach (Vector3 c in new[] { mid - su + sv, mid + su + sv, mid + su - sv, mid - su + sv, mid + su - sv, mid - su - sv })
            {
                st.SetColor(colour);
                st.SetNormal(n);
                st.AddVertex(c);
            }
        }
    }
}
