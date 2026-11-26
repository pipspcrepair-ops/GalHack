using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Skies;
using Slice.Orbit;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The sky is one system in every view: at one moment, every body drawn on the ground, just under
/// the cap, just over it and high in space stands where the model says it does from that eye, and
/// so in the same direction and at the same size from all of them, but for the true parallax of
/// the eye having moved. The clock is held still for the climb to make it one moment; afterwards it
/// runs on, in orbit too, and the sky back on the ground still agrees with the model.
/// </summary>
public partial class OrbitTest
{
    sealed record SkySample(string Where, D3 Eye, Dictionary<Orbiter, (Vector3 Direction, float Angular)> Bodies);

    readonly List<SkySample> _skySamples = new();
    int _skyStep = -1, _highTicks;
    double _skyWorst, _skySizeWorst, _clockAtTop;
    string _skyWorstAt = "";

    /// <summary>What the view drawing the sky has drawn just now, in the globe's frame, and from where.</summary>
    SkySample SampleSky(string where)
    {
        OrbitZone o = _game.Orbit;
        var sky = _game.Surface.Sky;
        double width = _game.Surface.Planet.Width;
        var map = new GlobeMap(width, _game.Surface.Planet.Height, width / Math.Tau);
        Vector3 cam = _game.Camera.GlobalPosition;
        bool space = _game.InOrbit && _game.Spaceflight.Band >= 1f;
        N.Quaternion toGlobe;
        N.Vector3 eye;
        if (_game.InOrbit)
        {
            toGlobe = N.Quaternion.Conjugate(o.Turn);
            eye = o.ToGlobe(cam.ToNumerics());
        }
        else
        {
            // The ground's sky is stood where the sleeper or vessel is (its focus), not at the chase camera behind it.
            Vector3 at = _game.Surface.Focus();
            double x = Planets.Gen.Mathx.WrapX(at.X, width), z = Planets.Gen.Mathx.Wrap(at.Z, _game.Surface.Planet.Height);
            toGlobe = map.Frame(x, z);
            eye = map.Direction(x, z) * (float)(width / Math.Tau + at.Y);
        }
        var d3 = new D3(eye.X, eye.Y, eye.Z);
        var bodies = new Dictionary<Orbiter, (Vector3, float)>();
        foreach (var (body, direction, angular) in space ? o.Space.Drawn.ToList() : sky.Drawn.ToList())
            bodies[body] = (N.Vector3.Transform(direction.ToNumerics(), toGlobe).ToGodot().Normalized(), angular);
        var sample = new SkySample(where, d3, bodies);
        StarSystem model = _game.Surface.Planet.StarSystem;
        foreach (var (body, (direction, angular)) in bodies)
        {
            Sighting s = model.See(body, sky.Days, d3);
            double off = direction.AngleTo(V(s.Direction)) * 180 / Math.PI;
            if (off > _skyWorst) (_skyWorst, _skyWorstAt) = (off, $"{body.Name} {where}");
            _skySizeWorst = Math.Max(_skySizeWorst, Math.Abs(angular - s.Angular) / s.Angular);
        }
        _skySamples.Add(sample);
        return sample;
    }

