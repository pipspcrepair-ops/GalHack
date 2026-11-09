using System;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// Holds a hover vessel at its ride height (ported from ship-handling's physics module, the
/// same rules). Each hover engine probes along the command chair's down for ground or water, and
/// pushes the craft away along it; the vessel rides
/// at the engines' average clearance. Look-ahead probes read the ground in front so it rises
/// over rocks early instead of snagging.
///
/// It follows the ground's attitude, pitching and rolling to the plane through what its engines
/// and look-ahead probes see (never more than the slope limit), so its nose comes up as it
/// reaches a rise and it drives down into and back out of dips, dug holes and craters. The
/// look-ahead points keep a short gap from tipping it: a dug trench narrower than the footprint
/// is just a dip the engines average over. Slopes are judged the same way, as a grade across
/// the vessel's own length (the rise from the ground under it to the ground ahead, over the run),
/// not by the face at one point: a crater's short, steep lip is climbed, a long ramp or cliff
/// steeper than the limit is refused.
///
/// Height is a velocity servo, not a spring: the target climb rate is the ground's own rise
/// rate (from its attitude) plus a gain times the height error, reached within fixed
/// acceleration limits. It tracks bumps tightly and can't oscillate.
/// </summary>
public sealed class HoverController
{
    readonly Vector3[] _engines;
    readonly Vector3[] _seen;   // this tick's probe hits: engines, then look-ahead
    float _lift, _jumpCooldown, _jumpGrace;
    static readonly float[] Reaches = { 0.5f, 1f }, Sides = { -1f, 1f };

    /// <summary>
    /// How strongly the attitude leans on the probes' own normals where its points can't say
    /// (a single probe, engines in a line), m². Small beside any real footprint's spread.
    /// </summary>
    const float NormalPrior = 0.5f;

    /// <summary>Mean world height of the ground or water under the engines.</summary>
    public float GroundRef { get; private set; }

    /// <summary>Mean height of the engines above what is under them. Ride height when all is well.</summary>
    public float Clearance { get; private set; } = float.PositiveInfinity;

    /// <summary>The up of the ground under the engines: the plane through their probes' hits.</summary>
    public Vector3 GroundNormal { get; private set; } = Vector3.UnitY;

    /// <summary>The attitude the vessel tilts to: the plane through what the engines and the look-ahead probes see.</summary>
    public Vector3 Attitude { get; private set; } = Vector3.UnitY;

    /// <summary>How far the look-ahead has raised the vessel above ride height, m.</summary>
    public float LookAheadLift => _lift;

    /// <summary>True while a slope ahead or underneath is steeper than the limit.</summary>
    public bool SlopeRefused { get; private set; }

    /// <summary>The steepest grade seen ahead this tick, degrees: the rise from the ground under the vessel over the run to it.</summary>
    public float SteepestAhead { get; private set; }

    public bool Grounded { get; private set; }

    /// <summary>Look-ahead probes on. Only switched off to prove what they are for.</summary>
    public bool LookAhead = true;

    /// <summary>
    /// The command chair's down in the vessel's frame: the engines probe for ground along it and push the craft away
    /// along it. Tipped onto its side or its roof, its engines face nothing below and hold nothing up: it falls and
    /// its levelling turns it back onto them.
    /// </summary>
    public Vector3 Down = -Vector3.UnitY;

    /// <param name="engines">Engine positions in the vessel's frame: the plane they ride on.</param>
    public HoverController(Vector3[] engines)
    {
        _engines = engines;
        _seen = new Vector3[engines.Length + Reaches.Length * Sides.Length];
    }

    public int EngineCount => _engines.Length;

