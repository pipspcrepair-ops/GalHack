using System;
using GalHack.Physics.Engine;
using Godot;
using Slice.Places;
using Wildlife;

namespace Slice.Game;

/// <summary>
/// The player's gesture wheel: hold its key (Z, rebindable on the escape menu's Controls page),
/// move the mouse toward a gesture, let go to make it. The view holds still while the wheel is
/// up. Every town near you reads it by the same rules its own people use (<see cref="Gestures"/>).
/// </summary>
public partial class Game
{
    /// <summary>The wheel is up; where the mouse has pushed its pointer (unit circle); the gesture under it, or -1.</summary>
    public bool WheelOpen { get; private set; }
    public Vector2 WheelAim { get; private set; }
    public int WheelPick { get; private set; } = -1;
    /// <summary>The last gesture made, and when (clock seconds): the visor shows it a moment.</summary>
    public PlayerGesture? LastGesture { get; private set; }
    public double LastGestureAt { get; private set; } = -9;
    public event Action<PlayerGesture> Gestured;

    /// <summary>The wheel's gestures, clockwise from the top.</summary>
    public static readonly PlayerGesture[] Wheel = (PlayerGesture[])Enum.GetValues(typeof(PlayerGesture));

    void StepGestures(Controls c)
    {
        StepRobbing(c);
        bool held = c.IsHeld(Key.Z) && !Suit.Dead;
        if (held)
        {
            if (!WheelOpen) { WheelOpen = true; WheelAim = Vector2.Zero; }
            Vector2 aim = WheelAim + c.Look * 0.012f;
            WheelAim = aim.Length() > 1f ? aim.Normalized() : aim;
            c.Look = Vector2.Zero; // the view holds still
            WheelPick = WheelAim.Length() > 0.35f ? Sector(WheelAim) : -1;
            return;
        }
        if (!WheelOpen) return;
        WheelOpen = false;
        if (WheelPick >= 0) MakeGesture(Wheel[WheelPick]);
        WheelPick = -1;
    }

    /// <summary>The settlement you stand in, if any.</summary>
    public Town TownHere => Survivor == null ? null : System.Linq.Enumerable.FirstOrDefault(_towns, t => t.Covers(Survivor.WorldFeet));

    /// <summary>Robbed someone (tests).</summary>
    public int Robbed { get; private set; }

    /// <summary>E on someone holding their hands up to you, close by: you take what they carry. Possible, and costly (<see cref="Town"/>).</summary>
    void StepRobbing(Controls c)
    {
        if (!c.WasPressed(Key.E) || TownHere is not Town t || t.Robbable(Survivor.WorldFeet) is not Town.Person p) return;
        c.Pressed.Remove(Key.E);
        t.Rob(p);
        Robbed++;
        var kit = Zone.Data.Rules.Materials.StartKit;
        if (kit != null && kit.Count > 0) { var (id, n) = System.Linq.Enumerable.ElementAt(kit, (int)(p.Spawn.Cell.X & 0x7fff) % kit.Count); Pack.Add(id, Math.Max(1, n / 2)); }
    }

    /// <summary>Which sector of the wheel a pointer lies in, clockwise from the top.</summary>
    public static int Sector(Vector2 aim)
    {
        float a = Mathf.PosMod(Mathf.Atan2(aim.X, -aim.Y) + Mathf.Pi / Wheel.Length, Mathf.Tau);
        return Math.Clamp((int)(a / Mathf.Tau * Wheel.Length), 0, Wheel.Length - 1);
    }

    /// <summary>Makes a gesture: your arms make it (seen in third person), and everyone near who can see you reads it.</summary>
    public void MakeGesture(PlayerGesture g)
    {
        LastGesture = g;
        LastGestureAt = Time.GetTicksMsec() / 1000.0;
        SinceGesture = 0f;
        Vector3 eye = Survivor.WorldEye(), look = Survivor.WorldLook();
        foreach (Town t in _towns) t.Heard(g, eye, look);
        Zone.Wildlife?.Heard(g, eye, look);
        Gestured?.Invoke(g);
    }

