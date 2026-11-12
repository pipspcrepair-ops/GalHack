using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;
using Survivor = Slice.Player.Survivor;

namespace Slice.Orbit;

/// <summary>
/// The seam between the surface and orbit: the air thickens round you to the cap, the land hands
/// over behind it, and it thins away to black space with the world curving away below.
///
/// The crossing itself changes nothing you can see: the orbit is laid out in the surface's own
/// coordinates (<see cref="OrbitZone.Align"/>). What you see is set from your height alone, the
/// same both ways:
///
/// - From the ground up the sheet (<see cref="Globe"/>) is the far land past the surface's own, in
///   every direction, so the land and its sea never end at an edge.
/// - The shroud thickens from under the clouds, in the world's own sky colours, thickest at the
///   cap. Behind it the sheet comes over the land just under the cap, at the same size and in the
///   same place, and at the cap the land is gone. Nothing of it is left over the cap.
/// - Over the cap the shroud thins, the sheet curves away, and the sky fades into space's own
///   backdrop (the same galaxy the orbit's sky draws, from the same catalogue, in the same
///   directions), so when the orbit's sky takes over at the top of the band nothing changes. Wind and haze go with the air, and the star's
///   light takes over from the planet's sun.
///
/// Air thin enough to have little shroud has little to hide the swap, and needs little: its sheet
/// is the same land in the same place, under the same black sky.
///
/// Coming down it all runs backward: at the band's top the orbit is aligned to the place under
/// you (rigidly, the sky too, so nothing shows), the surface's streamer has been running ahead
/// of you for a while, and the crossing itself falls just under the cap, behind the shroud.
/// </summary>
public partial class Spaceflight
{
    ShaderMaterial _sky;
    float _fogDensity, _fogHeight, _fogScatter, _fogSky, _ambient;
    bool _aligned, _streaming;
    float _wreckFade = -1f;
    float _band;

    /// <summary>Where you are in the band over the cap: 0 at its foot, 1 at its top (the orbit's own sky).</summary>
    public float Band => Game.InOrbit ? _band : 0f;

    /// <summary>How far the air has thinned away where you are, 0 to 1.</summary>
    public float AirGone { get; private set; }

    /// <summary>The surface's streamer is running ahead of a descent.</summary>
    public bool StreamingAhead => _streaming;

    /// <summary>The zone is laid out over the surface here: world coordinates are the surface's.</summary>
    public bool Aligned => _aligned;

    static float Smooth(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    float BandTop => Ceiling + Rules.Crossing.Band;

    /// <summary>The air gone at a height: nothing under the fade's start, all of it by the band's top.</summary>
    float AirGoneAt(float y) => Smooth((y - Rules.Crossing.FadeFrom) / (BandTop - Rules.Crossing.FadeFrom));

    /// <summary>The map point under a world point in orbit, and its height over the globe's ground, as a point in the surface's own frame near where it was left.</summary>
    Vector3 SurfacePoint(Vector3 world, out double x, out double z)
    {
        Celestial c = Here;
        N.Vector3 q = c.ToGlobe(world.ToNumerics());
        (x, z) = c.Map.MapPoint(q);
        Vector3 near = _leftAt.TryGetValue(c, out Vector3 left) ? left : Vector3.Zero;
        return new Vector3((float)PlanetSurface.Terrain.TerrainStreamer.NearestCopy(x, 0, near.X, c.Map.Width), c.SurfaceHeight(q.Length()),
            (float)PlanetSurface.Terrain.TerrainStreamer.NearestCopy(z, 0, near.Z, c.Map.Height));
    }

    /// <summary>
    /// Your height over the globe's ground, which is sea level, of the body you are near. Right over
    /// the crossing point it is the world's own Y, but the globe curves away: fly sideways off it and
    /// you are climbing.
    /// </summary>
    float Height(Vector3 world) => Here.Height(world);

    /// <summary>The most a frame has spent on the seam and the skies, and on the skies alone, ms (tests read these, and reset them).</summary>
    public double WorstSeamMs, WorstSkyMs;

    /// <summary>Screenshots of other bodies: the sky as seen from here, in the globe's frame, m.</summary>
    public PlanetSurface.Skies.D3? ViewFrom;

    public override void _Process(double delta)
    {
        if (Game.Phase != Slice.Game.Phase.Playing || Game.Survivor == null) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        _laps.Clear();
        _lapAt = started;
        TimeSpan paused = GC.GetTotalPauseDuration();
        _frameDelta = (float)delta;
        ProcessSeam(delta);
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (ms > WorstSeamMs)
        {
            WorstSeamMs = ms;
            double gc = (GC.GetTotalPauseDuration() - paused).TotalMilliseconds;
            WorstSeamParts = _laps.ToString().TrimEnd(',', ' ') + (gc > 0.5 ? $" (a {gc:F1} ms GC pause among them)" : "");
        }
    }

    /// <summary>Where the worst frame's seam time went (tests print it).</summary>
    public string WorstSeamParts = "";
    readonly System.Text.StringBuilder _laps = new();
    long _lapAt;

    /// <summary>Notes the time since the last lap against a name, for <see cref="WorstSeamParts"/>.</summary>
    void Lap(string name)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(_lapAt, now).TotalMilliseconds;
        _lapAt = now;
        if (ms >= 0.5) _laps.Append($"{name} {ms:F1}, ");
    }

