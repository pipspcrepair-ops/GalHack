using System;
using CapitalBase.Core;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Terrain;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using N = System.Numerics;

namespace CapitalBase.World;

public enum LiftOffPhase
{
    /// <summary>The ground holding the hull has been cut; waiting for its new colliders to be built.</summary>
    Cutting,

    /// <summary>Climbing straight up through the cut at the climb speed.</summary>
    Climbing,

    /// <summary>Clear of the ground: the pilot has it.</summary>
    Done,
}

/// <summary>
/// One lift-off, after the console has cut the hull free: wait until the ground's colliders match
/// the cut (so the hull can't meet ground that no longer exists, or miss ground that does), then
/// climb straight up at a steady speed, through the swept volume the cut was made for, and hand
/// the ship back. The climb is flown by the handling model like any other flight: the stick is
/// held at "up" in the world's frame, so a tilted hull rises vertically too.
/// </summary>
/// <summary>A hull lifting off: its body and flight model, its top speed, and letting go of whatever pinned it to the ground.</summary>
public interface ILiftHull
{
    Body Body { get; }
    HandlingFlight Flight { get; }
    void LetGoOfGround();
}

/// <summary>The world it lifts off from: how much ground is still being rebuilt, the climb speed, the wind, the hull's top speed.</summary>
public interface ILiftGround
{
    int Pending { get; }
    float ClimbSpeed { get; }
    float TopSpeed(ILiftHull hull);
    void Settle(ILiftHull hull);
}

public sealed class LiftOff
{
    public readonly ILiftHull Hull;
    public readonly CutPlan Plan;
    public readonly PlanetEdit Edit;

    /// <summary>How long planning the cut took, ms.</summary>
    public readonly double PlanMs;

    public LiftOffPhase Phase { get; private set; } = LiftOffPhase.Cutting;
    public int CuttingTicks { get; private set; }
    public int ClimbTicks { get; private set; }
    public float Climbed { get; private set; }

    /// <summary>The fastest the hull rose during the climb, m/s: a launch would show here.</summary>
    public float TopRise { get; private set; }

    /// <summary>The farthest the hull strayed sideways from straight up during the climb, m.</summary>
    public float Sideways { get; private set; }

    /// <summary>Ticks in the climb when the hull rose less than half the climb speed (a snag would show here).</summary>
    public int Stalls { get; private set; }

    N.Vector3 _start;

    public LiftOff(ILiftHull hull, CutPlan plan, PlanetEdit edit, double planMs)
    {
        Hull = hull;
        Plan = plan;
        Edit = edit;
        PlanMs = planMs;
    }

    public void Step(ILiftGround world, float dt)
    {
        Body body = Hull.Body;
        switch (Phase)
        {
            case LiftOffPhase.Cutting:
                CuttingTicks++;
                if (world.Pending > 0) return;
                Hull.LetGoOfGround();
                world.Settle(Hull);
                _start = body.Position;
                Phase = LiftOffPhase.Climbing;
                Hull.Flight.Input = Climb(world);
                return;

            case LiftOffPhase.Climbing:
                ClimbTicks++;
                Climbed = body.Position.Y - _start.Y;
                float rise = body.Velocity.Y;
                TopRise = MathF.Max(TopRise, rise);
                Sideways = MathF.Max(Sideways, MathX.Flat(body.Position - _start).Length());
                // Once up to speed, the climb never slows: a snag on the ground would stall it.
                float speed = world.ClimbSpeed;
                if (TopRise >= speed * 0.9f && rise < speed * 0.5f && Climbed < Plan.Climb) Stalls++;
                if (Climbed >= Plan.Climb)
                {
                    Phase = LiftOffPhase.Done;
                    Hull.Flight.Input = PilotInput.LetGo;
                    return;
                }
                Hull.Flight.Input = Climb(world);
                return;
        }
    }

    /// <summary>"Up" at the climb speed, in the hull's own frame (the flight model steers in that frame).</summary>
    PilotInput Climb(ILiftGround world)
    {
        float top = world.TopSpeed(Hull);
        N.Vector3 local = MathX.Unrotate(Hull.Body.Rotation, N.Vector3.UnitY * (world.ClimbSpeed / top));
        return PilotInput.Hands(local.ToGodot());
    }
}
