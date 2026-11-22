using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using Planets.Look;
using N = System.Numerics;

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

    /// <summary>The night side's lights: each lived-in place a warm speck, a town's glow a smudge (FarLights' clusters, the ground's own).</summary>
    public Image Lights { get; private init; }

    /// <summary>The ground's height over the sea, m, on the map's own grid: the globe's relief.</summary>
    public Image Relief { get; private init; }

    const float SeamBlend = 0.08f;

    /// <param name="coverage">The sky's own clouds over a map point (u, v, 0 to 1), so these are the clouds you fly through; null: the generator's.</param>
    /// <param name="land">The ground's colour at a map point, m, so the far land is the colour of the land it joins; null: the biomes'.</param>
    /// <param name="sites">The world's places, for its night lights (null: none).</param>
    public static GlobePaint Make(PlanetSpec spec, WorldMap map, OrbitRules.GlobeRules rules, Func<float, float, float> coverage = null, Func<double, double, Rgb> land = null,
        IReadOnlyList<PlanetSurface.Poi.PoiSite> sites = null)
    {
        ArchetypeLook look = ArchetypeLook.For(spec);
        ColorMap colors = ColorMap.Build(map, look, WidthFor(map.WidthMeters, rules), land);
        byte[] clouds = CloudMap.Build(spec, look, rules.CloudWidth, out int cloudHeight);
        for (int i = 3; i < clouds.Length; i += 4)
        {
            float alpha = clouds[i] / 255f * (float)rules.CloudOpacity;
            if (coverage != null)
            {
                int pixel = i / 4;
                float u = (pixel % rules.CloudWidth + 0.5f) / rules.CloudWidth, v = (pixel / rules.CloudWidth + 0.5f) / cloudHeight;
                float c = coverage(u, v);
                // The sky's noise doesn't wrap at the map's edges: the last stretch blends into the first.
                if (u > 1f - SeamBlend) c += (coverage(u - 1f, v) - c) * (u - (1f - SeamBlend)) / SeamBlend;
                if (v > 1f - SeamBlend) c += (coverage(u, v - 1f) - c) * (v - (1f - SeamBlend)) / SeamBlend;
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
            Relief = ReliefOf(map),
            Lights = LightsOf(sites, map.WidthMeters, map.HeightMeters),
        };
    }

    /// <summary>The colour map's width for a world: a texel to <see cref="OrbitRules.GlobeRules.TexelMetres"/> of its map, a power of two, as wide as the rules allow at most.</summary>
    public static int WidthFor(double mapWidth, OrbitRules.GlobeRules rules) =>
        Math.Clamp((int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Ceiling(mapWidth / rules.TexelMetres)), 1024, rules.TextureWidth);

    /// <summary>The night lights map: a soft splat for each lived-in place by its lamps, and a wider one for each town's glow.</summary>
    static Image LightsOf(IReadOnlyList<PlanetSurface.Poi.PoiSite> sites, double width, double height)
    {
        const int w = 1024;
        int h = Math.Max(1, (int)(w * height / width));
        var sum = new float[w * h];
        double perPixel = width / w;
        void Splat(double x, double z, double radius, float amount)
        {
            double r = Math.Max(radius / perPixel, 0.8);
            int cx = (int)(x / perPixel), cz = (int)(z / perPixel), reach = (int)Math.Ceiling(r * 2.5);
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                float f = (float)Math.Exp(-(dx * dx + dz * dz) / (r * r));
                if (f < 0.01f) continue;
                int px = ((cx + dx) % w + w) % w, pz = ((cz + dz) % h + h) % h; // the map wraps both ways
                sum[pz * w + px] += amount * f;
            }
        }
        if (sites != null)
        {
            foreach (PlanetSurface.Poi.PoiSite s in sites)
            {
                float lamps = PlanetSurface.Poi.FarLights.Lamps(s);
                if (lamps > 0f) Splat((s.Footprint.Min.X + s.Footprint.Max.X + 1) * 0.5, (s.Footprint.Min.Z + s.Footprint.Max.Z + 1) * 0.5, 40.0, 0.08f * MathF.Sqrt(lamps));
            }
            foreach (PlanetSurface.Poi.FarLights.Glow g in PlanetSurface.Poi.FarLights.Clusters(sites, width, height)) Splat(g.X, g.Z, g.Radius * 0.8, 0.6f * g.Strength);
        }
        var bytes = new byte[w * h * 3];
        for (int i = 0; i < sum.Length; i++)
        {
            float v = Math.Min(sum[i], 1f);
            bytes[i * 3] = (byte)(255f * v);
            bytes[i * 3 + 1] = (byte)(255f * v * 0.7f);
            bytes[i * 3 + 2] = (byte)(255f * v * 0.42f);
        }
        return Mipmapped(w, h, Image.Format.Rgb8, bytes);
    }

    static Image ReliefOf(WorldMap map)
    {
        var bytes = new byte[map.Count * 4];
        for (int k = 0; k < map.Count; k++) BitConverter.TryWriteBytes(bytes.AsSpan(k * 4), Math.Max(map.Elevation[k], 0f));
        Image image = Image.CreateFromData(map.Width, map.Height, false, Image.Format.Rf, bytes);
        image.Convert(Image.Format.Rh); // half floats filter on every card
        image.GenerateMipmaps();
        return image;
    }

    static Image Mipmapped(int width, int height, Image.Format format, byte[] data)
    {
        Image image = Image.CreateFromData(width, height, false, format, data);
        image.GenerateMipmaps();
        return image;
    }
}

/// <summary>How a globe is seen for one drawn frame: from where, the body's place and turn, its light.</summary>
public readonly record struct WorldView
{
    /// <summary>Where the eye is, in the world.</summary>
    public Vector3 Eye { get; init; }

    /// <summary>The body's centre in the world, and the turn taking its globe's frame to the world's.</summary>
    public Vector3 Centre { get; init; }
    public N.Quaternion Turn { get; init; }

    /// <summary>Toward the sun, in the world.</summary>
    public Vector3 ToSun { get; init; }

    /// <summary>What can shadow the globe now (SpaceSky.Umbrae): each body's place from its centre and radius, in its radii; how many; the sun's angular radius.</summary>
    public Vector4[] Umbra { get; init; }
    public int UmbraCount { get; init; }
    public float UmbraSun { get; init; }

    /// <summary>The cloud deck's true height over sea level, m (it is drawn as low as the relief is: see <see cref="Globe.Lie"/>).</summary>
    public float CloudHeight { get; init; }

    /// <summary>How much of the rim of air shows, 0 to 1.</summary>
    public float Rim { get; init; }
}

