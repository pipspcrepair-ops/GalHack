using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Slice.Vehicles;
using Weather.Model;

namespace Slice.Tests;

/// <summary>
/// The hover bike's drawn model and its rider's figure keep facing the way it goes (the owner saw the model pivot
/// away from forward while turning, and once face right and fly right). Measured every tick on the drawn nodes
/// (bike.Node and the Figure), not just the physics heading.
/// </summary>
public partial class VehicleGroundTest
{
    /// <summary>Limits, degrees: the drawn model's forward off the bike's heading; the figure's off the model's; under straight throttle the model's off the direction of travel and off where the rider looks.</summary>
    const float ModelLimit = 1.5f, FigureLimit = 1.5f, TravelLimit = 6f, ViewLimit = 3f;

    /// <summary>Past this tilt from upright, degrees, a forward flattened onto the ground says nothing (on its side, its roof): not judged.</summary>
    const float JudgedTilt = 45f;

    sealed class Facing
    {
        public float Model, Figure, Travel, View;
        public string ModelAt = "", FigureAt = "", TravelAt = "", ViewAt = "";
        public int Ticks, Straight;
    }

    Facing _face = new();
    string _segment = "";

    /// <summary>Straight throttle and the view held still: the direction of travel and where the rider looks are judged too.</summary>
    bool _straight;

    /// <summary>Whether the direction of travel is judged too: not in the second of riding off after a knock, while what the knock or a hop on a slope left in its slide still runs out.</summary>
    bool _judgeTravel = true;
    int _sinceTouch = 1000;

    /// <summary>The least speed ahead, m/s, at which the direction of travel is judged.</summary>
    float _minTravel = 8f;
    /// <summary>The bike has come round to where its rider looks (a line-up or a mouse turn finished): from here, any gap is drift.</summary>
    bool Aligned(HoverBike bike) => MathF.Abs(Mathf.Wrap(bike.Yaw - _game.Survivor.Yaw, -MathF.PI, MathF.PI)) < 0.03f;

    static Vector3 Flat(Vector3 v) => new Vector3(v.X, 0f, v.Z).Normalized();
    static float Deg(Vector3 a, Vector3 b) => Mathf.RadToDeg(a.AngleTo(b));

    /// <summary>The bike being ridden through the cases, for the drawn-frame look at the figure.</summary>
    HoverBike _riding;

    /// <summary>The open ground the bike cases started on.</summary>
    Vector3 _openGround;

    /// <summary>
    /// Each drawn frame, after the game has posed the figure: the rider's figure against the bike's drawn (interpolated)
    /// transform it was posed from, as the two are seen together on screen.
    /// </summary>
    public override void _Process(double delta)
    {
        HoverBike bike = _riding;
        if (bike == null || _game?.Figure == null || _game.Survivor?.OnBike != bike || !GodotObject.IsInstanceValid(bike.Node)) return;
        Basis m = bike.Node.GetGlobalTransformInterpolated().Basis.Orthonormalized();
        if (Mathf.RadToDeg(m.Y.AngleTo(Vector3.Up)) > JudgedTilt) return;
        float fig = Deg(Flat(-_game.Figure.GlobalTransform.Basis.Z), Flat(-m.Z));
        if (fig > _face.Figure) { _face.Figure = fig; _face.FigureAt = _segment; }
    }