    void ProcessSeam(double delta)
    {
        TurnGround();
        Lap("ground turning");
        if (Game.InOrbit)
        {
            // The surface waits, but the clock and the sky above it don't.
            Below.Sky.RunClock(delta);
            KeepOneClock();
            MoonComing();
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            TrackSky();
            Lap("skies");
            WorstSkyMs = Math.Max(WorstSkyMs, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            PlaceSeam();
        }
        else
        {
            KeepOneClock();
            ThinTheAir();
        }
        DrawOthers();
        Lap("other bodies");
    }

    /// <summary>Puts both skies' bodies where the system has them this moment, from your eye in orbit: the surface's own sky drew itself while it was running.</summary>
    void TrackSky()
    {
        OrbitZone o = Orbit;
        Celestial c = Here;
        SurfaceZone s = Below;
        Vector3 eye = Game.Camera.GlobalPosition;
        // The surface's sky from the body you are near (its own frame), space's from the start world's (the model's).
        N.Vector3 q = c.ToGlobe(eye.ToNumerics()), h = o.Home.ToGlobe(eye.ToNumerics());
        var inHome = ViewFrom ?? new PlanetSurface.Skies.D3(h.X, h.Y, h.Z);
        var inHere = c.IsHome ? inHome : new PlanetSurface.Skies.D3(q.X, q.Y, q.Z);
        s.Sky.Stand(c.Turn.ToBasis().Transposed(), inHere);
        s.Sky.Track();
        o.Space.Track(Game.Surface.Planet.StarSystem, s.Sky.Days, o.Home.Turn.ToBasis(), inHome, eye, Height(eye) < Rules.Crossing.TrueGlobeAt ? 38000f : 0f);
    }

    /// <summary>Lays the orbit out over the surface at the point under a world position.</summary>
    void AlignHere(Vector3 world)
    {
        Survivor s = Game.Survivor;
        N.Quaternion before = Here.Turn;
        Vector3 p = SurfacePoint(world, out double x, out double z);
        Orbit.Align(Here, x, z, p.X, p.Z);
        _aligned = true;
        if (s.Body.Frame == null) s.ViewFrame = N.Quaternion.Normalize(Here.Turn * N.Quaternion.Conjugate(before) * s.ViewFrame);
    }

    // ---- On the surface, climbing ---------------------------------------------------------------

    /// <summary>On the surface the sky and the zone set the air fresh every frame: it thins here, after them, with height.</summary>
    void ThinTheAir()
    {
        SurfaceZone s = Below;
        Godot.Environment env = s.Sky.Environment;
        _fogDensity = env.FogDensity;
        _fogHeight = env.FogHeightDensity;
        _fogScatter = env.FogSunScatter;
        _fogSky = env.FogSkyAffect;
        _ambient = env.AmbientLightEnergy;
        Vessel carrier = Carrier();
        Vector3 p = carrier != null ? carrier.Body.Position.ToGodot() : Game.Survivor.WorldFeet;
        AirGone = AirGoneAt(p.Y);
        if (Orbit != null && AirGone > 0f) SkyTurn();
        if (AirGone > 0f || _sky != null) SetSurfaceAir(AirGone, p.Y);
        DrawWorld(p.Y);
    }

    /// <summary>Tells the surface's sky how space's backdrop stands: it hangs in the system's own frame, as the star field does.</summary>
    void SkyTurn()
    {
        _sky ??= Below.Sky.Environment.Sky.SkyMaterial as ShaderMaterial;
        _sky.SetShaderParameter("space_turn", Below.Sky.SkyToStars);
        _sky.SetShaderParameter("space_sun", Below.Sky.StarInField);
    }

    /// <summary>The air gone from the surface's sky: its colour into space's, its haze, its share of the light on your hull.</summary>
    void SetSurfaceAir(float gone, float y)
    {
        SurfaceZone s = Below;
        Godot.Environment env = s.Sky.Environment;
        _sky ??= env.Sky.SkyMaterial as ShaderMaterial;
        _sky.SetShaderParameter("space_fade", gone);
        env.FogDensity = _fogDensity * (1f - gone);
        // Height fog thickens with height over the clouds' base: left to itself it would fog your own hull.
        env.FogHeightDensity = _fogHeight * (1f - Smooth((y - Rules.Crossing.ShroudFrom) / 150f));
        env.FogSunScatter = _fogScatter * (1f - gone);
        env.FogSkyAffect = _fogSky * (1f - gone);
        env.AmbientLightEnergy = _ambient * (1f - gone) + Rules.Space.Ambient[1] * gone;
        s.Sky.CloudDeckFade = (1f - gone) * (1f - OverLand(y));
    }

    // ---- The crossing ---------------------------------------------------------------------------

    /// <summary>Keeps the surface's look as it is at the moment of leaving, to bring it back.</summary>
    void HoldSurfaceBehind()
    {
        SurfaceZone s = Below;
        _sunOf[s] = (s.Sky.Sun.LightEnergy, s.Sky.NightLight.LightEnergy);
        _band = 0f;
    }

    /// <summary>The sun's and the night's light on a surface as it was left (or as its sky first set them).</summary>
    (float Sun, float Night) SunOf(SurfaceZone s) => _sunOf.TryGetValue(s, out var e) ? e : _sunOf[s] = (s.Sky.Sun.LightEnergy, s.Sky.NightLight.LightEnergy);

    readonly Dictionary<SurfaceZone, (float, float)> _sunOf = new();

    /// <summary>The surface is the zone you are in again.</summary>
    void ReleaseSurface()
    {
        SurfaceZone s = Below;
        Orbit.Space.Show(false, 0f);
        Orbit.Space.Light(0f);
        ShowSurface(true);
        ShowGround(true);
        (s.Sky.Sun.LightEnergy, s.Sky.NightLight.LightEnergy) = SunOf(s);
        StreamAhead(false, default);
    }

    void ShowSurface(bool shown)
    {
        SurfaceZone s = Below;
        if (shown == SurfaceShown) return;
        _surfaceShownOf[s] = shown;
        s.Visible = shown;
        s.Light.Active = shown;
        foreach (WorldEnvironment w in s.Sky.GetChildren().OfType<WorldEnvironment>()) w.Environment = shown ? s.Sky.Environment : null;
    }

    /// <summary>Whether the surface below is drawn, and its land, as each was left (the start world's are, to begin with).</summary>
    bool SurfaceShown => _surfaceShownOf.TryGetValue(Below, out bool shown) ? shown : true;
    bool GroundShown => _groundShownOf.TryGetValue(Below, out bool shown) ? shown : true;
    readonly Dictionary<SurfaceZone, bool> _surfaceShownOf = new(), _groundShownOf = new();
    readonly Dictionary<SurfaceZone, List<Node3D>> _hiddenKidsOf = new();

    /// <summary>The land, the sea and everything on them, drawn or not: the sky stays. Over the cap there is none of it.</summary>
    void ShowGround(bool shown)
    {
        if (shown == GroundShown) return;
        SurfaceZone s = Below;
        _groundShownOf[s] = shown;
        if (!_hiddenKidsOf.TryGetValue(s, out List<Node3D> _hiddenKids)) _hiddenKidsOf[s] = _hiddenKids = new List<Node3D>();
        // A turn left unfinished ends now, then this one is laid out a piece at a time (see TurnGround).
        while (_groundTurns.TryDequeue(out Action turn)) turn();
        if (shown)
        {
            foreach (Node n in s.GetChildren())
                if (n is Node3D d && n != s.Sky) _groundTurns.Enqueue(() => d.Visible = true);
            // Branches before their twigs: each is shown once what it hangs on is.
            for (int i = _hiddenKids.Count - 1; i >= 0; i--)
            {
                Node3D kid = _hiddenKids[i];
                _groundTurns.Enqueue(() => { if (IsInstanceValid(kid)) kid.Visible = true; });
            }
            _hiddenKids.Clear();
        }
        else
        {
            // Big branches go a twig at a time (two levels down), each before the branch it hangs on.
            void Hide(Node3D d, int depth)
            {
                if (depth < 2 && d.GetChildCount() > ManyKids)
                    foreach (Node k in d.GetChildren())
                        if (k is Node3D kid && kid.Visible)
                        {
                            Hide(kid, depth + 1);
                            _hiddenKids.Add(kid);
                        }
                _groundTurns.Enqueue(() => { if (IsInstanceValid(d)) d.Visible = false; });
            }
            foreach (Node n in s.GetChildren())
                if (n is Node3D d && n != s.Sky) Hide(d, 0);
        }
        Game.Camera.Far = shown ? _surfaceFar : Rules.Space.CameraFar;
    }

    readonly Vector4[] _umbra = new Vector4[4];

    const int ManyKids = 24;
    const double GroundTurnMs = 1.5;
    readonly Queue<Action> _groundTurns = new();

    /// <summary>
    /// Showing or hiding a node visits everything under it, and the land is thousands of nodes: all
    /// at once it cost 80 ms of the crossing's frame. It goes a little a frame, the sheet in front of
    /// it the while, so what's left of the land for those few frames can't be seen.
    /// </summary>
    void TurnGround()
    {
        if (_groundTurns.Count == 0) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (_groundTurns.TryDequeue(out Action turn))
        {
            turn();
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds > GroundTurnMs) break;
        }
    }