/// <summary>
/// A body seen from space: a true sphere at its true place and size, painted from its own map.
///
/// It is drawn as a polar grid on the sphere centred under the eye and moved with it, so its points
/// are closest where you look down, and it reaches over the limb. Each point is a direction from the
/// centre, and a direction is a place on the map (<see cref="GlobeMap"/>), except where that can't
/// be true: the map is a torus, so wrapped on the sphere its columns close to a point at each pole,
/// and over the poles a piece of the map is laid flat instead (the cap). Near you it is the flat map
/// laid out round the place under you, metre for metre, as the surface lays it out: what you see
/// under you is what you land on, at any latitude, and as you climb away it hands over to the
/// sphere's own map.
///
/// Its relief and cloud deck stand at their true heights near the ground and lie down far off (the
/// worlds are small: at full height their peaks would stand a quarter of the radius high). It writes
/// its true depth, so whatever is behind the world is hidden by it.
/// </summary>
public partial class Globe : Node3D
{
    /// <summary>The grid's geometry and map lookup, shared by the ground and the clouds.</summary>
    const string GridCode = """
        uniform vec3 eye_up = vec3(0.0, 1.0, 0.0);
        uniform vec3 eye_east = vec3(1.0, 0.0, 0.0);
        uniform vec3 eye_south = vec3(0.0, 0.0, 1.0);
        uniform vec3 map_up = vec3(0.0, 1.0, 0.0);
        uniform vec3 map_east = vec3(1.0, 0.0, 0.0);
        uniform vec3 map_south = vec3(0.0, 0.0, 1.0);
        uniform vec3 body_centre = vec3(0.0);
        uniform vec2 map_size = vec2(16000.0, 8000.0);
        uniform vec2 nadir = vec2(0.0);
        uniform float radius = 2546.0;
        uniform float height = 1000.0;
        uniform float reach = 4000.0;
        uniform float local = 0.0;
        uniform float map_per_metre = 1.0; // map metres to a metre on the sphere: the globe's scale against its map
        varying vec2 offset;

        // One point of the globe on the map: the sphere's own look, and up to two others over it.
        struct MapPoint {
            vec2 uv;
            vec2 cap_uv;
            vec2 under_uv;
            float cap;
            float under;
        };

        // o: metres east and south of the point under you, along the sphere.
        MapPoint map_point(vec2 o) {
            float s = max(length(o), 0.01);
            float a = s / radius;
            vec3 d = map_up * cos(a) + (map_east * o.x + map_south * o.y) * sin(a) / s;
            MapPoint m;
            m.uv = vec2(atan(d.x, d.z) / 6.2831853, acos(clamp(d.y, -1.0, 1.0)) / 3.1415927);
            // Near you: the flat map round the place under you, as the surface has it.
            m.under_uv = (nadir + o * map_per_metre) / map_size;
            m.under = local > 0.0 ? 1.0 - smoothstep(local * 0.7, local, s) : 0.0;
            m.cap = smoothstep(0.80, 0.93, abs(d.y)) * (1.0 - m.under);
            m.cap_uv = (d.y > 0.0 ? vec2(0.25) : vec2(0.75)) + vec2(d.x, d.z) * radius * map_per_metre / map_size;
            return m;
        }

        // Metres along the sphere a grid ring stands from the point under you.
        float arc(float t) { return reach * (exp(6.0 * t) - 1.0) / 402.43; }

        // The sphere's normal at arc s from under you on bearing t, in the world.
        vec3 sphere_normal(float s, vec3 t) {
            float a = s / radius;
            return eye_up * cos(a) + t * sin(a);
        }
        """;

    /// <summary>A map as the globe shows it at a point, filtered by how the lookup really moves across the screen.</summary>
    const string Paint = """
        vec4 paint(sampler2D map, MapPoint m) {
            vec2 dx = dFdx(m.uv), dy = dFdy(m.uv);
            dx -= round(dx); // over the map's edge uv jumps a whole turn
            dy -= round(dy);
            vec2 cx = dFdx(m.cap_uv), cy = dFdy(m.cap_uv), ux = dFdx(m.under_uv), uy = dFdy(m.under_uv);
            vec4 c = m.under < 1.0 ? textureGrad(map, m.uv, dx, dy) : vec4(0.0);
            if (m.cap > 0.0) c = mix(c, textureGrad(map, m.cap_uv, cx, cy), m.cap);
            if (m.under > 0.0) c = mix(c, textureGrad(map, m.under_uv, ux, uy), m.under);
            return c;
        }
        """;

    /// <summary>
    /// Depth for what is drawn by its rays rather than where its mesh is (the air's shell, the ring):
    /// each pixel's depth is where its ray meets that sphere round <c>globe_centre</c>.
    /// </summary>
    const string TrueDepthCode = """
        uniform float true_depth = 0.0;
        uniform vec3 globe_centre = vec3(0.0);
        float depth_of(vec3 world, mat4 view, mat4 projection) {
            vec4 c = projection * view * vec4(world, 1.0);
            return c.z / c.w;
        }
        // Where a ray from o along d first meets a sphere round the globe's centre, or -1.
        float sphere_hit(vec3 o, vec3 d, float r) {
            vec3 oc = o - globe_centre;
            float b = dot(oc, d), h = b * b - dot(oc, oc) + r * r;
            if (h < 0.0) return -1.0;
            float t = -b - sqrt(h);
            return t > 0.0 ? t : -1.0;
        }
        """;


