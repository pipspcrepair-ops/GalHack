using System;
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
        Gestured?.Invoke(g);
    }

    /// <summary>The castaway's figure as seen from outside (third person, screenshots): its arms make your gestures.</summary>
    public Node3D Figure { get; private set; }

    /// <summary>Seconds of play since the last gesture was made (the figure's clock: it stops with the world).</summary>
    public float SinceGesture { get; private set; } = 99f;

    /// <summary>The figure's arms in the last gesture, while it plays; at rest after.</summary>
    void PoseFigure(double delta)
    {
        SinceGesture += (float)delta;
        if (Figure != null) Slice.Player.CharacterModel.Pose(Figure, LastGesture, SinceGesture);
    }
}
