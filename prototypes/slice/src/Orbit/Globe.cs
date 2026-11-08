using System;
using System.Collections.Generic;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace Slice.Orbit;

/// <summary>
/// The globe's paint, made off the main thread from the world's own map: planet-generator's
/// colour map (albedo with glossy seas, and glow) and cloud map, as mipmapped images ready to
/// upload. Its pixel (u, v) shows the map point (uÂ·width, vÂ·height), which is what
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
            float e = ground_at(offset, lod);
            vec3 n;
            vec3 p = sheet_point(s, t, e, n);
            // The ground's slope, from the same map.
            float ex = ground_at(offset + vec2(step_m, 0.0), lod);
            float ez = ground_at(offset + vec2(0.0, step_m), lod);
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
            ALBEDO = a.rgb;
            ROUGHNESS = mix(0.9, 0.42, a.a);
            SPECULAR = mix(0.1, 0.45, a.a);
            float night = smoothstep(0.15, -0.25, dot(normalize(round_normal), sun_dir));
            EMISSION = paint(glow_map, m).rgb * (day_glow + night * night_glow);
            ALPHA = mix(over_land, 1.0, smoothstep(4800.0, 5800.0, max(abs(offset.x), abs(offset.y))));
            FOG = vec4(fog_color, 1.0 - exp(-fog_density * distance));
        }
        """;

    const string CloudSource = $$"""
        shader_type spatial;
        render_mode world_vertex_coords, blend_mix, depth_draw_never, cull_back, diffuse_lambert_wrap, specular_disabled, shadows_disabled;
        uniform sampler2D cloud_map : source_color, filter_linear_mipmap, repeat_enable;
        uniform float cloud_lift = 30.0;
        {{SheetCode}}
        {{Paint}}
        void vertex() {
            float s = arc(VERTEX.y);
            offset = VERTEX.xz * s;
            vec3 n;
            vec3 p = sheet_point(s, eye_east * VERTEX.x + eye_south * VERTEX.z, cloud_lift, n);
            distance = length(p);
            // Just nearer along the ray than the ground, whose hills would otherwise poke through it.
            VERTEX = CAMERA_POSITION_WORLD + p / distance * stand(distance) * 0.92;
            NORMAL = n;
        }
        void fragment() {
            vec4 c = paint(cloud_map, map_point(offset));
            ALBEDO = c.rgb;
            // Under the layer (or level with it) the sky draws the clouds: a flat sheet seen edge-on
            // stands along the horizon as a fence of slivers. It shows once the eye is over it.
            float over_layer = max(over_land, smoothstep(cloud_lift, cloud_lift + 200.0, height));
            ALPHA = c.a * over_layer * mix(over_land, 1.0, smoothstep(4800.0, 5800.0, max(abs(offset.x), abs(offset.y))));
            FOG = vec4(fog_color, 1.0 - exp(-fog_density * distance));
        }
        """;

    /// <summary>
    /// The air seen from outside, worked out from each eye ray against the sheet's own sphere, so
    /// it fits whatever the curve is: over the limb the glow thins away with the height of the
    /// ray's lowest point, over the disc it thins toward the middle, meeting at the ground's edge.
    /// </summary>
    const string AirSource = """
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
        varying vec3 ray;
        void vertex() {
            ray = VERTEX;
            VERTEX = CAMERA_POSITION_WORLD + VERTEX * shell;
        }
        void fragment() {
            vec3 r = normalize(ray);
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
    const string RingSource = """
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

    /// <summary>How far along the map the sheet reaches this frame, m.</summary>
    public float Reach { get; private set; }

    /// <summary>Sets the sheet up a piece at a time, each upload on its own frame, so none of them shows as a hitch.</summary>
    public IEnumerable<int> Setup(GlobePaint paint, float radius, float atmosphereRadius, OrbitRules.GlobeRules rules, Vector2 mapSize)
    {
        Radius = radius;
        _airThickness = atmosphereRadius - radius;
        _sphereReach = Mathf.DegToRad((float)rules.SphereReach);
        ArchetypeLook look = paint.Look;
        ArrayMesh grid = PolarGrid(rules.Rings, rules.Segments);
        _ground = new ShaderMaterial { Shader = new Shader { Code = GroundSource } };
        _ground.SetShaderParameter("albedo_map", ImageTexture.CreateFromImage(paint.Albedo));
        yield return 0;
        _ground.SetShaderParameter("glow_map", ImageTexture.CreateFromImage(paint.Glow));
        _ground.SetShaderParameter("relief_map", ImageTexture.CreateFromImage(paint.Relief));
        _ground.SetShaderParameter("day_glow", look.LiquidGlow > 0 ? 0.9f : 0.5f);
        _ground.SetShaderParameter("night_glow", look.GlowingNights ? 2.5f : 1.4f);
        _ground.SetShaderParameter("radius", radius);
        _ground.SetShaderParameter("map_size", mapSize);
        AddChild(Instance("Ground", grid, _ground));
        yield return 0;
        _clouds = new ShaderMaterial { Shader = new Shader { Code = CloudSource } };
        _clouds.SetShaderParameter("cloud_map", ImageTexture.CreateFromImage(paint.Clouds));
        _clouds.SetShaderParameter("radius", radius);
        _clouds.SetShaderParameter("map_size", mapSize);
        AddChild(Instance("Clouds", grid, _clouds));
        yield return 0;
        var shell = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24, CustomAabb = Everywhere };
        _air = new ShaderMaterial { Shader = new Shader { Code = AirSource } };
        _air.SetShaderParameter("color", C(look.Atmosphere));
        _air.SetShaderParameter("shell", AirShell);
        _airMesh = Instance("Atmosphere", shell, _air);
        AddChild(_airMesh);
        _shroud = new ShaderMaterial { Shader = new Shader { Code = ShroudSource }, RenderPriority = 100 };
        _shroud.SetShaderParameter("shell", ShroudShell);
        _shroudMesh = Instance("Shroud", shell, _shroud);
        _shroudMesh.Visible = false;
        AddChild(_shroudMesh);
        _ringMaterial = new ShaderMaterial { Shader = new Shader { Code = RingSource }, RenderPriority = 5 }; // nearer than anything else up there
        _ringMaterial.SetShaderParameter("color", C(look.RingColor));
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
        float sphereOut = ArcAt(_sphereReach, rho, Radius);
        foreach (ShaderMaterial m in new[] { _ground, _clouds })
        {
            m.SetShaderParameter("eye_up", v.Up);
            m.SetShaderParameter("eye_east", v.East);
            m.SetShaderParameter("eye_south", v.South);
            m.SetShaderParameter("map_up", v.MapUp);
            m.SetShaderParameter("map_east", v.MapEast);
            m.SetShaderParameter("map_south", v.MapSouth);
            m.SetShaderParameter("nadir", v.Nadir);
            m.SetShaderParameter("height", v.Height);
            m.SetShaderParameter("bend", v.Bend);
            m.SetShaderParameter("flat_from", v.FlatFrom);
            m.SetShaderParameter("flat_map", v.Flat);
            m.SetShaderParameter("taper", v.Taper);
            m.SetShaderParameter("local", v.Local);
            m.SetShaderParameter("reach", Reach);
            m.SetShaderParameter("sphere_out", sphereOut);
            m.SetShaderParameter("near_at", v.Near);
            m.SetShaderParameter("far_at", v.Far);
            m.SetShaderParameter("fog_density", v.FogDensity);
            m.SetShaderParameter("fog_color", v.FogColor);
            m.SetShaderParameter("over_land", v.OverLand);
        }
        // Over the cloud deck for the swap, under it as the far field.
        _ground.RenderPriority = v.OverLand > 0f && v.OverLand < 1f ? 1 : -2;
        _clouds.RenderPriority = _ground.RenderPriority + 1;
        _ground.SetShaderParameter("relief", v.Relief);
        _ground.SetShaderParameter("sun_dir", v.ToSun);
        _clouds.SetShaderParameter("cloud_lift", v.CloudLift);
        _air.SetShaderParameter("sun_dir", v.ToSun);
        _ring.Visible = _hasRing && v.RingShown > 0.002f;
        if (_ring.Visible)
        {
            // One centre and one scale for the ring, its shader and the sheet: nothing of it is placed apart.
            Vector3 centre = v.Eye - v.Up * (v.Height + v.RingRadius);
            _ring.GlobalTransform = new Transform3D(_ringBasis.Scaled(Vector3.One * (v.RingRadius / Radius)), centre);
            _ringMaterial.SetShaderParameter("to_sun", v.ToSun);
            _ringMaterial.SetShaderParameter("axis", _ringBasis.Y.Normalized());
            _ringMaterial.SetShaderParameter("centre", centre);
            _ringMaterial.SetShaderParameter("ring_radius", v.RingRadius);
            _ringMaterial.SetShaderParameter("eye_up", v.Up);
            _ringMaterial.SetShaderParameter("height", v.Height);
            _ringMaterial.SetShaderParameter("sheet_radius", rho);
            _ringMaterial.SetShaderParameter("stand_at", v.Near * 0.9f);
            _ringMaterial.SetShaderParameter("fade", v.RingShown);
            Vector3 sheetCentre = v.Eye - v.Up * (v.Height + rho);
            RingFit = (_ring.GlobalPosition.DistanceTo(sheetCentre), RingInner * v.RingRadius / rho);
        }
        _air.SetShaderParameter("eye_up", v.Up);
        _air.SetShaderParameter("ground", rho);
        _air.SetShaderParameter("thickness", _airThickness);
        _air.SetShaderParameter("eye_height", v.Height);
        _air.SetShaderParameter("strength", AirStrength * v.Rim);
        _airMesh.Visible = v.Rim > 0.002f;
    }

    /// <summary>The shroud over everything past the vessel: how thick (0 to 1), the air's colour low in the view and overhead, and its glow toward the sun.</summary>
    public void Shroud(float amount, Color low, Color high, Color glow, Vector3 toSun)
    {
        if (_shroud == null) return;
        _shroudMesh.Visible = amount > 0.002f;
        if (!_shroudMesh.Visible) return;
        _shroud.SetShaderParameter("amount", amount);
        _shroud.SetShaderParameter("low", low);
        _shroud.SetShaderParameter("high", high);
        _shroud.SetShaderParameter("glow", glow);
        _shroud.SetShaderParameter("sun_dir", toSun);
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