    /// <summary>
    /// A moon's shadow on the globe: at each point the share of the sun the bodies in front of it
    /// hide there (two discs' overlap, StarSystem.Covered's sum), so a dark umbra with a soft
    /// penumbra round it, moving as they move.
    /// </summary>
    internal const string UmbraCode = """
        uniform vec4 umbra[4];
        uniform int umbra_count = 0;
        uniform float umbra_sun = 0.0;
        float sun_hidden(float a, float b, float c) {
            if (a <= 0.0 || b <= 0.0 || c >= a + b) return 0.0;
            if (c <= abs(b - a)) return b >= a ? 1.0 : b * b / (a * a);
            float a2 = a * a, b2 = b * b, c2 = c * c;
            float lens = a2 * acos(clamp((c2 + a2 - b2) / (2.0 * c * a), -1.0, 1.0)) + b2 * acos(clamp((c2 + b2 - a2) / (2.0 * c * b), -1.0, 1.0))
                       - 0.5 * sqrt(max(0.0, (-c + a + b) * (c + a - b) * (c - a + b) * (c + a + b)));
            return clamp(lens / (3.14159265 * a2), 0.0, 1.0);
        }
        // The sunlight reaching a point of the globe (in its radii from its centre), 0 to 1.
        float sunlight_at(vec3 g, vec3 to_sun) {
            float open = 1.0;
            for (int i = 0; i < umbra_count; i++) {
                vec3 v = umbra[i].xyz - g;
                float dist = length(v);
                if (dot(v, to_sun) <= 0.0) continue;
                float size = asin(min(umbra[i].w / dist, 1.0));
                float apart = atan(length(cross(v, to_sun)), dot(v, to_sun));
                open *= 1.0 - sun_hidden(umbra_sun, size, apart);
            }
            return open;
        }
        """;


    const string GroundSource = $$"""
        shader_type spatial;
        render_mode world_vertex_coords, cull_back, diffuse_burley, specular_schlick_ggx, shadows_disabled, fog_disabled;
        uniform sampler2D albedo_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D glow_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D lights_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform float lights_gain = 3.0;
        uniform sampler2D relief_map : filter_linear_mipmap, repeat_enable;
        uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);
        uniform float day_glow = 0.6;
        uniform float night_glow = 1.6;
        uniform float relief = 1.0;
        uniform sampler2D grain : filter_linear_mipmap, repeat_enable;
        {{GridCode}}
        {{Paint}}
        {{UmbraCode}}
        varying vec3 round_normal;

        float ground_at(vec2 o, float lod) {
            MapPoint m = map_point(o);
            float e = m.under < 1.0 ? textureLod(relief_map, m.uv, lod).r : 0.0;
            if (m.cap > 0.0) e = mix(e, textureLod(relief_map, m.cap_uv, lod).r, m.cap);
            if (m.under > 0.0) e = mix(e, textureLod(relief_map, m.under_uv, lod).r, m.under);
            return e * relief;
        }

        void vertex() {
            float s = arc(VERTEX.y);
            vec3 t = eye_east * VERTEX.x + eye_south * VERTEX.z;
            offset = VERTEX.xz * s;
            // The relief at the grid's own spacing there, or it stands up as spikes.
            float step_m = max(32.0, s * 0.0375);
            float lod = log2(max(step_m * map_per_metre / 32.0, 1.0));
            // Past the limb the relief lies down: there the grid's few bearings would stand hills up
            // over it as a fence of posts, a bearing apart.
            float limb = radius * acos(radius / (radius + max(height, 1.0)));
            float beyond = 1.0 - smoothstep(0.0, 2000.0, s - limb);
            float e = ground_at(offset, lod) * beyond;
            float ex = ground_at(offset + vec2(step_m, 0.0), lod) * beyond;
            float ez = ground_at(offset + vec2(0.0, step_m), lod) * beyond;
            vec3 n = sphere_normal(s, t);
            round_normal = n;
            // East and south carried out along the great circle from under you: the bearing turns
            // down with the sphere, the way across it stays as it was.
            float a = s / radius;
            vec3 along = t * cos(a) - eye_up * sin(a), across = cross(eye_up, t);
            vec3 east = along * dot(eye_east, t) + across * dot(eye_east, across);
            vec3 south = along * dot(eye_south, t) + across * dot(eye_south, across);
            VERTEX = body_centre + n * (radius + e);
            NORMAL = normalize(n - (east * (ex - e) + south * (ez - e)) / step_m);
        }
        void fragment() {
            MapPoint m = map_point(offset);
            vec4 a = paint(albedo_map, m);
            vec3 rn = normalize(round_normal);
            // The ground's own grain, finer than its map holds (the land's patches of growth, scree and soil),
            // at two scales, each fading out as a pixel comes to cover it, so it never shimmers.
            vec2 at = (m.under > 0.5 ? m.under_uv : m.uv) * map_size;
            float metres = length(fwidth(at));
            vec2 fine = at / 23.0, broad = at / 97.0;
            float g = (textureGrad(grain, fine, dFdx(fine), dFdy(fine)).r - 0.5) * (1.0 - smoothstep(3.0, 20.0, metres))
                + (textureGrad(grain, broad, dFdx(broad), dFdy(broad)).r - 0.5) * (1.0 - smoothstep(12.0, 80.0, metres));
            ALBEDO = a.rgb * (1.0 + 0.4 * g * (1.0 - a.a)) * sunlight_at(rn, sun_dir);
            ROUGHNESS = mix(0.9, 0.42, a.a);
            SPECULAR = mix(0.1, 0.45, a.a);
            float night = smoothstep(0.15, -0.25, dot(rn, sun_dir));
            EMISSION = paint(glow_map, m).rgb * (day_glow + night * night_glow);
            // The places' lights, only after dark: the same towns' glow seen from the ground.
            EMISSION += paint(lights_map, m).rgb * smoothstep(0.05, -0.2, dot(rn, sun_dir)) * lights_gain;
        }
        """;

