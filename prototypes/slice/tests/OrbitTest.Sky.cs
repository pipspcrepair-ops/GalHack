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
            double x = Planets.Gen.Mathx.WrapX(cam.X, width), z = Planets.Gen.Mathx.Wrap(cam.Z, _game.Surface.Planet.Height);
            toGlobe = map.Frame(x, z);
            eye = map.Direction(x, z) * (float)(width / Math.Tau + cam.Y);
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
