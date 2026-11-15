using System;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PlanetSurface.Poi;
using ShipHandling.Handling;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Places;

/// <summary>What a sky ship is doing.</summary>
public enum SkyPhase { Landed, Rising, Cruising, Circling, Approaching, Descending, Fleeing, Wrecked }

/// <summary>
/// One generated vessel going about its business in the sky, flown by its pilot through the stick (<see cref="SkyPilot"/>):
/// up off the ground, across to its next place at a height over whatever lies ahead, round the place if it's a scout's
/// look, down onto a clear spot beside it, a while there, then on. Hit, it runs; it never fires. Shot down, it is a wreck
/// like any other. Far from the sleeper the same flight goes on: only a landing where the ground has no collision yet is
/// set down on the ground's own height, as a hull out of the collision's reach is.
/// </summary>
public sealed class Skyship
{
    public readonly Vessel Vessel;
    public readonly CraftRole Role;
    public SkyPhase Phase { get; private set; } = SkyPhase.Landed;

    /// <summary>The place it is at or bound for, and the one before.</summary>
    public PoiSite At { get; private set; }
    public PoiSite Bound { get; private set; }

    /// <summary>Where it means to put down (frame metres, on the ground), or to circle round.</summary>
    public N.Vector3 Spot { get; private set; }

    /// <summary>Tests: places it has left, landings made, the least height over the ground in cruise, hits taken from collisions (not shots) in flight.</summary>
    public int Departures { get; private set; }
    public int Landings { get; private set; }
    public float LeastClearance { get; private set; } = float.PositiveInfinity;
    public int Bumps { get; private set; }
    public int Flights => Departures;
    /// <summary>Tests: how many times it has fled, and the places it has landed at.</summary>
    public int Fled { get; private set; }
    public readonly System.Collections.Generic.List<PoiSite> Visited = new();

    /// <summary>The share of the usual wait at a stop (tests shorten it).</summary>
    public static float WaitScale = 1f;

    readonly SkyTraffic _traffic;
    readonly Random _rng;
    float _wait, _altitude, _altitudeIn, _circled, _fleeFor;
    int _struck, _bumps, _leg, _retries;
    float _heading;

    public Skyship(SkyTraffic traffic, Vessel vessel, CraftRole role, PoiSite home, int seed)
    {
        _traffic = traffic;
        Vessel = vessel;
        Role = role;
        At = home;
        _rng = new Random(seed);
        _wait = Wait() * 0.5f;
        _heading = MathX.Heading(vessel.Body.Rotation);
        Spot = vessel.Body.Position;
    }

    float Cruise => MathF.Min((Vessel.Stats?.TopSpeed ?? 40f) * 0.7f, Role == CraftRole.Scout ? 46f : 36f);
    float Clearance => Role == CraftRole.Scout ? 65f : 80f;
    float Wait() => (Role == CraftRole.Scout ? 25f : 45f) * (1f + (float)_rng.NextDouble()) * WaitScale;

    /// <summary>Landed, it stays at least this much longer (tests and shots).</summary>
    public void Stay(float seconds) => _wait = MathF.Max(_wait, seconds);

    /// <summary>The frame moved by a whole planet: so does where it is bound.</summary>
    public void Shift(Vector3 by) => Spot += by.ToNumerics();

    public void Tick(float dt)
    {
        Vessel v = Vessel;
        if (Phase == SkyPhase.Wrecked) return;
        if (v.IsWreck || v.Flight == null || v.Body.Kind != BodyKind.Vessel || v.Structure.Core == null)
        {
            Phase = SkyPhase.Wrecked;
            v.Roles.Stood(SkyTraffic.PilotName);
            return;
        }
        if (v.Roles.Pilot != SkyTraffic.PilotName)
        {
            if (v.Flight.Input.Seated) v.Flight.Input = PilotInput.Autopilot; // its pilot's seat was taken from them: nobody flies it
            return;
        }
        // Shot at or caught in a blast: it runs. Knocks against anything else in flight are counted (tests).
        if (v.Hits.ShotsTaken > _struck)
        {
            _struck = v.Hits.ShotsTaken;
            if (Phase != SkyPhase.Fleeing) Flee();
        }
        int bumps = v.Hits.HitsTaken - v.Hits.ShotsTaken;
        if (bumps > _bumps && Phase is SkyPhase.Cruising or SkyPhase.Circling or SkyPhase.Approaching) Bumps += bumps - _bumps;
        _bumps = bumps;
        float above = v.AboveGround;
        if (Phase is SkyPhase.Cruising or SkyPhase.Circling) LeastClearance = MathF.Min(LeastClearance, above);
        switch (Phase)
        {
            case SkyPhase.Landed:
                v.Flight.Input = PilotInput.LetGo;
                if ((_wait -= dt) > 0f) return;
                Depart();
                return;
            case SkyPhase.Rising:
                Steer(N.Vector3.UnitY * 5f, _heading);
                if (above > 22f) Phase = SkyPhase.Cruising;
                return;
            case SkyPhase.Cruising:
                CruiseTo(dt, above);
                return;
            case SkyPhase.Circling:
                Circle(dt);
                return;
            case SkyPhase.Approaching:
                Approach(dt);
                return;
            case SkyPhase.Descending:
                Descend();
                return;
            case SkyPhase.Fleeing:
                Run(dt);
                return;
        }
    }

