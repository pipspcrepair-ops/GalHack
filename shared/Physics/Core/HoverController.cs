using System;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// Holds a hover vessel at its ride height (ported from ship-handling's physics module, the
/// same rules). Each hover engine probes straight down for ground or water; the vessel rides
/// at the engines' average clearance. Look-ahead probes read the ground in front so it rises
/// over rocks early instead of snagging, and refuse slopes steeper than the limit. Tilt follows
/// the ground and settles within a second after a bump or a jump.
///
/// Height is a velocity servo, not a spring: the target climb rate is the ground's own rise
/// rate (from the probe normals) plus a gain times the height error, reached within fixed
/// acceleration limits. It tracks bumps tightly and can't oscillate. A dug trench narrower than
/// the footprint is just a dip the engines average over.
/// </summary>
public sealed class HoverController
{
    readonly Vector3[] _engines;
    float _lift, _jumpCooldown, _jumpGrace;
    static readonly float[] Reaches = { 0.5f, 1f }, Sides = { -1f, 1f };

    /// <summary>Mean world height of the ground or water under the engines.</summary>
    public float GroundRef { get; private set; }

    /// <summary>Mean height of the engines above what is under them. Ride height when all is well.</summary>
    public float Clearance { get; private set; } = float.PositiveInfinity;

    public Vector3 GroundNormal { get; private set; } = Vector3.UnitY;

    /// <summary>How far the look-ahead has raised the vessel above ride height, m.</summary>
    public float LookAheadLift => _lift;

    /// <summary>True while a slope ahead or underneath is steeper than the limit.</summary>
    public bool SlopeRefused { get; private set; }

    public bool Grounded { get; private set; }

    /// <summary>Look-ahead probes on. Only switched off to prove what they are for.</summary>
    public bool LookAhead = true;

    /// <param name="engines">Engine positions in the vessel's frame: the plane they ride on.</param>
    public HoverController(Vector3[] engines) => _engines = engines;

    public int EngineCount => _engines.Length;

    public void Reset()
    {
        _lift = 0f;
        _jumpCooldown = 0f;
        _jumpGrace = 0f;
        Clearance = float.PositiveInfinity;
        SlopeRefused = false;
    }