    /// <summary>
    /// The clouds: the sky's own cloud noise, drifted as the sky drifts it, under the live weather
    /// (<see cref="GlobeWeather"/>), worked out with the sky's sums laid flat (sky.gdshader's
    /// cloud_density), so a place's clouds from orbit are the ones over it on the ground: its region's
    /// cover, a front's deck behind its edge and the open air ahead with its anvils' sheet up high,
    /// the storm cells' towers and anvils standing up bright, dust, blowing snow and fog low under
    /// them, and lightning flickering in the storms and flashing where strikes come down, which shows
    /// on the night side. Until the weather is bound it is the paint's own cloud map.
    /// </summary>
    const string CloudSource = $$"""
        shader_type spatial;
        render_mode world_vertex_coords, blend_mix, depth_draw_never, cull_disabled, diffuse_lambert_wrap, specular_disabled, shadows_disabled, fog_disabled;
        uniform sampler2D cloud_map : source_color, filter_linear_mipmap, repeat_enable;
        uniform float cloud_lift = 30.0;
        uniform float live = 0.0;
        uniform sampler2D cloud_noise : filter_linear_mipmap, repeat_enable;
        uniform vec2 cloud_scale = vec2(8000.0);
        uniform vec2 cloud_drift = vec2(0.0);
        uniform sampler2D weather_was : filter_linear, repeat_enable;
        uniform sampler2D weather_now : filter_linear, repeat_enable;
        uniform sampler2D haze_was : filter_linear, repeat_enable;
        uniform sampler2D haze_now : filter_linear, repeat_enable;
        uniform float weather_blend = 1.0;
        uniform vec3 cloud_color : source_color = vec3(1.0);
        uniform vec3 dust_color : source_color = vec3(0.61, 0.49, 0.35);
        uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);
        uniform float clock = 0.0;
        uniform vec4 strikes[4];
        uniform float tower_lift = 16.0;
        {{GridCode}}
        {{Paint}}
        {{UmbraCode}}
        varying vec3 round_normal;

        struct Cloud { vec3 color; float alpha; float top; float storm; float lightning; float billow; };

        // The weather layer is coarse: read it smoothly (a cubic B-spline from four bilinear taps), so
        // neither its texels nor the light on its towers show.
        vec4 smooth_tap(sampler2D map, vec2 uv) {
            vec2 size = vec2(textureSize(map, 0));
            vec2 t = uv * size - 0.5, f = fract(t);
            t -= f;
            vec2 f2 = f * f, f3 = f2 * f;
            vec2 w0 = (1.0 - 3.0 * f + 3.0 * f2 - f3) / 6.0, w1 = (4.0 - 6.0 * f2 + 3.0 * f3) / 6.0;
            vec2 w2 = (1.0 + 3.0 * f + 3.0 * f2 - 3.0 * f3) / 6.0, w3 = f3 / 6.0;
            vec2 s0 = w0 + w1, s1 = w2 + w3;
            vec2 c0 = (t - 0.5 + w1 / s0) / size, c1 = (t + 1.5 + w3 / s1) / size;
            return (textureLod(map, vec2(c0.x, c0.y), 0.0) * s0.x + textureLod(map, vec2(c1.x, c0.y), 0.0) * s1.x) * s0.y
                + (textureLod(map, vec2(c0.x, c1.y), 0.0) * s0.x + textureLod(map, vec2(c1.x, c1.y), 0.0) * s1.x) * s1.y;
        }

        float noise_at(vec2 uv, vec2 dx, vec2 dy) { return textureGrad(cloud_noise, uv, dx, dy).r; }

        Cloud cloud_at(vec2 uv, vec2 dx, vec2 dy) {
            vec2 k = map_size / cloud_scale;
            vec2 q = (uv * map_size + cloud_drift) / cloud_scale, qx = dx * k, qy = dy * k;
            // The weather is read a little off where it is, by the clouds' own noise, so nothing it holds
            // has a clean edge: a front's edge, a storm's rim, one region's weather against the next, all
            // feather out into broken cloud a few hundred metres either way.
            // Every noise here is read at a whole number of its tiles to the map's, so it repeats exactly
            // round the globe and from pole to pole: the map's edge (u = ±0.5 in the lookup) never shows.
            vec2 warp = vec2(noise_at(q * 3.0 + 0.13, qx * 3.0, qy * 3.0), noise_at(q * 3.0 + 0.61, qx * 3.0, qy * 3.0)) - 0.5;
            warp += (vec2(noise_at(q * 9.0 + 0.37, qx * 9.0, qy * 9.0), noise_at(q * 9.0 + 0.83, qx * 9.0, qy * 9.0)) - 0.5) * 0.6;
            vec2 wuv = uv + warp * 900.0 / map_size;
            vec4 w = mix(smooth_tap(weather_was, wuv), smooth_tap(weather_now, wuv), weather_blend);
            vec4 hz = mix(smooth_tap(haze_was, wuv), smooth_tap(haze_now, wuv), weather_blend);
            // The sky's coverage (cloud_density laid flat): its noise, its cover, what the front and the cells add.
            float n = noise_at(q, qx, qy) * 0.65 + noise_at(q * 2.0 + 0.31, qx * 2.0, qy * 2.0) * 0.35;
            float extra = w.g * 2.0 - 1.0;
            float coverage = n - (1.0 - mix(0.12, 1.05, w.r)) + extra;
            // Its erosion into billows and rags (the sky's detail noise, at a kilometre and a third of one).
            float detail = noise_at(q * 8.0 + 0.11, qx * 8.0, qy * 8.0) * 0.65 + noise_at(q * 24.0 + 0.47, qx * 24.0, qy * 24.0) * 0.35;
            float sheet = w.a * 2.0 - 1.0, trail = max(-sheet, 0.0);
            coverage -= (detail - 0.45) * (0.7 + 0.5 * trail) * (1.0 - w.r * 0.4);
            // A storm's tower in cauliflower lobes, at its own scale (storm_at's lump).
            float lump = noise_at(q * 4.0, qx * 4.0, qy * 4.0) * 0.6 + noise_at(q * 8.0 + 0.2, qx * 8.0, qy * 8.0) * 0.4;
            float tower = w.b * smoothstep(0.0, 0.3, w.b + (lump - 0.55) * 0.8);
            // How thick the cloud is: thin cloud lets the ground through, thick cloud doesn't; a front's deck
            // trailing far behind its edge is a thinner grey sheet.
            float thick = max(coverage, 0.0) * (1.0 - 0.6 * trail) + tower * (0.6 + 0.8 * lump);
            float a = (1.0 - exp(-3.0 * thick)) * 0.96;
            // The anvils' thin sheet spread ahead of a front, up high: soft, combed out, never solid.
            float comb = noise_at(q * vec2(1.0, 3.0) + 0.21, qx * 3.0, qy * 3.0) * 0.6 + noise_at(q + 0.57, qx, qy) * 0.4;
            float veil = max(sheet, 0.0) * 0.3 * smoothstep(0.3, 0.7, comb);
            a = a + veil * (1.0 - a);
            vec3 col = cloud_color * mix(0.74, 1.0, smoothstep(0.0, 0.9, thick)) * (1.0 - 0.22 * trail) * (1.0 + 0.18 * tower);
            // Low under the clouds: dust lifted off dry ground, blowing snow on cold ground, fog in damp low ground.
            float rag = smoothstep(0.2, 0.75, detail * 0.6 + n * 0.4);
            float dust = hz.r * 0.75 * (0.55 + 0.45 * rag), snow = hz.g * 0.55 * (0.5 + 0.5 * rag), fog = hz.b * 0.7 * smoothstep(0.25, 0.7, detail);
            float low = max(dust, max(snow, fog));
            // Dust a shade darker and redder than the dry ground it lifts from, so it shows over it.
            vec3 low_col = (dust_color * vec3(0.82, 0.7, 0.58) * (0.8 + 0.3 * rag) * dust + vec3(0.92, 0.94, 0.98) * snow + vec3(0.74, 0.76, 0.78) * fog) / max(dust + snow + fog, 1e-3);
            float alpha = a + low * (1.0 - a);
            Cloud c;
            c.color = (col * a + low_col * low * (1.0 - a)) / max(alpha, 1e-4);
            c.alpha = alpha;
            // The tops: thick cloud stands higher, lumpy with its own billows; the towers well over the deck.
            c.top = min(thick, 1.6) * (0.5 + 0.3 * n + 0.2 * lump) * (1.0 - 0.4 * trail) + tower * (1.2 + 1.8 * lump) + (dust + snow) * (0.2 + 0.4 * rag) * (1.0 - a);
            c.storm = max(tower, smoothstep(0.9, 1.6, thick) * step(0.15, w.r));
            c.lightning = hz.a * 12.0;
            c.billow = detail;
            return c;
        }

        Cloud blend(Cloud a, Cloud b, float t) {
            Cloud c;
            c.alpha = mix(a.alpha, b.alpha, t);
            c.color = (a.color * a.alpha * (1.0 - t) + b.color * b.alpha * t) / max(c.alpha, 1e-4);
            c.top = mix(a.top, b.top, t);
            c.storm = mix(a.storm, b.storm, t);
            c.lightning = mix(a.lightning, b.lightning, t);
            c.billow = mix(a.billow, b.billow, t);
            return c;
        }

        float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

        // Lightning inside the storms: patches of cloud lit from within for a moment, as often as the
        // weather there strikes, and a bright flash where a strike has just come down.
        float flashes(vec2 xz, float rate, float storm, float billow) {
            float g = 0.0;
            if (rate * storm > 0.02) {
                // Patches a whole number of them to the map, so they too repeat round the globe.
                vec2 cells = max(floor(map_size / 320.0 + 0.5), vec2(1.0));
                vec2 size = map_size / cells;
                vec2 base = floor(xz / size);
                for (int j = -1; j <= 1; j++) {
                    for (int i = -1; i <= 1; i++) {
                        vec2 c = base + vec2(float(i), float(j)), k = mod(c, cells);
                        float t = clock * 3.0 + hash(k) * 17.0;
                        float tick = floor(t);
                        float on = step(hash(k + tick * vec2(1.73, 9.21)), rate / 60.0 / 3.0 * 0.6);
                        vec2 centre = (c + 0.2 + 0.6 * vec2(hash(k + 3.1), hash(k + 7.7))) * size;
                        // Ragged, not round: the glow reaches further through the thicker billows.
                        float spread = size.x * (0.45 + 0.9 * billow);
                        g += on * exp(-fract(t) * 7.0) * smoothstep(spread, 0.0, length(xz - centre));
                    }
                }
                g *= smoothstep(0.05, 0.4, storm);
            }
            for (int i = 0; i < 4; i++) {
                float age = clock - strikes[i].z;
                if (strikes[i].w <= 0.0 || age < 0.0 || age > 1.2) continue;
                vec2 d = xz - strikes[i].xy;
                d -= map_size * round(d / map_size);
                g += 1.2 * strikes[i].w * exp(-age * 7.0) * smoothstep(250.0 + 400.0 * billow, 0.0, length(d));
            }
            // Lit from within: the billows, not a disc.
            return g * (0.15 + 0.85 * smoothstep(0.35, 0.8, billow));
        }


        void vertex() {
            float s = arc(VERTEX.y);
            vec3 t = eye_east * VERTEX.x + eye_south * VERTEX.z;
            offset = VERTEX.xz * s;
            vec3 n = sphere_normal(s, t);
            round_normal = n;
            VERTEX = body_centre + n * (radius + cloud_lift);
            NORMAL = n;
        }
        void fragment() {
            MapPoint m = map_point(offset);
            Cloud c;
            if (live < 0.5) {
                vec4 base = paint(cloud_map, m);
                c.color = base.rgb; c.alpha = base.a; c.top = 0.0; c.storm = 0.0; c.lightning = 0.0; c.billow = 0.0;
            } else {
                vec2 dx = dFdx(m.uv), dy = dFdy(m.uv);
                dx -= round(dx);
                dy -= round(dy);
                vec2 cx = dFdx(m.cap_uv), cy = dFdy(m.cap_uv), ux = dFdx(m.under_uv), uy = dFdy(m.under_uv);
                c.color = vec3(1.0); c.alpha = 0.0; c.top = 0.0; c.storm = 0.0; c.lightning = 0.0; c.billow = 0.0;
                if (m.under < 1.0) c = cloud_at(m.uv, dx, dy);
                if (m.cap > 0.0) c = blend(c, cloud_at(m.cap_uv, cx, cy), m.cap);
                if (m.under > 0.0) c = blend(c, cloud_at(m.under_uv, ux, uy), m.under);
            }
            ALBEDO = c.color;
            // The storms stand up out of the deck: their tops lit on the sun's side, shaded on the other.
            // Seen edge on, near the limb, relief from screen-space slopes only streaks: it lies down there.
            float facing = abs(dot(normalize(NORMAL), normalize(VERTEX)));
            float h = c.top * tower_lift * smoothstep(0.15, 0.45, facing);
            vec3 dpx = dFdx(VERTEX), dpy = dFdy(VERTEX);
            vec3 r1 = cross(dpy, NORMAL), r2 = cross(NORMAL, dpx);
            float det = dot(dpx, r1);
            vec3 grad = sign(det) * (dFdx(h) * r1 + dFdy(h) * r2);
            NORMAL = normalize(abs(det) * NORMAL - grad);
            vec3 rn = normalize(round_normal);
            float night = smoothstep(0.1, -0.2, dot(rn, sun_dir));
            vec2 xz = (m.under > 0.5 ? m.under_uv : m.uv) * map_size;
            float flash = live > 0.5 ? flashes(xz, c.lightning, c.storm, c.billow) : 0.0;
            EMISSION = vec3(0.72, 0.78, 1.0) * flash * 1.6 * night * c.alpha;
            // Lit softly round the terminator, and dark past it: the night side's clouds are seen by their lightning.
            ALBEDO *= 1.0 - 0.7 * night;
            // An eclipse: a moon's shadow on the globe dims what lies under it.
            ALBEDO *= sunlight_at(rn, sun_dir);
            ROUGHNESS = 0.4;
            ALPHA = c.alpha;
        }
        """;