    // ---- Legs ---------------------------------------------------------------------------------------------

    /// <summary>The look for a spot to put down, worked a little each tick until it is done.</summary>
    SkyTraffic.SpotSearch _search;

    /// <summary>Off to the next place: a scout's look round it starts at once; a landing waits for its spot to be found.</summary>
    void Depart()
    {
        if (_search == null)
        {
            bool lands = Role == CraftRole.Trader || _leg % 3 == 2;
            PoiSite next = _traffic.NextStop(this, lands);
            if (next == null) { _wait = 10f; return; }
            if (!lands)
            {
                Begin(next, _traffic.CentreOf(next, Vessel.Body.Position), lands: false);
                return;
            }
            _search = _traffic.FindSpot(next, Vessel);
        }
        if (!_traffic.Advance(_search)) return;
        SkyTraffic.SpotSearch q = _search;
        _search = null;
        if (q.Spot == null) { _wait = 10f; return; }
        Begin(q.Site, q.Spot.Value, lands: true);
    }

    void Begin(PoiSite to, N.Vector3 spot, bool lands)
    {
        Bound = to;
        Spot = spot;
        _leg++;
        Departures++;
        if (Phase is SkyPhase.Landed) Phase = SkyPhase.Rising;
        else Phase = SkyPhase.Cruising;
        _altitudeIn = 0f;
        _circled = lands ? -1f : 0f;
    }

    /// <summary>Across to the next place at a height over whatever lies ahead, slowing as it nears it.</summary>
    void CruiseTo(float dt, float above)
    {
        N.Vector3 p = Vessel.Body.Position;
        N.Vector3 to = MathX.Flat(Spot - p);
        float d = to.Length();
        if (_circled >= 0f && d < CircleRadius + 30f) { Phase = SkyPhase.Circling; return; }
        if (_circled < 0f && d < 320f) { Phase = SkyPhase.Approaching; return; }
        N.Vector3 dir = d > 1e-3f ? to / d : MathX.Forward(Vessel.Body.Rotation);
        float speed = MathF.Min(Cruise, d * 0.1f + 6f);
        Hold(dir, speed, MathF.Min(d, MathF.Max(450f, speed * 14f)), Clearance, dt, boost: false);
    }

    const float CircleRadius = 160f;

    /// <summary>A scout's look round a place: once round it at a lower height, then on.</summary>
    void Circle(float dt)
    {
        N.Vector3 p = Vessel.Body.Position, out_ = MathX.Flat(p - Spot);
        float r = MathF.Max(out_.Length(), 1f);
        N.Vector3 radial = out_ / r, tangent = new(-radial.Z, 0f, radial.X);
        float speed = MathF.Min(Cruise, 30f);
        N.Vector3 dir = N.Vector3.Normalize(tangent * speed + radial * Math.Clamp(CircleRadius - r, -20f, 20f));
        _circled += speed / CircleRadius * dt;
        Hold(dir, speed, 260f, Clearance * 0.8f, dt, boost: false);
        if (_circled < MathF.Tau) return;
        Arrive(landed: false);
        Depart();
    }

    void Approach(float dt)
    {
        N.Vector3 p = Vessel.Body.Position, to = MathX.Flat(Spot - p);
        float d = to.Length();
        N.Vector3 dir = d > 1e-3f ? to / d : N.Vector3.Zero;
        float speed = Math.Clamp(d * 0.12f, 0f, Cruise);
        if (d > 3f) _heading = SkyPilot.HeadingOf(dir);
        // Down to a hover over the spot, held over the ground still between.
        if ((_altitudeIn -= dt) <= 0f)
        {
            _altitudeIn = 0.5f;
            _altitude = MathF.Max(Spot.Y + 30f, _traffic.HighestAhead(p, dir, d) + 25f);
        }
        Steer(dir * speed + N.Vector3.UnitY * Vertical(_altitude, p.Y), _heading);
        float flat = MathX.Flat(Vessel.Body.Velocity).Length();
        if (_search != null)
        {
            // Holding over it while another spot is found.
            if (!_traffic.Advance(_search)) return;
            if (_search.Spot is N.Vector3 again) Spot = again;
            _search = null;
            return;
        }
        if (d < 6f && flat < 2.5f)
        {
            // The spot is checked again now it can be seen: a tree or a hull there, and another is found.
            if (!_traffic.Clear(Spot, Vessel) && _retries++ < 3)
            {
                _search = _traffic.FindSpot(Bound, Vessel);
                return;
            }
            _retries = 0;
            Phase = SkyPhase.Descending;
        }
    }

