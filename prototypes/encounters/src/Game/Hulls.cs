using System.Collections.Generic;
using Godot;
using static Encounters.Palette;

namespace Encounters;

/// <summary>A built hull: every block baked into one mesh, plus where its engines are.</summary>
public sealed class Hull
{
    public string Name = "";
    public ArrayMesh Mesh;
    /// <summary>Bounding sphere radius round the origin, for HUD brackets and touch range.</summary>
    public float Radius;
    public readonly List<(Vector3 At, float Size)> Engines = new();
    public Color EngineColor = new(1f, 0.55f, 0.2f);
}

/// <summary>
/// Collects coloured blocks into a single mesh with vertex colours, so a whole ship is one draw
/// call however many blocks it has. Ships face -Z with +Y up, in metres.
/// </summary>
public sealed class BlockBuilder
{
    // Each face: outward normal and two in-plane axes with u × v = n.
    static readonly (Vector3 N, Vector3 U, Vector3 V)[] Faces =
    {
        (Vector3.Right, Vector3.Up, Vector3.Back), (Vector3.Left, Vector3.Back, Vector3.Up),
        (Vector3.Up, Vector3.Back, Vector3.Right), (Vector3.Down, Vector3.Right, Vector3.Back),
        (Vector3.Back, Vector3.Right, Vector3.Up), (Vector3.Forward, Vector3.Up, Vector3.Right),
    };

    readonly SurfaceTool _st = new();
    public readonly Hull Hull;

    public BlockBuilder(string name)
    {
        Hull = new Hull { Name = name };
        _st.Begin(Mesh.PrimitiveType.Triangles);
    }

    public void Box(float x, float y, float z, float sx, float sy, float sz, Color color, float glow = 0f) =>
        Box(new Vector3(x, y, z), new Vector3(sx, sy, sz), color, glow, Basis.Identity);

    /// <summary>A block turned by Euler angles in degrees: fins, spikes, growths.</summary>
    public void Tilted(Vector3 center, Vector3 size, Vector3 degrees, Color color, float glow = 0f) =>
        Box(center, size, color, glow, Basis.FromEuler(degrees * (Mathf.Pi / 180f)));

    public void Box(Vector3 center, Vector3 size, Color color, float glow, Basis basis)
    {
        Color paint = color.SrgbToLinear(); // the hull shader takes vertex colour as linear albedo
        Vector3 half = size * 0.5f;
        foreach (var (n, u, v) in Faces)
        {
            Vector3 mid = n * half, su = u * half, sv = v * half;
            Vector3 normal = (basis * n).Normalized();
            // Clockwise seen from outside: Godot's front faces.
            Corner(center, basis, mid - su + sv, normal, paint, glow);
            Corner(center, basis, mid + su + sv, normal, paint, glow);
            Corner(center, basis, mid + su - sv, normal, paint, glow);
            Corner(center, basis, mid - su + sv, normal, paint, glow);
            Corner(center, basis, mid + su - sv, normal, paint, glow);
            Corner(center, basis, mid - su - sv, normal, paint, glow);
        }
    }

    void Corner(Vector3 center, Basis basis, Vector3 local, Vector3 normal, Color paint, float glow)
    {
        Vector3 p = center + basis * local;
        Hull.Radius = Mathf.Max(Hull.Radius, p.Length());
        _st.SetNormal(normal);
        _st.SetColor(paint);
        _st.SetUV2(new Vector2(glow, 0f));
        _st.AddVertex(p);
    }

    /// <summary>Hazard stripes: the box cut into bands along one axis (0 x, 1 y, 2 z), alternating colours.</summary>
    public void Stripes(Vector3 center, Vector3 size, int bands, int axis, Color a, Color b)
    {
        float step = size[axis] / bands;
        for (int i = 0; i < bands; i++)
        {
            Vector3 c = center, s = size;
            c[axis] = center[axis] - size[axis] * 0.5f + (i + 0.5f) * step;
            s[axis] = step;
            Box(c, s, i % 2 == 0 ? a : b, 0f, Basis.Identity);
        }
    }