    /// <summary>The surface's streamer runs (it alone: the zone stays frozen) with its focus on the point under you.</summary>
    void StreamAhead(bool on, Vector3 focus)
    {
        SurfaceZone s = Below;
        if (on && s.Planet == null) return; // not arrived yet: nothing to stream
        if (on != _streaming)
        {
            _streaming = on;
            s.Streamer.ProcessMode = on ? ProcessModeEnum.Always : ProcessModeEnum.Inherit;
            if (on) s.Streamer.CollisionRadius = 0; // the view only: collision comes with the crossing
        }
        if (on) s.Streamer.SetFocus(focus);
    }

    // ---- The world below ------------------------------------------------------------------------

    /// <summary>The sheet over the land (0 to 1): complete at the cap, the land still there under it until then.</summary>
    float OverLand(float y) => Smooth((y - Rules.Crossing.SwapFrom) / (Ceiling - Rules.Crossing.SwapFrom));

    /// <summary>How thick the air round you stands, 0 to 1, whatever the world's air: it peaks at the cap, and thins away over it.</summary>
    float ShroudAt(float y)
    {
        var r = Rules.Crossing;
        return Smooth((y - r.ShroudFrom) / (Ceiling - r.ShroudFrom)) * (1f - Smooth((y - Ceiling - r.ShroudHolds) / r.ShroudThins));
    }

