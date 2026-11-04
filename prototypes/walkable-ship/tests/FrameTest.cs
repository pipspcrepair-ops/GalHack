using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WalkableShip;

/// <summary>
/// Headless test of the ship frame and cabin worlds. Run with tools/test.ps1, or:
///   godot --headless --path prototypes/walkable-ship res://tests/FrameTest.tscn
/// Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class FrameTest : Node
{
    const int StuntTicks = 900;   // 15 s at 60 Hz
    const int ZeroGTicks = 600;   // 10 s
    const int FlyTicks = 60;      // 1 s

    Main _main;
    int _tick, _stage, _failures;
    List<Vector3> _cargoStartA, _cargoStartB;
    (float height, float eye) _standing;
    StaticBody3D _overhang;

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100; // check after the ship and player have moved
        _main = new Main();
        AddChild(_main);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        Ship ship = _main.Ships[0], shipB = _main.Ships[1];
        Player player = _main.Player;

        switch (_stage)
        {
            case 0: // stand on the deck while the ship flies hard stunts
                if (_tick == 60)
                {
                    _cargoStartA = CargoPositions(ship);
                    _cargoStartB = CargoPositions(shipB);
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
                    CheckCargoApart(ship, shipB);
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
                    // Park ship A back beside ship B, passenger and all.
                    ship.Position = new Vector3(0, Ship.GroundClearance, 0);
                    ship.Basis = Basis.Identity;
                    player.DebugPlaceAboard(new Vector3(0, 0.05f, 13.2f), Vector3.Zero); // just past the door
                    Next();
                }
                else if (_tick > 30)
                {
                    Fail("never boarded");
                    Next();
                }
                break;

            case 4: // step off parked ship A into the main world, then go to ship B's door
                if (player.CurrentMode == Player.Mode.Outside)
                {
                    Check(player.GetParent() == _main && player.GetWorld3D() == _main.GetWorld3D(),
                        "stepped off ship A into the main world");
                    player.DebugPlaceOutside(shipB.GlobalTransform * new Vector3(0, 0.05f, 12.4f)); // on the door sill
                    player.DebugSetLook(0f, 0f); // facing into ship B
                    Press(Key.W, true);
                    Next();
                }
                else if (_tick > 30)
                {
                    Fail("never stepped off ship A");
                    Next();
                }
                break;

            case 5: // walk in through ship B's door
                if (player.CurrentMode == Player.Mode.Aboard)
                {
                    Press(Key.W, false);
                    Check(player.Ship == shipB && player.GetParent() == shipB.CabinWorld
                        && player.GetWorld3D() == shipB.CabinWorld.FindWorld3D()
                        && player.GetWorld3D() != ship.CabinWorld.FindWorld3D(),
                        "walked into ship B and boarded it, into ship B's own cabin world");
                    player.DebugPlaceAboard(ShipLayout.SeatExitLocal, Vector3.Zero);
                    Press(Key.F, true); // sit down, through the real input path
                    Press(Key.F, false);
                    Next();
                }
                else if (_tick > 120)
                {
                    Press(Key.W, false);
                    Fail($"never walked into ship B (at {player.GlobalPosition})");
                    Next();
                }
                break;

            case 6: // sit in ship B's seat and fly it forward
                if (_tick == 2)
                {
                    Check(player.CurrentMode == Player.Mode.Piloting && shipB.Piloted && !ship.Piloted,
                        "sat in ship B's pilot seat with the F key");
                    Press(Key.W, true);
                }
                else if (_tick == 2 + FlyTicks)
                {
                    Press(Key.W, false);
                    Check(shipB.Speed > 10f && ship.Speed == 0f,
                        $"flew ship B ({shipB.Speed:F0} m/s) while ship A stayed parked");
                    Press(Key.Ctrl, true);
                    Next();
                }
                break;

            case 7: // Ctrl is "down" in the seat; on foot it crouches, and a low overhang keeps you down
                if (_tick == 2)
                {
                    Check(shipB.ThrustInput.Y < 0f && !player.Crouching, "Ctrl still means down while piloting");
                    Press(Key.Ctrl, false);
                    player.DebugPlaceAboard(new Vector3(3f, 0.05f, -7.5f), Vector3.Zero); // a clear patch of deck
                }
                else if (_tick == 20)
                {
                    _standing = (Capsule(player).Height, EyeHeight(player));
                    Press(Key.Ctrl, true);
                }
                else if (_tick == 40)
                {
                    (float height, float eye) = (Capsule(player).Height, EyeHeight(player));
                    Check(player.Crouching && height < _standing.height - 0.5f && eye < _standing.eye - 0.5f,
                        $"crouching shrinks the capsule ({_standing.height:F2} -> {height:F2} m) and lowers the eye ({_standing.eye:F2} -> {eye:F2} m)");
                    _overhang = Overhang(new Vector3(3f, 1.45f, -7.5f)); // underside at 1.35 m
                    shipB.CabinWorld.AddChild(_overhang);
                    Press(Key.Ctrl, false);
                }
                else if (_tick == 60)
                {
                    Check(player.Crouching && Capsule(player).Height < _standing.height - 0.5f,
                        "can't stand up under a low overhang");
                    _overhang.QueueFree();
                }
                else if (_tick == 80)
                {
                    Check(!player.Crouching && Mathf.IsEqualApprox(Capsule(player).Height, _standing.height),
                        "stands up again once there is headroom");
                    Next();
                }
                break;

            default:
                GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
                GetTree().Quit(_failures == 0 ? 0 : 1);
                break;
        }
    }

    /// <summary>
    /// Both ships' crates start at the same ship-local spots, so if they shared a world they
    /// would collide. Ship A's cargo has been floating about in zero-g; ship B's must not budge.
    /// </summary>
    void CheckCargoApart(Ship shipA, Ship shipB)
    {
        RigidBody3D a = shipA.Crates.First(), b = shipB.Crates.First();
        Check(a.GetWorld3D().Space != b.GetWorld3D().Space && a.GetWorld3D() != _main.GetWorld3D(),
            "ship A's and ship B's cargo are simulated in separate worlds");
        float movedA = Moved(_cargoStartA, CargoPositions(shipA)), movedB = Moved(_cargoStartB, CargoPositions(shipB));
        Check(movedA > 0.5f && movedB < 0.01f,
            $"ship B's cargo never felt ship A's (A's moved up to {movedA:F2} m, B's {movedB:F3} m)");
    }

    static List<Vector3> CargoPositions(Ship ship) => ship.Crates.Select(c => c.Position).ToList();

    static float Moved(List<Vector3> from, List<Vector3> to) => from.Zip(to, (a, b) => a.DistanceTo(b)).Max();

    static bool InCabin(Player player) => ShipLayout.Cabin.Grow(0.4f).HasPoint(player.ShipLocalPosition);

    static CapsuleShape3D Capsule(Player player) => (CapsuleShape3D)player.GetNode<CollisionShape3D>("Capsule").Shape;

    /// <summary>The camera's height above the deck of the player's ship.</summary>
    static float EyeHeight(Player player) => (player.Ship.GlobalTransform.AffineInverse() * player.Camera.GlobalPosition).Y;

    /// <summary>A low slab in a cabin world: crouched you fit under it, standing you don't.</summary>
    static StaticBody3D Overhang(Vector3 center)
    {
        var slab = new StaticBody3D { Name = "Overhang", CollisionLayer = Layers.Interior, CollisionMask = 0, Position = center };
        slab.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2f, 0.2f, 2f) } });
        return slab;
    }

    /// <summary>Holds or releases a key, as if from the keyboard.</summary>
    static void Press(Key key, bool pressed) =>
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = pressed });

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
