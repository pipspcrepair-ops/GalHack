using System.Collections.Generic;
using Godot;

namespace Boarding;

/// <summary>
/// Shared materials, meshes and procedural textures. Everything is cached so hundreds of
/// blocks share a handful of resources. No imported assets: the look comes from flat
/// colour slabs, banded walls, hazard stripes and checkers, Foss-style.
/// </summary>
public static class Art
{
    static readonly Dictionary<string, Material> Materials = new();
    static readonly Dictionary<(Vector3, Vector3), ArrayMesh> Boxes = new();

    public static StandardMaterial3D Flat(Color color, float roughness = 0.8f, float metallic = 0f) =>
        Cached($"flat{color.ToHtml()}{roughness}{metallic}", () => new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = roughness,
            Metallic = metallic,
        });

    public static StandardMaterial3D Glow(Color color, float energy = 2.5f) =>
        Cached($"glow{color.ToHtml()}{energy}", () => new StandardMaterial3D
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = energy,
        });

    /// <summary>Unshaded, alpha-blended, for flashes, lasers and tracers.</summary>
    public static StandardMaterial3D Additive(Color color) =>
        Cached($"add{color.ToHtml()}", () => new StandardMaterial3D
        {
            AlbedoColor = color,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false,
        });

    /// <summary>
    /// Horizontal colour bands up a wall, mapped with object-local triplanar UVs so every
    /// block of a deck lines up (block origins all sit at mid-deck height).
    /// </summary>
    public static StandardMaterial3D Banded(string key, float height, params (float from, Color color)[] bands) =>
        Cached("band" + key, () => new StandardMaterial3D
        {
            AlbedoTexture = BandTexture(bands),
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(0.25f, 1f / height, 0.25f),
            Uv1Offset = new Vector3(0f, 0.5f, 0f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Roughness = 0.85f,
        });

    /// <summary>Diagonal two-colour stripes, metre-scaled: hazard plates, weak hull, doors.</summary>
    public static StandardMaterial3D Stripes(Color a, Color b, float metres = 0.5f) =>
        Cached($"stripe{a.ToHtml()}{b.ToHtml()}{metres}", () => new StandardMaterial3D
        {
            AlbedoTexture = StripeTexture(a, b),
            Uv1Triplanar = true,
            Uv1Scale = Vector3.One / (metres * 2f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Roughness = 0.7f,
        });

    public static StandardMaterial3D Checker(Color a, Color b, float metres = 0.5f) =>
        Cached($"check{a.ToHtml()}{b.ToHtml()}{metres}", () => new StandardMaterial3D
        {
            AlbedoTexture = CheckerTexture(a, b),
            Uv1Triplanar = true,
            Uv1Scale = Vector3.One / (metres * 2f),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Roughness = 0.7f,
        });

    /// <summary>A soft round spot, for decals and muzzle flashes.</summary>
    public static Texture2D Spot { get; } = new GradientTexture2D
    {
        Gradient = new Gradient
        {
            Offsets = new[] { 0f, 0.45f, 1f },
            Colors = new[] { Colors.White, new Color(1, 1, 1, 0.7f), new Color(1, 1, 1, 0f) },
        },
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(1f, 0.5f),
        Width = 64,
        Height = 64,
    };

    /// <summary>A decal-like quad material: a coloured spot that fades at the edge.</summary>
    public static StandardMaterial3D SpotMaterial(Color color, bool glow = false) =>
        Cached($"spot{color.ToHtml()}{glow}", () =>
        {
            var m = new StandardMaterial3D
            {
                AlbedoColor = color,
                AlbedoTexture = Spot,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 1f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            if (glow)
            {
                m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                m.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
            }
            return m;
        });

    public static BoxMesh BoxMesh(Vector3 size) => new() { Size = size };

    /// <summary>
    /// A box whose vertices are already offset from the origin. Pieces of a broken-up block
    /// use these so their triplanar UVs carry on exactly where the whole block's left off.
    /// </summary>
    public static ArrayMesh Box(Vector3 center, Vector3 size)
    {
        if (Boxes.TryGetValue((center, size), out var cached)) return cached;
        var st = new SurfaceTool();
        st.Begin(Godot.Mesh.PrimitiveType.Triangles);
        Vector3 h = size * 0.5f;
        Vector3[] normals = { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward };
        foreach (Vector3 n in normals)
        {
            // Two axes spanning the face, chosen so the winding faces outward.
            Vector3 u = Mathf.Abs(n.Y) > 0.5f ? Vector3.Right : Vector3.Up;
            Vector3 v = n.Cross(u);
            Vector3 c = center + n * h;
            Vector3 du = u * h, dv = v * h;
            Vector3 a = c - du - dv, b = c + du - dv, cc = c + du + dv, d = c - du + dv;
            st.SetNormal(n);
            st.SetUV(new Vector2(0, 0)); st.AddVertex(a);
            st.SetUV(new Vector2(0, 1)); st.AddVertex(d);
            st.SetUV(new Vector2(1, 1)); st.AddVertex(cc);
            st.SetUV(new Vector2(0, 0)); st.AddVertex(a);
            st.SetUV(new Vector2(1, 1)); st.AddVertex(cc);
            st.SetUV(new Vector2(1, 0)); st.AddVertex(b);
        }
        var mesh = st.Commit();
        Boxes[(center, size)] = mesh;
        return mesh;
    }

    static T Cached<T>(string key, System.Func<T> make) where T : Material
    {
        if (Materials.TryGetValue(key, out var m)) return (T)m;
        T made = make();
        Materials[key] = made;
        return made;
    }

    static Texture2D BandTexture((float from, Color color)[] bands)
    {
        const int h = 256;
        var image = Image.CreateEmpty(4, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
        {
            float v = 1f - (y + 0.5f) / h;   // image rows run top-down, bands bottom-up
            Color color = bands[0].color;
            foreach (var (from, c) in bands)
                if (v >= from) color = c;
            for (int x = 0; x < 4; x++) image.SetPixel(x, y, color);
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    static Texture2D StripeTexture(Color a, Color b)
    {
        const int n = 64;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                image.SetPixel(x, y, ((x + y) / (n / 2)) % 2 == 0 ? a : b);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    static Texture2D CheckerTexture(Color a, Color b)
    {
        var image = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, a);
        image.SetPixel(1, 1, a);
        image.SetPixel(1, 0, b);
        image.SetPixel(0, 1, b);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A plain mesh instance; shorthand used all over the builders.</summary>
    public static MeshInstance3D Mesh(Mesh mesh, Material material, Vector3 position, bool shadows = true) => new()
    {
        Mesh = mesh,
        MaterialOverride = material,
        Position = position,
        CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
    };
}
