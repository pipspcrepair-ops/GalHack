using System;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Player;

/// <summary>
/// The castaway's body: a registered body like any other (100 kg with suit and pack), walked by
/// the shared <see cref="WalkController"/> (legs, jump, the suit's jetpack) and moved by a
/// character controller. It is the physics sandbox's player, on a planet: landing on (or, in
/// the air, touching) a hull puts it in that structure's cabin world; it lets go by the shared
/// rules (a jump, or out of contact), sits in a command chair to fly, and takes every impact in
/// full through the survival suit (<see cref="Suit"/>).
/// </summary>
public sealed class Survivor
{
    public const float Mass = 100f, Radius = 0.35f, Height = 1.8f, EyeHeight = 1.6f;

    public readonly SurfaceZone Zone;
    public readonly Body Body;
    public readonly CharacterBinding Binding;
    public readonly WalkController Legs = new();

    public CharacterBody3D Node => Binding.Character;

    /// <summary>View heading and pitch, radians, within the body's walk frame.</summary>
    public float Yaw, Pitch;

    /// <summary>The walk frame the camera uses: it eases after the body's, so hull edges roll the view smoothly.</summary>
    public N.Quaternion ViewFrame = N.Quaternion.Identity;

    /// <summary>The vessel whose seat the survivor sits in, or null.</summary>
    public Vessel Seated { get; private set; }

    /// <summary>The structure whose cabin world the survivor is in (inside or on its hull), or null.</summary>
    public Vessel Aboard => Body.Frame == null ? null : Zone.VesselOf(Body.Frame);

    public bool OnOuterHull => Body.Frame != null && Body.OnOuterHull;

    /// <summary>Joined or let go of a structure, or passed through its doorway.</summary>
    public event Action<string> Moved;

    /// <summary>
    /// How fast the body would hit the ground, m/s: its speed while it is airborne and coming down
    /// (falling, or flung downward); nothing on its feet, in a seat, or while it rises or hangs
    /// level (lifting off on the jetpack is never a fall). The visor flashes IMPACT once this passes
    /// the safe landing speed, so the warning always comes before a hard landing can hurt.
    /// </summary>
    public float ImpactSpeed => Body.OnFloor || Seated != null || -N.Vector3.Dot(Body.Velocity, Body.Up) < 0.5f ? 0f : Body.Velocity.Length();

    /// <summary>Times the survivor walked off a structure straight onto the ground (tests).</summary>
    public int StepsOff { get; private set; }

    Body _letGoOf;
    float _rejoinBlocked;
    bool _wasOutside;

    Survivor(SurfaceZone zone, Body body, CharacterBinding binding)
    {
        Zone = zone;
        Body = body;
        Binding = binding;
        body.Controller = Legs;
    }

