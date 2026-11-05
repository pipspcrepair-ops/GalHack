using System.Collections.Generic;
using Godot;

namespace Encounters;

/// <summary>
/// Stand-ins for space creatures in this prototype's view: smooth, dark, faintly glowing shapes
/// at the right size, so a shoal or a leviathan reads on the HUD and from the cockpit. The
/// creatures themselves (parts, rigs, attacks, drops) are built by the wildlife prototype; the
/// director only knows a creature's kind, so that is all this draws from.
/// </summary>
public static class CreatureForms
{
    static readonly Dictionary<string, Hull> Cache = new();
    static readonly Dictionary<string, StandardMaterial3D> Materials = new();

    public static bool Known(string kind) => kind is "veil-drifter" or "rock-ray" or "sky-manta" or "hull-leech" or "void-leviathan";

    /// <summary>How the HUD names a kind.</summary>
    public static string Name(string kind) => kind switch
    {
        "veil-drifter" => "veil drifter",
        "rock-ray" => "rock ray",
        "sky-manta" => "sky manta",
        "hull-leech" => "hull leech",
        "void-leviathan" => "void leviathan",
        _ => kind.Replace('-', ' '),
    };

    public static Hull Get(string kind)
    {
        if (Cache.TryGetValue(kind, out Hull hull)) return hull;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float radius;
        switch (kind)
        {
            case "veil-drifter":
                Blob(st, new Vector3(5f, 4.4f, 6f), Vector3.Zero);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.Tau / 6f;
                    Blob(st, new Vector3(0.25f, 0.25f, 9f), new Vector3(Mathf.Cos(a) * 1.6f, Mathf.Sin(a) * 1.4f, 7f));
                }
                radius = 6f;
                break;
            case "rock-ray":
            case "sky-manta":
            {
                float s = kind == "sky-manta" ? 5f : 1f;
                Blob(st, new Vector3(2.4f, 0.8f, 4f) * s, Vector3.Zero);
                for (int side = -1; side <= 1; side += 2)
                    Blob(st, new Vector3(4.6f, 0.25f, 3f) * s, new Vector3(side * 3f, 0f, 0.5f) * s, new Basis(Vector3.Up, side * 0.3f));
                Blob(st, new Vector3(0.2f, 0.2f, 5f) * s, new Vector3(0f, 0f, 4.5f) * s);
                radius = 5f * s;
                break;
            }
            case "hull-leech":
                for (int i = 0; i < 6; i++) Blob(st, new Vector3(0.45f, 0.4f, 0.55f), new Vector3(0f, 0f, i * 0.42f));
                radius = 1.5f;
                break;
            default: // the void leviathan: a hundred metres of tapering segments
                for (int i = 0; i < 12; i++)
                {
                    float r = 11f * (1f - 0.7f * i / 12f);
                    Blob(st, new Vector3(r, r * 0.9f, 10f), new Vector3(Mathf.Sin(i * 0.6f) * 4f, 0f, -50f + i * 8.5f));
                }
                radius = 55f;
                break;
        }
        st.GenerateNormals();
        hull = new Hull { Name = kind, Mesh = st.Commit(), Radius = radius };
        Cache[kind] = hull;
        return hull;
    }

    /// <summary>A dark body that glows a little in its own colour.</summary>
    public static StandardMaterial3D Material(string kind)
    {
        if (Materials.TryGetValue(kind, out StandardMaterial3D m)) return m;
        (Color body, Color glow) = kind switch
        {
            "veil-drifter" => (new Color("3a1f5a"), new Color("9d8cff")),
            "rock-ray" => (new Color("4a3a30"), new Color("ffb03b")),
            "sky-manta" => (new Color("8a6a48"), new Color("ffd27a")),
            "hull-leech" => (new Color("2a1014"), new Color("ff3b3b")),
            _ => (new Color("141428"), new Color("7cf9ff")),
        };
        m = new StandardMaterial3D { AlbedoColor = body, Roughness = 0.5f, EmissionEnabled = true, Emission = glow, EmissionEnergyMultiplier = 0.35f, RimEnabled = true, Rim = 0.4f };
        Materials[kind] = m;
        return m;
    }

    static void Blob(SurfaceTool st, Vector3 size, Vector3 at, Basis? turn = null)
    {
        var sphere = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 14, Rings = 8 };
        st.AppendFrom(sphere, 0, new Transform3D((turn ?? Basis.Identity) * Basis.FromScale(size), at));
    }
}
