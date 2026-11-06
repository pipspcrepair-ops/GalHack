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
/// The seam between the surface and orbit: only the atmosphere fading away as you climb.
///
/// The crossing itself changes nothing you can see. The orbit is laid out in the surface's own
/// coordinates (<see cref="OrbitZone.Align"/>), and across it the surface stays on screen, its
/// terrain, sea, sky, sun and clouds exactly where they were, frozen.
///
/// The air thins with height alone, from under the clouds up through the crossing to the top of
/// the overlap band over it, the same both ways. The sky fades into space's own backdrop (the very
/// same stars and nebula the orbit's sky draws: planet-surface's sky shader carries them), so when
/// the orbit's sky takes over at the band's top nothing changes. Haze and fog thin to nothing, the
/// wind dies away. Over the band the globe, set tangent to sea level under you and showing the
/// same map, fades in over the terrain as the flat land fades out, and the star's light takes over
/// from the planet's sun, so the world below reads as the same world throughout.
///
/// Coming down it all runs backward: on the way into the band the orbit is aligned to the place
/// under you (rigidly, the sky too, so nothing shows), the surface's streamer has been running
/// ahead of you for a while, and by the foot of the band the view is the surface's, so the
/// handover changes nothing there either.
/// </summary>
public partial class Spaceflight
{
    WorldEnvironment _surfaceWorld;
    ShaderMaterial _sky;
    float _fogDensity, _fogHeight, _fogScatter, _ambient, _sunEnergy, _nightEnergy;
    bool _aligned, _surfaceShown = true, _streaming, _skyLookSet;
    float _wreckFade = -1f;
    float _band;

    /// <summary>Where you are in the overlap band: 0 at its foot (the surface fills the view), 1 at its top (the globe and space).</summary>
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
        _ambient = env.AmbientLightEnergy;
        Vessel carrier = Carrier();
        Vector3 p = carrier != null ? carrier.Body.Position.ToGodot() : Game.Survivor.WorldFeet;
        AirGone = AirGoneAt(p.Y);
        if (AirGone <= 0f && _sky == null) return;
        if (Orbit != null && AirGone > 0f)
        {
            // The backdrop as it will stand from here: it joins the orbit's sky at the crossing point.
            N.Quaternion frame = Orbit.Map.Frame(Planets.Gen.Mathx.WrapX(p.X, s.Planet.Width), Planets.Gen.Mathx.Wrap(p.Z, s.Planet.Height));
            SkyTurn(frame, Orbit.SunPlaced ? Orbit.Space.ToSun : MathX.Rotate(frame, s.Sky.ToSun.ToNumerics()).ToGodot());
        }
        SetSurfaceAir(AirGone);
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
    void SetSurfaceAir(float gone)
    {
        SurfaceZone s = Game.Surface;
        Godot.Environment env = s.Sky.Environment;
        _sky ??= env.Sky.SkyMaterial as ShaderMaterial;
        _sky.SetShaderParameter("space_fade", gone);
        env.FogDensity = _fogDensity * (1f - gone);
        env.FogHeightDensity = _fogHeight * (1f - gone);
        env.FogSunScatter = _fogScatter * (1f - gone);
        env.AmbientLightEnergy = _ambient * (1f - gone) + Rules.Space.Ambient[1] * gone;
        s.Sky.CloudDeckFade = 1f - gone;
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
        FinishFadingSurface(0f);
        s.Sea.Transparency = 0f;
        s.Sky.Sun.LightEnergy = _sunEnergy;
        s.Sky.NightLight.LightEnergy = _nightEnergy;
        Game.Camera.Far = _surfaceFar;
        StreamAhead(false, default);
    }

    void ShowSurface(bool shown)
    {
        if (shown == _surfaceShown) return;
        _surfaceShown = shown;
        Game.Surface.Visible = shown;
        _surfaceWorld.Environment = shown ? Game.Surface.Sky.Environment : null;
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

    // ---- The overlap band -----------------------------------------------------------------------

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
            return;
        }
        float f = Mathf.Clamp((y - Ceiling) / Rules.Crossing.Band, 0f, 1f);
        _band = f;
        AirGone = AirGoneAt(y);
        SurfaceZone s = Game.Surface;
        ShowSurface(true);
        o.Space.Show(false, 0f);
        SkyTurn(N.Quaternion.Conjugate(o.Turn), o.Space.ToSun);
        SetSurfaceAir(AirGone);
        s.Sky.PlaceCloudDeck(at);
        s.Sea.GlobalPosition = new Vector3(at.X, 0f, at.Z);
        // The star's light takes over from the planet's sun.
        float k = Smooth(f);
        s.Sky.Sun.LightEnergy = _sunEnergy * (1f - k);
        s.Sky.NightLight.LightEnergy = _nightEnergy * (1f - k);
        o.Space.Light(k);
        // The globe fades in over the land, which fades out behind it.
        o.Globe.Fade(Smooth(f / 0.55f), Smooth((f - 0.5f) / 0.5f));
        o.Globe.Pull(Game.Camera.GlobalPosition);
        float land = Smooth((f - 0.08f) / 0.85f);
        s.Sea.Transparency = land;
        FadeSurface(land);
        o.Space.FadeBodies(Smooth((f - 0.6f) / 0.4f));
        FadeWrecks(Smooth((f - 0.5f) / 0.5f));
    }

    /// <summary>Over the band's top: the orbit's sky takes over (the surface's had already become it) and nothing of the surface is left to draw.</summary>
    void AllSpace()
    {
        OrbitZone o = Orbit;
        ShowSurface(false);
        o.Space.Show(true, 1f);
        o.Space.Light(1f);
        o.Space.FadeBodies(1f);
        o.Globe.Fade(1f, 1f);
        o.Globe.Pull(null);
        FadeWrecks(1f);
    }

    // The land's fade: every mesh on the surface, a walk of its tree a slice a frame.
    readonly Stack<Node> _walk = new();
    float _landFade, _walkFade = -1f;
    const int WalkPerFrame = 900;

    void FadeSurface(float t)
    {
        _landFade = t;
        if (t == _walkFade && _walk.Count == 0) return;
        if (_walk.Count == 0)
        {
            _walkFade = t;
            _walk.Push(Game.Surface);
        }
        Walk(WalkPerFrame);
    }

    void Walk(int budget)
    {
        SurfaceZone s = Game.Surface;
        while (budget-- > 0 && _walk.Count > 0)
        {
            Node n = _walk.Pop();
            if (!IsInstanceValid(n)) continue; // gone since this pass began (a chunk streamed away)
            if (n is GeometryInstance3D g && n != s.Sea && n != s.Sky.CloudDeck) g.Transparency = _landFade;
            int children = n.GetChildCount();
            for (int i = 0; i < children; i++) _walk.Push(n.GetChild(i));
        }
    }

    /// <summary>Sets the land's fade everywhere now (the crossing down: the surface must be solid).</summary>
    void FinishFadingSurface(float t)
    {
        _landFade = t;
        if (_walkFade == t && _walk.Count == 0) return;
        _walk.Clear();
        _walkFade = t;
        _walk.Push(Game.Surface);
        Walk(int.MaxValue);
    }

    /// <summary>The wrecks far off fade in as the land goes: none of them hangs in the planet's sky.</summary>
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