    /// <summary>The world's share of the shroud by how much air it has: thick air closes in, thin air hardly at all.</summary>
    float AirShare => 1f - MathF.Exp(-2.5f * MathF.Max(Below.Planet?.Sky.Density ?? 0f, 0f));

    /// <summary>The shroud's thickness where you are (tests read this).</summary>
    public float Shrouded { get; private set; }

    /// <summary>Lays the world below out for this drawn frame, from your height.</summary>
    void DrawWorld(float y)
    {
        if (Orbit?.Ready != true) return;
        Celestial o = Here;
        if (!o.Ready) return;
        var r = Rules.Crossing;
        SurfaceZone s = Below;
        Vector3 eye = Game.Camera.GlobalPosition;
        bool orbit = Game.InOrbit;
        if (s.Planet == null)
        {
            // Its surface hasn't arrived yet (it does while you are still far over it): the whole globe, where it is.
            DrawFar(o, eye, 0f, 0f);
            return;
        }
        float cap = Ceiling, R = o.GlobeRadius;
        // The weather it shows: often while its clouds are in sight, now and then while they're under the sky's own.
        o.Globe.Weather?.Update(_frameDelta, s.Weather?.Sim, s.Sky, s.Planet.Look.Dust, orbit || y > s.Sky.Air.CloudBase ? GlobeWeather.LeastPeriod : 15.0);
        double w = s.Planet.Width, h = s.Planet.Height;

        // Where the sheet stands: under you on the flat map, turning into under you on the globe up the band.
        float onGlobe = !orbit ? 0f : _aligned ? Smooth((y - cap) / r.Band) : 1f;
        double nx = Planets.Gen.Mathx.WrapX(eye.X, w), nz = Planets.Gen.Mathx.Wrap(eye.Z, h);
        Vector3 up = Vector3.Up, east = Vector3.Right, south = Vector3.Back;
        float height = eye.Y;
        if (onGlobe > 0f)
        {
            N.Vector3 q = o.ToGlobe(eye.ToNumerics());
            var (gx, gz) = o.Map.MapPoint(q);
            N.Quaternion frame = N.Quaternion.Normalize(o.Turn * o.Map.Frame(gx, gz));
            up = up.Lerp(MathX.Rotate(frame, N.Vector3.UnitY).ToGodot(), onGlobe).Normalized();
            east = east.Lerp(MathX.Rotate(frame, N.Vector3.UnitX).ToGodot(), onGlobe);
            east = (east - up * east.Dot(up)).Normalized();
            south = east.Cross(up);
            nx = Planets.Gen.Mathx.WrapX(nx + onGlobe * Planets.Gen.Mathx.WrapDelta(gx - nx, w), w);
            nz = Planets.Gen.Mathx.Wrap(nz + onGlobe * Planets.Gen.Mathx.WrapDelta(gz - nz, h), h);
            height = Mathf.Lerp(height, o.SurfaceHeight(q.Length()), onGlobe);
        }
        N.Quaternion map = o.Map.Frame(nx, nz);

        // What stands in front of what. It looks the same wherever it stands on its eye rays.
        float over = OverLand(y), near, far;
        if (y >= r.TrueGlobeAt) near = far = 0f;
        else if (orbit && y >= cap && _groundTurns.Count == 0) (near, far) = (16000f, 38000f);
        else if (over > 0f)
        {
            float clear = MathF.Max(60f, y - Highest) * 0.9f;
            (near, far) = (clear * 0.8f, clear);
        }
        else (near, far) = (Game.Camera.Far * 0.9f, Game.Camera.Far * 0.99f);

        float climb = Mathf.Clamp((y - cap) / (r.TrueGlobeAt - cap), 0f, 1f);
        float bend = Mathf.Lerp(1f / r.FlatCurve, 1f / R, climb);
        float cloudBend = Mathf.Lerp(0f, 1f / R, climb); // what the climb adds to the ground's curve: the clouds take that and not the flat land's own
        s.Sky.CloudBend = cloudBend;
        float ringRadius = Globe.RingSphere(1f / bend, R, y - cap);
        s.Sky.RingRadius = ringRadius;
        Godot.Environment env = s.Sky.Environment;
        float share = AirShare;
        // The air's colour as the land's own fog has it, in the world's own light: part haze, part the sky behind it.
        var sky = s.Planet.Sky;
        Color fog = env.FogLightColor;
        float haze = MathF.Max(sky.Haze.R + sky.Haze.G + sky.Haze.B, 0.01f);
        float day = Mathf.Clamp((fog.R + fog.G + fog.B) / haze, 0f, 1.2f);
        Color airFar = fog.Lerp(new Color(sky.Horizon.R * day, sky.Horizon.G * day, sky.Horizon.B * day), env.FogAerialPerspective);
        int umbrae = s.Sky.Umbrae(_umbra, out float umbraSun);
        o.Globe.Draw(new WorldView
        {
            Umbra = _umbra, UmbraCount = umbrae, UmbraSun = umbraSun,
            Up = up, East = east, South = south,
            MapUp = MathX.Rotate(map, N.Vector3.UnitY).ToGodot(),
            MapEast = MathX.Rotate(map, N.Vector3.UnitX).ToGodot(),
            MapSouth = MathX.Rotate(map, N.Vector3.UnitZ).ToGodot(),
            Nadir = new Vector2((float)nx, (float)nz),
            Height = MathF.Max(height, 2f), // under the sea's level (a bunker) it's still the far land past what you can see
            Bend = bend,
            FlatFrom = PlanetSurface.Terrain.TerrainStreamer.RoundFrom * (1f - Smooth((y - cap) / 300f)),
            Eye = eye,
            RingRadius = ringRadius,
            RingShown = orbit ? Smooth((y - cap) / 120f) : 0f, // the sky draws it too, out to the band's top: the sheet covers the sky in front of it
            Flat = 1f - Smooth((y - cap) / r.MapWrapsBy),
            Taper = Smooth((y - cap) / 60f),
            Local = Mathf.Clamp(MathF.Min((float)nz, (float)(h - nz)) * 0.8f, 300f, 3000f),
            Relief = Mathf.Lerp(1f, (float)Rules.Globe.ReliefFar, climb),
            CloudBend = cloudBend,
            CloudLift = Mathf.Lerp(s.Sky.Air.CloudBase + s.Sky.Air.CloudThickness * 0.45f, (float)Rules.Globe.CloudHeight * R, Smooth((y - cap) / 400f)),
            OverLand = over,
            Near = near, Far = far,
            ToSun = s.Sky.ToSun,
            FogDensity = SurfaceShown ? env.FogDensity : 0f,
            FogColor = airFar,
            Rim = Smooth((y - cap) / 600f) * share,
        });

        // Land and sea both end with the land's own mesh, and the sheet goes on from there. A flat sea
        // running on past it would stand over the sheet's curve: a ring of water with no land in it.
        s.Streamer.FarFieldBehind = o.Globe.Visible;
        s.SeaRim = o.Globe.Visible ? s.Streamer.HorizonRim : PlanetSurface.Terrain.TerrainStreamer.NoRim;

        // The shroud, in this world's own air as it is lit now.
        Color high = new(sky.Zenith.R * day, sky.Zenith.G * day, sky.Zenith.B * day);
        Color glow = new(sky.StarColor.R * day, sky.StarColor.G * day, sky.StarColor.B * day);
        Shrouded = ShroudAt(y) * r.ShroudThickness * share;
        o.Globe.Shroud(Shrouded, fog.Lerp(high, 0.3f), high, glow, s.Sky.ToSun);
    }