    /// <summary>One tick's look at the drawn bike.</summary>
    void Face(HoverBike bike)
    {
        _riding = bike;
        Basis m = bike.Node.GlobalTransform.Basis.Orthonormalized();
        Vector3 model = Flat(-m.Z);
        if (Mathf.RadToDeg(m.Y.AngleTo(Vector3.Up)) > JudgedTilt) return;
        // The drawn model's yaw, its lean to the slope and into a turn taken out (yaw, then pitch, then roll), against
        // the bike's heading. Only while the weather hasn't tipped it (a tumble's tilt about a slanting axis turns
        // the flattened forward by itself, which is no drift).
        if (bike.Tilt < Mathf.DegToRad(10f))
        {
            _face.Ticks++;
            float off = MathF.Abs(Mathf.RadToDeg(Mathf.Wrap(m.GetEuler(EulerOrder.Yxz).Y - bike.Yaw, -MathF.PI, MathF.PI)));
            if (off > _face.Model) { _face.Model = off; _face.ModelAt = _segment; }
        }
        // Scraping along a bank or a wall, it goes where that lets it, and the slide it was turned into takes a moment to grip out.
        _sinceTouch = bike.Touching || !bike.Grounded ? 0 : _sinceTouch + 1; // (and in the air the wind blows it where it will)
        if (!_straight || !bike.Grounded || _sinceTouch < 45 || _game.Survivor.OnBike != bike) return;
        Vector3 flat = new(bike.Velocity.X, 0f, bike.Velocity.Z);
        if (flat.Dot(model) < _minTravel) return; // slow, or knocked back off something: no direction of travel to speak of
        _face.Straight++;
        float travel = _judgeTravel ? Deg(Flat(flat), model) : 0f;
        if (travel > TravelLimit && _face.Travel <= TravelLimit)
            GD.Print($"  (off its travel by {travel:F1}° {_segment}: speed {bike.Speed:F1}, velocity {bike.Velocity}, heading {bike.Forward}, yaw {bike.Yaw:F3} view {_game.Survivor.Yaw:F3}, grounded {bike.Grounded}, throttle {bike.Throttle}, steer {bike.Steer})");
        if (travel > _face.Travel) { _face.Travel = travel; _face.TravelAt = _segment; }
        float yaw = _game.Survivor.Yaw;
        float view = Deg(new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw)), model);
        if (view > _face.View) { _face.View = view; _face.ViewAt = _segment; }
    }

    void ReportFacing(string mode, string what)
    {
        Facing f = _face;
        Check(f.Ticks > 0 && f.Model < ModelLimit,
            $"{mode}, hover bike, {what}: the drawn model's forward at most {f.Model:F2}° off its heading over {f.Ticks} ticks (limit {ModelLimit}°{(f.ModelAt != "" ? $"; worst {f.ModelAt}" : "")})");
        Check(f.Figure < FigureLimit,
            $"{mode}, hover bike, {what}: the rider's figure faces the way the bike does, at most {f.Figure:F2}° off (limit {FigureLimit}°{(f.FigureAt != "" ? $"; worst {f.FigureAt}" : "")})");
        Check(f.Straight > 30 && f.Travel < TravelLimit && f.View < ViewLimit,
            $"{mode}, hover bike, {what}: under straight throttle ({f.Straight} ticks) the model faces the way it goes, at most {f.Travel:F1}° off its travel (limit {TravelLimit}°{(f.TravelAt != "" ? $", worst {f.TravelAt}" : "")}), and at most {f.View:F1}° off where its rider looks (limit {ViewLimit}°{(f.ViewAt != "" ? $", worst {f.ViewAt}" : "")})");
    }

    /// <summary>Lined up on a clear way, the bike come round to it.</summary>
    IEnumerable<int> LineUp(HoverBike bike)
    {
        _game.Survivor.Yaw = YawOf(ClearWay(bike.Position));
        for (int i = 0; i < 300 && MathF.Abs(Mathf.Wrap(bike.Yaw - _game.Survivor.Yaw, -MathF.PI, MathF.PI)) > 0.03f; i++)
            foreach (int x in Observe(bike, new Watch(), 1)) yield return x;
    }

    IEnumerable<int> Stop(HoverBike bike)
    {
        _straight = false;
        _game.Controls.Hold(Key.W, false);
        _game.Controls.Hold(Key.Shift, false);
        _game.Controls.Hold(Key.S);
        for (int i = 0; i < 120 && bike.Speed > 0.5f; i++)
            foreach (int x in Observe(bike, new Watch(), 1)) yield return x;
        _game.Controls.Hold(Key.S, false);
    }

    /// <summary>Steady turns under throttle, each way, by the keys and by the mouse, at a crawl, at full speed and on boost; then straightening out.</summary>
    IEnumerable<int> Turns(HoverBike bike, string mode)
    {
        var w = new Watch();
        foreach (var (pace, boost, target) in new[] { ("at 8 m/s", false, 8f), ("at full speed", false, HoverBike.MaxSpeed), ("on boost", true, HoverBike.BoostSpeed) })
            foreach (string how in new[] { "A", "D", "the mouse left", "the mouse right" })
            {
                bool crawl = target < HoverBike.MaxSpeed;
                _segment = $"{pace}, turning with {how}";
                foreach (int x in LineUp(bike)) yield return x;
                _game.Controls.Hold(Key.Shift, boost);
                bool aligned = false;
                for (int i = 0; i < 150 && bike.Speed < target * 0.9f; i++)
                {
                    _straight = aligned |= Aligned(bike);
                    _game.Controls.Hold(Key.W, !crawl || bike.Speed < target);
                    foreach (int x in Observe(bike, w, 1)) yield return x;
                }
                _straight = false;
                for (int i = 0; i < 120; i++)
                {
                    _game.Controls.Hold(Key.W, !crawl || bike.Speed < target);
                    _game.Controls.Hold(Key.A, how == "A");
                    _game.Controls.Hold(Key.D, how == "D");
                    if (how.StartsWith("the mouse")) _game.Survivor.Yaw += (how.EndsWith("left") ? 1f : -1f) * 1.4f / 60f;
                    foreach (int x in Observe(bike, w, 1)) yield return x;
                }
                _game.Controls.Hold(Key.A, false);
                _game.Controls.Hold(Key.D, false);
                _segment = $"{pace}, straightening out after turning with {how}";
                bool caught = false;
                for (int i = 0; i < 90; i++)
                {
                    _game.Controls.Hold(Key.W, !crawl || bike.Speed < target);
                    _straight = i > 45 && (caught |= Aligned(bike));
                    foreach (int x in Observe(bike, w, 1)) yield return x;
                }
                foreach (int x in Stop(bike)) yield return x;
            }
        Check(w.Sunk < BikeTolerance, $"{mode}, hover bike, through the turns: deepest {w.Sunk * 100f:F1} cm into anything solid (tolerance {BikeTolerance * 100f:F0} cm)");
    }

    /// <summary>Five seconds of straight throttle with a crosswind gusting from a steady push to twice it (a gale, short of a tornado's grip).</summary>
    IEnumerable<int> Crosswind(HoverBike bike, string mode)
    {
        _segment = "in a gusting crosswind";
        _minTravel = 15f; // at speed: crabbing into a crosswind at a crawl is no fault
        foreach (int x in LineUp(bike)) yield return x;
        var rng = new Random();
        float gust = 0f;
        _game.Controls.Hold(Key.W);
        for (int i = 0; i < 300; i++)
        {
            if (i % 20 == 0) gust = (float)rng.NextDouble() * 2f;
            _straight = i > 60 && Aligned(bike);
            bike.Push(bike.Right * (2f + gust) * bike.Mass); // a gale (what a parked bike just holds against on its skids) gusting to twice it
            foreach (int x in Observe(bike, new Watch(), 1)) yield return x;
        }
        _minTravel = 8f;
        foreach (int x in Stop(bike)) yield return x;
    }

    /// <summary>A tornado over the ridden bike: the rider is torn off, the bike tumbles and comes down; it rights itself, the rider gets back on and rides off.</summary>
    IEnumerable<int> Tornado(HoverBike bike, string mode)
    {
        _segment = "in a tornado's grip";
        // Out on the open ground it started on (a tornado touches down on the land, not a roof).
        bike.Position = _openGround + Vector3.Up * HoverBike.Hover;
        bike.Settle();
        foreach (int x in Observe(bike, new Watch(), 60)) yield return x;
        WeatherSim sim = _game.Zone.Weather.Sim;
        bool calm = _game.Zone.Weather.Bridge.Calm;
        _game.Zone.Weather.Bridge.Calm = false;
        TornadoShapeRules shape = sim.Data.Tornado.Shapes.First(x => x.Shape == TornadoShape.Cone);
        Vector3 at = bike.Position + bike.Right * 2f;
        Tornado t = sim.SpawnTornado(at, at + Vector3.Right * 500f, sim.Data.Tornado.Shaped(shape, 0.4f, 240f));
        t.Homing = false;
        t.Kept = true;
        t.SkipFormation();
        var w = new Watch();
        float high = bike.Position.Y;
        bool tornOff = false;
        for (int i = 0; i < 60 * 12; i++)
        {
            foreach (int x in Observe(bike, w, 1)) yield return x;
            high = MathF.Max(high, bike.Position.Y);
            tornOff |= _game.Survivor.OnBike != bike;
            if (i > 60 * 4 && (w.Toppled || w.Tilt > 60f)) break;
        }
        sim.ClearTornadoes();
        _game.Zone.Weather.Bridge.Calm = calm;
        for (int i = 0; i < 600 && !Steady(bike); i++) foreach (int x in Observe(bike, w, 1)) yield return x;
        bool rights = Steady(bike);
        GD.Print($"  ({mode}: the tornado lifted the bike {high - at.Y:F1} m, turned it {w.Tilt:F0}° from upright, {(tornOff ? "tore the rider off" : "left the rider on")}; {(rights ? "upright again" : "still not upright")})");
        if (_game.Survivor.OnBike != bike)
        {
            // Thrown on their own, the rider walks back to it and gets on (here: set down beside it and on).
            for (int i = 0; i < 600 && !_game.Survivor.Body.OnFloor; i++) yield return 0;
            _game.Suit.Vitals.Health = _game.Suit.Sim.Data.Vitals.Health.Max; // the throw's hurt is not what this checks
            Vector3 beside = bike.Position - bike.Right * 1.5f;
            _game.Survivor.PlaceOutside((_game.Zone.GroundAt(beside.X, beside.Z) + Vector3.Up * 0.2f).ToNumerics());
            foreach (int x in _drive.Wait(20)) yield return x;
            Check(_game.Survivor.Mount(bike), $"{mode}: back on the hover bike after the tornado");
        }
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(rights, $"{mode}, hover bike: after a tornado turned it {w.Tilt:F0}° from upright it is back on its plates, upright, by itself");
        foreach (int x in RidesOff(bike, mode, "after a tornado", w)) yield return x;
    }
}

