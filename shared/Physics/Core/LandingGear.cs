using System;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>A landing leg: where it hangs from the hull (vessel frame) and how far the foot reaches below.</summary>
public readonly record struct GearLeg(Vector3 Hip, float Length);

/// <summary>
/// Landing legs (ported from ship-handling's physics module). They drop by themselves near the
/// ground at low speed and come up again when the ship climbs or speeds away; the pilot's
/// toggle overrides that until the automatic choice next changes its mind. Each foot probes
/// straight down and works as a stiff suspension: it never sinks into the ground, and a gentle
/// touchdown costs nothing. Each foot remembers the ground it found, so a ship settling on
/// uneven, dug ground can sit on its feet instead of on one of them.
/// </summary>
public sealed class LandingGear
{
    readonly GearLeg[] _legs;
    bool _autoWant;
    Vector3 _lowestFoot;

    public LandingGear(GearLeg[] legs)
    {
        _legs = legs;
        FootGround = new Vector3[legs.Length];
        FootFound = new bool[legs.Length];
    }

    public int Count => _legs.Length;
    public GearLeg Leg(int i) => _legs[i];

    /// <summary>Whether the gear is (going) down.</summary>
    public bool Down { get; private set; }

    /// <summary>0 retracted .. 1 fully down. Animated over the extend time.</summary>
    public float Extension { get; private set; }

    public bool Deployed => Extension >= 0.999f;

    /// <summary>True while the last change came from the pilot rather than the automatic rule.</summary>
    public bool Manual { get; private set; }

    /// <summary>Feet on (or within a hand's width of) the ground.</summary>
    public bool Contact { get; private set; }

    /// <summary>Lowest foot's height above the ground at the current extension, m.</summary>
    public float MinClearance { get; private set; } = float.PositiveInfinity;

    /// <summary>Lowest foot's height above the ground if the gear were fully down, m.</summary>
    public float FootAltitude { get; private set; } = float.PositiveInfinity;

    /// <summary>The ground under each foot at the last probe (valid where <see cref="FootFound"/>).</summary>
    public Vector3[] FootGround { get; }
    public bool[] FootFound { get; }

    public void Toggle()
    {
        Down = !Down;
        Manual = true;
    }

    public void Reset(bool down)
    {
        Down = down;
        _autoWant = down;
        Extension = down ? 1f : 0f;
        Manual = false;
        Contact = false;
    }

    public void Step(PhysicsWorld world, Body body, ref Vector3 velocity, float dt)
    {
        PhysicsRules.GearData t = world.Rules.Gear;
        Probe(world, body);

        // The automatic wish, with hysteresis so it doesn't flicker at the threshold.
        float speed = body.WorldVelocity.Length();
        bool want = _autoWant
            ? !(FootAltitude > t.DeployAltitude + t.Hysteresis || speed > t.DeploySpeed + t.Hysteresis)
            : FootAltitude < t.DeployAltitude && speed < t.DeploySpeed;
        if (want != _autoWant)
        {
            _autoWant = want;
            Manual = false; // the automatic rule speaks again
        }
        if (!Manual) Down = _autoWant;

        Extension = MathX.MoveToward(Extension, Down ? 1f : 0f, dt / MathF.Max(t.ExtendTime, 1e-3f));
        Probe(world, body);
        Suspend(world, body, ref velocity);
    }

    /// <summary>Pushes the ship up out of the ground on its feet, absorbing downward speed.</summary>
    void Suspend(PhysicsWorld world, Body body, ref Vector3 velocity)
    {
        if (Extension < 0.5f || !(MinClearance < 0f)) return;
        body.Position += Vector3.UnitY * -MinClearance;
        if (velocity.Y < 0f)
        {
            float closing = -velocity.Y;
            velocity.Y = 0f;
            // A gentle touchdown is free; slamming down on the legs is an impact like any other.
            world.ResolveImpact(body, null, _lowestFoot, Vector3.UnitY, closing, Surface.Ground);
        }
        MinClearance = 0f;
    }

    public void Probe(PhysicsWorld world, Body body)
    {
        PhysicsRules.GearData t = world.Rules.Gear;
        float lowest = float.PositiveInfinity, lowestFull = float.PositiveInfinity;
        for (int i = 0; i < _legs.Length; i++)
        {
            GearLeg leg = _legs[i];
            Vector3 hip = body.ToParent(leg.Hip);
            float reach = leg.Length + t.DeployAltitude + t.Hysteresis + 5f;
            FootFound[i] = world.Query.Ray(hip + Vector3.UnitY * 0.5f, hip - Vector3.UnitY * reach, Layers.ProbeMask, body, out RayHit ground);
            if (!FootFound[i]) continue;
            FootGround[i] = ground.Point;
            float full = hip.Y - leg.Length - ground.Point.Y;
            float now = hip.Y - leg.Length * Extension - ground.Point.Y;
            lowestFull = MathF.Min(lowestFull, full);
            if (now < lowest)
            {
                lowest = now;
                _lowestFoot = new Vector3(hip.X, ground.Point.Y, hip.Z);
            }
        }
        FootAltitude = lowestFull;
        MinClearance = lowest;
        Contact = Extension > 0.5f && lowest < 0.2f;
    }

    /// <summary>
    /// The up direction that puts every foot on its ground: a plane fitted through the ground
    /// under the feet, tilted no more than <paramref name="maxTilt"/> radians. Level when there
    /// are fewer than three feet on something.
    /// </summary>
    public Vector3 GroundUp(Body body, float maxTilt)
    {
        // Least squares fit of y = a·x + b·z + c through the ground points, relative to the body.
        double sxx = 0, sxz = 0, szz = 0, sx = 0, sz = 0, sy = 0, sxy = 0, szy = 0;
        int n = 0;
        for (int i = 0; i < _legs.Length; i++)
        {
            if (!FootFound[i]) continue;
            Vector3 p = FootGround[i] - body.Position;
            sxx += p.X * p.X; sxz += p.X * p.Z; szz += p.Z * p.Z;
            sx += p.X; sz += p.Z; sy += p.Y; sxy += p.X * p.Y; szy += p.Z * p.Y;
            n++;
        }
        if (n < 3) return Vector3.UnitY;

        // Normal equations, centred so they stay well conditioned.
        double mx = sx / n, mz = sz / n, my = sy / n;
        double cxx = sxx / n - mx * mx, cxz = sxz / n - mx * mz, czz = szz / n - mz * mz;
        double cxy = sxy / n - mx * my, czy = szy / n - mz * my;
        double det = cxx * czz - cxz * cxz;
        if (Math.Abs(det) < 1e-6) return Vector3.UnitY;
        double a = (cxy * czz - czy * cxz) / det, b = (czy * cxx - cxy * cxz) / det;
        Vector3 up = Vector3.Normalize(new Vector3((float)-a, 1f, (float)-b));
        float tilt = MathX.Angle(up, Vector3.UnitY);
        return tilt > maxTilt ? MathX.Slerp(Vector3.UnitY, up, maxTilt / tilt) : up;
    }
}
