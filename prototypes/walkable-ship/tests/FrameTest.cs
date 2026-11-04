using Godot;

namespace WalkableShip;

/// <summary>
/// Headless test of the ship frame. Run with tools/test.ps1, or:
///   godot --headless --path prototypes/walkable-ship res://tests/FrameTest.tscn
/// Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class FrameTest : Node
{
    const int StuntTicks = 900;   // 15 s at 60 Hz
    const int ZeroGTicks = 600;   // 10 s

    Main _main;
    int _tick, _stage, _failures;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100; // check after the ship and player have moved
        _main = new Main();
        AddChild(_main);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        Ship ship = _main.Ship;
        Player player = _main.Player;

        switch (_stage)
        {
            case 0: // stand on the deck while the ship flies hard stunts
                if (_tick == 60)
                {
                    ship.Position = new Vector3(0, 150, 0);
                    ship.StuntIntensity = 2.5f;
                    ship.StuntAutopilot = true;
                }
                else if (_tick > 60 && !InCabin(player))
                {
                    Fail($"left the cabin during stunts at {player.ShipLocalPosition}");
                    Next();
                }
                else if (_tick == 60 + StuntTicks)
                {
                    Check(player.CurrentMode == Player.Mode.Aboard, "still aboard after 15 s of barrel rolls");
                    Check(player.IsOnFloor() && player.ShipLocalPosition.Y < 0.2f,
                        $"standing on the deck the whole time (height {player.ShipLocalPosition.Y:F2} m)");
                    ship.ToggleGravity();
                    Next();
                }
                break;

            case 1: // core gravity off, still stunting: float, but never through a wall
                if (!InCabin(player))
                {
                    Fail($"floated out of the cabin at {player.ShipLocalPosition}");
                    Next();
                }
                else if (_tick == ZeroGTicks)
                {
                    Pass("stayed inside for 10 s of zero-g stunts");
                    ship.ToggleGravity();
                    ship.StuntAutopilot = false;
                    ship.LocalAngularVelocity = Vector3.Zero;
                    ship.Velocity = ship.GlobalBasis * new Vector3(0, 0, -40); // 40 m/s forward
                    player.DebugPlaceAboard(new Vector3(0, 0.05f, 13.2f), Vector3.Zero); // just past the door
                    Next();
                }
                break;

            case 2: // stepping off the back keeps the ship's velocity
                if (player.CurrentMode == Player.Mode.Outside)
                {
                    float error = (player.Velocity - ship.Velocity).Length();
                    Check(error < 1f, $"kept the ship's 40 m/s on stepping off (error {error:F2} m/s)");
                    ship.Velocity = Vector3.Zero;
                    player.DebugPlaceOutside(ship.GlobalTransform * new Vector3(0, 0.05f, 10f));
                    Next();
                }
                else if (_tick > 30)
                {
                    Fail("never stepped off through the door");
                    Next();
                }
                break;

            case 3: // walking into the cabin from outside boards the ship
                if (player.CurrentMode == Player.Mode.Aboard)
                {
                    Pass("boarded from outside");
                    Next();
                }
                else if (_tick > 30)
                {
                    Fail("never boarded");
                    Next();
                }
                break;

            default:
                GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
                GetTree().Quit(_failures == 0 ? 0 : 1);
                break;
        }
    }

    static bool InCabin(Player player) => ShipLayout.Cabin.Grow(0.4f).HasPoint(player.ShipLocalPosition);

    void Next()
    {
        _stage++;
        _tick = 0;
    }

    void Check(bool ok, string what)
    {
        if (ok) Pass(what);
        else Fail(what);
    }

    static void Pass(string what) => GD.Print($"PASS  {what}");

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL  {what}");
    }
}
