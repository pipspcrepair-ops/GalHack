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

    /// <summary>The ground's height over the sea, m, on the map's own grid: the sheet's relief.</summary>
    public Image Relief { get; private init; }

    const float SeamBlend = 0.08f;

    /// <param name="coverage">The sky's own clouds over a map point (u, v, 0 to 1), so these are the clouds you fly through; null: the generator's.</param>
    /// <param name="land">The ground's colour at a map point, m, so the far land is the colour of the land it joins; null: the biomes'.</param>
    public static GlobePaint Make(PlanetSpec spec, WorldMap map, OrbitRules.GlobeRules rules, Func<float, float, float> coverage = null, Func<double, double, Rgb> land = null)
    {
        ArchetypeLook look = ArchetypeLook.For(spec);
        ColorMap colors = ColorMap.Build(map, look, rules.TextureWidth, land);
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
        };
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

/// <summary>How the world below stands for one drawn frame, all of it worked out from your height.</summary>
public readonly record struct WorldView
{
    /// <summary>The drawing frame at the point under you, in the world.</summary>
    public Vector3 Up { get; init; }

    /// <summary>What can shadow the globe now (SkyRig.Umbrae): each body's place from its centre and radius, in its radii; how many; the sun's angular radius.</summary>
    public Vector4[] Umbra { get; init; }
    public int UmbraCount { get; init; }
    public float UmbraSun { get; init; }
    public Vector3 East { get; init; }
    public Vector3 South { get; init; }
    /// <summary>The same place's frame on the globe, for the map lookup.</summary>
    public Vector3 MapUp { get; init; }
    public Vector3 MapEast { get; init; }
    public Vector3 MapSouth { get; init; }
    /// <summary>The map point under you, m.</summary>
    public Vector2 Nadir { get; init; }
    /// <summary>The eye's height over the sheet, m.</summary>
    public float Height { get; init; }
    /// <summary>The sheet's curvature, 1 / m.</summary>
    public float Bend { get; init; }
    /// <summary>How far out from under you the sheet stays flat before it curves, m: the land's own round_sink under the cap.</summary>
    public float FlatFrom { get; init; }
    /// <summary>1: the map laid out flat, as the surface has it. 0: wrapped on the sphere.</summary>
    public float Flat { get; init; }
    /// <summary>1: past <see cref="Local"/> m from under you the map is the sphere's whatever <see cref="Flat"/> is (a pole has no flat map round it). 0: flat out to the rim of the sheet.</summary>
    public float Taper { get; init; }
    public float Local { get; init; }
    /// <summary>Share of the map's relief drawn.</summary>
    public float Relief { get; init; }
    /// <summary>The height of the cloud layer, m.</summary>
    public float CloudLift { get; init; }
    /// <summary>The sky's cloud deck's curvature, 1 / m: flat until the climb bends the ground, then the same. The sheet's own clouds curve with its ground, as the sky's far clouds do.</summary>
    public float CloudBend { get; init; }
    /// <summary>The sheet over the land that's still drawn (0 to 1).</summary>
    public float OverLand { get; init; }
    /// <summary>The nearest and farthest the sheet is drawn along each eye ray, m; 0: where it really is.</summary>
    public float Near { get; init; }
    public float Far { get; init; }
    public Vector3 ToSun { get; init; }
    public float FogDensity { get; init; }
    public Color FogColor { get; init; }
    /// <summary>The rim of air seen from outside, 0 to 1.</summary>
    public float Rim { get; init; }
    /// <summary>Where the eye is, in the world.</summary>
    public Vector3 Eye { get; init; }
    /// <summary>The radius of the sphere the world's ring stands round, m (<see cref="Globe.RingSphere"/>), and how much of it shows (0 to 1).</summary>
    public float RingRadius { get; init; }
    public float RingShown { get; init; }

    /// <summary>In space: each pixel's depth is where its ray meets the true globe, round <see cref="TrueCentre"/> (what stands behind the world is hidden by it).</summary>
    public bool TrueDepth { get; init; }

    /// <summary>The true globe's centre in the world.</summary>
    public Vector3 TrueCentre { get; init; }
}

