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
/// The seam between the surface and orbit: a scene switch at the cap (owner, 17 and 22 Nov: a
/// visible cut is fine; no shroud, no curving ground).
///
/// - On the surface the ground is flat to the horizon, and the air thins away with height: the sky
///   darkens into space's own backdrop (the same galaxy the orbit's sky draws, from the same
///   catalogue, in the same directions), the haze goes and the stars come out, all of it gone by the
///   cap. Every other body is drawn by the surface's sky.
/// - At the cap the scene switches: the surface is hidden and waits, and space is what you see, the
///   world below its true globe (<see cref="Globe"/>) at its true place, and every other body with
///   ground its own globe.
/// - Coming down, the orbit is laid out over the place under you a band over the cap (rigidly,
///   which changes nothing you see), the surface's streamer runs ahead of you, and the switch back
///   falls just under the cap.
///
/// The orbit is laid out in the surface's own coordinates at a crossing (<see cref="OrbitZone.Align"/>),
/// so position, heading and velocity carry straight across both ways.
/// </summary>
public partial class Spaceflight
{
    ShaderMaterial _sky;
    float _fogDensity, _fogHeight, _fogScatter, _fogSky, _ambient;
    bool _aligned, _streaming;

    /// <summary>How far the air has thinned away where you are, 0 to 1 (all of it in space).</summary>
    public float AirGone { get; private set; }

    /// <summary>The surface's streamer is running ahead of a descent.</summary>
    public bool StreamingAhead => _streaming;

    /// <summary>The zone is laid out over the surface here: world coordinates are the surface's.</summary>
    public bool Aligned => _aligned;

    /// <summary>Out in space past the descent's band, in the sphere's own terms (tests and screenshots wait for it).</summary>
    public bool PastBand => Game.InOrbit && !_aligned;

    static float Smooth(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    float BandTop => Ceiling + Rules.Crossing.Band;

    /// <summary>The air gone at a height: nothing under the fade's start, all of it by the cap.</summary>
    float AirGoneAt(float y) => Smooth((y - Rules.Crossing.FadeFrom) / (Ceiling - Rules.Crossing.FadeFrom));

    /// <summary>The map point under a world point in orbit, and its height over the globe's ground, as a point in the surface's own frame near where it was left.</summary>
    Vector3 SurfacePoint(Vector3 world, out double x, out double z)
    {
        Celestial c = Here;
        N.Vector3 q = c.ToGlobe(world.ToNumerics());
        (x, z) = c.Map.MapPoint(q);
        Vector3 near = _leftAt.TryGetValue(c, out Vector3 left) ? left : new Vector3((float)c.Map.Width / 2f, 0f, (float)c.Map.Height / 2f);
        return new Vector3((float)PlanetSurface.Terrain.TerrainStreamer.NearestCopy(x, 0, near.X, c.Map.Width), c.SurfaceHeight(q.Length()),
            (float)PlanetSurface.Terrain.TerrainStreamer.NearestCopy(z, 0, near.Z, c.Map.Height));
    }

    /// <summary>
    /// Your height over the globe's ground, which is sea level, of the body you are near. Right over
    /// the crossing point it is the world's own Y, but the globe curves away: fly sideways off it and
    /// you are climbing.
    /// </summary>
    float Height(Vector3 world) => Here.Height(world);

    /// <summary>The most a frame has spent on the seam and the skies, and on the skies alone, and all the skies' time over <see cref="SkyFrames"/>, ms (tests read these, and reset them).</summary>
    public double WorstSeamMs, WorstSkyMs, SkyMs;
    public int SkyFrames;

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
        if (Game.InOrbit)
        {
            // The surface waits, but the clock and the sky above it don't.
            Below.Sky.RunClock(delta);
            KeepOneClock();
            MoonComing();
            Lap("a moon arriving");
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            TrackSky();
            Lap("the orbit's sky");
            double sky = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            (WorstSkyMs, SkyMs, SkyFrames) = (Math.Max(WorstSkyMs, sky), SkyMs + sky, SkyFrames + 1);
            InSpace();
        }
        else
        {
            KeepOneClock();
            ThinTheAir();
        }
        DrawGlobes();
        Lap("globes");
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
        if (s.Arrived)
        {
            s.Sky.Stand(c.Turn.ToBasis().Transposed(), inHere);
            s.Sky.Track();
            Lap("the sky below");
        }
        o.Space.Track(Game.Surface.Planet.StarSystem, s.Sky.Days, o.Home.Turn.ToBasis(), inHome, eye);
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
        if (AirGone > 0f || _sky != null) SetSurfaceAir(AirGone);
    }

    /// <summary>Tells the surface's sky how space's backdrop stands: it hangs in the system's own frame, as the star field does.</summary>
    void SkyTurn()
    {
        _sky ??= Below.Sky.Environment.Sky.SkyMaterial as ShaderMaterial;
        _sky.SetShaderParameter("space_turn", Below.Sky.SkyToStars);
        _sky.SetShaderParameter("space_sun", Below.Sky.StarInField);
    }

    /// <summary>The air gone from the surface's sky: its colour into space's, its haze, its share of the light on your hull.</summary>
    void SetSurfaceAir(float gone)
    {
        SurfaceZone s = Below;
        Godot.Environment env = s.Sky.Environment;
        _sky ??= env.Sky.SkyMaterial as ShaderMaterial;
        _sky.SetShaderParameter("space_fade", gone);
        env.FogDensity = _fogDensity * (1f - gone);
        env.FogHeightDensity = _fogHeight * (1f - gone);
        env.FogSunScatter = _fogScatter * (1f - gone);
        env.FogSkyAffect = _fogSky * (1f - gone);
        env.AmbientLightEnergy = _ambient * (1f - gone) + Rules.Space.Ambient[1] * gone;
    }