    /// <summary>The clouds' shader code (tests read its noise scales).</summary>
    public static string CloudShader => CloudSource;

    /// <summary>
    /// The air seen from outside, worked out from each eye ray against the world's sphere: over the
    /// limb the glow thins away with the height of the ray's lowest point, over the disc it thins
    /// toward the middle, meeting at the ground's edge.
    /// </summary>
    const string AirSource = $$"""
        shader_type spatial;
        render_mode world_vertex_coords, unshaded, blend_add, depth_draw_never, cull_front, fog_disabled, shadows_disabled;
        uniform vec3 color : source_color;
        uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);
        uniform vec3 eye_up = vec3(0.0, 1.0, 0.0);
        uniform float strength = 1.0;
        uniform float ground = 1.0;
        uniform float thickness = 0.1;
        uniform float eye_height = 1.0;
        uniform float shell = 15000.0;
        uniform float air_radius = 1.0;
        varying vec3 ray;
        {{TrueDepthCode}}
        void vertex() {
            ray = VERTEX;
            VERTEX = CAMERA_POSITION_WORLD + VERTEX * shell;
        }
        void fragment() {
            vec3 r = normalize(ray);
            float hit = true_depth > 0.5 ? sphere_hit(CAMERA_POSITION_WORLD, r, air_radius) : -1.0;
            DEPTH = hit > 0.0 ? depth_of(CAMERA_POSITION_WORLD + r * hit, VIEW_MATRIX, PROJECTION_MATRIX) : FRAGCOORD.z;
            vec3 eye = eye_up * (ground + eye_height);
            float along = max(-dot(eye, r), 0.0);
            vec3 low = eye + r * along;
            float lowest = length(low);
            float h = (lowest - ground) / thickness;
            // Past the limb it thins away with height; over the ground, with how steeply the ray goes down through the air.
            float x = lowest / ground;
            float graze = sqrt(2.0 * thickness / ground) / max(sqrt(max(1.0 - x * x, 0.0)), 0.0001);
            float thick = h >= 0.0 ? exp(-4.5 * h) * clamp(1.0 - h, 0.0, 1.0) : min(graze * graze, 1.0) * x * x;
            // Lit as the ground is where the ray meets it (the point it passes lowest is inside the world, and its light
            // there would end at a straight line across the disc); past the limb, where it passes lowest.
            vec3 lit = h >= 0.0 ? low : eye + r * (along - sqrt(max(ground * ground - lowest * lowest, 0.0)));
            float day = smoothstep(-0.3, 0.35, dot(normalize(lit), sun_dir));
            ALBEDO = color * thick * day * strength;
        }
        """;