/// <summary>
/// The world below, drawn: one sheet that is the far land past the surface's own, the land itself
/// at the crossing, and the globe from orbit. The map stays 16 km round; what changes is how it
/// is laid out on the eye's view, so at the crossing it is exactly the flat map you were flying
/// over, and it curves away smoothly from there.
///
/// It is a polar grid centred under the eye, moved with it. A grid point is a point on the map a
/// known distance and bearing from the place under you, drawn at that same distance along a sphere
/// whose curvature grows with height: almost flat at the crossing, the true globe far out. The
/// height under you is always your height, so the land keeps the size it had. Directions from the
/// globe's centre stay places on the map (<see cref="GlobeMap"/>): the map lookup follows the great
/// circle out from the place under you, and low down it eases into the surface's own flat map
/// (away from the equator the two differ in width). The lookup round the sphere is stereographic,
/// so what shows keeps its shape on a sheet much wider than the globe; past the far side, where
/// no lookup round a sphere could fill the sheet's rim, it goes back to the repeating flat map.
///
/// Nothing it draws touches the game: it has no collider, and every point is drawn somewhere else
/// along its own eye ray, which changes nothing you see and only sets what it stands in front of
/// (<see cref="WorldView.Near"/>): behind the surface's land while it is the far field, in front
/// of it for the swap, and between the wrecks and the moons in space. So its fog is its own too,
/// from the distance it stands for.
///
/// The shroud (<see cref="Shroud"/>) is the air itself thickening round the eye, in the world's
/// own sky colours: the swap happens behind it, at the crossing.
/// </summary>
public partial class Globe : Node3D
{
    /// <summary>The sheet's geometry and map lookup, shared by the ground and the clouds.</summary>
    const string SheetCode = """
        uniform vec3 eye_up = vec3(0.0, 1.0, 0.0);
        uniform vec3 eye_east = vec3(1.0, 0.0, 0.0);
        uniform vec3 eye_south = vec3(0.0, 0.0, 1.0);
        uniform vec3 map_up = vec3(0.0, 1.0, 0.0);
        uniform vec3 map_east = vec3(1.0, 0.0, 0.0);
        uniform vec3 map_south = vec3(0.0, 0.0, 1.0);
        uniform vec2 map_size = vec2(16000.0, 8000.0);
        uniform vec2 nadir = vec2(0.0);
        uniform float radius = 2546.0;
        uniform float height = 1000.0;
        uniform float bend = 0.0;
        uniform float flat_from = 0.0;
        uniform float flat_map = 1.0;
        uniform float taper = 0.0;
        uniform float local = 3000.0;
        uniform float reach = 20000.0;
        uniform float near_at = 0.0;
        uniform float far_at = 0.0;
        uniform float fog_density = 0.0;
        uniform vec3 fog_color : source_color = vec3(0.5);
        uniform float over_land = 1.0;
        uniform float sphere_out = 100000.0;
        varying vec2 offset;
        varying float distance;

        // One point of the sheet on the map: mostly where uv says, with up to two other looks over it.
        struct MapPoint {
            vec2 uv;
            vec2 cap_uv;
            vec2 tile_uv;
            float cap;
            float tile;
        };

        // How far round the globe a point at arc s stands, radians: the globe laid on the sheet's own
        // sphere stereographically, at the map's scale under you. Land keeps its shape, nothing is
        // drawn twice, and the far side of the globe never closes up on the sheet. Where the sheet
        // is the true sphere this is s / radius.
        float round_angle(float s) {
            return 2.0 * atan(tan(s * bend * 0.5) / (bend * radius));
        }

        MapPoint map_point(vec2 o) {
            float s = max(length(o), 0.01);
            float a = round_angle(s);
            vec3 d = map_up * cos(a) + (map_east * o.x + map_south * o.y) * sin(a) / s;
            vec2 globe = vec2(atan(d.x, d.z) / 6.2831853, acos(clamp(d.y, -1.0, 1.0)) / 3.1415927);
            MapPoint m;
            m.tile_uv = (nadir + o) / map_size;
            vec2 gap = m.tile_uv - globe;
            gap -= round(gap);
            // The flat map hands over to the sphere near you by moving; further out the two are too far
            // apart, and over the pole the sphere's map jumps a half turn, so there they cross-fade.
            float keep = flat_map * (1.0 - taper * smoothstep(local * 0.35, local, s)) * (1.0 - smoothstep(local, local * 1.3, s));
            m.uv = globe + gap * keep;
            // The map is a torus, so wrapped on the sphere its columns close to a point at each pole
            // and the land fans out from there like spokes. Over the poles it is laid flat instead:
            // a piece of the map well clear of the map's edges, at its own scale.
            m.cap = smoothstep(0.80, 0.93, abs(d.y)) * (1.0 - keep);
            m.cap_uv = (d.y > 0.0 ? vec2(0.25) : vec2(0.75)) + vec2(d.x, d.z) * radius / map_size;
            // Low down the sheet runs on far past the far side of the globe, and the rim of a sheet
            // that wide is many times the globe's own girth: anything of the sphere there is smeared
            // round it. So past the sphere's reach it is the flat map that stood there under the cap,
            // which repeats both ways with no seam.
            m.tile = max(smoothstep(sphere_out, sphere_out * 1.35, s), (1.0 - taper) * smoothstep(local * 0.8, local, s));
            return m;
        }

        // Metres of the map a grid point stands from the point under you.
        float arc(float t) { return reach * (exp(6.0 * t) - 1.0) / 402.43; }

        // A point at arc s on bearing t, lifted, as seen from the eye; the sheet's own normal is n. It
        // is flat out to flat_from, as the land is, and curves away past it (round_sink on the surface).
        vec3 sheet_point(float s, vec3 t, float lift, out vec3 n) {
            float f = min(s, flat_from), r = s - f;
            float x = r * bend;
            float along = f + (x > 0.0001 ? sin(x) / bend : r);
            float drop = x > 0.0001 ? (1.0 - cos(x)) / bend : r * x * 0.5;
            n = t * sin(x) + eye_up * cos(x);
            return t * along - eye_up * (height + drop) + n * lift;
        }

        // How far along its own eye ray a point at distance d is drawn.
        float stand(float d) {
            if (far_at <= 0.0) return d;
            return near_at + (far_at - near_at) * d / (d + height);
        }
        """;