    // ---- The switch -----------------------------------------------------------------------------

    /// <summary>Over the cap: space is what you see, and nothing of the surface is drawn.</summary>
    void AllSpace()
    {
        OrbitZone o = Orbit;
        ShowSurface(false);
        o.Space.Show(true, 1f);
        o.Space.Light(1f);
        o.Space.ShowBodies(true);
        AirGone = 1f;
    }

    /// <summary>Under the cap again: the surface is the zone you see, its sky the one drawing every other body.</summary>
    void ReleaseSurface()
    {
        Orbit.Space.Show(false, 0f);
        Orbit.Space.Light(0f);
        Orbit.Space.ShowBodies(false);
        ShowSurface(true);
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
        Game.Camera.Far = shown ? _surfaceFar : Rules.Space.CameraFar;
    }

    /// <summary>Whether the surface below is drawn, as it was left (the start world's is, to begin with).</summary>
    bool SurfaceShown => _surfaceShownOf.TryGetValue(Below, out bool shown) ? shown : true;
    readonly Dictionary<SurfaceZone, bool> _surfaceShownOf = new();

    readonly Vector4[] _umbra = new Vector4[4];

    /// <summary>The surface's streamer runs (it alone: the zone stays frozen) with its focus on the point under you.</summary>
    void StreamAhead(bool on, Vector3 focus)
    {
        SurfaceZone s = Below;
        if (on && !s.Arrived) return; // not arrived yet: nothing to stream
        if (on != _streaming)
        {
            _streaming = on;
            s.Streamer.ProcessMode = on ? ProcessModeEnum.Always : ProcessModeEnum.Inherit;
            if (on) s.Streamer.CollisionRadius = 0; // the view only: collision comes with the crossing
        }
        if (on) s.Streamer.SetFocus(focus);
    }

    // ---- In space -------------------------------------------------------------------------------

    /// <summary>Every drawn frame in space: the galaxy as it stands, and which body is the frame you fly in.</summary>
    void InSpace()
    {
        Vessel carrier = Carrier();
        Vector3 at = carrier != null ? carrier.DrawTransform.Origin : Game.Survivor.WorldFeet;
        Game.Surface.Sky.Stars.Poll();
        Orbit.Space.Galaxy(Game.Surface.Sky.Stars);
        if (Height(at) >= BandTop) _aligned = false; // well over the cap: the sphere's own terms again
        HandOverFrames(at);
    }

    /// <summary>
    /// The bodies with ground, each drawn by its own globe at its true place, in space only: on a
    /// surface its sky draws them. The body you are near always; the start world always; a moon
    /// fading in over space's far look of it as you come near.
    /// </summary>
    void DrawGlobes()
    {
        OrbitZone o = Orbit;
        if (o?.Ready != true) return;
        bool space = Game.InOrbit;
        Vector3 eye = Game.Camera.GlobalPosition;
        if (Here.Ready && Below.Arrived)
        {
            // Its weather: often while you are over its clouds, now and then under them, so it is there when you cross.
            SurfaceZone s = Below;
            Here.Globe.Weather?.Update(_frameDelta, s.Weather?.Sim, s.Sky, s.Planet.Look.Dust, space || eye.Y > s.Sky.Air.CloudBase ? GlobeWeather.LeastPeriod : 15.0);
        }
        foreach (Celestial c in o.Bodies)
        {
            if (c.Globe == null) continue;
            float show = 0f;
            if (space && c.Ready)
            {
                if (c.IsHome || c == Here) show = 1f;
                else
                {
                    float[] from = Rules.Space.MoonGlobeFrom;
                    show = Smooth((from[0] - (eye.DistanceTo(c.Centre) - c.GlobeRadius)) / MathF.Max(from[0] - from[1], 1f));
                }
            }
            if (!c.IsHome) o.Space.Fade(c.Body, space ? 1f - show : 1f);
            if (show <= 0f)
            {
                c.Globe.Visible = false;
                continue;
            }
            DrawGlobe(c, eye, show);
        }
    }

    /// <summary>A body drawn as its globe at its true place; its air as much as it shows (<paramref name="shown"/>: the far look's air the rest).</summary>
    void DrawGlobe(Celestial c, Vector3 eye, float shown)
    {
        float rim = c.World != null && c.World.Sky.Density >= SurfaceWeather.NoAir ? Globe.AirShare(c.World.Sky.Density) : 0f;
        Basis home = Orbit.Home.Turn.ToBasis();
        int umbrae = Orbit.Space.Umbrae(Game.Surface.Planet.StarSystem, c.Body, Below.Sky.Days, home, _umbra, out float umbraSun);
        PlanetSurface.Skies.AtmosphereState? sky = c.Surface is { Arrived: true } s ? s.Sky.Air : null;
        c.Globe.Draw(new WorldView
        {
            Eye = eye, Centre = c.Centre, Turn = c.Turn,
            ToSun = (home * Orbit.Space.ToSun).Normalized(),
            Umbra = _umbra, UmbraCount = umbrae, UmbraSun = umbraSun,
            CloudHeight = sky is { } air ? air.CloudBase + air.CloudThickness * 0.45f : Rules.Globe.CloudHeight,
            Rim = rim * shown,
        });
    }
}