    /// <summary>
    /// The world's ring round its true globe. Every pixel works out its own ray against the ring's
    /// plane, so the mesh only covers the ring, and the globe hides the far half of it behind the
    /// world. It writes no depth (everything behind it would be cut out at its rim, gaps and all), but
    /// is tested at the depth of its plane, so what stands in front of it stands in front.
    /// </summary>
    const string RingSource = $$"""
        shader_type spatial;
        render_mode world_vertex_coords, unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
        uniform vec3 color : source_color;
        uniform vec3 axis = vec3(0.0, 1.0, 0.0);
        uniform vec3 to_sun = vec3(0.0, 1.0, 0.0);
        uniform float ring_radius = 2546.0;
        {{TrueDepthCode}}
        void fragment() {
            vec3 ro = CAMERA_POSITION_WORLD, rd = normalize(mat3(INV_VIEW_MATRIX) * VERTEX);
            float denom = dot(rd, axis);
            float t = dot(globe_centre - ro, axis) / (abs(denom) > 1e-5 ? denom : 1e-5);
            if (t <= 0.0) discard;
            float r = (length(ro + rd * t - globe_centre) / ring_radius - 1.45) / 0.85; // 0 at the inner edge, 1 at the outer
            if (r <= 0.0 || r >= 1.0) discard;
            float globe = sphere_hit(ro, rd, ring_radius);
            if (globe > 0.0 && globe < t) discard;
            float bands = 0.55 + 0.25 * sin(r * 61.0) + 0.2 * sin(r * 23.0 + 1.0);
            float gap = smoothstep(0.0, 0.02, abs(r - 0.62)) * smoothstep(0.0, 0.05, r) * smoothstep(1.0, 0.94, r);
            ALBEDO = color * (0.45 + 0.55 * clamp(bands, 0.0, 1.0)) * (0.8 + 0.3 * bands) * (0.35 + 0.65 * clamp(abs(dot(axis, to_sun)) * 1.5, 0.0, 1.0));
            ALPHA = gap * mix(0.95, 1.0, clamp(bands, 0.0, 1.0));
            DEPTH = depth_of(ro + rd * t, VIEW_MATRIX, PROJECTION_MATRIX);
        }
        """;