    static Vector3 V(D3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    void HoldSkyForTheClimb()
    {
        _game.Surface.Sky.Paused = true;
        _skyStep = 0;
    }

    /// <summary>Every tick of the climb: a sample at each of its four places.</summary>
    void WatchSky()
    {
        if (_skyStep < 0 || _skyStep > 3) return;
        float y = _game.Camera.GlobalPosition.Y;
        var flight = _game.Spaceflight;
        _highTicks = _game.InOrbit && flight.Band >= 1f ? _highTicks + 1 : 0;
        switch (_skyStep)
        {
            case 0: SampleSky("on the ground"); _skyStep++; break;
            case 1 when !_game.InOrbit && y > 1030f: SampleSky("just under the cap"); _skyStep++; break;
            case 2 when _game.InOrbit && flight.Band is > 0.01f and < 0.2f: SampleSky("just over the cap"); _skyStep++; break;
            case 3 when _highTicks > 10:
                SampleSky("high in space");
                _skyStep++;
                _game.Surface.Sky.Paused = false;
                _clockAtTop = _game.Surface.Sky.Days;
                foreach (var (body, off, inner) in _game.Orbit.Space.RingFit().ToList())
                    Check(off < 0.01f && inner > 1.4f, $"{body.Name}'s ring stands on it: {off:F2} m off its centre, its inner edge {inner:F2} radii out");
                TheSkyWasOneSystem();
                break;
        }
    }

    void TheSkyWasOneSystem()
    {
        double worst = 0;
        string at = "";
        int pairs = 0;
        foreach (SkySample a in _skySamples)
            foreach (SkySample b in _skySamples)
                foreach (var (body, (direction, _)) in a.Bodies)
                {
                    if (!b.Bodies.TryGetValue(body, out var other)) continue;
                    pairs++;
                    double parallax = Math.Atan2((a.Eye - b.Eye).Length, _game.Surface.Planet.StarSystem.See(body, _game.Surface.Sky.Days, a.Eye).Distance) * 180 / Math.PI;
                    double past = direction.AngleTo(other.Direction) * 180 / Math.PI - parallax;
                    if (past > worst) (worst, at) = (past, $"{body.Name}, {a.Where} against {b.Where}");
                }
        Check(pairs > 0 && worst < 0.3, $"every body stands in the same direction from the ground, under the cap, over it and high in space, past the true parallax: {worst:F3} degrees at worst{(at != "" ? $" ({at})" : "")}, {pairs} comparisons");
        Check(_skyWorst < 0.3, $"every view draws each body where the system has it: {_skyWorst:F3} degrees off at worst ({_skyWorstAt})");
        Check(_skySizeWorst < 0.02, $"and at the size its distance gives: {_skySizeWorst:P1} off at worst");
    }

    // ---- The sky's clouds hand over to the globe's ----------------------------------------------------

    int _overCapTicks, _skyCloudsOverCap, _cloudPops, _coverPixels = -1, _underCapPixels;
    float _lastShown = -1f;
    string _firstOverCap, _firstPop, _shot;
    int _shotTick;
    bool _shotOverCap;
    uint _cullMask;
    List<CanvasLayer> _hud;
    readonly List<string> _shots = new();
    readonly string _keep = Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--keep="))?["--keep=".Length..]; // shots, run in a window

    /// <summary>
    /// Every tick: over the cap the sky draws none of its own clouds (owner, 24 Nov: a flat deck spread past the globe's limb
    /// at 1,309 m): the globe's are all there are. Up and down they go with height and never step.
    /// </summary>
    void WatchCloudHandOver()
    {
        if (_game?.Spaceflight?.Below?.Sky?.Environment?.Sky?.SkyMaterial is not ShaderMaterial sky) return;
        Vector3 eye = _game.Camera.GlobalPosition;
        float height = _game.InOrbit && _game.Orbit?.Anchor is { } here ? here.Height(eye) : eye.Y;
        Variant set = sky.GetShaderParameter("clouds_shown");
        float shown = set.VariantType == Variant.Type.Nil ? 1f : (float)set, cap = _game.Spaceflight.Below.Physics.Zone.Ceiling;
        if (_lastShown >= 0f && MathF.Abs(shown - _lastShown) > 0.1f)
        {
            _cloudPops++;
            _firstPop ??= $"from {_lastShown:P0} to {shown:P0} in a tick at {height:F0} m";
        }
        _lastShown = shown;
        if (height >= cap + 10f && _game.Spaceflight.Band < 1f)
        {
            _overCapTicks++;
            if (shown > 0f)
            {
                _skyCloudsOverCap++;
                _firstOverCap ??= $"{shown:P0} of them at {height:F0} m";
            }
        }
        if (_keep != null) LimbShots(sky, height, cap, _skiff?.Body.Velocity.Y < 0f);
    }

    /// <summary>
    /// Run in a window (--keep=DIR): a picture at 1,000, 1,100 and 1,300 m each way, and the sky's cloud cover alone in the same
    /// view (its debug view, nothing else drawn): over the cap no cloud at all, anywhere outside the globe's disc.
    /// </summary>
    void LimbShots(ShaderMaterial sky, float height, float cap, bool down)
    {
        Camera3D cam = _game.Camera;
        if (_shot == null)
        {
            foreach (float at in new[] { 1000f, 1100f, 1300f })
            {
                string name = $"{(down ? "down" : "up")}-{at:F0}m";
                if (MathF.Abs(height - at) < 30f && !_shots.Contains(name)) _shot = name;
            }
            if (_shot == null) return;
            _shots.Add(_shot);
            _shotTick = 0;
            _shotOverCap = height > cap + 10f;
        }
        string file = System.IO.Path.Combine(_keep, $"orbitclouds-{_shot}");
        switch (_shotTick++)
        {
            case 0:
                System.IO.Directory.CreateDirectory(_keep);
                cam.GetViewport().GetTexture().GetImage().SavePng(file + ".png");
                (_cullMask, cam.CullMask) = (cam.CullMask, 0u);
                _hud = GetTree().Root.FindChildren("*", nameof(CanvasLayer), true, false).OfType<CanvasLayer>().Where(c => c.Visible).ToList();
                _hud.ForEach(c => c.Visible = false);
                sky.SetShaderParameter("debug_view", 1);
                break;
            case 4:
                Image cover = cam.GetViewport().GetTexture().GetImage();
                cover.SavePng(file + "-cover.png");
                cover.Convert(Image.Format.Rgb8);
                byte[] px = cover.GetData();
                // Over the frame's floor (no cloud is not quite black once the frame is graded): its darkest but for a stray few.
                int[] counts = new int[256];
                for (int i = 0; i < px.Length; i += 3) counts[px[i]]++;
                int least = 0;
                for (int seen = 0; (seen += counts[least]) < px.Length / 300; least++) { }
                // The pilot's figure in its seat still draws (posed every frame, apart from the camera's layers): its spot is left out.
                int w = cover.GetWidth(), clouded = 0;
                Vector2 pilot = _game.Figure != null && !cam.IsPositionBehind(_game.Figure.GlobalPosition) ? cam.UnprojectPosition(_game.Figure.GlobalPosition) : new Vector2(-1e4f, -1e4f);
                for (int i = 0; i < px.Length; i += 3)
                    if (px[i] > least + 24 && MathF.Max(MathF.Abs(i / 3 % w - pilot.X), MathF.Abs(i / 3 / w - pilot.Y)) > 60f) clouded++;
                if (_shotOverCap) _coverPixels = Math.Max(_coverPixels, clouded);
                else _underCapPixels = Math.Max(_underCapPixels, clouded);
                GD.Print($"  ({_shot}: {height:F0} m, {clouded} pixels of the sky's own cloud; {file}.png)");
                cam.CullMask = _cullMask;
                _hud.ForEach(c => c.Visible = true);
                sky.SetShaderParameter("debug_view", 0);
                _shot = null;
                break;
        }
    }

    void TheCloudsHandedOver()
    {
        Check(_overCapTicks > 0 && _skyCloudsOverCap == 0, $"over the cap the sky draws none of its own clouds, up and down: only the globe's ({_skyCloudsOverCap} of {_overCapTicks} ticks wrong{(_firstOverCap != null ? ": " + _firstOverCap : "")})");
        Check(_cloudPops == 0, $"the sky's clouds hand over to the globe's with height, never a step ({_cloudPops} steps{(_firstPop != null ? ": " + _firstPop : "")})");
        if (_keep != null)
        {
            Check(_shots.Count(n => !n.EndsWith("1000m")) >= 3 && _coverPixels == 0, $"in the pictures from orbit ({string.Join(", ", _shots)}) no pixel of the sky's own cloud, outside the globe's disc or anywhere: {_coverPixels} at most");
            GD.Print($"  (under the cap the same reading finds {_underCapPixels} pixels of the sky's cloud)");
        }
    }

    /// <summary>Back on the ground, after the zone has been re-aligned and crossed both ways: the sky still agrees, and the clock ran on up there.</summary>
    void TheSkyCameBack()
    {
        _skyWorst = 0;
        _skySizeWorst = 0;
        SampleSky("back on the ground");
        Check(_skyWorst < 0.5, $"after the crossings the ground's sky is still the system's: {_skyWorst:F3} degrees off at worst ({_skyWorstAt})");
        double ran = _game.Surface.Sky.Days - _clockAtTop;
        Check(ran > 0.001, $"the one clock kept running through orbit: {ran:F3} days");
    }
}
