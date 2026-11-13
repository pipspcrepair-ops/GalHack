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
    /// Crouched (the game sets it while the body's capsule is shortened): a slow, careful walk, no
    /// sprinting, and a low hop for a jump. Swimming, climbing and floating are unchanged.
    /// </summary>
    public bool Crouch;

    /// <summary>
    /// Zero-g (no gravity where the body is): steer with suit thrusters instead of walking. The
    /// thrusters fire only when asked, so a body coasts on its momentum.
    /// </summary>
    public bool Floating;

    /// <summary>Up/down while floating, -1..1.</summary>
    public float Vertical;

    /// <summary>Look pitch, radians, for steering while floating.</summary>
    public float Pitch;

    /// <summary>
    /// Held by something other than the feet: a ladder (forward climbs, back goes down, as does
    /// <see cref="Vertical"/>) or a lift shaft (only <see cref="Vertical"/> rides it, the walk
    /// carries on across). No falling either way: the climb holds against gravity and wind as far as
    /// grip allows. The game sets it every tick from what is at the body's feet.
    /// </summary>
    public ClimbMode Climb;

    /// <summary>
    /// The ladder being climbed, as the game sees it, set each tick with <see cref="Climb"/>. Left
    /// unknown (default), forward is up and the walk carries on across. Known, it is climbed as a
    /// person climbs one: forward or up climbs, back or down goes down, and on it the climber keeps
    /// to its line (a free-standing one is never walked through); over its top any way steps off;
    /// at its foot, back, aside, or away from its wall walks off it.
    /// </summary>
    public LadderHold Ladder;

    public float ClimbSpeed = 2.6f, ShaftSpeed = 3.5f;

    public float CrouchSpeed = 2.4f, CrouchJumpShare = 0.7f;
    public float WalkSpeed = 5.5f, SprintSpeed = 9f, GroundAccel = 45f, AirAccel = 10f, JumpSpeed = 4.8f;
    public float JetpackAccel = 18f, JetpackFuelMax = 3f, ZeroGSpeed = 5f, ZeroGAccel = 7f;

    /// <summary>How hard the legs can push, m/s² of the body's mass: walking, and holding against outside forces.</summary>
    public float Grip = 60f;

    /// <summary>
    /// How hard an outside push along the ground the feet can stand against before they lose their
    /// footing, m/s² of the body's mass (about the friction of boots on rough ground). Enough to stand
    /// in a gale; hurricane-force wind on a body standing tall slides it (crouching halves the push),
    /// and a tornado's pull drags you. Past it the feet slip: they hold back only a share of it.
    /// </summary>
    public float Footing = 5f, SlipHold = 0.5f;

    /// <summary>The feet lost their footing this tick: an outside push was too strong to stand against.</summary>
    public bool Slipping { get; private set; }

    /// <summary>
    /// The jetpack also pushes along the way the body is steered in the air, this share of its thrust:
    /// enough to fight clear of the edge of a weak tornado, never out of a strong core.
    /// </summary>
    public float JetpackSide = 0.6f;

    public float JetpackFuel { get; internal set; } = 3f;

    /// <summary>
    /// A pack that has run dry stays off until it has taken back this much (seconds of thrust) on the
    /// ground: it cannot stutter on and off at empty, one tick of fuel at a time.
    /// </summary>
    public float JetpackMinRefill = 0.5f;

    /// <summary>The pack ran dry and has not yet taken back <see cref="JetpackMinRefill"/>.</summary>
    public bool JetpackSpent { get; private set; }

    /// <summary>The pack fired this tick.</summary>
    public bool JetpackFiring { get; private set; }

    /// <summary>The legs pushed off this tick (a deliberate jump: it lets go of a hull at once).</summary>
    public bool Jumped { get; private set; }

    /// <summary>
    /// <see cref="Jump"/> has been held since the last jump: a held key jumps once (and then fires the jetpack in the air), so
    /// landing with it still held stands, not hops. It must be let go to jump again.
    /// </summary>
    bool _jumpHeld;

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
        bool fired = JetpackFiring;
        JetpackFiring = false;
        Slipping = false;
        if (!Jump) _jumpHeld = false;
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

        Vector2 cm = Move.LengthSquared() > 1f ? Vector2.Normalize(Move) : Move;
        LadderHold hold = Ladder;
        bool known = Climb == ClimbMode.Ladder && hold.Known;
        Vector3 outward = known ? Vector3.Normalize(hold.Out - up * Vector3.Dot(hold.Out, up)) : Vector3.Zero;
        Vector3 wishAcross = MathX.Rotate(heading, new Vector3(cm.X, 0f, cm.Y));
        // At a ladder's foot (the feet down at the floor it stands on): back, aside, nothing, or forward
        // away from its wall is the ordinary walk. That is how you step off it at the bottom.
        bool offAtFoot = known && !hold.Below && Vertical <= 0f
                         && (cm.Y >= 0f || hold.Backed && Vector3.Dot(wishAcross, outward) > 0.5f);
        if (Climb != ClimbMode.None && !offAtFoot)
        {
            // On a ladder forward or up climbs (walking into it climbs it, and over its top), back or
            // down goes down; in a shaft only up and down ride it. Across, a climber moves at a climb on
            // a ladder and at a walk in a shaft, so stepping off at the top or out of a shaft door is the
            // ordinary walk. On a known ladder below its top, and on the way down from it, the climber
            // keeps hold: drawn to its line, moving only along its face.
            bool ladder = Climb == ClimbMode.Ladder;
            float rise = ladder ? Math.Clamp(-cm.Y + Vertical, -1f, 1f) * ClimbSpeed : Math.Clamp(Vertical, -1f, 1f) * ShaftSpeed;
            Vector3 across = wishAcross * (ladder ? ClimbSpeed : Sprint ? SprintSpeed : WalkSpeed);
            if (known && !(hold.Top && rise > 0f))
            {
                Vector3 toLine = hold.Centre - body.Position;
                toLine -= up * Vector3.Dot(toLine, up);
                Vector3 side = Vector3.Normalize(Vector3.Cross(up, outward));
                float sideways = Vector3.Dot(across, side);
                // Off its line along its face only when asked to; to and from its wall, always back to it.
                Vector3 pull = outward * Vector3.Dot(toLine, outward) + (cm.X == 0f ? side * Vector3.Dot(toLine, side) : Vector3.Zero);
                across = side * sideways + MathX.LimitLength(pull * 4f, ClimbSpeed);
            }
            Vector3 climbed = Toward(v, across + up * rise, GroundAccel * dt);
            world.AddForce(body, MathX.LimitLength(m * (climbed - v) / dt - body.PendingTotalForce, m * Grip), ForceKind.Walk);
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
        // Refilled on the ground, Space held or not (a held Space neither jumps again nor fires on the ground), but not
        // straight after the pack fired: a body that touches down and lifts off every tick (wedged under a ceiling, on an
        // edge) must not earn back the fuel it burns.
        if (onFloor && !fired)
        {
            JetpackFuel = MathX.MoveToward(JetpackFuel, JetpackFuelMax, dt);
            if (JetpackSpent && JetpackFuel >= MathF.Min(JetpackMinRefill, JetpackFuelMax)) JetpackSpent = false;
        }
        float speed = Crouch ? CrouchSpeed : Sprint ? SprintSpeed : WalkSpeed;
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
            // Pushed harder than the feet can stand: they slip, holding back only part of it.
            Slipping = outside.Length() > m * Footing;
            world.AddForce(body, MathX.LimitLength(legs, Slipping ? m * Footing * SlipHold : m * Grip), ForceKind.Walk);
        }
        else if (move != Vector2.Zero)
        {
            // In the air, steer without bleeding off momentum.
            Vector3 steered = h + Vector3.Normalize(wishDir) * AirAccel * dt;
            float cap = MathF.Max(h.Length(), speed);
            world.AddForce(body, m * (MathX.LimitLength(steered, cap) - h) / dt, ForceKind.Walk);
        }

        if (Jump && onFloor && !_jumpHeld)
        {
            world.AddImpulse(body, up * m * MathF.Max(0f, JumpSpeed * (Crouch ? CrouchJumpShare : 1f) - Vector3.Dot(v, up)), ForceKind.Walk);
            Jumped = _jumpHeld = true;
        }
        else if (Jetpack && !onFloor && !JetpackSpent && JetpackFuel > 0f)
        {
            // Up (until climbing fast), and along the way it is steered: whatever else has hold of the
            // body (a tornado's pull, a fall), the pack fires as a force on the one path, so it brakes a
            // fall and can fight a pull. Nothing locks it out.
            Vector3 thrust = Vector3.Dot(v, up) < 8f ? up * JetpackAccel : Vector3.Zero;
            if (move != Vector2.Zero) thrust += Vector3.Normalize(wishDir) * JetpackAccel * JetpackSide;
            if (thrust != Vector3.Zero)
            {
                world.AddForce(body, thrust * m, ForceKind.Walk);
                JetpackFiring = true;
                JetpackFuel = MathF.Max(0f, JetpackFuel - dt);
                if (JetpackFuel <= 0f) JetpackSpent = true;
            }
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

/// <summary>What holds a walking body up besides its feet (see <see cref="WalkController.Climb"/>).</summary>
public enum ClimbMode { None, Ladder, Shaft }

/// <summary>A ladder at a climber's feet (see <see cref="WalkController.Ladder"/>), in the climber's world.</summary>
public struct LadderHold
{
    /// <summary>Which way it looks out from what it is fixed to (level, unit); zero when unknown.</summary>
    public Vector3 Out;
    /// <summary>A point on its line, the middle of its cells.</summary>
    public Vector3 Centre;
    /// <summary>A wall behind it (not one standing free), so walking away from it at its foot is walking off it.</summary>
    public bool Backed;
    /// <summary>It goes on below the feet: false at its foot, once the feet are down within a hand of the floor it stands on.</summary>
    public bool Below;
    /// <summary>The feet are over its top (only the cell under them is ladder): from here any way steps off.</summary>
    public bool Top;

    public readonly bool Known => Out.LengthSquared() > 0.25f;
}
