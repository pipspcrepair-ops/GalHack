using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator;
using PoiGenerator.Gen;
using PlanetSurface.Skies;
using PlanetSurface.World;

namespace PlanetSurface.Poi;

/// <summary>
/// The lights that carry at night, cheaply (sci-fi-inspirations 17, Night Lights). Three draws for the
/// whole planet, none of them a light: every place's outward lamps, windows and beacons as points that
/// keep a few pixels wide however far off; a soft glow in the air over each cluster of lived-in places;
/// and a beam straight up from a few old installations. Positions are canonical and wrapped round the
/// world in the shaders, so nothing moves on the CPU. A place's real lights hand off to its points over
/// one band (<see cref="HandOff"/>): past it the points are all there is, and only where someone lives.
/// </summary>
public partial class FarLights : Node3D
{
    /// <summary>Where a place's real lights fade out (m from the eye, and over how far): its points carry on from there.</summary>
    public const float FadeBegin = 500f, FadeLength = 120f;

    /// <summary>The one hand-off: a real light fades out over the band its far point fades up across.</summary>
    public static void HandOff(Light3D l)
    {
        l.DistanceFadeEnabled = true;
        l.DistanceFadeBegin = FadeBegin;
        l.DistanceFadeLength = FadeLength;
    }

    /// <summary>The sky the lights read night, haze and the cloud base from (null: always night, clear).</summary>
    public SkyRig Sky;

    /// <summary>Who keeps lamps lit: people, not creatures or machines.</summary>
    static readonly HashSet<Occupant> Keepers = new() { Occupant.Scavengers, Occupant.Squatters, Occupant.Gang, Occupant.Faction, Occupant.Cult, Occupant.Keepers };

    /// <summary>Someone lives there and keeps its lights: a working place, or people in a fallen one.</summary>
    public static bool Lived(PoiSite s) => s.State == PoiState.Active || s.Occupancy is { } o && (Keepers.Contains(o.Main) || o.Second is { } two && Keepers.Contains(two));

    /// <summary>A lamp that shows from outside: a beacon, a lit window or wall lamp, a big lamp in the open (never one in a room under a roof).</summary>
    public static bool Outward(PoiInstance inst, LightSpot spot)
    {
        if (spot.Cell.Y < 0 || spot.Mode == LightMode.Dead) return false;
        Block b = inst.Blocks.Get(spot.Cell);
        return spot.Mode == LightMode.Beacon || b.Material == PoiGenerator.Gen.Material.Lumen && b.Role is Role.Window or Role.Light
            || spot.Range >= 10f && !inst.Layout.Occupied[spot.Cell];
    }

    /// <summary>The glow over a cluster of lived-in places: where, how wide and how bright (also the globe's night lights).</summary>
    public readonly record struct Glow(double X, double Z, float Y, float Radius, float Strength, int Places);

    /// <summary>How many lamps a lived-in place shows, by its size (its lights are only known once built; this is known from the start).</summary>
    public static float Lamps(PoiSite s)
    {
        if (!Lived(s)) return 0f;
        float area = (s.Footprint.Max.X - s.Footprint.Min.X + 1f) * (s.Footprint.Max.Z - s.Footprint.Min.Z + 1f);
        float lamps = Math.Clamp(area / 300f, 1f, 60f) * (s.State == PoiState.Active ? 1f : 0.6f);
        return s.Primary ? lamps * 2f : lamps;
    }

    /// <summary>Lamps in a cluster before the air over it glows: a town or a port, never a lone farm.</summary>
    public const float GlowFrom = 30f;