public partial class VehicleGroundTest
{
    /// <summary>Ridden flat out into something (a steep bank, a wall), pressing into it a second, then ridden off by a clear way.</summary>
    IEnumerable<int> RamInto(HoverBike bike, string mode, string what, Func<Vector3, Vector3, bool> fits)
    {
        Vector3 at = bike.Position;
        Vector3? found = null;
        for (int k = 0; k < 32 && found == null; k++)
        {
            Vector3 way = new Vector3(0f, 0f, -1f).Rotated(Vector3.Up, k * Mathf.Tau / 32f);
            if (fits(at, way)) found = way;
        }
        if (found is not Vector3 dir) { GD.Print($"  ({mode}: no {what} within reach here: not tried)"); yield break; }
        _segment = what;
        _game.Survivor.Yaw = YawOf(dir);
        for (int i = 0; i < 300 && !Aligned(bike); i++) foreach (int x in Observe(bike, new Watch(), 1)) yield return x;
        var w = new Watch();
        _game.Controls.Hold(Key.W);
        _game.Controls.Hold(Key.Shift);
        float fastest = 0f;
        int pressed = 0;
        for (int t = 0; t < 480 && pressed < 60; t++)
        {
            foreach (int x in Observe(bike, w, 1)) yield return x;
            fastest = MathF.Max(fastest, bike.Speed);
            if (pressed > 0 || fastest > 8f && bike.Speed < fastest * 0.5f) pressed++;
        }
        _game.Controls.Hold(Key.Shift, false);
        _game.Controls.Hold(Key.W, false);
        GD.Print($"  ({mode}: the hover bike {what} at up to {fastest:F0} m/s{(pressed > 0 ? ", then pressed into it a second" : ", never stopped by it")})");
        foreach (int x in RidesOff(bike, mode, what, w)) yield return x;
    }

