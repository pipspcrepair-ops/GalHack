using Weather.Forces;
using Weather.Model;

namespace Weather;

/// <summary>Mass and wind profile of everything the force module pushes. Tests use the same numbers as the scene.</summary>
public static class Specs
{
    public const float PlayerMass = 100f;          // with suit and pack
    public const float PlayerDragArea = 0.9f;      // standing
    public const float PlayerCrouchDragArea = 0.45f;

    public const float HoverMass = 280f;           // a light skiff
    public const float HoverDragArea = 2.6f;

    public const float RoverMass = 6500f;          // a block-built hauler
    public const float RoverDragArea = 7.5f;
}

/// <summary>
/// The only place the weather model and the force module meet. Neither knows about the other:
/// the model describes tornadoes, the force module pushes bodies around vortices.
/// </summary>
public static class Glue
{
    public static Vortex ToVortex(Tornado t)
    {
        TornadoRules r = t.Rules;
        return new Vortex
        {
            Id = t.Id,
            Base = t.Position,
            Velocity = t.Velocity,
            Strength = t.Strength,
            CaptureRadius = r.CaptureRadius,
            InfluenceRadius = r.InfluenceRadius,
            OrbitRadius = r.OrbitRadius,
            OrbitSpeed = r.OrbitSpeed,
            InflowSpeed = r.InflowSpeed,
            LiftSpeed = r.LiftSpeed,
            ReleaseHeight = r.ReleaseHeight,
            MaxCarrySeconds = r.MaxCarrySeconds,
            FlingSpeed = r.FlingSpeed,
            ReleaseImmunity = r.ReleaseImmunity,
            LiftMassLimit = r.LiftMassLimit,
            Response = r.Response,
        };
    }

    /// <summary>Copies this tick's tornadoes and wind into the force module.</summary>
    public static void Sync(WeatherSim sim, ForceModule forces)
    {
        forces.Vortices.Clear();
        foreach (Tornado t in sim.Tornadoes)
            if (t.Touchdown) forces.Vortices.Add(ToVortex(t));
    }
}
