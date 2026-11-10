using System.Collections.Generic;
using Godot;

namespace AssetViewer.Providers;

/// <summary>
/// Flat-shaded boxes and quads gathered into one vertex-coloured mesh: street surfaces, kerbs, painted lines, plazas, pads.
/// Everything is built from its centre line and a width, so nothing is a one-off drawing.
/// </summary>
public sealed class Flat
{
    readonly List<Vector3> _v = new(), _n = new();
    readonly List<Color> _c = new();

    public int Count => _v.Count;

    /// <summary>A horizontal rectangle (x0..x1, z0..z1) at height y, facing up.</summary>
    public void Quad(float x0, float z0, float x1, float z1, float y, Color c)
    {
        var a = new Vector3(x0, y, z0); var b = new Vector3(x1, y, z0); var d = new Vector3(x1, y, z1); var e = new Vector3(x0, y, z1);
        Tri(a, b, d, c); Tri(a, d, e, c);
    }

    /// <summary>A horizontal rectangle along the direction (dx, dz) from p: length, width, centred on the line.</summary>
    public void Strip(float px, float pz, float dx, float dz, float length, float width, float y, Color c)
    {
        Vector2 d = new Vector2(dx, dz).Normalized(), n = new Vector2(-d.Y, d.X) * (width * 0.5f);
        Vector2 p = new(px, pz), a = p - n, b = p + n, e = p + d * length + n, f = p + d * length - n;
        Tri(new Vector3(a.X, y, a.Y), new Vector3(b.X, y, b.Y), new Vector3(e.X, y, e.Y), c);
        Tri(new Vector3(a.X, y, a.Y), new Vector3(e.X, y, e.Y), new Vector3(f.X, y, f.Y), c);
    }

    /// <summary>An axis-aligned box from its low corner.</summary>
    public void Box(float x0, float y0, float z0, float x1, float y1, float z1, Color c)
    {
        Quad(x0, z0, x1, z1, y1, c);
        Side(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), y1 - y0, c, Vector3.Forward);
        Side(new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), y1 - y0, c, Vector3.Back);
        Side(new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), y1 - y0, c, Vector3.Right);
        Side(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), y1 - y0, c, Vector3.Left);
    }

    /// <summary>A disc (n sides) at height y.</summary>
    public void Disc(float cx, float cz, float r, float y, Color c, int n = 28)
    {
        for (int i = 0; i < n; i++)
        {
            float a0 = Mathf.Tau * i / n, a1 = Mathf.Tau * (i + 1) / n;
            Tri(new Vector3(cx, y, cz), new Vector3(cx + Mathf.Sin(a1) * r, y, cz + Mathf.Cos(a1) * r), new Vector3(cx + Mathf.Sin(a0) * r, y, cz + Mathf.Cos(a0) * r), c);
        }
    }

    void Side(Vector3 a, Vector3 b, float h, Color c, Vector3 normal)
    {
        var up = new Vector3(0, h, 0);
        Tri(a, a + up, b + up, c, normal);
        Tri(a, b + up, b, c, normal);
    }

    void Tri(Vector3 a, Vector3 b, Vector3 c, Color colour, Vector3? want = null)
    {
        // Godot's front face runs clockwise seen from the front: its outward normal is (c - a) x (b - a).
        Vector3 n = (c - a).Cross(b - a).Normalized();
        if (n.Dot(want ?? Vector3.Up) < 0) { (b, c) = (c, b); n = -n; }
        _v.Add(a); _v.Add(b); _v.Add(c);
        for (int i = 0; i < 3; i++) { _n.Add(n); _c.Add(colour); }
    }

    public MeshInstance3D ToMesh(string name, float roughness = 0.95f, bool translucent = false)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _n.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _c.ToArray();
        var mesh = new ArrayMesh();
        if (_v.Count > 0) mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (mesh.GetSurfaceCount() > 0) mesh.SurfaceSetMaterial(0, new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = roughness,
                Transparency = translucent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled, CullMode = translucent ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back,
            });
        return new MeshInstance3D { Name = name, Mesh = mesh };
    }
}
