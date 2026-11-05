using Godot;
using GalHack.Structures.Presentation;

namespace BlockBuilding;

/// <summary>
/// The set: a desert under a hazy 1970s paperback sky with a ringed giant on the horizon, mesas
/// for scale, and a big concrete landing pad with hazard edging to build on.
/// </summary>
public static class World
{
    public const float PadSize = 72f, PadTop = 0.3f;

    public static void Build(Node3D root)
    {
        BuildSky(root);
        BuildGround(root);
        BuildPad(root);
    }

    static void BuildSky(Node3D root)
    {
        var environment = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ShaderMaterial { Shader = new Shader { Code = SkyCode } }, RadianceSize = Sky.RadianceSizeEnum.Size256 },
            AmbientLightSource = Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.6f,
            ReflectedLightSource = Environment.ReflectionSource.Sky,
            TonemapMode = Environment.ToneMapper.Filmic,
            TonemapExposure = 1.0f,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowBloom = 0.04f,
            GlowHdrThreshold = 1.1f,
            FogEnabled = true,
            FogLightColor = new Color("f1a874"),
            FogDensity = 0.00035f,
            FogSkyAffect = 0.0f,
            FogAerialPerspective = 0.35f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.15f,
            AdjustmentContrast = 1.08f,
        };
        root.AddChild(new WorldEnvironment { Environment = environment });

        var sun = new DirectionalLight3D
        {
            LightColor = new Color("ffe0b0"),
            LightEnergy = 1.6f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 180f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            ShadowBias = 0.04f,
        };
        root.AddChild(sun);
        // Low from behind the usual viewpoint, so the ship's port bow is lit and its shadow falls away.
        sun.Basis = Basis.LookingAt(new Vector3(0.56f, -0.58f, 0.6f).Normalized(), Vector3.Up);
    }

    static void BuildGround(Node3D root)
    {
        var ground = new StaticBody3D { Name = "Ground", CollisionLayer = PhysicsLayers.World, CollisionMask = 0 };
        ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(8000f, 2f, 8000f) }, Position = new Vector3(0, -1f, 0) });
        ground.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(8000f, 8000f) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("d58b52"),
                AlbedoTexture = new NoiseTexture2D { Noise = new FastNoiseLite { Frequency = 0.015f, FractalOctaves = 4 }, Seamless = true, Width = 512, Height = 512 },
                Uv1Triplanar = true,
                Uv1Scale = Vector3.One * 0.045f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                Roughness = 1f,
            },
        });
        root.AddChild(ground);

        // Mesas and spires: big, simple, warm. They give the eye a scale for the ship.
        var rng = new RandomNumberGenerator { Seed = 1975 };
        Color[] rock = { new("a0522d"), new("b8692e"), new("8b3a1e"), new("cf8a52"), new("9c4a2a") };
        for (int i = 0; i < 90; i++)
        {
            float angle = rng.RandfRange(0, Mathf.Tau), distance = rng.RandfRange(260f, 2200f);
            var spot = new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance);
            float width = rng.RandfRange(14f, 70f), height = rng.RandfRange(14f, 110f);
            bool spire = rng.Randf() < 0.3f;
            Vector3 size = spire ? new Vector3(width * 0.3f, height * 1.7f, width * 0.3f) : new Vector3(width, height * 0.55f, width * rng.RandfRange(0.6f, 1.5f));
            var mesa = new StaticBody3D { CollisionLayer = PhysicsLayers.World, CollisionMask = 0 };
            mesa.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            var material = new StandardMaterial3D { AlbedoColor = rock[i % rock.Length], Roughness = 1f };
            mesa.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
            // A cap stratum in a lighter band, like layered sandstone.
            mesa.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(size.X * 1.03f, size.Y * 0.12f, size.Z * 1.03f) },
                Position = new Vector3(0, size.Y * 0.38f, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = rock[(i + 3) % rock.Length].Lightened(0.15f), Roughness = 1f },
            });
            root.AddChild(mesa);
            mesa.Position = spot + Vector3.Up * size.Y * 0.5f;
            mesa.RotationDegrees = new Vector3(0, rng.RandfRange(0, 90), 0);
        }
    }

    static void BuildPad(Node3D root)
    {
        var pad = new StaticBody3D { Name = "Pad", CollisionLayer = PhysicsLayers.World, CollisionMask = 0 };
        pad.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(PadSize, PadTop + 1f, PadSize) }, Position = new Vector3(0, (PadTop - 1f) * 0.5f, 0) });
        pad.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(PadSize, PadTop + 1f, PadSize) },
            Position = new Vector3(0, (PadTop - 1f) * 0.5f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("8d877c"), Roughness = 0.95f },
        });
        pad.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(PadSize, PadSize) },
            Position = new Vector3(0, PadTop + 0.005f, 0),
            MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = PadCode } },
        });
        root.AddChild(pad);
    }

    const string PadCode = @"