    /// <summary>The ring's edges, in the globe's radii.</summary>
    public const float RingInner = 1.45f, RingOuter = 2.3f;

    const float AirStrength = 1.1f;

    /// <summary>Under this height the relief and the cloud deck stand at their true heights; they lie down to the far share over three radii more, m.</summary>
    const float TrueHeightUnder = 1500f;

    ShaderMaterial _ground, _clouds, _air, _ringMaterial;
    ShaderMaterial[] _grids, _byRay; // the ground and clouds, laid on the grid; the air and the ring, worked out by each ray
    MeshInstance3D _airMesh, _ring;
    bool _hasRing;
    float _airThickness, _reliefFar;
    GlobeMap _map;
    Basis _ringBasis = Basis.Identity;

    /// <summary>Not drawn at all (screenshots of other bodies, from far from here).</summary>
    public bool Hidden;

    public float Radius { get; private set; }

    /// <summary>The live weather the clouds are drawn from.</summary>
    public GlobeWeather Weather { get; private set; }

    /// <summary>The globe's angular radius when last drawn, and the eye it was drawn for (tests: its true size).</summary>
    public float Angular { get; private set; }
    public Vector3 DrawnFrom { get; private set; }

    /// <summary>Its colour map's width in texels, as uploaded (tests: no low-detail skin), and the way to the sun it was last lit from.</summary>
    public int Detail { get; private set; }
    public Vector3 LitFrom { get; private set; }