    /// <summary>A bank rising more than a metre in two within 25 m along a way.</summary>
    bool Bank(Vector3 at, Vector3 way)
    {
        float last = _game.Bikes.SolidAt(at.X, at.Z, at.Y + 1.5f);
        for (float d = 2f; d <= 25f; d += 2f)
        {
            Vector3 p = at + way * d;
            if (_game.Bikes.Blocked(at + Vector3.Up * 0.5f, way, d)) return false;
            float h = _game.Bikes.SolidAt(p.X, p.Z, last + 6f);
            if (h - last > 1.2f) return true;
            if (MathF.Abs(h - last) > 0.8f) return false;
            last = h;
        }
        return false;
    }

    /// <summary>A wall (a place's, a rock's face, a trunk) square across a way within 25 m, the ground level up to it.</summary>
    bool Wall(Vector3 at, Vector3 way)
    {
        var hit = _game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 0.5f, at + Vector3.Up * 0.5f + way * 25f,
            GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull | GalHack.Physics.Layers.Props | PlanetSurface.World.Layers.Flora));
        if (hit.Count == 0 || ((Vector3)hit["normal"]).Y > 0.3f) return false;
        float d = ((Vector3)hit["position"]).DistanceTo(at);
        if (d < 8f) return false;
        float last = _game.Bikes.SolidAt(at.X, at.Z, at.Y + 1.5f);
        for (float s = 2f; s < d - 2f; s += 2f)
        {
            Vector3 p = at + way * s;
            float h = _game.Bikes.SolidAt(p.X, p.Z, last + 2f);
            if (MathF.Abs(h - last) > 0.8f) return false;
            last = h;
        }
        return true;
    }
}