    // ---- The band ------------------------------------------------------------------------------

    /// <summary>Everything that changes over the band, set every drawn frame from your height alone.</summary>
    void PlaceSeam()
    {
        Vessel carrier = Carrier();
        Vector3 at = carrier != null ? carrier.DrawTransform.Origin : Game.Survivor.WorldFeet;
        OrbitZone o = Orbit;
        float y = Height(at);
        Game.Surface.Sky.Stars.Poll();
        o.Space.Galaxy(Game.Surface.Sky.Stars);
        HandOverFrames(at);
        if (!_aligned || y >= BandTop)
        {
            AirGone = 1f;
            if (_band < 1f) AllSpace();
            _band = 1f;
            _aligned = false; // over the top: the sphere's own terms again
            DrawWorld(y);
            return;
        }
        float f = Mathf.Clamp((y - Ceiling) / Rules.Crossing.Band, 0f, 1f);
        _band = f;
        AirGone = AirGoneAt(y);
        SurfaceZone s = Below;
        ShowSurface(true);
        Lap("surface shown");
        ShowGround(y < Ceiling);
        Lap("ground");
        o.Space.Show(false, 0f);
        SkyTurn();
        Lap("space sky");
        s.Sky.PlaceCloudDeck(at); // it sets the height fog, so it goes before the air is thinned
        Lap("cloud deck");
        SetSurfaceAir(AirGone, y);
        Lap("air");
        s.Sea.GlobalPosition = new Vector3(at.X, 0f, at.Z);
        // The star's light takes over from the planet's sun.
        float k = Smooth(f);
        var (sun, night) = SunOf(s);
        s.Sky.Sun.LightEnergy = sun * (1f - k);
        s.Sky.NightLight.LightEnergy = night * (1f - k);
        o.Space.Light(k);
        o.Space.ShowBodies(false);
        FadeWrecks(Smooth((f - 0.5f) / 0.5f));
        Lap("light and wrecks");
        DrawWorld(y);
        Lap("world below");
    }

    /// <summary>Over the band's top: the orbit's sky takes over (the surface's had already become it) and nothing of the surface is left to draw.</summary>
    void AllSpace()
    {
        OrbitZone o = Orbit;
        ShowGround(false);
        ShowSurface(false);
        o.Space.Show(true, 1f);
        o.Space.Light(1f);
        o.Space.ShowBodies(true);
        FadeWrecks(1f);
    }

    /// <summary>The wrecks far off fade in up the band: none of them hangs in the planet's sky.</summary>
    void FadeWrecks(float fade)
    {
        if (fade == _wreckFade || (Mathf.Abs(fade - _wreckFade) < 0.04f && fade is > 0f and < 1f)) return;
        _wreckFade = fade;
        foreach (Vessel w in Orbit.Wrecks)
        {
            w.Node.Visible = fade > 0.01f;
            foreach (Node n in w.View.GetChildren())
                if (n is GeometryInstance3D g) g.Transparency = 1f - fade;
        }
    }
}