    /// <summary>A checkerboard panel; its thinnest dimension is its thickness.</summary>
    public void Checker(Vector3 center, Vector3 size, int cols, int rows, Color a, Color b)
    {
        int thin = size.X <= size.Y && size.X <= size.Z ? 0 : size.Y <= size.Z ? 1 : 2;
        int ua = thin == 0 ? 2 : 0, va = thin == 1 ? 2 : 1;
        float cu = size[ua] / cols, cv = size[va] / rows;
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                Vector3 c = center, s = size;
                c[ua] = center[ua] - size[ua] * 0.5f + (i + 0.5f) * cu;
                c[va] = center[va] - size[va] * 0.5f + (j + 0.5f) * cv;
                s[ua] = cu;
                s[va] = cv;
                Box(c, s, (i + j) % 2 == 0 ? a : b, 0f, Basis.Identity);
            }
    }

    /// <summary>A glowing nozzle facing aft, with a flare drawn over it by the ship's view.</summary>
    public void Engine(float x, float y, float z, float size)
    {
        Box(x, y, z, size, size, 0.4f, Hull.EngineColor, 1f);
        Hull.Engines.Add((new Vector3(x, y, z + 0.4f), size));
    }

    public Hull Commit()
    {
        Hull.Mesh = _st.Commit();
        return Hull;
    }
}

/// <summary>
/// The placeholder ships, block-built in code. Chunky silhouettes and loud liveries so each
/// faction reads at a glance, even a few kilometres away.
/// </summary>
public static class HullLibrary
{
    static readonly Dictionary<string, Hull> Cache = new();

    public static Hull Get(string name)
    {
        if (Cache.TryGetValue(name, out Hull hull)) return hull;
        hull = name switch
        {
            "freighter" => Freighter(),
            "hauler" => Hauler(),
            "corvette" => Corvette(),
            "raider" => Raider(),
            "trader" => Trader(),
            "courier" => Courier(),
            "miner" => Miner(),
            "lancer" => Lancer(),
            "bloom-hulk" => BloomHulk(),
            "skiff" => Skiff(),
            "cargo-pod" => CargoPod(),
            _ => Hauler(),
        };
        Cache[name] = hull;
        return hull;
    }

    /// <summary>A long-haul freighter: bridge at the bow, a spine stacked with containers, four big engines.</summary>
    static Hull Freighter()
    {
        var b = new BlockBuilder("freighter");
        b.Box(0, 0, 0, 6, 6, 126, Dark);
        b.Box(0, 2, -68, 22, 14, 18, Cream);
        b.Box(0, -3.6f, -68, 22.3f, 3f, 18.3f, Red);
        b.Box(0, 5.5f, -77.1f, 17, 2.2f, 0.4f, Warm, 1f);
        b.Box(0, 11, -65, 10, 4, 10, Cream);
        b.Box(0, 11.4f, -70.1f, 8, 1.3f, 0.3f, Warm, 1f);
        b.Box(0, 15, -63, 0.6f, 6, 0.6f, Steel);
        b.Checker(new Vector3(0, -0.5f, -77.2f), new Vector3(22, 4, 0.3f), 11, 2, Black, Yellow);

        Color[] cargo = { Orange, Yellow, Teal, Red, Cream, Navy, Orange, Teal, Yellow };
        int k = 0;
        for (int row = 0; row < 5; row++)
        {
            float z = -48 + row * 21;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { -1f, 1f })
                {
                    k++;
                    if (k % 7 == 3) continue; // gaps make the silhouette
                    Color c = cargo[(row * 4 + k * 5) % cargo.Length];
                    b.Box(sx * 6.6f, sy * 6.6f, z, 12.4f, 12.4f, 19, c);
                    b.Box(sx * 6.6f, sy * 6.6f, z + 8.2f, 12.6f, 12.6f, 1.2f, Cream); // container rib
                }
        }