    public void Reset()
    {
        _lift = 0f;
        _jumpCooldown = 0f;
        _jumpGrace = 0f;
        Clearance = float.PositiveInfinity;
        SlopeRefused = false;
        SteepestAhead = 0f;
        GroundNormal = Attitude = Vector3.UnitY;
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
        float maxSlopeCos = MathF.Cos(Units.Radians(h.MaxSlope)), maxGrade = MathF.Tan(Units.Radians(h.MaxSlope));
        Vector3 pos = body.Position;
        Quaternion basis = orientation;
        var horizontal = MathX.Flat(velocity);

        _jumpCooldown -= dt;
        _jumpGrace -= dt;

        // Engine probes: the ground (or water) under each engine, along the chair's down. On its side or its roof
        // they face away from the ground and find none (it falls, levelling).
        Vector3 down = MathX.Rotate(basis, Down);
        int hits = 0;
        float groundSum = 0f, engineSum = 0f;
        Vector3 normalSum = Vector3.Zero;
        foreach (Vector3 engine in _engines)
        {
            Vector3 p = pos + MathX.Rotate(basis, engine);
            if (down.Y > -0.3f || !world.Query.Ray(p - down * ride, p + down * depth, Layers.ProbeMask, body, out RayHit hit)
                || hit.Normal.Y < 0.1f)
                continue;
            _seen[hits++] = hit.Point;
            groundSum += hit.Point.Y;
            engineSum += p.Y;
            normalSum += hit.Normal;
        }

        Grounded = hits > 0;
        if (!Grounded)
        {
            // Off a ledge or mid-jump, out of the engines' reach: fall freely and level out.
            Clearance = float.PositiveInfinity;
            SlopeRefused = false;
            SteepestAhead = 0f;
            velocity.Y -= g * dt;
            orientation = MathX.TiltToward(orientation, Vector3.UnitY, 1f - MathF.Exp(-h.LevelRate * dt));
            return;
        }

        GroundRef = groundSum / hits;
        float engineY = engineSum / hits;
        Clearance = engineY - GroundRef;
        int under = hits;
        Vector3 underNormal = Vector3.Normalize(normalSum);

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

        // The ground ahead: anything rising more steeply than the limit, as a grade from the ground
        // under the vessel over the run to it, is refused rather than lifted over. A short steep
        // lip (a crater's rim, a dug edge) rises little over that run and is climbed; a long ramp
        // or a cliff steeper than the limit is not.
        float steepest = 0f;
        if (LookAhead)
        {
            foreach (float k in Reaches)
            {
                float distance = halfLength + reach * k;
                // From above anything it could climb there, so a wall ahead is seen, not started inside.
                float probeTop = MathF.Max(engineY, GroundRef + distance * maxGrade) + ride + 3f;
                foreach (float lateral in Sides)
                {
                    Vector3 q = pos + dir * distance + side * halfWidth * lateral;
                    if (!world.Query.Ray(new Vector3(q.X, probeTop, q.Z), new Vector3(q.X, GroundRef - depth, q.Z),
                            Layers.ProbeMask, body, out RayHit hit))
                        continue;
                    _seen[hits++] = hit.Point;
                    steepest = MathF.Max(steepest, (hit.Point.Y - GroundRef) / distance);
                }
            }
        }
        SteepestAhead = Units.Degrees(MathF.Atan(steepest));
        bool wall = steepest > maxGrade;

        // The ground under the engines (how fast it rises under the vessel, whether it is too steep
        // to stay on), and the attitude to tilt to: the plane through what the engines and the
        // look-ahead see, so the nose comes up as a rise arrives and a short gap doesn't tip it.
        GroundNormal = FitPlane(_seen.AsSpan(0, under), pos, underNormal);
        Attitude = hits > under ? FitPlane(_seen.AsSpan(0, hits), pos, underNormal) : GroundNormal;
        Vector3 gn = GroundNormal;
        float groundRise = Math.Clamp(-(gn.X * horizontal.X + gn.Z * horizontal.Z) / gn.Y, -h.MaxClimbRate, h.MaxClimbRate);

        // On a slope we're already climbing, the ground ahead is expected to be higher: only the
        // rise beyond that (a rock, a step) needs lifting for.
        float climbPerMetre = MathF.Max(0f, -(gn.X * dir.X + gn.Z * dir.Z) / gn.Y);
        float aheadRise = 0f;
        if (!wall)
            for (int i = under; i < hits; i++)
                aheadRise = MathF.Max(aheadRise, _seen[i].Y - GroundRef - climbPerMetre * Vector3.Dot(MathX.Flat(_seen[i] - pos), dir));
        float lift = Math.Clamp(aheadRise - h.StepAllowance * ride, 0f, h.MaxLift * ride);
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
                float vy = velocity.Y;
                velocity.Y = MathX.MoveToward(velocity.Y, wanted, limit);
                // The engines push along the chair's down: what they change in the climb, tipped, pushes it sideways too.
                velocity += new Vector3(-down.X, 0f, -down.Z) / -down.Y * (velocity.Y - vy);
            }
        }

        // Slope limit: never climb into a slope steeper than the limit; slide off one it is on (the
        // ground under it and ahead: a short steep lip under its engines alone is just being climbed).
        Vector3 att = Attitude;
        bool steepUnder = att.Y < maxSlopeCos;
        SlopeRefused = wall || steepUnder;
        if (wall) BlockUphill(ref velocity, Vector3.Normalize(new Vector3(-dir.X * steepest, 1f, -dir.Z * steepest)));
        if (steepUnder)
        {
            BlockUphill(ref velocity, att);
            Vector3 downhill = MathX.SafeNormalize(MathX.Flat(att), Vector3.Zero);
            velocity += downhill * g * MathF.Sqrt(1f - att.Y * att.Y) * dt;
        }

        // Tilt to the ground's attitude (never past the slope limit), or level with the horizon in the air.
        Vector3 targetUp = Clearance < ride * 2.5f ? Attitude : Vector3.UnitY;
        float tilt = MathX.Angle(targetUp, Vector3.UnitY), maxTilt = Units.Radians(h.MaxSlope);
        if (tilt > maxTilt) targetUp = MathX.Slerp(Vector3.UnitY, targetUp, maxTilt / tilt);
        orientation = MathX.TiltToward(orientation, targetUp, 1f - MathF.Exp(-h.LevelRate * dt));
    }

    /// <summary>
    /// The plane through some ground points, by least squares, as its up normal. Where the points
    /// can't tell a tilt (one point, or all in a line) it leans on <paramref name="normal"/>, the
    /// probes' own surface normals.
    /// </summary>
    static Vector3 FitPlane(ReadOnlySpan<Vector3> points, Vector3 origin, Vector3 normal)
    {
        Vector3 mean = Vector3.Zero;
        foreach (Vector3 p in points) mean += p - origin;
        mean /= points.Length;
        float ny = MathF.Max(normal.Y, 0.1f);
        float sxx = NormalPrior, szz = NormalPrior, sxz = 0f;
        float sxy = NormalPrior * -normal.X / ny, szy = NormalPrior * -normal.Z / ny;
        foreach (Vector3 p in points)
        {
            Vector3 d = p - origin - mean;
            sxx += d.X * d.X;
            szz += d.Z * d.Z;
            sxz += d.X * d.Z;
            sxy += d.X * d.Y;
            szy += d.Z * d.Y;
        }
        // Solve [sxx sxz; sxz szz] (b, c) = (sxy, szy) for the plane y = b x + c z. The prior keeps
        // the determinant at least NormalPrior², so it never divides by nothing.
        float det = sxx * szz - sxz * sxz;
        float b = (sxy * szz - szy * sxz) / det, c = (szy * sxx - sxy * sxz) / det;
        return Vector3.Normalize(new Vector3(-b, 1f, -c));
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