    /// <summary>
    /// The lived-in places gathered into clusters (any within <see cref="Gather"/> of a cluster's middle joins it),
    /// and those with lamps enough for a glow. The same list draws the glow on the ground and on the globe.
    /// </summary>
    public static List<Glow> Clusters(IReadOnlyList<PoiSite> sites, double width, double height)
    {
        var groups = new List<(double X, double Z, double Y, float Lamps, int Places)>();
        foreach (PoiSite s in sites.Where(Lived).OrderByDescending(Lamps))
        {
            double x = (s.Footprint.Min.X + s.Footprint.Max.X + 1) * 0.5, z = (s.Footprint.Min.Z + s.Footprint.Max.Z + 1) * 0.5;
            float lamps = Lamps(s);
            int at = groups.FindIndex(g => Math.Abs(Planets.Gen.Mathx.WrapDelta(x - g.X, width)) < Gather && Math.Abs(Planets.Gen.Mathx.WrapDelta(z - g.Z, height)) < Gather);
            if (at < 0) { groups.Add((x, z, s.Origin.Y, lamps, 1)); continue; }
            var g = groups[at];
            float total = g.Lamps + lamps;
            // The middle moves toward the new place by its share of the lamps, the short way round the seams.
            groups[at] = (g.X + Planets.Gen.Mathx.WrapDelta(x - g.X, width) * lamps / total, g.Z + Planets.Gen.Mathx.WrapDelta(z - g.Z, height) * lamps / total,
                g.Y + (s.Origin.Y - g.Y) * lamps / total, total, g.Places + 1);
        }
        return groups.Where(g => g.Lamps >= GlowFrom)
            .Select(g => new Glow(Planets.Gen.Mathx.WrapX(g.X, width), Planets.Gen.Mathx.Wrap(g.Z, height), (float)g.Y,
                250f + 45f * MathF.Sqrt(g.Lamps), Math.Clamp(g.Lamps / 120f, 0.25f, 1.6f), g.Places)).ToList();
    }

    /// <summary>How far apart places may stand and still glow as one, m.</summary>
    const double Gather = 700.0;

    /// <summary>The installations that throw a beam straight up: grand or landmark high-tech families, one in four rolled, at most <see cref="MostBeams"/>.</summary>
    public static List<PoiSite> Beams(IReadOnlyList<PoiSite> sites) => sites
        .Where(s => s.Siting != "city" && s.Siting != "part" && (s.Family.Landmark || s.Family.Tags.Contains("grand")) && s.Family.Techs.Contains("high"))
        .Select(s => (s, roll: Planets.Gen.Hash.Of(0xBEA4UL, s.Origin.X, s.Origin.Z) % 1000))
        .Where(t => t.roll < 250).OrderBy(t => t.roll).Take(MostBeams).Select(t => t.s).ToList();

    const int MostBeams = 3;

    MultiMesh _points, _domes, _beams;
    ShaderMaterial _pointMaterial, _domeMaterial, _beamMaterial;
    readonly List<float> _pointData = new();
    /// <summary>Points added since the last frame, to put in.</summary>
    readonly List<int> _fresh = new();
    double _clock;
    PlanetWorld _world;

    /// <summary>Points, glows and beams drawn (tests).</summary>
    public int PointCount => _pointData.Count / Stride;
    public IReadOnlyList<Glow> Glows { get; private set; } = Array.Empty<Glow>();
    public IReadOnlyList<PoiSite> Beaming { get; private set; } = Array.Empty<PoiSite>();

    /// <summary>The canonical points of each place added, by place (tests: which are lit from far off).</summary>
    public readonly Dictionary<PoiSite, (int First, int Count, bool Lived)> Of = new();

    const int Stride = 20; // a 3x4 transform, a colour and custom data

