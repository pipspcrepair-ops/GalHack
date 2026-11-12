using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Skies;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using Slice.Game;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Surface;

/// <summary>
/// A planet's surface as one zone: the generated planet streamed around a focus (planet-surface's
/// streamer, sky and sea), its weather and wildlife, and one shared physics world in which the
/// player, the pod and every vessel are registered bodies. It knows nothing about input, cameras
/// or the HUD; the game and the tests drive it the same way.
///
/// The planet is a torus. Whatever the focus follows stays on the planet itself: past an edge,
/// everything in the zone moves by a whole planet and the streamer re-keys its chunks.
/// </summary>
public partial class SurfaceZone : Node3D
{
    public GameData Data;
    public PlanetSpec Spec;

    /// <summary>What the new game forced onto this world's roll (depth, theme, fall, mid-collapse, eras), or null for nothing.</summary>
    public PoiGenerator.Gen.WorldForce Force;

    /// <summary>
    /// Another body of a world's system (its moon), made from <see cref="Body"/>'s own spec: it shares
    /// that world's galaxy, star and system (its sky is the system seen from here), and its places
    /// come later. Null: the start world, which owns the system.
    /// </summary>
    public PlanetWorld ShareFrom;
    public Orbiter Body;

    /// <summary>A moon or another body of the start world's system, not the start world itself.</summary>
    public bool IsOtherBody => ShareFrom != null;

    /// <summary>Where streaming centres: the player, or the vessel they fly.</summary>
    public Func<Vector3> Focus = () => Vector3.Zero;

    /// <summary>The camera that draws this zone (weather effects face it). Set by whoever owns it.</summary>
    public Camera3D Camera;

    public PlanetWorld Planet { get; private set; }
    public TerrainStreamer Streamer { get; private set; }
    public SkyRig Sky { get; private set; }
    public SurfaceWeather Weather { get; protected set; }
    public WildlifePatch Wildlife { get; protected set; }
    public BloodFx Blood { get; protected set; }
    public PhysicsWorld Physics { get; protected set; }
    public PhysicsHost Host { get; protected set; }
    public PlanetEdits Edits { get; private set; }
    public PlanetGround Ground { get; private set; }

    /// <summary>What daylight reaches, and how lit any spot is.</summary>
    public SkyLight Light { get; private set; }
    public MeshInstance3D Sea { get; private set; }

    /// <summary>The seas and rivers as a volume: what bodies swim in, and what the view sinks into.</summary>
    public PlanetWater Water { get; private set; }

    /// <summary>The dust and flying debris (set by the game once it has them): puffs from digging, landing and thrusters.</summary>
    public PlanetSurface.Fx.Debris Dust;

    /// <summary>What the camera sees under the surface.</summary>
    public UnderwaterView Underwater { get; protected set; }

    ShaderMaterial _seaMaterial, _riverMaterial;
    Vector3 _seaRim = PlanetSurface.Terrain.TerrainStreamer.NoRim;

    /// <summary>Where the sea plane ends: at the land's own rim while the far field stands behind it (<see cref="PlanetSurface.Terrain.TerrainStreamer.HorizonRim"/>).</summary>
    public Vector3 SeaRim
    {
        get => _seaRim;
        set
        {
            _seaRim = value;
            (Sea.MaterialOverride as ShaderMaterial)?.SetShaderParameter("rim", value);
        }
    }

    /// <summary>Planet-surface's synthesised sounds (digging, building, thunder).</summary>
    public PlanetSurface.Fx.Sfx Sfx { get; protected set; }

    /// <summary>Every vessel, base, wreck and pod in the zone.</summary>
    public readonly List<Vessel> Vessels = new();

    /// <summary>Standalone devices set down on the ground (the survival constructor).</summary>
    public readonly List<Device> Devices = new();

    /// <summary>The world's one game-mode setting (shared/Items): placement, every constructor and the tech tree read it.</summary>
    public readonly GalHack.Items.WorldRules WorldRules = new();