    /// <summary>
    /// One tick: adjusts the vertical velocity and the tilt the flight model left. Falls freely
    /// (adding gravity itself) when the engines are out of reach of the ground.
    /// </summary>
    public void Step(PhysicsWorld world, Body body, ref Vector3 velocity, ref Quaternion orientation,
        float ride, bool canHover, float g, bool jump, float dt)
    {
        PhysicsRules.HoverData h = world.Rules.Hover;
        float depth = ride * h.ProbeDepth;
        float maxSlopeCos = MathF.Cos(Units.Radians(h.MaxSlope));
        Vector3 pos = body.Position;
        Quaternion basis = orientation;
        var horizontal = MathX.Flat(velocity);

        _jumpCooldown -= dt;
        _jumpGrace -= dt;

        // Engine probes: ground (or water) height, its normal, and how fast it rises under us.
        int hits = 0;
        float groundSum = 0f, engineSum = 0f, riseSum = 0f;
        Vector3 normalSum = Vector3.Zero;
        foreach (Vector3 engine in _engines)
        {
            Vector3 p = pos + MathX.Rotate(basis, engine);
            if (!world.Query.Ray(p + Vector3.UnitY * ride, p - Vector3.UnitY * depth, Layers.ProbeMask, body, out RayHit hit)
                || hit.Normal.Y < 0.1f)
                continue;
            Vector3 n = hit.Normal;
            hits++;
            groundSum += hit.Point.Y;
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
            velocity.Y -= g * dt;
            orientation = MathX.TiltToward(orientation, Vector3.UnitY, 1f - MathF.Exp(-h.LevelRate * dt));
            return;
        }

        GroundRef = groundSum / hits;
        float engineY = engineSum / hits;
        Clearance = engineY - GroundRef;
        GroundNormal = Vector3.Normalize(normalSum);
        float groundRise = Math.Clamp(riseSum / hits, -h.MaxClimbRate, h.MaxClimbRate);

        // Look-ahead: probe in front of the footprint, along the direction of travel.
        float speed = horizontal.Length();
        Vector3 dir = speed > 0.5f ? horizontal / speed : MathX.Forward(MathX.Level(basis));
        Vector3 side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));
        float halfLength = 0f, halfWidth = 0f;
        foreach (Vector3 engine in _engines)
        {
            Vector3 offset = MathX.Rotate(basis, engine);
            halfLength = MathF.Max(halfLength, MathF.Abs(Vector3.Dot(offset, dir)));
            halfWidth = MathF.Max(halfWidth, MathF.Abs(Vector3.Dot(offset, side)));
        }
        float reach = Math.Clamp(speed * h.LookAheadTime, h.LookAheadMin, h.LookAheadMax);

        // On a slope we're already climbing, the ground ahead is expected to be higher: only the
        // rise beyond that (a rock, a step) needs lifting for.
        Vector3 gn = GroundNormal;
        float climbPerMetre = MathF.Max(0f, -(gn.X * dir.X + gn.Z * dir.Z) / gn.Y);
        float aheadRise = 0f;
        Vector3? wall = null;
        float probeTop = engineY + ride + 3f;
        if (LookAhead)
        {
            foreach (float k in Reaches)
            {
                float distance = halfLength + reach * k;
                foreach (float lateral in Sides)
                {
                    Vector3 q = pos + dir * distance + side * halfWidth * lateral;
                    if (!world.Query.Ray(new Vector3(q.X, probeTop, q.Z), new Vector3(q.X, GroundRef - depth, q.Z),
                            Layers.ProbeMask, body, out RayHit hit))
                        continue;
                    Vector3 n = hit.Normal;
                    bool uphill = n.X * dir.X + n.Z * dir.Z < 0f;
                    if (n.Y < maxSlopeCos && uphill && hit.Point.Y > GroundRef + 0.3f)
                    {
                        wall = n; // too steep to climb: refuse it rather than lift over it
                        continue;
                    }
                    aheadRise = MathF.Max(aheadRise, hit.Point.Y - GroundRef - climbPerMetre * distance);
                }
            }
        }
        float lift = MathF.Max(0f, aheadRise - h.StepAllowance * ride);
        _lift = lift >= _lift ? lift : MathX.MoveToward(_lift, lift, 2f * ride * dt); // rise at once, let down gently

        if (jump && canHover && _jumpCooldown <= 0f && Clearance < ride + 0.6f)
        {
            velocity.Y = MathF.Max(velocity.Y, 0f) + h.JumpSpeed;
            _jumpCooldown = h.JumpCooldown;
            _jumpGrace = 0.25f;
        }

        // Height servo. Grip (pulling down) only works close to the ground, so jumps fly.
        float error = GroundRef + ride + _lift - engineY;
        if (!canHover) velocity.Y -= g * dt; // too heavy: the engines can't hold it up
        else
        {
            bool gripping = Clearance < ride + _lift + h.GripBand && _jumpGrace <= 0f;
            float wanted = groundRise + h.VerticalGain * error;
            if (!gripping && error < 0f) velocity.Y -= g * dt;
            else
            {
                float limit = (wanted > velocity.Y ? h.MaxRise : h.MaxGrip) * dt;
                velocity.Y = MathX.MoveToward(velocity.Y, wanted, limit);
            }
        }

        // Slope limit: never climb into a slope steeper than the limit; slide off one underneath.
        bool steepUnder = GroundNormal.Y < maxSlopeCos;
        SlopeRefused = wall != null || steepUnder;
        if (wall is Vector3 w) BlockUphill(ref velocity, w);
        if (steepUnder)
        {
            BlockUphill(ref velocity, GroundNormal);
            Vector3 downhill = MathX.SafeNormalize(MathX.Flat(GroundNormal), Vector3.Zero);
            velocity += downhill * g * MathF.Sqrt(1f - GroundNormal.Y * GroundNormal.Y) * dt;
        }

        // Level with the ground (never tilting past the slope limit), or with the horizon in the air.
        Vector3 targetUp = Clearance < ride * 2.5f ? GroundNormal : Vector3.UnitY;
        float tilt = MathX.Angle(targetUp, Vector3.UnitY), maxTilt = Units.Radians(h.MaxSlope);
        if (tilt > maxTilt) targetUp = MathX.Slerp(Vector3.UnitY, targetUp, maxTilt / tilt);
        orientation = MathX.TiltToward(orientation, targetUp, 1f - MathF.Exp(-h.LevelRate * dt));
    }

    /// <summary>A bump tips the vessel a little; the levelling brings it back.</summary>
    public static Quaternion Kick(Quaternion orientation, Vector3 normal, float closing)
    {
        Vector3 axis = Vector3.Cross(normal, Vector3.UnitY);
        if (axis.LengthSquared() < 1e-4f) return orientation;
        float angle = MathF.Min(0.35f, closing * 0.03f);
        return MathX.ThenWorld(orientation, MathX.AxisAngle(Vector3.Normalize(axis), -angle));
    }

    /// <summary>Takes out the part of horizontal velocity heading up a slope with this normal.</summary>
    static void BlockUphill(ref Vector3 velocity, Vector3 slopeNormal)
    {
        Vector3 away = MathX.Flat(slopeNormal);
        if (away.LengthSquared() < 1e-6f) return;
        away = Vector3.Normalize(away);
        float into = velocity.X * away.X + velocity.Z * away.Z;
        if (into < 0f) velocity -= away * into;
    }
}
