using System;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// A walking body's own forces: legs, jumps and the suit jetpack (players; creatures can use
/// it too). Walking is a force like any other on the one force path, so wind and tornadoes add
/// to it, and a character controller (the mover) keeps the body out of the floor.
///
/// Numbers are walkable-ship's: brisk on the ground, a little air control that never bleeds off
/// momentum (jumping off a fast ship keeps its speed), and a short jetpack for climbing back in.
/// </summary>
public sealed class WalkController : IBodyController, IForceReactor
{
    // Inputs, set every tick by whoever controls the body.

    /// <summary>x right, y back (forward is -1), each -1..1, relative to <see cref="Yaw"/>.</summary>
    public Vector2 Move;

    /// <summary>Heading of the walk frame, radians, in the frame the body is in.</summary>
    public float Yaw;
    public bool Sprint, Jump, Jetpack;

    /// <summary>Zero-g aboard (core gravity off): drift and steer with suit thrusters instead of walking.</summary>
    public bool Floating;

    /// <summary>Up/down while floating, -1..1.</summary>
    public float Vertical;

    /// <summary>Look pitch, radians, for steering while floating.</summary>
    public float Pitch;

    public float WalkSpeed = 5.5f, SprintSpeed = 9f, GroundAccel = 45f, AirAccel = 10f, JumpSpeed = 4.8f;
    public float JetpackAccel = 18f, JetpackFuelMax = 3f, ZeroGSpeed = 5f, ZeroGAccel = 7f;

    /// <summary>
    /// How hard feet can hold against outside forces, m/s² of the body's mass. Enough to stand
    /// in a gale; a tornado's pull is stronger and drags you.
    /// </summary>
    public float Grip = 60f;

    public float JetpackFuel { get; private set; } = 3f;

    /// <summary>Walking reacts to what else pushes this tick, so it acts in <see cref="React"/>.</summary>
    public void Drive(PhysicsWorld world, Body body, float dt) { }

    public void React(PhysicsWorld world, Body body, float dt)
    {
        if (body.Asleep) return;
        Vector3 v = body.Velocity;
        float m = body.Mass;
        Vector3 change;

        if (Floating)
        {
            Quaternion look = MathX.ThenLocal(MathX.Yaw(Yaw), MathX.AxisAngle(Vector3.UnitX, Pitch));
            Vector3 wish = MathX.LimitLength(MathX.Rotate(look, new Vector3(Move.X, 0f, Move.Y)) + MathX.Up(look) * Vertical, 1f) * ZeroGSpeed;
            change = Toward(v, wish, ZeroGAccel * dt) - v;
            world.AddForce(body, m * change / dt, ForceKind.Walk);
            return;
        }

        bool onFloor = body.OnFloor;
        if (onFloor) JetpackFuel = MathX.MoveToward(JetpackFuel, JetpackFuelMax, dt);
        float speed = Sprint ? SprintSpeed : WalkSpeed;
        Vector2 move = Move.LengthSquared() > 1f ? Vector2.Normalize(Move) : Move;
        Vector3 wishDir = MathX.Rotate(MathX.Yaw(Yaw), new Vector3(move.X, 0f, move.Y));
        Vector3 wishVel = wishDir * speed;
        Vector3 h = MathX.Flat(v);

        if (onFloor)
        {
            // Feet on the ground: reach the wished speed, holding against whatever else pushes
            // sideways (wind) as far as grip allows. Gravity is vertical, so the flat part of the
            // force so far is all outside forces.
            var next = new Vector3(MathX.MoveToward(h.X, wishVel.X, GroundAccel * dt), 0f, MathX.MoveToward(h.Z, wishVel.Z, GroundAccel * dt));
            Vector3 legs = m * (next - h) / dt - MathX.Flat(body.PendingTotalForce);
            world.AddForce(body, MathX.LimitLength(legs, m * Grip), ForceKind.Walk);
        }
        else if (move != Vector2.Zero)
        {
            // In the air, steer without bleeding off momentum.
            Vector3 steered = h + Vector3.Normalize(wishDir) * AirAccel * dt;
            float cap = MathF.Max(h.Length(), speed);
            world.AddForce(body, m * (MathX.LimitLength(steered, cap) - h) / dt, ForceKind.Walk);
        }

        if (Jump && onFloor) world.AddImpulse(body, Vector3.UnitY * m * MathF.Max(0f, JumpSpeed - v.Y), ForceKind.Walk);
        else if (Jetpack && !onFloor && JetpackFuel > 0f && v.Y < 8f)
        {
            world.AddForce(body, Vector3.UnitY * m * JetpackAccel, ForceKind.Walk);
            JetpackFuel -= dt;
        }
    }

    public void AfterMove(PhysicsWorld world, Body body, float dt) { }

    static Vector3 Toward(Vector3 from, Vector3 to, float delta)
    {
        Vector3 d = to - from;
        float l = d.Length();
        return l <= delta || l < 1e-9f ? to : from + d / l * delta;
    }
}