    public void Bind(PlanetWorld world)
    {
        Clear();
        _world = world;
        var size = new Vector2((float)world.Width, (float)world.Height);
        _pointMaterial = Shaded(PointSource, size);
        _domeMaterial = Shaded(DomeSource, size);
        _beamMaterial = Shaded(BeamSource, size);
        _points = Layer("Far points", new QuadMesh { Size = Vector2.One * 2f }, _pointMaterial);
        _domes = Layer("Light glow", new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 12 }, _domeMaterial);
        _beams = Layer("Beams", new QuadMesh { Size = new Vector2(2f, 1f), CenterOffset = new Vector3(0f, 0.5f, 0f) }, _beamMaterial);
        Glows = Clusters(world.Pois, world.Width, world.Height);
        Fill(_domes, Glows.Select(g => (new Vector3((float)g.X, g.Y, (float)g.Z), g.Radius, new Color(1f, 0.72f, 0.42f), new Color(g.Strength, 0f, 0f, 0f))).ToList());
        Beaming = Beams(world.Pois);
        Fill(_beams, Beaming.Select(s => (new Vector3((s.Footprint.Min.X + s.Footprint.Max.X + 1) * 0.5f, s.Footprint.Max.Y, (s.Footprint.Min.Z + s.Footprint.Max.Z + 1) * 0.5f),
            1f, new Color(0.72f, 0.86f, 1f), new Color((Planets.Gen.Hash.Of(9UL, s.Origin.X, s.Origin.Z) % 1000) / 1000f, 0f, 0f, 0f))).ToList());
    }

    static ShaderMaterial Shaded(string code, Vector2 size)
    {
        var m = new ShaderMaterial { Shader = new Shader { Code = code } };
        m.SetShaderParameter(Fx.Names.Of("world_size"), size);
        m.SetShaderParameter(Fx.Names.Of("hand_off"), new Vector2(FadeBegin, FadeLength));
        m.SetShaderParameter(Fx.Names.Of("round"), new Vector2(Terrain.TerrainStreamer.RoundFrom, Terrain.TerrainStreamer.RoundRadius));
        return m;
    }

    MultiMesh Layer(string name, Mesh mesh, ShaderMaterial material)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh };
        AddChild(new MultiMeshInstance3D
        {
            Name = name, Multimesh = mm, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Placed round the camera in the shader: never culled on the CPU.
            CustomAabb = new Aabb(new Vector3(-1e6f, -1e5f, -1e6f), new Vector3(2e6f, 2e5f, 2e6f)),
        });
        return mm;
    }

    static void Fill(MultiMesh mm, List<(Vector3 At, float Scale, Color Color, Color Custom)> items)
    {
        mm.InstanceCount = items.Count;
        for (int i = 0; i < items.Count; i++)
        {
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * items[i].Scale), items[i].At));
            mm.SetInstanceColor(i, items[i].Color);
            mm.SetInstanceCustomData(i, items[i].Custom);
        }
    }

    /// <summary>
    /// A built place's points: its outward lights, from the lights it was built with (the same list its real
    /// lights come from). A place nobody lives in keeps its points only inside the hand-off: its lamps go dark
    /// with distance, as its real lights do.
    /// </summary>
    public void Add(PoiSite site, PoiInstance inst)
    {
        if (_points == null || Of.ContainsKey(site)) return;
        bool lived = Lived(site);
        int first = PointCount, count = 0;
        int inner = 0;
        foreach (LightSpot spot in inst.Lights)
        {
            // A lamp inside shows from far off as its lit window does: past the hand-off, drawn a few metres toward the eye,
            // so the ones by an outer wall show through it. A few a place, so a city block isn't a sheet of them.
            bool outward = Outward(inst, spot);
            if (!outward && (spot.Cell.Y < 0 || spot.Mode == LightMode.Dead || ++inner > MostInner)) continue;
            Color c = spot.Style is { } style ? PoiLitFaces.At(style, spot.Energy, 0.0).Item2 : new Color(spot.Color.R / 255f, spot.Color.G / 255f, spot.Color.B / 255f);
            float mode = spot.Mode switch { LightMode.Beacon => 2f, LightMode.Flicker => 1f, LightMode.Pulse or LightMode.Bloom => 3f, _ => 0f };
            float size = spot.Mode == LightMode.Beacon ? 4f : 1.75f; // half the old halo's width
            float bright = Math.Clamp(spot.Energy / 1.4f, 0.5f, 3f);
            float phase = (float)(Hash.Cell(spot.Cell, 7) % 1000) / 160f;
            float x = site.Origin.X + spot.Cell.X + 0.5f, y = site.Origin.Y + spot.Cell.Y + 0.5f, z = site.Origin.Z + spot.Cell.Z + 0.5f;
            _pointData.AddRange(new[] { size, 0f, 0f, x, 0f, size, 0f, y, 0f, 0f, size, z, c.R, c.G, c.B, bright, mode, phase, lived ? 1f : 0f, outward ? 0f : 1f });
            _fresh.Add(PointCount - 1);
            count++;
        }
        Of[site] = (first, count, lived);
    }

    /// <summary>The most lamps inside one place that show from far off.</summary>
    const int MostInner = 12;

    public override void _Process(double delta)
    {
        if (_points == null) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        _clock += delta;
        if (_fresh.Count > 0)
        {
            // Room for twice as many when it runs out (the whole buffer then, rarely); else only the new points, which upload
            // as the few regions they fall in. A city's hundreds of places coming in never send the lot each time.
            if (PointCount > _points.InstanceCount)
            {
                var room = new float[Math.Max(PointCount * 2, 256) * Stride];
                _pointData.CopyTo(room);
                _points.InstanceCount = room.Length / Stride;
                _points.Buffer = room;
            }
            else
                foreach (int i in _fresh)
                {
                    int o = i * Stride;
                    var d = _pointData;
                    _points.SetInstanceTransform(i, new Transform3D(new Basis(new Vector3(d[o], d[o + 4], d[o + 8]), new Vector3(d[o + 1], d[o + 5], d[o + 9]), new Vector3(d[o + 2], d[o + 6], d[o + 10])), new Vector3(d[o + 3], d[o + 7], d[o + 11])));
                    _points.SetInstanceColor(i, new Color(d[o + 12], d[o + 13], d[o + 14], d[o + 15]));
                    _points.SetInstanceCustomData(i, new Color(d[o + 16], d[o + 17], d[o + 18], d[o + 19]));
                }
            _points.VisibleInstanceCount = PointCount;
            _fresh.Clear();
        }
        float night = 1f, fog = 0f, cover = 0f, cloudBase = 2000f;
        if (Sky != null)
        {
            night = 1f - Sky.Daylight;
            fog = Sky.Environment?.FogDensity ?? 0f;
            cover = Sky.Air.CloudCover;
            cloudBase = Sky.Air.CloudBase;
        }
        foreach (ShaderMaterial m in new[] { _pointMaterial, _domeMaterial, _beamMaterial })
        {
            m.SetShaderParameter(Fx.Names.Of("night"), night);
            m.SetShaderParameter(Fx.Names.Of("fog_density"), fog);
            m.SetShaderParameter(Fx.Names.Of("clock"), (float)(_clock % 3600.0));
        }
        _domeMaterial.SetShaderParameter(Fx.Names.Of("cloud"), new Vector2(cloudBase, cover));
        LastMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    /// <summary>The main thread's time on the far lights last frame, ms (tests).</summary>
    public double LastMs { get; private set; }

    public void Clear()
    {
        foreach (Node n in GetChildren()) n.QueueFree();
        _points = _domes = _beams = null;
        _pointData.Clear();
        _fresh.Clear();
        Of.Clear();
        Glows = Array.Empty<Glow>();
        Beaming = Array.Empty<PoiSite>();
        _world = null;
    }

    // ---- Shaders --------------------------------------------------------------------------------

    /// <summary>Shared by the three: an instance's canonical position wrapped to the copy nearest the eye, and sunk over the round horizon.</summary>
    const string PlaceCode = """
        uniform vec2 world_size = vec2(16000.0, 8000.0);
        uniform vec2 hand_off = vec2(500.0, 120.0);
        uniform vec2 round = vec2(2000.0, 300000.0);
        uniform float night = 1.0;
        uniform float fog_density = 0.0;
        uniform float clock = 0.0;
        vec3 near_copy(vec3 at, vec3 eye) {
            at.xz = eye.xz + mod(at.xz - eye.xz + world_size * 0.5, world_size) - world_size * 0.5;
            float d = max(length(at.xz - eye.xz) - round.x, 0.0);
            at.y -= d * d / (2.0 * round.y);
            return at;
        }
        // A lamp is brighter than what it lights: it shows through more of the haze than the ground does.
        float haze(float d) { return exp(-fog_density * d * 0.3); }
        """;

    const string PointSource = $$"""
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;
        {{PlaceCode}}
        varying vec3 tint;
        varying float amount;
        void vertex() {
            vec3 eye = INV_VIEW_MATRIX[3].xyz;
            vec3 at = near_copy(MODEL_MATRIX[3].xyz, eye);
            float d = length(at - eye);
            // At least two pixels across however far off, a little less bright for it.
            float world = length(MODEL_MATRIX[0].xyz);
            float pixel = d * 2.0 / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y);
            float size = max(world, pixel * 2.2);
            vec3 view = (VIEW_MATRIX * vec4(at, 1.0)).xyz;
            float inner = INSTANCE_CUSTOM.w;
            // In front of its own lamp's block; far off, in front of its place's far mesh too (solid in 4 m cells); a lamp inside, through its wall.
            float far = smoothstep(hand_off.x, hand_off.x + hand_off.y, d);
            view += normalize(-view) * min(max(mix(0.8, 6.0, far), 8.0 * inner), d * 0.5);
            POSITION = PROJECTION_MATRIX * vec4(view + vec3(VERTEX.xy * size, 0.0), 1.0);
            float mode = INSTANCE_CUSTOM.x, s = clock + INSTANCE_CUSTOM.y;
            float wave = mode > 1.5 && mode < 2.5 ? (mod(s, 1.6) < 0.25 ? 1.0 : 0.05)
                : mode > 0.5 && mode < 1.5 ? (sin(s * 23.0) * sin(s * 7.3) > 0.55 ? 0.15 : 0.9)
                : mode > 2.5 ? 0.6 + 0.4 * sin(s * 0.9) : 1.0;
            // The old halo's 40 to 120 m fade near; full past the hand-off, where it is all that is left of the lamp.
            float a = smoothstep(40.0, 120.0, d) * mix(0.55, 1.0, far) * mix(1.0, far * 0.8, inner);
            // Nobody lives there: its lamps go out with its real lights.
            a *= mix(1.0 - far, 1.0, INSTANCE_CUSTOM.z);
            // By day a near halo shows faintly, as it did; far off only after dark.
            a *= max(night, 0.4 * (1.0 - far));
            a *= wave * haze(d) * COLOR.a * mix(1.0, clamp(world / (pixel * 2.2), 0.6, 1.0), far);
            tint = COLOR.rgb;
            amount = a;
        }
        void fragment() {
            float r = length(UV * 2.0 - 1.0);
            float core = exp(-r * r * 9.0) + 0.25 * exp(-r * r * 2.5);
            ALBEDO = tint * amount * core * 2.4;
            ALPHA = 1.0;
        }
        """;

    const string DomeSource = $$"""
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never, cull_front, fog_disabled, shadows_disabled;
        {{PlaceCode}}
        uniform vec2 cloud = vec2(2000.0, 0.0);
        varying vec3 centre;
        varying float radius;
        varying float strength;
        varying vec3 world;
        void vertex() {
            vec3 eye = INV_VIEW_MATRIX[3].xyz;
            centre = near_copy(MODEL_MATRIX[3].xyz, eye);
            radius = length(MODEL_MATRIX[0].xyz);
            strength = INSTANCE_CUSTOM.x;
            world = centre + VERTEX * radius;
            POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world, 1.0);
        }
        // The lit air in the dome along a point: thickest low over the middle, and where low cloud lies in it.
        float lit(vec3 p) {
            vec3 o = (p - centre) / radius;
            float h = max(o.y, 0.0);
            float air = pow(max(1.0 - dot(o, o), 0.0), 1.5) * exp(-h * 2.0);
            float deck = cloud.y * exp(-pow((p.y - cloud.x) / (radius * 0.15), 2.0));
            return air * (1.0 + 4.0 * deck);
        }
        void fragment() {
            vec3 eye = INV_VIEW_MATRIX[3].xyz;
            vec3 ray = normalize(world - eye);
            // Where the eye's ray runs through the dome, above the ground it stands on.
            vec3 oc = eye - centre;
            float b = dot(oc, ray), c = dot(oc, oc) - radius * radius;
            float disc = b * b - c;
            if (disc <= 0.0) discard;
            float t0 = max(-b - sqrt(disc), 0.0), t1 = -b + sqrt(disc);
            if (abs(ray.y) > 1e-4) {
                float tg = (centre.y - eye.y) / ray.y;
                if (ray.y < 0.0) t1 = min(t1, tg); else t0 = max(t0, tg);
            }
            if (t1 <= t0) discard;
            float sum = 0.0;
            for (int i = 0; i < 4; i++) sum += lit(eye + ray * mix(t0, t1, (float(i) + 0.5) / 4.0));
            float along = (t1 - t0) / radius * sum / 4.0;
            float d = length(centre - eye);
            ALBEDO = COLOR.rgb * along * strength * 0.06 * night * haze(max(d - radius, 0.0));
            ALPHA = 1.0;
        }
        """;

    const string BeamSource = $$"""
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;
        {{PlaceCode}}
        varying float up;
        varying float amount;
        void vertex() {
            vec3 eye = INV_VIEW_MATRIX[3].xyz;
            vec3 at = near_copy(MODEL_MATRIX[3].xyz, eye);
            float d = length(at.xz - eye.xz);
            // Turned about the upright to face the eye; at least a pixel and a half wide.
            vec3 side = normalize(cross(vec3(0.0, 1.0, 0.0), at - eye + vec3(0.001, 0.0, 0.0)));
            float pixel = max(d, 1.0) * 2.0 / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y);
            float width = max(3.0, pixel * 1.5);
            vec3 p = at + side * VERTEX.x * width + vec3(0.0, VERTEX.y * 1400.0, 0.0);
            POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(p, 1.0);
            up = VERTEX.y;
            // A slow swell, each beam its own; seen only after dark, and through the haze.
            amount = night * (0.75 + 0.25 * sin(clock * 0.4 + INSTANCE_CUSTOM.x * 6.283)) * haze(d) * clamp(3.0 / width, 0.4, 1.0);
        }
        void fragment() {
            float across = 1.0 - abs(UV.x * 2.0 - 1.0);
            ALBEDO = COLOR.rgb * amount * across * across * (1.0 - up) * (1.0 - up) * 1.4;
            ALPHA = 1.0;
        }
        """;
}
