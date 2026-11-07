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
/// - From low down the sheet (<see cref="Globe"/>) is the far land past the surface's own, in
///   every direction, so the land and its sea never end at an edge.
/// - The shroud thickens from under the clouds, in the world's own sky colours, thickest at the
///   cap. Behind it the sheet comes over the land just under the cap, at the same size and in the
///   same place, and at the cap the land is gone. Nothing of it is left over the cap.
/// - Over the cap the shroud thins, the sheet curves away, and the sky fades into space's own
///   backdrop (the very same stars and nebula the orbit's sky draws: planet-surface's sky shader
///   carries them, and they only come out as the sky goes dark), so when the orbit's sky takes
///   over at the top of the band nothing changes. Wind and haze go with the air, and the star's
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
    WorldEnvironment _surfaceWorld;
    ShaderMaterial _sky;
    float _fogDensity, _fogHeight, _fogScatter, _fogSky, _ambient, _sunEnergy, _nightEnergy;
    bool _aligned, _surfaceShown = true, _groundShown = true, _streaming, _skyLookSet;
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
        OrbitZone o = Orbit;
        N.Vector3 q = o.ToGlobe(world.ToNumerics());
        (x, z) = o.Map.MapPoint(q);
        return Game.Surface.Planet.NearestCopy(new Vector3((float)x, o.SurfaceHeight(q.Length()), (float)z), _leftSurfaceAt);
    }

    /// <summary>
    /// Your height over the globe's ground, which is sea level. Right over the crossing point it is
    /// the world's own Y, but the globe curves away: fly sideways off it and you are climbing.
    /// </summary>
    float Height(Vector3 world) => Orbit.SurfaceHeight(Orbit.ToGlobe(world.ToNumerics()).Length());

    public override void _Process(double delta)
    {
        if (Game.Phase != Slice.Game.Phase.Playing || Game.Survivor == null) return;
        if (Game.InOrbit) PlaceSeam();
        else ThinTheAir();
    }

    /// <summary>Lays the orbit out over the surface at the point under a world position.</summary>
    void AlignHere(Vector3 world)
    {
        Survivor s = Game.Survivor;
        N.Quaternion before = Orbit.Turn;
        Vector3 p = SurfacePoint(world, out double x, out double z);
        Orbit.Align(x, z, p.X, p.Z);
        _aligned = true;
        if (s.Body.Frame == null) s.ViewFrame = N.Quaternion.Normalize(Orbit.Turn * N.Quaternion.Conjugate(before) * s.ViewFrame);
    }

    // ---- On the surface, climbing ---------------------------------------------------------------

    /// <summary>On the surface the sky and the zone set the air fresh every frame: it thins here, after them, with height.</summary>
    void ThinTheAir()
    {
        SurfaceZone s = Game.Surface;
        Godot.Environment env = s.Sky.Environment;
        _fogDensity = env.FogDensity;
        _fogHeight = env.FogHeightDensity;
        _fogScatter = env.FogSunScatter;
        _fogSky = env.FogSkyAffect;
        _ambient = env.AmbientLightEnergy;
        Vessel carrier = Carrier();
        Vector3 p = carrier != null ? carrier.Body.Position.ToGodot() : Game.Survivor.WorldFeet;
        AirGone = AirGoneAt(p.Y);
        if (Orbit != null && AirGone > 0f)
        {
            // The backdrop as it will stand from here: it joins the orbit's sky at the crossing point.
            N.Quaternion frame = Orbit.Map.Frame(Planets.Gen.Mathx.WrapX(p.X, s.Planet.Width), Planets.Gen.Mathx.Wrap(p.Z, s.Planet.Height));
            SkyTurn(frame, Orbit.SunPlaced ? Orbit.Space.ToSun : MathX.Rotate(frame, s.Sky.ToSun.ToNumerics()).ToGodot());
        }
        if (AirGone > 0f || _sky != null) SetSurfaceAir(AirGone, p.Y);
        DrawWorld(p.Y);
    }

    /// <summary>Tells the surface's sky how space's backdrop stands: <paramref name="frame"/> takes the world's directions to the globe's.</summary>
    void SkyTurn(N.Quaternion frame, Vector3 sunInGlobe)
    {
        _sky ??= Game.Surface.Sky.Environment.Sky.SkyMaterial as ShaderMaterial;
        _sky.SetShaderParameter("space_turn", frame.ToBasis());
        _sky.SetShaderParameter("space_sun", sunInGlobe);
        if (_skyLookSet || !Orbit.Prepared) return;
        _skyLookSet = true;
        var look = Orbit.Paint.Look;
        var second = look.Atmosphere * 0.6f;
        _sky.SetShaderParameter("space_nebula", new Color(look.Nebula.R, look.Nebula.G, look.Nebula.B));
        _sky.SetShaderParameter("space_nebula_b", new Color(second.R, second.G, second.B));
    }

    /// <summary>The air gone from the surface's sky: its colour into space's, its haze, its share of the light on your hull.</summary>
    void SetSurfaceAir(float gone, float y)
    {
        SurfaceZone s = Game.Surface;
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
        SurfaceZone s = Game.Surface;
        _surfaceWorld ??= s.Sky.GetChildren().OfType<WorldEnvironment>().First();
        _sunEnergy = s.Sky.Sun.LightEnergy;
        _nightEnergy = s.Sky.NightLight.LightEnergy;
        _band = 0f;
    }

    /// <summary>The surface is the zone you are in again.</summary>
    void ReleaseSurface()
    {
        SurfaceZone s = Game.Surface;
        Orbit.Space.Show(false, 0f);
        Orbit.Space.Light(0f);
        ShowSurface(true);
        ShowGround(true);
        s.Sky.Sun.LightEnergy = _sunEnergy;
        s.Sky.NightLight.LightEnergy = _nightEnergy;
        StreamAhead(false, default);
    }

    void ShowSurface(bool shown)
    {
        if (shown == _surfaceShown) return;
        _surfaceShown = shown;
        Game.Surface.Visible = shown;
        _surfaceWorld.Environment = shown ? Game.Surface.Sky.Environment : null;
    }

    /// <summary>The land, the sea and everything on them, drawn or not: the sky stays. Over the cap there is none of it.</summary>
    void ShowGround(bool shown)
    {
        if (shown == _groundShown) return;
        _groundShown = shown;
        SurfaceZone s = Game.Surface;
        foreach (Node n in s.GetChildren())
            if (n is Node3D d && n != s.Sky) d.Visible = shown;
        Game.Camera.Far = shown ? _surfaceFar : Rules.Space.CameraFar;
    }

    /// <summary>The surface's streamer runs (it alone: the zone stays frozen) with its focus on the point under you.</summary>
    void StreamAhead(bool on, Vector3 focus)
    {
        SurfaceZone s = Game.Surface;
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
    float AirShare => 1f - MathF.Exp(-2.5f * MathF.Max(Game.Surface.Planet.Sky.Density, 0f));

    /// <summary>The shroud's thickness where you are (tests read this).</summary>
    public float Shrouded { get; private set; }

    /// <summary>Lays the world below out for this drawn frame, from your height.</summary>
    void DrawWorld(float y)
    {
        OrbitZone o = Orbit;
        if (o?.Ready != true) return;
        var r = Rules.Crossing;
        SurfaceZone s = Game.Surface;
        Vector3 eye = Game.Camera.GlobalPosition;
        bool orbit = Game.InOrbit;
        float cap = Ceiling, R = o.GlobeRadius;
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
        else if (orbit && y >= cap) (near, far) = (16000f, 38000f);
        else if (over > 0f)
        {
            float clear = MathF.Max(60f, y - Highest) * 0.9f;
            (near, far) = (clear * 0.8f, clear);
        }
        else (near, far) = (Game.Camera.Far * 0.9f, Game.Camera.Far * 0.99f);

        float climb = Mathf.Clamp((y - cap) / (r.TrueGlobeAt - cap), 0f, 1f);
        Godot.Environment env = s.Sky.Environment;
        float share = AirShare;
        o.Globe.Draw(new WorldView
        {
            Up = up, East = east, South = south,
            MapUp = MathX.Rotate(map, N.Vector3.UnitY).ToGodot(),
            MapEast = MathX.Rotate(map, N.Vector3.UnitX).ToGodot(),
            MapSouth = MathX.Rotate(map, N.Vector3.UnitZ).ToGodot(),
            Nadir = new Vector2((float)nx, (float)nz),
            Height = height,
            Bend = Mathf.Lerp(1f / r.FlatCurve, 1f / R, climb),
            Flat = 1f - Smooth((y - cap) / r.MapWrapsBy),
            Taper = Smooth((y - cap) / 60f),
            Local = Mathf.Clamp(MathF.Min((float)nz, (float)(h - nz)) * 0.8f, 300f, 3000f),
            Relief = Mathf.Lerp(1f, (float)Rules.Globe.ReliefFar, climb),
            CloudLift = Mathf.Lerp(s.Sky.Air.CloudBase + s.Sky.Air.CloudThickness * 0.45f, (float)Rules.Globe.CloudHeight * R, Smooth((y - cap) / 400f)),
            OverLand = over,
            Whole = Smooth((y - r.FarLandFrom) / 200f),
            Near = near, Far = far,
            ToSun = orbit ? N.Vector3.Transform(o.Space.ToSun.ToNumerics(), o.Turn).ToGodot() : s.Sky.ToSun,
            FogDensity = _surfaceShown ? env.FogDensity : 0f,
            FogColor = env.FogLightColor,
            Rim = Smooth((y - cap) / 600f) * share,
        });
        o.Globe.FadeRing(Smooth((y - r.TrueGlobeAt) / 500f));

        // The shroud, in this world's own air as it is lit now.
        var sky = s.Planet.Sky;
        Color fog = env.FogLightColor;
        float haze = MathF.Max(sky.Haze.R + sky.Haze.G + sky.Haze.B, 0.01f);
        float day = Mathf.Clamp((fog.R + fog.G + fog.B) / haze, 0f, 1.2f);
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
        SurfaceZone s = Game.Surface;
        ShowSurface(true);
        ShowGround(y < Ceiling);
        o.Space.Show(false, 0f);
        SkyTurn(N.Quaternion.Conjugate(o.Turn), o.Space.ToSun);
        s.Sky.PlaceCloudDeck(at); // it sets the height fog, so it goes before the air is thinned
        SetSurfaceAir(AirGone, y);
        s.Sea.GlobalPosition = new Vector3(at.X, 0f, at.Z);
        // The star's light takes over from the planet's sun.
        float k = Smooth(f);
        s.Sky.Sun.LightEnergy = _sunEnergy * (1f - k);
        s.Sky.NightLight.LightEnergy = _nightEnergy * (1f - k);
        o.Space.Light(k);
        o.Space.FadeBodies(Smooth((f - 0.6f) / 0.4f));
        FadeWrecks(Smooth((f - 0.5f) / 0.5f));
        DrawWorld(y);
    }

    /// <summary>Over the band's top: the orbit's sky takes over (the surface's had already become it) and nothing of the surface is left to draw.</summary>
    void AllSpace()
    {
        OrbitZone o = Orbit;
        ShowGround(false);
        ShowSurface(false);
        o.Space.Show(true, 1f);
        o.Space.Light(1f);
        o.Space.FadeBodies(1f);
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
