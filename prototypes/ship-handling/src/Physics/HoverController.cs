using Godot;
using ShipHandling.Handling;

namespace ShipHandling.Physics;

/// <summary>
/// Holds a hover vessel at its ride height. Each hover engine probes straight down for ground
/// or water; the vessel rides at the engines' average clearance. Look-ahead probes read the
/// ground in front so it rises over rocks early instead of snagging, and refuse slopes steeper
/// than the limit. Tilt follows the ground and settles within a second after a bump or a jump.
///
/// Height is a velocity servo, not a spring: the target climb rate is the ground's own rise
/// rate (from the probe normals) plus a gain times the height error, reached within fixed
/// acceleration limits. It tracks bumps tightly and can't oscillate.
/// </summary>
public sealed class HoverController
{
    readonly Vessel _v;
    float _lift, _jumpCooldown, _jumpGrace;
    static readonly float[] Reaches = { 0.5f, 1f }, Sides = { -1f, 1f };

    /// <summary>Mean world height of the ground or water under the engines.</summary>
    public float GroundRef { get; private set; }

    /// <summary>Mean height of the engines above what is under them. Ride height when all is well.</summary>
    public float Clearance { get; private set; } = float.PositiveInfinity;

    public Vector3 GroundNormal { get; private set; } = Vector3.Up;

    /// <summary>How far the look-ahead has raised the vessel above ride height, m.</summary>
    public float LookAheadLift => _lift;

    /// <summary>True while a slope ahead or underneath is steeper than the limit.</summary>
    public bool SlopeRefused { get; private set; }

    public bool Grounded { get; private set; }

    internal HoverController(Vessel vessel) => _v = vessel;

    internal void Reset()
    {
        _lift = 0f;
        _jumpCooldown = 0f;
        _jumpGrace = 0f;
        Clearance = float.PositiveInfinity;
        SlopeRefused = false;
    }