    /// <summary>
    /// What stands in front of what, in space. The sheet and its clouds, rim and ring are drawn along
    /// their eye rays wherever suits the climb, but each pixel's depth is where its ray meets the
    /// true globe (or its cloud shell, its air, its ring): so a wreck or a moon behind the world is
    /// hidden by it, and one in front of it stands in front. Where a ray misses the true globe the
    /// pixel keeps the depth it is drawn at.
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

    /// <summary>A map as the sheet shows it at a point, filtered by how the lookup really moves across the screen: the sphere's map swells on the sheet.</summary>
    const string Paint = """
        vec4 paint(sampler2D map, MapPoint m) {
            vec2 dx = dFdx(m.uv), dy = dFdy(m.uv);
            dx -= round(dx); // over the map's edge uv jumps a whole turn
            dy -= round(dy);
            vec2 cx = dFdx(m.cap_uv), cy = dFdy(m.cap_uv), tx = dFdx(m.tile_uv), ty = dFdy(m.tile_uv);
            vec4 c = m.tile < 1.0 ? textureGrad(map, m.uv, dx, dy) : vec4(0.0);
            if (m.cap > 0.0) c = mix(c, textureGrad(map, m.cap_uv, cx, cy), m.cap);
            if (m.tile > 0.0) c = mix(c, textureGrad(map, m.tile_uv, tx, ty), m.tile);
            return c;
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
        render_mode world_vertex_coords, blend_mix, depth_draw_always, cull_back, diffuse_burley, specular_schlick_ggx, shadows_disabled;
        uniform sampler2D albedo_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D glow_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D relief_map : filter_linear_mipmap, repeat_enable;
        uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);
        uniform float day_glow = 0.6;
        uniform float night_glow = 1.6;
        uniform float relief = 1.0;
        {{SheetCode}}
        {{Paint}}
        {{UmbraCode}}
        {{TrueDepthCode}}
        varying vec3 round_normal;

        float ground_at(vec2 o, float lod) {
            MapPoint m = map_point(o);
            float e = m.tile < 1.0 ? textureLod(relief_map, m.uv, lod).r : 0.0;
            if (m.cap > 0.0) e = mix(e, textureLod(relief_map, m.cap_uv, lod).r, m.cap);
            if (m.tile > 0.0) e = mix(e, textureLod(relief_map, m.tile_uv, lod).r, m.tile);
            return e * relief;
        }

        void vertex() {
            float s = arc(VERTEX.y);
            vec3 t = eye_east * VERTEX.x + eye_south * VERTEX.z;
            offset = VERTEX.xz * s;
            // The relief at the grid's own spacing there, or it stands up as spikes.
            float step_m = max(32.0, s * 0.0375);
            float lod = log2(step_m / 32.0);
            // Past the horizon the relief lies down: there the grid's few bearings would stand hills
            // up over the horizon as a fence of posts, a bearing apart.
            float beyond = 1.0 - smoothstep(0.0, 2000.0, s - flat_from - sqrt(2.0 * height / max(bend, 1e-9)));
            float e = ground_at(offset, lod) * beyond;
            vec3 n;
            vec3 p = sheet_point(s, t, e, n);
            // The ground's slope, from the same map.
            float ex = ground_at(offset + vec2(step_m, 0.0), lod) * beyond;
            float ez = ground_at(offset + vec2(0.0, step_m), lod) * beyond;
            vec3 slope = (eye_east * (ex - e) + eye_south * (ez - e)) / step_m;
            // Lit as the sheet lies low down, as the sphere it stands for high up: the night side comes in with the curve.
            float a = round_angle(s);
            round_normal = t * sin(a) + eye_up * cos(a);
            distance = length(p);
            VERTEX = CAMERA_POSITION_WORLD + p / distance * stand(distance);
            NORMAL = normalize(mix(round_normal, n, flat_map) - slope);
        }
        void fragment() {
            MapPoint m = map_point(offset);
            vec4 a = paint(albedo_map, m);
            ALBEDO = a.rgb * sunlight_at(normalize(round_normal), sun_dir);
            ROUGHNESS = mix(0.9, 0.42, a.a);
            SPECULAR = mix(0.1, 0.45, a.a);
            float night = smoothstep(0.15, -0.25, dot(normalize(round_normal), sun_dir));
            EMISSION = paint(glow_map, m).rgb * (day_glow + night * night_glow);
            ALPHA = mix(over_land, 1.0, smoothstep(4800.0, 5800.0, max(abs(offset.x), abs(offset.y))));
            FOG = vec4(fog_color, 1.0 - exp(-fog_density * distance));
            float hit = true_depth > 0.5 ? sphere_hit(CAMERA_POSITION_WORLD, normalize(mat3(INV_VIEW_MATRIX) * VERTEX), radius) : -1.0;
            DEPTH = hit > 0.0 ? depth_of(CAMERA_POSITION_WORLD + normalize(mat3(INV_VIEW_MATRIX) * VERTEX) * hit, VIEW_MATRIX, PROJECTION_MATRIX) : FRAGCOORD.z;
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
        render_mode world_vertex_coords, blend_mix, depth_draw_never, cull_back, diffuse_lambert_wrap, specular_disabled, shadows_disabled;
        uniform sampler2D cloud_map : source_color, filter_linear_mipmap, repeat_enable;
        uniform float cloud_lift = 30.0;
        uniform float live = 0.0;
        {{TrueDepthCode}}
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
        {{SheetCode}}
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
            offset = VERTEX.xz * s;
            float turned = round_angle(s);
            round_normal = (eye_east * VERTEX.x + eye_south * VERTEX.z) * sin(turned) + eye_up * cos(turned);
            vec3 n;
            vec3 t = eye_east * VERTEX.x + eye_south * VERTEX.z;
            vec3 p = sheet_point(s, t, cloud_lift, n);
            float a = round_angle(s);
            round_normal = t * sin(a) + eye_up * cos(a);
            distance = length(p);
            // Just nearer along the ray than the ground, whose hills would otherwise poke through it.
            VERTEX = CAMERA_POSITION_WORLD + p / distance * stand(distance) * 0.92;
            NORMAL = n;
        }
        void fragment() {
            MapPoint m = map_point(offset);
            vec4 base = vec4(0.0);
            Cloud c;
            if (live < 0.5) {
                base = paint(cloud_map, m);
                c.color = base.rgb; c.alpha = base.a; c.top = 0.0; c.storm = 0.0; c.lightning = 0.0; c.billow = 0.0;
            } else {
                vec2 dx = dFdx(m.uv), dy = dFdy(m.uv);
                dx -= round(dx);
                dy -= round(dy);
                vec2 cx = dFdx(m.cap_uv), cy = dFdy(m.cap_uv), tx = dFdx(m.tile_uv), ty = dFdy(m.tile_uv);
                c.color = vec3(1.0); c.alpha = 0.0; c.top = 0.0; c.storm = 0.0; c.lightning = 0.0; c.billow = 0.0;
                if (m.tile < 1.0) c = cloud_at(m.uv, dx, dy);
                if (m.cap > 0.0) c = blend(c, cloud_at(m.cap_uv, cx, cy), m.cap);
                if (m.tile > 0.0) c = blend(c, cloud_at(m.tile_uv, tx, ty), m.tile);
            }
            ALBEDO = c.color;
            // The storms stand up out of the deck: their tops lit on the sun's side, shaded on the other.
            float drawn = stand(distance) * 0.92 / max(distance, 1.0);
            // Seen edge on, near the limb, relief from screen-space slopes only streaks: it lies down there.
            float facing = abs(dot(normalize(NORMAL), normalize(VERTEX)));
            float h = c.top * tower_lift * drawn * smoothstep(0.15, 0.45, facing);
            vec3 dpx = dFdx(VERTEX), dpy = dFdy(VERTEX);
            vec3 r1 = cross(dpy, NORMAL), r2 = cross(NORMAL, dpx);
            float det = dot(dpx, r1);
            vec3 grad = sign(det) * (dFdx(h) * r1 + dFdy(h) * r2);
            NORMAL = normalize(abs(det) * NORMAL - grad);
            float night = smoothstep(0.1, -0.2, dot(normalize(round_normal), sun_dir));
            vec2 xz = (m.tile > 0.5 ? m.tile_uv : m.uv) * map_size;
            float flash = live > 0.5 ? flashes(xz, c.lightning, c.storm, c.billow) : 0.0;
            EMISSION = vec3(0.72, 0.78, 1.0) * flash * 1.6 * night * c.alpha;
            // Lit softly round the terminator, and dark past it: the night side's clouds are seen by their lightning.
            ALBEDO *= 1.0 - 0.7 * night;
            // An eclipse: a moon's shadow on the globe dims what lies under it.
            ALBEDO *= sunlight_at(normalize(round_normal), sun_dir);
            ROUGHNESS = 0.4;
            // Under the layer (or level with it) the sky draws the clouds: a flat sheet seen edge-on
            // stands along the horizon as a fence of slivers. It shows once the eye is over it.
            float over_layer = max(over_land, smoothstep(cloud_lift, cloud_lift + 200.0, height));
            ALPHA = c.alpha * over_layer * mix(over_land, 1.0, smoothstep(4800.0, 5800.0, max(abs(offset.x), abs(offset.y))));
            FOG = vec4(fog_color, 1.0 - exp(-fog_density * distance));
            vec3 ray_w = normalize(mat3(INV_VIEW_MATRIX) * VERTEX);
            float hit = true_depth > 0.5 ? sphere_hit(CAMERA_POSITION_WORLD, ray_w, radius + cloud_lift) : -1.0;
            DEPTH = hit > 0.0 ? depth_of(CAMERA_POSITION_WORLD + ray_w * hit, VIEW_MATRIX, PROJECTION_MATRIX) : FRAGCOORD.z;
        }
        """;

