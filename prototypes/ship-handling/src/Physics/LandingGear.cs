using Godot;
using ShipHandling.Handling;

namespace ShipHandling.Physics;

/// <summary>
/// Landing legs. They drop by themselves near the ground at low speed and come up again when
/// the ship climbs or speeds away, with no button. Each foot probes straight down and works as a stiff suspension: it never
/// sinks into the ground, and a gentle touchdown costs nothing.
/// </summary>
public sealed class LandingGear
{
    readonly Vessel _v;
    bool _autoWant;

    /// <summary>Whether the gear is (going) down.</summary>
    public bool Down { get; private set; }

    /// <summary>0 retracted .. 1 fully down. Animated over the extend time.</summary>
    public float Extension { get; private set; }

    public bool Deployed => Extension >= 0.999f;

    /// <summary>Feet on (or within a hand's width of) the ground.</summary>
    public bool Contact { get; private set; }

    /// <summary>Lowest foot's height above the ground at the current extension, m.</summary>
    public float MinClearance { get; private set; } = float.PositiveInfinity;

    /// <summary>Lowest foot's height above the ground if the gear were fully down, m.</summary>
    public float FootAltitude { get; private set; } = float.PositiveInfinity;

    internal LandingGear(Vessel vessel) => _v = vessel;

    internal void Reset(bool down)
    {
        Down = down;
        _autoWant = down;
        Extension = down ? 1f : 0f;
        Contact = false;
    }

    internal void Step(float dt)
    {
        Tunables.GearData t = _v.World.Tunables.Gear;
        Probe();

        // The automatic wish, with hysteresis so it doesn't flicker at the threshold.
        float speed = _v.Speed;
        _autoWant = _autoWant
            ? !(FootAltitude > t.DeployAltitude + t.Hysteresis || speed > t.DeploySpeed + t.Hysteresis)
            : FootAltitude < t.DeployAltitude && speed < t.DeploySpeed;
        Down = _autoWant;

        Extension = Mathf.MoveToward(Extension, Down ? 1f : 0f, dt / Mathf.Max(t.ExtendTime, 1e-3f));
        Probe();
        Suspend();
    }

    /// <summary>Pushes the ship up out of the ground on its feet, absorbing downward speed.</summary>
    void Suspend()
    {
        if (Extension < 0.5f || !(MinClearance < 0f)) return;
        ref FlightState s = ref _v.State;
        _v.GlobalPosition += Vector3.Up * -MinClearance;
        if (s.Velocity.Y < 0f)
        {
            float closing = -s.Velocity.Y;
            s.Velocity.Y = 0f;
            // A gentle touchdown is free; slamming down on the legs is an impact like any other.
            float damage = ImpactRules.Damage(_v.Stats.Mass, closing, _v.World.Tunables);
            _v.GroundImpact(_lowestFoot, Vector3.Up, closing, damage);
        }
        MinClearance = 0f;
    }

    Vector3 _lowestFoot;

    internal void Probe()
    {
        Tunables.GearData t = _v.World.Tunables.Gear;
        Transform3D frame = _v.GlobalTransform;
        float lowest = float.PositiveInfinity, lowestFull = float.PositiveInfinity;
        foreach (var leg in _v.Structure.LandingGear)
        {
            Vector3 hip = frame * leg.Position;
            float reach = leg.Length + t.DeployAltitude + t.Hysteresis + 5f;
            if (!_v.Ray(hip + Vector3.Up * 0.5f, hip + Vector3.Down * reach, Layers.Probe, out Vector3 ground, out _))
                continue;
            float full = hip.Y - leg.Length - ground.Y;
            float now = hip.Y - leg.Length * Extension - ground.Y;
            lowestFull = Mathf.Min(lowestFull, full);
            if (now < lowest)
            {
                lowest = now;
                _lowestFoot = new Vector3(hip.X, ground.Y, hip.Z);
            }
        }
        FootAltitude = lowestFull;
        MinClearance = lowest;
        Contact = Extension > 0.5f && lowest < 0.2f;
    }
}
