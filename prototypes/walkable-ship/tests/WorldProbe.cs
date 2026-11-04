using Godot;

namespace WalkableShip;

/// <summary>
/// Headless probe of the engine feature cabin worlds rely on: a SubViewport with its own
/// World3D and rendering disabled still simulates physics, and its bodies never touch bodies
/// in any other world. Run with:
///   .\tools\test.ps1 -Scene res://tests/WorldProbe.tscn
/// Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class WorldProbe : Node
{
    const int FallTicks = 60;     // 1 s at 60 Hz
    const int SwitchTicks = 30;

    SubViewport _worldA, _worldB;
    RigidBody3D _crateMain, _crateA, _crateB;
    CharacterBody3D _walker;
    int _tick, _stage, _failures;

    public override void _Ready()
    {
        ulong before = OS.GetStaticMemoryUsage();

        // The main world: a floor and a crate.
        var main = new Node3D { Name = "MainWorld" };
        AddChild(main);
        main.AddChild(Floor());
        _crateMain = Crate(main);

        // World A: a floor, a crate in exactly the same spot, and a character beside it.
        _worldA = HiddenWorld("WorldA");
        _worldA.AddChild(Floor());
        _crateA = Crate(_worldA);
        _walker = new CharacterBody3D { Name = "Walker" };
        _walker.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
        });
        _worldA.AddChild(_walker);
        _walker.Position = new Vector3(2f, 2f, 0f);

        // World B: the same crate again, with nothing under it.
        _worldB = HiddenWorld("WorldB");
        _crateB = Crate(_worldB);

        GD.Print($"INFO  two hidden worlds cost {(OS.GetStaticMemoryUsage() - before) / 2 / 1048576.0:F1} MB each");
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        _walker.Velocity += Vector3.Down * 9.8f * (float)delta;
        _walker.MoveAndSlide();

        switch (_stage)
        {
            case 0: // everything falls for 1 s
                if (_tick < FallTicks) break;
                Check(Mathf.Abs(_crateA.Position.Y - 0.5f) < 0.05f,
                    $"a crate in a hidden world falls and lands on its floor (y {_crateA.Position.Y:F2} m)");
                Check(_walker.IsOnFloor() && _walker.Position.Y < 0.05f,
                    $"a character in a hidden world falls and lands on its floor (y {_walker.Position.Y:F2} m)");
                Check(Flat(_crateMain) < 0.01f && Flat(_crateA) < 0.01f && Mathf.Abs(_crateMain.Position.Y - 0.5f) < 0.05f,
                    "crates in the same spot in the main world and a hidden world never touch");
                Check(_crateB.Position.Y < -1f,
                    $"a crate in another hidden world falls through both floors (y {_crateB.Position.Y:F2} m)");
                MoveWalker(_worldB, new Vector3(2f, 0.05f, 0f));
                Next();
                break;

            case 1: // moved into world B, which has no floor
                if (_tick < SwitchTicks) break;
                Check(_walker.Position.Y < -0.5f,
                    $"a character moved into another world leaves the old floor behind (y {_walker.Position.Y:F2} m)");
                MoveWalker(_worldA, new Vector3(2f, 1f, 0f));
                Next();
                break;

            case 2: // and back into world A
                if (_tick < SwitchTicks) break;
                Check(_walker.IsOnFloor() && _walker.Position.Y < 0.05f,
                    $"a character moved back lands on the first world's floor (y {_walker.Position.Y:F2} m)");
                Next();
                break;

            default:
                GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
                GetTree().Quit(_failures == 0 ? 0 : 1);
                break;
        }
    }

    /// <summary>A world of its own that is never drawn, like a ship's cabin world.</summary>
    SubViewport HiddenWorld(string name)
    {
        var world = new SubViewport
        {
            Name = name,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            Size = new Vector2I(2, 2),
        };
        AddChild(world);
        return world;
    }

    static StaticBody3D Floor()
    {
        var floor = new StaticBody3D { Name = "Floor" };
        floor.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(20f, 1f, 20f) },
            Position = new Vector3(0, -0.5f, 0),
        });
        return floor;
    }

    static RigidBody3D Crate(Node parent)
    {
        var crate = new RigidBody3D { Name = "Crate", Mass = 20f };
        crate.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One } });
        parent.AddChild(crate);
        crate.Position = new Vector3(0f, 3f, 0f);
        return crate;
    }

    /// <summary>How far a crate has been pushed sideways from where it started.</summary>
    static float Flat(Node3D crate) => new Vector2(crate.Position.X, crate.Position.Z).Length();

    void MoveWalker(Node world, Vector3 position)
    {
        _walker.Reparent(world, false);
        _walker.Position = position;
        _walker.Velocity = Vector3.Zero;
        _walker.ResetPhysicsInterpolation();
    }

    void Next()
    {
        _stage++;
        _tick = 0;
    }

    void Check(bool ok, string what)
    {
        if (ok) GD.Print($"PASS  {what}");
        else
        {
            _failures++;
            GD.PrintErr($"FAIL  {what}");
        }
    }
}
