using System;
using System.Collections.Generic;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace Planets.Surface;

/// <summary>
/// The polar caps as you see them from the ground: a towering curtain of churning cloud that
/// follows the cap's wobbling edge all the way round the planet. Storm worlds put it right at the
/// foot of the wall; ice worlds put a blizzard behind their ice cliffs instead. The terrain itself
/// is what stops you (the generator raises the caps far too steeply to climb); the curtain makes
/// the edge of the world read as weather rather than as a missing map.
/// </summary>
public partial class CapWalls : Node3D
{
    const float Spacing = 40f, Bottom = -60f, Top = 1500f;
    const double Billow = 420;     // metres across a cloud billow
    const double TexelSize = 24;   // metres per texel along the wall

    internal WorldMap Map;
    internal ArchetypeLook Look;

    public override void _Ready()
    {
        bool ice = Map.Def.Cap == CapStyle.IceCliff;
        Rgb dark = ice ? Rgb.Hex("b8c8e0") : Look.CapColor * 0.7f;
        Rgb light = ice ? Rgb.Hex("ffffff") : Rgb.Lerp(Look.CapColor, Look.SkyHorizon, 0.55f);
        var material = new ShaderMaterial { Shader = Shaders.Curtain };
        material.SetShaderParameter("dark", Color(dark));
        material.SetShaderParameter("light", Color(light));
        material.SetShaderParameter("fog", Color(Look.SkyHorizon));
        material.SetShaderParameter("noise_tex", CloudTexture());
        material.SetShaderParameter("drift", (float)(4.0 / Map.WidthMeters)); // about 4 m/s along the wall

        float inset = ice ? 420f : 18f; // metres inside the cap edge
        Mesh north = Curtain(x => (float)Map.Model.NorthEdge(x) - inset);
        Mesh south = Curtain(x => (float)Map.Model.SouthEdge(x) + inset);
        // Three copies around the camera's map width, so the curtain never ends at the seam.
        for (int copy = -1; copy <= 1; copy++)
        {
            foreach (Mesh mesh in new[] { north, south })
            {
                AddChild(new MeshInstance3D
                {
                    Mesh = mesh,
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Position = new Vector3((float)(copy * Map.WidthMeters), 0, 0),
                });
            }
        }
    }

    /// <summary>
    /// One texture wrapped once around the whole planet: billows sampled from the generator's own
    /// noise on its cylinder, so the pattern meets itself exactly at the seam. UV.y covers the
    /// curtain's height once; the shader only ever scrolls it sideways.
    /// </summary>
    ImageTexture CloudTexture()
    {
        int width = Math.Clamp((int)(Map.WidthMeters / TexelSize), 256, 2048);
        int height = (int)((Top - Bottom) / TexelSize);
        var noise = new Fractal(Hash.Of(Map.Spec.Seed, 0x5707), Billow, 4, 0.5f);
        var bytes = new byte[width * height];
        for (int j = 0; j < height; j++)
        for (int i = 0; i < width; i++)
        {
            P3 p = Map.Model.Cylinder.At(i * Map.WidthMeters / width, 50000 + j * TexelSize);
            bytes[j * width + i] = Rgb.Byte(0.5f + 0.5f * Math.Clamp(noise.Fbm(p, TexelSize), -1f, 1f));
        }
        Image image = Image.CreateFromData(width, height, false, Image.Format.L8, bytes);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    Mesh Curtain(Func<double, float> edgeZ)
    {
        int segments = (int)(Map.WidthMeters / Spacing);
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        for (int s = 0; s <= segments; s++)
        {
            double x = s * Spacing;
            float z = edgeZ(x % Map.WidthMeters);
            verts.Add(new Vector3((float)x, Bottom, z));
            verts.Add(new Vector3((float)x, Top, z));
            uvs.Add(new Vector2((float)(x / Map.WidthMeters), 0f));
            uvs.Add(new Vector2((float)(x / Map.WidthMeters), 1f));
            if (s == 0) continue;
            int a = (s - 1) * 2;
            indices.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    static Color Color(Rgb c) => new(c.R, c.G, c.B);
}
