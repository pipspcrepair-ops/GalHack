using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Physics.Engine;
using GalHack.Physics;
using Godot;
using Slice.Game;
using Slice.Vehicles;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// Vehicles never sink into what they hit, and stay operable after any knock (the owner's bug: a vehicle that
/// partly phased into the ground could no longer be operated). On a fresh random world, in Survival and then in
/// Creative, for each vehicle kind: a crash into the ground at speed, a steep-angle hit, a drop off a height and a
/// placement that overlaps the ground, against the terrain and against a place's blocks. During and after each its
/// deepest overlap with anything solid stays under a stated tolerance, and with the rider kept in the seat it
/// takes input and moves away under its own control. The hover bike also hops, standing and at full boost, and
/// stays level in the air and lands rideable; thrown onto its roof it rights itself and drives on without a
/// remount. Run with:
///   .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/VehicleGroundTest.tscn
/// </summary>
public partial class VehicleGroundTest : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>The deepest a hover bike's hull may go into anything solid, m.</summary>
    const float BikeTolerance = 0.05f;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100; // checks after the game has stepped
        ProcessPriority = 1000; // and each drawn frame after it has posed the figure
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(20));
            Console.Error.WriteLine("VehicleGroundTest took over 20 minutes of wall-clock time: watchdog");
            System.Environment.Exit(2);
        }) { IsBackground = true }.Start();
        _run = Scenarios().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"  FAIL  {what}");
    }

    bool Check(bool ok, string what)
    {
        if (ok) { _passes++; GD.Print($"  ok    {what}"); }
        else Fail(what);
        return ok;
    }

    IEnumerable<int> Scenarios()
    {
        string[] args = OS.GetCmdlineUserArgs();
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(args, WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        // The weather stays live (a bike's own drag through the air is part of what tipped it); the creatures keep still.
        _game.Zone.Wildlife.Frozen = true;
        GD.Print($"-- world {_game.World} --");
        foreach (GameMode mode in new[] { GameMode.Survival, GameMode.Creative })
        {
            _game.Mode = mode;
            GD.Print($"== {mode} ==");
            foreach (int x in ToPlace()) yield return x;
            if (!args.Contains("--vessels-only")) foreach (int x in Bike(mode.ToString())) yield return x;
            if (!args.Contains("--bike-only")) foreach (int x in Vessels(mode.ToString())) yield return x;
        }
    }

    // ---- Where ----------------------------------------------------------------------------------

    /// <summary>A dry, flat, open spot near the survivor (a ring search outward), and a clear way to ride from it.</summary>
    Vector3 FlatSpot(out Vector3 way)
    {
        var zone = _game.Zone;
        Vector3 feet = _game.Survivor.WorldFeet;
        way = new Vector3(0f, 0f, -1f);
        for (int i = 0; i < 120; i++)
        {
            float a = i * 0.7f, r = 14f + i * 2.5f;
            var p = feet + new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
            float h = zone.GroundAt(p.X, p.Z).Y, spread = 0f;
            if (h < zone.Planet.Source.WaterLevel + 1f) continue;
            for (int k = 0; k < 8; k++)
                spread = MathF.Max(spread, MathF.Abs(zone.GroundAt(p.X + MathF.Cos(k * 0.785f) * 14f, p.Z + MathF.Sin(k * 0.785f) * 14f).Y - h));
            if (spread > 1.2f) continue;
            return zone.GroundAt(p.X, p.Z);
        }
        return zone.GroundAt(feet.X + 20f, feet.Z);
    }

    /// <summary>A heading with nothing in the way for a good run.</summary>
    Vector3 ClearWay(Vector3 at)
    {
        Vector3 chest = at + Vector3.Up * 1.4f;
        for (int k = 0; k < 16; k++)
        {
            Vector3 way = new Vector3(0f, 0f, -1f).Rotated(Vector3.Up, k * Mathf.Tau / 16f);
            // Clear for its whole width, low and high, as its own rays see it (a trunk beside its line stops it).
            Vector3 side = Vector3.Up.Cross(way).Normalized() * 0.7f;
            bool blocked = _game.Bikes.Blocked(chest, way, 60f);
            foreach (float s in new[] { -1f, 0f, 1f })
                blocked = blocked || _game.Bikes.Blocked(at + Vector3.Up * 0.3f + side * s, way, 60f) || _game.Bikes.Blocked(at + Vector3.Up * 0.8f + side * s, way, 60f);
            if (blocked) continue;
            // And ground a rider would take: no bank steeper than one in two along it, no water.
            bool ridable = true;
            float last = _game.Bikes.SolidAt(at.X, at.Z, at.Y + 1.5f);
            for (float d = 1.5f; d <= 45f && ridable; d += 1.5f)
            {
                Vector3 p = at + way * d;
                float h = _game.Bikes.SurfaceAt(p.X, p.Z, last + 2f, out bool wet);
                ridable = h - last < 0.7f && !wet; // a step down (a roof's edge) is ridden off
                last = h;
            }
            if (ridable) return way;
        }
        return new Vector3(0f, 0f, -1f);
    }

    static float YawOf(Vector3 way) => MathF.Atan2(-way.X, -way.Z);

    // ---- The hover bike -------------------------------------------------------------------------
    /// <summary>How far any part of the bike's hull is into anything solid, m, measured apart from the bike's own rule.</summary>
    /// <summary>How far any part of the bike's hull is into anything solid, m, measured apart from the bike's own rule: the density field at each corner and along the hull's floor, and a ray down onto the collision shapes.</summary>
    float Sunk(HoverBike bike)
    {
        // The engine's own contact query with the hull's box against every collision shape: the ground's, a place's blocks, hulls.
        Basis b = bike.Attitude;
        Vector3 size = HoverBike.HullMax - HoverBike.HullMin;
        var q = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = size }, CollisionMask = GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull, Margin = 0f,
            Transform = new Transform3D(b, bike.Position + b * ((HoverBike.HullMin + HoverBike.HullMax) * 0.5f)),
        };
        return Separation(new[] { ((Shape3D)q.Shape, q.Transform) }, null);
    }

    /// <summary>A ridden bike's case: the rider stays seated throughout; afterwards it takes the throttle and rides off.</summary>
    sealed class Watch
    {
        public float Sunk, AirTilt, Tilt, AirPitch, AirRoll, Takeoff, AirGain;
        public bool WasGrounded = true;
        public bool Toppled, Flew;
    }

    IEnumerable<int> Observe(HoverBike bike, Watch w, int ticks, Func<bool> done = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            yield return 0;
            w.Sunk = MathF.Max(w.Sunk, Sunk(bike));
            float tilt = Mathf.RadToDeg(bike.Attitude.Y.AngleTo(Vector3.Up));
            w.Tilt = MathF.Max(w.Tilt, tilt);
            if (!bike.Grounded)
            {
                if (w.WasGrounded) w.Takeoff = tilt; // the lean to the slope it left from
                w.AirGain = MathF.Max(w.AirGain, tilt - w.Takeoff);
                Basis b = bike.Attitude;
                w.AirTilt = MathF.Max(w.AirTilt, tilt);
                w.AirPitch = MathF.Max(w.AirPitch, MathF.Abs(Mathf.RadToDeg(MathF.Asin(Math.Clamp(-b.Z.Dot(Vector3.Up), -1f, 1f)))));
                w.AirRoll = MathF.Max(w.AirRoll, MathF.Abs(Mathf.RadToDeg(MathF.Asin(Math.Clamp(b.X.Dot(Vector3.Up), -1f, 1f)))));
                w.Flew = true;
            }
            w.WasGrounded = bike.Grounded;
            w.Toppled |= bike.Toppled;
            Face(bike);
            if (done != null && i > 2 && done()) yield break;
        }
    }

    /// <summary>Back on its cushion, upright and still enough to judge.</summary>
    static bool Steady(HoverBike bike) => !bike.Toppled && bike.Grounded && bike.Tilt < 0.15f && MathF.Abs(bike.Velocity.Y) < 0.5f;

    /// <summary>With the rider still on: full throttle for a second, and it goes.</summary>
    IEnumerable<int> RidesOff(HoverBike bike, string mode, string what, Watch w)
    {
        for (int i = 0; i < 300 && !Steady(bike); i++) yield return 0;
        // The rider looks along a clear way, as a rider picks a line, and the bike comes round to it.
        _game.Survivor.Yaw = YawOf(ClearWay(bike.Position));
        for (int i = 0; i < 90 && MathF.Abs(Mathf.Wrap(bike.Yaw - _game.Survivor.Yaw, -MathF.PI, MathF.PI)) > 0.05f; i++) yield return 0;
        bool seated = _game.Survivor.OnBike == bike;
        Vector3 from = bike.Position;
        _game.Controls.Hold(Key.W);
        string was = _segment;
        _segment = $"riding off {what}";
        _judgeTravel = false;
        int took = -1;
        for (int i = 0; i < 60; i++)
        {
            _straight = i > 30 && Aligned(bike);
            foreach (int x in Observe(bike, w, 1)) yield return x;
            if (took < 0 && new Vector2(bike.Position.X - from.X, bike.Position.Z - from.Z).Length() > 1f) took = i;
        }
        _straight = false;
        _judgeTravel = true;
        _segment = was;
        float throttle = bike.Throttle;
        _game.Controls.Hold(Key.W, false);
        float ran = new Vector2(bike.Position.X - from.X, bike.Position.Z - from.Z).Length();
        string way = "";
        if (ran <= 3f && _game.Survivor.OnBike == bike)
        {
            // Nose against something on its line (a trunk, a wall, a bank): the rider backs off it, as anyone would.
            from = bike.Position;
            _game.Controls.Hold(Key.S);
            for (int i = 0; i < 60; i++)
            {
                foreach (int x in Observe(bike, w, 1)) yield return x;
                if (took < 0 && new Vector2(bike.Position.X - from.X, bike.Position.Z - from.Z).Length() > 1f) took = i;
            }
            _game.Controls.Hold(Key.S, false);
            ran = new Vector2(bike.Position.X - from.X, bike.Position.Z - from.Z).Length();
            way = " in reverse";
        }
        if (ran <= 3f)
            GD.Print($"  (stuck: throttle {throttle}, ridden {bike.Ridden}, grounded {bike.Grounded}, toppled {bike.Toppled}, water {bike.OnWater}, speed {bike.Speed:F1}, at {bike.Position}, "
                + $"blocked ahead {_game.Bikes.Blocked(bike.Position + Vector3.Up * 0.5f, bike.Forward, 2f)}, ground here {_game.Bikes.SolidAt(bike.Position.X, bike.Position.Z, bike.Position.Y + 1.5f):F2}, "
                + $"ahead {_game.Bikes.SolidAt(bike.Position.X + bike.Forward.X * 2f, bike.Position.Z + bike.Forward.Z * 2f, bike.Position.Y + 1.5f):F2}, dead {_game.Suit.Dead}, menu {_game.Menu.IsOpen}, push-out {_game.Bikes.PushOut(bike.Attitude, bike.Position)})");
        if (ran <= 3f)
            foreach (float h in new[] { 0.3f, 0.8f })
                foreach (float s in new[] { -0.6f, 0f, 0.6f })
                {
                    Vector3 o = bike.Position + Vector3.Up * h + bike.Forward.Cross(Vector3.Up).Normalized() * -s;
                    var hit = _game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(o, o + bike.Forward * 1.6f, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull | GalHack.Physics.Layers.Props | GalHack.Physics.Layers.Flora));
                    if (hit.Count > 0) GD.Print($"    ray {h} {s}: {(hit["collider"].AsGodotObject() as Node)?.Name ?? "a body with no node"} at {((Vector3)hit["position"]).DistanceTo(o):F2} m, normal {hit["normal"]}");
                }
        Check(seated && _game.Survivor.OnBike == bike && (ran > 3f || way != "" && ran > 2f) && w.Sunk < BikeTolerance,
            $"{mode}, hover bike, {what}: deepest {w.Sunk * 100f:F1} cm into anything solid (tolerance {BikeTolerance * 100f:F0} cm), tilted at most {w.Tilt:F0}°{(w.Toppled ? ", toppled and righted itself" : "")}; the rider still seated, it rides {ran:F1} m off{way} in a second under its own throttle, moving a metre {(took >= 0 ? $"{took / 60f:F2} s" : "never")} after it");
        // Brought to a stop again for the next case.
        _game.Controls.Hold(Key.S);
        for (int i = 0; i < 90 && bike.Speed > 0.5f; i++) yield return 0;
        _game.Controls.Hold(Key.S, false);
    }

    IEnumerable<int> Bike(string mode)
    {
        GD.Print($"-- {mode}: the hover bike --");
        Vector3 spot = _openGround = FlatSpot(out _);
        _game.Survivor.Dismount();
        _game.Survivor.PlaceOutside((spot + Vector3.Up * 0.2f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        Vector3 way = ClearWay(spot);
        HoverBike bike = _game.Bikes.Place(spot + way * 3f, YawOf(way));
        Check(_game.Survivor.Mount(bike), $"{mode}: on the hover bike");
        _game.Survivor.Yaw = bike.Yaw;
        _game.ThirdPerson = true; // the rider's figure is drawn and posed on the bike
        foreach (int x in _drive.Wait(20)) yield return x;

        // The owner's case first: ridden flat out into a steep bank and into a wall, the rider seated.
        foreach (int x in RamInto(bike, mode, "ridden into a steep bank", Bank)) yield return x;
        foreach (int x in RamInto(bike, mode, "ridden into a wall", Wall)) yield return x;

        // First, turning under throttle (where the owner saw the model pivot away from forward).
        _face = new Facing();
        foreach (int x in Turns(bike, mode)) yield return x;
        ReportFacing(mode, "through steady turns under throttle (A, D and the mouse, both ways, at low and full speed and on boost) and as each straightens out");
        _face = new Facing();

        // Back on the open ground it started on for the rest (the turns roam).
        bike.Position = _openGround + Vector3.Up * HoverBike.Hover;
        bike.Settle();
        foreach (int x in _drive.Wait(30)) yield return x;

        // Hops standing: straight up and down, level all the way, never a flip.
        _segment = "hopping standing";
        var w = new Watch();
        for (int h = 0; h < 6; h++)
        {
            _game.Controls.Hold(Key.Space);
            foreach (int x in Observe(bike, w, 3)) yield return x;
            _game.Controls.Hold(Key.Space, false);
            foreach (int x in Observe(bike, w, 150, () => Steady(bike))) yield return x;
        }
        Check(w.Flew && w.AirGain < 4f && !w.Toppled, $"{mode}, hover bike: six hops standing, each straight up and level in the air (at most {w.AirPitch:F1}° pitch, {w.AirRoll:F1}° roll; never more than {w.AirGain:F1}° past its lean to the ground it left), no flip");
        foreach (int x in RidesOff(bike, mode, "after the standing hops", w)) yield return x;

        // Hops at full boost: its own speed's drag once tipped it back past its gyro and flipped it.
        _segment = "hopping at full boost";
        w = new Watch();
        _game.Survivor.Yaw = bike.Yaw = YawOf(ClearWay(bike.Position));
        _game.Controls.Hold(Key.W);
        _game.Controls.Hold(Key.Shift);
        foreach (int x in Observe(bike, w, 90)) yield return x;
        float top = bike.Speed;
        for (int h = 0; h < 5; h++)
        {
            _game.Controls.Hold(Key.Space);
            foreach (int x in Observe(bike, w, 3)) yield return x;
            _game.Controls.Hold(Key.Space, false);
            foreach (int x in Observe(bike, w, 150, () => bike.Grounded && !bike.Toppled)) yield return x;
            foreach (int x in Observe(bike, w, 10)) yield return x;
        }
        _game.Controls.Hold(Key.W, false);
        _game.Controls.Hold(Key.Shift, false);
        Check(w.Flew && w.AirGain < 4f && !w.Toppled, $"{mode}, hover bike: five hops at {top:F0} m/s, each level in the air (at most {w.AirPitch:F1}° pitch, {w.AirRoll:F1}° roll; never more than {w.AirGain:F1}° past its lean to the ground it left), no flip");
        foreach (int x in RidesOff(bike, mode, "after the hops at speed", w)) yield return x;

        // Thrown onto its roof (a tornado's fling, a cliff): the rider stays on; it rights itself and drives.
        _segment = "thrown onto its roof and righting itself";
        w = new Watch();
        bike.Throw(Vector3.Up * 9f, bike.Forward * 9f);
        foreach (int x in Observe(bike, w, 600, () => w.Toppled && Steady(bike))) yield return x;
        Check(w.Tilt > 120f, $"{mode}, hover bike: thrown over ({w.Tilt:F0}° from upright at worst)");
        foreach (int x in RidesOff(bike, mode, "thrown onto its roof", w)) yield return x;

        // Forced upsets, the rider seated throughout: laid upside down, on its side, and on its side half buried in the ground.
        foreach (var (what, roll, sink) in new[] { ("laid upside down", MathF.PI, 0f), ("laid on its side", MathF.PI / 2f, 0f), ("laid on its side half buried", MathF.PI / 2f, 0.7f) })
        {
            _segment = what;
            foreach (int x in Observe(bike, new Watch(), 30, () => Steady(bike))) yield return x;
            w = new Watch();
            bike.Lay(new Basis(bike.Forward, roll));
            float ground = _game.Zone.GroundAt(bike.Position.X, bike.Position.Z).Y;
            bike.Position = new Vector3(bike.Position.X, ground + 0.3f - sink, bike.Position.Z);
            int t = 0;
            bool seatedAll = true;
            for (; t < 600 && !(Steady(bike) && Sunk(bike) < BikeTolerance); t++)
            {
                foreach (int x in Observe(bike, w, 1)) yield return x;
                seatedAll &= _game.Survivor.OnBike == bike;
            }
            float yaw = bike.Yaw;
            _game.Controls.Hold(Key.W);
            _game.Controls.Hold(Key.D);
            foreach (int x in Observe(bike, w, 40)) yield return x;
            _game.Controls.Hold(Key.D, false);
            _game.Controls.Hold(Key.W, false);
            float steered = Mathf.RadToDeg(MathF.Abs(Mathf.Wrap(bike.Yaw - yaw, -MathF.PI, MathF.PI)));
            Check(t < 600 && seatedAll && steered > 10f,
                $"{mode}, hover bike, {what}: it rights itself and comes clear in {t / 60f:F2} s, the rider seated throughout; then throttle and steering turn it {steered:F0}° as it goes");
            foreach (int x in RidesOff(bike, mode, what, w)) yield return x;
        }

        // A drop off a height.
        _segment = "dropped, crashed and set into the ground";
        w = new Watch();
        bike.Position += Vector3.Up * 25f;
        foreach (int x in Observe(bike, w, 300, () => Steady(bike))) yield return x;
        foreach (int x in RidesOff(bike, mode, "dropped 25 m", w)) yield return x;

        // Into the ground at speed, nose first at a steep angle.
        w = new Watch();
        bike.Position += Vector3.Up * 8f;
        bike.Throw(bike.Forward * 20f + Vector3.Down * 25f, bike.Right * -2.5f);
        foreach (int x in Observe(bike, w, 400, () => Steady(bike))) yield return x;
        foreach (int x in RidesOff(bike, mode, "a steep hit at speed (nose down, 32 m/s)", w)) yield return x;

        // A sideways crash at speed, rolled.
        w = new Watch();
        bike.Position += Vector3.Up * 5f;
        bike.Throw(bike.Right * 25f + Vector3.Down * 15f, bike.Forward * 4f);
        foreach (int x in Observe(bike, w, 400, () => Steady(bike))) yield return x;
        foreach (int x in RidesOff(bike, mode, "a crash rolling sideways at 29 m/s", w)) yield return x;

        // Placed half in the ground: pushed straight out, the first tick on.
        w = new Watch();
        bike.Position = new Vector3(bike.Position.X, _game.Zone.GroundAt(bike.Position.X, bike.Position.Z).Y - 0.6f, bike.Position.Z);
        foreach (int x in Observe(bike, w, 300, () => Steady(bike))) yield return x;
        foreach (int x in RidesOff(bike, mode, "set down 0.6 m into the ground", w)) yield return x;

        // Against a place's blocks: dropped onto its top, and set down partly into it.
        if (_placeTop is Vector3 roof)
        {
            w = new Watch();
            bike.Position = roof + Vector3.Up * 10f;
            foreach (int x in Observe(bike, w, 300, () => Steady(bike))) yield return x;
            foreach (int x in RidesOff(bike, mode, "dropped 10 m onto a place's blocks", w)) yield return x;
            w = new Watch();
            bike.Position = roof - Vector3.Up * 0.4f;
            foreach (int x in Observe(bike, w, 300, () => Steady(bike))) yield return x;
            foreach (int x in RidesOff(bike, mode, "set down 0.4 m into a place's blocks", w)) yield return x;
        }

        // A long straight ride in a crosswind with gusts (short of a tornado's grip).
        foreach (int x in Crosswind(bike, mode)) yield return x;
        // And a tornado's pick-up: the rider is torn off and thrown on their own; the bike tumbles, lands and rights itself.
        foreach (int x in Tornado(bike, mode)) yield return x;
        ReportFacing(mode, "through the hops, the flip and its recovery, the drops and crashes, a crosswind with gusts and a tornado");

        _game.Survivor.Dismount();
        _game.Bikes.Remove(bike);
    }
}

/// <summary>A place's blocks to come down on: the flat top of a standing place's blocks near the start.</summary>
public partial class VehicleGroundTest
{
    /// <summary>A flat top of a place's blocks (a roof or a floor open to the sky), clear for a few metres round, or null.</summary>
    Vector3? _placeTop;

    IEnumerable<int> ToPlace()
    {
        _placeTop = null;
        Vector3 feet = _game.Survivor.WorldFeet;
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        var sites = _game.Zone.Planet.Pois.Where(s => s.Instance != null && s.Instance.Blocks != null).OrderBy(s => _game.Places.DistanceTo(s, feet)).Take(8).ToList();
        foreach (var site in sites)
        {
            var box = site.Footprint;
            Vector3 mid = _game.Zone.Planet.NearestCopy(new Vector3((box.Min.X + box.Max.X) * 0.5f, site.Origin.Y, (box.Min.Z + box.Max.Z) * 0.5f), feet);
            Vector3 stand = mid + new Vector3((box.Max.X - box.Min.X) * 0.5f + 12f, 0f, 0f);
            _game.Survivor.PlaceOutside((_game.Zone.GroundAt(stand.X, stand.Z) + Vector3.Up * 0.3f).ToNumerics());
            for (int t = 0; t < 1800 && _game.Places.PlaceOf(site) is not { Ready: true }; t++) yield return 0;
            foreach (int x in _drive.UntilSettled(20)) yield return x;
            foreach (int x in _drive.Wait(20)) yield return x;
            if (_game.Places.PlaceOf(site) is not { Ready: true }) continue;
            Vector3 lo = _game.Zone.Planet.NearestCopy(new Vector3(box.Min.X, 0f, box.Min.Z), feet);
            float top = site.Origin.Y + 80f;
            Vector3? Hit(float x, float z)
            {
                var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(x, top, z), new Vector3(x, site.Origin.Y - 30f, z), GalHack.Physics.Layers.World));
                if (hit.Count == 0 || _game.Places.PlaceOf((GodotObject)hit["collider"]) == null || ((Vector3)hit["normal"]).Y < 0.95f) return null;
                return (Vector3)hit["position"];
            }
            for (int i = 2; i < box.Max.X - box.Min.X - 2 && _placeTop == null; i += 2)
                for (int k = 2; k < box.Max.Z - box.Min.Z - 2 && _placeTop == null; k += 2)
                {
                    float x = lo.X + i + 0.5f, z = lo.Z + k + 0.5f;
                    if (Hit(x, z) is not Vector3 p) continue;
                    bool flat = true;
                    for (int d = 0; d < 8 && flat; d++)
                        flat = Hit(x + MathF.Cos(d * 0.785f) * 2.5f, z + MathF.Sin(d * 0.785f) * 2.5f) is Vector3 q && MathF.Abs(q.Y - p.Y) < 0.05f;
                    // And a way off it for a vehicle set down there: some heading with no wall in it for 10 m.
                    bool off = false;
                    for (int d = 0; d < 8 && flat && !off; d++)
                    {
                        var dir = new Vector3(MathF.Cos(d * 0.785f), 0f, MathF.Sin(d * 0.785f));
                        off = !_game.Bikes.Blocked(p + Vector3.Up * 1.2f, dir, 10f) && !_game.Bikes.Blocked(p + Vector3.Up * 2f, dir, 10f);
                        // and nothing it would take for a bank (a pit's side, a step up) along it
                        float last = p.Y;
                        for (float s = 1.5f; s <= 10f && off; s += 1.5f)
                        {
                            Vector3 o = p + dir * s;
                            float h = _game.Bikes.SolidAt(o.X, o.Z, p.Y + 2f);
                            off = h - last < 0.6f;
                            last = h;
                        }
                    }
                    if (flat && off) _placeTop = p;
                }
            if (_placeTop is Vector3 found)
            {
                GD.Print($"  (a place's blocks to come down on: {site.Name}, its top at {found})");
                yield break;
            }
        }
        GD.Print("  (no flat top of a place's blocks near the start on this world)");
    }
}
