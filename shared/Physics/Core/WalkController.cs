using System;
using System.Numerics;

namespace GalHack.Physics;

/// <summary>
/// A walking body's own forces: legs, jumps and the suit jetpack (players; creatures can use
/// it too). Walking is a force like any other on the one force path, so wind and tornadoes add
/// to it, and a character controller (the mover) keeps the body out of the floor.
///
/// It walks on whatever face the body's <see cref="Body.Up"/> says is underfoot: the ground,
/// a cabin floor, or the side or underside of a hull in mag boots. Its look (<see cref="Yaw"/>,
/// <see cref="Pitch"/>) is relative to the body's walk frame, <see cref="Body.Rotation"/>.
///
/// Numbers are walkable-ship's: brisk on the ground, a little air control that never bleeds off
/// momentum (jumping off a fast ship keeps its speed), and a short jetpack for climbing back in.
/// </summary>
public sealed class WalkController : IBodyController, IForceReactor
{
    // Inputs, set every tick by whoever controls the body.

    /// <summary>x right, y back (forward is -1), each -1..1, relative to <see cref="Yaw"/>.</summary>
    public Vector2 Move;

    /// <summary>Heading within the walk frame, radians.</summary>
    public float Yaw;
    public bool Sprint, Jump, Jetpack;

    /// <summary>
    /// Zero-g (no gravity where the body is): steer with suit thrusters instead of walking. The
    /// thrusters fire only when asked, so a body coasts on its momentum.
    /// </summary>
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

    /// <summary>The legs pushed off this tick (a deliberate jump: it lets go of a hull at once).</summary>
    public bool Jumped { get; private set; }

    /// <summary>
    /// In water deep enough to swim: more than the rules' swimDepth of the body is under (and it
    /// wades again below wadeDepth). Swimming, <see cref="Move"/> strokes along the heading and
    /// <see cref="Vertical"/> goes up or down (Space and Ctrl): no other controls. Left alone, a
    /// swimmer drifts, and floats up to the surface if it is lighter than water.
    /// </summary>
    public bool Swimming { get; private set; }

    /// <summary>Walking reacts to what else pushes this tick, so it acts in <see cref="React"/>.</summary>
    public void Drive(PhysicsWorld world, Body body, float dt) { }

    public void React(PhysicsWorld world, Body body, float dt)
    {
        Jumped = false;
        if (body.Asleep) return;
        Vector3 v = body.Velocity, up = Vector3.Normalize(body.Up);
        float m = body.Mass;
        Quaternion frame = body.Rotation;
        Quaternion heading = MathX.ThenLocal(frame, MathX.Yaw(Yaw));

        PhysicsRules.WaterData water = world.Rules.Water;
        float wet = body.Water.Fraction;
        Swimming = wet > 0f && (Swimming ? wet > water.WadeDepth : wet >= water.SwimDepth);
        if (Swimming)
        {
            // Strokes reach the wished velocity, holding against whatever else pushes so far this
            // tick (weight, buoyancy, drag, wind) as far as the swimmer's strength allows. Without
            // a wish there are no strokes: the water has its way.
            Vector2 stroke = Move.LengthSquared() > 1f ? Vector2.Normalize(Move) : Move;
            Vector3 swimWish = MathX.LimitLength(MathX.Rotate(heading, new Vector3(stroke.X, 0f, stroke.Y)) + up * Vertical, 1f);
            if (swimWish.LengthSquared() < 1e-6f) return;
            float swimSpeed = Sprint ? water.SwimSprintSpeed : water.SwimSpeed;
            Vector3 swum = Toward(v, swimWish * swimSpeed, water.SwimAccel * dt);
            world.AddForce(body, MathX.LimitLength(m * (swum - v) / dt - body.PendingTotalForce, m * water.SwimGrip), ForceKind.Walk);
            return;
        }

        if (Floating)
        {
            Quaternion look = MathX.ThenLocal(heading, MathX.AxisAngle(Vector3.UnitX, Pitch));
            Vector3 wish = MathX.LimitLength(MathX.Rotate(look, new Vector3(Move.X, 0f, Move.Y)) + MathX.Up(look) * Vertical, 1f);
            if (wish.LengthSquared() < 1e-6f) return; // no thrusters asked for: keep the momentum
            world.AddForce(body, m * (Toward(v, wish * ZeroGSpeed, ZeroGAccel * dt) - v) / dt, ForceKind.Walk);
            return;
        }

        bool onFloor = body.OnFloor;
        if (onFloor) JetpackFuel = MathX.MoveToward(JetpackFuel, JetpackFuelMax, dt);
        float speed = Sprint ? SprintSpeed : WalkSpeed;
        Vector2 move = Move.LengthSquared() > 1f ? Vector2.Normalize(Move) : Move;
        Vector3 wishDir = MathX.Rotate(heading, new Vector3(move.X, 0f, move.Y));
        Vector3 h = v - up * Vector3.Dot(v, up); // velocity along the face underfoot

        if (onFloor)
        {
            // Feet on the face: reach the wished speed, holding against whatever else pushes along
            // it (wind) as far as grip allows. Gravity pushes into the face, so the part of the force
            // so far that runs along it is all outside forces. Worked out in the walk frame, axis by
            // axis, exactly as on flat ground.
            Vector3 hf = MathX.Unrotate(frame, h), wf = MathX.Unrotate(frame, wishDir * speed);
            var nextF = new Vector3(MathX.MoveToward(hf.X, wf.X, GroundAccel * dt), 0f, MathX.MoveToward(hf.Z, wf.Z, GroundAccel * dt));
            Vector3 next = MathX.Rotate(frame, nextF);
            Vector3 outside = body.PendingTotalForce - up * Vector3.Dot(body.PendingTotalForce, up);
            Vector3 legs = m * (next - h) / dt - outside;
            world.AddForce(body, MathX.LimitLength(legs, m * Grip), ForceKind.Walk);
        }
        else if (move != Vector2.Zero)
        {
            // In the air, steer without bleeding off momentum.
            Vector3 steered = h + Vector3.Normalize(wishDir) * AirAccel * dt;
            float cap = MathF.Max(h.Length(), speed);
            world.AddForce(body, m * (MathX.LimitLength(steered, cap) - h) / dt, ForceKind.Walk);
        }

        if (Jump && onFloor)
        {
            world.AddImpulse(body, up * m * MathF.Max(0f, JumpSpeed - Vector3.Dot(v, up)), ForceKind.Walk);
            Jumped = true;
        }
        else if (Jetpack && !onFloor && JetpackFuel > 0f && Vector3.Dot(v, up) < 8f)
        {
            world.AddForce(body, up * m * JetpackAccel, ForceKind.Walk);
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
