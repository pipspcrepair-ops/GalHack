using System;
using System.Collections.Generic;
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

    const float SeamBlend = 0.08f;

    /// <param name="coverage">The sky's own clouds over a map point (u, v, 0 to 1), so these are the clouds you fly through; null: the generator's.</param>
    public static GlobePaint Make(PlanetSpec spec, WorldMap map, OrbitRules.GlobeRules rules, Func<float, float, float> coverage = null)
    {
        ArchetypeLook look = ArchetypeLook.For(spec);
        ColorMap colors = ColorMap.Build(map, look, rules.TextureWidth);
        byte[] clouds = CloudMap.Build(spec, look, rules.CloudWidth, out int cloudHeight);
        for (int i = 3; i < clouds.Length; i += 4)
        {
            float alpha = clouds[i] / 255f * (float)rules.CloudOpacity;
            if (coverage != null)
            {
                int pixel = i / 4;
                float u = (pixel % rules.CloudWidth + 0.5f) / rules.CloudWidth, v = (pixel / rules.CloudWidth + 0.5f) / cloudHeight;
                float c = coverage(u, v);
                // The sky's noise doesn't wrap at the map's edge: the last stretch blends into the first.
                if (u > 1f - SeamBlend) c += (coverage(u - 1f, v) - c) * (u - (1f - SeamBlend)) / SeamBlend;
                alpha = Math.Clamp(c / 0.16f, 0f, 1f) * 0.9f;
            }
            clouds[i] = (byte)(alpha * 255f);
        }
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
/// The world below, seen from its orbit: planet-generator's globe (its planet and ring shaders, as
/// its OrbitView draws them, with air of its own) at the orbit zone's scale, centred on the zone's frame, so
/// a direction from the centre is always the same place on the map. It has no collider: a descent
/// crosses into the atmosphere well above the ground it shows.
///
/// Over the overlap band it fades in over the surface's terrain (<see cref="Fade"/>). The ground
/// and clouds use planet-generator's shaders with two additions: a fade, and a pull toward the
/// camera. The surface's terrain stands between you and the globe, so while they overlap the globe
/// is drawn pulled in along each eye ray to a few hundred metres: the same on screen, in front of
/// the terrain, still behind your vessel.
/// </summary>
public partial class Globe : Node3D
{
    const string PullVertex = """
        uniform float pull = 1.0;
        void vertex() {
            vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            world = CAMERA_POSITION_WORLD + (world - CAMERA_POSITION_WORLD) * pull;
            VERTEX = (inverse(MODEL_MATRIX) * vec4(world, 1.0)).xyz;
        }
        """;

    const string GroundSource = $$"""
        shader_type spatial;
        render_mode blend_mix, depth_draw_always, cull_back, diffuse_burley, specular_schlick_ggx;
        uniform sampler2D albedo_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D glow_map : source_color, filter_linear_mipmap, repeat_enable;
        uniform vec3 sun_dir = vec3(1.0, 0.0, 0.0);
        uniform float day_glow = 0.6;
        uniform float night_glow = 1.6;
        uniform float fade = 1.0;
        {{PullVertex}}
        void fragment() {
            vec4 a = texture(albedo_map, UV);
            ALBEDO = a.rgb;
            ROUGHNESS = mix(0.9, 0.42, a.a);
            SPECULAR = mix(0.1, 0.45, a.a);
            vec3 n = normalize(mat3(transpose(MODEL_MATRIX)) * (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
            float night = smoothstep(0.15, -0.25, dot(n, sun_dir));
            EMISSION = texture(glow_map, UV).rgb * (day_glow + night * night_glow);
            ALPHA = fade;
        }
        """;

    const string CloudSource = $$"""
        shader_type spatial;
        render_mode blend_mix, depth_draw_never, cull_back, diffuse_lambert_wrap, specular_disabled;
        uniform sampler2D cloud_map : source_color, filter_linear_mipmap, repeat_enable;
        uniform float fade = 1.0;
        {{PullVertex}}
        void fragment() {
            vec4 c = texture(cloud_map, UV);
            ALBEDO = c.rgb;
            ALPHA = c.a * fade;
        }
        """;

    /// <summary>
    /// The air seen from outside, worked out from each eye ray rather than the shell's normal (the
    /// generator's rim reads as a solid band from close to): over the limb the glow thins away with
    /// the height of the ray's lowest point, and over the disc it thins toward the middle, meeting
    /// at the ground's edge, so there is no line there and none at the top.
    /// </summary>
    const string AirSource = """
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never, cull_back;
        uniform vec3 color : source_color;
        uniform vec3 sun_dir = vec3(1.0, 0.0, 0.0);
        uniform float strength = 1.0;
        uniform float ground = 1.0;
        uniform float top = 1.1;
        void fragment() {
            vec3 centre = MODEL_MATRIX[3].xyz;
            vec3 point = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
            vec3 ray = normalize(point - CAMERA_POSITION_WORLD);
            vec3 eye = CAMERA_POSITION_WORLD - centre;
            float lowest = length(eye + ray * max(-dot(eye, ray), 0.0));
            float h = (lowest - ground) / (top - ground);
            float thick = h >= 0.0 ? exp(-4.5 * h) * clamp(1.0 - h, 0.0, 1.0) : pow(lowest / ground, 10.0);
            float day = smoothstep(-0.3, 0.35, dot(normalize(point - centre), sun_dir));
            ALBEDO = color * thick * day * strength;
        }
        """;

    /// <summary>How far off the globe's nearest point is drawn while it overlaps the terrain, m.</summary>
    const float PulledTo = 450f;

    const float AirStrength = 1.1f;

    ShaderMaterial _ground, _clouds, _air, _ringMaterial;
    MeshInstance3D _ring;
    bool _hasRing;

    public float Radius { get; private set; }

    /// <summary>Sets the globe up a piece at a time, each upload on its own frame, so none of them shows as a hitch.</summary>
    public IEnumerable<int> Setup(GlobePaint paint, float radius, float atmosphereRadius, OrbitRules.GlobeRules rules)
    {
        Radius = radius;
        ArchetypeLook look = paint.Look;
        _ground = new ShaderMaterial { Shader = new Shader { Code = GroundSource }, RenderPriority = 0 };
        _ground.SetShaderParameter("albedo_map", ImageTexture.CreateFromImage(paint.Albedo));
        yield return 0;
        _ground.SetShaderParameter("glow_map", ImageTexture.CreateFromImage(paint.Glow));
        _ground.SetShaderParameter("day_glow", look.LiquidGlow > 0 ? 0.9f : 0.5f);
        _ground.SetShaderParameter("night_glow", look.GlowingNights ? 2.5f : 1.4f);
        AddChild(Sphere("Ground", radius, rules.Segments, rules.Rings, _ground));
        yield return 0;
        _clouds = new ShaderMaterial { Shader = new Shader { Code = CloudSource }, RenderPriority = 1 };
        _clouds.SetShaderParameter("cloud_map", ImageTexture.CreateFromImage(paint.Clouds));
        AddChild(Sphere("Clouds", radius * (1f + (float)rules.CloudHeight), rules.Segments * 2 / 3, rules.Rings * 2 / 3, _clouds));
        yield return 0;
        _air = new ShaderMaterial { Shader = new Shader { Code = AirSource } };
        _air.SetShaderParameter("color", C(look.Atmosphere));
        _air.SetShaderParameter("ground", radius);
        _air.SetShaderParameter("top", atmosphereRadius);
        AddChild(Sphere("Atmosphere", atmosphereRadius, rules.Segments / 2, rules.Rings / 2, _air));
        _ringMaterial = new ShaderMaterial { Shader = Planets.Shaders.Ring };
        _ringMaterial.SetShaderParameter("color", C(look.RingColor));
        _hasRing = look.Ring;
        _ring = new MeshInstance3D { Name = "Ring", Mesh = RingMesh(radius * 1.45f, radius * 2.3f, 160), MaterialOverride = _ringMaterial, Visible = false };
        AddChild(_ring);
        Fade(0f, 0f);
    }

    /// <summary>Where the sun is, toward it: in the globe's frame for the night side's glow, in the world for the rim of air's day side.</summary>
    public void SetSun(Vector3 inGlobe, Vector3 inWorld)
    {
        _ground?.SetShaderParameter("sun_dir", inGlobe);
        _air?.SetShaderParameter("sun_dir", inWorld);
    }

    /// <summary>The globe's fade over the band: the ground and clouds (0 to 1), and the rim of air and ring, which stand clear of the ground and come in later.</summary>
    public void Fade(float ground, float rim)
    {
        if (_clouds == null || _air == null) return;
        _ground.SetShaderParameter("fade", ground);
        _clouds.SetShaderParameter("fade", ground);
        _air.SetShaderParameter("strength", AirStrength * rim);
        _ring.Visible = _hasRing && rim > 0.95f;
        Visible = ground > 0.002f;
    }

    /// <summary>Draws the globe pulled in along each eye ray, in front of the terrain (eye: where the camera is), or where it really is (null).</summary>
    public void Pull(Vector3? eye)
    {
        float pull = 1f;
        if (eye is Vector3 e)
        {
            float near = MathF.Max(1f, e.DistanceTo(GlobalPosition) - Radius);
            pull = MathF.Min(1f, PulledTo / near);
        }
        if (Mathf.IsEqualApprox(pull, _pull)) return;
        _pull = pull;
        _ground.SetShaderParameter("pull", pull);
        _clouds.SetShaderParameter("pull", pull);
    }

    float _pull = 1f;

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