    void Descend()
    {
        Vessel v = Vessel;
        float ground = _traffic.GroundUnder(v.Body.Position);
        float height = v.Body.Position.Y - FeetBelow(v) - ground;
        if (v.Controller is { Frozen: true })
        {
            Arrive(landed: true);
            return;
        }
        if (!_traffic.Collides(v.Body.Position) && height < 0.4f)
        {
            // No collision under it yet (out of the sleeper's reach): set down on the ground's height, as a hull out there is.
            v.Controller.PlaceLanded(v.Body, new N.Vector3(v.Body.Position.X, ground + FeetBelow(v) + 0.02f, v.Body.Position.Z), _heading);
            v.Binding.Teleport();
            Arrive(landed: true);
            return;
        }
        v.Flight.Input = SkyPilot.Land(v, new N.Vector2(Spot.X, Spot.Z), height, _heading);
    }

    void Arrive(bool landed)
    {
        At = Bound;
        if (!landed) return;
        Landings++;
        Visited.Add(At);
        Phase = SkyPhase.Landed;
        _wait = Wait();
        Vessel.Flight.Input = PilotInput.LetGo;
        _traffic.Landed(this);
    }

    /// <summary>How far its lowest point (its pads' feet, or its keel) is below its origin, m.</summary>
    public static float FeetBelow(Vessel v)
    {
        float feet = v.Body.Shape.FeetBelowOrigin;
        if (v.Spec != null)
            foreach (GearSpec g in v.Spec.LandingGear) feet = MathF.Max(feet, -g.Position.Y + g.Length);
        return feet;
    }

    // ---- Running ------------------------------------------------------------------------------------------

    void Flee()
    {
        Phase = SkyPhase.Fleeing;
        _search = null;
        Fled++;
        _fleeFor = 25f;
    }

    /// <summary>Away from the sleeper flat out, climbing, on the boost when it's ready; then on to a place away from them.</summary>
    void Run(float dt)
    {
        N.Vector3 p = Vessel.Body.Position, away = MathX.Flat(p - _traffic.Threat());
        N.Vector3 dir = away.LengthSquared() > 1f ? N.Vector3.Normalize(away) : MathX.Forward(Vessel.Body.Rotation);
        float top = Vessel.Stats?.TopSpeed ?? 50f;
        Hold(dir, top, 500f, Clearance + 120f, dt, boost: true);
        if ((_fleeFor -= dt) > 0f) return;
        // On to a place away from the sleeper, still running while its spot is found; with none, a look round it instead.
        if (_search == null)
        {
            PoiSite next = _traffic.NextStop(this, lands: true, awayFromThreat: true);
            if (next == null) { _fleeFor = 5f; return; }
            _search = _traffic.FindSpot(next, Vessel);
        }
        if (!_traffic.Advance(_search)) return;
        SkyTraffic.SpotSearch q = _search;
        _search = null;
        Begin(q.Site, q.Spot ?? _traffic.CentreOf(q.Site, p), lands: q.Spot != null);
    }

    // ---- Flying ------------------------------------------------------------------------------------------------

    /// <summary>On a flat heading at a speed, at a height over the ground ahead (sampled every half second): it climbs before it goes fast.</summary>
    void Hold(N.Vector3 dir, float speed, float ahead, float clearance, float dt, bool boost)
    {
        N.Vector3 p = Vessel.Body.Position;
        if ((_altitudeIn -= dt) <= 0f)
        {
            _altitudeIn = 0.5f;
            _altitude = MathF.Min(_traffic.HighestAhead(p, dir, ahead) + clearance, SkyTraffic.Ceiling);
        }
        float short_ = _altitude - p.Y;
        float hold = Math.Clamp(1f - (short_ - 25f) / 80f, 0.2f, 1f);
        _heading = SkyPilot.HeadingOf(dir);
        Steer(dir * speed * hold + N.Vector3.UnitY * Vertical(_altitude, p.Y), _heading, boost);
    }

    static float Vertical(float want, float y) => Math.Clamp((want - y) * 0.35f, -5f, 8f);

    void Steer(N.Vector3 velocity, float heading, bool boost = false) =>
        Vessel.Flight.Input = SkyPilot.Velocity(Vessel, velocity, heading, boost);
}