    /// <summary>Containers on the ground: creature bodies, backpacks, things set down.</summary>
    public Slice.Items.Lootables Loot { get; protected set; }

    public bool IsBuilt => Planet != null;
    public int FrameShifts { get; private set; }

    /// <summary>The planet has been generated and its world set up (fires once, on the main thread).</summary>
    public event Action Built;

    /// <summary>Everything in the zone just moved by this much (a seam was crossed).</summary>
    public event Action<Vector3> FrameShifted;

    Task<PlanetWorld> _building;
    readonly Dictionary<Body, Vessel> _vesselOf = new();
    double _valleyTimer;
    float _valleyFloor;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 50; // after the physics host (-10) and game logic (0): frame shifts see where things ended up
        Data ??= GameData.Shared;
        SliceRules.PlanetRules p = Data.Rules.Planet;
        // A zone made without a world (a tool, a probe): the world the slice's tests are tuned on.
        Spec ??= Slice.Game.WorldChoice.Tested.Spec();
        PlanetTerrain.ConfigureWorldBounds();
        // Trunks and boulders are solid (planet-surface's rules): colliders in the near rings.
        PlanetSurface.Terrain.FarBuilder.Solids ??= PlanetSurface.Terrain.FloraSolidRules.Load(GameData.Proto("planet-surface", "data", "flora-solids.json"));

        SkyLit.Watch(GetTree());
        Sky = new SkyRig { Name = "Sky", TimeOfDay = p.StartTimeOfDay };
        AddChild(Sky);
        Light = new SkyLight { Name = "SkyLight", Zone = this };
        AddChild(Light);
        Streamer = new TerrainStreamer { Name = "Terrain" };
        AddChild(Streamer);
        Sea = new MeshInstance3D
        {
            // Subdivided so its far side can sink over the horizon with the land (round_sink in the water shader).
            Name = "Sea", Mesh = new PlaneMesh { Size = new Vector2(24000f, 24000f), SubdivideWidth = 95, SubdivideDepth = 95 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = PlanetSurface.Terrain.TerrainStreamer.RoundSink(17000f),
        };
        AddChild(Sea);
        Sfx = new PlanetSurface.Fx.Sfx { Name = "Sfx" };
        AddChild(Sfx);

        Physics = new PhysicsWorld(Data.Physics);
        Physics.Zone.Strength = p.Gravity;
        Physics.Zone.Ceiling = p.Ceiling;
        Host = new PhysicsHost(Physics);
        AddChild(Host);
        Physics.MotionChanged += body => VesselOf(body)?.BecomeWreck();
        Physics.Unregistered += body => _vesselOf.Remove(body);

        Weather = new SurfaceWeather { Name = "Weather", Zone = this };
        AddChild(Weather);
        Wildlife = new WildlifePatch { Name = "Wildlife", Zone = this };
        AddChild(Wildlife);
        Blood = new BloodFx { Name = "Blood" };
        AddChild(Blood);
        Underwater = new UnderwaterView { Name = "Underwater", Zone = this };
        AddChild(Underwater);
        Loot = new Slice.Items.Lootables { Name = "Loot", Items = Data.Items, BodyAt = c => Wildlife.WorldOf(c), Settle = FloorUnder };
        AddChild(Loot);

        // Places collide on the world layer: walkers, vessels, E and the tool meet them (planet-surface's own layer is its own).
        PlanetSurface.Poi.PoiLayer.PlaceLayer = GalHack.Physics.Layers.World;
        PlanetSurface.Poi.PoiLayer.Tiered = true; // places are drawn by distance; a near one stands as a structure (Places/PoiPlaces)
        PlanetSurface.Infra.InfraLayer.OreTellsCollide = true; // ore rocks are solid; the survival tool breaks them down for a little ore
        Pois = new PlanetSurface.Poi.PoiLayer { Name = "Pois" };
        AddChild(Pois);
        Infra = new PlanetSurface.Infra.InfraLayer { Name = "Infra" };
        AddChild(Infra);
        PlanetSpec spec = Spec;
        if (ShareFrom != null)
        {
            // Another body of the system: no places yet (they come with the vacuum worlds' own families), its sky the system's.
            PlanetWorld of = ShareFrom;
            Orbiter body = Body;
            _building = Task.Run(() =>
            {
                PlanetWorld w = PlanetWorld.Build(spec, null);
                w.ShareSystem(of, body);
                return w;
            });
            return;
        }
        PlanetSurface.Poi.PoiCatalog catalog = PoiCatalog;
        // Every game starts on a start world: no faction holds ground there (the POI siting's rule).
        PlanetSurface.Poi.PoiSiting.StartWorld = true;
        // And what the new game forced onto its roll (its depth, theme, fall, mid-collapse and eras), or nothing.
        PoiGenerator.Gen.WorldForce.Current = Force ?? new PoiGenerator.Gen.WorldForce();
        _building = Task.Run(() => PlanetWorld.Build(spec, catalog));
    }