shader_type spatial;
render_mode diffuse_burley;
uniform float size = 72.0;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) { vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y); }
float aa(float e, float x) { float w = fwidth(x) * 0.8; return smoothstep(e - w, e + w, x); }
void fragment() {
    vec2 m = (UV - 0.5) * size;                 // metres from the pad centre
    vec3 col = vec3(0.39, 0.375, 0.35);
    col *= 0.9 + 0.12 * noise(m * 0.7) + 0.05 * noise(m * 6.0);
    vec2 slab = abs(fract(m / 6.0) - 0.5) * 6.0;    // 6 m concrete slabs
    col *= mix(0.72, 1.0, aa(0.05, min(slab.x, slab.y)));
    float edge = size * 0.5 - max(abs(m.x), abs(m.y));
    float band = aa(0.0, edge) * (1.0 - aa(2.6, edge));
    float hazard = aa(0.5, fract((m.x + m.y) / 2.2));
    col = mix(col, mix(vec3(0.95, 0.63, 0.05), vec3(0.08, 0.08, 0.09), hazard), band);
    float r = length(m);
    float ring = aa(23.0, r) * (1.0 - aa(24.2, r));
    col = mix(col, vec3(0.93, 0.36, 0.05), ring * 0.9);
    float tick = (1.0 - aa(0.35, min(abs(m.x), abs(m.y)))) * aa(25.5, r) * (1.0 - aa(31.0, r));
    col = mix(col, vec3(0.95, 0.9, 0.78), tick * 0.85);
    col *= 0.88 + 0.12 * smoothstep(0.2, 0.8, noise(m * 0.15));   // weathering
    ALBEDO = col;
    ROUGHNESS = 0.92;
}
";

    internal const string SkyCode = @"
shader_type sky;
render_mode use_debanding;

uniform vec3 zenith : source_color = vec3(0.08, 0.24, 0.38);
uniform vec3 horizon : source_color = vec3(1.0, 0.8, 0.58);
uniform vec3 haze : source_color = vec3(1.0, 0.6, 0.38);
uniform vec3 ground : source_color = vec3(0.6, 0.32, 0.18);
uniform vec3 planet_dir = vec3(0.22, 0.36, 0.9);
uniform float planet_size = 0.21;
uniform vec3 moon_dir = vec3(0.84, 0.47, 0.25);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) { vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y); }
// Value noise that repeats every `period` cells along x, and its fbm (each octave twice the last's
// period): the cloud streaks run round the sky by azimuth and must meet themselves where it wraps.
float noise_wrap(vec2 p, float period) { vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    float x0 = mod(i.x, period), x1 = mod(i.x + 1.0, period);
    return mix(mix(hash(vec2(x0, i.y)), hash(vec2(x1, i.y)), f.x), mix(hash(vec2(x0, i.y + 1.0)), hash(vec2(x1, i.y + 1.0)), f.x), f.y); }
float fbm_wrap(vec2 p, float period) {
    return 0.5 * noise_wrap(p, period) + 0.25 * noise_wrap(p * 2.0 + vec2(0.0, 3.0), period * 2.0) + 0.125 * noise_wrap(p * 4.0 + vec2(0.0, 7.0), period * 4.0); }