        b.Box(0, 0, 58, 28, 20, 16, Cream);
        b.Stripes(new Vector3(0, -7.5f, 58), new Vector3(28.3f, 3, 16.3f), 9, 2, Black, Yellow);
        b.Box(0, 9.5f, 58, 20, 1.5f, 14, Orange);
        foreach (float sx in new[] { -1f, 1f })
        {
            b.Box(sx * 17.5f, 0, 54, 7, 0.8f, 14, Teal);
            b.Box(sx * 21.5f, 0, 54, 1.2f, 6, 14, Teal);
        }
        foreach (float sx in new[] { -7f, 7f })
            foreach (float sy in new[] { -5f, 5f })
            {
                b.Box(sx, sy, 67.5f, 8.5f, 8.5f, 3, Dark);
                b.Engine(sx, sy, 69.2f, 6.5f);
            }
        return b.Commit();
    }

    /// <summary>A short hauler for convoys: cab, three container pairs, two engines.</summary>
    static Hull Hauler()
    {
        var b = new BlockBuilder("hauler");
        b.Box(0, 0, 0, 4, 4, 58, Dark);
        b.Box(0, 1.5f, -31, 13, 9, 9, Cream);
        b.Box(0, -2.2f, -31, 13.2f, 2f, 9.2f, Orange);
        b.Box(0, 3.4f, -35.6f, 10, 1.6f, 0.3f, Warm, 1f);
        Color[] cargo = { Yellow, Teal, Red, Orange, Cream, Teal };
        for (int row = 0; row < 3; row++)
            foreach (float sx in new[] { -1f, 1f })
                b.Box(sx * 4.4f, 0, -16 + row * 15, 8.4f, 8.4f, 13.5f, cargo[(row * 2 + (sx > 0 ? 1 : 0)) % cargo.Length]);
        b.Box(0, 0, 28, 15, 10, 9, Cream);
        b.Stripes(new Vector3(0, 0, 28), new Vector3(15.2f, 2.2f, 9.2f), 7, 2, Black, Yellow);
        foreach (float sx in new[] { -4f, 4f })
        {
            b.Box(sx, 0, 33.2f, 5.5f, 5.5f, 2, Dark);
            b.Engine(sx, 0, 34.3f, 4.2f);
        }
        return b.Commit();
    }

    /// <summary>Concord patrol corvette: clean cream wedge, navy and red livery, pale blue engines.</summary>
    static Hull Corvette()
    {
        var b = new BlockBuilder("corvette");
        b.Hull.EngineColor = new Color(0.55f, 0.85f, 1f);
        b.Box(0, 0, 0, 9, 5, 26, Cream);
        b.Box(0, -0.5f, -16, 7, 4, 6, Cream);
        b.Box(0, -1, -20.5f, 4.5f, 2.6f, 3, Cream);
        b.Box(0, -1, -22.1f, 4.6f, 1, 0.3f, Red);
        b.Box(0, 2.55f, 0, 3, 0.12f, 24, Navy);
        foreach (float sx in new[] { -1f, 1f })
        {
            b.Box(sx * 4.55f, 0, 0, 0.12f, 1.4f, 26, Red);
            b.Box(sx * 7, 0, 7, 4, 4, 14, Navy);
            b.Box(sx * 7, 2.05f, 7, 3, 0.12f, 12, Cream);
            b.Engine(sx * 7, 0, 14.2f, 3);
            b.Box(sx * 10.5f, -1, 6, 6, 0.6f, 9, Cream);
            b.Checker(new Vector3(sx * 12.2f, -0.65f, 6), new Vector3(2.4f, 0.1f, 8), 2, 6, Navy, Cream);
        }
        b.Box(0, 4, -2, 5, 3, 8, Cream);
        b.Box(0, 4.3f, -6.1f, 4, 1.1f, 0.25f, Cyan, 0.8f);
        b.Box(0, 6.5f, 9, 0.6f, 5, 6, Red);
        return b.Commit();
    }

    /// <summary>Carrion Reaver raider: rust and black, hazard chevrons, ram prongs, mismatched engines.</summary>
    static Hull Raider()
    {
        var b = new BlockBuilder("raider");
        b.Hull.EngineColor = new Color(1f, 0.3f, 0.1f);
        b.Box(0, 0, 0, 7, 4, 18, Rust);
        foreach (float sx in new[] { -2.5f, 2.5f })
        {
            b.Box(sx, -0.5f, -14, 1.5f, 1.5f, 12, Dark);
            b.Box(sx, -0.5f, -20.3f, 1.7f, 1.7f, 1, Yellow);
        }
        b.Box(1.8f, 2.6f, -4, 3, 1.6f, 5, Dark);
        b.Box(1.8f, 2.7f, -6.55f, 2.4f, 0.5f, 0.2f, Red, 1f);
        b.Stripes(new Vector3(-3.55f, 0, 0), new Vector3(0.12f, 2.4f, 16), 8, 2, Yellow, Black);
        b.Stripes(new Vector3(3.55f, 0, 0), new Vector3(0.12f, 2.4f, 16), 8, 2, Black, Yellow);
        b.Box(0, 2.06f, 2, 4, 0.12f, 5, Red);
        b.Box(-4.2f, 0.6f, 8, 3.6f, 3.6f, 9, Dark);
        b.Box(4.4f, -0.6f, 7, 2.6f, 2.6f, 7, Dark);
        b.Engine(-4.2f, 0.6f, 12.6f, 2.8f);
        b.Engine(4.4f, -0.6f, 10.6f, 2f);
        b.Tilted(new Vector3(0, 3.4f, 4), new Vector3(0.6f, 4, 0.6f), new Vector3(-35, 0, 0), Steel);
        b.Tilted(new Vector3(-2, 2.9f, 6), new Vector3(0.5f, 3.4f, 0.5f), new Vector3(-40, 0, 25), Steel);
        b.Tilted(new Vector3(2.4f, -2.6f, 3), new Vector3(0.5f, 3, 0.5f), new Vector3(30, 0, -20), Steel);
        return b.Commit();
    }

    /// <summary>Free trader: stubby yellow body, teal band, side pods.</summary>
    static Hull Trader()
    {
        var b = new BlockBuilder("trader");
        b.Box(0, 0, 0, 14, 9, 30, Yellow);
        b.Box(0, 0, 0, 14.2f, 2, 30.2f, Teal);
        b.Box(0, 1.5f, -18, 10, 6, 6, Cream);
        b.Box(0, 2.5f, -21.05f, 8, 1.6f, 0.2f, Warm, 1f);
        b.Box(0, 5.2f, 2, 9, 1.4f, 20, Orange);
        foreach (float sx in new[] { -1f, 1f })
        {
            b.Box(sx * 10, -1, 2, 6, 6, 20, Cream);
            b.Box(sx * 10, -1, -8.3f, 6.2f, 6.2f, 1.4f, Orange);
            b.Box(sx * 10, -1, 12.3f, 6.2f, 6.2f, 1.4f, Orange);
            b.Engine(sx * 3.5f, 0, 17.3f, 3.6f);
        }
        b.Box(0, 0, 16, 10, 6, 3, Dark);
        b.Box(3, 7, -6, 0.4f, 4, 0.4f, Steel);
        b.Checker(new Vector3(0, 4.6f, -12), new Vector3(8, 0.1f, 4), 4, 2, Black, Cream);
        return b.Commit();
    }

    /// <summary>A small courier, here holed and drifting with dead engines: the distress ship.</summary>
    static Hull Courier()
    {
        var b = new BlockBuilder("courier");
        b.Box(0, 0, 0, 7, 4.5f, 20, Cream);
        b.Box(0, -0.9f, 0, 7.15f, 1.4f, 20.1f, Orange);
        b.Box(0, 1.2f, -11.5f, 5.5f, 3, 3, Cream);
        b.Box(0, 1.8f, -13.05f, 4.4f, 1, 0.2f, Warm, 0.4f);
        foreach (float sx in new[] { -1f, 1f })
        {
            b.Box(sx * 4.8f, 0, 7, 2.5f, 2.5f, 8, Dark);
            b.Box(sx * 4.8f, 0, 11.1f, 2, 2, 0.3f, Black);
        }
        b.Box(0, 2.3f, 3, 3, 0.2f, 4, Black); // a hole in the roof
        b.Tilted(new Vector3(2.2f, 2.9f, 4), new Vector3(3, 0.3f, 3), new Vector3(0, 10, 28), Cream);
        b.Box(0, 3.4f, -4, 0.4f, 2.2f, 0.4f, Steel);
        b.Stripes(new Vector3(-3.6f, 0.8f, 0), new Vector3(0.12f, 1, 12), 6, 2, Yellow, Black);
        return b.Commit();
    }

    /// <summary>Kessari miner: ochre industrial block with drill arms and cyan robot eyes.</summary>
    static Hull Miner()
    {
        var b = new BlockBuilder("miner");
        b.Hull.EngineColor = new Color(0.5f, 1f, 0.95f);
        b.Box(0, 0, 0, 12, 8, 22, Ochre);
        b.Stripes(new Vector3(0, 0, -11.1f), new Vector3(12, 8, 0.25f), 9, 0, Black, Yellow);
        foreach (float sx in new[] { -1f, 1f })
        {
            b.Box(sx * 5, -2.5f, -16, 2.5f, 2.5f, 12, Dark);
            b.Box(sx * 5, -2.5f, -23, 3.8f, 3.8f, 2.5f, Steel);
            b.Box(sx * 5, -2.5f, -24.4f, 1.6f, 1.6f, 0.6f, Cyan, 1f);
            b.Box(sx * 2.6f, 2, -11.3f, 1.8f, 1, 0.2f, Cyan, 1f);
            b.Box(sx * 6.1f, 0, 0, 0.2f, 3, 18, Rust);
        }
        b.Box(0, 5.5f, 4, 9, 3.4f, 11, Rust);
        b.Box(0, 7.3f, 4, 7, 0.3f, 9, Dark);
        b.Box(0, 0, 12, 8, 6, 3, Dark);
        b.Engine(-2, 0, 13.6f, 2.6f);
        b.Engine(2, 0, 13.6f, 2.6f);
        return b.Commit();
    }

    /// <summary>Kessari lancer: a fighter built round a long lance.</summary>
    static Hull Lancer()
    {
        var b = new BlockBuilder("lancer");
        b.Hull.EngineColor = new Color(0.5f, 1f, 0.95f);
        b.Box(0, 0, 0, 5, 3, 16, Ochre);
        b.Box(0, 0, -14, 1, 1, 13, Steel);
        b.Box(0, 0, -20.8f, 1.4f, 1.4f, 0.8f, Cyan, 1f);
        b.Stripes(new Vector3(0, 1.56f, 0), new Vector3(3, 0.12f, 12), 6, 2, Black, Yellow);
        foreach (float sx in new[] { -1f, 1f })
        {
            b.Box(sx * 5, 0, 3, 5, 0.5f, 6, Dark);
            b.Box(sx * 7.6f, 0, 3, 0.4f, 3, 5, Ochre);
            b.Box(sx * 1.4f, 0.9f, -8.1f, 1, 0.6f, 0.2f, Cyan, 1f);
        }
        b.Box(0, 0, 8.5f, 4, 2.5f, 2, Dark);
        b.Engine(0, 0, 9.6f, 2.2f);
        return b.Commit();
    }

    /// <summary>A derelict overgrown by the Hollow Bloom: bone-pale hull, pulsing violet and magenta growths.</summary>
    static Hull BloomHulk()
    {
        var b = new BlockBuilder("bloom-hulk");
        b.Box(0, 0, 0, 14, 12, 78, Bone);
        b.Tilted(new Vector3(0, 2, -44), new Vector3(12, 9, 9), new Vector3(9, 7, 4), Bone);
        b.Box(0, -1, 42, 18, 14, 10, Steel);
        b.Box(0, -6.2f, 0, 14.2f, 1.2f, 78.2f, Rust);
        b.Box(-5, 4, 47.5f, 5, 5, 1, Black);
        b.Box(5, -3, 47.5f, 5, 5, 1, Black);

        var rng = new RandomNumberGenerator { Seed = 1979 };
        for (int i = 0; i < 46; i++)
        {
            // Growths cluster along the upper flank and boil out of a breach amidships.
            float z = rng.RandfRange(-38, 40);
            Vector3 at = new(rng.RandfRange(-2, 8), rng.RandfRange(2, 8), z);
            if (i % 3 == 0) at = new Vector3(rng.RandfRange(-8, 8), rng.RandfRange(-7, 7), rng.RandfRange(-6, 10));
            Vector3 size = new Vector3(rng.RandfRange(2, 9), rng.RandfRange(2, 7), rng.RandfRange(2, 9)) * (i % 5 == 0 ? 1.4f : 1f);
            Vector3 tilt = new(rng.RandfRange(-40, 40), rng.RandfRange(-40, 40), rng.RandfRange(-40, 40));
            Color color = i % 3 == 0 ? Magenta : Violet;
            b.Tilted(at, size, tilt, color, rng.RandfRange(0.15f, 0.5f));
        }
        for (int i = 0; i < 14; i++)
        {
            Vector3 at = new(rng.RandfRange(-4, 9), rng.RandfRange(0, 10), rng.RandfRange(-30, 30));
            Vector3 tilt = new(rng.RandfRange(-70, 70), rng.RandfRange(-90, 90), rng.RandfRange(-70, 70));
            b.Tilted(at, new Vector3(0.8f, 0.8f, rng.RandfRange(12, 26)), tilt, Magenta, 0.6f);
        }
        for (int i = 0; i < 8; i++)
            b.Box(rng.RandfRange(-6, 8), rng.RandfRange(-5, 9), rng.RandfRange(-30, 30), 1.4f, 1.4f, 1.4f, new Color("ffb3e6"), 1.5f);
        return b.Commit();
    }

    /// <summary>The player's skiff: small, cream and orange, twin engines.</summary>
    static Hull Skiff()
    {
        var b = new BlockBuilder("skiff");
        b.Box(0, 0, 0, 3.2f, 1.6f, 8, Cream);
        b.Box(0, 0.82f, 0.5f, 1.1f, 0.06f, 7, Orange);
        b.Box(0, 1.1f, -1.4f, 2, 0.9f, 2.6f, new Color("1b4a52"), 0.25f);
        b.Box(0, -0.2f, -4.6f, 2.2f, 1, 1.4f, Cream);
        b.Box(0, -0.2f, -5.35f, 2.3f, 0.4f, 0.12f, Red);
        foreach (float sx in new[] { -1f, 1f })
        {
            b.Box(sx * 3, -0.2f, 1.6f, 3.4f, 0.25f, 3, Orange);
            b.Box(sx * 4.8f, -0.2f, 1.6f, 0.3f, 0.9f, 2.6f, Yellow);
            b.Box(sx * 1.1f, 0, 4.3f, 1.2f, 1.2f, 1.4f, Dark);
            b.Engine(sx * 1.1f, 0, 5.05f, 0.9f);
        }
        return b.Commit();
    }

    /// <summary>A cargo pod a player dropped: theirs, so it never goes away.</summary>
    static Hull CargoPod()
    {
        var b = new BlockBuilder("cargo-pod");
        b.Box(0, 0, 0, 3, 3, 4.5f, Orange);
        b.Box(0, 0, -1.4f, 3.1f, 3.1f, 0.4f, Yellow);
        b.Box(0, 0, 1.4f, 3.1f, 3.1f, 0.4f, Yellow);
        b.Box(0, 1.6f, 0, 0.5f, 0.3f, 0.5f, new Color("9bf26b"), 2f);
        return b.Commit();
    }
}