    // ---- Arriving --------------------------------------------------------------------------

    /// <summary>Waits for the planet to finish generating (tests and screenshots, which have nothing else to do).</summary>
    public void WaitForPlanet()
    {
        if (_building == null) return;
        _building.Wait();
        Arrive();
    }

    void Arrive()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        ArriveInner();
        ArriveMs = watch.Elapsed.TotalMilliseconds;
        Built?.Invoke();
    }

    void ArriveInner()
    {
        if (_building.IsFaulted) throw new InvalidOperationException("the planet failed to build", _building.Exception);
        Planet = _building.Result;
        _building = null;
        if (!IsOtherBody) Use(); // another body's is set when you get there: the world you are on keeps its own till then

        Edits = new PlanetEdits(Planet.Terrain, Streamer, Physics);
        Edits.Edited += edit => Infra.GroundDug(edit.Removed); // an ore rock doesn't hang on over a hole
        Edits.Edited += _ => Light.Touch();
        Physics.Terrain = Edits;
        Ground = new PlanetGround(Planet, Streamer) { Ceiling = Physics.Zone.Ceiling };
        Physics.StuckFields.Add(Ground);
        Water = new PlanetWater(Planet);
        Physics.Water = Water;

        Pois.Build(Planet);
        Infra.Build(Planet);
        Vector3 landing = Start = PickStart?.Invoke() ?? LandingSite;
        Streamer.SetWorld(Planet, landing, _riverMaterial = WaterMaterial(sea: false));
        Sea.MaterialOverride = _seaMaterial = WaterMaterial(sea: true);
        Sky.SetWorld(Planet);
        Underwater.SetWorld(Planet);
        Weather.SetWorld(Planet);
        Wildlife.SetWorld(Planet, landing);
    }

    /// <summary>Finishes arriving once the planet has been made, without waiting for it (a body made while you are elsewhere). True once it has.</summary>
    public bool TryArrive()
    {
        if (Planet != null) return true;
        if (_building == null || !_building.IsCompleted) return false;
        Arrive();
        return true;
    }

    /// <summary>The world, once made, whether or not it has arrived in the scene yet (any thread may read its maps).</summary>
    public PlanetWorld Made => Planet ?? (_building is { IsCompletedSuccessfully: true } b ? b.Result : null);

    /// <summary>How long arriving took on the main thread, ms (tests read it: it must not show as a hitch).</summary>
    public double ArriveMs { get; private set; }

    /// <summary>
    /// This is the world you are on now: the shaders' world-wide settings are its own (its map's
    /// size, and on a body with no air, no weather on the ground).
    /// </summary>
    public void Use()
    {
        if (Planet == null) return;
        RenderingServer.GlobalShaderParameterSet("planet_size", new Vector2((float)Planet.Width, (float)Planet.Height));
        if (Weather?.Sim != null) return;
        RenderingServer.GlobalShaderParameterSet("weather_wind", Vector4.Zero);
        RenderingServer.GlobalShaderParameterSet("weather_ground", Vector4.Zero);
        RenderingServer.GlobalShaderParameterSet("weather_flash", Vector4.Zero);
    }

    /// <summary>
    /// Picks where the game starts, once the planet is generated and before any ground streams
    /// in (the game's bunker site); the streaming and the creatures' calm opening centre there.
    /// Null, or returning null: the generator's landing site.
    /// </summary>
    public Func<Vector3?> PickStart;

    /// <summary>The places on the planet (ruins, wrecks, abandoned works), drawn by planet-surface's POI layer.</summary>
    public PlanetSurface.Poi.PoiLayer Pois { get; private set; }
    public PlanetSurface.Infra.InfraLayer Infra { get; private set; }

    static PlanetSurface.Poi.PoiCatalog _poiCatalog;
    /// <summary>The POI generator's families and pieces, read once.</summary>
    public static PlanetSurface.Poi.PoiCatalog PoiCatalog => _poiCatalog ??= PlanetSurface.Poi.PoiCatalog.Load(GameData.Proto("poi-generator", "data"));

    /// <summary>Where the game starts, on the ground.</summary>
    public Vector3 Start { get; private set; }

    /// <summary>The generator's landing site, on the ground.</summary>
    public Vector3 LandingSite => GroundAt((float)Planet.Landing.X, (float)Planet.Landing.Y);

    /// <summary>
    /// The first floor at or below a point that someone on foot stands on (a place's floor, a deck,
    /// the dug ground), and never above the point: a thing left inside a ceiling or on a ledge out of
    /// reach comes down to it. With nothing below in reach, the natural ground.
    /// </summary>
    public Vector3 FloorUnder(Vector3 p)
    {
        var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 0.3f, p + Vector3.Down * 80f, GalHack.Physics.Layers.Solid);
        q.HitFromInside = false;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count > 0) return (Vector3)hit["position"];
        Vector3 ground = GroundAt(p.X, p.Z);
        return float.IsFinite(ground.Y) ? ground : p;
    }

    /// <summary>The natural ground (before digging) under a frame position.</summary>
    public virtual Vector3 GroundAt(float x, float z) => new(x, Planet.GroundHeight(Mathx.WrapX(x, Planet.Width), Mathx.Wrap(z, Planet.Height)), z);

    ShaderMaterial WaterMaterial(bool sea)
    {
        WorldLook look = Planet.Look;
        var water = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
        water.SetShaderParameter("shallow", look.Shallow);
        water.SetShaderParameter("deep", look.Deep);
        water.SetShaderParameter("lava", look.Lava);
        water.SetShaderParameter("glow", look.LiquidGlow);
        if (sea)
        {
            water.SetShaderParameter("sea", true);
            water.SetShaderParameter("sea_map", Planet.SeaTexture());
            water.SetShaderParameter("sea_mask", GalHack.Terrain.PlanetTerrainSource.SeaMaskDepth);
        }
        return water;
    }

    // ---- Dry ground over the sea --------------------------------------------------------------

    const int DryTexels = 256; // a metre each, round the eye
    Image _dry;
    ImageTexture _dryTexture;
    Vector3 _dryAt = new(float.NaN, 0f, 0f);

    /// <summary>
    /// The sea plane is drawn by the world map's cells. A place whose pad stands dry over a sea cell, with
    /// rooms dug below sea level, showed the plane through its openings: a sheet of water hanging at
    /// ground level over a stairwell. Round the eye, each such place's footprint where the ground holds no
    /// sea (as the water is decided, <see cref="PlanetTerrainSource.WaterSurfaceAt"/>) is marked dry, and
    /// the sea draws nothing there.
    /// </summary>
    void KeepDry(Vector3 eye)
    {
        if (_seaMaterial == null || Planet == null) return;
        if (!float.IsNaN(_dryAt.X) && new Vector2(eye.X - _dryAt.X, eye.Z - _dryAt.Z).Length() < 48f) return;
        _dryAt = eye;
        _dry ??= Image.CreateEmpty(DryTexels, DryTexels, false, Image.Format.R8);
        _dry.Fill(Colors.Black);
        PlanetTerrainSource src = Planet.Source;
        float ox = MathF.Floor(eye.X) - DryTexels / 2, oz = MathF.Floor(eye.Z) - DryTexels / 2;
        int marked = 0;
        foreach (TerrainStamp st in src.Stamps)
        {
            if (st.Shape == StampShape.Road || st.CarveBottom >= src.WaterLevel) continue; // nothing of it dug below the sea
            Vector3 at = Planet.NearestCopy(new Vector3(st.MinX, 0f, st.MinZ), eye);
            int i0 = (int)(at.X - ox), k0 = (int)(at.Z - oz), w = st.MaxX - st.MinX + 1, d = st.MaxZ - st.MinZ + 1;
            if (i0 + w < 0 || k0 + d < 0 || i0 >= DryTexels || k0 >= DryTexels) continue;
            for (int k = Math.Max(0, k0); k < Math.Min(DryTexels, k0 + d); k++)
            for (int i = Math.Max(0, i0); i < Math.Min(DryTexels, i0 + w); i++)
            {
                double x = Mathx.WrapX(ox + i + 0.5, Planet.Width), z = Mathx.Wrap(oz + k + 0.5, Planet.Height);
                if (src.WaterSurfaceAt(x, z, out _)) continue;
                _dry.SetPixel(i, k, Colors.White);
                marked++;
            }
        }
        DryMarked = marked;
        if (_dryTexture == null) _dryTexture = ImageTexture.CreateFromImage(_dry);
        else _dryTexture.Update(_dry);
        _seaMaterial.SetShaderParameter("dry_map", _dryTexture);
        _seaMaterial.SetShaderParameter("dry_area", new Vector4(ox, oz, DryTexels, marked > 0 ? 1f : 0f));
    }

    /// <summary>Metres of ground round the eye the sea is kept off (for tests).</summary>
    public int DryMarked { get; private set; }

    /// <summary>Whether the sea is kept off a point (for tests): it lies in the dry mask's area and is marked.</summary>
    public bool KeptDry(float x, float z)
    {
        if (_dry == null || float.IsNaN(_dryAt.X)) return false;
        float ox = MathF.Floor(_dryAt.X) - DryTexels / 2, oz = MathF.Floor(_dryAt.Z) - DryTexels / 2;
        int i = (int)MathF.Floor(x - ox), k = (int)MathF.Floor(z - oz);
        return i >= 0 && k >= 0 && i < DryTexels && k < DryTexels && _dry.GetPixel(i, k).R > 0.5f;
    }

    // ---- Vessels --------------------------------------------------------------------------

    public void Track(Vessel vessel)
    {
        Vessels.Add(vessel);
        _vesselOf[vessel.Body] = vessel;
    }

    public void Untrack(Vessel vessel)
    {
        Vessels.Remove(vessel);
        _vesselOf.Remove(vessel.Body);
    }

    public Vessel VesselOf(Body body)
    {
        if (body == null) return null;
        if (_vesselOf.TryGetValue(body, out Vessel v)) return v;
        foreach (Vessel vessel in Vessels)
            if (vessel.Body == body)
            {
                _vesselOf[body] = vessel;
                return vessel;
            }
        return null;
    }

    // ---- Every frame and tick --------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_building != null)
        {
            if (!_building.IsCompleted) return;
            Arrive();
        }
        if (Planet == null) return;
        Vector3 focus = Focus();
        Streamer.SetFocus(focus);
        float latitude = (float)(Mathx.Wrap(focus.Z, Planet.Height) / Planet.Height * 2.0 - 1.0);
        // The sky is the system's, seen from where you stand: the sun goes down as you go east.
        double east = Mathx.WrapDelta(focus.X - Planet.Width * 0.5, Planet.Width) / Planet.Width * Math.Tau * Slice.Orbit.OrbitRules.Shared.Globe.MapDaysPerTurn;
        Sky.Stand(PlanetSurface.Skies.StarSystem.NoonLongitude + east, GalHack.Stars.SkyFrame.SeamColatitude(Mathx.Wrap(focus.Z, Planet.Height), Planet.Height), focus.Y);
        Sky.Advance(delta, latitude, Weather.Air(Sky.ToSun.Y), Weather.Wind2D);
        UpdateValleyFog(delta, focus);
        Vector3 eye = Camera?.GlobalPosition ?? focus;
        Sea.GlobalPosition = new Vector3(eye.X, 0f, eye.Z);
        KeepDry(eye);
        Sky.PlaceCloudDeck(eye);
        Light.DimFog(Sky.Environment);
        Underwater.Follow(eye, Camera);
        // The water's underside is lit by the day above it.
        float day = 0.15f + 0.85f * Sky.Daylight;
        _seaMaterial.SetShaderParameter("daylight", day);
        _riverMaterial.SetShaderParameter("daylight", day);
        _seaMaterial.SetShaderParameter("rain", Weather.RainHere);
        _riverMaterial.SetShaderParameter("rain", Weather.RainHere);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Planet != null) WrapFrame();
    }

    /// <summary>Morning fog pools on the low ground within a kilometre or so (as planet-surface does it).</summary>
    void UpdateValleyFog(double delta, Vector3 focus)
    {
        _valleyTimer -= delta;
        if (_valleyTimer <= 0)
        {
            _valleyTimer = 1.0;
            var heights = new List<float>(49);
            for (int r = 1; r <= 3; r++)
            for (int a = 0; a < 16; a++)
            {
                double angle = a * Math.Tau / 16;
                heights.Add(Math.Max(0f, Planet.Map.Sample(Planet.Map.Elevation,
                    Mathx.WrapX(focus.X + Math.Cos(angle) * r * 350, Planet.Width), Mathx.Wrap(focus.Z + Math.Sin(angle) * r * 350, Planet.Height))));
            }
            heights.Add(Math.Max(0f, Planet.GroundHeight(Mathx.WrapX(focus.X, Planet.Width), Mathx.Wrap(focus.Z, Planet.Height))));
            heights.Sort();
            _valleyFloor = heights[heights.Count / 6];
        }
        Sky.SetValleyFog(_valleyFloor, Sky.Air);
        // Thinner than planet-surface's own: the opening view from the pod should read clearly.
        Sky.Environment.FogHeightDensity *= Data.Rules.Planet.ValleyFogScale;
    }

    /// <summary>
    /// Keeps the focus on the planet itself: past an edge (with a few metres of slack, so a
    /// seam walked along doesn't flip back and forth) every body in the zone moves by a whole
    /// planet, and the streamer re-keys what it has loaded.
    /// </summary>
    void WrapFrame()
    {
        Vector3 p = Focus();
        int w = Planet.Terrain.Width, h = Planet.Terrain.Height;
        int dx = p.X < -8 ? 1 : p.X >= w + 8 ? -1 : 0;
        int dz = p.Z < -8 ? 1 : p.Z >= h + 8 ? -1 : 0;
        if (dx == 0 && dz == 0) return;
        ShiftFrame(dx, dz);
    }

    public void ShiftFrame(int dx, int dz)
    {
        var shift = new Vector3(dx * Planet.Terrain.Width, 0f, dz * Planet.Terrain.Height);
        N.Vector3 s = shift.ToNumerics();
        foreach (Body body in Physics.Bodies)
        {
            if (body.Frame != null) continue; // riders live in their ship's frame and move with it
            body.Position += s;
            Host.BindingOf(body)?.Teleport();
        }
        Streamer.ShiftFrame(dx * Planet.Terrain.ChunksX, dz * Planet.Terrain.ChunksZ);
        Streamer.SetFocus(Focus());
        Wildlife.Shift(shift);
        Blood.Shift(shift);
        Loot.Shift(shift);
        foreach (Device d in Devices) d.GlobalPosition += shift;
        Light.Shift(shift);
        FrameShifts++;
        FrameShifted?.Invoke(shift);
    }
}
