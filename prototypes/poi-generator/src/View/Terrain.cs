using System;
using System.Collections.Generic;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// Flat ground at y = 0 built from 8 m tiles, with holes where a place's rock plug takes
/// over, and a few huge quads around it out to the horizon. Mesas and spires give the eye
/// something to judge distance by.
/// </summary>
public static class Terrain
{
    public const int Tile = PoiView.Tile;

    /// <summary>
    /// Broad, soft variation over the flats, per 8 m tile. Rock plugs use the same function
    /// for their top faces, so a place's plug can't be told from the ground around it.
    /// </summary>
    public static Color Shaded(Color c, float x, float z)
    {
        int tx = Mathf.FloorToInt(x / Tile), tz = Mathf.FloorToInt(z / Tile);
        float j = 0.93f + 0.12f * Gen.Noise.Fbm(0x5EEDUL, tx, tz, 0.06f);
        return new Color(c.R * j, c.G * j, c.B * j);
    }

    /// <param name="min">world corner of the tiled area, a multiple of the tile size</param>
    /// <param name="tiles">tiles across, each way</param>
    /// <param name="colorAt">ground colour at a world position</param>
    /// <param name="holes">world rectangles (inclusive cells) to leave open for plugs</param>
    public static Node3D Build(Vector2I min, Vector2I tiles, Func<float, float, Color> colorAt, List<Rect2I> holes, bool collision, float skirt)
    {
        var root = new Node3D { Name = "Terrain" };
        var verts = new List<Vector3>();
        var colors = new List<Color>();
        var faces = new List<Vector3>();

        bool InHole(int x, int z)
        {
            foreach (var h in holes)
                if (x >= h.Position.X && x < h.End.X && z >= h.Position.Y && z < h.End.Y) return true;
            return false;
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            verts.Add(a); verts.Add(b); verts.Add(c);
            verts.Add(a); verts.Add(c); verts.Add(d);
            for (int i = 0; i < 6; i++) colors.Add(col);
            if (collision)
            {
                faces.Add(a); faces.Add(b); faces.Add(c);
                faces.Add(a); faces.Add(c); faces.Add(d);
            }
        }

        for (int tz = 0; tz < tiles.Y; tz++)
            for (int tx = 0; tx < tiles.X; tx++)
            {
                int x = min.X + tx * Tile, z = min.Y + tz * Tile;
                if (InHole(x, z)) continue;
                var c = Shaded(colorAt(x + Tile / 2f, z + Tile / 2f), x, z);
                Quad(new Vector3(x, 0, z), new Vector3(x + Tile, 0, z), new Vector3(x + Tile, 0, z + Tile), new Vector3(x, 0, z + Tile), c);
            }

        // The skirt: four big quads out to the horizon, in the colour of the middle.
        if (skirt > 0)
        {
            float x0 = min.X, z0 = min.Y, x1 = min.X + tiles.X * Tile, z1 = min.Y + tiles.Y * Tile;
            var c = colorAt((x0 + x1) / 2, (z0 + z1) / 2).Darkened(0.04f);
            float s = skirt;
            Quad(new Vector3(x0 - s, 0, z0 - s), new Vector3(x1 + s, 0, z0 - s), new Vector3(x1 + s, 0, z0), new Vector3(x0 - s, 0, z0), c);
            Quad(new Vector3(x0 - s, 0, z1), new Vector3(x1 + s, 0, z1), new Vector3(x1 + s, 0, z1 + s), new Vector3(x0 - s, 0, z1 + s), c);
            Quad(new Vector3(x0 - s, 0, z0), new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x0 - s, 0, z1), c);
            Quad(new Vector3(x1, 0, z0), new Vector3(x1 + s, 0, z0), new Vector3(x1 + s, 0, z1), new Vector3(x1, 0, z1), c);
        }

        // (Each quad above runs clockwise seen from above, which is Godot's front face.)
        var normals = new Vector3[verts.Count];
        Array.Fill(normals, Vector3.Up);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 1f });
        root.AddChild(new MeshInstance3D { Mesh = mesh, Name = "Ground" });

        if (collision)
        {
            var body = new StaticBody3D { Name = "GroundBody", CollisionLayer = 1, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces.ToArray(), BackfaceCollision = true } });
            root.AddChild(body);
        }
        return root;
    }

    /// <summary>Mesas and spires scattered around, kept clear of the given circles.</summary>
    public static Node3D Mesas(ulong seed, Vector2 center, float radiusMin, float radiusMax, int count, Color rock, List<(Vector2 at, float r)> keepClear, float scale = 1f)
    {
        var root = new Node3D { Name = "Mesas" };
        var rng = new RandomNumberGenerator { Seed = seed };
        var mats = new StandardMaterial3D[4];
        for (int i = 0; i < 4; i++)
            mats[i] = new StandardMaterial3D { AlbedoColor = rock.Lightened(i * 0.07f).Darkened(0.05f * (i % 2)), Roughness = 1f };
        int placed = 0;
        for (int tries = 0; tries < count * 6 && placed < count; tries++)
        {
            float ang = rng.RandfRange(0, Mathf.Tau), dist = Mathf.Sqrt(rng.RandfRange(radiusMin * radiusMin, radiusMax * radiusMax));
            var at = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
            bool clear = true;
            foreach (var (c, r) in keepClear)
                if (at.DistanceTo(c) < r) { clear = false; break; }
            if (!clear) continue;
            placed++;
            float w = rng.RandfRange(10f, 55f) * scale, h = rng.RandfRange(12f, 90f) * scale;
            bool spire = rng.Randf() < 0.35f;
            // Stepped tiers, like eroded strata.
            int tiers = spire ? 1 : rng.RandiRange(1, 3);
            float y = 0, tw = spire ? w * 0.3f : w, td = spire ? w * 0.3f : w * rng.RandfRange(0.6f, 1.3f);
            float th = spire ? h * 1.6f : h * 0.5f / tiers;
            var node = new Node3D { Position = new Vector3(at.X, 0, at.Y), RotationDegrees = new Vector3(0, rng.RandfRange(0, 90), 0) };
            for (int t = 0; t < tiers; t++)
            {
                node.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(tw, th, td) },
                    MaterialOverride = mats[(placed + t) % 4],
                    Position = new Vector3(0, y + th / 2, 0),
                });
                y += th;
                tw *= 0.7f;
                td *= 0.7f;
            }
            root.AddChild(node);
        }
        return root;
    }
}
