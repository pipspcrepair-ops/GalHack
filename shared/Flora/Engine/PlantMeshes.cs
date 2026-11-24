using Godot;

namespace GalHack.Plants;

/// <summary>The one engine seam of the flora module: a plain plant mesh into a Godot mesh.</summary>
public static class PlantMeshes
{
    public static ArrayMesh ToGodot(PlantMesh m)
    {
        int n = m.Positions.Length;
        var vertices = new Godot.Vector3[n];
        var normals = new Godot.Vector3[n];
        var colors = new Color[n];
        for (int i = 0; i < n; i++)
        {
            System.Numerics.Vector3 p = m.Positions[i], q = m.Normals[i];
            System.Numerics.Vector4 c = m.Colors[i];
            vertices[i] = new Godot.Vector3(p.X, p.Y, p.Z);
            normals[i] = new Godot.Vector3(q.X, q.Y, q.Z);
            colors[i] = new Color(c.X, c.Y, c.Z, c.W);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