    /// <summary>Sets the globe up a piece at a time, each upload on its own frame, so none of them shows as a hitch.</summary>
    public IEnumerable<int> Setup(GlobePaint paint, float radius, float atmosphereRadius, OrbitRules.GlobeRules rules, GlobeMap map)
    {
        Radius = radius;
        _map = map;
        _airThickness = atmosphereRadius - radius;
        _reliefFar = (float)rules.ReliefFar;
        var mapSize = new Vector2((float)map.Width, (float)map.Height);
        ArchetypeLook look = paint.Look;
        ArrayMesh grid = PolarGrid(rules.Rings, rules.Segments);
        _ground = new ShaderMaterial { Shader = new Shader { Code = GroundSource } };
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("albedo_map"), ImageTexture.CreateFromImage(paint.Albedo));
        Detail = paint.Albedo.GetWidth();
        yield return 0;
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("glow_map"), ImageTexture.CreateFromImage(paint.Glow));
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("relief_map"), ImageTexture.CreateFromImage(paint.Relief));
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("lights_map"), ImageTexture.CreateFromImage(paint.Lights));
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("grain"), Grain);
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("day_glow"), look.LiquidGlow > 0 ? 0.9f : 0.5f);
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("night_glow"), look.GlowingNights ? 2.5f : 1.4f);
        AddChild(Instance("Ground", grid, _ground));
        yield return 0;
        _clouds = new ShaderMaterial { Shader = new Shader { Code = CloudSource } };
        _clouds.SetShaderParameter(PlanetSurface.Fx.Names.Of("cloud_map"), ImageTexture.CreateFromImage(paint.Clouds));
        Weather = new GlobeWeather(mapSize);
        Weather.Bind(_clouds, C(look.CloudColor));
        AddChild(Instance("Clouds", grid, _clouds));
        _grids = new[] { _ground, _clouds };
        foreach (ShaderMaterial m in _grids)
        {
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("radius"), radius);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("map_size"), mapSize);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("map_per_metre"), (float)(map.Width / (Math.Tau * radius)));
        }
        yield return 0;
        (_airMesh, _air) = AirMesh(C(look.Atmosphere));
        _air.SetShaderParameter(PlanetSurface.Fx.Names.Of("true_depth"), 1f);
        _air.SetShaderParameter(PlanetSurface.Fx.Names.Of("air_radius"), atmosphereRadius);
        AddChild(_airMesh);
        _ringMaterial = new ShaderMaterial { Shader = new Shader { Code = RingSource } };
        _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("color"), C(look.RingColor));
        _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("ring_radius"), radius);
        _hasRing = look.Ring;
        // The mesh only covers the ring: a shade wider than it, so its straight sides never cut the round edge.
        _ring = new MeshInstance3D { Name = "Ring", Mesh = RingMesh(radius * (RingInner - 0.02f), radius * (RingOuter + 0.03f), 160), MaterialOverride = _ringMaterial, Visible = _hasRing, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_ring);
        _byRay = new[] { _air, _ringMaterial };
        Visible = false;
    }

    /// <summary>How far along the sphere the grid reaches, m: out to the limb and a little past it, for the back of the sphere is never in view.</summary>
    public static float RimOf(float radius, float height) => radius * MathF.Acos(radius / (radius + height + 700f)) * 1.04f;

    /// <summary>How far off an eye at a height is, 0 to 1: near the ground (what you land on, at its true heights, seen through little air) to far off.</summary>
    float Away(float height) => Smooth((height - TrueHeightUnder) / (3f * Radius));

    /// <summary>How far the relief and cloud deck stand up at a height, 0 to 1: all of it near the ground, the far share far off.</summary>
    public float Lie(float height) => Mathf.Lerp(1f, _reliefFar, Away(height));

    static readonly Aabb Everywhere = new(new Vector3(-1e6f, -1e6f, -1e6f), new Vector3(2e6f, 2e6f, 2e6f));

    /// <summary>The ground's grain, one tile for every globe (generated, seamless).</summary>
    static readonly NoiseTexture2D Grain = new()
    {
        Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true,
        Noise = new FastNoiseLite { Seed = 11, Frequency = 0.02f, FractalOctaves = 4, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex },
    };

    /// <summary>The ring stands in the globe's frame.</summary>
    public void PlaceRing(Transform3D globe)
    {
        _ringBasis = globe.Basis;
    }

    /// <summary>Lays the globe out for this frame, under the eye.</summary>
    public void Draw(in WorldView v)
    {
        if (_byRay == null) return; // still being set up
        Visible = !Hidden;
        if (!Visible) return;
        N.Vector3 q = N.Vector3.Transform((v.Eye - v.Centre).ToNumerics(), N.Quaternion.Conjugate(v.Turn));
        float distance = q.Length(), height = MathF.Max(distance - Radius, 1f);
        (Angular, DrawnFrom, LitFrom) = (MathF.Asin(MathF.Min(Radius / distance, 1f)), v.Eye, v.ToSun);
        var (gx, gz) = _map.MapPoint(q);
        N.Quaternion map = _map.Frame(gx, gz), frame = N.Quaternion.Normalize(v.Turn * map);
        float lie = Lie(height);
        // The flat map under you reaches as far as the land you'd see from there, and none of it from far off: there the globe is its own map.
        float local = MathF.Min(1.6f * height, 0.6f * Radius) * (1f - Smooth((height - Radius) / (2f * Radius)));
        foreach (ShaderMaterial m in _grids)
        {
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("eye_up"), MathX.Rotate(frame, N.Vector3.UnitY).ToGodot());
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("eye_east"), MathX.Rotate(frame, N.Vector3.UnitX).ToGodot());
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("eye_south"), MathX.Rotate(frame, N.Vector3.UnitZ).ToGodot());
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("map_up"), MathX.Rotate(map, N.Vector3.UnitY).ToGodot());
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("map_east"), MathX.Rotate(map, N.Vector3.UnitX).ToGodot());
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("map_south"), MathX.Rotate(map, N.Vector3.UnitZ).ToGodot());
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("body_centre"), v.Centre);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("nadir"), new Vector2((float)gx, (float)gz));
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("height"), height);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("reach"), RimOf(Radius, height));
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("local"), local);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("sun_dir"), v.ToSun);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("umbra_count"), v.Umbra == null ? 0 : v.UmbraCount);
            if (v.Umbra != null) m.SetShaderParameter(PlanetSurface.Fx.Names.Of("umbra"), v.Umbra);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("umbra_sun"), v.UmbraSun);
        }
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("relief"), lie);
        _clouds.SetShaderParameter(PlanetSurface.Fx.Names.Of("cloud_lift"), v.CloudHeight * lie);
        if (_hasRing)
        {
            _ring.GlobalTransform = new Transform3D(_ringBasis, v.Centre);
            _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("to_sun"), v.ToSun);
            _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("axis"), _ringBasis.Y.Normalized());
        }
        foreach (ShaderMaterial m in _byRay) m.SetShaderParameter(PlanetSurface.Fx.Names.Of("globe_centre"), v.Centre);
        // Close over the ground you look down through little air: the rim of it shows as you draw away.
        _airMesh.Visible = Air(_air, (v.Eye - v.Centre).Normalized(), Radius, _airThickness, height, v.Rim * Away(height), v.ToSun);
    }

    static float Smooth(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    static readonly SphereMesh Shell = new() { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24, CustomAabb = Everywhere };
    static Shader _airShader;

    /// <summary>A world's air seen from outside, over its disc and round its limb, in its colour: a shell round the eye, drawn by every look of a world with air (its globe and space's far look of it).</summary>
    public static (MeshInstance3D Mesh, ShaderMaterial Material) AirMesh(Color color)
    {
        var m = new ShaderMaterial { Shader = _airShader ??= new Shader { Code = AirSource } };
        m.SetShaderParameter(PlanetSurface.Fx.Names.Of("color"), color);
        return (Instance("Atmosphere", Shell, m), m);
    }

    /// <summary>
    /// Lays an air shell out for this frame: the world's centre down <paramref name="up"/> from the eye, its ground's radius and its air's
    /// thickness (any one unit), the eye's height over the ground, how much of it shows (<paramref name="rim"/>), the way to the sun.
    /// Whether it draws at all.
    /// </summary>
    public static bool Air(ShaderMaterial m, Vector3 up, float ground, float thickness, float height, float rim, Vector3 toSun)
    {
        if (rim <= 0.002f) return false;
        m.SetShaderParameter(PlanetSurface.Fx.Names.Of("eye_up"), up);
        m.SetShaderParameter(PlanetSurface.Fx.Names.Of("ground"), ground);
        m.SetShaderParameter(PlanetSurface.Fx.Names.Of("thickness"), thickness);
        m.SetShaderParameter(PlanetSurface.Fx.Names.Of("eye_height"), height);
        m.SetShaderParameter(PlanetSurface.Fx.Names.Of("strength"), AirStrength * rim);
        m.SetShaderParameter(PlanetSurface.Fx.Names.Of("sun_dir"), toSun);
        return true;
    }

    /// <summary>How much of a world's air shows from outside, 0 to 1, by its sky's density: thin air hardly at all.</summary>
    public static float AirShare(float density) => 1f - MathF.Exp(-2.5f * MathF.Max(density, 0f));

    static MeshInstance3D Instance(string name, Mesh mesh, Material material) => new()
    {
        Name = name, Mesh = mesh, MaterialOverride = material,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>A polar grid: x and z the bearing's unit direction, y how far out (0 to 1). The shader does the rest.</summary>
    static ArrayMesh PolarGrid(int rings, int segments)
    {
        int n = (segments + 1) * (rings + 1);
        var points = new Vector3[n];
        var indices = new int[segments * rings * 6];
        int at = 0;
        for (int r = 0; r <= rings; r++)
            for (int s = 0; s <= segments; s++)
            {
                float a = s * MathF.Tau / segments;
                points[at++] = new Vector3(MathF.Cos(a), r / (float)rings, MathF.Sin(a));
            }
        at = 0;
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * (segments + 1) + s, b = a + 1, c = a + segments + 1, d = c + 1;
                indices[at++] = a; indices[at++] = c; indices[at++] = b;
                indices[at++] = b; indices[at++] = c; indices[at++] = d;
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.CustomAabb = Everywhere;
        return mesh;
    }

    /// <summary>A flat annulus in the equatorial plane; UV.x runs from the inner to the outer edge (as OrbitView's).</summary>
    public static ArrayMesh RingMesh(float inner, float outer, int segments)
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