void sky() {
    vec3 d = normalize(EYEDIR);
    float h = d.y;
    vec3 col = mix(horizon, zenith, pow(smoothstep(0.0, 0.9, h), 0.55));
    col = mix(col, haze, exp(-max(h, 0.0) * 10.0) * 0.6);
    // Below the horizon, from the very colour just above it down to the ground's (from the haze
    // alone, it was a hard line along the horizon seen from any height).
    if (h < 0.0) col = mix(mix(horizon, haze, 0.6), ground, smoothstep(0.0, 0.2, -h));

    // Long streaks of high cloud, the airbrushed kind: noise round the sky by azimuth, 32 cells
    // to the turn, repeating so it meets itself behind (at -Z), where the azimuth wraps, with no seam.
    float around = (atan(d.x, d.z) / 6.2831853 + 0.5) * 32.0;
    float streak = fbm_wrap(vec2(around, h * 34.0), 32.0) * smoothstep(0.02, 0.08, h) * (1.0 - smoothstep(0.16, 0.34, h));
    col = mix(col, vec3(1.0, 0.9, 0.78), smoothstep(0.45, 0.8, streak) * 0.45);

    vec3 sun = LIGHT0_ENABLED ? LIGHT0_DIRECTION : normalize(vec3(0.5, 0.6, 0.6));
    float sd = max(dot(d, sun), 0.0);
    col += vec3(1.0, 0.92, 0.75) * (pow(sd, 2200.0) * 24.0 + pow(sd, 60.0) * 0.45 + pow(sd, 6.0) * 0.12);

    // A ringed gas giant hanging over the horizon.
    vec3 pd = normalize(planet_dir);
    vec3 right = normalize(cross(vec3(0.0, 1.0, 0.0), pd));
    vec3 up = cross(pd, right);
    float along = dot(d, pd);
    float above = smoothstep(-0.01, 0.05, h);
    if (along > 0.5) {
        vec2 p = vec2(dot(d, right), dot(d, up)) / along / tan(planet_size);
        float r = length(p);
        float ca = cos(0.42), sa = sin(0.42);
        vec2 q = vec2(ca * p.x + sa * p.y, -sa * p.x + ca * p.y);
        float rr = length(vec2(q.x, q.y / 0.26));
        float ring = smoothstep(1.32, 1.36, rr) * (1.0 - smoothstep(2.3, 2.36, rr));
        ring *= 1.0 - 0.85 * smoothstep(1.76, 1.79, rr) * (1.0 - smoothstep(1.86, 1.89, rr));
        ring *= 0.6 + 0.4 * sin(rr * 41.0) * sin(rr * 9.0 + 1.0);
        vec3 ringCol = mix(vec3(0.97, 0.86, 0.66), vec3(0.88, 0.55, 0.36), smoothstep(1.4, 2.3, rr));
        float lit = 0.75 + 0.25 * dot(sun, -pd);
        if (r < 1.0) {
            float z = sqrt(1.0 - r * r);
            vec3 n = normalize(right * p.x + up * p.y - pd * z);
            float lambert = clamp(dot(n, sun) * 0.9 + 0.12, 0.0, 1.0);
            float lat = q.y + 0.04 * sin(q.x * 9.0);
            vec3 bands = mix(vec3(0.94, 0.84, 0.64), vec3(0.85, 0.45, 0.24), smoothstep(-0.2, 0.2, sin(lat * 11.0)));
            bands = mix(bands, vec3(0.2, 0.52, 0.55), smoothstep(0.55, 0.9, sin(lat * 5.0 + 1.3)) * 0.8);
            vec3 planet = bands * (0.1 + 0.95 * lambert) + haze * pow(1.0 - z, 2.5) * 0.5;
            // Rings cross in front of the lower half and hide behind the upper half.
            if (q.y < 0.0) planet = mix(planet, ringCol * lit, ring * 0.9);
            float edge = smoothstep(1.0, 0.985, r);
            col = mix(col, mix(col, planet, 0.9), edge * above);
        } else {
            col = mix(col, ringCol * lit, ring * 0.85 * above);
            col += haze * smoothstep(1.12, 1.0, r) * 0.18 * above;
        }
    }

    // A small pale moon.
    float md = dot(d, normalize(moon_dir));
    if (md > 0.9993) {
        vec3 mn = normalize(moon_dir);
        vec3 mr = normalize(cross(vec3(0.0, 1.0, 0.0), mn));
        vec2 mp = vec2(dot(d, mr), dot(d, cross(mn, mr))) / 0.0375;
        float shade = 0.55 + 0.45 * clamp(dot(normalize(vec3(mp, sqrt(max(0.0, 1.0 - dot(mp, mp))))), normalize(vec3(-0.6, 0.3, 0.7))), 0.0, 1.0);
        float crater = 0.85 + 0.15 * noise(mp * 6.0);
        col = mix(col, vec3(0.8, 0.93, 0.92) * shade * crater, smoothstep(0.9993, 0.99945, md));
    }

    COLOR = col;
}
";
}