    public static Survivor Spawn(SurfaceZone zone, N.Vector3 feet)
    {
        Body body = zone.Physics.Register(new BodySpec
        {
            Kind = BodyKind.Player, Label = "Survivor", Mass = Mass, DragArea = 0.9f,
            Shape = new StuckShape(Height, Radius), Position = feet,
        });
        var c = new CharacterBody3D { Name = "Survivor", CollisionLayer = Layers.Player, CollisionMask = Layers.PlayerMask };
        c.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = Radius, Height = Height }, Position = Vector3.Up * (Height / 2f) });
        zone.AddChild(c);
        var survivor = new Survivor(zone, body, zone.Host.Bind(body, new CharacterBinding(c)));
        // Sprinting tops out just under the safe impact speed, so running into a rock never hurts;
        // only airborne speed (falls, flings, the jetpack) can, and the visor warns of that.
        survivor.Legs.SprintSpeed = MathF.Min(survivor.Legs.SprintSpeed, zone.Physics.Rules.Contacts.SafeSpeed - 0.1f);
        survivor.Binding.Teleport();
        zone.Weather.Bridge.Track(body);
        return survivor;
    }

    // ---- Every tick, after physics ----------------------------------------------------------

    public void AfterPhysics(float dt)
    {
        PhysicsRules.RidingData rules = Zone.Physics.Rules.Riding;
        _rejoinBlocked -= dt;
        if (Body.OnFloor) Zone.Weather.Bridge.Landed(Body); // a fling's protection ends on touching down

        if (Seated != null)
        {
            Body.Position = Seated.Seat.Value + N.Vector3.UnitY * 0.05f;
            Body.Velocity = N.Vector3.Zero;
            Binding.Teleport();
            return;
        }

        if (Body.Frame == null)
        {
            if (Node.GetParent() != Zone) MoveNodeTo(Zone, inCabin: false); // a rescue took it out of a cabin
            // A base never moves, so there is nothing to ride: it is walked on in the zone's world.
            if (Binding.TouchedHull is Body hull && hull.Motion != Motion.Fixed && (Binding.TouchedHullUnderfoot || !Body.OnFloor)
                && !(hull == _letGoOf && _rejoinBlocked > 0f) && Zone.VesselOf(hull) is Vessel vessel)
                Join(vessel, Binding.TouchedHullNormal);
        }
        else
        {
            Vessel vessel = Aboard;
            Body.OnOuterHull = !vessel.Interior.Contains(Body.Position + Body.Up * 0.9f);
            float distance = vessel.DistanceFromHull(Body.Position, rules.LetGoDistance + 0.5f);
            // Walked out of the door and off its floor, over the ground: back in the zone's world at
            // once (shared riding's step-off).
            // No block right under the middle of its feet (the character controller still calls the
            // floor's edge "floor" while it slides off it), so it isn't standing on the hull.
            bool hullUnder = vessel.DistanceFromHull(Body.Position - Body.Up * 0.1f, 0.3f) < 0.02f;
            N.Vector3 ground = default;
            bool steppedOff = !hullUnder && Riding.StepsOntoGround(Body, hullUnder, Zone.Physics.Query, Zone.Physics.Rules.Walking.StepHeight * 2f, out ground);
            if (steppedOff || Riding.LetGo(rules, Body, Legs.Jumped, Binding.Touching, distance, dt))
            {
                _letGoOf = vessel.Body;
                _rejoinBlocked = rules.RejoinAfter;
                Leave();
                if (steppedOff)
                {
                    // Onto the ground it stepped over, never into it.
                    if (Body.Position.Y < ground.Y) Body.Position = Body.Position with { Y = ground.Y + 0.02f };
                    Binding.Teleport();
                    StepsOff++;
                }
            }
            else if (Body.OnOuterHull != _wasOutside)
                Moved?.Invoke(Body.OnOuterHull ? $"out on the {vessel.Name}'s hull" : $"inside the {vessel.Name}");
        }
        _wasOutside = Body.Frame != null && Body.OnOuterHull;

        // Mag boots hold you to the face underfoot out on a hull; anywhere without gravity you float.
        Binding.MagBoots = _wasOutside && rules.MagBoots;
        Binding.Floating = !Binding.MagBoots && Zone.Physics.GravityAt(Body).LengthSquared() < 1e-6f;
        Legs.Floating = Binding.Floating;
        Legs.Yaw = Yaw;
        Legs.Pitch = Pitch;
        ViewFrame = N.Quaternion.Normalize(N.Quaternion.Slerp(ViewFrame, Body.Rotation, 1f - MathF.Exp(-rules.ViewEase * dt)));
    }

    // ---- Changing frames ----------------------------------------------------------------------

    /// <summary>Into a structure's cabin world: world position, velocity and orientation are kept.</summary>
    public void Join(Vessel vessel, N.Vector3? worldNormal = null)
    {
        N.Quaternion view = WorldView();
        Riding.EnterCabin(Body, vessel.Body);
        ViewFrame = N.Quaternion.Normalize(N.Quaternion.Conjugate(vessel.Body.Rotation) * view);
        MoveNodeTo(vessel.Cabin, inCabin: true);
        Body.OnOuterHull = !vessel.Interior.Contains(Body.Position + Body.Up * 0.9f);
        if (Body.OnOuterHull && Zone.Physics.Rules.Riding.MagBoots && worldNormal is N.Vector3 n)
            Riding.SetUp(Body, Riding.DirectionToLocal(vessel.Body, n));
        _wasOutside = Body.OnOuterHull;
        Binding.MagBoots = _wasOutside && Zone.Physics.Rules.Riding.MagBoots;
        Binding.Teleport();
        Moved?.Invoke(Body.OnOuterHull ? $"on the {vessel.Name}'s hull" : $"aboard the {vessel.Name}");
    }

    /// <summary>Lets go of the structure: back in the zone's world with its velocity there plus your own.</summary>
    public void Leave()
    {
        Vessel vessel = Aboard;
        N.Quaternion view = WorldView();
        Riding.LeaveCabin(Body);
        ViewFrame = view;
        MoveNodeTo(Zone, inCabin: false);
        _wasOutside = false;
        Binding.MagBoots = false;
        Binding.Teleport();
        Moved?.Invoke($"off the {vessel?.Name}");
    }

    void MoveNodeTo(Node parent, bool inCabin)
    {
        if (Node.GetParent() != parent) Node.Reparent(parent, false);
        Node.CollisionLayer = inCabin ? Layers.Interior : Layers.Player;
        Node.CollisionMask = inCabin ? Layers.CabinMask : Layers.PlayerMask;
    }

    N.Quaternion WorldView() => Body.Frame == null ? ViewFrame : N.Quaternion.Normalize(Body.Frame.Rotation * ViewFrame);

    /// <summary>The frame the view's yaw and pitch turn in, in the world (the eased walk frame, and the ship's turn aboard).</summary>
    public N.Quaternion WorldViewFrame => Seated is Vessel seat ? seat.Body.Rotation : WorldView();

    /// <summary>Sits in a command chair: the body stops simulating and the controls fly the vessel.</summary>
    public bool Sit(Vessel vessel)
    {
        if (vessel.Seat == null || vessel.IsWreck) return false;
        if (Aboard != vessel) PlaceAboard(vessel, vessel.Seat.Value);
        Seated = vessel;
        Body.Asleep = true;
        Node.CollisionLayer = 0;
        Node.CollisionMask = 0;
        Yaw = 0f;
        Pitch = -0.08f;
        if (vessel.Flight != null) vessel.Flight.Input = ShipHandling.Handling.PilotInput.LetGo;
        return true;
    }

    /// <summary>Gets up from the seat, a step behind it.</summary>
    public void StandUp()
    {
        Vessel vessel = Seated;
        if (vessel == null) return;
        Seated = null;
        if (vessel.Flight != null) vessel.Flight.Input = ShipHandling.Handling.PilotInput.Autopilot; // nobody at the controls: it holds course
        Body.Asleep = false;
        MoveNodeTo(vessel.Cabin, inCabin: true);
        Body.Position = vessel.Seat.Value + StandOffset(vessel);
        Body.Velocity = N.Vector3.Zero;
        Binding.Teleport();
    }

    /// <summary>Where getting up from a seat puts you: behind it, or beside it if that's blocked.</summary>
    static N.Vector3 StandOffset(Vessel vessel)
    {
        N.Vector3 seat = vessel.Seat.Value;
        float cell = vessel.Structure.Grid.CellSize;
        foreach (N.Vector3 o in new[] { new N.Vector3(0, 0.05f, 2 * cell), new N.Vector3(2 * cell, 0.05f, 0), new N.Vector3(-2 * cell, 0.05f, 0), new N.Vector3(0, 0.05f, -2 * cell) })
            if (vessel.DistanceFromHull(seat + o + N.Vector3.UnitY * 0.9f, 0.5f) >= 0.5f) return o;
        return new N.Vector3(0f, 0.05f, 2 * cell);
    }

    /// <summary>Puts the survivor in a structure's cabin world at a structure-local spot, at rest relative to it.</summary>
    public void PlaceAboard(Vessel vessel, N.Vector3 local)
    {
        if (Seated != null) StandUp();
        if (Aboard != vessel)
        {
            if (Body.Frame != null) Leave();
            Body.Position = vessel.Body.ToParent(local);
            Body.Velocity = vessel.Body.PointVelocity(Body.Position);
            Join(vessel);
        }
        Body.Position = local;
        Body.Velocity = N.Vector3.Zero;
        Body.Rotation = N.Quaternion.Identity;
        Body.Up = N.Vector3.UnitY;
        ViewFrame = Body.Rotation;
        Body.OnOuterHull = !vessel.Interior.Contains(local + Body.Up * 0.9f);
        _wasOutside = Body.OnOuterHull;
        Binding.MagBoots = _wasOutside && Zone.Physics.Rules.Riding.MagBoots;
        Binding.Teleport();
    }

    /// <summary>Puts the survivor in the zone's world, upright (respawns, tests, screenshots).</summary>
    public void PlaceOutside(N.Vector3 feet, N.Vector3 velocity = default)
    {
        if (Seated != null) StandUp();
        if (Body.Frame != null) Leave();
        Body.Position = feet;
        Body.Velocity = velocity;
        Body.Rotation = N.Quaternion.Identity;
        Body.Up = N.Vector3.UnitY;
        ViewFrame = N.Quaternion.Identity;
        Body.OnFloor = false;
        _letGoOf = null;
        Binding.Teleport();
    }

    // ---- View ---------------------------------------------------------------------------------

    /// <summary>The eye, from interpolated transforms, standing on the eased view frame.</summary>
    public Transform3D Eye()
    {
        var look = new Basis(Vector3.Up, Yaw) * new Basis(Vector3.Right, Pitch);
        if (Seated is Vessel seat)
            return seat.DrawTransform * new Transform3D(look, (seat.Seat.Value + N.Vector3.UnitY * 0.95f).ToGodot());
        Vector3 feet = Node.GetGlobalTransformInterpolated().Origin;
        Basis frame = ViewFrame.ToBasis();
        var local = new Transform3D(frame * look, feet + frame.Y * EyeHeight);
        Vessel aboard = Aboard;
        return aboard == null ? local : aboard.DrawTransform * local;
    }

    /// <summary>Feet in the zone's world.</summary>
    public Vector3 WorldFeet => Body.WorldPosition.ToGodot();

    /// <summary>The look direction in the world (not interpolated: for aiming).</summary>
    public Vector3 WorldLook()
    {
        N.Quaternion look = MathX.ThenLocal(MathX.ThenLocal(WorldView(), MathX.Yaw(Yaw)), MathX.AxisAngle(N.Vector3.UnitX, Pitch));
        if (Seated is Vessel seat) look = MathX.ThenLocal(MathX.ThenLocal(seat.Body.Rotation, MathX.Yaw(Yaw)), MathX.AxisAngle(N.Vector3.UnitX, Pitch));
        return MathX.Forward(look).ToGodot();
    }

    /// <summary>The body's up in the world.</summary>
    public Vector3 WorldUp()
    {
        if (Seated is Vessel seat) return MathX.Rotate(seat.Body.Rotation, N.Vector3.UnitY).ToGodot();
        return (Body.Frame == null ? Body.Up : MathX.Rotate(Body.Frame.Rotation, Body.Up)).ToGodot();
    }

    /// <summary>The eye in the world (not interpolated: for aiming).</summary>
    public Vector3 WorldEye()
    {
        if (Seated is Vessel seat) return seat.Body.ToParent(seat.Seat.Value + N.Vector3.UnitY * 0.95f).ToGodot();
        N.Vector3 up = Body.Frame == null ? Body.Up : MathX.Rotate(Body.Frame.Rotation, Body.Up);
        return (Body.WorldPosition + up * EyeHeight).ToGodot();
    }
}
