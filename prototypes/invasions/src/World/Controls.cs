using Godot;

namespace Invasions.World;

/// <summary>
/// Input actions, registered in code so the project file stays minimal. Using actions (not raw key
/// checks) also lets the tests press keys with Input.ActionPress, which works headless.
/// </summary>
public static class Controls
{
    public const string Forward = "move_forward", Back = "move_back", Left = "move_left", Right = "move_right";
    public const string Jump = "jump", Sprint = "sprint", Crouch = "crouch";
    public const string Fire = "fire", Interact = "interact", ManTurret = "man_turret";
    public const string DebugInvade = "debug_invade", DebugFastTime = "debug_fast_time";
    public const string DebugPlaceCore = "debug_place_core", DebugKnockOutSource = "debug_knock_out_source";
    public const string DebugNextFaction = "debug_next_faction", DebugWrong = "debug_wrong", DebugFavour = "debug_favour";
    public const string FreeMouse = "free_mouse";

    public static void Register()
    {
        Bind(Forward, Godot.Key.W);
        Bind(Back, Godot.Key.S);
        Bind(Left, Godot.Key.A);
        Bind(Right, Godot.Key.D);
        Bind(Jump, Godot.Key.Space);
        Bind(Sprint, Godot.Key.Shift);
        Bind(Crouch, Godot.Key.Ctrl);
        Bind(Interact, Godot.Key.E);
        Bind(ManTurret, Godot.Key.F);
        Bind(DebugInvade, Godot.Key.I);
        Bind(DebugFastTime, Godot.Key.T);
        Bind(DebugPlaceCore, Godot.Key.P);
        Bind(DebugKnockOutSource, Godot.Key.K);
        Bind(DebugNextFaction, Godot.Key.J);
        Bind(DebugWrong, Godot.Key.Minus);
        Bind(DebugFavour, Godot.Key.Equal);
        Bind(FreeMouse, Godot.Key.Escape);
        if (!InputMap.HasAction(Fire))
        {
            InputMap.AddAction(Fire);
            InputMap.ActionAddEvent(Fire, new InputEventMouseButton { ButtonIndex = MouseButton.Left });
        }
    }

    static void Bind(string action, Key key)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
