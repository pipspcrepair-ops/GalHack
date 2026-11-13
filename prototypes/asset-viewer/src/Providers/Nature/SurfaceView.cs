using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Skies;
using PlanetSurface.Terrain;
using PlanetSurface.World;

namespace AssetViewer.Providers.Nature;

/// <summary>
/// Planets for the viewer, built as the game builds them (planet-surface's world, no places) and kept
/// a few at a time, so changing a weather or a time of day does not generate the world again.
/// </summary>
public static class NatureWorlds
{
    /// <summary>The width of the worlds the viewer builds, km: the smallest the game rolls, which generates fastest.</summary>
    public const int WidthKm = 8;

    static readonly List<(PlanetSpec Spec, PlanetWorld World)> Cache = new();
    static bool _ready;

    public static string Prototypes => ProjectSettings.GlobalizePath("res://") + "../";

    /// <summary>What the game's streamer and its worlds need once, before the first world.</summary>
    public static void Prepare()
    {
        if (_ready) return;
        _ready = true;
        PlanetTerrain.ConfigureWorldBounds();
        FarBuilder.Solids ??= FloraSolidRules.Load(Prototypes + "planet-surface/data/flora-solids.json");
        // The globals the game's shaders read; the viewer's project declares the first four.
        RenderingServer.GlobalShaderParameterSet("planet_size", new Vector2(WidthKm * 1000f, WidthKm * 500f));
    }

    public static PlanetSpec SpecOf(Archetype archetype, int variant, ulong seed, Modifier? modifiers = null)
    {
        PlanetSpec spec = PlanetSpec.Roll(seed, archetype, variant, WidthKm);
        return modifiers is Modifier m ? spec with { Modifiers = m } : spec;
    }

    /// <summary>The world of a spec, generated once (a few seconds) and then kept.</summary>
    public static PlanetWorld Get(PlanetSpec spec)
    {
        Prepare();
        for (int i = 0; i < Cache.Count; i++)
            if (Cache[i].Spec == spec)
            {
                var hit = Cache[i];
                Cache.RemoveAt(i);
                Cache.Insert(0, hit);
                return hit.World;
            }
        PoiGenerator.Gen.WorldForce.Current = new PoiGenerator.Gen.WorldForce();
        PlanetWorld world = PlanetWorld.Build(spec, null);
        Cache.Insert(0, (spec, world));
        while (Cache.Count > 3) Cache.RemoveAt(Cache.Count - 1);
        return world;
    }

    static readonly List<(PlanetSpec Spec, float Depth, PlanetWorld World)> DeepCache = new();

    /// <summary>The world of a spec whose star lies at a depth in the galaxy asked for (0 on the rim to 1 at the core).</summary>
    public static PlanetWorld Get(PlanetSpec spec, float depth)
    {
        Prepare();
        for (int i = 0; i < DeepCache.Count; i++)
            if (DeepCache[i].Spec == spec && DeepCache[i].Depth == depth) return DeepCache[i].World;
        PoiGenerator.Gen.WorldForce.Current = new PoiGenerator.Gen.WorldForce { Depth = depth };
        PlanetWorld world;
        try { world = PlanetWorld.Build(spec, null); }
        finally { PoiGenerator.Gen.WorldForce.Current = new PoiGenerator.Gen.WorldForce(); }
        DeepCache.Insert(0, (spec, depth, world));
        while (DeepCache.Count > 2) DeepCache.RemoveAt(DeepCache.Count - 1);
        return world;
    }

    public static string ArchetypeName(Archetype a, int variant) => $"{a} {Archetypes.Get(a, variant).VariantName}";

    /// <summary>Every world type the generator has, as (archetype, variant, label), read off its table.</summary>
    public static IEnumerable<(Archetype Archetype, int Variant, string Label)> Types()
    {
        foreach (Archetype a in Enum.GetValues<Archetype>())
            for (int v = 0; v < PlanetSpec.VariantCount; v++)
                yield return (a, v, ArchetypeName(a, v));
    }
}

/// <summary>
/// A stretch of a planet's surface as the game draws it: its streamed terrain, plants, sea, sky rig and
/// light, round a spot, seen from the viewer's camera. The base of the weather, terrain and sky assets.
/// </summary>
public partial class SurfaceView : AssetInstance
{
    public PlanetWorld World { get; }
    public SkyRig Sky { get; private set; }
    public TerrainStreamer Streamer { get; private set; }
    public MeshInstance3D Sea { get; private set; }

    /// <summary>Where the view is about: a point on the ground, in the game's frame.</summary>
    public Vector3 Spot { get; protected set; }

    /// <summary>Half the side of the box the viewer frames, m.</summary>
    protected float HalfSize = 260f;
    protected float Height = 140f;

    /// <summary>Time of day to open at, 0 midnight to 1, 0.5 noon.</summary>
    public double OpenTime = 0.42;

    /// <summary>How the camera pulls the streaming focus: false keeps it on the spot.</summary>
    protected bool FocusFollowsCamera = true;
    ShaderMaterial _water, _seaMaterial;
    bool _settled;
    protected double SettleSeconds = 5.0;

