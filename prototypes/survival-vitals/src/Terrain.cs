using System;
using System.Collections.Generic;
using Godot;
using SurvivalVitals.Model;

namespace SurvivalVitals;

/// <summary>
/// The planet patch's ground: a 1 m heightfield shaped from planet.json, so every hazard reads
/// at a glance. The frozen valley is a snowy hollow, the vent a black cone with lava in its
/// mouth, the radiation crater a sickly bowl and the airless sinkhole a dark funnel. Pads under
/// the shelter, pod and tent are flattened so structures sit level.
/// </summary>
public sealed class Terrain
{
    public const int Vertices = 281;              // per side, 1 m apart
    public const float Half = (Vertices - 1) / 2f; // 140 m from the centre to each edge

    readonly PlanetData _planet;
    readonly FastNoiseLite _noise = new() { Seed = 1977, Frequency = 0.012f, FractalOctaves = 3 };
    readonly List<(Vector2 at, float radius)> _pads = new();

    public Terrain(PlanetData planet)
    {
        _planet = planet;
        Volume shelter = planet.Volumes[0];
        _pads.Add((new Vector2((float)(shelter.Min.X + shelter.Max.X) / 2, (float)(shelter.Min.Z + shelter.Max.Z) / 2), 11f));
        _pads.Add((Flat(planet.EscapePod.Position), 6f));
        _pads.Add((Flat(planet.Start.Position), 4f));
        foreach (RespawnSpot spot in planet.RespawnPoints)
            if (spot.Kind == RespawnKind.SurvivalTent) _pads.Add((Flat(spot.Position), 4f));
    }

    static Vector2 Flat(Pos p) => new((float)p.X, (float)p.Z);

    public float Height(float x, float z)
    {
        var here = new Vector2(x, z);
        float h = _noise.GetNoise2D(x, z) * 2.2f;

        foreach (Zone zone in _planet.Zones)
        {
            if (zone.Terrain == null) continue;
            float d = here.DistanceTo(Flat(zone.Center));
            float r = (float)zone.Radius, f = (float)zone.Falloff, depth = (float)zone.Terrain.Depth;
            switch (zone.Terrain.Shape)
            {
                case "valley": // a flat snowy floor with long gentle sides
                    h += depth * Smooth(1 - Mathf.Clamp((d - r * 0.7f) / (r + f - r * 0.7f), 0, 1));
                    break;
                case "cone": // a volcanic cone with a crater holding the lava pool
                    float rim = (float)zone.LavaRadius + 3f, foot = r + f * 0.7f;
                    h += depth * Smooth(Mathf.Clamp((foot - d) / (foot - rim), 0, 1));
                    if (d < rim) h -= depth * 0.55f * Smooth(1 - d / rim);
                    break;
                case "bowl":
                    float edge = r + f * 0.6f;
                    if (d < edge) h += depth * Smooth(1 - d / edge);
                    break;
                case "funnel": // steady sides you can walk down and back up
                    float width = (float)zone.Terrain.Width;
                    if (d < width) h += depth * Mathf.Clamp((width - d) / (width - 3f), 0, 1);
                    break;
            }
        }

        foreach (var (at, radius) in _pads)
        {
            float w = Smooth(Mathf.Clamp((radius + 6f - here.DistanceTo(at)) / 6f, 0, 1));
            h = Mathf.Lerp(h, 0f, w);
        }
        return h;
    }

    /// <summary>The lava pool's surface: the bottom of the vent's crater.</summary>
    public float LavaLevel(Zone vent) => Height((float)vent.Center.X, (float)vent.Center.Z) + 0.15f;

    static float Smooth(float t) => t * t * (3 - 2 * t);

    public StaticBody3D Build()
    {
        int n = Vertices;
        var heights = new float[n * n];
        var vertices = new Vector3[n * n];
        var colors = new Color[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = i - Half, z = j - Half;
                float h = Height(x, z);
                heights[j * n + i] = h;
                vertices[j * n + i] = new Vector3(x, h, z);
                colors[j * n + i] = ColorAt(x, z, h);
            }

        var normals = new Vector3[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float hl = heights[j * n + Math.Max(i - 1, 0)], hr = heights[j * n + Math.Min(i + 1, n - 1)];
                float hd = heights[Math.Max(j - 1, 0) * n + i], hu = heights[Math.Min(j + 1, n - 1) * n + i];
                normals[j * n + i] = new Vector3(hl - hr, 2f, hd - hu).Normalized();
            }

        var indices = new int[(n - 1) * (n - 1) * 6];
        int k = 0;
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                indices[k++] = a; indices[k++] = b; indices[k++] = c;
                indices[k++] = b; indices[k++] = d; indices[k++] = c;
            }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var ground = new StaticBody3D { Name = "Ground", CollisionLayer = Layers.World, CollisionMask = 0 };
        ground.AddChild(new CollisionShape3D
        {
            Shape = new HeightMapShape3D { MapWidth = n, MapDepth = n, MapData = heights },
        });
        ground.AddChild(new MeshInstance3D
        {
            Name = "GroundMesh",
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                AlbedoTexture = new NoiseTexture2D
                {
                    Noise = new FastNoiseLite { Frequency = 0.03f },
                    Seamless = true,
                    Width = 512,
                    Height = 512,
                    ColorRamp = new Gradient { Colors = new[] { new Color(0.72f, 0.72f, 0.72f), Colors.White } },
                },
                Uv1Triplanar = true,
                Uv1Scale = Vector3.One * 0.08f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                Roughness = 1f,
            },
        });
        return ground;
    }

    /// <summary>Ground colour: desert ochre, tinted by whichever hazard owns the spot.</summary>
    Color ColorAt(float x, float z, float h)
    {
        var here = new Vector2(x, z);
        float grain = _noise.GetNoise2D(x * 3.1f, z * 3.1f) * 0.5f + 0.5f;
        Color c = Palette.Sand.Lerp(Palette.Rust, grain * 0.45f);

        foreach (Zone zone in _planet.Zones)
        {
            float d = here.DistanceTo(Flat(zone.Center));
            float w = (float)Model.Planet.ZoneWeight(zone, Pos.Flat(x, z));
            switch (zone.Id)
            {
                case "frozen-valley":
                    c = c.Lerp(Palette.Snow.Lerp(new Color("93b4e0"), grain * 0.6f), Mathf.Clamp(w * 1.3f, 0, 1));
                    break;
                case "volcanic-vent":
                    c = c.Lerp(Palette.Basalt, Mathf.Clamp(w * 1.4f, 0, 1));
                    if (d < zone.LavaRadius + 3) c = c.Lerp(new Color("7a2410"), Smooth(1 - d / (float)(zone.LavaRadius + 3)));
                    break;
                case "radiation-crater":
                    c = c.Lerp(Palette.RadGround.Lerp(new Color("b4c94e"), grain * 0.6f), Mathf.Clamp(w * 1.2f, 0, 1));
                    break;
                case "airless-sinkhole":
                    float funnel = zone.Terrain != null ? (float)zone.Terrain.Width : (float)zone.Radius;
                    c = c.Lerp(new Color("3e2b4c"), Smooth(Mathf.Clamp((funnel + 2 - d) / 6f, 0, 1)));
                    break;
            }
        }

        foreach (Territory land in _planet.Territories)
            if (here.DistanceTo(Flat(land.Center)) < land.Radius) c = c.Lerp(new Color("b5543a"), 0.25f);

        foreach (var (at, radius) in _pads)
            if (here.DistanceTo(at) < radius - 2f) c = c.Lerp(new Color("9b9086"), 0.35f);
        return c;
    }
}
