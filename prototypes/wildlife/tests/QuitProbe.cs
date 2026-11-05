using Godot;

namespace Wildlife;

/// <summary>
/// The playable scene's way out. Plays the real scene for a few seconds with sound going (the
/// hovercraft's looping hum, its blades grinding, the wildlife about), then closes the window
/// the way a player would. The game must quit through <see cref="Shutdown"/> and exit 0.
///   godot --headless --path prototypes/wildlife res://tests/QuitProbe.tscn
/// </summary>
public partial class QuitProbe : Node
{
    Main _main;
    int _frame;

    public override void _Ready()
    {
        _main = new Main { Name = "Main" };
        AddChild(_main);
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;
        if (_frame == 20)
        {
            // Board the hovercraft and drive into whatever is ahead, so the loops are playing.
            _main.Player.Teleport(_main.Vessel.GlobalPosition + new Vector3(-3f, 0f, 0f));
        }
        else if (_frame == 25)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F, Pressed = false });
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.W, Pressed = true });
        }
        else if (_frame == 200)
        {
            if (!_main.Player.Driving)
            {
                GD.PrintErr("FAIL  could not board the hovercraft before closing");
                Shutdown.Quit(this, 1);
                return;
            }
            GD.Print("ALL PASSED (closing the window while driving, sound playing)");
            // Exactly what the window manager sends when the player closes the game.
            GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
        }
        else if (_frame > 60 * 30)
        {
            GD.PrintErr("FAIL  closing the window never quit the game");
            GetTree().Quit(1);
        }
    }
}
