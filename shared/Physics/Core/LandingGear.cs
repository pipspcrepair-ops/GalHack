using System;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// One landing pad: where its face is on the hull (vessel frame) and how far its telescoping leg
/// reaches below it, m (0: a bare pad, which attaches only where its own face touches).
/// </summary>
public readonly record struct GearLeg(Vector3 Hip, float Length);

/// <summary>
/// Landing gear as an attachment point, not a load-bearing part: in effect a docking pad. It comes
/// out by itself near the ground at low speed and goes in again when the craft climbs or speeds
/// away, with no button. Each telescoping leg reaches down until its own foot meets the surface
/// under it, so on uneven ground or a host's deck every foot stands on its own ground. A foot on a
/// surface attaches the craft there: to the ground, or to another structure (it is then parked on
/// it). It carries no load and nothing counts legs or mass: what holds a craft up is its hull's
/// collision and, once attached and landed, its being landed. A foot never stands on a tree too thin
/// for the craft's weight (<see cref="PhysicsWorld.Footing"/>): the hull breaks that tree. Gear is a
/// weak point: a pad shot off or broken on a hard landing is simply gone from the structure, and its
/// attachment with it.
/// </summary>
public sealed class LandingGear
{
    readonly GearLeg[] _legs;
    readonly float[] _drop;

    /// <summary>A foot within this of the surface under it touches it, m.</summary>
    public const float Touch = 0.2f;

    public LandingGear(GearLeg[] legs)
    {
        _legs = legs;
        _drop = new float[legs.Length];
        FootGround = new Vector3[legs.Length];
        FootFound = new bool[legs.Length];
        FootBody = new Body[legs.Length];
        FootTree = new long[legs.Length];
    }

    public int Count => _legs.Length;
    public GearLeg Leg(int i) => _legs[i];

    /// <summary>Whether the gear is (going) out.</summary>
    public bool Down { get; private set; }

    /// <summary>0 retracted .. 1 fully out. Animated over the extend time.</summary>
    public float Extension { get; private set; }

    public bool Deployed => Extension >= 0.999f;

    /// <summary>A foot is on a surface: the craft is attached there.</summary>
    public bool Contact { get; private set; }

    /// <summary>Out, with a foot on a surface.</summary>
    public bool Attached => Deployed && Contact;

    /// <summary>The lowest foot's height above the surface under it with its leg fully out, m.</summary>
    public float FootAltitude { get; private set; } = float.PositiveInfinity;

    /// <summary>The surface under each foot at the last probe (valid where <see cref="FootFound"/>).</summary>
    public Vector3[] FootGround { get; }
    public bool[] FootFound { get; }

    /// <summary>The registered body under each foot (a structure's deck), or null for the ground.</summary>
    public Body[] FootBody { get; }

    /// <summary>The tree under each foot (<see cref="IFloraField"/>), or 0 for anything else.</summary>
    public long[] FootTree { get; }

    /// <summary>A foot of it stands on this tree.</summary>
    public bool Stands(long tree)
    {
        for (int i = 0; i < _legs.Length; i++)
            if (FootTree[i] == tree && Touching(i)) return true;
        return false;
    }

    /// <summary>How far leg <paramref name="i"/> is out now, m: down to its own surface when that is within its reach.</summary>
    public float Reach(int i)
    {
        float most = _legs[i].Length * Extension;
        return FootFound[i] ? Math.Clamp(_drop[i], 0f, most) : most;
    }

    /// <summary>Whether foot <paramref name="i"/> is on the surface under it.</summary>
    public bool Touching(int i) => Extension > 0.5f && FootFound[i] && _drop[i] <= _legs[i].Length * Extension + Touch;

    /// <summary>The registered body an attached foot stands on (a deck rather than the ground), else null.</summary>
    public Body StandingOn
    {
        get
        {
            if (!Attached) return null;
            for (int i = 0; i < _legs.Length; i++)
                if (Touching(i) && FootBody[i] != null) return FootBody[i];
            return null;
        }
    }

    public void Reset(bool down)
    {
        Down = down;
        Extension = down ? 1f : 0f;
        Contact = false;
    }

    public void Step(PhysicsWorld world, Body body, float dt)
    {
        PhysicsRules.GearData t = world.Rules.Gear;
        Probe(world, body);
        // Out below the deploy height and speed, in again past them plus the hysteresis, so it doesn't flicker.
        float speed = body.WorldVelocity.Length();
        Down = Down
            ? !(FootAltitude > t.DeployAltitude + t.Hysteresis || speed > t.DeploySpeed + t.Hysteresis)
            : FootAltitude < t.DeployAltitude && speed < t.DeploySpeed;
        Extension = MathX.MoveToward(Extension, Down ? 1f : 0f, dt / MathF.Max(t.ExtendTime, 1e-3f));
        Probe(world, body);
    }

    public void Probe(PhysicsWorld world, Body body)
    {
        PhysicsRules.GearData t = world.Rules.Gear;
        float lowest = float.PositiveInfinity;
        bool contact = false;
        for (int i = 0; i < _legs.Length; i++)
        {
            GearLeg leg = _legs[i];
            Vector3 hip = body.ToParent(leg.Hip);
            float reach = leg.Length + t.DeployAltitude + t.Hysteresis + 5f;
            FootFound[i] = world.Footing(body, hip + Vector3.UnitY * 0.5f, hip - Vector3.UnitY * reach, out RayHit ground, out FootTree[i]);
            FootBody[i] = FootFound[i] ? ground.Body : null;
            if (!FootFound[i]) continue;
            FootGround[i] = ground.Point;
            _drop[i] = hip.Y - ground.Point.Y;
            lowest = MathF.Min(lowest, _drop[i] - leg.Length);
            contact |= Touching(i);
        }
        FootAltitude = lowest;
        Contact = contact;
    }
}
