using System;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace Slice.Orbit;

/// <summary>
/// The globe's paint, made off the main thread from the world's own map: planet-generator's
/// colour map (albedo with glossy seas, and glow) and cloud map, as mipmapped images ready to
/// upload. Its pixel (u, v) shows the map point (u·width, v·height), which is what
/// <see cref="GlobeMap"/> assumes and the globe test checks against Godot's sphere.
/// </summary>
public sealed class GlobePaint
{
    public ArchetypeLook Look { get; private init; }
    public Image Albedo { get; private init; }
    public Image Glow { get; private init; }
    public Image Clouds { get; private init; }

    public static GlobePaint Make(PlanetSpec spec, WorldMap map, OrbitRules.GlobeRules rules)
    {
        ArchetypeLook look = ArchetypeLook.For(spec);
        ColorMap colors = ColorMap.Build(map, look, rules.TextureWidth);
        byte[] clouds = CloudMap.Build(spec, look, rules.CloudWidth, out int cloudHeight);
        for (int i = 3; i < clouds.Length; i += 4) clouds[i] = (byte)(clouds[i] * rules.CloudOpacity);
        return new GlobePaint
        {
            Look = look,
            Albedo = Mipmapped(colors.Width, colors.Height, Image.Format.Rgba8, colors.Albedo),
            Glow = Mipmapped(colors.Width, colors.Height, Image.Format.Rgb8, colors.Glow),
            Clouds = Mipmapped(rules.CloudWidth, cloudHeight, Image.Format.Rgba8, clouds),
        };
    }

    static Image Mipmapped(int width, int height, Image.Format format, byte[] data)
    {
        Image image = Image.CreateFromData(width, height, false, format, data);
        image.GenerateMipmaps();
        return image;
    }
}

/// <summary>
/// The world below, seen from its orbit: planet-generator's globe (its planet, cloud, atmosphere
/// and ring shaders, as its OrbitView draws them) at the orbit zone's scale, centred on the zone's
/// origin and fixed there, so a direction from the centre is always the same place on the map.
/// It has no collider: a descent starts at the atmosphere's top, above the ground it shows.
/// </summary>
public partial class Globe : Node3D
{
    ShaderMaterial _ground, _clouds, _air, _ringMaterial;
    MeshInstance3D _ring;

    public float Radius { get; private set; }

    public void Setup(GlobePaint paint, float radius, float atmosphereRadius, OrbitRules.GlobeRules rules)
    {
        Radius = radius;
        ArchetypeLook look = paint.Look;
        _ground = new ShaderMaterial { Shader = Planets.Shaders.Planet };
        _ground.SetShaderParameter("albedo_map", ImageTexture.CreateFromImage(paint.Albedo));
        _ground.SetShaderParameter("glow_map", ImageTexture.CreateFromImage(paint.Glow));
        _ground.SetShaderParameter("day_glow", look.LiquidGlow > 0 ? 0.9f : 0.5f);
        _ground.SetShaderParameter("night_glow", look.GlowingNights ? 2.5f : 1.4f);
        AddChild(Sphere("Ground", radius, rules.Segments, rules.Rings, _ground));

        _clouds = new ShaderMaterial { Shader = Planets.Shaders.Cloud };
        _clouds.SetShaderParameter("cloud_map", ImageTexture.CreateFromImage(paint.Clouds));
        AddChild(Sphere("Clouds", radius * (1f + (float)rules.CloudHeight), rules.Segments * 2 / 3, rules.Rings * 2 / 3, _clouds));

        _air = new ShaderMaterial { Shader = Planets.Shaders.Atmosphere };
        _air.SetShaderParameter("color", C(look.Atmosphere));
        AddChild(Sphere("Atmosphere", atmosphereRadius, rules.Segments / 2, rules.Rings / 2, _air));

        _ringMaterial = new ShaderMaterial { Shader = Planets.Shaders.Ring };
        _ringMaterial.SetShaderParameter("color", C(look.RingColor));
        _ring = new MeshInstance3D { Name = "Ring", Mesh = RingMesh(radius * 1.45f, radius * 2.3f, 160), MaterialOverride = _ringMaterial, Visible = look.Ring };
        AddChild(_ring);
    }

    /// <summary>Where the sun is (a unit direction toward it): the night side glows, the rim of air lights on the day side.</summary>
    public void SetSun(Vector3 toSun)
    {
        _ground?.SetShaderParameter("sun_dir", toSun);
        _air?.SetShaderParameter("sun_dir", toSun);
    }

    static MeshInstance3D Sphere(string name, float radius, int segments, int rings, Material material) => new()
    {
        Name = name,
        Mesh = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = segments, Rings = rings },
        MaterialOverride = material,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>A flat annulus in the equatorial plane; UV.x runs from the inner to the outer edge (as OrbitView's).</summary>
    static ArrayMesh RingMesh(float inner, float outer, int segments)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * MathF.Tau / segments, a1 = (i + 1) * MathF.Tau / segments;
            Vector3 i0 = new(MathF.Cos(a0) * inner, 0, MathF.Sin(a0) * inner), o0 = new(MathF.Cos(a0) * outer, 0, MathF.Sin(a0) * outer);
            Vector3 i1 = new(MathF.Cos(a1) * inner, 0, MathF.Sin(a1) * inner), o1 = new(MathF.Cos(a1) * outer, 0, MathF.Sin(a1) * outer);
            void V(Vector3 p, float u)
            {
                st.SetNormal(Vector3.Up);
                st.SetUV(new Vector2(u, 0));
                st.AddVertex(p);
            }
            V(i0, 0); V(o0, 1); V(o1, 1);
            V(i0, 0); V(o1, 1); V(i1, 0);
        }
        return st.Commit();
    }

    static Color C(Rgb c) => new(c.R, c.G, c.B);
}