    public SurfaceView(PlanetWorld world, Vector3? spot = null, float clearAhead = 0f)
    {
        World = world;
        Spot = spot ?? OpenSpot(world, clearAhead);
        Stage.OwnsEnvironment = true;
        Stage.Ground = false;
        Stage.Background = "sky";
        Sky = new SkyRig { Name = "Sky" };
        AddChild(Sky);
        Streamer = new TerrainStreamer { Name = "Terrain" };
        AddChild(Streamer);
        Sea = new MeshInstance3D
        {
            Name = "Sea", Mesh = new PlaneMesh { Size = new Vector2(24000f, 24000f), SubdivideWidth = 95, SubdivideDepth = 95 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = TerrainStreamer.RoundSink(17000f),
        };
        AddChild(Sea);
    }

    /// <summary>
    /// An open stretch of dry land near the landing site: flat all round, and clear for <paramref name="ahead"/> metres
    /// towards -Z (where the camera looks), so the view is of the weather and not of a cliff.
    /// </summary>
    public static Vector3 OpenSpot(PlanetWorld world, float ahead = 0f, float reach = 1800f)
    {
        float cx = (float)world.Landing.X, cz = (float)world.Landing.Y;
        Vector3 best = default;
        float bestScore = float.MaxValue;
        for (float dz = -reach; dz <= reach; dz += 150f)
        for (float dx = -reach; dx <= reach; dx += 150f)
        {
            float x = cx + dx, z = cz + dz;
            float h = world.GroundHeight(x, z);
            if (h < 6f) continue;
            float score = Mathf.Abs(dx) * 0.002f + Mathf.Abs(dz) * 0.002f + TreesAt(world, x, z) * 14f;
            for (int ring = 1; ring <= 3; ring++)
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.Tau / 8f, r = ring * 110f;
                score += Mathf.Abs(world.GroundHeight(x + Mathf.Cos(a) * r, z + Mathf.Sin(a) * r) - h) * 0.6f;
            }
            for (float d = 100f; d <= ahead; d += 100f)
            {
                float p = world.GroundHeight(x, z - d);
                score += Mathf.Max(0f, p - h - 12f) * 3f + (p < 1f ? 8f : 0f);
            }
            if (score < bestScore) { bestScore = score; best = new Vector3(x, h, z); }
        }
        return bestScore == float.MaxValue ? new Vector3(cx, world.GroundHeight(cx, cz), cz) : best;
    }

    /// <summary>How thickly trees stand at a point (the scatter's tree densities of its biome), for placing a clear view.</summary>
    public static float TreesAt(PlanetWorld world, double x, double z)
    {
        Biome biome = world.Palette.BiomeAt(Mathx.WrapX(x, world.Width), Mathx.Wrap(z, world.Height));
        float trees = 0f;
        foreach (Planets.Look.FloraRule r in world.Flora.RulesOf(biome))
            if (r.Kind is Planets.Look.Flora.Conifer or Planets.Look.Flora.Broadleaf or Planets.Look.Flora.JungleTree or Planets.Look.Flora.Palm or Planets.Look.Flora.GiantMushroom)
                trees += r.Density;
        return trees;
    }

    Vector3 Landing()
    {
        float x = (float)World.Landing.X, z = (float)World.Landing.Y;
        return new Vector3(x, World.GroundHeight(x, z), z);
    }

    public override bool Spins => false;

    public override Aabb? FrameBounds
    {
        get
        {
            float ground = World.GroundHeight(Spot.X, Spot.Z);
            return new Aabb(new Vector3(Spot.X - HalfSize, ground - 10f, Spot.Z - HalfSize), new Vector3(HalfSize * 2f, Height, HalfSize * 2f));
        }
    }

    /// <summary>The ground height under a spot, never below the sea.</summary>
    public float GroundAt(float x, float z) => Mathf.Max(0f, World.GroundHeight(x, z));

    public override void _Ready()
    {
        Spot = new Vector3(Spot.X, World.GroundHeight(Spot.X, Spot.Z), Spot.Z);
        WorldLook look = World.Look;
        _water = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
        _water.SetShaderParameter("shallow", look.Shallow);
        _water.SetShaderParameter("deep", look.Deep);
        _water.SetShaderParameter("lava", look.Lava);
        _water.SetShaderParameter("glow", look.LiquidGlow);
        _seaMaterial = (ShaderMaterial)_water.Duplicate();
        _seaMaterial.SetShaderParameter("sea", true);
        _seaMaterial.SetShaderParameter("sea_map", World.SeaTexture());
        _seaMaterial.SetShaderParameter("sea_mask", PlanetTerrainSource.SeaMaskDepth);
        Sea.MaterialOverride = _seaMaterial;
        RenderingServer.GlobalShaderParameterSet("planet_size", new Vector2((float)World.Width, (float)World.Height));
        Streamer.SetWorld(World, Spot, _water);
        Streamer.CollisionEnabled = false;
        Sky.SetWorld(World);
        Sky.TimeOfDay = OpenTime;
        Sky.Paused = true;
        AfterWorld();
        Frame(0.016);
        Settle();
    }