    /// <summary>The clouds' shader code (tests read its noise scales).</summary>
    public static string CloudShader => CloudSource;

    /// <summary>
    /// The air seen from outside, worked out from each eye ray against the sheet's own sphere, so
    /// it fits whatever the curve is: over the limb the glow thins away with the height of the
    /// ray's lowest point, over the disc it thins toward the middle, meeting at the ground's edge.
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
            vec3 low = eye + r * max(-dot(eye, r), 0.0);
            float lowest = length(low);
            float h = (lowest - ground) / thickness;
            // Past the limb it thins away with height; over the ground, with how steeply the ray goes down through the air.
            float x = lowest / ground;
            float graze = sqrt(2.0 * thickness / ground) / max(sqrt(max(1.0 - x * x, 0.0)), 0.0001);
            float thick = h >= 0.0 ? exp(-4.5 * h) * clamp(1.0 - h, 0.0, 1.0) : min(graze * graze, 1.0) * x * x;
            float day = smoothstep(-0.3, 0.35, dot(low / lowest, sun_dir));
            ALBEDO = color * thick * day * strength;
        }
        """;

    /// <summary>
    /// The shroud: the world's own sky round the eye, thicker the more air the eye looks through.
    /// It stands just off the vessel, over the land and the sheet alike, so what happens behind it
    /// is hidden the same for both; it never quite closes, and it keeps the sky's gradient.
    /// </summary>
    const string ShroudSource = """
        shader_type spatial;
        render_mode world_vertex_coords, unshaded, blend_mix, depth_draw_never, cull_front, fog_disabled, shadows_disabled;
        uniform vec3 low : source_color;
        uniform vec3 high : source_color;
        uniform vec3 glow : source_color;
        uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);
        uniform float amount = 0.0;
        uniform float shell = 40.0;
        varying vec3 ray;
        void vertex() {
            ray = VERTEX;
            VERTEX = CAMERA_POSITION_WORLD + VERTEX * shell;
        }
        void fragment() {
            vec3 r = normalize(ray);
            float rise = smoothstep(-0.05, 0.75, r.y);
            float toward = max(dot(r, sun_dir), 0.0);
            ALBEDO = mix(low, high, rise) + glow * pow(toward, 6.0) * 0.5;
            ALPHA = amount * mix(1.0, 0.72, rise);
        }
        """;

    /// <summary>
    /// The world's ring, always round the sphere you can see: over the cap that is the sheet, whose
    /// curve runs from nearly flat to the true globe, so the ring stands on the sheet's centre at
    /// the sheet's scale (<see cref="RingSphere"/>). Every pixel works out its own ray against the
    /// ring's plane and that sphere, as the surface's sky does, so what the mesh is only covers the
    /// ring: while the sheet is drawn elsewhere along its eye rays the mesh stands just in front of
    /// it, behind what you fly near. It writes no depth, or everything behind it would be cut out at
    /// its rim, gaps and all.
    /// </summary>
    const string RingSource = $$"""
        shader_type spatial;
        render_mode world_vertex_coords, unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
        uniform vec3 color : source_color;
        uniform vec3 axis = vec3(0.0, 1.0, 0.0);
        uniform vec3 to_sun = vec3(0.0, 1.0, 0.0);
        uniform vec3 centre = vec3(0.0);
        uniform float ring_radius = 2546.0;
        uniform vec3 eye_up = vec3(0.0, 1.0, 0.0);
        uniform float height = 1000.0;
        uniform float sheet_radius = 2546.0;
        uniform float stand_at = 0.0;
        uniform float fade = 1.0;
        {{TrueDepthCode}}
        void vertex() {
            if (stand_at > 0.0) {
                vec3 to = VERTEX - CAMERA_POSITION_WORLD;
                float d = length(to);
                VERTEX = CAMERA_POSITION_WORLD + to / d * stand_at * (1.0 + 0.05 * d / (d + stand_at));
            }
        }
        void fragment() {
            vec3 ro = CAMERA_POSITION_WORLD, rd = normalize(mat3(INV_VIEW_MATRIX) * VERTEX);
            float denom = dot(rd, axis);
            float t = dot(centre - ro, axis) / (abs(denom) > 1e-5 ? denom : 1e-5);
            if (t <= 0.0) discard;
            float r = (length(ro + rd * t - centre) / ring_radius - 1.45) / 0.85; // 0 at the inner edge, 1 at the outer
            if (r <= 0.0 || r >= 1.0) discard;
            vec3 oc = eye_up * (height + sheet_radius);
            float b = dot(oc, rd), past = b * b - dot(oc, oc) + sheet_radius * sheet_radius;
            if (past > 0.0 && -b - sqrt(past) > 0.0 && -b - sqrt(past) < t) discard;
            float bands = 0.55 + 0.25 * sin(r * 61.0) + 0.2 * sin(r * 23.0 + 1.0);
            float gap = smoothstep(0.0, 0.02, abs(r - 0.62)) * smoothstep(0.0, 0.05, r) * smoothstep(1.0, 0.94, r);
            ALBEDO = color * (0.45 + 0.55 * clamp(bands, 0.0, 1.0)) * (0.8 + 0.3 * bands) * (0.35 + 0.65 * clamp(abs(dot(axis, to_sun)) * 1.5, 0.0, 1.0));
            ALPHA = gap * mix(0.95, 1.0, clamp(bands, 0.0, 1.0)) * fade;
            DEPTH = true_depth > 0.5 ? depth_of(ro + rd * t, VIEW_MATRIX, PROJECTION_MATRIX) : FRAGCOORD.z;
        }
        """;

    /// <summary>The ring's edges, in radii of the sphere it stands round.</summary>
    public const float RingInner = 1.45f, RingOuter = 2.3f;

    /// <summary>How far over the cap the ring has moved onto the sheet, m.</summary>
    public const float RingFitsBy = 400f;

    /// <summary>
    /// The radius of the sphere a world's ring stands round: the true globe's under the cap, where
    /// the sky shows it from the ground, and the sheet's from a little over it, so the ring never
    /// lands inside the disc you can see.
    /// </summary>
    public static float RingSphere(float sheetRadius, float radius, float overCap)
    {
        float k = Mathf.Clamp(overCap / RingFitsBy, 0f, 1f);
        return Mathf.Lerp(radius, sheetRadius, k * k * (3f - 2f * k));
    }

    /// <summary>How the ring stands against the sheet this frame: its centre's distance from the sheet's, m, and its inner edge in radii of the sheet.</summary>
    public (float Off, float Inner) RingFit { get; private set; }

    const float AirStrength = 1.1f;
    const float ShroudShell = 40f;

    /// <summary>Where the rim of air's shell stands off the eye, m: in front of the sheet in space, behind what you fly near.</summary>
    const float AirShell = 15000f;

    ShaderMaterial _ground, _clouds, _air, _shroud, _ringMaterial;
    MeshInstance3D _airMesh, _shroudMesh, _ring;
    bool _hasRing;
    float _airThickness, _sphereReach;
    Basis _ringBasis = Basis.Identity;

    /// <summary>Not drawn at all (screenshots of other bodies, from far from here).</summary>
    public bool Hidden;

    public float Radius { get; private set; }

    /// <summary>The live weather the clouds are drawn from.</summary>
    public GlobeWeather Weather { get; private set; }

    /// <summary>How far along the map the sheet reaches this frame, m.</summary>
    public float Reach { get; private set; }

    /// <summary>The angular radius of the sphere the sheet was last drawn on, and the eye it was drawn for (tests: its true size).</summary>
    public float Angular { get; private set; }
    public Vector3 DrawnFrom { get; private set; }

    /// <summary>Sets the sheet up a piece at a time, each upload on its own frame, so none of them shows as a hitch.</summary>
    public IEnumerable<int> Setup(GlobePaint paint, float radius, float atmosphereRadius, OrbitRules.GlobeRules rules, Vector2 mapSize)
    {
        Radius = radius;
        _airThickness = atmosphereRadius - radius;
        _sphereReach = Mathf.DegToRad((float)rules.SphereReach);
        ArchetypeLook look = paint.Look;
        ArrayMesh grid = PolarGrid(rules.Rings, rules.Segments);
        _ground = new ShaderMaterial { Shader = new Shader { Code = GroundSource } };
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("albedo_map"), ImageTexture.CreateFromImage(paint.Albedo));
        yield return 0;
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("glow_map"), ImageTexture.CreateFromImage(paint.Glow));
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("relief_map"), ImageTexture.CreateFromImage(paint.Relief));
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("day_glow"), look.LiquidGlow > 0 ? 0.9f : 0.5f);
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("night_glow"), look.GlowingNights ? 2.5f : 1.4f);
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("radius"), radius);
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("map_size"), mapSize);
        AddChild(Instance("Ground", grid, _ground));
        yield return 0;
        _clouds = new ShaderMaterial { Shader = new Shader { Code = CloudSource } };
        _clouds.SetShaderParameter(PlanetSurface.Fx.Names.Of("cloud_map"), ImageTexture.CreateFromImage(paint.Clouds));
        _clouds.SetShaderParameter(PlanetSurface.Fx.Names.Of("radius"), radius);
        _clouds.SetShaderParameter(PlanetSurface.Fx.Names.Of("map_size"), mapSize);
        Weather = new GlobeWeather(mapSize);
        Weather.Bind(_clouds, C(look.CloudColor));
        AddChild(Instance("Clouds", grid, _clouds));
        yield return 0;
        var shell = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24, CustomAabb = Everywhere };
        _air = new ShaderMaterial { Shader = new Shader { Code = AirSource } };
        _air.SetShaderParameter(PlanetSurface.Fx.Names.Of("color"), C(look.Atmosphere));
        _air.SetShaderParameter(PlanetSurface.Fx.Names.Of("shell"), AirShell);
        _airMesh = Instance("Atmosphere", shell, _air);
        AddChild(_airMesh);
        _shroud = new ShaderMaterial { Shader = new Shader { Code = ShroudSource }, RenderPriority = 100 };
        _shroud.SetShaderParameter(PlanetSurface.Fx.Names.Of("shell"), ShroudShell);
        _shroudMesh = Instance("Shroud", shell, _shroud);
        _shroudMesh.Visible = false;
        AddChild(_shroudMesh);
        _ringMaterial = new ShaderMaterial { Shader = new Shader { Code = RingSource }, RenderPriority = 5 }; // nearer than anything else up there
        _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("color"), C(look.RingColor));
        _hasRing = look.Ring;
        // The mesh only covers the ring: a shade wider than it, so its straight sides never cut the round edge.
        _ring = new MeshInstance3D { Name = "Ring", Mesh = RingMesh(radius * (RingInner - 0.02f), radius * (RingOuter + 0.03f), 160), MaterialOverride = _ringMaterial, Visible = false, CustomAabb = Everywhere };
        AddChild(_ring);
        Visible = false;
    }

    /// <summary>How far along the sheet its rim stands, m: out to the limb and a little past it, for the back of the sphere is never in view.</summary>
    public static float RimOf(float rho, float height) => MathF.Min(rho * MathF.Acos(rho / (rho + height + 700f)) * 1.04f, 60000f);

    /// <summary>The arc on a sheet of curve radius <paramref name="rho"/> that stands <paramref name="angle"/> radians round a globe of <paramref name="radius"/>: the inverse of the shader's <c>round_angle</c>.</summary>
    public static float ArcAt(float angle, float rho, float radius) => 2f * rho * MathF.Atan(MathF.Tan(angle * 0.5f) * radius / rho);

    static readonly Aabb Everywhere = new(new Vector3(-1e6f, -1e6f, -1e6f), new Vector3(2e6f, 2e6f, 2e6f));

    /// <summary>The ring stands in the globe's frame.</summary>
    public void PlaceRing(Transform3D globe)
    {
        _ringBasis = globe.Basis;
    }

    /// <summary>Lays the sheet out for this frame.</summary>
    public void Draw(in WorldView v)
    {
        if (_ground == null) return;
        Visible = !Hidden;
        if (!Visible) return;
        float rho = v.Bend > 1e-7f ? 1f / v.Bend : 1e7f;
        Reach = v.FlatFrom + RimOf(rho, v.Height);
        (Angular, DrawnFrom) = (MathF.Asin(rho / (rho + v.Height)), v.Eye);
        float sphereOut = ArcAt(_sphereReach, rho, Radius);
        foreach (ShaderMaterial m in new[] { _ground, _clouds })
        {
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("eye_up"), v.Up);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("eye_east"), v.East);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("eye_south"), v.South);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("map_up"), v.MapUp);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("map_east"), v.MapEast);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("map_south"), v.MapSouth);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("nadir"), v.Nadir);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("height"), v.Height);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("bend"), v.Bend);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("flat_from"), v.FlatFrom);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("flat_map"), v.Flat);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("taper"), v.Taper);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("local"), v.Local);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("reach"), Reach);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("sphere_out"), sphereOut);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("near_at"), v.Near);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("far_at"), v.Far);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("fog_density"), v.FogDensity);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("fog_color"), v.FogColor);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("over_land"), v.OverLand);
        }
        // Over the cloud deck for the swap, under it as the far field.
        _ground.RenderPriority = v.OverLand > 0f && v.OverLand < 1f ? 1 : -2;
        _clouds.RenderPriority = _ground.RenderPriority + 1;
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("relief"), v.Relief);
        _ground.SetShaderParameter(PlanetSurface.Fx.Names.Of("sun_dir"), v.ToSun);
        _clouds.SetShaderParameter(PlanetSurface.Fx.Names.Of("sun_dir"), v.ToSun);
        foreach (ShaderMaterial m in new[] { _ground, _clouds })
        {
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("umbra_count"), v.Umbra == null ? 0 : v.UmbraCount);
            if (v.Umbra != null) m.SetShaderParameter(PlanetSurface.Fx.Names.Of("umbra"), v.Umbra);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("umbra_sun"), v.UmbraSun);
        }
        _clouds.SetShaderParameter(PlanetSurface.Fx.Names.Of("cloud_lift"), v.CloudLift);
        _clouds.SetShaderParameter(PlanetSurface.Fx.Names.Of("sun_dir"), v.ToSun);
        _air.SetShaderParameter(PlanetSurface.Fx.Names.Of("sun_dir"), v.ToSun);
        _ring.Visible = _hasRing && v.RingShown > 0.002f;
        if (_ring.Visible)
        {
            // One centre and one scale for the ring, its shader and the sheet: nothing of it is placed apart.
            Vector3 centre = v.Eye - v.Up * (v.Height + v.RingRadius);
            _ring.GlobalTransform = new Transform3D(_ringBasis.Scaled(Vector3.One * (v.RingRadius / Radius)), centre);
            _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("to_sun"), v.ToSun);
            _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("axis"), _ringBasis.Y.Normalized());
            _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("centre"), centre);
            _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("ring_radius"), v.RingRadius);
            _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("eye_up"), v.Up);
            _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("height"), v.Height);
            _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("sheet_radius"), rho);
            _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("stand_at"), v.Near * 0.9f);
            _ringMaterial.SetShaderParameter(PlanetSurface.Fx.Names.Of("fade"), v.RingShown);
            Vector3 sheetCentre = v.Eye - v.Up * (v.Height + rho);
            RingFit = (_ring.GlobalPosition.DistanceTo(sheetCentre), RingInner * v.RingRadius / rho);
        }
        foreach (ShaderMaterial m in new[] { _ground, _clouds, _air, _ringMaterial })
        {
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("true_depth"), v.TrueDepth ? 1f : 0f);
            m.SetShaderParameter(PlanetSurface.Fx.Names.Of("globe_centre"), v.TrueCentre);
        }
        _air.SetShaderParameter(PlanetSurface.Fx.Names.Of("air_radius"), Radius + _airThickness);
        _air.SetShaderParameter(PlanetSurface.Fx.Names.Of("eye_up"), v.Up);
        _air.SetShaderParameter(PlanetSurface.Fx.Names.Of("ground"), rho);
        _air.SetShaderParameter(PlanetSurface.Fx.Names.Of("thickness"), _airThickness);
        _air.SetShaderParameter(PlanetSurface.Fx.Names.Of("eye_height"), v.Height);
        _air.SetShaderParameter(PlanetSurface.Fx.Names.Of("strength"), AirStrength * v.Rim);
        _airMesh.Visible = v.Rim > 0.002f;
    }

    /// <summary>The shroud over everything past the vessel: how thick (0 to 1), the air's colour low in the view and overhead, and its glow toward the sun.</summary>
    public void Shroud(float amount, Color low, Color high, Color glow, Vector3 toSun)
    {
        if (_shroud == null) return;
        _shroudMesh.Visible = amount > 0.002f;
        if (!_shroudMesh.Visible) return;
        _shroud.SetShaderParameter(PlanetSurface.Fx.Names.Of("amount"), amount);
        _shroud.SetShaderParameter(PlanetSurface.Fx.Names.Of("low"), low);
        _shroud.SetShaderParameter(PlanetSurface.Fx.Names.Of("high"), high);
        _shroud.SetShaderParameter(PlanetSurface.Fx.Names.Of("glow"), glow);
        _shroud.SetShaderParameter(PlanetSurface.Fx.Names.Of("sun_dir"), toSun);
    }

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