    internal void Step(float dt)
    {
        Tunables.HoverData h = _v.World.Tunables.Hover;
        HandlingStats stats = _v.Stats;
        ref FlightState s = ref _v.State;
        float ride = stats.RideHeight;
        float g = _v.Conditions.Gravity;
        float depth = ride * h.ProbeDepth;
        float maxSlopeCos = Mathf.Cos(Mathf.DegToRad(h.MaxSlope));
        Vector3 pos = _v.GlobalPosition;
        Basis basis = s.Orientation;
        var horizontal = new Vector3(s.Velocity.X, 0f, s.Velocity.Z);

        _jumpCooldown -= dt;
        _jumpGrace -= dt;

        // Engine probes: ground (or water) height, its normal, and how fast it rises under us.
        int hits = 0;
        float groundSum = 0f, engineSum = 0f, riseSum = 0f;
        Vector3 normalSum = Vector3.Zero;
        foreach (var engine in _v.Structure.HoverEngines)
        {
            Vector3 p = pos + basis * engine.Position;
            if (!_v.Ray(p + Vector3.Up * ride, p + Vector3.Down * depth, Layers.Probe, out Vector3 hit, out Vector3 n) || n.Y < 0.1f)
                continue;
            hits++;
            groundSum += hit.Y;
            engineSum += p.Y;
            normalSum += n;
            riseSum += -(n.X * horizontal.X + n.Z * horizontal.Z) / n.Y;
        }

        Grounded = hits > 0;
        if (!Grounded)
        {
            // Off a ledge or mid-jump, out of the engines' reach: fall freely and level out.
            Clearance = float.PositiveInfinity;
            SlopeRefused = false;
            s.Velocity.Y -= g * dt;
            Level(ref s, Vector3.Up, h.LevelRate, dt);
            return;
        }

        GroundRef = groundSum / hits;
        float engineY = engineSum / hits;
        Clearance = engineY - GroundRef;
        GroundNormal = normalSum.Normalized();
        float groundRise = Mathf.Clamp(riseSum / hits, -h.MaxClimbRate, h.MaxClimbRate);

        // Look-ahead: probe in front of the footprint, along the direction of travel.
        float speed = horizontal.Length();
        Vector3 dir = speed > 0.5f ? horizontal / speed : FlightModel.Frame(basis, true) * Vector3.Forward;
        Vector3 side = dir.Cross(Vector3.Up).Normalized();
        float halfLength = 0f, halfWidth = 0f;
        foreach (var engine in _v.Structure.HoverEngines)
        {
            Vector3 offset = basis * engine.Position;
            halfLength = Mathf.Max(halfLength, Mathf.Abs(offset.Dot(dir)));
            halfWidth = Mathf.Max(halfWidth, Mathf.Abs(offset.Dot(side)));
        }
        float reach = Mathf.Clamp(speed * h.LookAheadTime, h.LookAheadMin, h.LookAheadMax);

        // On a slope we're already climbing, the ground ahead is expected to be higher: only the
        // rise beyond that (a rock, a step) needs lifting for.
        float climbPerMetre = Mathf.Max(0f, -(GroundNormal.X * dir.X + GroundNormal.Z * dir.Z) / GroundNormal.Y);
        float aheadRise = 0f;
        Vector3? wall = null;
        float probeTop = engineY + ride + 3f;
        foreach (float k in Reaches)
        {
            float distance = halfLength + reach * k;
            foreach (float lateral in Sides)
            {
                Vector3 q = pos + dir * distance + side * halfWidth * lateral;
                if (!_v.Ray(new Vector3(q.X, probeTop, q.Z), new Vector3(q.X, GroundRef - depth, q.Z), Layers.Probe,
                        out Vector3 hit, out Vector3 n))
                    continue;
                bool uphill = n.X * dir.X + n.Z * dir.Z < 0f;
                if (n.Y < maxSlopeCos && uphill && hit.Y > GroundRef + 0.3f)
                {
                    wall = n; // too steep to climb: refuse it rather than lift over it
                    continue;
                }
                aheadRise = Mathf.Max(aheadRise, hit.Y - GroundRef - climbPerMetre * distance);
            }
        }
        float lift = Mathf.Max(0f, aheadRise - h.StepAllowance * ride);
        _lift = lift >= _lift ? lift : Mathf.MoveToward(_lift, lift, 2f * ride * dt); // rise at once, let down gently

        bool canHover = stats.CanLiftOff || g <= 0f;
        if (_v.Input.Seated && _v.Input.Jump && canHover && _jumpCooldown <= 0f && Clearance < ride + 0.6f)
        {
            s.Velocity.Y = Mathf.Max(s.Velocity.Y, 0f) + h.JumpSpeed;
            _jumpCooldown = h.JumpCooldown;
            _jumpGrace = 0.25f;
        }

        // Height servo. Grip (pulling down) only works close to the ground, so jumps fly.
        float error = GroundRef + ride + _lift - engineY;
        if (!canHover) s.Velocity.Y -= g * dt; // too heavy: the engines can't hold it up
        else
        {
            bool gripping = Clearance < ride + _lift + h.GripBand && _jumpGrace <= 0f;
            float wanted = groundRise + h.VerticalGain * error;
            if (!gripping && error < 0f) s.Velocity.Y -= g * dt;
            else
            {
                float limit = (wanted > s.Velocity.Y ? h.MaxRise : h.MaxGrip) * dt;
                s.Velocity.Y = Mathf.MoveToward(s.Velocity.Y, wanted, limit);
            }
        }

        // Slope limit: never climb into a slope steeper than the limit; slide off one underneath.
        bool steepUnder = GroundNormal.Y < maxSlopeCos;
        SlopeRefused = wall != null || steepUnder;
        if (wall is Vector3 w) BlockUphill(ref s, w);
        if (steepUnder)
        {
            BlockUphill(ref s, GroundNormal);
            var downhill = new Vector3(GroundNormal.X, 0f, GroundNormal.Z).Normalized();
            s.Velocity += downhill * g * Mathf.Sqrt(1f - GroundNormal.Y * GroundNormal.Y) * dt;
        }

        // Level with the ground (never tilting past the slope limit), or with the horizon in the air.
        Vector3 targetUp = Clearance < ride * 2.5f ? GroundNormal : Vector3.Up;
        float tilt = targetUp.AngleTo(Vector3.Up), maxTilt = Mathf.DegToRad(h.MaxSlope);
        if (tilt > maxTilt) targetUp = Vector3.Up.Slerp(targetUp, maxTilt / tilt);
        Level(ref s, targetUp, h.LevelRate, dt);
    }

    /// <summary>A bump tips the vessel a little; the levelling brings it back.</summary>
    internal void Kick(Vector3 normal, float closing)
    {
        Vector3 axis = normal.Cross(Vector3.Up);
        if (axis.LengthSquared() < 1e-4f) return;
        float angle = Mathf.Min(0.35f, closing * 0.03f);
        ref FlightState s = ref _v.State;
        s.Orientation = (new Basis(axis.Normalized(), -angle) * s.Orientation).Orthonormalized();
    }

    /// <summary>Takes out the part of horizontal velocity heading up a slope with this normal.</summary>
    static void BlockUphill(ref FlightState s, Vector3 slopeNormal)
    {
        var away = new Vector3(slopeNormal.X, 0f, slopeNormal.Z);
        if (away.LengthSquared() < 1e-6f) return;
        away = away.Normalized();
        float into = s.Velocity.X * away.X + s.Velocity.Z * away.Z;
        if (into < 0f) s.Velocity -= away * into;
    }

    /// <summary>Turns the vessel's up toward a target up, a fixed fraction per second; heading is kept.</summary>
    static void Level(ref FlightState s, Vector3 targetUp, float rate, float dt)
    {
        Vector3 up = s.Orientation.Y.Normalized();
        float angle = up.AngleTo(targetUp);
        if (angle < 1e-5f) return;
        Vector3 axis = up.Cross(targetUp);
        if (axis.LengthSquared() < 1e-10f) return;
        float k = 1f - Mathf.Exp(-rate * dt);
        s.Orientation = (new Basis(axis.Normalized(), angle * k) * s.Orientation).Orthonormalized();
    }
}