    /// <summary>One step of whatever else streams in beside the terrain, while the first frame is waited for.</summary>
    protected virtual void SettleStep() { }

    /// <summary>Called once the world is wired into the sky and the streamer; weather and the like hook in here.</summary>
    protected virtual void AfterWorld() { }

    /// <summary>Runs the streamer until the ground round the spot is built (or a few seconds pass), so the first frame shows land and not a hole.</summary>
    public void Settle()
    {
        var watch = Stopwatch.StartNew();
        Streamer.UploadBudgetMs = 40.0;
        Camera3D cam = GetViewport()?.GetCamera3D();
        while (watch.Elapsed.TotalSeconds < SettleSeconds)
        {
            Frame(0.016);
            Streamer._Process(0.016);
            SettleStep();
            if (Streamer.Pending == 0 && Streamer.ReadyAround(Spot, 1, false) && Streamer.FarComplete) break;
            System.Threading.Thread.Sleep(3);
        }
        Streamer.UploadBudgetMs = 3.0;
        _settled = true;
        SettledIn = watch.Elapsed.TotalSeconds;
    }

    public double SettledIn { get; private set; }

    /// <summary>Where the streamer should centre: the spot from afar, the camera when it comes close.</summary>
    Vector3 StreamFocus()
    {
        Camera3D cam = IsInsideTree() ? GetViewport()?.GetCamera3D() : null;
        if (cam == null || !FocusFollowsCamera) return Spot;
        Vector3 eye = cam.GlobalPosition;
        float distance = eye.DistanceTo(Spot);
        float near = Mathf.SmoothStep(150f, 450f, distance);
        Vector3 focus = eye.Lerp(Spot, near);
        focus.Y = Mathf.Max(focus.Y, 0f);
        return focus;
    }

    /// <summary>The weather's air for this frame; the weather view overrides it.</summary>
    protected virtual PlanetSurface.Skies.AtmosphereState Air() => Atmosphere.Compute(World.Sky, Atmosphere.Clear(), Sky.ToSun.Y);

    protected virtual Vector2 Wind => Vector2.Zero;

    /// <summary>One step of everything the view keeps running.</summary>
    protected void Frame(double delta)
    {
        Vector3 focus = StreamFocus();
        Streamer.SetFocus(focus);
        float latitude = (float)(Mathx.Wrap(Spot.Z, World.Height) / World.Height * 2.0 - 1.0);
        Sky.Advance(delta, latitude, Air(), Wind);
        Vector3 eye = IsInsideTree() && GetViewport()?.GetCamera3D() is Camera3D c ? c.GlobalPosition : Spot + Vector3.Up * 50f;
        Sea.GlobalPosition = new Vector3(eye.X, 0f, eye.Z);
        Sky.PlaceCloudDeck(eye);
        float day = 0.15f + 0.85f * Sky.Daylight;
        _seaMaterial?.SetShaderParameter("daylight", day);
        _water?.SetShaderParameter("daylight", day);
        // Morning fog pools on the low ground: the floor of the land round the spot.
        _valleyTimer -= delta;
        if (_valleyTimer <= 0)
        {
            _valleyTimer = 2.0;
            var heights = new List<float>(49);
            for (int r = 1; r <= 3; r++)
            for (int a = 0; a < 16; a++)
            {
                double angle = a * Math.Tau / 16;
                heights.Add(Math.Max(0f, World.Map.Sample(World.Map.Elevation,
                    Mathx.WrapX(Spot.X + Math.Cos(angle) * r * 350, World.Width), Mathx.Wrap(Spot.Z + Math.Sin(angle) * r * 350, World.Height))));
            }
            heights.Add(Math.Max(0f, World.GroundHeight(Spot.X, Spot.Z)));
            heights.Sort();
            _valleyFloor = heights[heights.Count / 6];
        }
        Sky.SetValleyFog(_valleyFloor, Sky.Air);
    }

    double _valleyTimer;
    float _valleyFloor;

    public override void _Process(double delta)
    {
        if (!_settled) return;
        Frame(delta);
    }

    public override void Release()
    {
        Streamer?.Clear();
    }

    protected string TimeName() => OpenTime switch { < 0.2 or >= 0.9 => "Night", < 0.32 => "Dawn", < 0.45 => "Morning", < 0.58 => "Noon", < 0.7 => "Afternoon", < 0.8 => "Dusk", _ => "Evening" };

    public static readonly string[] Times = { "Dawn", "Morning", "Noon", "Afternoon", "Dusk", "Night" };

    public static double TimeValue(string name) => name switch
    {
        "Dawn" => 0.26, "Morning" => 0.36, "Noon" => 0.5, "Afternoon" => 0.64, "Dusk" => 0.74, "Night" => 0.97, _ => 0.42,
    };
}