    /// <summary>The sleeper's figure as seen from outside (third person, screenshots) and, seated, from its own eyes: its arms make your gestures.</summary>
    public Node3D Figure { get; private set; }

    /// <summary>Seconds of play since the last gesture was made (the figure's clock: it stops with the world).</summary>
    public float SinceGesture { get; private set; } = 99f;

    /// <summary>
    /// The figure where the sleeper is and doing what it does: on foot it faces the view's heading
    /// in the frame it stands in (the planet's up, or a vessel's aboard) and walks, runs, jumps,
    /// flies, swims and climbs by the walker's own state; on the hover bike it sits astride, facing
    /// and turning with the bike; in a chair it sits. The last gesture plays on its arms.
    /// </summary>
    void PoseFigure(double delta)
    {
        SinceGesture += (float)delta;
        if (Figure == null || Survivor == null) return;
        Slice.Player.Survivor s = Survivor;
        var legs = s.Legs;
        System.Numerics.Vector3 v = s.Body.Velocity, up = System.Numerics.Vector3.Normalize(s.Body.Up);
        float climb = System.Numerics.Vector3.Dot(v, up);
        var m = new Slice.Player.SleeperMotion
        {
            Speed = (v - up * climb).Length(),
            Climb = climb,
            Grounded = s.Body.OnFloor,
            Crouched = s.Crouched,
            Jetting = legs.JetpackFiring || Spaceflight?.EvaThrusting == true,
            Swimming = legs.Swimming,
            Floating = legs.Floating,
            Ladder = legs.Climb != GalHack.Physics.ClimbMode.None,
            Riding = s.OnBike != null,
            Seated = s.Seated != null,
            SeatDrop = s.Seated?.SeatDrop ?? 0f,
            Pitch = s.Pitch,
        };
        Transform3D place;
        if (s.OnBike is Slice.Vehicles.HoverBike bike)
            place = bike.Node.GetGlobalTransformInterpolated() * new Transform3D(Basis.Identity, new Vector3(0f, 0.07f, 0.35f));
        else if (s.Seated is Slice.Vessels.Vessel seat && seat.Seat is System.Numerics.Vector3 chair)
            place = seat.DrawTransform * new Transform3D(seat.ChairBasis, chair.ToGodot() - seat.ChairBasis.Y * seat.SeatDrop); // on the floor under the seat: the figure puts its hips on the seat
        else
        {
            // The view's own frame with its pitch taken out: the eye's heading, feet under it.
            Transform3D eye = s.Eye();
            Basis turn = (eye.Basis * new Basis(Vector3.Right, -s.Pitch)).Orthonormalized();
            place = new Transform3D(turn, eye.Origin - turn.Y * s.EyeAbove);
            // Swimming, it lies out along the way it faces, the head clear of the water.
            if (m.Swimming) place *= new Transform3D(Basis.Identity, Vector3.Up * 1.1f) * new Transform3D(new Basis(Vector3.Right, -1.2f), Vector3.Zero) * new Transform3D(Basis.Identity, Vector3.Down * 1.1f);
        }
        Figure.GlobalTransform = place;
        // Crouched, it squats with the eye.
        float squat = Mathf.Clamp((s.EyeAbove - Slice.Player.Survivor.CrouchEyeHeight) / (Slice.Player.Survivor.EyeHeight - Slice.Player.Survivor.CrouchEyeHeight), 0f, 1f);
        Figure.Scale = new Vector3(1f, Mathf.Lerp(Slice.Player.Survivor.CrouchHeight / Slice.Player.Survivor.Height, 1f, squat), 1f);
        Slice.Player.CharacterModel.Drive(Figure, m, LastGesture, SinceGesture, (float)delta);
        Slice.Player.CharacterModel.SetHelmet(Figure, !Suit.Sim.Player.Gear.HelmetOn, (float)delta);
    }
}
